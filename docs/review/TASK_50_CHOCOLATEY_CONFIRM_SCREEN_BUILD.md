# Task 50 — Chocolatey Confirm screen: build record (G9-D4)

`FR-UPD-15`, `FR-UPD-01/03–09`, `FR-UI-03/04`, `FR-HIS-01/03`, `TM-03/04/07/13`. Built from the [design note](TASK_50_CHOCOLATEY_CONFIRM_SCREEN_DESIGN.md) with the owner's three §9 answers: the check may be at most 10 minutes old (C2); C6 blocks after any non-Success result where the update started, but not after a declined prompt or an update that never started; WinGet is unchanged.

## 1. What was built

| Piece | Project | Role |
|---|---|---|
| `ChocolateyClientUpdateAdmission` | Application | C1: one compiled constant, read only through `IsOwnerApproved`. |
| `ChocolateyClientUpdateReviewSession` | Application | The one-use review. `PrepareAsync` freezes the client operation from the verified release, the compiled list's offer and a fresh review observation, then saves the draft. `ConfirmAsync` re-checks the press-time conditions and runs the workflow at most once. |
| `ChocolateyClientUpdateAttention` | Application | C6 over recent history (50 rows). Unreadable history fails closed. |
| `ChocolateyClientUpdateRunPresentation` | Application | Progress and result text. A recorded result shows the history text of its own record. |
| `ChocolateyClientUpdateViewModel` (changed) | Application | The §4.2 review states, the change and expiry lines, "Check again", and the primary button binding. |
| `ChocolateyDesktopObservation.ObserveClientReviewAsync` | Provider.Chocolatey | The review observation: one client-mode observation of exactly the verified listed release. |
| `ChocolateyClientUpdateProducers` | Provider.Chocolatey | Public façade over the N6 composition (F′ source and client verifier over one baseline store, plus the review observation). |
| `DataOperationLease.RunReviewedUpdateAsync` | Storage | The current-user gate with reconciliation for the workflow, refused once the review deadline has passed. |
| `ChocolateyClientUpdateComposition` | Execution | The single production composition (constructed once in `App.xaml.cs`); it exposes only `CreateSession`. |
| `ClientRouteOnlyDispatcher` | Execution | Admits only the exact client shape, then defers to the route dispatcher (which also checks the deployment capability). Reports the progress points. |
| `ChocolateyClientUpdateReviewDialog` | Desktop | Drives one open dialog: check, prepare, Confirm, progress, result. Refuses to close while a run is in flight; the main window's close is refused too. |

## 2. Where each condition is enforced

| Condition | Display (view model) | On the press (session) | Inside the workflow and helper |
|---|---|---|---|
| C1 owner admission | not-admitted reason | `NotAdmitted` refusal; nothing is prepared while it is off | — |
| C2 check age (10 min) | expired reason | `CheckTooOld` refusal | — |
| C3 an offer exists | no-offer reasons | the frozen operation is built only from the offer | F′ re-observes the client |
| C4 split token limited | split-token reasons | the token is probed again | — |
| C5 deployment capability | — | — | `Supports` and the transport |
| C6 earlier update needs attention | attention reason | history is read again | — |
| C7 the gate (no other task) | (shown only as a press-time refusal) | — | gate with reconciliation, claim CAS |
| C8 live review | expired reason | `ReviewedUpdateReview.Confirm` (30 min) | the one-use permit |

## 3. Deviations from the design note

1. After a result, the dialog offers "Check again" instead of re-checking at once, so the result stays readable.
2. The "Waiting for permission" and "Running" points share one status line: the helper exchange is one call, so the two cannot be told apart from the Desktop.
3. C7 has no display state yet: the "another task is running" reason is wired but always false, and the gate refuses at the press.
4. The primary button is hidden when the installed client is verified and nothing is offered (§4.2 table).
5. A history row whose integrity cannot be verified counts as needing attention (C6 fails closed).
6. The change and expiry lines are shown only while the review is live and the check is fresh.

## 4. Admission and enabling

The owner approved real Chocolatey client updates (obligation 8). The constant is enabled in its own commit after the locked build, so the switch is reviewable on its own. Confirm still cannot turn on until a second reviewed release is on the compiled list: today the list holds only 2.7.5, so nothing is offered. Both admission values stay under test through test-only factories (`OpenWithAdmissionForTests`, `CreateWithAdmissionForTests`), which no production code calls (architecture-pinned).

Before anything runs for real on the owner's PC: a full security review (independent review plus our own threat review) and its fixes, then the ripgrep install step (G9-T1).

## 5. Tests

- Application `ChocolateyClientUpdateReviewSessionTests`: frozen shape, one run, a second press while running, every preparation mismatch, every press-time refusal, the 10-minute boundary, C6 clearing after a later check, and live text equal to history. These run over the real workflow and SQLite store.
- Application `ChocolateyClientUpdateAdmittedViewModelTests`: the §4.2 rows, the C6 rule and the result texts. `ChocolateyClientUpdateViewModelTests` pins the locked screen.
- Security `ChocolateyClientUpdateCompositionTests`: the review observation, and the client-only dispatcher refusing the package route before the inner dispatcher is asked.
- Architecture: one composition and one constructor site per piece; the session as the only caller of `Confirm` for the client route and of the workflow; no production admission override; the dialog's close guards.
