# Task 49 — Q3 implementation agent evidence

## Outcome and scope

Implemented the accepted Q3 bounded package-derived running-application heuristic with actual coverage disclosure. Requirements: **FR-UPD-14**, retained **FR-UPD-01** (existing review/acknowledgement contracts) and **FR-UPD-11** (cancellation); no additional operation/provider authority. Read AGENTS, STATUS, Task 49 prompt, Phase C/shared contract, accepted Q2/Q3 planning decision and parent's extraction/interface record. Authoritative FR-UPD-14/01/11 text was checked directly. Ownership checkpoint supplied by main: `607271d`; parent/other workers' uncommitted changes preserved.

Exclusive files changed:

- `src/PCSteward.Application/RunningApplicationDetection.cs`
- `tests/PCSteward.Application.IntegrationTests/RunningApplicationCoverageTests.cs`
- `docs/review/TASK_49_Q3_AGENT.md` (this report)

No common status, traceability, dialogue, workflow, Desktop or other worker file edited. No commit, restore, packages/network, real provider, app launch/scan, elevation or live-data action.

## API and consumer handoff

Existing public default detector constructor, two-argument `RunningApplicationDetection` record constructor, `NotDetected`, interface, and internal `Func<IReadOnlyCollection<string>>` test seam remain supported. The record exposes `CoverageDisclosure` as an instance string, computed from optional typed `Coverage`; legacy results explicitly say counts are unavailable. Main appends this string to backend late warnings; UI worker appends it to review disclosures. No consumer edits are required for the new evidence types.

New public data-only `RunningApplicationCoverage` records inspected/readable/inaccessible/exited/unavailable/invalid counters, inspection-limit reached, and process enumeration unavailable. Internal `RunningProcessObservation`/kind and `RunningApplicationSnapshot` separate synthetic/production observations from bounded capture/matching. New internal `Func<CancellationToken, IEnumerable<RunningProcessObservation>>` constructor provides deterministic fault/race/boundary/cancellation fixtures without changing or ambiguating existing zero-argument lambda calls.

Capture reads at most 4,096 entries, retains at most 4,096 names of at most 64 characters, validates name characters without truncating, derives at most two candidates and preserves ambiguous suffix suppression, exact case-insensitive matching, distinct matches and sorted output. Exactly 4,096 is conservatively marked limit-reached; a 4,097th `MoveNext` is never performed just to discover exhaustion. A process enumeration failure sets a separate unavailable flag rather than inventing an inspected entry. All disclosure variants retain inaccessible/exited/invalid/saturation and no-match/race-gap limitations. Every check requests a fresh snapshot. Pre-cancellation requests none; mid-cancellation stops matching/reading and disposes the enumerator.

Production observes only `ProcessName`, translating Win32/invalid-operation/not-supported failures to typed evidence. Iterator ownership surrounds creation/read/disposal; every returned Process object, including uninspected objects and cancellation paths, is disposed in `finally`. No process close/kill or elevation path exists.

## Commands and measured results

Every dotnet test used this synchronous Windows PowerShell wrapper (SDK verified **10.0.403**, installed runtime **10.0.14**):

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$m = [System.Threading.Mutex]::new($false, 'Local\PCSteward.Task49.Build')
$held = $false
try {
    try { $held = $m.WaitOne(60000) }
    catch [System.Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Task49 mutex not acquired' }
    # Commands below: stdout/stderr redirected to unique TEMP logs.
    dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter '<filter>' *> $log
    $result = $LASTEXITCODE
    Get-Content $log -Tail 30
    exit $result
} finally {
    if ($held) { $m.ReleaseMutex() }
    $m.Dispose()
}
```

| Stage / filter | Result | Unique log under `%TEMP%` |
|---|---|---|
| Original behavioral red: `FullyQualifiedName~RunningApplicationCoverageTests` | Compiled, 1 failed; actual no-match disclosure lacked `Inspected 2 process entries` and invalid-name coverage. Exit 1. | `pc-steward-task49-q3-red-d42daee484fe4b9ea8a972a892de4cb7.log` |
| Initial green attempt: coverage + two existing detector tests | Test analyzer CA1861 at test line 103; fixed with static readonly expected names. This is **not** behavioral red. Exit 1. | `pc-steward-task49-q3-green-9b2c70342d1949c49c22dc6df87ef32f.log` |
| Coverage + two existing detector tests | 21/21 pass, zero skipped, exit 0. | `pc-steward-task49-q3-green-df17ac12ecd34842ad7ef898df13dded.log` |
| Additional enumeration behavioral red: `FullyQualifiedName~EnumerationFailureReportsUnknownCoverage` | Compiled typed scaffold without enumeration-failure accounting; 1 failed, expected inspected 0 versus actual 1. Exit 1. | `pc-steward-task49-q3-enumeration-red-cd9dfabf34524211a548ee58bc2b0cb9.log` |
| Final green: `FullyQualifiedName~RunningApplicationCoverageTests\|FullyQualifiedName~SequentialUpdateBatchTests\|FullyQualifiedName~ReviewedUpdateExecutionWorkflowTests` | 41/41 pass, zero skipped, exit 0; 20 new coverage cases and 21 existing workflow/batch cases. Release dependency/test compilation emitted no warnings/errors. | `pc-steward-task49-q3-final-81ce1e01cf1141d985276b5891bfc0e9.log` |

New synthetic cases cover actual invalid-name/no-match disclosure; inaccessible/exited/unavailable observations; enumeration failure; 4,096/4,097 saturation and no overread; enumerator disposal; late-start snapshots; all nine ambiguous suffixes versus full identifier matches; exact/case-insensitive/distinct/sorted matching; 64/65-character bounds and invalid/control/empty names; pre/mid-snapshot cancellation; legacy construction. Existing workflow tests preserve review/late warning/explicit acknowledgement and sequential/cancel behavior. Tests never instantiate the default detector or inspect host processes.

## Limits and integration responsibilities

- `Process.GetProcesses()` still materializes native OS entries before bounded name inspection; its allocation/native enumeration and individual `ProcessName` read latency are not claimed bounded or cancellable. Disposal must walk all returned objects. This preserves the existing supported API without introducing native process traversal or new packages; name capture/matching itself is bounded. The production handle-disposal path is source-reviewed; synthetic enumerator disposal is tested, not actual host Process handles.
- Native failures are represented by the typed synthetic seam; no real access-denied/exited host process was probed. The heuristic can miss other executable names, generic suffixes, inaccessible/exited/invalid entries or saturation; no match proves nothing about closure, and another process can start after the snapshot.
- Main owns backend/UI composition review, shared STATUS/TRACEABILITY, canonical Release build and full suite. This report proves only the scoped component/focused synthetic fixtures, not complete Task 49 or live UI/provider/host safety gates.
- No unresolved implementation blocker or consultation needed. AGENTS' owner clarification was read: any future stuck work routes through main, with main maintaining the council/failure records.
- No commit by explicit subagent scope; parent reviews/integrates.
