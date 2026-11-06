# Chocolatey package updates: Confirm screen (design addendum)

Date: 2026-11-06. Status: **design, owner decision taken; built in the same change.** Addendum to the [Chocolatey Confirm design note](TASK_50_CHOCOLATEY_CONFIRM_SCREEN_DESIGN.md) and its [build record](TASK_50_CHOCOLATEY_CONFIRM_SCREEN_BUILD.md).

Requirements: `FR-UPD-15`, `FR-UPD-01/03–09`, `FR-UI-03/04`, `FR-HIS-01/03`, `TM-03/04/07/13`; `AC-UX-01–09` best effort (ADR-021). Builds on [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md), the [activation record](TASK_50_ACTIVATION_CHANGE.md) and the [G9 target plan](TASK_50_G9_TARGET_PLAN.md) (§6a, §11, §12).

## 1. Why

The client Confirm screen can only offer an update to a newer reviewed Chocolatey release. The Community feed's newest Chocolatey is 2.7.5, which is the only release on the compiled list, so the client screen has nothing to offer and the G9 real launch could not start. The owner chose (2026-11-06) to **extend Confirm to Chocolatey package updates** instead: route `UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3`, with ripgrep 14.1.0 → 15.2.0 as the first package (G9-D2b). That is the package-route plan G9 already described (P1–P4, G9-T1 to G9-T5). The client screen is unchanged and still waits for a second reviewed release.

**In scope:** the Confirm path for one Chocolatey Community package on the runtime-limit route, from the package's detail page.

**Out of scope:** the client self-update screen, WinGet, batch updates, the scheduled host, any change to the helper, the deployment capability, the scripts or the files Task 50 pins, and any action on the owner's PC.

## 2. What is reused unchanged

Every authority-bearing step is the one the client screen already uses or the package route already had:

- the review gate (`ReviewedUpdateReview.HasSupportedMode` → `ReviewedChocolateyRouteAdmission.Admits`, the exact package shape, never a client identity);
- the 30-minute review lifetime and the one-use `Confirm()`;
- the reviewed dispatch workflow: the current-user gate with reconciliation, F′ (a fresh Desktop-role observation of the frozen installed version), the claim CAS, the one-use permit, the route dispatcher's `Supports` and the deployment capability, one protocol-2 exchange, settlement, the package verifier and history;
- the helper's own stages B–D, where stage C confirms the exact candidate from protected machine data before anything is updated;
- the user-cache intent supply path (`ChocolateyReviewIntentResolver` over the existing `ChocolateyUpdateSourceConnector` query, then `ChocolateyReviewedRouteComposition.ResolveReviewIntentAsync` with its in-call time rule and the producer's own validation);
- the Profile V2 disclosure (`ChocolateyReviewDisclosurePresentation.Present`): the provisional user-cache candidate and its check time, the fixed user-cache disclosure, the package-script mode, the script-runtime limits, running installers and any lib-bad residue.

## 3. When Confirm turns on (package route)

| # | Condition | Package-route meaning | Re-checked on press |
|---|---|---|---|
| C1 | Owner admission | Its own compiled constant, `ChocolateyPackageUpdateAdmission.OwnerApproved`, enabled in its own commit. Separate from the client constant because package scripts run as administrator: either can be turned off alone | Yes |
| C1a | One admitted package | A compiled allowlist with one entry, `ChocolateyPackageUpdateAdmission.AdmittedPackageId` (`ripgrep`, ordinal, exact case). The entry, the session (before any check and again at freeze) and the composed dispatcher all refuse any other ID. It is widened only after G9-T4 passes, in its own reviewed commit | Yes (frozen ID) |
| C2 | The check is at most 10 minutes old | The check is the user-cache candidate check of this dialog (its resolved time). The review is frozen only if that check is at most 2 minutes old (existing rule) | Yes (age) |
| C3 | An offer exists | The user cache reports a stable, strictly newer candidate for exactly the selected package ID, and a fresh Desktop observation finds exactly the installed version | — (F′ and stage C re-check) |
| C4 | Split token limited | Probed when the review opens and again on the press | Yes |
| C5 | Deployment capability | The protected statement lists `Update/ChocolateyMachineRuntimeLimitsV3` | Inside the workflow |
| C6 | Earlier update needs attention | An earlier Chocolatey reviewed update (package or client route) that started and did not succeed, recorded after this check started. Unreadable history, or a row whose integrity cannot be verified, fails closed. A declined prompt or an update that never started does not block (owner decision 2, unchanged). Said plainly: the dialog's own check always starts after any earlier failure, so closing and reopening the dialog clears C6. In practice it blocks only a failure recorded while this review is open. A broken earlier run is still caught by helper stage B and by the lib-bad residue disclosure, not by C6 | Yes |
| C7 | The gate | No other task holds the data-operation gate | Inside the workflow |
| C8 | Live review | 30 minutes, one use | Yes |
| C9 | Trusted sources are on | The check asks Chocolatey Community about the selected package ID, as a scan does, so it runs only while the owner's trusted-sources setting is on | When the dialog opens |

## 4. The screen

- The entry stays on Chocolatey Community rows only. While C1 is off, it is exactly today's read-only disclosure.
- With C1 on, the entry reads "Review Chocolatey update…". The dialog checks the selected package, then shows the Profile V2 disclosure lines, the change ("ripgrep 14.1.0 → 15.2.0") and when the review expires, and every reason Confirm is off.
- Confirm reads "Update <package>". Close is the default button, so Enter and Esc never confirm.
- After Confirm: "checking again", then "waiting for permission / running", then "verifying". While the run is in flight, Close is hidden and the main window refuses to close (the client screen's guard).
- The result shows the history text of its own record. "Check again" starts a new review; a used review is never reused.

## 5. Composition

- `ChocolateyPackageUpdateProducers` (Provider.Chocolatey, public façade): one N6 composition (F′ source and verifier over one baseline store) and the production intent resolver over the existing connector. It is the only constructor site of the resolver.
- `ChocolateyPackageUpdateComposition` (Execution): constructed once in `App.xaml.cs`; it exposes only `CreateSession`. Each session gets its own producers, capability reader, transport, route dispatcher and workflow.
- `PackageRouteOnlyDispatcher` admits only the exact package shape (runtime-limit route, its guard contract, no client identity), then defers to the route dispatcher. The client composition's `ClientRouteOnlyDispatcher` still refuses the package route, so each composition dispatches only its own route.
- `ChocolateyPackageUpdateReviewSession` (Application): prepare (check, freeze from the checked intent only, save the draft, start the review) and Confirm (C1, C2, C4, C6, C8, then one workflow run off the UI thread, never cancelled from the screen).

No scan value becomes a version or an argument: the row's package ID only selects which package to check, and the frozen operation is built from the checked intent, whose ID must match it exactly.

## 6. What still has to happen before a real run

1. The full security review of the enabled build (owner condition), and its high and medium findings fixed.
   - That includes a Codex security review of this change itself.
   - Security review M1 (code-loading settings such as `COR_*` and `APPDOMAIN_MANAGER_*` reach Chocolatey's elevated child processes through its environment reload) is narrowed, not closed, by the H1 guard, and the Native AOT helper does not change it. Before G9-T4, the owner's ADR-025 residual-risk acceptance has to name code-loading settings explicitly. That is the owner's decision.
   - The H1 helper change (merged before this one) means the helper must be redeployed before G9-T1 in any case.
2. G9-T1 (the owner installs ripgrep 14.1.0 in their own elevated window, pinned to the nupkg SHA-256 in the G9 plan §12), G9-T2 and G9-T3 (redeploy with this build; a new probe run if the binding set changed: the G-0 condition is that the composition is deployed only after a recorded probe PASS). Each is its own approval.
3. G9-T4: one real update through this screen.

Nothing in this change approves or runs a host step.

## 7. Build notes

- Shared with the client screen and unchanged: the stage, refusal, result and progress types (`ChocolateyClientUpdateReviewStage`, `ChocolateyClientUpdateRefusal`, `ChocolateyClientUpdateRunResult`, `ChocolateyClientUpdatePhase`), the history read, the gate and the store.
- The session measures C2 and C6 from its own check start, which is no later than the intent's resolved time; the intent must also have been resolved after that start, and its candidate must be strictly newer (checked again in Application, before anything is saved).
- Without a limited split token the dialog shows the reason and checks nothing: no user-cache query runs and no draft is saved.
- A row whose package ID is not the admitted package (C1a) never shows the admitted entry either; it keeps today's read-only disclosure.
- A row whose package ID is a Chocolatey client identity (`chocolatey`, `chocolatey.extension`, any case) never shows the admitted entry; the session, the composed dispatcher and the review gate refuse it as well.
- Tests keep both admission values covered through test-only factories (`CreateWithAdmissionForTests`, `OpenWithAdmissionForTests`), which no production code calls (architecture-pinned).
