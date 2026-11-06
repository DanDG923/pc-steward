# Task 50 slice N6 — UI/history scope

Requirements: `FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `TM-03/04/07/13`. Sources: [design rev 6](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) table row N6, §(L6), settle-window rev-4 L4; [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md) "UI must disclose before review"; [fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md) §4.2–§4.3; [composition record](TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) "History projection", "Helper-check history", N5 obligations.

## Inventory (what already exists)

- **History projection** (`Application/ReviewedUpdateHistory.cs`): `ReviewedUpdateHistoryPresentation.Present` → `ReviewedUpdateHistoryView` with fixed title, status per outcome, installer-mode text (Selected vs package-script-controlled), candidate-evidence text (UserCacheIntent = provisional), runtime-limit disclosure, blocking flag, sanitized diagnostic and the §B.6 helper report (`HelperReport`). Strict v2/v3 terminal parsing and integrity checks are done.
- **Disclosure** (`Application/ReviewedUpdateProviderDisclosure.cs`): the Chocolatey Community script-runtime limits text for the runtime-limit route only.
- **Workflow messages** (`Application/ReviewedUpdateDispatchWorkflow.cs`): fixed verification messages (target not detected, verification did not finish, `ClientNotVerifiedMessage`, `RestartContractViolationMessage`) recorded as the terminal diagnostic; they are string literals in places and not classified in history.
- **Producers** (`Provider.Chocolatey/Native`, N5): `ChocolateyProfileEvidenceSource` (F′ + `ObserveReviewIntentAsync`, intent time `IntentResolvedAt`, fixed `UserCacheDisclosure`), `ChocolateyReviewedUpdateVerifier`, `ChocolateyDispatchBaselines`. No `src` code constructs them (`DesktopProducersBoundaryTests`).
- **Desktop**: `MainWindow.cs` reviews and renders only the older WinGet `ReviewedUpdateExecutionWorkflow` path; no reviewed-dispatch history list and no Chocolatey review exists. `HasSupportedMode` admits only `WinGetUserExactV3`.
- **Not present**: the lingering-installer disclosure, the `lingering-descendants-terminated` history text, the split-token (L6) unavailability reason, a review view that shows the intent's own time, verification-detail rendering, and a composition point sharing one baseline store.

## What N6 adds

1. **Review disclosure model** (Application, pure): `ChocolateyReviewDisclosurePresentation.Present(operation, intent facts, split-token state)` → view with the Profile V2 user-cache text verbatim, the intent's own resolved time, the runtime-limit disclosure, the package-script mode text, the lingering-process disclosure (design rev-4 L4 wording), the lib-bad residue note, and availability. Availability = `HasSupportedMode(operation)` ∧ split token ∧ intent matches the frozen selection; every failing conjunct contributes a fixed reason (not admitted pending owner host evidence; UAC off / standard user; elevated desktop; token unsupported/unknown; review data mismatch). `CanConfirm` is false whenever any reason exists — always, today.
2. **History** (extend `ReviewedUpdateHistoryPresentation`, no new projection): appended `Route` text, `VerificationDetail` (closed classification of the workflow's fixed verification messages, now public constants), and `LingeringProcessesNote` when the recorded diagnostic carries the fixed token. The workflow keeps the helper's token in the recorded message on the settled/verified path (it is otherwise dropped); the token never changes an outcome.
3. **Composition prep** (obligations a/b): internal `ChocolateyReviewedRouteComposition` in `Provider.Chocolatey/Native` creates ONE `ChocolateyDispatchBaselines` shared by the evidence source and the verifier, and a fresh-intent supply path that requires the supplied intent's resolution time to fall inside the supply call, then observes immediately. A pure mapping `TokenVerdict` → split-token state feeds the review. Nothing in `src` constructs the composition; it supplies no dispatcher.

## Not done (out of scope)

- No route admission: `HasSupportedMode`, `Supports` and dispatcher composition are unchanged; no UI path can dispatch Chocolatey.
- No Desktop/XAML change: the Desktop has no reviewed-dispatch review or history surface to extend, and the route cannot be reviewed; wiring the views into `MainWindow` is follow-up UI integration once admitted (or once a history list is scoped).
- No helper-side emission of `lingering-descendants-terminated` (N4 helper work); N6 only carries and renders it.
- No persistence/schema change.

## Tests

- Application integration (portable): every review string and reason, availability conjunct-by-conjunct, `CanConfirm` false for the runtime-limit route even with a split token, intent time shown as given; history route/verification/lingering texts per message and per outcome, token survives workflow finalization into history via real SQLite.
- Security fixture (portable): composition shares one baseline instance (F′ baseline consumed by the verifier → Success path), fresh-intent supply refuses stale/pre-call/future times, verdict mapping.
- Architecture: producers constructed only inside the composition; nothing in `src` constructs the composition; the route stays non-admitted (`HasSupportedMode` source pin + behavioural test).
