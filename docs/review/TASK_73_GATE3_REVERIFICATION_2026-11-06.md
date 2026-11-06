# Task 73 on the activated Task 50: design §8.6 gate 3 re-verification

Date: 2026-11-06. Requirements: `FR-UPD-15`, `FR-UI-03/04`, `TM-03/04/07/13`; ADR-031 (decision 7), design §8.6 gate 3; [activation record](TASK_50_ACTIVATION_CHANGE.md) §6 merge condition (review L10).

## 1. What was done

- The Task 73 branch `claude/task-73-ui-overhaul` merged the Task 50 head `5285cfb` (branch `claude/sweet-franklin-j3mw4a`, which carries the activation `d423dff`/`ebb4061` and Profile V2 rev 2.1 `bc24a20`/`5606da2`). Merge, not rebase, as in the earlier Task 50 → Task 73 merges, so no history is rewritten and PR #13 stays stacked on #12.
- Git merged every file without conflicts. The Task 50 changes to the files both branches touch (`ChocolateyClientUpdatePresentation.cs`, `MainWindow.cs`, `ReviewedUpdateDispatchWorkflow.cs`, `ChocolateyReviewDisclosure.cs`, `ReviewedRouteViewModels.cs`) are doc comments only. The Domain and provider changes (`IsChocolateyClientIdentity`, `IsClientTarget`) are additive.

## 2. Gate 3: the Desktop offer comes only from the verified observer

The condition: with the client route admitted in code, the Task 73 "Update Chocolatey" offer must still come only from the verified installed-client observation, and nothing on that surface can confirm or dispatch.

| Check | Result | Evidence |
|---|---|---|
| The offer is computed only from `ChocolateyInstalledClientVerification.Verified`, never from the scan-reported version | Holds (unchanged by Task 50) | `ChocolateyClientUpdateViewModel`; `Verified`'s single `src` caller is pinned (`ChocolateyClientUpdateDesktopUiBoundaryTests`) |
| Confirm cannot be enabled after activation | Holds | `NotAdmittedPendingReview` is added unconditionally in the view model's constructor; it is **not** derived from `HasSupportedMode`, so the activation does not remove it. Pinned by source in `ChocolateyClientUpdateDesktopUiBoundaryTests` (the reason is never removed; `CanConfirm => UnavailableReasons.Count == 0`) |
| The view model holds no operation, review, draft, permit or confirm path | Holds | Reflection test in `ChocolateyClientUpdateViewModelTests` and the forbidden-target IL scan |
| Nothing in the Desktop composes a Chocolatey client review or dispatch | Holds | Task 50's `DesktopProducersBoundaryTests.TheRuntimeLimitRouteIsAdmittedOnlyByShapeAndStaysUncomposed` and `ClientRunFaultBoundaryTests.TheClientSelfUpdateRouteIsAdmittedOnlyThroughTheThreeActivationPoints` now scan the merged `src`, including the Task 73 Desktop code |
| The display check is not a dispatch input | Holds | Any future job still re-observes at F′ and stages B/D (design §8.6, residual) |

Release list: still 2.7.5 only, so on the owner's PC a verified 2.7.5 shows "No newer reviewed Chocolatey release is available" and no offer.

Side effect of Profile V2 rev 2.1 (`bc24a20`): the Desktop check uses the same observer, so on a host whose config holds `containsLegacyPackageInstalls = true` it can now verify the installed client instead of refusing at step 6. That gives a verified release, never an offer, while the list has one release. The empty `cacheLocation` host precondition still refuses, and the dialog then says "PC Steward could not verify the installed Chocolatey".

## 3. Changes made in the merge

- **Test that activation broke:** `ChocolateyClientUpdateViewModelTests.TheRouteStaysNonAdmittedAndTheViewModelHasNoConfirmPath` asserted `HasSupportedMode` refuses the client shape and the sheet shows `UnavailableText`. It is now `TheAdmittedRouteStillGivesTheViewModelNoConfirmPath`: it asserts the activated gate admits the exact shape and the sheet has no unavailable reason (as Task 50's own `ChocolateyClientUpdatePresentationTests` does). It then asserts, for a verified release with a newer listed one and for every unverified result, across every split-token state, that the not-admitted reason comes first, `CanConfirm` and `IsConfirmEnabled` are false, and the offer exists only for the verified result. This is the gate-3 re-verification as a test.
- **Build break avoided:** `ChocolateyClientUpdateDesktopUiBoundaryTests` had a `<see cref>` to `ClientRunFaultBoundaryTests.NothingInSrcAdmitsTheClientSelfUpdateRoute`, which the activation replaced. With `TreatWarningsAsErrors` and documentation generation, that is CS1574. It now points at `TheClientSelfUpdateRouteIsAdmittedOnlyThroughTheThreeActivationPoints`.
- **Doc comments** in `ChocolateyClientUpdateViewModel`, `ChocolateyInstalledClientVerification`, `ChocolateyClientUpdateSurfaces`, `MainWindow` and the boundary test no longer say the route is not admitted. They say why Confirm stays off: the unconditional reason, no Desktop composition, owner admission still outstanding. No pinned source string changed.

## 4. Verification and limits

- No .NET SDK in this cloud container (the SDK download hosts are blocked by the network policy), so nothing was built or run here. Proof is Windows CI on PR #13. A failed CI run on this merge is the expected place to find anything this review missed.
- No host action. Before any deployment that includes this merge, gate 3 is met by the checks above and the green CI run; the activation record's other conditions (obligation 8, the owner's final admission approval) are unchanged.
- Not merged to `main`.
