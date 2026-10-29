# Task 90 — repair Task 49 machine-provider ownership loss

Date: 2026-10-29. Reported by the owner's GitHub Codex review; exact comment/PR URL was not supplied. Baseline `d28d69d`, implementation source `cfeb0ef`. No GitHub comment or review state was changed by this remediation.

## Finding and corrected assessment

**High / owned-work admission defect (`FR-UPD-14`, retained `FR-UPD-03/09`, `NFR-OBS-01–05`).** `WingetMachineUpdateExecutor` projected `WingetProcessResult` to scalar `ProviderProcessResult` without awaiting `OwnedCompletion`. After bounded teardown returned with pending provider-owned exit/I/O, the helper could return a response and exit. Desktop `HelperProcessWork` then observed helper exit and released admission despite the helper's unresolved provider-owned work. The user route already retained its cleanup root.

This is a local owned-work defect, not merely a disclosed limitation about external installers or restart-surviving barriers. The prior Task 49 completion and scoped review missed it; their PASS reports are historical and superseded for this finding. Task 49 completion is reopened until the correction and fresh independent repair review pass. The reviewer's recommendation is implemented without serializing a Task or changing helper protocol 1.

## Smallest repair

The explicit protected machine executor now awaits and observes the existing provider `OwnedCompletion`, when present, **before** returning its scalar result. It does not pass the operation cancellation token to this ownership wait, add another timeout or detach/abandon the task. The production helper already awaits that protected execution before mapping/writing its response and returning; no helper entry-point refactor is needed. Thus normal helper exit cannot be caused by this projection while its provider cleanup root is pending. Desktop's existing helper-exit ownership retains the local admission hold.

Absent/already-completed ownership preserves the existing fast path and scalar fields. Faulted/cancelled completion is observed and propagates to the existing helper failure/cancellation handling only after that completion has settled. The original uncertain result stays uncertain after cleanup; no later cleanup invents a successful installer outcome.

No public interface/type/schema, wire bytes, request authentication/validation, command arguments, provider selection, operation deadline or Task 50/52 authority changes. A one-shot helper may remain alive while unresolved owned work remains pending, including after client disconnect/deadline; this is ownership retention, not a new request-accepting service. Forced external process termination, external installer quiescence and restart/cross-process admission remain separate limitations. No live provider, app/helper launch, UAC, deployment, signing, OS-record read, package/network action or user-data mutation was performed.

## Regression evidence

`MachineProviderOwnershipTests` invokes the actual protected machine WinGet executor with a synthetic `IWingetProcess` result. It covers:

- Pending provider completion prevents scalar projection, including after caller-token cancellation.
- A helper-lifetime composition that waits on the actual protected operation cannot signal helper exit or release production `HelperProcessWork`/`UpdateExecutionLifetime` admission until owned completion is released. The composition is synthetic; it does not launch the privileged entry point or prove endpoint authentication.
- Faulted/cancelled completion is observed before a projected result can be produced.
- Null/already-completed roots preserve existing bounded scalar evidence and add no Task field.

Initial test compile hit CA1859 in the fixture factory; this was corrected by returning its concrete implementation and explicitly exercising the protected interface. That analyzer failure is not behavioral red.

Behavioral red at unfixed source: **4 failed / 2 passed / 0 skipped**, exit 1. Assertions showed early projection, early simulated helper exit and ignored pending fault/cancellation roots. `%TEMP%\pc-steward-task90-machine-ownership-red.log`.

After the four-line production correction, the new regressions plus existing helper-ownership and protocol-1 compatibility cases pass **40/40**, zero skipped, exit 0. `%TEMP%\pc-steward-task90-machine-ownership-green.log`. Canonical build/full-suite and fresh independent repair review results are recorded below when complete.

Host: Windows PowerShell 5.1, command-only approved SDK 10.0.403 PATH/DOTNET_ROOT, .NET 10.0.14. Focused command:

```powershell
dotnet test tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~MachineProviderOwnershipTests|FullyQualifiedName~HelperProcessOwnershipTests|FullyQualifiedName~Task49ProtocolCompatibilityTests'
```

Unchanged canonical `scripts/Build.ps1` and `scripts/Test.ps1` run under the same documented Windows PowerShell equivalent; no dependency/SDK install is needed.

## Review and completion state

Fresh independent Sol High Fast repair review is pending. The finding must not be marked closed from tests alone. Fixing source commit and final verification will be attached after the green repair is committed and reviewed. Task 50 remains unstarted; host/readiness/distribution gates stay open.

### Canonical verification of repair candidate

Unchanged `scripts/Build.ps1`: exit 0, Release zero warnings/errors. `%TEMP%\pc-steward-task90-machine-ownership-build.log`.
Unchanged `scripts/Test.ps1`: exit 0, **413/413**, zero skipped (59 Domain, 250 Application, 30 Architecture, 74 Security). `%TEMP%\pc-steward-task90-machine-ownership-test.log`.
`git diff --check` passes. The production diff is exactly the four-line awaited-ownership guard/comment inside the protected WinGet projection; six synthetic cases add the regression evidence. No protocol or canonical script changes. Fresh independent review remains pending at candidate commit time.