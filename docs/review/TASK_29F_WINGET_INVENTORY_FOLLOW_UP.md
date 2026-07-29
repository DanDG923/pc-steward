# Task 29F — WinGet inventory scan follow-up

## Trigger

The recorded host completed a new scan with the readable local completion time from Task 29E, but the desktop still displayed the generic `WinGet installed inventory:failed` warning.

## Remediation

- The WinGet display-inventory boundary now catches a launch/read fault and returns a bounded local category: executable access denied, executable unavailable, executable launch failed, or runtime error. Exception text is not surfaced.
- Each parsed installed record is independently mapped. A malformed record is skipped with `winget:invalid-installed-record`; already accepted rows remain available to the scan.
- Cancellation still propagates, so the orchestrator keeps its existing timeout and close semantics.

## Verification

- Focused offline WinGet provider tests: 16 passed.
- Release build: 0 warnings, 0 errors.
- Full offline suite: 197 passed.

## Recorded-host follow-up

The user must start a fresh scan from the rebuilt Release desktop. This executes the same disclosure-gated, read-only WinGet list request and may send installed package identifiers to enabled package sources. The result should either show the managed inventory or a specific bounded category rather than a generic provider failure. No performance pass claim is made; `PO-EXC-29-PERF-01` remains unchanged.
