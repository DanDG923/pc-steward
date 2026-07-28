$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -Path (Join-Path $PSScriptRoot '..\SecurityBoundarySpike.cs')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:Assertions++
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $thrown = $false
    try { & $Action } catch { $thrown = $true }
    Assert-True $thrown $Message
}

$script:Assertions = 0
$script:Skips = [System.Collections.Generic.List[string]]::new()
$container = Join-Path ([IO.Path]::GetTempPath()) ('pc-steward-security-spike-' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $container 'owned'
$outside = Join-Path $container 'outside'
[IO.Directory]::CreateDirectory($root) | Out-Null
[IO.Directory]::CreateDirectory($outside) | Out-Null
$junctions = [System.Collections.Generic.List[string]]::new()
$symbolicLinks = [System.Collections.Generic.List[string]]::new()

try {
    $manifest = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetResetManifest()
    Assert-True ($manifest.Count -eq 10) 'Reset manifest must contain only the ten fixed owned categories.'
    Assert-True ($manifest.Values -notcontains '..') 'Reset manifest must not contain traversal.'
    foreach ($category in [enum]::GetValues([PcSteward.SecurityBoundarySpike.ResetCategory])) {
        $path = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath($root, $category)
        Assert-True ($path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Every category must be component-contained.'
    }

    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, 'relative\logs')
    } 'Relative paths must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $outside)
    } 'Alternate owned roots must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, (Join-Path $root 'logs\..\..\outside'))
    } 'Crafted metadata traversal must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, ($root + '-prefix\logs'))
    } 'Path-prefix confusion must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, ($root + '\logs:stream'))
    } 'Alternate data streams must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, '\\localhost\c$\outside')
    } 'UNC alternate roots must be rejected.'

    $logs = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Logs)
    [IO.Directory]::CreateDirectory($logs) | Out-Null
    $deletable = Join-Path $logs 'deletable.log'
    $locked = Join-Path $logs 'locked.log'
    [IO.File]::WriteAllText($deletable, 'delete me')
    [IO.File]::WriteAllText($locked, 'locked')
    $lock = [IO.File]::Open($locked, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $partial = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
            $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Logs, 1, 5)
        Assert-True ($partial.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A locked file must produce Partial.'
        Assert-True (-not [IO.File]::Exists($deletable)) 'Deletable siblings must be removed during partial cleanup.'
        Assert-True ([IO.File]::Exists($locked)) 'A locked file must remain.'
        Assert-True ($partial.Attempts -eq 2) 'The bounded retry count must be honoured.'
        Assert-True ($partial.Residuals.RelativePath -contains 'logs\locked.log') 'Residual report must identify the locked file relatively.'
        Assert-True ($partial.Residuals.Reason -contains 'LockedOrInUse') 'Residual report must classify sharing violations.'
    }
    finally {
        $lock.Dispose()
    }

    $retried = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Logs)
    Assert-True ($retried.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Success) 'A safe retry after releasing the file must succeed.'
    $idempotent = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Logs)
    Assert-True ($idempotent.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Success -and $idempotent.AlreadyAbsent) 'Repeated deletion must be successful and explicitly already absent.'

    $downloads = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Downloads)
    [IO.Directory]::CreateDirectory($downloads) | Out-Null
    $outsideSentinel = Join-Path $outside 'must-survive.txt'
    [IO.File]::WriteAllText($outsideSentinel, 'outside')
    $junction = Join-Path $downloads 'junction'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $junctions.Add($junction)
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $junction)
    } 'Junction traversal must be rejected.'
    $junctionReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Downloads)
    Assert-True ($junctionReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A reparse point must prevent a Success result.'
    Assert-True ($junctionReport.Residuals.Reason -contains 'UnsafePath') 'Reparse residual must be reported as unsafe.'
    Assert-True ([IO.File]::Exists($outsideSentinel)) 'Junction rejection must preserve the external target.'

    $settings = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Settings)
    [IO.Directory]::CreateDirectory($settings) | Out-Null
    $link = Join-Path $settings 'symbolic-link'
    $linkCreated = $false
    try {
        New-Item -ItemType SymbolicLink -Path $link -Target $outsideSentinel | Out-Null
        $symbolicLinks.Add($link)
        $linkCreated = $true
    }
    catch {
        $script:Skips.Add('Symbolic-link creation unavailable; execute the report VM case.')
    }
    if ($linkCreated) {
        Assert-Throws {
            [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $link)
        } 'Symbolic-link traversal must be rejected.'
        $linkReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
            $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Settings)
        Assert-True ($linkReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A symbolic link must prevent a Success result.'
        Assert-True ([IO.File]::Exists($outsideSentinel)) 'Symbolic-link rejection must preserve the external target.'
    }

    $scanData = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::ScanData)
    [IO.Directory]::CreateDirectory($scanData) | Out-Null
    $swap = Join-Path $scanData 'swap'
    [IO.Directory]::CreateDirectory($swap) | Out-Null
    $swapState = [pscustomobject]@{ Swapped = $false }
    $swapHook = [Action[string]] {
        param([string]$path)
        if (-not $swapState.Swapped -and $path -eq $swap) {
            [IO.Directory]::Delete($swap)
            New-Item -ItemType Junction -Path $swap -Target $outside | Out-Null
            $junctions.Add($swap)
            $swapState.Swapped = $true
        }
    }
    $swapReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::ScanData, 0, 0, $swapHook)
    Assert-True $swapState.Swapped 'The test must perform a path swap after validation and before handle acquisition.'
    Assert-True ($swapReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A path-swap to a reparse point must fail closed.'
    Assert-True ([IO.File]::Exists($outsideSentinel)) 'The TOCTOU attempt must not delete the outside target.'

    $key = [byte[]](1..32)
    $jobId = [guid]::NewGuid()
    $nonce = [Convert]::ToBase64String([byte[]](33..48))
    $now = [DateTimeOffset]::UtcNow
    $request = [PcSteward.SecurityBoundarySpike.HelperProtocol]::CreateAuthenticatedRequest(
        $jobId, 'Contoso.App', '2.0.0', $nonce, $now, $key)
    $accepted = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, $jobId, $nonce, $now.AddSeconds(1), $key, [string[]]@('Contoso.App'))
    Assert-True ($accepted.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::AcceptedNoOp) 'An authenticated allowlisted request must reach no-op acceptance.'

    $mutated = $request.Replace('Contoso.App', 'Other.App')
    $deniedMutation = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $mutated, $jobId, $nonce, $now, $key, [string[]]@('Contoso.App', 'Other.App'))
    Assert-True ($deniedMutation.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Authenticated request mutation must be denied.'
    $wrongJob = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, [guid]::NewGuid(), $nonce, $now, $key, [string[]]@('Contoso.App'))
    Assert-True ($wrongJob.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Job replay into another immutable job must be denied.'
    $wrongNonce = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, $jobId, 'wrong-nonce', $now, $key, [string[]]@('Contoso.App'))
    Assert-True ($wrongNonce.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Nonce replay must be denied.'
    $unreviewed = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, $jobId, $nonce, $now, $key, [string[]]@('Other.App'))
    Assert-True ($unreviewed.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Unreviewed package IDs must be denied.'
    $expired = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, $jobId, $nonce, $now.AddMinutes(3), $key, [string[]]@('Contoso.App'))
    Assert-True ($expired.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Expired) 'Expired requests must have a distinct state.'
    $cancelled = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $request, $jobId, $nonce, $now, $key, [string[]]@('Contoso.App'), $true)
    Assert-True ($cancelled.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Cancelled) 'Cancellation must have a distinct pre-execution state.'

    $withPath = $request.Insert($request.LastIndexOf('}'), ',"workingDirectory":"C:\\Windows"')
    $deniedPath = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $withPath, $jobId, $nonce, $now, $key, [string[]]@('Contoso.App'))
    Assert-True ($deniedPath.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Unknown path fields must be structurally denied.'
    $duplicate = $request.Insert($request.LastIndexOf('}'), ',"packageId":"Contoso.App"')
    $deniedDuplicate = [PcSteward.SecurityBoundarySpike.HelperProtocol]::Evaluate(
        $duplicate, $jobId, $nonce, $now, $key, [string[]]@('Contoso.App'))
    Assert-True ($deniedDuplicate.State -eq [PcSteward.SecurityBoundarySpike.HelperState]::Denied) 'Duplicate fields must be denied.'

    Write-Output "PASS: $Assertions assertions; $($Skips.Count) VM/manual skips"
    foreach ($skip in $Skips) { Write-Output "SKIP: $skip" }
}
finally {
    foreach ($linkPath in @($symbolicLinks) + @($junctions)) {
        if ([string]::IsNullOrWhiteSpace($linkPath)) { continue }
        try {
            if ((Get-Item -LiteralPath $linkPath -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                (Get-Item -LiteralPath $linkPath -Force).Delete()
            }
        } catch {}
    }
    if ([IO.Directory]::Exists($container)) {
        [IO.Directory]::Delete($container, $true)
    }
}
