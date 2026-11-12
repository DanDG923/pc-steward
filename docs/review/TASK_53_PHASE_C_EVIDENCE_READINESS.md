# Task 53 — Phase C evidence readiness report

Phase C stage C3 (owner-agreed plan, 2026-11-12). Prompt: [53](../../prompts/53_UPDATER_EVIDENCE_READINESS.md). Phase brief: [Phase C](../requirements/V2_PHASE_C.md). Requirements: `FR-UPD-01–15`, `AC-UPD-01–11`, `FR-HIS-01–06`, `NFR-PERF-01–10`, `NFR-REL-01–05`.

**Outcome: ready for the independent Task 54 review, with the open items in §9.** Every mandatory updater criterion is mapped to a test, a CI result or a host record below. Nothing is marked as passed without one. Where only fixture evidence exists, the row says so, and where nothing exists, the row is open.

## 1. Snapshot

| Item | Identity |
|---|---|
| Implementation snapshot reviewed | `phase/4-beta` at `1530e9d` plus this PR's code commits, ending at **`4e3f94a`**. The later commits in this PR change only `docs/`. |
| Code change in this PR | Third-party notices in the release payload (§7.3) and a CI step that runs the fixture benchmark (§6). No product (`src/`) change. |
| Dirty state | None. The CI runs below build a clean checkout of the commit. |
| Windows CI on the snapshot | Run [43289637420](https://github.com/DanDG923/pc-steward/actions/runs/43289637420), head `4e3f94a`: all 10 jobs succeeded (`build-and-test`, `desktop-launch-smoke`, `msi-install-test`, `owner-only-candidate-builder`, `owner-only-signing-diagnostic`, `native-helper-signing-diagnostic`, `helper-native-aot-feasibility`, `owner-build-toolchain-check`, `package-licence-map`, `task50-cleanup-paste`). The run built the pull-request merge commit `a389940` (snapshot on `phase/4-beta`). Release build 0 warnings, SDK 10.0.404. Tests: Domain 192/192, Architecture 226/226, Application integration 1270/1270, Security fixtures 1788 passed + 1 skipped of 1789 (the skip is `WindowsCanonicalisesAShortNameTempPath`, inconclusive because 8.3 short names are disabled on the runner volume). Both `ThirdPartyNoticesTests` rows (PowerShell 7 and Windows PowerShell 5.1) ran and passed. |
| Newest installed payload | 0.1.3-private-beta, source `e20c70d`, installed through the in-app updater on the owner's PC on 2026-11-12 ([record](TASK_53A_DEPENDENCY_SERVICING.md#release-013-on-the-owners-pc-2026-11-12)). `git diff e20c70d 4e3f94a -- src` is empty: the installed app's code is the reviewed code. Its payload lacks only the notices file (§7.3). |

## 2. Entry conditions

| Condition | State |
|---|---|
| Tasks 49–52 committed | Yes. 49: [record](TASK_49_IMPLEMENTATION.md). 50: [handover](TASK_50_HANDOVER_2026-11-06.md), probe re-run and the first real Chocolatey update ([G9-T1](TASK_50_G9_T1_RIPGREP_TEST.md)). 51 and 52: [record](TASK_51_52_RESTART_OUTCOMES_AND_BATCH_AUDIT.md), PR #63 (`92c735e`). |
| Task 53A committed | Yes, PR #65 (`40181ea`), shipped in 0.1.3. |
| Task 54B (projection change) committed | Yes, PR #66 (`e20c70d`). The phase brief and ADR-003 amendment 1 require this report and the Task 54 review to cover it, which they do. |
| WS5 decision recorded | Yes, [ADR-026](../adr/ADR-026-WS5-AUTOMATION-AUTHORIZATION-DECISION.md), Option C (Defer), owner-accepted 2026-10-29. See §8. |
| 53A delta and packaging evidence | Yes: [53A record](TASK_53A_DEPENDENCY_SERVICING.md), CI job `package-licence-map`. |

## 3. What Phase C changed

| Task | Gap closed | Record |
|---|---|---|
| 49 | G1 truthful progress: review, queued/preparing, observable phases, verification and result are distinct; provider detail that cannot be observed is shown as indeterminate, never as a percentage. | [49](TASK_49_IMPLEMENTATION.md), [independent review](TASK_49_INDEPENDENT_REVIEW.md) |
| 50 | G2 enforceable reviewed mode: typed, immutable mode in the reviewed operation, revalidated by runner and helper; protocol 2; Chocolatey route under its reviewed profile; Native AOT helper (ADR-033). **Closed for the Chocolatey route only**: the WinGet routes are not composed onto it (R1), and the helper still accepts protocol 1 (R2). | [handover](TASK_50_HANDOVER_2026-11-06.md), [G9-T1](TASK_50_G9_T1_RIPGREP_TEST.md) |
| 51 | G3 reliable restart: WinGet's typed restart exit codes reach history as Restart required, never Success. | [51/52](TASK_51_52_RESTART_OUTCOMES_AND_BATCH_AUDIT.md) |
| 52 | G4 frozen Update all: audit of every prompt case; three gaps fixed. | [51/52](TASK_51_52_RESTART_OUTCOMES_AND_BATCH_AUDIT.md) |
| 53A | Windows App SDK 1.8.260921001 servicing. | [53A](TASK_53A_DEPENDENCY_SERVICING.md) |
| 54B | WinGet COM 1.29.380, 26100 target with a 22000 minimum. | [54B](TASK_54B_WINGET_COM_1_29_ADOPTION.md) |
| C3 | Third-party notices in the release payload (this PR). | §7.3 |

## 4. Requirement map

Evidence kinds: **CI** = an automated test that runs in Windows CI on the snapshot; **Host** = an observation on the owner's PC, with the payload it was made on; **Open** = unproven, with the blocker in §9.

### Updates (`FR-UPD`, `AC-UPD`)

| Requirement | Evidence | Kind |
|---|---|---|
| FR-UPD-01, AC-UPD-01: explicit selection and confirmation | `ReviewedUpdateReviewTests`, `ReviewedUpdateDraftTests`, `UpdateBatchWorkflowTests` (confirmation binds the exact saved draft; a changed item is not started). G9-T1: one Confirm press on the real review. | CI; Host (Chocolatey, 2026-11-11, build `d9a754b`) |
| FR-UPD-02/03, AC-UPD-02: structured operations only, no command strings | `WingetReviewedUpdateArgumentsTests`, `ChocolateyReviewedUpdateArgumentsTests`, `ProcessBoundaryHasNoExecutablePathParameterAndOversizedOutputFailsClosed`, `ReviewedUpdateWireCodecTests` | CI |
| FR-UPD-04, AC-UPD-03/04: elevation only when needed, only at execution | `CurrentEvidenceMismatchAndMachineScopeNeverReachTheNonElevatedProcess`, `DesktopComposesProtectedMachineUpdatesOnlyThroughTheExistingElevatedHelper`, `Task49MilestoneTests` (ElevationRequested follows final trust). G9-T1: one UAC prompt, at Confirm. | CI; Host (machine scope, Chocolatey, old build). A user-scope update without UAC on a current build is **Open (O1)**. |
| AC-UPD-05: declined UAC is cancelled/denied | `ADeclinedPermissionPromptForTheFirstAppLetsTheLaterAppsRun`; Task 90 dispatch remediation (ElevationDenied). | CI only; **Open (O1)** on the host |
| FR-UPD-05/06: running-app warning; no forced close | `ATargetThatStartsRunningAfterReview…`, `ANewRunningProcessAtTheItemsTurnNeedsANewAcknowledgement`, `TheCoordinatorsLastCheckAlsoNeedsTheWarnedProcesses`, `RunningApplicationCoverageTests` (Q3 coverage disclosure). No code path closes an app. | CI |
| FR-UPD-07, AC-UPD-11: job record fields | `ReviewedUpdateHistoryTests`, `SqliteDatabaseTests.UpdateHistoryIsAppendOnlyRecoverableRetainedAndIncludedInTheProductTimeline` | CI |
| FR-UPD-08/09, AC-UPD-06/07/08: re-inventory; success only on the expected version or a reliable restart | `ExactPostUpdateInventoryRejectsWrongVersion…`, `ExactPostUpdateQueryRequiresOneExpectedPackageAndVersion`, `VerificationIsTheOnlyPathToSuccessAndBatchRemainsSequential`, `ATypedProviderRestartIsRecordedAsRestartRequiredNeverVerified`, the `…NeverSuccess` family (`AMissingOrUnreadablePostcheckIsUnverifiedNeverSuccess`, `ARestartPendingVerificationIsUnverifiedNeverSuccess`, …). G9-T1 post-check found 15.2.0. | CI; Host (old build). Known limit **L1** (MSI 3010). |
| FR-UPD-10, AC-UPD-09: sequential batch, independent results | `ItemsRunOneAtATimeAndAChangedItemIsNotStarted`, `UpdateAllRunsSequentiallyThroughThePerAppPathAndStopsOnlyBetweenApps`, `EachItemHasItsOwnHonestOutcome…` | CI |
| FR-UPD-11, AC-UPD-10: cancel before the next item; keep completed results | `StopIsReadBetweenItemsOnly…`, `CancellationStopsOnlyUnstartedItems…`, `CancellationBeforeTheProviderStartsNothingAndSaysSo`, `CancellationAfterProviderCompletionRemainsVerificationRequired` | CI |
| FR-UPD-12: unattended rules | Later. Deferred by ADR-026 (§8). | Not in scope |
| FR-UPD-13: direct vendor installers | Later. Not built. | Not in scope |
| FR-UPD-14: truthful progress | `UpdateProgressTests`, `UpdateProgressIntegrationTests`, `UpdateProgressPresentationTests` (no percentages, terminal only after the durable record) | CI. Real WinUI frames on the current build: **Open (O3)**. |
| FR-UPD-15: typed reviewed mode | Chocolatey route: `ReviewedInstallerMode` is closed data and the route records NotControllable (`ProviderScriptControlled`); `FinalizedDispatchKeepsTheReviewedModeAndVerifiedOutcome`, `ARecomputedUnionModeOrProviderChangeIsRefusedByEveryCommand`; protocol-2 interpreter tests. WinGet routes: **not enforced** (Task 54 finding, **R1**). The desktop's Updates page runs WinGet updates through `ReviewedUpdateExecutionWorkflow` → `ApprovedUpdateExecutor` with the legacy `ImmutableUpdateJob`, which has no mode field; user scope uses `WingetUpdateExecutor.BuildUpdateArguments` (no `--silent`), machine scope uses protocol 1. `WingetReviewedUpdateArguments.Build` (the Silent/MSIX mapping that `ExactUserMappingPinsSilentModeAndMsixInstallerType` tests) has no production caller. | Chocolatey: CI; Host (old build). WinGet: **Gap R1**. |

### History (`FR-HIS`)

| Requirement | Evidence | Kind |
|---|---|---|
| FR-HIS-01: local scan and job history | `SqliteDatabaseTests.UpdateHistoryIsAppendOnlyRecoverable…`, `ReviewedUpdateHistoryTests` | CI |
| FR-HIS-02: per-product timeline | `…IncludedInTheProductTimeline`, `AppDetailsAreStructuredAndTheTimelineIsInWords` | CI |
| FR-HIS-03: distinct failure kinds | `UpdatesAndJobPresentationTests` (source, download, installer, elevation, timeout, verification; Task 51 adds "restart needed before install") | CI |
| FR-HIS-04: retention limits | `SqliteDatabaseTests.UpdateHistoryIsAppendOnlyRecoverableRetained…` (`UpdateHistoryRetention` prune), `UpdateSourceRefreshServiceTests` (source cache retention, 1–90 days), `DownloadedArtifactCacheTests` (quota and eviction) | CI |
| FR-HIS-05/06: sanitized diagnostic bundle, no secrets | `CopyDiagnosticUsesTheSanitizedBundle`, `DefaultAndTypedSettingsSourcesAreCopiedWithoutRetainingCredentials`, `ScanDiagnosticsTests` | CI |

### Reliability (`NFR-REL`)

| Requirement | Evidence | Kind |
|---|---|---|
| NFR-REL-01: one connector failing keeps the others | `UpdateSourceRefreshServiceTests`, `NoCacheFailureIsUnavailableAndConnectorConcurrencyIsBounded` | CI |
| NFR-REL-02: transactional writes | `MigrationAndPromotionPreserveLastKnownGoodSnapshotWhenReplacementFails`, `ClearScanDataIsTransactional…`, `FailedClaimTransactionConsumesApprovalAndRollsBackReservation` | CI |
| NFR-REL-03: crashed or cancelled batch keeps results | `UpdateHistoryIsAppendOnlyRecoverable…` (an interrupted `Running` row is recovered as not verified); a batch is never stored, so nothing replays | CI |
| NFR-REL-04: cached results show their time | `HealthyRefreshPersistsLiveSourceAndOfflineModeUsesClearlyCachedData` | CI |
| NFR-REL-05: malformed registry values | `RegistryInventoryProviderTests`, `MalformedSnapshotIdentifiersAreSkippedInsteadOfCrashingStartupReads` | CI |

### Performance (`NFR-PERF`)

See §6 for the numbers.

| Requirement | Evidence | Kind |
|---|---|---|
| NFR-PERF-01/02 (AC-NFR-01/02) | Fixture harness, 30 and 20 runs on the hosted runner. | CI fixture only; the baseline PC: **Open (O2)** |
| NFR-PERF-03, -04, -05, -06, -07 (AC-NFR-06/07/11) | Never measured as process-tree resources on the baseline PC. `ScheduledHost` has no route to Execution or the helper (`ScheduledHostDoesNotReferenceExecutionOrElevatedHelper`). | **Open (O2)** |
| NFR-PERF-08 | `InventoryScanOrchestratorTests.CancellationDoesNotReplaceLastKnownGoodInventory` | CI fixture; UI timing **Open (O2)** |
| NFR-PERF-09 | `ConcurrentScanIsRejectedAndMeasuredProviderConcurrencyIsOne`, `NoCacheFailureIsUnavailableAndConnectorConcurrencyIsBounded` | CI |
| NFR-PERF-10 | `QuotaEvictsOldestInactiveArtifactButNeverAnActiveArtifact`, `RealByteQuotaHonoursFramePendingAndCumulativeExactBoundaries` | CI |

`AC-UX-01–09` stay Optional (ADR-021) and are not part of this gate.

## 5. Mandatory verification cases

| Case | Test | Result on the snapshot |
|---|---|---|
| F8 (source consent pair) regression | `AsymmetricTrustedSourceConsentLoadsFailClosed`, `ScheduledHostUsesTheUnifiedFailClosedTrustedSourceConsent`, `BothWinGetConnectorDisclosuresPersistAndFailClosedWhenRevoked` | Pass (CI) |
| F10 (malformed WinGet output) regression | `ShortMalformedUpdateOutputCannotProduceALiveSnapshot` | Pass (CI) |
| Old and new protocol rejection | `ExplicitFifteenFieldLegacyProtocol2ResponseIsRejected`, `ProtocolTwoIsDispatchedAndProtocolOneIsUnchanged`, `ProtocolTwoTransportAndDispatcherAreNotComposedAndNeverFallBackToProtocolOne`, `AMalformedProtocol2LineExitsWith…`, Task 49's 30 golden/schema cases. These prove that protocol 2 never falls back to protocol 1 and that the protocol-1 bootstrap refuses protocol 2. They do not prove the reverse: the shipped helper still runs protocol 1 (`ElevatedHelperEntryPoint.RunAsync` → `RunProtocol1Async`, with the WinGet and Chocolatey machine executors), and the desktop still uses it for machine-scope WinGet updates (`ElevatedUpdateRunner`). ADR-025 requires each desktop/helper pair to accept exactly one protocol and a new helper never to accept protocol 1. | **Not met** (Task 54 finding, **R2**) |
| Existing Chocolatey boundary | `ChocolateyCallerTrustRejectionHappensBeforeTheElevatedHelperCanLaunch`, the Chocolatey profile, release-list and postcheck suites; restart codes stay failures on the Chocolatey route (Task 51) | Pass (CI) |
| History migration | `ClaimMigrationFromTenIsTransactionalAndPreservesTheOriginalDraft`, `GenerationMigrationFailurePreservesSchemaNineDataAndRollsBackTheColumn`, `MigrationCommitFaultRollsBackTheNewTableAndPreservesLegacyData`, `FutureSchemaIsRefusedBeforeMigrationMutation`. Phase C itself added no schema change (Tasks 51/52 used the existing `RestartRequired` status). | Pass (CI) |
| Synthetic crash | `UpdateHistoryIsAppendOnlyRecoverable…` (interrupted `Running` rows), `QuotaAccountsForAndEvictsAnInactiveCrashOrphan`, the Task 49 fault-race fixtures | Pass (CI) |

## 6. Measured resources (ADR-006)

The fixture harness (`scripts/Benchmark.ps1`, `tests/PCSteward.Performance`) now runs in the CI `build-and-test` job on the built tree and uploads its JSON (`fixture-performance`).

| Workload (CI `build-and-test`, run 43289637420, Windows runner) | Runs | Size | Median | p95 |
|---|---|---|---|---|
| Phase 1 deterministic product workload | 30 | 300 records | 2.60 ms | 4.05 ms |
| Task 18 fixture online check (fixture connectors only) | 20 | 2 sources | 9.83 ms | 15.85 ms |
| Task 35 large-inventory presentation (load, search, filter, sort) | 20 | 2,500 records | 2.02 ms | 9.02 ms |

Raw JSON: the run's `fixture-performance` artifact (30-day retention).

What this is not: the owner's PC, the WinUI shell, real WinGet or Chocolatey sources, five-minute idle, or process-tree CPU and memory. Every sample includes the first (cold) run; there is no separate warm-up and no cache variant. The last baseline-PC run of the same harness was 2026-07-29 on an older build (`docs/review/raw/task-29-fixture-performance.json`). Process-tree measurements for NFR-PERF-03–07 have never been taken (**O2**).

## 7. Dependency, licence and payload evidence

### 7.1 Package pins, by where they live

| Where | WinAppSDK | WinGet COM | Notes |
|---|---|---|---|
| PR #8 (`eef3784`, 2026-10-30) | 1.8.250907003 | 1.10.340 | SQLite and test package bumps. An ancestor of every release commit below and of the snapshot. |
| Installed 0.1.0 (`a219e42`), 0.1.1 (`f6ca90f`), 0.1.2 (`9322ca1`) | 1.8.250907003 | 1.10.340 | Superseded on the owner's PC by 0.1.3. |
| Installed 0.1.3 (`e20c70d`), the owner's PC now | 1.8.260921001 | 1.29.380 | Needs Windows App Runtime 1.8 ≥ 8000.994.2142.0; the publish paste read exactly that on 2026-11-12. |
| Snapshot (`4e3f94a`) | 1.8.260921001 | 1.29.380 | Same pins as 0.1.3. |

There are no branch-local pins: `Directory.Packages.props` on the snapshot equals `e20c70d`.

### 7.2 Licences

The licence map reconciles exactly to 17 packages (CI `package-licence-map`, run 43289637420). The 53A additions (WindowsAppSDK.ML, System.Numerics.Tensors) and the 54B re-check (still MIT, larger permissive NOTICE; [ADR-008 entry](../adr/ADR-008-LICENCE-AND-SOURCE-AVAILABILITY.md)) are recorded.

### 7.3 Third-party notices in the release payload (gap from 53A, closed here)

Released MSIs 0.1.0 to 0.1.3 contain no `THIRD-PARTY-NOTICES.txt`, because only the unused `New-PrivateBetaPackage.ps1` wrote one. Now:

- `scripts/PCStewardThirdPartyNotices.ps1` holds the one licence/notice map and the notices text. `New-PrivateBetaPackage.ps1` and `New-OwnerOnlyCandidate.ps1` both use it. It is ASCII and reads every file as UTF-8, because the candidate builder runs under Windows PowerShell 5.1.
- The candidate builder writes `THIRD-PARTY-NOTICES.txt` into the payload before the manifest, so the file is pinned, signed into the MSI and checked after install like every other file. It covers every package in the desktop's dependency manifest, the InProcCom runtime, and the two .NET runtime packages that Native AOT compiles into the helper (`Microsoft.NETCore.App.Runtime.NativeAOT.win-x64`, `runtime.win-x64.Microsoft.DotNet.ILCompiler`, both MIT; versions read from the helper's restore, because they follow the SDK patch). An unmapped package, an unused map entry, a changed licence or copyright, or a missing or empty notice stops the build.
- Evidence: `ThirdPartyNoticesTests` (PowerShell 7 and Windows PowerShell 5.1, 12 refusals); `TheReleaseBuilderAndThePrivateBetaPackageShareOneNoticesScript`; the `owner-only-candidate-builder` job checks the four key sections in a real build; `msi-install-test` installs it. In run 43289637420 the candidate builder logged `third-party notices: 19 packages` and re-validated a 105-file candidate (104 before this change; the one new file is the notices).
- Effect on releases: the next release (0.1.4) has one more file. The publish paste pins `New-OwnerOnlyCandidate.ps1` by hash, so the 0.1.4 paste needs the new hash. 0.1.0–0.1.3 stay as published.

## 8. WS5

Recorded as deferred ([ADR-026](../adr/ADR-026-WS5-AUTOMATION-AUTHORIZATION-DECISION.md), Option C). `FR-UPD-12` stays Later. `ScheduledHost` is scan-only: it references no execution or helper project (`ScheduledHostDoesNotReferenceExecutionOrElevatedHelper`). No automation code exists on the snapshot.

## 9. Open items and unsupported capabilities

Open items have no invented pass. R1 and R2 are gaps the Task 54 review found: Phase C cannot close until each is fixed or the owner accepts it. The other items need their own owner steps.

| Id | Item | What would close it |
|---|---|---|
| R1 | WinGet updates from the desktop do not bind or enforce a reviewed installer mode (FR-UPD-15). They run on the legacy mode-less job; the reviewed Silent/MSIX mapping is built and tested but not composed. | Route WinGet user-scope updates through the reviewed operation pipeline (mode bound at review, re-checked before launch, `--silent` from the reviewed mapping), with a composition test that fails if the legacy builder is reachable. |
| R2 | The protocol-2 helper still accepts protocol 1 and the desktop still uses it for machine-scope WinGet updates, against ADR-025 (one protocol per pair; a new helper never accepts protocol 1). | Either a protocol-2 machine-scope WinGet route and then a helper that refuses protocol 1 (with a regression test), or an owner-accepted ADR-025 amendment for a transition period. Removing protocol 1 now would stop machine-scope WinGet updates. |
| O1 | No real update through the current release yet: a user-scope WinGet update without UAC (AC-UPD-03), a declined UAC on the host (AC-UPD-05), and a machine-scope update on 0.1.3. The last real update (G9-T1, Chocolatey ripgrep, 2026-11-11) ran on an older build. | An owner-approved real update on 0.1.3 or later. |
| O2 | NFR-PERF-01–08 on the baseline PC, including process-tree CPU and memory, idle, and cold start. Only hosted-runner fixture timings exist (§6). | A measurement session on the owner's PC with its own plan and approval. |
| O3 | Truthful progress in the real WinUI shell during a real update on the current build. | Observed during O1. |
| O4 | WinGet COM 1.29.380 at runtime on the owner's PC (a scan on 0.1.3), and on a real Windows 11 22000 machine. | A scan in 0.1.3 (no admin needed). 22000 needs a test machine. |
| L1 | Known limit: a current WinGet client exits 0 after an MSI 3010, so that case reads Verified or Installed, not verified, instead of Restart required. Typed evidence needs COM execution, which ADR-003 amendment 1 and ADR-033 exclude. | An owner decision (COM execution for user scope). |
| U1 | Machine-scope WinGet updates have no reviewed-mode route yet (`UpdateInstallerModeCapabilities` maps WinGet user scope only); they run today on protocol 1 without a reviewed mode (see R2). | Phase D stage D4b, or the R2 fix. |
| U2 | Unsupported: interactive installer mode is not offered on any route. Chocolatey is NotControllable; WinGet runs with `--disable-interactivity` but without a bound mode (R1). | A later task, if wanted. |
| U3 | Unsupported: installs, uninstalls, unattended rules, direct vendor installers (`FR-UPD-12/13`, ADR-023/024 paused). | Phase D, owner-paused. |

## 10. Scope handed to Task 54

The independent review covers the snapshot in §1: Tasks 49–52 as merged, the 53A package delta and runtime prerequisite, the 54B projection and target change, and the notices change in §7.3, against `FR-UPD-01–15`, `AC-UPD-01–11`, ADR-025 and ADR-026, including progress, mode, restart, batch authority, migration, security and the measured performance in §6. Under the owner's agreed process for Phase C, the independent reviewer is Codex (chatgpt-codex-connector) on the PR that carries this report; its result is recorded in the [Task 54 record](TASK_54_PHASE_C_REVIEW_GATE.md).
