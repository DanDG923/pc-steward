[CmdletBinding()]
param()

# Task 71 (ADR-029; docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md §10 item 2): build the one-request AI test build on a
# disposable GitHub-hosted runner. It is the only place that sets PCStewardAiFirstLiveTest=true, which compiles the Task 69
# candidate in as the one reviewed model and paid requests in first-live-test mode (the cumulative $1 envelope). The output is
# unsigned and is never a release: it has no MSI, no release check, no signing and no updater channel, and the release
# candidate builder refuses any payload that carries its mark. The test build itself refuses app updates, the scheduled check,
# factory reset and self-update, and says in its title that it is a test build.
#
# Steps: the AI tests with the property on, then the desktop build with the property on (framework-dependent .NET, the Windows
# App SDK carried in the folder as in the launch smoke), then checks that the mark is in PCSteward.Domain.dll and no elevated
# helper is in the folder, then a manifest of every file's SHA-256. Nothing is sent to a provider and no key is used.

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'The AI test build is built on a disposable GitHub-hosted runner only.' }

$repository = Split-Path -Parent $PSScriptRoot
$commit = [string](& git -C $repository rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $commit.Trim() -notmatch '\A[0-9a-f]{40}\z') { throw 'The source commit could not be read.' }
$commit = $commit.Trim()
$status = @(& git -C $repository status --porcelain)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw 'The source tree is not clean, so the build would not be the commit.' }

$out = Join-Path $repository 'artifacts\ai-first-live-test'
$app = Join-Path $out 'app'
if (Test-Path -LiteralPath $out) { throw "The output folder already exists: $out" }
New-Item -ItemType Directory -Path $app -Force | Out-Null

$property = '-p:PCStewardAiFirstLiveTest=true'
$scheduledHost = Join-Path $repository 'src\PCSteward.ScheduledHost\PCSteward.ScheduledHost.csproj'
$desktop = Join-Path $repository 'src\PCSteward.Desktop\PCSteward.Desktop.csproj'
$domainTests = Join-Path $repository 'tests\PCSteward.Domain.UnitTests\PCSteward.Domain.UnitTests.csproj'
$applicationTests = Join-Path $repository 'tests\PCSteward.Application.IntegrationTests\PCSteward.Application.IntegrationTests.csproj'
# The output ends in a separator: without one, the WinUI resource copy wrote App.xbf and Resources\*.xbf a second time beside the
# folder, as appApp.xbf and appResources\ (Task 71 review F1). The count check below fails the build if anything lands outside it.
$steps = @(
    # The AI suites compiled as the test build: they check that only the candidate is reviewed, only for the first live test.
    @('test', $domainTests, '--configuration', 'Release', $property, '--filter', 'FullyQualifiedName~AdvisoryAi'),
    @('test', $applicationTests, '--configuration', 'Release', $property, '--filter', 'FullyQualifiedName~AdvisoryAi'),
    @('restore', $scheduledHost, '--runtime', 'win-x64', $property),
    @('restore', $desktop, '--runtime', 'win-x64', '-p:WindowsAppSDKSelfContained=true', $property),
    @('build', $scheduledHost, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore', $property),
    @('build', $desktop, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore', '-p:WindowsAppSDKSelfContained=true', $property, '--output', ($app + '\'))
)
foreach ($step in $steps) {
    & dotnet @step
    if ($LASTEXITCODE -ne 0) { throw ('dotnet ' + $step[0] + ' failed with exit code ' + $LASTEXITCODE) }
}

foreach ($required in @('PCSteward.Desktop.exe', 'PCSteward.Domain.dll', 'PCSteward.AdvisoryAi.Windows.dll', 'OpenAI.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $app $required) -PathType Leaf)) { throw "The test build is missing $required." }
}
# The mark is a metadata name (NUL-terminated in the #Strings heap) that only PCSTEWARD_AI_FIRST_LIVE_TEST compiles in.
$mark = 'AdvisoryAiFirstLiveTestBuildMark'
$domainText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $app 'PCSteward.Domain.dll')))
if ($domainText.IndexOf($mark + [char]0, [StringComparison]::Ordinal) -lt 0) { throw 'PCSteward.Domain.dll was not built as the AI test build.' }
# No elevated helper: the test build runs from a folder the user can write to, so it must never ask Windows for permission.
foreach ($leaf in @('PCSteward.ElevatedHelper.exe', 'PCSteward.ElevatedHelper.dll')) {
    if (Test-Path -LiteralPath (Join-Path $app $leaf)) { throw "The test build must not contain $leaf." }
}

# Ordinal path order, so the manifest is the same for the same files on any runner.
$entries = [Collections.Generic.List[object]]::new()
# -Force: hidden and system files are listed too, so the manifest covers every file the upload carries (Task 71 review F1).
foreach ($file in Get-ChildItem -LiteralPath $app -Recurse -File -Force) {
    $entries.Add([ordered]@{
        path = $file.FullName.Substring($app.Length + 1).Replace('\', '/')
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    })
}
$entries.Sort([Comparison[object]] { param($left, $right) [string]::CompareOrdinal([string]$left.path, [string]$right.path) })
$files = $entries.ToArray()
$openAi = @($files | Where-Object { $_.path -ceq 'OpenAI.dll' })[0]
$manifest = [ordered]@{
    kind = 'pc-steward-ai-first-live-test-build'
    notARelease = $true
    sourceCommit = $commit
    runId = [string]$env:GITHUB_RUN_ID
    buildProperty = 'PCStewardAiFirstLiveTest=true'
    reviewedModel = 'gpt-6.1-sol'
    paidRequests = 'FirstLiveTest'
    openAiPackage = [ordered]@{ version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $app 'OpenAI.dll')).ProductVersion; sha256 = $openAi.sha256 }
    files = $files
}
$manifestPath = Join-Path $out 'ai-first-live-test-manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
# The upload folder must hold exactly the listed app files and the manifest: nothing else, hidden or not.
$uploaded = @(Get-ChildItem -LiteralPath $out -Recurse -File -Force)
if ($uploaded.Count -ne $files.Count + 1) { throw "The upload folder holds $($uploaded.Count) files, but the manifest lists $($files.Count) plus itself." }
Write-Host ("AI-TEST-BUILD commit=$commit files=$($files.Count) manifestSha256=" + (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash)
