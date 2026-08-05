# Task 38 review-required decision actions

**Date:** 2026-08-05
**Branch:** `phase/4-beta`
**Requirements:** `FR-ID-04–06`, `FR-POL-01–03/05`, `FR-UI-03–04`, `AC-MAT-02/05/07`, `AC-UX-05`
**Outcome:** IMPLEMENTATION AND AUTOMATED VALIDATION PASS; live visual verification and deployment remain uncollected.

## Reported defect

The redesigned result and Review actions pages correctly kept four uncertain matches non-executable, but their cards exposed only `Open evidence` or `Review evidence`. Safe decision controls existed lower in the application-detail route and manual candidate selection was hidden in Technical details. The pages therefore said that a decision was needed without presenting the available decisions where the user encountered the item.

The first correction covered the later Review actions page only. User follow-up screenshots then proved that the earlier Inventory results entry point still rendered its old `Open evidence` card and that cached/offline exact matches needed a contextual source-refresh action before identity confirmation could become safe. A final screenshot isolated a different Microsoft Store case: the package identity was exact and freshly read, but the repository had not supplied an available version. That was not a user decision at all, even though it was being labelled `review required` and showing pin/ignore controls.

## Implemented behavior

Every review-required card on both Inventory results and Review actions now states and exposes its safe next actions:

- a single live, exact source identity can be confirmed through an explicit confirmation dialog;
- an unresolved item with multiple trusted source candidates opens a structured candidate picker without parsing display text or choosing automatically;
- an exact match blocked by cached, offline, or unavailable source evidence exposes `Refresh source evidence` directly on its result/review card and detail page;
- every item can pin its installed version through a confirmation dialog;
- every item can be ignored indefinitely or until a selected date through a confirmation dialog;
- full evidence and the same decision controls remain available through `Review details`.

An exact, high-confidence package identity whose repository did not establish update availability is now classified as `Update evidence unavailable`, not `Needs your decision`. It appears in the separate Unavailable result/update group, exposes `Refresh source evidence`, displays the repository's partial-result warning in plain language, and removes map/pin/ignore controls from the primary next-step card. A retry may obtain usable availability evidence; until then there is no identity or update decision for the user to approve.

The contextual refresh checks all enabled approved repositories and then rebinds an open detail page to the refreshed row. It does not confirm a mapping automatically. A fresh, unique, version-bound result can expose identity confirmation; an offline or availability-incomplete result remains refreshable, and a genuinely incomparable non-null version remains non-executable even when live.

Optional reasons are retained for confirming, pinning, ignoring, and manual mapping. All source/package values passed to the workflow come from typed presentation records and are revalidated against the approved source cache before a mapping is stored. Decision actions are reversible and the refresh changes source metadata only; none creates an update job, launches an update provider, requests elevation, or changes an installed application.

## Scenario evidence

| Scenario | Expected behavior | Evidence | Result |
|---|---|---|---|
| Node.js or Vortex has one cached/offline exact WinGet row and publisher evidence is ambiguous | Offer `Refresh source evidence`, pin, ignore, and details; do not expose confirmation from a newer stale candidate | Cached and unavailable exact-identity scenarios plus two-entry-point/detail architecture contract | PASS |
| The same exact WinGet row is returned once with live, version-bound evidence | Replace the freshness blocker with `Confirm package identity`; confirmation records only a reversible mapping | Live exact-identity scenario | PASS |
| A registry product has more than one equally supported package candidate | Offer `Choose package identity`; preserve source ID and package ID as typed fields and never select automatically | Structured two-candidate view-model scenario plus detail-picker architecture contract | PASS |
| Microsoft Store identity is exact with a non-null but incomparable version | Offer refresh while stale; after a live refresh still offer only pin/ignore and never claim version ordering | Cached-to-live incomparable-version scenario | PASS |
| Microsoft Store identity is exact and live, but the repository supplied no available version | Classify as `Update evidence unavailable`, remove it from Needs decision, disclose the partial source result, and offer refresh rather than identity/pin/ignore decisions | Domain missing-availability scenario, presentation scenario, and unavailable-result/detail UI contract | PASS |
| Any review-required card is shown on Inventory results, Review actions, or detail | Reuse the same refresh/confirm/choose/pin/ignore/details controls where applicable; preserve the detail route through refresh; never call update execution from a review action | Three-surface architecture contract | PASS |

## Validation

- Focused `InventoryExperienceViewModelTests`: **20/20**.
- Focused three-surface UI contract: **6/6**; full Architecture suite: **25/25**.
- Release build: **PASS**, 0 warnings and 0 errors.
- Corrected normal-host official full suite: **271/271** — Domain **55/55**, Application integration **170/170**, Architecture **25/25**, Security **21/21**.
- The initial managed-sandbox full run caught and led to correction of one UI-thread field read, then reproduced the already documented sandbox-only native-storage failures. The unchanged corrected suite passed on the normal Windows host.

No PC Steward executable was launched by Codex, no protected installation was replaced, and no application, schedule, source setting, inventory, or PC Steward owner data was changed during this correction. The user-supplied screenshots record the successive pre-fix behaviors; a post-fix live capture still requires a launch of this rebuilt Release output or a separately authorized deployment.
