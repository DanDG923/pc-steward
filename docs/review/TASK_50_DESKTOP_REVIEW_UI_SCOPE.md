# Task 50: Desktop review/history UI and production intent resolver (scope)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `FR-UI-03`, `FR-UI-04`, `TM-03/04/07/13`. Sources: [composition record](TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) "Slice N6" review M1 and "Admission obligations (consolidated, after N6)" items 5 and 6; [N6 scope](TASK_50_N6_SCOPE.md).

## What exists

- Application: `ChocolateyReviewDisclosurePresentation.Present` → `ChocolateyReviewDisclosureView` (every review field, closed unavailability reasons, `CanConfirm` false today); `ReviewedUpdateHistoryPresentation.Present` → `ReviewedUpdateHistoryView` (`Route`, `VerificationDetail`, `LingeringProcessesNote`, `HelperReport`); `SqliteDatabase` implements the read-only `IReviewedUpdateHistoryReader`.
- Provider: `ChocolateyReviewedRouteComposition` (internal, constructed by no `src` code) with `ResolveReviewIntentAsync(resolver, …)` and `ProbeSplitTokenState`; no production resolver.
- Desktop: `MainWindow.cs` is code-built (no page XAML). It reviews only the WinGet `ReviewedUpdateExecutionWorkflow` path, has no reviewed-dispatch history list and no Chocolatey review. No test project references the Desktop.
- No `src` code can create a `ReviewedUpdateOperation` for `ChocolateyMachineRuntimeLimitsV3`: that needs the composition's fresh observation, which stays unconstructed.

## Chosen approach

1. **Obligation 6, production resolver** (`Provider.Chocolatey/Native/ChocolateyReviewIntentResolver.cs`): for the exact selected package ID it calls the existing `ChocolateyUpdateSourceConnector.ResolveAsync` (existing process runner and fixed `outdated` arguments; no new process construction). It stamps its own resolution time from the injected clock when resolution starts, caps the call, and returns null on any error, timeout, cancellation, missing, ambiguous, non-Community or non-exact candidate. The composition gains an overload that takes the resolver and package ID and calls it immediately before the observation. Neither the resolver nor the composition is constructed in `src`, so the architecture pins stay unchanged.
2. **Obligation 5, view models** (Application, portable): `ChocolateyReviewedRouteReviewViewModel` exposes every disclosure field as a bound property and as ordered display fields. It probes the split-token state through an injected function on every `Open`. `IsConfirmEnabled` equals `CanConfirm`. `TryConfirm` goes only through `ReviewedUpdateReview.Confirm` (which rechecks) and returns null whenever `CanConfirm` is false. Without a reviewed operation (the only production case today), `ChocolateyReviewDisclosurePresentation.PresentWithoutReview` gives the same view type with the fixed disclosures and the "not admitted" plus split-token reasons. `ReviewedUpdateHistoryItemViewModel` exposes every history field; `ReviewedUpdateHistoryListViewModel` loads through the reader and fails closed to an "unavailable" state.
3. **Desktop, thin**: a new `ReviewedRouteSurfaces.cs` renders the view models. Two small `MainWindow` hooks:
   - the application detail page of a Chocolatey Community row gets a "Chocolatey reviewed update (unavailable)" button. It opens a dialog that renders every field, with a disabled Confirm button (`IsPrimaryButtonEnabled = IsConfirmEnabled`); the confirm handler only calls `TryConfirm` and never dispatches;
   - the Activity page gets a read-only "Reviewed update outcomes" list.
   
   `App.xaml.cs` passes the database as `IReviewedUpdateHistoryReader` and a public split-token probe (`ChocolateyReviewedRouteProbe`, Provider.Chocolatey). The probe forwards to `ProbeSplitTokenState` and reads only the desktop token.

## What stays non-admitted

- `HasSupportedMode`, `DesktopProducersBoundaryTests`, `DeploymentCapabilityBoundaryTests` and `HelperEndpointBoundaryTests` are unchanged.
- No `src` code constructs the composition, the dispatch workflow or the route dispatcher. No UI path can create a Chocolatey operation or dispatch one. Confirm is disabled whenever `CanConfirm` is false (always today).

## Tests

- Security fixture (portable): resolver success (clock time, exact arguments, one call) and every fail-closed case; the composition overload resolves before observing and keeps the resolver's time.
- Application integration (portable): every bound review property for a matching intent, a lib-bad intent and the no-review case; the probe is called on every `Open`; Confirm stays disabled and `TryConfirm` is null (including with a split token); every history property per outcome; the list's fail-closed load.
- Architecture (new file): no `src` caller constructs the resolver; the resolver's only provider call is `ChocolateyUpdateSourceConnector.ResolveAsync`; the Desktop's only Chocolatey review entry is the probe; the route stays non-admitted.
- The Desktop code cannot be built on Linux; it is validated only by Windows CI.
