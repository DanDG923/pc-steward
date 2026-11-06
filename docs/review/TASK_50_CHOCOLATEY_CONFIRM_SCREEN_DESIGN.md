# Chocolatey client update: Desktop composition and the Confirm screen (design note)

Date: 2026-11-06. Status: **design, owner questions decided (§9). No code change.** Handover item 3 ("Desktop composition of a Chocolatey review", [handover](TASK_50_HANDOVER_2026-11-06.md) §5). Scope approved by the owner on 2026-11-06 (project thread "Design the Chocolatey Confirm screen").

Requirements: `FR-UPD-15`, `FR-UPD-01/03–09`, `FR-UI-03/04`, `FR-HIS-01/03`, `TM-03/04/07/13`; `AC-UX-01–09` best effort (ADR-021). Builds on [ADR-031](../adr/ADR-031-CHOCOLATEY-CLIENT-SELF-UPDATE.md), [ADR-025](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md), the [ADR-031 design mapping](TASK_50_ADR031_CLIENT_SELF_UPDATE_DESIGN.md) (§2, §3, §3a, §8.6), the [activation record](TASK_50_ACTIVATION_CHANGE.md) and the [gate 3 re-verification](TASK_73_GATE3_REVERIFICATION_2026-11-06.md). The base is the Task 73 branch `claude/task-73-ui-overhaul` at `9a789ea` (PR #13).

**In scope:** only the Chocolatey client self-update (`UpdateRouteContract.ChocolateyClientSelfUpdateV1`): the path from the verified offer to Confirm, what the screen shows in every state, what happens after Confirm, and the safety cases and tests that a later build task needs.

**Out of scope:** any change under `src/` (the Task 73 thread owns the Desktop code), Chocolatey package updates (`ChocolateyMachineRuntimeLimitsV3`), batch updates, admitting or turning on the route, and any action on the owner's PC. Nothing here approves obligation 8, waives a gate, or changes D4 = G-0.

## 1. Where things stand (code facts at `9a789ea`)

| Fact | Evidence |
|---|---|
| The Settings → Chocolatey card opens a review dialog. When the dialog opens, it runs a read-only, non-elevated check of the installed client, then shows the offer ("A newer reviewed Chocolatey release is available: X (not yet enabled)") or why there is none | `ChocolateyClientUpdateViewModel`, `ChocolateyClientUpdateSurfaces`, `ChocolateyInstalledClientVerification` |
| Confirm is always off. `NotAdmittedPendingReview` is added unconditionally, and `CanConfirm => UnavailableReasons.Count == 0` | `ChocolateyClientUpdateViewModel` constructor; pinned by `ChocolateyClientUpdateDesktopUiBoundaryTests` |
| The view model holds no operation, review, draft, permit or confirm path | Reflection test in `ChocolateyClientUpdateViewModelTests` |
| The review gate admits the exact client shape (activation C-A) | `ReviewedUpdateReview.HasSupportedMode` → `ReviewedChocolateyRouteAdmission.Admits` |
| **Nothing in `src` constructs `ReviewedUpdateDispatchWorkflow`, for any route.** WinGet updates in the Desktop still go through `ApprovedUpdateExecutor` / `ReviewedUpdateExecutionWorkflow`, so the ADR-025 protocol-2 pipeline has never been composed in the app | `App.xaml.cs`; `DesktopProducersBoundaryTests.TheRuntimeLimitRouteIsAdmittedOnlyByShapeAndStaysUncomposed` |
| The client-mode producers exist, but only test factories build them in client mode (`ChocolateyProfileEvidenceSource.CreateClientForTests`, `ChocolateyReviewedUpdateVerifier.CreateClientForTests`). The production `ChocolateyReviewedRouteComposition` builds package-mode producers and nothing constructs it | `ChocolateyProfileEvidenceSource.cs`, `ChocolateyReviewedUpdateVerifier.cs`, `DesktopProducersBoundaryTests` |
| `ChocolateyRuntimeLimitsRouteDispatcher` (internal, in `PCSteward.Execution`) carries the client mode over `WindowsReviewedHelperTransport`. Nothing constructs it | `ChocolateyRuntimeLimitsRouteDispatcher.cs`, `ReviewedHelperProtocol2Transport.cs` |
| The deployment capability statement lists `Update/ChocolateyClientSelfUpdateV1` | `PCSteward.DeploymentCapability.txt` |
| The full client workflow (review, confirm, F′, claim, permit, dispatch, finalization, history) passes through the real types in tests (gate 1), and stages B–Launch pass on Windows CI (gate 2) | `ClientRouteWorkflowAdmissionGateTests`, `ClientRouteSqliteAdmissionGateTests`; activation record D7-4/D7-5 |
| The review sheet, Verify and history texts already exist | `ChocolateyClientUpdatePresentation` (`Review`, `VerifiedText`, `NotVerifiedMessage`, `RunFaultMessage`, the stop and leftover messages) |
| A review lives at most 30 minutes, and `Confirm()` works once | `ReviewedUpdateLifetime.MaximumReviewAge`, `ReviewedUpdateReview.Confirm` |
| The compiled reviewed list holds only 2.7.5, so even a composed screen offers nothing until a second release is reviewed (G9) | `ChocolateyReviewedReleases.Listed`; handover §5 item 6 |

So the work is three pieces: a production composition of the reviewed pipeline in client mode, a review session that the screen drives, and one compiled owner admission switch that replaces the unconditional reason. The first two can be built and tested with Confirm still off. Only the third turns Confirm on.

## 2. When Confirm turns on

Confirm is enabled only when **every** condition below holds. Each one is evaluated when the screen renders, and again (where marked) when Confirm is pressed. No condition is cached across dialog openings.

| # | Condition | Source of truth | Re-checked on press |
|---|---|---|---|
| C1 | **Owner admission is compiled in.** A new compiled constant (proposed name `ChocolateyClientUpdateAdmission.OwnerApproved`) replaces the unconditional `NotAdmittedPendingReview`. It ships `false`. Setting it `true` is its own reviewed commit, made only after a recorded probe PASS, the open G9 real-launch residuals exercised on the owner's host (§8 item 4), and the owner's obligation 8 approval. This turns D4 = G-0 from procedure into code | Compiled constant; boundary test pins its single definition and that no configuration, registry or database value can set it | Yes |
| C2 | **The installed client is verified in this dialog.** The check result is `Verified`, from this opening of the dialog, and less than 10 minutes old (owner decision, §9). An older result shows "Check again" instead of Confirm | `ChocolateyInstalledClientVerification` with its observation time | Yes (age) |
| C3 | **An offer exists.** The verified release is listed, and a strictly newer listed release exists. The target is the newest listed release | `ChocolateyClientUpdateViewModel.OfferTarget` over the compiled list | — |
| C4 | **Split token is limited.** UAC on, owner is an administrator running a limited desktop | `ChocolateyReviewedRouteProbe.ProbeSplitTokenState`, probed on each opening | Yes |
| C5 | **The deployment lists the route.** The protected capability statement lists `Update/ChocolateyClientSelfUpdateV1` | `IHelperProtocolCapability.Evaluate`, through the dispatcher's `Supports` | Yes (inside the workflow) |
| C6 | **No earlier client update needs attention.** No Chocolatey client job is Unverified, Uncertain or has `ReleaseIncomplete`, unless the owner has since run a check that verifies the client again (design §2 R3: every Chocolatey route refuses until the tree is repaired) | Reviewed-update history store | Yes |
| C7 | **Nothing else is running.** No other update, batch, scan or data operation holds the current-user gate | The common gate (`OperationOwnership`) | Yes (the gate runner) |
| C8 | **The review is live.** The frozen operation is saved, `ReviewedUpdateReview` is current (under 30 minutes, not cancelled, not confirmed), and `HasSupportedMode` admits it | `ReviewedUpdateReview` | Yes (`Confirm()` throws otherwise) |

C2 is a display condition only. Pressing Confirm never trusts it: the workflow re-observes at F′ and stages B/D, as today (design §8.6 residual). C1 is the only new trust decision, and it can only make things stricter.

## 3. Composition (what the build task adds, in outline)

```
Settings card ─► review dialog opens
   │   ChocolateyClientUpdateViewModel.Open(catalog, Checking, reported, probe)   (unchanged)
   │   read-only check ─► WithVerification(result)                               (unchanged)
   ▼
offer present and C1, C3, C4, C6 hold ─► "Prepare review" (automatic, no button)
   │   ChocolateyClientUpdateReviewSession.PrepareAsync(verified observation, compiled list)
   │     ├─ builds the frozen ReviewedUpdateOperation (schema 3, chocolatey, Machine,
   │     │  ProviderScriptControlled, choco-profile-v2: evidence id, observed → target)
   │     ├─ SaveReviewedOperationAsync ─► ReviewedUpdateDraft
   │     └─ new ReviewedUpdateReview(draft)            (30-minute lifetime starts)
   ▼
screen shows ChocolateyClientUpdatePresentation.Review(operation) + C1–C8
   │
Confirm ─► review.Confirm() ─► LiveApprovedUpdate
   ▼
ReviewedUpdateDispatchWorkflow.RunAsync(approval)
   gate runner ─► F′ evidence (client mode) ─► durable claim ─► one-use permit
   ─► dispatcher (protocol 2, one UAC prompt) ─► helper stages B…D, watchdog armed
   ─► settlement ─► client verifier (re-observation of the target release)
   ─► finalization ─► history
   ▼
result screen ─► automatic re-check of the installed client ─► card updated
```

**New types (proposed names; the build task may rename):**

1. **`ChocolateyClientUpdateComposition`** (`PCSteward.Provider.Chocolatey.Native`, internal, with a public factory called once from `App.xaml.cs`). It is the single production site that constructs, in client mode: one `ChocolateyDispatchBaselines`, the client `ChocolateyProfileEvidenceSource`, the client `ChocolateyReviewedUpdateVerifier` (which implements `IReviewedClientUpdateVerifier`), the dispatcher over `WindowsReviewedHelperTransport` and the protected capability, the gate runner over the common current-user gate, and one `ReviewedUpdateDispatchWorkflow`. It exposes only a review session factory, never the workflow, dispatcher or producers. It follows the existing package-mode `ChocolateyReviewedRouteComposition` shape (shared baselines, factory lambdas) so the same boundary tests can be extended rather than written from nothing.
2. **`ChocolateyClientUpdateReviewSession`** (Application). It holds the frozen operation, the `ReviewedUpdateReview` and the Confirm path. It is the only type that calls `Confirm()` for this route and the only caller of the workflow. It runs once: after Confirm, any further press is refused, and a new review needs a new session. The view model stays a pure display model with no confirm path, so the gate 3 reflection test stands unchanged.
3. **`ChocolateyClientUpdateAdmission.OwnerApproved`** (Application, compiled `const bool`, ships `false`). It is C1.

**The package route stays uncomposed.** `ChocolateyMachineRuntimeLimitsV3` keeps its current boundary: the new composition must refuse to build a session for any operation that is not the exact client shape, and `TheRuntimeLimitRouteIsAdmittedOnlyByShapeAndStaysUncomposed` keeps its package-route half.

**The WinGet path is not touched.** WinGet keeps `ApprovedUpdateExecutor`. Moving WinGet onto the protocol-2 pipeline is a separate decision.

## 4. The screen

### 4.1 Layout

The existing `ContentDialog` from `ChocolateyClientUpdateSurfaces.ReviewDialog`, with the same field rows. Additions are marked **new**.

```
┌ Update Chocolatey to 2.8.0 ───────────────────────────────────────┐
│ Installed            2.7.5 (verified just now)                     │
│ Reported by the last scan   2.7.5 (reported by the last scan)      │
│ Offer                A newer reviewed Chocolatey release is        │
│                      available: 2.8.0                              │
│ Change               Chocolatey 2.7.5 → 2.8.0              (new)   │
│ Source               Chocolatey Community repository, release      │
│                      checked against PC Steward's reviewed list    │
│                      (signer: …)                           (new)   │
│ Reviewed releases    2.7.5, 2.8.0                                  │
│                                                                    │
│ What this update does                                              │
│   What happens / Verification / Administrator permission /         │
│   Safety checks while it runs / If the folder may have been changed│
│ Chocolatey's setup also                                            │
│   (the five R4 disclosures)                                        │
│                                                                    │
│ Why this update cannot be confirmed   (shown only while a reason   │
│   remains; each unmet condition is one line)                       │
│                                                                    │
│ This review expires at 21:34.                              (new)   │
│                                                                    │
│                       [ Update Chocolatey to 2.8.0 ]   [ Close ]   │
└────────────────────────────────────────────────────────────────────┘
```

- **Close is the default button,** so Enter and Esc never confirm. Confirm needs a deliberate click or Tab then Space, as with the reset flow.
- The title and the Confirm label name the exact target version from the frozen operation (`ChocolateyClientUpdatePresentation.Title`), never "Update" alone.
- There is no "don't ask again" option, no batch option and no scheduling option.
- The expiry line uses the review's own 30-minute lifetime. When it passes, Confirm turns off and the button reads "Check again", which closes the review session and re-runs the check.

### 4.2 States before Confirm

Existing strings are reused wherever they exist. **New** strings are proposals for the build task's copy review.

| State | Offer line | Confirm button | "Why this update cannot be confirmed" lines |
|---|---|---|---|
| Checking | `CheckingOfferText` | Off | `InstalledReleaseCheckingText` |
| Could not verify (refused, unavailable, timed out, stopped) | `CouldNotVerifyOfferText` + detail | Off | `InstalledReleaseNotVerifiedText` |
| Verified, no newer listed release (today's owner host) | `NoNewerReleaseText` | Hidden | `NoNewerReviewedReleaseText` |
| Verified, not on the list | `InstalledNotListedOfferText` | Hidden | `InstalledReleaseNotReviewedText` |
| Reviewed list unavailable | `CatalogUnavailableOfferText` | Hidden | `ReviewedReleasesUnavailableReasonText` |
| Offer, owner admission not compiled in (C1 false; what ships first) | `AvailableTitlePrefix` + X + `NotYetEnabledSuffix` | Off | `NotAdmittedText` |
| Offer, split token not limited | offer line | Off | the existing split-token text |
| Offer, earlier update needs attention (C6) | offer line | Off | **new:** "The last Chocolatey update was not verified. Check the installed Chocolatey again before you update it." |
| Offer, another task running (C7) | offer line | Off | **new:** "Another update or task is running. You can confirm when it finishes." |
| Offer, preparing the review | offer line | Off | **new:** "Preparing the review…" |
| Offer, review could not be prepared | offer line | Off | **new:** "PC Steward could not prepare this review. Nothing was started. Close this and try again." |
| Offer, check too old (C2) or review expired (C8) | offer line | "Check again" | **new:** "This review has expired. Check again to see the current state." |
| **Offer, all conditions hold** | `AvailableTitlePrefix` + X (no suffix) | **On:** "Update Chocolatey to X" | section hidden |

"Hidden" means no offer exists, so a disabled Confirm button would suggest something that cannot happen. "Off" means an offer exists but a condition is unmet. The disabled button then gets an automation help text that names the first unmet reason.

### 4.3 After Confirm

The dialog stays open and becomes the progress view. It shows one status line (a polite live region) and the frozen "Chocolatey 2.7.5 → 2.8.0" line. Close is disabled from the press until the result is final, because closing the dialog cannot stop an elevated run that has started. The main window has no close guard today. The build task should add one for this run (**new:** "Chocolatey is updating. Closing PC Steward now leaves the result unverified."), or record why it is not needed.

| Phase (workflow point) | Status text (**new** unless named) |
|---|---|
| Gate, F′ evidence, claim | "Checking Chocolatey again before it starts…" |
| Waiting for the UAC prompt | "Windows is asking for administrator permission." |
| Running (helper launched, watchdog armed) | "Updating Chocolatey. PC Steward is watching its folder and settings while it runs." |
| Verifying | "Checking the updated Chocolatey against the reviewed release…" |

The result replaces the status line. Every result text below already exists in `ChocolateyClientUpdatePresentation` or `ReviewedUpdateDispatchWorkflow`, so live text equals history (FR-HIS-01).

| Outcome | Shown text | Next step offered |
|---|---|---|
| `Success` | `VerifiedText(note)`: `VerifiedMessage`, plus `VerifiedLeftoverNote` when the reviewed leftover is present | None. The card re-checks and shows "No newer reviewed Chocolatey release" (or the next offer) |
| `Unverified`, leftover not reviewed or not from the source release | `NotVerifiedLeftoverMessage` / `NotVerifiedLeftoverNotSourceMessage` | The manual step in the message. PC Steward never deletes it |
| `Unverified`, release not matched or unreadable | `NotVerifiedReleaseMessage` / `NotVerifiedUnreadableMessage` | "Check again" |
| `Unverified`, `ReleaseIncomplete` | `NotVerifiedReleaseIncompleteMessage` | "Check again" |
| Watchdog stop (setup override, folder writable, watch failed, alone or with an incomplete release) | `StoppedSetupOverrideMessage`, `StoppedRootWritableMessage`, `StoppedWatchFailedMessage` and the `…ReleaseIncompleteMessage` variants, with `WatchdogRecoveryStep` | The recovery step in the message |
| `Uncertain` (lost track) | `LostTrackCautionMessage` with `UntrustedFolderRecoveryStep` | The recovery step in the message |
| `ElevationDenied` | **new:** "You declined the administrator prompt. Nothing was changed." | Close. A new attempt needs a new review |
| `Refused` / `NotStarted` before launch | The workflow message, prefixed **new:** "Nothing was started." | "Check again" |
| `Failed` / `TimedOut` / `Cancelled` after start | `ReviewedUpdateDispatchWorkflow.StoppedAfterStartMessage(status)` | "Check again" |
| `RestartRequired` | Never for this route (`RestartPending` is always false). If seen, it is shown as `RestartContractViolationMessage` and treated as not verified | "Check again" |

After any result, the dialog re-runs the read-only check so the card shows the current installed release. Any outcome other than `Success` sets C6 until a later check verifies the client again. History uses `ReviewedUpdateHistory` with `HistoryRouteText` and the frozen versions, unchanged.

## 5. Refusals and safety invariants (the build task pins each with a test)

1. Confirm is never enabled while `ChocolateyClientUpdateAdmission.OwnerApproved` is `false`. The constant has exactly one definition, and no configuration, registry, environment or database value reaches it.
2. Only `ChocolateyClientUpdateReviewSession` calls `ReviewedUpdateReview.Confirm()` for the client route, and only `ChocolateyClientUpdateComposition` constructs the workflow, the dispatcher, the client producers and the baselines. Only `App.xaml.cs` constructs the composition. The view model keeps no confirm path (gate 3 unchanged).
3. The frozen operation is built only from the verified observation and two compiled list entries (observed and target). The target is the offer rule's result. A scan value, wire value or database value never becomes a version, a package ID or a command argument.
4. One Confirm leads to at most one dispatch. A second press, an expired review, a cancelled review or a lost race with another task is refused before any claim, and nothing reaches the helper.
5. F′ re-observation, stages B/D and the client verifier run in full on every dispatch. The display check never stands in for them.
6. The package route stays uncomposed. A session for any shape other than the exact client shape is refused.
7. Success needs the exact target manifest and asset match. Every other result is shown as not verified, uncertain or not started, with its recovery step, and sets C6.
8. Close is disabled while a run is in flight, and Enter or Esc never confirms.
9. Nothing in the composition deletes, repairs or reconfigures anything in the Chocolatey folder or settings (design §3a; handover cause 2: `cacheLocation` stays the owner's to set).
10. No new background work: the check runs only when the dialog opens or "Check again" is pressed, and no elevated process outlives the job.

## 6. Tests the build task needs

**Architecture (IL and source scans):**
- Extend `DesktopProducersBoundaryTests`: the client composition is the only site that constructs the client producers, the baselines, the dispatcher and the workflow; `App.xaml.cs` is its only constructor caller; the test-only factories still have no `src` caller.
- Change `TheRuntimeLimitRouteIsAdmittedOnlyByShapeAndStaysUncomposed` deliberately. It keeps the package-route half, and the client half now asserts the single composition site.
- `ChocolateyClientUpdateDesktopUiBoundaryTests`: `NotAdmittedPendingReview` is now derived only from `OwnerApproved`; the constant has one definition and ships `false`; the only `Confirm()` caller for the route is the session.

**Application:**
- `ChocolateyClientUpdateViewModelTests`: one test per row of §4.2, including the new C6, C7 and expiry rows; Confirm is enabled only when C1–C8 all hold, across every split-token state.
- `ChocolateyClientUpdateReviewSessionTests` (new): it builds the exact client shape from fixture observations and lists; it refuses unlisted, equal, older or malformed versions and every near miss of the shape; one Confirm gives one workflow run; a second press, expiry and cancellation refuse before any claim; a C7 conflict refuses before any claim.
- Presentation: one test per row of §4.3, checking that the live text equals the history text.

**Workflow and Windows:**
- Re-run `ClientRouteWorkflowAdmissionGateTests` and `ClientRouteSqliteAdmissionGateTests` through the composition rather than a hand-built workflow.
- A Windows CI end-to-end with a stand-in engine, from dialog to history, with `OwnerApproved` set `true` only in a test build. If a test-only build switch is not acceptable, the session-level tests above are the limit, and the first real run is the owner-host probe.

**Not provable in CI:** the real UAC prompt, a real self-upgrade under the held binding (design §5, the VM run), Narrator and high-contrast behaviour. These stay owner-host evidence.

## 7. Requirement mapping

| Requirement | Where this design meets it |
|---|---|
| `FR-UPD-15` (reviewed Chocolatey client update) | §2 C1–C8, §3, §5 items 1–7 |
| `FR-UPD-01/03–09` (approve, prepare, elevate, verify) | §3 flow, §4.3 phases and outcomes |
| `FR-UI-03/04` (review and status surfaces) | §4.1 layout, §4.2 states, §4.3 progress |
| `FR-HIS-01/03` (history equals live result) | §4.3 result table (existing strings only) |
| `TM-03/04/07/13` (no unreviewed action, no unverified success, narrow elevation, owner control) | §5 items 2–5 and 7–10 |
| `AC-UX-01–09` (best effort) | Close as default, live region for status, automation text for disabled Confirm, no Enter confirm |

## 8. What has to happen before a real Confirm

1. The build task implements §3–§6 with `OwnerApproved = false`, and Windows CI goes green. The screen then looks exactly like today's on the owner's PC.
2. The probe re-run passes (handover §5 item 2; PR #15; CB2.8 before 2026-11-20T18:53:44Z).
3. A second reviewed release is on the compiled list (G9). Until then C3 never holds.
4. The open G9 real-launch residuals are exercised and recorded on the owner's host: a real self-upgrade under the held binding (design §5, the VM run), the native held DLL load, the PowerShell helper reads and an elevated launcher holding files (`TASK_50_ACTIVATION_CHANGE.md` §1). The probe proves only UAC and pre-launch enforcement, so fixture and probe evidence alone never admit Confirm.
5. The owner gives the obligation 8 final admission approval, and a separate reviewed commit sets `OwnerApproved = true`.

## 9. Owner decisions (2026-11-06)

The owner accepted all three defaults in the project thread on 2026-11-06.

1. **Check age at Confirm:** at most 10 minutes (C2). F′ re-checks anyway.
2. **C6 scope:** any non-Success result after the helper started blocks Confirm until a later check verifies the client again. `ElevationDenied` and `NotStarted` do not block, because nothing ran.
3. **WinGet:** not moved onto the protocol-2 pipeline by this work. That is a separate decision.
