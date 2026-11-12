# Stage C4 — reviewed installer mode for per-user WinGet updates (Task 54 R1) and the protocol-1 transition (R2)

Requirements: `FR-UPD-15`, `FR-UPD-01/03`, `AC-UPD-03`, `TM-03/04/13`. Authority: [Task 54 owner decision](TASK_54_PHASE_C_REVIEW_GATE.md#owner-decision-2026-11-12) ("Fix R1 now; amend ADR-025 for R2") and the owner's 2026-11-12 C4 decision "Silent for all per-user apps". Code, tests and docs only; no host action, deployment or release.

## 1. What changed

**R1: the desktop's per-user WinGet updates now run only on a reviewed operation with Silent bound at review.**

- New route contract `UpdateRouteContract.WinGetUserSilentV1` (appended; no existing value is reinterpreted). The `ReviewedUpdateOperation` constructor and `TryRestore` refuse it unless the shape is schema 3, WinGet, source `winget` or `msstore`, User scope, `Selected(Silent)` and an Observed architecture. `ReviewedUpdateOperation.BindsJob(job)` checks that a frozen operation was made from exactly that job (IDs, approval time, valid digest, same selection).
- `UpdateInstallerModeCapabilities.Resolve` now returns `Selected(Silent)` for WinGet user scope from `winget` or `msstore`. `ReviewedUpdateReview.HasSupportedMode` admits the new route (its exact text is pinned by `ChocolateyRouteActivationBoundaryTests`), and its `WinGetUserExactV3` clause now names the `winget` source explicitly so that route does not widen to `msstore`.
- `WingetUserReviewedRoute` (Application) binds the mode at review (`BindMode`), freezes the approved job into the reviewed operation (`Freeze`) and is the one admission check (`Admits`) shared by the workflow, the runner and the provider.
- `ReviewedUpdateExecutionWorkflow`: a per-user review records the bound mode on `UpdateJobReview.InstallerMode`. On Confirm the mode is checked again against the fresh re-preparation before approval; the requesting user's SID must be readable before approval; after approval the job is frozen with the mode and passed on `UpdateBatchItem.Reviewed`. If any step fails, nothing is approved or started. Chocolatey is refused on this route (it has its own Confirm screens).
- `SequentialUpdateBatchCoordinator` refuses an item whose reviewed operation does not bind its job. `ApprovedUpdateExecutor` passes the operation to `NonElevatedUpdateRunner`, which now takes only `IWingetReviewedUserUpdateExecutor` and denies (`ReviewedModeUnavailable`) a user job without an admitted, bound operation before any provider call.
- `WingetReviewedUserUpdateExecutor` builds its arguments only from `WingetReviewedUpdateArguments.Build(operation)`. The legacy mode-less `WingetUpdateExecutor` and its `BuildUpdateArguments(ImmutableUpdateJob)` are removed.
- `App.xaml.cs` composes `new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor())` and passes `requestingUserSid: CurrentUserSid`.

**R2 (transition, pending the owner's acceptance of the wording):** [ADR-025 proposed amendment](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md#proposed-amendment-2026-11-12--protocol-1-transition-for-machine-scope-winget-stage-c4-task-54-r2). This stage already narrows protocol 1 to machine-scope WinGet: the protocol-1 request validator refuses Chocolatey (`request-authority-not-allowlisted`), the helper's protocol-1 executor has no Chocolatey arm, and the desktop's `ElevatedUpdateRunner` denies any non-WinGet job (`UnsupportedProvider`) before a helper launch. Real Chocolatey updates are unaffected: they run on protocol 2 through their own reviewed dispatch.

## 2. The per-user mapping (Task 50 F6 amendment)

```
upgrade --id <id> --version <target> --source <winget|msstore> --exact --scope user --architecture <x86|x64|arm64|neutral> --disable-interactivity --silent
```

Compared with the removed legacy builder, the only argument added is `--silent`. No channel or installer-type argument is ever added. See the [Task 50 mapping amendment](TASK_50_PROVIDER_MAPPING_V2.md#amendment-2026-11-12--stage-c4-per-user-silent-route).

## 3. Disclosed limits (not claimed)

- **Coverage over native qualification (owner's choice).** The owner chose Silent for every per-user WinGet app over the narrower MSIX-only `WinGetUserExactV3` route. "Observed" architecture here is the architecture the desktop's WinGet inventory reported for the installed app, not the F6 native current-user MSIX observation. F6's native qualification and F7's CLI/COM implementation-identity binding are not done for this route.
- **`--silent` is a request, not proof.** Whether an installer honours it depends on the package manifest. Actual mode enforcement and installed-state outcomes through a real update still need host evidence (ADR-025 "Follow-up controls and tests"; Task 53 O1).
- **The review screen does not show the mode in words.** The mode is bound and enforced, but the existing WinGet review dialog is unchanged.
- **Machine scope** still runs on protocol 1 with no reviewed or bound mode and no `--silent` (`upgrade … --scope machine --architecture <a> --disable-interactivity`) until Phase D stage D4b ships the reviewed machine route, under the proposed ADR-025 amendment.
- **Not compiled in the cloud:** the WinUI Desktop project and the integration tests that need SQLite. Windows CI is the proof for those.

## 4. Evidence

- Application `WingetUserReviewedRouteTests`: per-user binding and dispatch for `winget` and `msstore`; machine scope binds no mode; Chocolatey refused; missing or other mode refused before approval; no requesting user means nothing approved or started; `BindMode` coverage; mapping goldens for x86, x64, arm64 and neutral; recomputed-digest mutations refused by `TryRestore`; `Freeze` refusals.
- Architecture `WingetUserReviewedRouteCompositionTests`: the desktop composes only the reviewed executor (fails if `WingetUpdateExecutor` or `BuildUpdateArguments` appears in `App.xaml.cs`); no `src` file names the legacy executor and the one remaining job-based builder is the machine one; the runner checks the bound operation before the provider; no built `src` assembly defines or references the legacy type.
- Security fixtures: `UpdateRunnerSecurityTests.UserJobWithoutItsReviewedOperationNeverReachesTheProvider`, `ReviewedUserExecutorRefusesAnOperationOutsideThePerUserRoute`, golden arguments now ending `--silent`; `ElevatedHelperSecurityTests.ProtocolOneRefusesChocolateyRequests` and the Chocolatey `ElevatedUpdateRunner` case (Denied, zero helper launches).
- Cloud run (Linux, csc with the repository analyzers, warnings as errors): Domain, Application, both providers and Execution compile; Application subset 99/99; Security and Architecture subsets fail only the same Windows-only tests as the unchanged base. Full proof is Windows CI on the PR.
