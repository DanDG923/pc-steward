# Tasks 51 and 52 — Restart outcomes and the Update all audit

Phase C stage C1 (owner-agreed plan, 2026-11-12). Base: `phase/4-beta` at `c045fe5`. Requirements: `FR-UPD-05/06/08–11`, `AC-UPD-06–11`, `FR-UI-06`, `FR-HIS-01/03`.

## Task 51 — Reliable restart outcomes

### What was wrong

- `InventoryWorkflow.ReInventoryAsync` always returned `RestartPending = false`, and nothing else in the WinGet route carried a restart.
- Any non-zero WinGet exit was a provider failure. So an update whose installer started a restart itself (WinGet `0x8A15010B`) read as **Failed**.
- Home's restart notice cleared as soon as a scan saw the target version. An installer often registers the new version before the restart that finishes it, so the notice could vanish before the restart.

### What WinGet actually reports (winget-cli source, `master`)

| Installer result | WinGet client exit | Meaning |
|---|---|---|
| MSI/WiX/Burn 3010 (`rebootRequiredToFinish`) | **0** on current clients, with only a text warning; `0x8A150109` on older clients | Installed, restart to finish |
| MSI/Burn 1641 (`rebootInitiated`) | `0x8A15010B` | Installed, the installer started a restart |
| Inno Setup 8 (`rebootRequiredForInstall`) | `0x8A15010A` | Not installed: restart, then install again |

Source: `ReportInstallerResult` in `src/AppInstallerCLICore/Workflows/InstallFlow.cpp`, default return codes in `src/AppInstallerCommonCore/Manifest/ManifestCommon.cpp`, codes in `AppInstallerErrors.h`.

### The change

- **Typed evidence only.** `Provider.WinGet/WingetRestartExitCodes.cs` (new) classifies only those three HRESULT exit codes. Output text is never read, so the warning "Restart your PC to finish installation" proves nothing on its own. A raw 3010 is not a WinGet client exit code, so it stays a failure.
- **Execution.** `NonElevatedUpdateRunner.ClassifyExit` and `ElevatedUpdateRunner` map `0x8A150109` and `0x8A15010B` to the new `UpdateExecutionStatus.RestartReported`, for WinGet jobs only. Chocolatey keeps its route contract, where a restart code is a failure. `0x8A15010A` is a provider failure with the fixed diagnostic `restart-required-before-install; update-not-applied`.
- **Batch.** `ApprovedUpdateExecutor` passes it on as `UpdateProviderAttemptStatus.RestartReported`. `SequentialUpdateBatchCoordinator` still runs post-update inventory, then records **RestartRequired**. It never records Success (AC-UPD-07), with or without the target version, and even when inventory fails or is interrupted. The diagnostic says which: `provider-reported-restart-required; target-version-detected-by-post-update-inventory`, `…; target-version-not-yet-detected` or `…; post-update-inventory-did-not-finish`.
- **Inventory.** `ReInventoryAsync` still returns `RestartPending = false`, now documented as deliberate. Machine-wide restart indicators are not tied to one job, so they cannot be reliable evidence for it.
- **Presentation.** Restart required already had its wording. "Restart needed before install" now reads as Failed: "The installer needs Windows to restart before it can install this update, so nothing was updated…". On Home, `UpdateJobHistory.RestartPending` keeps a restart-required update listed until Windows has started after it. The desktop reads the boot time from the system tick count. A newer version or a removed app still clears it.
- **History.** No schema change and no migration. `UpdateHistoryStatus.RestartRequired` already existed. Older rows read exactly as before.

### Known limit

On a current WinGet client, an MSI 3010 exits **0** with only a localised text warning. PC Steward does not read that text, so it records **Verified** if the target version is already registered, or **Installed, not verified** if it is not. It never records Restart required in that case. Typed restart evidence for that case needs the WinGet COM API's `InstallResult.RebootRequired`. Moving update execution to COM is Task 54A/54B.

## Task 52 — Update all audit

Update all was built in Task 73 Phase 6 ([record](TASK_73_PHASE6_UPDATE_ALL_AND_SKIP_VERSION.md)). Each failure case from `prompts/52_FROZEN_UPDATE_BATCHES.md` and ADR-025 was checked against the code and tests.

| Case | Status before | Evidence now |
|---|---|---|
| Frozen set; a new scan can't add or retarget items | Covered | `ItemsRunOneAtATimeAndAChangedItemIsNotStarted` |
| Excluded, stale and newly incompatible rows; new candidate after review | Covered | `BatchReviewPreparesEachItemFreshly…`, `ItemsRunOneAtATime…` (a new candidate version is not started) |
| Mixed outcomes, later items still run | Covered; restart added | `EachItemHasItsOwnHonestOutcome…`, `ADeclinedPermissionPrompt…`, new `ATypedProviderRestartIsRecordedAsRestartRequiredNeverVerified` |
| Late warning: target starts running after review | Covered | `ATargetThatStartsRunningAfterReview…` |
| **Late warning: a new process for an acknowledged target** | **Gap, fixed** | The acknowledgement was a plain yes/no, so a target the owner was warned about as process A could start with process B. It is now tied to the process names in the warning they confirmed. Any other process is a new warning, and the item is not started. The coordinator's last check before the provider uses the same rule. New `ANewRunningProcessAtTheItemsTurnNeedsANewAcknowledgement`, `TheCoordinatorsLastCheckAlsoNeedsTheWarnedProcesses` |
| Stop between items; the running item is never cancelled | Covered | `StopIsReadBetweenItemsOnly…`, `AStopRequestedDuringTheSettleWait…` |
| **Cancellation during the next item's re-preparation or running-app check** | **Gap, fixed** | It fell into the fault path and read as "may have run, not verified", although nothing had started. It now reads as **Not started**, approves nothing and records no history for that item. New `CancellationBeforeTheProviderStartsNothingAndSaysSo` (prepare, detect, between) |
| Cancellation during the provider or verification | Covered | `CancellationAfterProviderCompletionRemainsVerificationRequired`, `CancellationStopsOnlyUnstartedItems…` |
| 30-minute ceiling; no silent renewal; clock going backwards | Covered | `AnItemPastTheThirtyMinuteDispatchCeiling…`, `…ExpiresDuringItsOwnReprepare…` |
| **Exact boundary, four-minute first installer, just-in-time approval** | **Untested, now tested** | Approval time came from the system clock, not the workflow's clock, so it couldn't be tested. It now uses the workflow's clock (the same in production). New `TheDispatchCeilingIsExactAndASlowFirstInstallerDoesNotExpireTheNextApp`: exactly 30:00 runs, one tick over doesn't, and the next app's approval is made at its own turn |
| Two-minute helper request freshness | Covered | The request is built when the item launches (`ElevatedHelperProtocol.RequestLifetime`), never at review |
| Process crash before or after invocation; no replay | Covered | Interrupted `Running` rows are recovered as not verified (`SqliteDatabaseTests.UpdateHistoryIsAppendOnlyRecoverable…`). A batch is never stored, so nothing is replayed after a restart |
| Scan, reset and ScheduledHost ordering | Covered, stricter than ADR-025 | The whole batch runs inside one mutating data lease. A busy lease (scan, ScheduledHost, reset, earlier update) refuses the batch before anything starts. It doesn't wait. ADR-025 allows a bounded wait inside the 30 minutes. Refusing is stricter, so it is left as is |

## Not claimed

- No live update, reboot or host action. Behaviour on a real installer that returns 1641 or Inno 8 is fixture logic until it is seen on Windows.
- The current-client MSI 3010 case above stays unrecognised until COM adoption (Task 54B).
