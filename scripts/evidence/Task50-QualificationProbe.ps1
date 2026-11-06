#Requires -Version 5.1
<#
  Task 50 Part C qualification-probe host tooling (S7; steps C1-C7 of docs/review/TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md section 8).
  Requirements: FR-UPD-15, TM-03/04/07/13. Contract: docs/review/TASK_50_QUALIFICATION_PROBE_SPEC.md sections 5.3, 8 and 9
  (S7 obligations 1-3). Record: docs/review/TASK_50_ACTIVATION_CHANGE.md. Approval binds the CRLF file SHA-256 of this script.

  The owner runs it from their own non-elevated Windows PowerShell 5.1 console, after the C-pre Activity check:
    -Mode Check  C1 pins and C2 baseline only; launches nothing.
    -Mode Run    C1, C2, then C3 (one launch of the installed desktop with --qualify-chocolatey-helper; exactly one UAC
                 prompt, answered by the owner), C4 (C2 again), C5 (strict read of the result file), C6 (spec section 8
                 interpretation tokens) and C7 (removes %TEMP%\pcsteward-exact-artifact only, and only if C3 created it).

  Safety: never runs choco, never asks for elevation itself, starts exactly one process (the installed desktop),
  writes nothing (no transcript, log or copy), deletes only the result file and folder it checked in C7, and prints only
  fixed tokens, hashes, counts, versions and the closed result document: no path, user name or SID. Any unexpected state
  prints "STOP: ..." and ends the run. Pure ASCII, CRLF.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Check', 'Run')][string]$Mode,
    # The owner's C-pre answer (S7 obligation 3): Activity shows no record that blocks other updates.
    [Parameter(Mandatory = $true)][ValidateSet('NoBlockingRecord')][string]$ActivityCheck,
    # The G3 builder record (candidate-build.json) of the deployed activation build.
    [Parameter(Mandatory = $true)][string]$BuildRecordPath,
    # Deployment record pins from the approved redeploy (filled in the approval text; uppercase hex).
    [Parameter(Mandatory = $true)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory = $true)][string]$ExpectedLedgerSha256,
    [Parameter(Mandatory = $true)][string]$ExpectedSignerThumbprint,
    # yyyy-MM-ddTHH:mm:ssZ (UTC).
    [Parameter(Mandatory = $true)][string]$ExpectedSignerNotAfterUtc
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- Pure functions (the fixture tests run these from the parsed script) -------------------------------------------------

# The closed domains of the result document (spec section 5.3) and of the compiled activation build. A fixture test pins each
# list against the C# enums and the compiled contract.
function Get-ProbeContract {
    [ordered]@{
        Schema = 'pc-steward-qualification-probe-result:2'
        Fields = [string[]]@('schema', 'desktopOutcome', 'capabilityStatus', 'initialLaunchTrust', 'finalLaunchTrust', 'helperProcessCreated',
            'pipeConnected', 'desktopPeerVerified', 'helperFileIdEqual', 'responseFrameReceived', 'pipeEofObserved', 'helperExitObserved',
            'bindingHeldUntilExit', 'terminationFailed', 'helperExitCode', 'pins', 'probe', 'startedAtUtc', 'finishedAtUtc')
        Facts = [string[]]@('initialLaunchTrust', 'finalLaunchTrust', 'helperProcessCreated', 'pipeConnected', 'desktopPeerVerified',
            'helperFileIdEqual', 'responseFrameReceived', 'pipeEofObserved', 'helperExitObserved', 'bindingHeldUntilExit', 'terminationFailed')
        PinFields = [string[]]@('ledgerSha256', 'capabilityFileSha256', 'desktopImageSha256', 'helperImageSha256', 'signerThumbprint', 'signerNotAfterUtc')
        ProbeFields = [string[]]@('tokenVerdict', 'g1Status', 'observation', 'failedStep', 'failedCheck', 'failedAccess', 'refusalReason',
            'refusalDetail', 'nodesEvaluated', 'ancestorsEvaluated', 'observedClientVersion', 'fingerprintPresent', 'fingerprintLength', 'diagnostic')
        Outcomes = [string[]]@('ResponseAccepted', 'GateBusy', 'GateUnavailable', 'GateRecoveryRequired', 'GateResetPending', 'DesktopNotLimited',
            'ConflictingProcess', 'LaunchTrustFailed', 'CapabilityNotHeld', 'ElevationDeclined', 'HelperNotStarted', 'HelperNotConnected',
            'PeerNotVerified', 'NoValidResponse', 'ResponseRejected')
        # The compiled capability allow-list of the activation build, in ordinal order.
        Tokens = [string[]]@('Update/ChocolateyClientSelfUpdateV1', 'Update/ChocolateyMachineRuntimeLimitsV3')
        Statuses = [string[]]@('Supported', 'Absent', 'Incomplete', 'Unreadable', 'Mismatch', 'NotEvaluated')
        Verdicts = [string[]]@('Accepted', 'FactsMalformed', 'PeerNotLimited', 'PeerSidMismatch', 'EvaluationSidMismatch', 'AuthenticationIdMismatch',
            'EvaluationNotLimited', 'EvaluationNotMediumIntegrity', 'EvaluationRestricted', 'EvaluationAppContainer', 'EvaluationAdministratorsEnabled',
            'EvaluationPrivilegeNotAllowed', 'PeerProcessIdMismatch', 'PeerExited', 'PeerTokenUnreadable', 'NoLinkedToken', 'EvaluationTokenUnreadable',
            'EvaluationNotIdentificationLevel')
        Observations = [string[]]@('NotRun', 'Observed', 'Refused', 'TimedOut')
        Checks = [string[]]@('None', 'StepRule', 'InstalledVersion', 'SetupAccessGuard', 'ReleaseLeftover')
        Accesses = [string[]]@('None', 'Granted', 'Unreadable', 'Other')
        Reasons = [string[]]@('None', 'MachineInputsUnsupported', 'SourceNotCommunity', 'InstalledMismatch')
        Details = [string[]]@('None', 'UnlistedReleaseLeftover', 'SetupAccessOverride')
        Diagnostics = [string[]]@('probe-token-refused', 'probe-capability-refused', 'probe-observed', 'probe-observation-refused', 'probe-observation-timed-out')
        # The compiled reviewed release list (ascending); the last is the newest listed release (E2).
        ListedReleases = [string[]]@('2.7.5')
        MaximumNodes = 1048576
        MaximumAncestors = 65536
        MaximumBytes = 4096
        # spec section 8, E3.
        InconclusiveOutcomes = [string[]]@('ElevationDeclined', 'GateBusy', 'GateUnavailable', 'GateRecoveryRequired', 'GateResetPending',
            'ConflictingProcess', 'DesktopNotLimited', 'HelperNotStarted')
        # S7 obligation 1: the G3 builder's probe-build mark.
        BuildRecordSchema = 'pc-steward-owner-only-candidate-build-v2'
        ProbeMarkRule = 'pc-steward-probe-build-mark:1'
        ProbeCommit = '2172f955a751ccc464b27075053c835790bc412f'
        LedgerSchema = 'pc-steward-owner-only-protected-test-v6'
    }
}

function Test-ProbeHex([object]$Value, [int]$Length) {
    return ($Value -is [string] -and $Value.Length -eq $Length -and $Value -cmatch '\A[0-9A-F]+\z')
}

# Strict compact JSON reader for the closed result document: objects, strings without escapes (printable ASCII only),
# integers, true, false and null; no whitespace, no arrays, at most three levels. Returns ordinal ordered dictionaries,
# [string], [long], [bool] or $null. Anything else throws a fixed token.
function ConvertFrom-ProbeCompactJson([string]$Text) {
    $state = [pscustomobject]@{ Text = $Text; Index = 0 }
    $value = Read-ProbeJsonValue $state 0
    if ($state.Index -ne $Text.Length) { throw 'json-trailing' }
    return , $value
}

function Read-ProbeJsonValue($State, [int]$Depth) {
    $text = $State.Text
    if ($State.Index -ge $text.Length) { throw 'json-end' }
    $c = $text[$State.Index]
    if ($c -ceq '{') {
        if ($Depth -ge 3) { throw 'json-depth' }
        $object = [Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal)
        $State.Index++
        if ($State.Index -lt $text.Length -and $text[$State.Index] -ceq '}') { $State.Index++; return , $object }
        while ($true) {
            if ($State.Index -ge $text.Length -or $text[$State.Index] -cne '"') { throw 'json-key' }
            $key = Read-ProbeJsonString $State
            if ($State.Index -ge $text.Length -or $text[$State.Index] -cne ':') { throw 'json-colon' }
            $State.Index++
            $item = Read-ProbeJsonValue $State ($Depth + 1)
            if ($object.Contains($key)) { throw 'json-duplicate' }
            $object.Add($key, $item)
            if ($State.Index -ge $text.Length) { throw 'json-end' }
            $delimiter = $text[$State.Index]
            $State.Index++
            if ($delimiter -ceq ',') { continue }
            if ($delimiter -ceq '}') { return , $object }
            throw 'json-delimiter'
        }
    }
    if ($c -ceq '"') { return Read-ProbeJsonString $State }
    foreach ($literal in @('true', 'false', 'null')) {
        if ($State.Index + $literal.Length -le $text.Length -and [string]::CompareOrdinal($text, $State.Index, $literal, 0, $literal.Length) -eq 0) {
            $State.Index += $literal.Length
            if ($literal -ceq 'true') { return $true }
            if ($literal -ceq 'false') { return $false }
            return $null
        }
    }
    $start = $State.Index
    if ($text[$State.Index] -ceq '-') { $State.Index++ }
    $digits = $State.Index
    while ($State.Index -lt $text.Length -and $text[$State.Index] -ge [char]'0' -and $text[$State.Index] -le [char]'9') { $State.Index++ }
    $length = $State.Index - $digits
    if ($length -lt 1 -or $length -gt 10 -or ($length -gt 1 -and $text[$digits] -ceq '0')) { throw 'json-number' }
    $number = [long]::Parse($text.Substring($start, $State.Index - $start), [Globalization.CultureInfo]::InvariantCulture)
    if ($number -lt [int]::MinValue -or $number -gt [int]::MaxValue) { throw 'json-number' }
    return $number
}

function Read-ProbeJsonString($State) {
    $text = $State.Text
    $State.Index++
    $start = $State.Index
    while ($State.Index -lt $text.Length -and $text[$State.Index] -cne '"') {
        $code = [int]$text[$State.Index]
        if ($code -lt 0x20 -or $code -gt 0x7E -or $text[$State.Index] -ceq '\') { throw 'json-string' }
        $State.Index++
    }
    if ($State.Index -ge $text.Length) { throw 'json-string' }
    $value = $text.Substring($start, $State.Index - $start)
    $State.Index++
    return $value
}

function Assert-ProbeKeys($Object, [string[]]$Expected, [string]$Name) {
    if ($Object -isnot [Collections.Specialized.OrderedDictionary]) { throw "result-$Name-not-object" }
    $keys = [string[]]@($Object.Keys)
    if ($keys.Length -ne $Expected.Length) { throw "result-$Name-keys" }
    for ($index = 0; $index -lt $keys.Length; $index++) { if ($keys[$index] -cne $Expected[$index]) { throw "result-$Name-keys" } }
}

function Assert-ProbeMember([object]$Value, [string[]]$Domain, [string]$Name) {
    if ($Value -isnot [string] -or $Domain -cnotcontains $Value) { throw "result-$Name" }
}

function Assert-ProbeTimestamp([object]$Value, [string]$Name) {
    if ($Value -isnot [string] -or $Value -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z\z') { throw "result-$Name" }
}

function Assert-ProbeCount([object]$Value, [long]$Minimum, [long]$Maximum, [string]$Name) {
    if ($Value -isnot [long] -or $Value -lt $Minimum -or $Value -gt $Maximum) { throw "result-$Name" }
}

# C5: the closed result document (spec section 5.3), read strictly. Returns the parsed document or throws a fixed token.
function Test-ProbeResultDocument([byte[]]$Bytes) {
    $contract = Get-ProbeContract
    if ($null -eq $Bytes -or $Bytes.Length -lt 2 -or $Bytes.Length -gt $contract['MaximumBytes']) { throw 'result-size' }
    if ($Bytes[0] -eq 0xEF) { throw 'result-bom' }
    foreach ($byte in $Bytes) { if ($byte -lt 0x20 -or $byte -gt 0x7E) { throw 'result-not-printable-ascii' } }
    $text = [Text.Encoding]::ASCII.GetString($Bytes)
    try { $document = ConvertFrom-ProbeCompactJson $text } catch { throw ('result-' + $_.Exception.Message) }
    Assert-ProbeKeys $document $contract['Fields'] 'top'
    if ($document['schema'] -isnot [string] -or $document['schema'] -cne $contract['Schema']) { throw 'result-schema' }
    Assert-ProbeMember $document['desktopOutcome'] $contract['Outcomes'] 'desktopOutcome'
    Assert-ProbeKeys $document['capabilityStatus'] $contract['Tokens'] 'capabilityStatus'
    foreach ($token in $contract['Tokens']) { Assert-ProbeMember $document['capabilityStatus'][$token] $contract['Statuses'] 'capabilityStatus' }
    foreach ($fact in $contract['Facts']) { if ($document[$fact] -isnot [bool]) { throw "result-$fact" } }
    if ($null -ne $document['helperExitCode']) { Assert-ProbeCount $document['helperExitCode'] ([int]::MinValue) ([int]::MaxValue) 'helperExitCode' }
    $pins = $document['pins']
    if ($null -ne $pins) {
        Assert-ProbeKeys $pins $contract['PinFields'] 'pins'
        foreach ($field in @('ledgerSha256', 'capabilityFileSha256', 'desktopImageSha256', 'helperImageSha256')) { if (!(Test-ProbeHex $pins[$field] 64)) { throw "result-$field" } }
        if (!(Test-ProbeHex $pins['signerThumbprint'] 40)) { throw 'result-signerThumbprint' }
        Assert-ProbeTimestamp $pins['signerNotAfterUtc'] 'signerNotAfterUtc'
    }
    $probe = $document['probe']
    if (($null -ne $probe) -ne ($document['desktopOutcome'] -ceq 'ResponseAccepted')) { throw 'result-probe-presence' }
    if ($null -ne $probe) {
        Assert-ProbeKeys $probe $contract['ProbeFields'] 'probe'
        Assert-ProbeMember $probe['tokenVerdict'] $contract['Verdicts'] 'tokenVerdict'
        Assert-ProbeKeys $probe['g1Status'] $contract['Tokens'] 'g1Status'
        foreach ($token in $contract['Tokens']) { Assert-ProbeMember $probe['g1Status'][$token] $contract['Statuses'] 'g1Status' }
        Assert-ProbeMember $probe['observation'] $contract['Observations'] 'observation'
        if ($null -ne $probe['failedStep']) { Assert-ProbeCount $probe['failedStep'] 1 9 'failedStep' }
        Assert-ProbeMember $probe['failedCheck'] $contract['Checks'] 'failedCheck'
        Assert-ProbeMember $probe['failedAccess'] $contract['Accesses'] 'failedAccess'
        Assert-ProbeMember $probe['refusalReason'] $contract['Reasons'] 'refusalReason'
        Assert-ProbeMember $probe['refusalDetail'] $contract['Details'] 'refusalDetail'
        Assert-ProbeCount $probe['nodesEvaluated'] 0 $contract['MaximumNodes'] 'nodesEvaluated'
        Assert-ProbeCount $probe['ancestorsEvaluated'] 0 $contract['MaximumAncestors'] 'ancestorsEvaluated'
        if ($null -ne $probe['observedClientVersion']) { Assert-ProbeMember $probe['observedClientVersion'] $contract['ListedReleases'] 'observedClientVersion' }
        if ($probe['fingerprintPresent'] -isnot [bool]) { throw 'result-fingerprintPresent' }
        Assert-ProbeCount $probe['fingerprintLength'] 0 64 'fingerprintLength'
        Assert-ProbeMember $probe['diagnostic'] $contract['Diagnostics'] 'diagnostic'
    }
    Assert-ProbeTimestamp $document['startedAtUtc'] 'startedAtUtc'
    Assert-ProbeTimestamp $document['finishedAtUtc'] 'finishedAtUtc'
    return , $document
}

# C1 (S7 obligation 1): the G3 build record must be the deployed build and carry the "probe included" mark.
# Returns the build commit, or throws 'not-a-probe-build:<detail>'.
function Test-ProbeBuildRecord([string]$RecordText, [string]$ExpectedManifestSha256) {
    $contract = Get-ProbeContract
    try { $record = ConvertFrom-Json -InputObject $RecordText } catch { throw 'not-a-probe-build:record-unreadable' }
    $names = @($record.PSObject.Properties.Name)
    foreach ($name in @('schema', 'validation', 'manifestSha256', 'sourceCommit', 'sourceDirty', 'probeIncluded', 'probeMark', 'probeMarkDetail', 'probeCommit')) {
        if ($names -cnotcontains $name) { throw "not-a-probe-build:record-lacks-$name" }
    }
    if ($record.schema -isnot [string] -or $record.schema -cne $contract['BuildRecordSchema']) { throw 'not-a-probe-build:record-schema' }
    if ($record.validation -isnot [string] -or $record.validation -cne 'Pass') { throw 'not-a-probe-build:record-not-validated' }
    if (!(Test-ProbeHex $record.manifestSha256 64) -or $record.manifestSha256 -cne $ExpectedManifestSha256) { throw 'not-a-probe-build:record-is-not-the-deployed-build' }
    if ($record.sourceCommit -isnot [string] -or $record.sourceCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'not-a-probe-build:record-commit' }
    if ($record.sourceDirty -isnot [bool] -or $record.sourceDirty) { throw 'not-a-probe-build:record-dirty' }
    if ($record.probeMark -isnot [string] -or $record.probeMark -cne $contract['ProbeMarkRule']) { throw 'not-a-probe-build:mark-rule' }
    if ($record.probeCommit -isnot [string] -or $record.probeCommit -cne $contract['ProbeCommit']) { throw 'not-a-probe-build:mark-commit' }
    if ($record.probeIncluded -isnot [bool] -or !$record.probeIncluded -or $record.probeMarkDetail -cne 'included') { throw 'not-a-probe-build:probe-not-included' }
    return [string]$record.sourceCommit
}

# C1: the deployment record (ADR-022 ledger). Returns the installed SHA-256 the ledger pins for each probe-relevant file, or
# throws 'pin-differs:<name>'.
function Read-ProbeLedgerPins([string]$LedgerText, [string]$ExpectedManifestSha256, [string]$ExpectedSignerThumbprint) {
    $contract = Get-ProbeContract
    try { $ledger = ConvertFrom-Json -InputObject $LedgerText } catch { throw 'pin-differs:ledger-unreadable' }
    $names = @($ledger.PSObject.Properties.Name)
    foreach ($name in @('schema', 'completed', 'payloadOrigin', 'candidateManifestSha256', 'certificateThumbprint', 'files')) {
        if ($names -cnotcontains $name) { throw "pin-differs:ledger-lacks-$name" }
    }
    if ($ledger.schema -cne $contract['LedgerSchema'] -or $ledger.completed -isnot [bool] -or !$ledger.completed) { throw 'pin-differs:ledger-state' }
    if ($ledger.payloadOrigin -cne 'pinned-unsigned-v2') { throw 'pin-differs:payload-origin' }
    if ($ledger.candidateManifestSha256 -cne $ExpectedManifestSha256) { throw 'pin-differs:manifest' }
    if ($ledger.certificateThumbprint -cne $ExpectedSignerThumbprint) { throw 'pin-differs:signer' }
    $pins = [ordered]@{}
    foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.DeploymentCapability.txt')) {
        $entries = @($ledger.files | Where-Object { $_.path -ceq $leaf })
        if ($entries.Count -ne 1) { throw "pin-differs:ledger-$leaf" }
        $hash = ([string]$entries[0].sha256).ToUpperInvariant()
        if (!(Test-ProbeHex $hash 64)) { throw "pin-differs:ledger-$leaf" }
        $pins[$leaf] = $hash
    }
    return $pins
}

function Get-ProbeFileSha256([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $sha.Dispose() }
    } finally { $stream.Dispose() }
}

# C2/C4: one combined SHA-256 over the sorted lines "D|path", "R|path" (a reparse point, never followed) and
# "F|path|length|sha256" (or "F|path|length|UNREADABLE") of a tree. Paths are relative with '\' separators. Reads only.
function Get-ProbeTreeDigest([string]$Root) {
    $rootInfo = [IO.DirectoryInfo]::new($Root)
    if (!$rootInfo.Exists -or ($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'tree-root' }
    $lines = [Collections.Generic.List[string]]::new()
    $files = 0
    $pending = [Collections.Generic.Stack[object]]::new()
    $pending.Push([pscustomobject]@{ Directory = $rootInfo; Relative = '' })
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($item in $current.Directory.GetFileSystemInfos()) {
            $relative = if ($current.Relative) { $current.Relative + '\' + $item.Name } else { $item.Name }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $lines.Add('R|' + $relative); continue }
            if ($item -is [IO.DirectoryInfo]) { $lines.Add('D|' + $relative); $pending.Push([pscustomobject]@{ Directory = $item; Relative = $relative }); continue }
            $files++
            $hash = 'UNREADABLE'
            try {
                $stream = [IO.File]::Open($item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                try {
                    $sha = [Security.Cryptography.SHA256]::Create()
                    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $sha.Dispose() }
                } finally { $stream.Dispose() }
            } catch { $hash = 'UNREADABLE' }
            $lines.Add('F|' + $relative + '|' + $item.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + '|' + $hash)
        }
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]::Join("`n", $sorted))
    $digest = [Security.Cryptography.SHA256]::Create()
    try { $value = [BitConverter]::ToString($digest.ComputeHash($bytes)).Replace('-', '') } finally { $digest.Dispose() }
    [pscustomobject]@{ Sha256 = $value; Files = $files }
}

function New-ProbeVerdict([string]$Verdict, [string]$Detail) { [pscustomobject]@{ Verdict = $Verdict; Detail = $Detail } }

# C6: the spec section 8 interpretation. $ResultState is 'absent', 'malformed' or 'valid' ($Result is the parsed document
# when valid). $Pins holds the C1 values; $Baseline and $After the C2 and C4 observations ($null when missing). E3 also
# needs the owner's own judgment of the UAC prompt, which no script can see.
function Get-ProbeVerdicts([string]$ResultState, $Result, $Pins, $Baseline, $After) {
    $contract = Get-ProbeContract
    $verdicts = [ordered]@{}
    $accepted = $ResultState -ceq 'valid' -and $Result['desktopOutcome'] -ceq 'ResponseAccepted'
    $probe = if ($accepted) { $Result['probe'] } else { $null }
    $tokenAccepted = $accepted -and $probe['tokenVerdict'] -ceq 'Accepted'
    $allSupported = $tokenAccepted -and @($contract['Tokens'] | Where-Object { $probe['g1Status'][$_] -cne 'Supported' }).Count -eq 0

    # E1 token match.
    $verdicts['E1'] = if (!$accepted) { New-ProbeVerdict 'INCONCLUSIVE' 'no-accepted-response' }
        elseif ($tokenAccepted) { New-ProbeVerdict 'PASS' 'tokenVerdict=Accepted' }
        else { New-ProbeVerdict 'FAIL' ('tokenVerdict=' + $probe['tokenVerdict']) }
    # A3 / G1, for every allow-list token.
    $verdicts['A3'] = if (!$tokenAccepted) { New-ProbeVerdict 'INCONCLUSIVE' 'token-not-accepted' }
        elseif ($allSupported) { New-ProbeVerdict 'PASS' 'g1Status=Supported for every token' }
        else { New-ProbeVerdict 'FAIL' ('g1Status=' + (@($contract['Tokens'] | ForEach-Object { $probe['g1Status'][$_] }) -join ',')) }
    # E2 AccessCheck.
    $newest = $contract['ListedReleases'][$contract['ListedReleases'].Length - 1]
    $verdicts['E2'] = if (!$allSupported) { New-ProbeVerdict 'INCONCLUSIVE' 'not-run' }
        elseif ($probe['observation'] -ceq 'Observed') {
            if ($probe['observedClientVersion'] -ceq $newest -and $probe['fingerprintPresent'] -eq $true -and $probe['fingerprintLength'] -eq 64 -and
                $probe['nodesEvaluated'] -ge 1 -and $probe['ancestorsEvaluated'] -ge 1 -and $null -eq $probe['failedStep']) { New-ProbeVerdict 'PASS' ('observed ' + $newest) }
            else { New-ProbeVerdict 'FAIL' 'observed-facts-incomplete' }
        }
        elseif ($probe['observation'] -ceq 'Refused' -and $probe['refusalReason'] -cne 'InstalledMismatch') {
            New-ProbeVerdict 'FAIL' ('refused step=' + $probe['failedStep'] + ' check=' + $probe['failedCheck'] + ' access=' + $probe['failedAccess'] + ' reason=' + $probe['refusalReason'] + ' detail=' + $probe['refusalDetail'])
        }
        else { New-ProbeVerdict 'INCONCLUSIVE' ('observation=' + $probe['observation'] + ' reason=' + $probe['refusalReason']) }
    # E3 real transport (machine facts; the owner judges the prompt).
    $verdicts['E3'] = if ($ResultState -ceq 'absent') { New-ProbeVerdict 'INCONCLUSIVE' 'no-result-file' }
        elseif ($ResultState -cne 'valid') { New-ProbeVerdict 'FAIL' 'result-malformed' }
        elseif ($contract['InconclusiveOutcomes'] -ccontains $Result['desktopOutcome']) { New-ProbeVerdict 'INCONCLUSIVE' ('desktopOutcome=' + $Result['desktopOutcome']) }
        elseif (!$accepted) { New-ProbeVerdict 'FAIL' ('desktopOutcome=' + $Result['desktopOutcome']) }
        else {
            $falseFacts = @($contract['Facts'] | Where-Object { $_ -cne 'terminationFailed' -and $Result[$_] -ne $true })
            $resultPins = $Result['pins']
            $pinsEqual = $null -ne $resultPins -and $null -ne $Pins -and $resultPins['ledgerSha256'] -ceq $Pins['ledgerSha256'] -and
                $resultPins['capabilityFileSha256'] -ceq $Pins['capabilityFileSha256'] -and $resultPins['desktopImageSha256'] -ceq $Pins['desktopImageSha256'] -and
                $resultPins['helperImageSha256'] -ceq $Pins['helperImageSha256'] -and $resultPins['signerThumbprint'] -ceq $Pins['signerThumbprint'] -and
                $resultPins['signerNotAfterUtc'] -ceq $Pins['signerNotAfterUtc']
            if ($falseFacts.Count -ne 0) { New-ProbeVerdict 'FAIL' ('false-facts=' + ($falseFacts -join ',')) }
            elseif ($Result['terminationFailed'] -ne $false) { New-ProbeVerdict 'FAIL' 'terminationFailed' }
            elseif ($Result['helperExitCode'] -isnot [long] -or $Result['helperExitCode'] -ne 0) { New-ProbeVerdict 'FAIL' 'helperExitCode' }
            elseif (!$pinsEqual) { New-ProbeVerdict 'FAIL' 'pins-differ-from-C1' }
            else { New-ProbeVerdict 'PASS' 'machine facts; owner prompt judgment required' }
        }
    # E5 nothing changed.
    $verdicts['E5'] = if ($null -eq $Baseline -or $null -eq $After) { New-ProbeVerdict 'INCONCLUSIVE' 'baseline-missing' }
        else {
            $changes = @(foreach ($name in @('Database', 'Data', 'Chocolatey')) { if ($Baseline.$name -cne $After.$name) { $name } })
            if ($After.ChocoProcesses -ne 0) { $changes += 'ChocoProcess' }
            if (!$After.OnlyResultFolderNew) { $changes += 'TempFolder' }
            if ($changes.Count -eq 0) { New-ProbeVerdict 'PASS' 'C4=C2' } else { New-ProbeVerdict 'FAIL' ('changed=' + ($changes -join ',')) }
        }
    $all = @($verdicts.Values | ForEach-Object { $_.Verdict })
    $verdicts['OVERALL'] = if ($all -ccontains 'FAIL') { New-ProbeVerdict 'FAIL' 'any FAIL stops use (Decision 2); not repeated' }
        elseif (@($all | Where-Object { $_ -cne 'PASS' }).Count -eq 0) { New-ProbeVerdict 'PASS' 'valid only with the owner E3 prompt confirmation; proves UAC and pre-launch enforcement only' }
        else { New-ProbeVerdict 'INCONCLUSIVE' 'proves nothing; a repeat needs a new approval' }
    return $verdicts
}

# C6 output: one sanitized line per verdict (fixed tokens and closed values only).
function Format-ProbeVerdicts($Verdicts) {
    foreach ($key in @('E1', 'A3', 'E2', 'E3', 'E5', 'OVERALL')) {
        $prefix = if ($key -ceq 'OVERALL') { 'C6 OVERALL: ' } else { "C6 VERDICT ${key}: " }
        $prefix + $Verdicts[$key].Verdict + ' (' + $Verdicts[$key].Detail + ')'
    }
    'C6 OWNER-CHECK E3: PASS needs exactly one UAC prompt, for PCSteward.ElevatedHelper.exe signed by the owner-only test signer, whose details show --kind qualify-chocolatey-helper right after the program path.'
}

# C7: the result folder may hold only the result file; nothing is followed or recursed into.
function Test-ProbeResultFolderRemovable([string]$Folder) {
    $info = [IO.DirectoryInfo]::new($Folder)
    if (!$info.Exists) { return 'absent' }
    if ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) { return 'reparse-point' }
    foreach ($child in $info.GetFileSystemInfos()) {
        if ($child -is [IO.DirectoryInfo] -or ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $child.Name -cne 'qualification-result.json') { return 'unexpected-content' }
    }
    return 'removable'
}

# ---- Host steps -------------------------------------------------------------------------------------------------------------

function Stop-ProbeRun([string]$Reason) { throw ('STOP: ' + $Reason) }

# Only fixed tokens are ever printed: a message that is not one of this script's own tokens could hold a path or a name.
function Get-ProbeSafeMessage([string]$Message, [string]$Pattern, [string]$Fallback) {
    if ($Message -cmatch $Pattern) { return $Message }
    return $Fallback
}

function Get-ProbeProcessCount([string]$Name) { @(Get-Process -Name $Name -ErrorAction SilentlyContinue).Count }

function Get-ProbeObservation([string]$Database, [string]$DataRoot, [string]$ChocolateyRoot, [string]$ResultFolder) {
    foreach ($suffix in @('-wal', '-shm', '-journal')) { if (Test-Path -LiteralPath ($Database + $suffix)) { Stop-ProbeRun 'database sidecar present' } }
    if (!(Test-Path -LiteralPath $Database -PathType Leaf)) { Stop-ProbeRun 'database absent (run the C-pre first launch)' }
    $chocolatey = Get-ProbeTreeDigest $ChocolateyRoot
    $data = Get-ProbeTreeDigest $DataRoot
    $resultState = Test-ProbeResultFolderRemovable $ResultFolder
    [pscustomobject]@{
        Database = Get-ProbeFileSha256 $Database
        Data = $data.Sha256
        Chocolatey = $chocolatey.Sha256
        ChocolateyFiles = $chocolatey.Files
        ChocoProcesses = Get-ProbeProcessCount 'choco'
        PcStewardProcesses = Get-ProbeProcessCount 'PCSteward*'
        ResultFolder = $resultState
        # E5: nothing but the result folder (holding at most the result file) may be new in TEMP; no folder at all is fine.
        OnlyResultFolderNew = ($resultState -ceq 'removable' -or $resultState -ceq 'absent')
    }
}

function Invoke-ProbeHostRun {
    $contract = Get-ProbeContract
    # Preconditions: the owner's own non-elevated Windows PowerShell 5.1 console on Windows.
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or $PSVersionTable.PSEdition -cne 'Desktop') { Stop-ProbeRun 'precondition (Windows PowerShell 5.1 on Windows)' }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Stop-ProbeRun 'precondition (the console is elevated; use a normal console)' }
    foreach ($pin in @(@($ExpectedManifestSha256, 64, 'manifest'), @($ExpectedLedgerSha256, 64, 'ledger'), @($ExpectedSignerThumbprint, 40, 'signer'))) {
        if (!(Test-ProbeHex $pin[0] $pin[1])) { Stop-ProbeRun ('precondition (expected ' + $pin[2] + ' pin is not uppercase hex)') }
    }
    if ($ExpectedSignerNotAfterUtc -cnotmatch '\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z') { Stop-ProbeRun 'precondition (expected signer NotAfter format)' }
    if ((Get-ProbeProcessCount 'choco') -ne 0 -or (Get-ProbeProcessCount 'PCSteward*') -ne 0) { Stop-ProbeRun 'precondition (a choco or PCSteward process is running; close PC Steward and wait for Chocolatey)' }

    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $installRoot = Join-Path $programFiles 'PC Steward'
    $stateRoot = Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
    $desktop = Join-Path $installRoot 'PCSteward.Desktop.exe'
    $helper = Join-Path $installRoot 'PCSteward.ElevatedHelper.exe'
    $capability = Join-Path $installRoot 'PCSteward.DeploymentCapability.txt'
    $temp = [IO.Path]::GetTempPath()
    if ($temp.Contains('~')) { Stop-ProbeRun 'precondition (TEMP is a short 8.3 path; the probe would refuse it)' }
    $resultFolder = Join-Path $temp 'pcsteward-exact-artifact'
    $resultFile = Join-Path $resultFolder 'qualification-result.json'
    if (Test-Path -LiteralPath $resultFolder) { Stop-ProbeRun 'precondition (the result folder already exists; a run never reuses it)' }
    $dataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward'
    $database = Join-Path $dataRoot 'pcsteward.db'
    $chocolateyRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'chocolatey'
    if (!(Test-Path -LiteralPath $chocolateyRoot -PathType Container)) { Stop-ProbeRun 'precondition (the Chocolatey folder is absent)' }

    # ---- C1 pins ----
    $ledgerBytes = [IO.File]::ReadAllBytes((Join-Path $stateRoot 'deployment.json'))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $ledgerSha256 = [BitConverter]::ToString($sha.ComputeHash($ledgerBytes)).Replace('-', '') } finally { $sha.Dispose() }
    if ($ledgerSha256 -cne $ExpectedLedgerSha256) { Stop-ProbeRun 'pin differs (ledger)' }
    try { $ledgerPins = Read-ProbeLedgerPins ([Text.UTF8Encoding]::new($false, $true).GetString($ledgerBytes).TrimStart([char]0xFEFF)) $ExpectedManifestSha256 $ExpectedSignerThumbprint }
    catch { Stop-ProbeRun ('pin differs (' + (Get-ProbeSafeMessage ([string]$_.Exception.Message) '\Apin-differs:[A-Za-z0-9.-]+\z' 'ledger').Replace('pin-differs:', '') + ')') }
    if ((Get-ProbeFileSha256 (Join-Path $stateRoot 'candidate-manifest.json')) -cne $ExpectedManifestSha256) { Stop-ProbeRun 'pin differs (protected manifest)' }
    $installed = [ordered]@{ desktop = (Get-ProbeFileSha256 $desktop); helper = (Get-ProbeFileSha256 $helper); capability = (Get-ProbeFileSha256 $capability) }
    if ($installed['desktop'] -cne $ledgerPins['PCSteward.Desktop.exe']) { Stop-ProbeRun 'pin differs (desktop)' }
    if ($installed['helper'] -cne $ledgerPins['PCSteward.ElevatedHelper.exe']) { Stop-ProbeRun 'pin differs (helper)' }
    if ($installed['capability'] -cne $ledgerPins['PCSteward.DeploymentCapability.txt']) { Stop-ProbeRun 'pin differs (capability)' }
    $notAfter = $null
    foreach ($image in @($desktop, $helper)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $image
        if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -cne $ExpectedSignerThumbprint) { Stop-ProbeRun 'pin differs (signature)' }
        $value = $signature.SignerCertificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
        if ($value -cne $ExpectedSignerNotAfterUtc) { Stop-ProbeRun 'pin differs (signer NotAfter)' }
        if ($signature.SignerCertificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddMinutes(30)) { Stop-ProbeRun 'pin differs (signer expires within 30 minutes)' }
        $notAfter = $value
    }
    try { $buildCommit = Test-ProbeBuildRecord ([IO.File]::ReadAllText($BuildRecordPath)) $ExpectedManifestSha256 }
    catch { Stop-ProbeRun ('not a probe build (' + (Get-ProbeSafeMessage ([string]$_.Exception.Message) '\Anot-a-probe-build:[A-Za-z0-9-]+\z' 'record-unreadable').Replace('not-a-probe-build:', '') + ')') }
    $pins = [ordered]@{
        ledgerSha256 = $ledgerSha256; capabilityFileSha256 = $installed['capability']; desktopImageSha256 = $installed['desktop']
        helperImageSha256 = $installed['helper']; signerThumbprint = $ExpectedSignerThumbprint; signerNotAfterUtc = $notAfter.Replace('Z', '.000Z')
    }
    'C1 PINS-OK ledger=' + $ledgerSha256 + ' manifest=' + $ExpectedManifestSha256 + ' desktop=' + $installed['desktop'] + ' helper=' + $installed['helper'] +
        ' capability=' + $installed['capability'] + ' signer=' + $ExpectedSignerThumbprint + ' notAfter=' + $notAfter + ' probeBuild=true commit=' + $buildCommit

    # ---- C2 baseline ----
    $baseline = Get-ProbeObservation $database $dataRoot $chocolateyRoot $resultFolder
    if ($baseline.ChocoProcesses -ne 0 -or $baseline.PcStewardProcesses -ne 0) { Stop-ProbeRun 'baseline (a choco or PCSteward process is running)' }
    'C2 BASELINE: db=' + $baseline.Database + ' data=' + $baseline.Data + ' choco=' + $baseline.Chocolatey + ' chocoFiles=' + $baseline.ChocolateyFiles + ' chocoProcess=0'
    if ($Mode -ceq 'Check') { 'CHECK-COMPLETE: nothing was launched'; return }

    # ---- C3 probe (one UAC prompt) ----
    'C3 LAUNCH: expect exactly one UAC prompt, for PCSteward.ElevatedHelper.exe, whose details show --kind qualify-chocolatey-helper right after the program path. Answer Yes only to that prompt; answer No to anything else.'
    $process = Start-Process -FilePath $desktop -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru
    $exitCode = $process.ExitCode
    $created = Test-Path -LiteralPath $resultFolder -PathType Container
    'C3 EXIT: ' + $exitCode + ' RESULT: ' + $(if (Test-Path -LiteralPath $resultFile -PathType Leaf) { 'present' } else { 'absent' })

    # ---- C4 after ----
    $after = $null
    try { $after = Get-ProbeObservation $database $dataRoot $chocolateyRoot $resultFolder }
    catch { 'C4 AFTER-UNAVAILABLE: ' + (Get-ProbeSafeMessage ([string]$_.Exception.Message) '\ASTOP: [A-Za-z0-9 ().;,=-]+\z' 'unreadable').Replace('STOP: ', '') }
    if ($null -ne $after) {
        'C4 UNCHANGED: db=' + ($after.Database -ceq $baseline.Database) + ' data=' + ($after.Data -ceq $baseline.Data) + ' choco=' + ($after.Chocolatey -ceq $baseline.Chocolatey) +
            ' chocoProcess=' + $after.ChocoProcesses + ' pcstewardProcess=' + $after.PcStewardProcesses + ' onlyResultFolderNew=' + $after.OnlyResultFolderNew
    }

    # ---- C5 evidence ----
    $resultState = 'absent'
    $result = $null
    if ($created -and (Test-ProbeResultFolderRemovable $resultFolder) -ceq 'removable' -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        $length = ([IO.FileInfo]::new($resultFile)).Length
        if ($length -gt $contract['MaximumBytes']) { $resultState = 'malformed'; 'C5 RESULT-REJECTED: result-size' }
        else {
            $bytes = [IO.File]::ReadAllBytes($resultFile)
            try { $result = Test-ProbeResultDocument $bytes; $resultState = 'valid'; 'C5 RESULT: ' + [Text.Encoding]::ASCII.GetString($bytes) }
            catch { $resultState = 'malformed'; 'C5 RESULT-REJECTED: ' + (Get-ProbeSafeMessage ([string]$_.Exception.Message) '\Aresult-[A-Za-z0-9-]+\z' 'result-unreadable') }
        }
    }
    elseif ($created) { $resultState = 'malformed'; 'C5 RESULT-REJECTED: result-folder-content' }
    else { 'C5 RESULT-ABSENT' }

    # ---- C6 interpretation ----
    Format-ProbeVerdicts (Get-ProbeVerdicts $resultState $result $pins $baseline $after)

    # ---- C7 cleanup ----
    if (!$created) { 'C7 CLEANUP-NOT-NEEDED'; return }
    $removable = Test-ProbeResultFolderRemovable $resultFolder
    if ($removable -cne 'removable') { Stop-ProbeRun ('cleanup refused (' + $removable + ')') }
    if (Test-Path -LiteralPath $resultFile -PathType Leaf) { [IO.File]::Delete($resultFile) }
    [IO.Directory]::Delete($resultFolder, $false)
    if (Test-Path -LiteralPath $resultFolder) { Stop-ProbeRun 'cleanup incomplete' }
    'C7 CLEANUP-COMPLETE'
}

try {
    Invoke-ProbeHostRun
    exit 0
}
catch {
    # Only fixed STOP tokens are printed; any other failure is reported without its text (it could hold a path or a name).
    Get-ProbeSafeMessage ([string]$_.Exception.Message) '\ASTOP: [A-Za-z0-9 ().;,=-]+\z' 'STOP: internal failure (no detail is printed)'
    exit 1
}
