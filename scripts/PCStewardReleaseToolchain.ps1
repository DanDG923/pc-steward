# Codex security scan finding 1 (2026-11-12; FR-UPD-15, TM-03/13): the toolchain a release payload is built with is pinned in
# scripts/release-toolchain-pins.json and checked before any release project builds. Dot-sourced by
# Test-OwnerOnlyCandidateBuilderCI.ps1 (PowerShell 7, GitHub-hosted runner only). Pure functions plus three that touch disk:
#   - Install-PCStewardPinnedSdk downloads Microsoft's official SDK zip from the pinned URL, checks its size and SHA-512 and
#     extracts it into a new folder that holds only that SDK, so global.json's latestPatch cannot pick another one;
#   - Get-PCStewardRestoredPackages reads the restore records (obj\project.assets.json) of the three release projects: every
#     package library and every download-only pack (the Native AOT runtime and ILCompiler packs are download-only);
#   - Test-PCStewardRestoredPackages checks that set against the pins (same graph, nothing more, nothing less), each cached
#     .nupkg against its pinned SHA-512, and every extracted file against that .nupkg's own entry, and that the cache holds
#     nothing else.
# The pins say which bytes a release may be built from; the commit that carries them is what the owner's signing script
# binds (New-PCStewardRelease.ps1 checks the record's pin-file digest against its own clean clone).

$script:ReleaseToolchainPinsSchema = 'pc-steward-release-toolchain-pins:1'
$script:ReleaseToolchainPinsLeaf = 'release-toolchain-pins.json'
$script:ReleaseToolchainProjects = [string[]]@(
    'src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj',
    'src/PCSteward.Desktop/PCSteward.Desktop.csproj',
    'src/PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj'
)

function Get-PCStewardToolchainSha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}

function Get-PCStewardToolchainFileSha512Base64([string]$Path) {
    $sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-PCStewardToolchainFileSha512Hex([string]$Path) {
    $sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-PCStewardToolchainStreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Stream)).Replace('-', '') } finally { $sha.Dispose(); $Stream.Dispose() }
}

# Pure over the pin text and global.json's SDK version: the parsed, validated pins with a lookup by "id/version" (lower case).
function ConvertFrom-PCStewardReleaseToolchainPins {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][string]$GlobalJsonSdkVersion)
    $document = $Text | ConvertFrom-Json
    if ($document.schema -cne $script:ReleaseToolchainPinsSchema) { throw 'release-toolchain-pins.json has an unknown schema.' }
    $version = [string]$document.sdkVersion
    if ($version -cnotmatch '\A\d+\.\d+\.\d+\z') { throw 'release-toolchain-pins.json has no release SDK version.' }
    if ($version -cne $GlobalJsonSdkVersion) { throw "release-toolchain-pins.json pins SDK $version, but global.json names $GlobalJsonSdkVersion." }
    $zip = $document.sdkZip
    $expectedUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip"
    if ([string]$zip.url -cne $expectedUrl) { throw "The pinned SDK zip must be Microsoft's official $expectedUrl." }
    if ([string]$zip.sha512 -cnotmatch '\A[0-9a-f]{128}\z') { throw 'The pinned SDK zip has no SHA-512.' }
    if ([long]$zip.bytes -lt 1MB -or [long]$zip.bytes -gt 1GB) { throw 'The pinned SDK zip size is outside its bound.' }
    $map = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($package in @($document.packages)) {
        if (!$package) { continue }
        $id = [string]$package.id; $packageVersion = [string]$package.version
        if ($id -cnotmatch '\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}\z' -or $packageVersion -cnotmatch '\A\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?\z') { throw "Malformed pinned package: $id $packageVersion" }
        if (@('library', 'download', 'resolution') -cnotcontains [string]$package.kind) { throw "Pinned package $id has an unknown kind." }
        if ([string]$package.sha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==\z') { throw "Pinned package $id has no SHA-512." }
        $key = $id.ToLowerInvariant() + '/' + $packageVersion.ToLowerInvariant()
        if ($map.ContainsKey($key)) { throw "Package $key is pinned twice." }
        $map.Add($key, $package)
    }
    [pscustomobject]@{ Document = $document; SdkVersion = $version; SdkZip = $zip; Packages = $map }
}

# Reads scripts\release-toolchain-pins.json and global.json of a checkout. Sha256 is over the file's exact bytes (the file is
# -text in .gitattributes, so every checkout has the same bytes).
function Read-PCStewardReleaseToolchainPins {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $path = [IO.Path]::Combine($RepositoryRoot, 'scripts', $script:ReleaseToolchainPinsLeaf)
    $bytes = [IO.File]::ReadAllBytes($path)
    $sdk = [string]([IO.File]::ReadAllText([IO.Path]::Combine($RepositoryRoot, 'global.json')) | ConvertFrom-Json).sdk.version
    $pins = ConvertFrom-PCStewardReleaseToolchainPins -Text ([Text.UTF8Encoding]::new($false, $true).GetString($bytes)) -GlobalJsonSdkVersion $sdk
    $pins | Add-Member -NotePropertyName Sha256 -NotePropertyValue (Get-PCStewardToolchainSha256Hex $bytes)
    $pins
}

# Downloads and checks the pinned SDK zip, then extracts it into Folder\dotnet (Folder must be new and empty). Returns the
# dotnet root. dotnet --version is asked outside any global.json and must be exactly the pinned version.
function Install-PCStewardPinnedSdk {
    param([Parameter(Mandatory)]$Pins, [Parameter(Mandatory)][string]$Folder)
    $zipPath = Join-Path $Folder 'dotnet-sdk.zip'
    $previous = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    try { Invoke-WebRequest -Uri ([string]$Pins.SdkZip.url) -OutFile $zipPath -UseBasicParsing } finally { $ProgressPreference = $previous }
    $length = (Get-Item -LiteralPath $zipPath).Length
    if ($length -ne [long]$Pins.SdkZip.bytes) { throw "The downloaded SDK zip is $length bytes, not the pinned $($Pins.SdkZip.bytes)." }
    $hash = Get-PCStewardToolchainFileSha512Hex $zipPath
    if ($hash -cne [string]$Pins.SdkZip.sha512) { throw 'The downloaded SDK zip does not match its pinned SHA-512.' }
    $root = Join-Path $Folder 'dotnet'
    if (Test-Path -LiteralPath $root) { throw 'The SDK folder must not exist yet.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $root)
    Remove-Item -LiteralPath $zipPath -Force
    $dotnet = Join-Path $root 'dotnet.exe'
    $sdks = @(Get-ChildItem -LiteralPath (Join-Path $root 'sdk') -Directory | ForEach-Object { $_.Name })
    if ($sdks.Count -ne 1 -or $sdks[0] -cne $Pins.SdkVersion) { throw "The SDK folder holds $($sdks -join ', '), not only $($Pins.SdkVersion)." }
    Push-Location -LiteralPath $Folder
    try { $actual = ([string](& $dotnet --version | Select-Object -Last 1)).Trim() } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0 -or $actual -cne $Pins.SdkVersion) { throw "The pinned dotnet reports '$actual', not $($Pins.SdkVersion)." }
    $root
}

# Pure over the restore records: every package the three release projects use ("id/version" lower case -> Id, Version, Kind)
# and every record problem (another package folder, a fallback folder, a library path that is not its own name).
function Get-PCStewardRestoredPackages {
    param([Parameter(Mandatory)][string[]]$AssetsPaths, [Parameter(Mandatory)][string]$PackagesRoot)
    $used = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::Ordinal)
    $problems = [Collections.Generic.List[string]]::new()
    $root = $PackagesRoot.TrimEnd('\', '/')
    foreach ($assetsPath in $AssetsPaths) {
        $assets = [IO.File]::ReadAllText($assetsPath) | ConvertFrom-Json
        $folders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name.TrimEnd('\', '/') })
        if ($folders.Count -ne 1 -or !$folders[0].Equals($root, [StringComparison]::OrdinalIgnoreCase)) { $problems.Add("$assetsPath restores from another or an extra package folder: $($folders -join ' | ')") }
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -cne 'package') { continue }
            if (([string]$library.Value.path) -cne $library.Name.ToLowerInvariant()) { $problems.Add("library path differs: $($library.Name)") }
            $slash = $library.Name.IndexOf('/')
            $key = $library.Name.ToLowerInvariant()
            $used[$key] = [pscustomobject]@{ Id = $library.Name.Substring(0, $slash); Version = $library.Name.Substring($slash + 1); Kind = 'library' }
        }
        foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
            foreach ($download in @($framework.Value.downloadDependencies)) {
                if (!$download) { continue }
                $version = ([string]$download.version).Trim('[', ']', ' ').Split(',')[0].Trim()
                $key = ([string]$download.name).ToLowerInvariant() + '/' + $version.ToLowerInvariant()
                if (!$used.ContainsKey($key)) { $used[$key] = [pscustomobject]@{ Id = [string]$download.name; Version = $version; Kind = 'download' } }
            }
        }
    }
    [pscustomobject]@{ Used = $used; Problems = $problems }
}

# Files below Root as relative paths (case-insensitive), plus every reparse point found on the way (never followed).
function Get-PCStewardToolchainTree([string]$Root) {
    $files = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $reparse = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Root)
    $prefix = $Root.TrimEnd('\').Length + 1
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in [IO.DirectoryInfo]::new($directory).EnumerateFileSystemInfos()) {
            $relative = $item.FullName.Substring($prefix)
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse.Add($relative); continue }
            if ($item.Attributes -band [IO.FileAttributes]::Directory) { $pending.Push($item.FullName) } else { $files[$relative] = $item.FullName }
        }
    }
    [pscustomobject]@{ Files = $files; Reparse = $reparse }
}

# The packages in the cache: the used ones, plus any other id/version folder (kind "resolution": fetched by NuGet only to
# walk the graph). Anything that is not a plain id\version folder is returned as unexpected.
function Get-PCStewardCachedPackages {
    param([Parameter(Mandatory)]$Used, [Parameter(Mandatory)][string]$PackagesRoot)
    $packages = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::Ordinal)
    $unexpected = [Collections.Generic.List[string]]::new()
    foreach ($key in $Used.Keys) { $packages[$key] = $Used[$key] }
    foreach ($item in [IO.DirectoryInfo]::new($PackagesRoot).EnumerateFileSystemInfos()) {
        if (!($item.Attributes -band [IO.FileAttributes]::Directory) -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { $unexpected.Add($item.Name); continue }
        foreach ($versionItem in $item.EnumerateFileSystemInfos()) {
            $key = $item.Name.ToLowerInvariant() + '/' + $versionItem.Name.ToLowerInvariant()
            if (!($versionItem.Attributes -band [IO.FileAttributes]::Directory) -or ($versionItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { $unexpected.Add($key); continue }
            if (!$packages.ContainsKey($key)) {
                # The nuspec carries the id's own casing; the folder name is lower case.
                $nuspec = [IO.Path]::Combine($versionItem.FullName, $item.Name.ToLowerInvariant() + '.nuspec')
                $id = $item.Name
                if (Test-Path -LiteralPath $nuspec -PathType Leaf) { try { $id = [string]([xml][IO.File]::ReadAllText($nuspec)).package.metadata.id } catch { $id = $item.Name } }
                if (!$id -or !$id.Equals($item.Name, [StringComparison]::OrdinalIgnoreCase)) { $id = $item.Name }
                $packages[$key] = [pscustomobject]@{ Id = $id; Version = $versionItem.Name; Kind = 'resolution' }
            }
        }
    }
    [pscustomobject]@{ Packages = $packages; Unexpected = $unexpected }
}

# Checks the package cache against the pins. Returns the list of problems (empty: every used package is pinned with the same
# bytes, every pin is in the cache, every extracted file equals its .nupkg entry, and the cache holds nothing unpinned).
function Test-PCStewardRestoredPackages {
    param([Parameter(Mandatory)]$Used, [Parameter(Mandatory)]$Pins, [Parameter(Mandatory)][string]$PackagesRoot)
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $problems = [Collections.Generic.List[string]]::new()
    $root = $PackagesRoot.TrimEnd('\', '/')
    foreach ($key in $Used.Keys) { if (!$Pins.Packages.ContainsKey($key)) { $problems.Add("unpinned package $key") } }
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $problems.Add('the package cache is a reparse point'); return $problems }
    # Nothing in the cache but the used packages and the pinned ones NuGet fetched only to resolve the graph (an older
    # version it then lifted), each in its own folder. Both are checked byte for byte below.
    $present = Get-PCStewardCachedPackages -Used $Used -PackagesRoot $root
    foreach ($bad in $present.Unexpected) { $problems.Add("unexpected cache entry $bad") }
    foreach ($key in $present.Packages.Keys) { if (!$Pins.Packages.ContainsKey($key) -and !$Used.ContainsKey($key)) { $problems.Add("unpinned cache entry $key") } }
    foreach ($key in $Pins.Packages.Keys) { if (!$present.Packages.ContainsKey($key)) { $problems.Add("pinned package not restored $key") } }
    # NuGet extracts every entry except the OPC parts (it keeps .signature.p7s) and writes the nuspec under the lower-case id.
    $opcSkip = '\A(\[Content_Types\]\.xml|_rels/.*|package/.*)\z'
    foreach ($key in $present.Packages.Keys) {
        if (!$Pins.Packages.ContainsKey($key)) { continue }
        $u = $present.Packages[$key]; $id = $u.Id.ToLowerInvariant(); $version = $u.Version.ToLowerInvariant()
        $folder = [IO.Path]::Combine($root, $id, $version)
        $nupkgName = "$id.$version.nupkg"; $nupkg = Join-Path $folder $nupkgName
        $pin = [string]$Pins.Packages[$key].sha512
        if (!(Test-Path -LiteralPath $nupkg -PathType Leaf)) { $problems.Add("missing $key"); continue }
        if ((Get-PCStewardToolchainFileSha512Base64 $nupkg) -cne $pin) { $problems.Add("nupkg differs from its pin $key"); continue }
        $shaFile = Join-Path $folder ($nupkgName + '.sha512')
        if (!(Test-Path -LiteralPath $shaFile -PathType Leaf) -or [IO.File]::ReadAllText($shaFile).Trim() -cne $pin) { $problems.Add("sha512 record differs $key") }
        $tree = Get-PCStewardToolchainTree $folder
        foreach ($r in $tree.Reparse) { $problems.Add("reparse point $key $r") }
        $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
        $archive = [IO.Compression.ZipFile]::OpenRead($nupkg)
        try {
            foreach ($entry in $archive.Entries) {
                if (!$entry.Name -or $entry.FullName -match $opcSkip) { continue }
                $relative = if ($entry.FullName.IndexOf('/') -lt 0 -and $entry.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)) { "$id.nuspec" } else { [Uri]::UnescapeDataString($entry.FullName).Replace('/', '\') }
                $expected[$relative] = $entry
            }
            foreach ($relative in $expected.Keys) {
                if (!$tree.Files.ContainsKey($relative)) { $problems.Add("missing file $key $relative"); continue }
                $entry = $expected[$relative]; $path = $tree.Files[$relative]
                if ((Get-Item -LiteralPath $path -Force).Length -ne $entry.Length -or (Get-PCStewardToolchainStreamSha256 $entry.Open()) -cne (Get-PCStewardToolchainStreamSha256 ([IO.File]::OpenRead($path)))) { $problems.Add("changed file $key $relative") }
            }
        } finally { $archive.Dispose() }
        foreach ($relative in $tree.Files.Keys) {
            if (!$expected.ContainsKey($relative) -and @($nupkgName, ($nupkgName + '.sha512'), '.nupkg.metadata') -notcontains $relative) { $problems.Add("extra file $key $relative") }
        }
    }
    $problems
}

# The pin file a fresh restore would need (printed by CI when the committed pins differ, for review and commit).
function ConvertTo-PCStewardReleaseToolchainPinsJson {
    param([Parameter(Mandatory)]$Pins, [Parameter(Mandatory)]$Used, [Parameter(Mandatory)][string]$PackagesRoot)
    $present = Get-PCStewardCachedPackages -Used $Used -PackagesRoot $PackagesRoot.TrimEnd('\', '/')
    $packages = @(foreach ($key in $present.Packages.Keys) {
        $u = $present.Packages[$key]; $id = $u.Id.ToLowerInvariant(); $version = $u.Version.ToLowerInvariant()
        $nupkg = [IO.Path]::Combine($PackagesRoot, $id, $version, "$id.$version.nupkg")
        [ordered]@{ id = $u.Id; version = $u.Version; kind = $u.Kind; sha512 = $(if (Test-Path -LiteralPath $nupkg -PathType Leaf) { Get-PCStewardToolchainFileSha512Base64 $nupkg } else { 'missing' }) }
    })
    $document = [ordered]@{
        schema = $script:ReleaseToolchainPinsSchema
        sdkVersion = $Pins.SdkVersion
        sdkZip = [ordered]@{ url = [string]$Pins.SdkZip.url; sha512 = [string]$Pins.SdkZip.sha512; bytes = [long]$Pins.SdkZip.bytes }
        projects = $script:ReleaseToolchainProjects
        packages = $packages
    }
    (($document | ConvertTo-Json -Depth 5) -replace "`r`n", "`n") + "`n"
}
