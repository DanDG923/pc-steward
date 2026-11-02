# Task 73 Phase 6 — Update all available and Skip this version

Owner request, 2026-11-02: "I would want the update all available button & skip the version." Base: `claude/task-73-ui-overhaul` at `ddcca30`.

- **Update all** implements the accepted SPEC §9.13 G4: reviewed multi-select / Update All, with cancellation and per-item partial outcomes. It covers `FR-UPD-09–11`, `FR-UI-01–07` and `AC-UPD-01–11`.
- **Skip this version** is a new owner decision. It is recorded in [V2_PLANNING_DECISIONS](../requirements/V2_PLANNING_DECISIONS.md) and closes the skip-version part of Q2/Q3. It extends `FR-POL-01–05`.

## How a single job works today (before this phase)

1. **Updates page.** `UpdatesPagePresenter.Build` (`src/PCSteward.Application/UpdatesPagePresentation.cs`) puts in Ready only the domain's `VerifiedUpdate` rows with complete evidence while sources are on.
   - The Desktop (`src/PCSteward.Desktop/MainWindow.cs`, `BuildUpdatesPage`) shows one radio per Ready row, with nothing preselected.
2. **Review.** `ReviewAndExecuteAsync(row)` calls `ReviewedUpdateExecutionWorkflow.BeginReviewAsync` (`src/PCSteward.Application/ReviewedUpdateExecutionWorkflow.cs`).
   - This prepares fresh typed inventory evidence and creates an `UpdateJobReview` (domain `UpdateJobs.cs`), the running-app detection and a per-job `UpdateProgressMailbox`.
3. **Sheet.** `UpdateReviewSheet.Create` (`UpdateJobPresentation.cs`) builds the §17.2 facts, the confidence in words, the risk callouts and the administrator requirement (`AdministratorRequirementPresenter.ForSelection`).
4. **Decision.** The sheet awaits a `TaskCompletionSource<bool>`. The only `TrySetResult(true)` is the commit button, and Cancel is the default.
5. **Confirm and run.** `ConfirmAndRunAsync` does these steps in order:
   1. re-prepares the evidence and requires `review.Selection.EqualsEvidence(fresh)`, or nothing starts;
   2. re-detects running apps and requires an acknowledgement;
   3. persists the `ImmutableUpdateJob` (`UpdateJobApprovalService.ConfirmAsync`);
   4. runs a one-item `SequentialUpdateBatchCoordinator.RunAsync` (`UpdateBatchWorkflow.cs`).
6. **Execution.** The coordinator records Queued, Running and the final history entries.
   - It runs `IApprovedUpdateExecutor` (`src/PCSteward.Execution/ApprovedUpdateExecutor.cs`), then `IPostUpdateInventory`.
   - **Success** is recorded only when re-inventory finds the target version. **RestartRequired** is recorded only for a reliable restart-pending result.
7. **Elevation.** `ApprovedUpdateExecutor` routes machine scope to `ElevatedUpdateRunner` (`ElevatedUpdateRunner.cs`). That re-checks the job digest and evidence and starts the one-shot helper through `WindowsElevatedHelperTransport`.
   - The helper accepts exactly one request per launch: `OneShotHelperRequestValidator` (`ElevatedHelperProtocol.cs`) rejects a second request as `duplicate-request`.
   - **So the protocol is one UAC prompt per machine-scope job. It has no multi-item elevated job.**
8. **Verify.** `UpdateOutcomePresenter.ForResult` maps the recorded entries to Verified, Restart required, Installed, not verified, Failed, Not started or Result not verified.

## Feature 1 — Update all available (G4)

- **Selection (`UpdatesPagePresentation`).**
  - `SelectedKeys` (a set) replaces `SelectedKey`. Only Ready keys survive `Build`. Needs-review, ignored, skipped and pinned rows can never be selected.
  - Nothing is preselected. "Select all ready" and the primary "Review all ready (N)" select exactly the Ready group.
  - Checkboxes replace the radio.
- **Batch review (`ReviewedUpdateExecutionWorkflow.BeginBatchReviewAsync`).**
  - Prepares each key through the existing `BeginReviewAsync`. That gives a fresh `UpdateJobReview`, a running detection and a mailbox per item.
  - An item that can't be prepared is excluded, and its message is kept. Duplicate keys are rejected. The frozen set records `ReviewedAt`.
- **One sheet (`UpdateReviewSheet.CreateBatch`).**
  - Shows every item's §17.2 facts and its confidence in words, the excluded rows with their reasons, and aggregated risks: running apps (named), restart, and administrator ("N of M apps need administrator permission").
  - The commit button reads "Update N apps", with "anyway" if any app appears to be running. It carries the UAC shield only if an item is machine scope.
  - Cancel stays the default, and the single confirmation site is unchanged. A one-item batch renders exactly as today.
- **Execution (`ConfirmAndRunBatchAsync`).**
  - A sequential loop that calls the existing `ConfirmAndRunAsync` for each frozen item, so every item is freshly revalidated immediately before its own start.
  - It is fail-closed: changed or stale evidence means that item is **not started** with an explanation, and it is never silently re-reviewed.
  - A target that starts running after review needs the existing acknowledgement, so that item is not started.
  - Items whose review is older than the accepted 30-minute dispatch ceiling (V2 decisions, 2026-10-29) are not started and need a fresh review.
  - A stop request (`UpdateBatchStop`) is checked only **between** items. The running item's token is never cancelled by Stop, so there is no mid-install cancel and no "Cancel update".
  - Earlier results are kept as recorded.
- **Elevation choice.**
  - Items run sequentially, each with its own existing approval and elevation. The helper and protocol are unchanged, because the one-shot protocol has no multi-item job.
  - The sheet says: "Windows may ask for permission once for each app that needs it."
  - Changing the protocol would need ADR-025 versioning and endpoint validation. That is out of scope and must not be weakened.
- **Desktop.**
  - `ReviewAndExecuteAsync(IReadOnlyList<InventoryRow>)` is the one review/confirm site for one or many apps.
  - The Update step lists per-item states (Waiting, current phase, outcome label) and offers **Stop after this app**.
  - Verify groups per-item outcomes (from `UpdateOutcomePresenter`) and keeps Delete downloaded files. History and Activity record each item as today, through the coordinator.

## Feature 2 — Skip this version

- **Domain.**
  - `ProductUpdatePolicy.SkippedVersion`, plus a deterministic `SkipVersionRule.Applies(skipped, candidate)`:
    - comparer `Equal` → skipped;
    - identical trimmed strings → skipped (the exact version the user saw);
    - `Newer`, `Older`, `Incomparable` or `ConnectorDeferred` → not skipped.
  - `UpdateIntelligence.Assess` applies it after matching, only to `VerifiedUpdate` or `ReviewRequired` items.
    - Those become the new `UpdateIntelligenceStatus.Skipped`, appended so existing ordinals are unchanged, with `IsActionable = false`.
    - The precedence is ignore, then pin, then skip.
  - A skip can only remove actionability; it can never add it.
- **Persistence.** No migration.
  - The `stable_decisions` row gets `kind = "SkipVersion"` and `value` = the trimmed candidate version. The existing `reason` column holds up to 512 characters, and `expires_at` is null.
  - Saving a skip replaces that product's earlier skip.
  - Unknown kinds are ignored by policy evaluation. Decisions and the timeline show them as "Saved decision (not recognised)" instead of a pin.
- **UI.**
  - Ready and needs-review rows that have a new version get the overflow item "Skip this version (V)". It uses the existing confirmation dialog pattern with an optional reason.
  - Skipped rows appear under "Ignored and skipped" as "Skipped version V", with "Stop skipping V".
  - Decisions lists the skip, and the Installed apps filter and the Home tile become "Pinned, ignored & skipped".

## Safety invariants and tests

| Invariant | Evidence |
|---|---|
| Only Ready rows are selectable; nothing is preselected | Presenter tests; UI contract tests |
| Approval only through the single confirm site, after the frozen review | `TrySetResult(true)` count = 1; ordering contract |
| Fresh revalidation per item at its start; changed means not started | Batch workflow tests (changed evidence, late running app, expiry) |
| Stop only between items; the running item finishes and is recorded | Batch test with stop during item 1; contract: no "Cancel update" |
| Earlier results kept; outcome per item; Verified only from Success | Batch and presenter tests |
| No elevation change; at most one UAC prompt per machine-scope item | Execution and helper sources untouched; architecture tests |
| Skip rule deterministic; never actionable | Domain unit tests (equal, newer, older, incomparable, identical string, precedence) |
| Skip persistence round-trip; no schema change; unknown kinds safe | SQLite and decision-service tests |

## Open risks

- Per-item UAC prompts for a batch of machine-scope apps is noisier than one prompt. A multi-item elevated job would need a new protocol version and an ADR.
- Behaviour on the WinUI runtime (checkbox selection churn, live progress switching between items) can only be checked on Windows. Linux checks compile the Desktop through a stub.
- A one-app job now also gets the 30-minute dispatch ceiling. This is stricter, never looser.
