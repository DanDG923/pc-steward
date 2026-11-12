[CmdletBinding()]
param(
    # The green release-payload workflow run to release (the number in the run's address).
    [Parameter(Mandatory)][long]$RunId,
    # The full commit the owner expects that run to have built. The script releases nothing else.
    [Parameter(Mandatory)][string]$Commit,
    # A new folder for the downloaded payload and the signed release.
    [Parameter(Mandatory)][string]$WorkRoot
)

# ADR-032 revision 2 §12 step 4 (owner decision 2026-11-12, "one paste does everything"): publish a release from the
# owner's PC without editing it in the browser. Run it in Windows PowerShell 5.1 as the owner, not elevated, from a clean
# clone at -Commit. With a fine-grained GitHub token limited to this repository (Contents: read and write, Actions: read),
# kept encrypted for the owner's Windows account (DPAPI) under %LOCALAPPDATA%\PCSteward-Release:
#   1. GitHub: the run is a green, hand-started release-payload run on phase/4-beta or main at -Commit, and the draft
#      release v<msiVersion> that publish-release.yml made from it is still a draft at -Commit holding only that run's
#      check file (an MSI left by an earlier attempt of this script is removed).
#   2. The run's payload artefact is downloaded and must have the SHA-256 GitHub records for it.
#   3. New-PCStewardRelease.ps1 signs it with the YubiKey (PIN and a touch per signature) and checks it as before.
#   4. The signed MSI is uploaded to the draft, GitHub's digest of the upload must equal the local file, and the draft is
#      published. The published release must be immutable, at -Commit, with exactly the check file and the MSI.
# The token can only manage this repository's contents and read its workflow runs: it cannot sign, so PC Steward refuses
# anything it uploads that the release key did not sign and CI did not build. It is never printed or written in plain text.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 (powershell.exe).' }
foreach ($module in @('Management', 'Utility', 'Security')) {
    $manifest = [IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")
    if (Test-Path -LiteralPath $manifest) { Import-Module $manifest -Force }
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from a normal (not elevated) PowerShell window.' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repository = 'DanDG923/pc-steward'
$api = "https://api.github.com/repos/$repository"
$uploads = "https://uploads.github.com/repos/$repository"
$workflowPath = '.github/workflows/release-payload.yml'
$payloadArtifactName = 'pc-steward-release-payload'
$checkArtifactName = 'pc-steward-release-check'
$checkAssetName = 'pc-steward-release-check.zip'
$allowedBranches = @('phase/4-beta', 'main')
$maxPayloadBytes = 1GB
$tokenFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward-Release'
$tokenFile = Join-Path $tokenFolder 'github-token.dat'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ($Commit -notmatch '\A[0-9a-f]{40}\z') { throw 'Commit must be a full 40-character commit id in lower case.' }
if ($RunId -le 0) { throw 'RunId must be a positive number.' }
$work = [IO.Path]::GetFullPath($WorkRoot)
if (Test-Path -LiteralPath $work) { throw "WorkRoot already exists: $work" }
$localCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $localCommit -cne $Commit) { throw "Check out commit $Commit in $repositoryRoot first (this clone is at $localCommit)." }

function Get-PCStewardFileSha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
}

# The token: read from the encrypted file, or asked for once and saved. Only a fine-grained token can be limited to one
# repository, so a classic token is refused.
function Read-PCStewardReleaseToken {
    if (Test-Path -LiteralPath $tokenFile -PathType Leaf) {
        return (Get-Content -LiteralPath $tokenFile -Raw).Trim() | ConvertTo-SecureString
    }
    Write-Host 'No GitHub release token is saved for this Windows account yet.'
    $secure = Read-Host -AsSecureString 'Paste the fine-grained GitHub token (github_pat_...) and press Enter'
    $plain = ConvertFrom-PCStewardSecureToken $secure
    try { if ($plain -cnotmatch '\Agithub_pat_[A-Za-z0-9_]{20,255}\z') { throw 'That is not a fine-grained GitHub token (it must start with github_pat_). Classic tokens cannot be limited to one repository.' } }
    finally { $plain = $null }
    $script:saveTokenAfterCheck = $true
    return $secure
}
function ConvertFrom-PCStewardSecureToken([Security.SecureString]$Secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Save-PCStewardReleaseToken([Security.SecureString]$Secure) {
    if (!(Test-Path -LiteralPath $tokenFolder)) { New-Item -ItemType Directory -Path $tokenFolder | Out-Null }
    # ConvertFrom-SecureString without a key encrypts with DPAPI for this Windows account on this PC.
    [IO.File]::WriteAllText($tokenFile, ($Secure | ConvertFrom-SecureString), [Text.UTF8Encoding]::new($false))
}

# Every authenticated call goes to this repository on api.github.com or uploads.github.com, nowhere else.
function Get-PCStewardApiHeaders {
    $plain = ConvertFrom-PCStewardSecureToken $script:token
    try { @{ Authorization = "Bearer $plain"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'pc-steward-release-script' } }
    finally { $plain = $null }
}
function Assert-PCStewardRepositoryUri([string]$Uri) {
    if (!$Uri.StartsWith("$api/", [StringComparison]::Ordinal) -and !$Uri.StartsWith("$uploads/", [StringComparison]::Ordinal)) { throw "Refusing to send the token to $Uri." }
}
function Invoke-PCStewardGitHub([string]$Method, [string]$Uri, $Body) {
    Assert-PCStewardRepositoryUri $Uri
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = (Get-PCStewardApiHeaders); MaximumRedirection = 0; UseBasicParsing = $true }
    if ($null -ne $Body) { $parameters.Body = ($Body | ConvertTo-Json -Compress); $parameters.ContentType = 'application/json' }
    Invoke-RestMethod @parameters
}
# An artefact or asset download answers with a redirect to GitHub's storage. The token goes only to the API request; the
# redirect is followed by hand without it, and the bytes are checked against GitHub's digest afterwards.
function Save-PCStewardGitHubDownload([string]$Uri, [string]$Path, [long]$MaxBytes) {
    Assert-PCStewardRepositoryUri $Uri
    $request = [Net.HttpWebRequest]::Create($Uri)
    $request.AllowAutoRedirect = $false; $request.UserAgent = 'pc-steward-release-script'; $request.Accept = 'application/vnd.github+json'
    $headers = Get-PCStewardApiHeaders
    $request.Headers.Add('Authorization', $headers.Authorization); $request.Headers.Add('X-GitHub-Api-Version', '2022-11-28')
    $headers = $null
    $response = $request.GetResponse()
    try {
        if ([int]$response.StatusCode -ne 302) { throw "GitHub answered $([int]$response.StatusCode) instead of a download redirect." }
        $location = [Uri]$response.Headers['Location']
    }
    finally { $response.Dispose() }
    if ($location.Scheme -cne 'https') { throw 'The download redirect is not HTTPS.' }
    $download = [Net.HttpWebRequest]::Create($location)
    $download.AllowAutoRedirect = $false; $download.UserAgent = 'pc-steward-release-script'
    $response = $download.GetResponse()
    try {
        if ([int]$response.StatusCode -ne 200) { throw "The download answered $([int]$response.StatusCode)." }
        $source = $response.GetResponseStream(); $target = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $buffer = New-Object byte[] 1048576; $total = 0L
            while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $total += $read
                if ($total -gt $MaxBytes) { throw 'The download is larger than any release payload.' }
                $target.Write($buffer, 0, $read)
            }
        }
        finally { $target.Dispose(); $source.Dispose() }
    }
    finally { $response.Dispose() }
}
function Get-PCStewardDraft([string]$Tag) {
    # Assigned first: Windows PowerShell 5.1 passes a JSON array down a pipeline as one object.
    $all = Invoke-PCStewardGitHub GET "$api/releases?per_page=100"
    $matching = @($all | Where-Object { $_.tag_name -ceq $Tag })
    if ($matching.Count -ne 1) { throw "There is no single release $Tag. Run publish-release.yml for run $RunId first." }
    $release = $matching[0]
    if ($release.draft -ne $true) { throw "Release $Tag is already published." }
    if ([string]$release.target_commitish -cne $Commit) { throw "Draft $Tag targets $($release.target_commitish), not $Commit." }
    if ([string]$release.name -cne "PC Steward $($Tag.Substring(1))") { throw "Draft $Tag is not named PC Steward $($Tag.Substring(1))." }
    return $release
}

$script:saveTokenAfterCheck = $false
$script:token = Read-PCStewardReleaseToken
try {
    # 1. The run, from GitHub. A first call with a wrong or expired token fails here, before anything is downloaded.
    Assert-PCStewardRepositoryUri "$api/actions/runs/$RunId"
    try { $runResponse = Invoke-WebRequest -Uri "$api/actions/runs/$RunId" -Headers (Get-PCStewardApiHeaders) -UseBasicParsing -MaximumRedirection 0 }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) { throw "GitHub refused the saved token (expired or revoked). Delete $tokenFile, make a new token and run again." }
        throw
    }
    $expires = [string]$runResponse.Headers['github-authentication-token-expiration']
    $run = $runResponse.Content | ConvertFrom-Json
    if ($run.repository.full_name -cne $repository -or $run.head_repository.full_name -cne $repository) { throw 'The run is not from this repository.' }
    if ($run.path -cne $workflowPath -or $run.event -cne 'workflow_dispatch') { throw "Run $RunId is not a hand-started release-payload run." }
    if ($run.status -cne 'completed' -or $run.conclusion -cne 'success') { throw "Run $RunId did not finish successfully." }
    if ($allowedBranches -cnotcontains [string]$run.head_branch) { throw "Run $RunId built $($run.head_branch), not $($allowedBranches -join ' or ')." }
    if ([string]$run.head_sha -cne $Commit) { throw "Run $RunId built $($run.head_sha), not the expected commit $Commit." }
    $artifacts = @((Invoke-PCStewardGitHub GET "$api/actions/runs/$RunId/artifacts").artifacts)
    $payloadArtifact = @($artifacts | Where-Object { $_.name -ceq $payloadArtifactName })
    $checkArtifact = @($artifacts | Where-Object { $_.name -ceq $checkArtifactName })
    if ($payloadArtifact.Count -ne 1 -or $payloadArtifact[0].expired -or $checkArtifact.Count -ne 1 -or $checkArtifact[0].expired) { throw "Run $RunId has no single, unexpired payload and check artefact." }
    if ([string]$payloadArtifact[0].digest -cnotmatch '\Asha256:[0-9a-f]{64}\z' -or [string]$checkArtifact[0].digest -cnotmatch '\Asha256:[0-9a-f]{64}\z') { throw 'GitHub publishes no SHA-256 digest for the artefacts.' }
    if ($script:saveTokenAfterCheck) { Save-PCStewardReleaseToken $script:token; Write-Output "Token saved for this Windows account in $tokenFile." }
    if ($expires) { Write-Output "GitHub token expires: $expires" }

    # 2. The payload, downloaded and checked against GitHub's digest before the version is read from it.
    New-Item -ItemType Directory -Path $work | Out-Null
    $zip = Join-Path $work "$payloadArtifactName.zip"
    Save-PCStewardGitHubDownload "$api/actions/artifacts/$($payloadArtifact[0].id)/zip" $zip $maxPayloadBytes
    $zipHash = Get-PCStewardFileSha256 $zip
    if ('sha256:' + $zipHash.ToLowerInvariant() -cne [string]$payloadArtifact[0].digest) { throw "The downloaded payload's SHA-256 $zipHash is not the one GitHub recorded." }
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entry = $archive.GetEntry('release-payload.json')
        if ($null -eq $entry) { throw 'The payload has no release-payload.json.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $record = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    if ([string]$record.sourceCommit -cne $Commit -or [string]$record.runId -cne [string]$RunId) { throw 'release-payload.json does not name this run and commit.' }
    $msiVersion = ([string]$record.productVersion -split '-', 2)[0]
    if ($msiVersion -cnotmatch '\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z') { throw 'The payload has no major.minor.patch version.' }
    $tag = "v$msiVersion"
    $msiName = "PCSteward-$msiVersion.msi"

    # The draft is checked before signing, so a missing or wrong draft costs no YubiKey touches.
    $draft = Get-PCStewardDraft $tag
    $unexpected = @($draft.assets | Where-Object { $_.name -cne $checkAssetName -and $_.name -cne $msiName })
    $check = @($draft.assets | Where-Object { $_.name -ceq $checkAssetName })
    if ($unexpected.Count -ne 0 -or $check.Count -ne 1 -or [string]$check[0].digest -cne [string]$checkArtifact[0].digest) { throw "Draft $tag does not hold exactly run $RunId's check file." }
    Write-Output "Run $RunId and draft $tag checked: PC Steward $($record.productVersion) at $Commit."

    # 3. Sign (and check) with the existing release script.
    $windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'
    $signed = Join-Path $work 'release'
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-PCStewardRelease.ps1') -PayloadZip $zip -RunId $RunId -OutputRoot $signed
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed (see the message above). Nothing was uploaded.' }
    $release = Get-Content -LiteralPath (Join-Path $signed 'release.json') -Raw | ConvertFrom-Json
    $msi = Join-Path $signed $msiName
    if ([string]$release.sourceCommit -cne $Commit -or [string]$release.runId -cne [string]$RunId -or [string]$release.artefactSha256 -cne $zipHash -or [string]$release.msiVersion -cne $msiVersion -or [string]$release.signedWith -cne 'primary') { throw 'release.json does not match this run. Nothing was uploaded.' }
    if (!(Test-Path -LiteralPath $msi -PathType Leaf) -or (Get-PCStewardFileSha256 $msi) -cne [string]$release.msiSha256) { throw 'The signed MSI is not the one release.json records. Nothing was uploaded.' }

    # 4. Upload to the draft (still a draft, still at this commit) and publish.
    $draft = Get-PCStewardDraft $tag
    foreach ($stale in @($draft.assets | Where-Object { $_.name -ceq $msiName })) {
        Invoke-PCStewardGitHub DELETE "$api/releases/assets/$($stale.id)" $null | Out-Null
        Write-Output "Removed $msiName left on draft $tag by an earlier attempt."
    }
    $uploadUri = "$uploads/releases/$($draft.id)/assets?name=$msiName"
    Assert-PCStewardRepositoryUri $uploadUri
    $uploaded = Invoke-RestMethod -Method POST -Uri $uploadUri -Headers (Get-PCStewardApiHeaders) -ContentType 'application/octet-stream' -InFile $msi -UseBasicParsing -MaximumRedirection 0
    $msiDigest = 'sha256:' + ([string]$release.msiSha256).ToLowerInvariant()
    if ($uploaded.name -cne $msiName -or $uploaded.state -cne 'uploaded' -or [long]$uploaded.size -ne (Get-Item -LiteralPath $msi).Length) { throw "GitHub did not take $msiName whole. The release is still a draft." }
    # GitHub's own SHA-256 of the uploaded file must be the signed MSI's before anything is published (it can take a few
    # seconds to appear).
    for ($attempt = 0; ; $attempt++) {
        $onDraft = @((Invoke-PCStewardGitHub GET "$api/releases/$($draft.id)").assets | Where-Object { $_.name -ceq $msiName })
        if ($onDraft.Count -eq 1 -and $onDraft[0].digest) { break }
        if ($attempt -ge 15) { throw "GitHub reports no SHA-256 for the uploaded $msiName. The release is still a draft." }
        Start-Sleep -Seconds 2
    }
    if ([string]$onDraft[0].digest -cne $msiDigest) { throw "GitHub's copy of $msiName is not the signed file. The release is still a draft." }
    $null = Invoke-PCStewardGitHub PATCH "$api/releases/$($draft.id)" @{ draft = $false; make_latest = 'true' }

    $published = Invoke-PCStewardGitHub GET "$api/releases/tags/$tag"
    $names = @($published.assets | ForEach-Object { [string]$_.name } | Sort-Object)
    $msiAsset = @($published.assets | Where-Object { $_.name -ceq $msiName })
    $checkAsset = @($published.assets | Where-Object { $_.name -ceq $checkAssetName })
    if ($published.draft -ne $false -or $names.Count -ne 2 -or $msiAsset.Count -ne 1 -or $checkAsset.Count -ne 1 -or [string]$msiAsset[0].digest -cne $msiDigest -or [string]$checkAsset[0].digest -cne [string]$checkArtifact[0].digest) { throw "Published release $tag does not hold exactly the check file and the signed MSI. Check it on GitHub." }
    $ref = Invoke-PCStewardGitHub GET "$api/git/ref/tags/$tag"
    if ($ref.object.type -cne 'commit' -or [string]$ref.object.sha -cne $Commit) { throw "Tag $tag does not point at $Commit. Check it on GitHub." }
    if ($published.immutable -ne $true) { throw "Release $tag was published but is not immutable. Turn on immutable releases in the repository settings." }
    Write-Output "PASS: published PC Steward $($record.productVersion) as $tag (immutable) at $Commit; MSI SHA-256 $($release.msiSha256); tables $($release.tablesSha256)."
}
finally {
    $script:token.Dispose()
    $script:token = $null
}
