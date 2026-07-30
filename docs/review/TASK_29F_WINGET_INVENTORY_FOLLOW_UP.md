# Task 29F — WinGet inventory scan follow-up

## Trigger

The recorded host completed a new scan with the readable local completion time from Task 29E, but the desktop still displayed the generic `WinGet installed inventory:failed` warning. The first follow-up build safely exposed `winget:executable-unavailable`, confirming that the non-elevated desktop could not enumerate the protected WindowsApps directory.

## Remediation

- The WinGet display-inventory boundary now catches a launch/read fault and returns a bounded local category: executable access denied, executable unavailable, executable launch failed, or runtime error. Exception text is not surfaced.
- Each parsed installed record is independently mapped. A malformed record is skipped with `winget:invalid-installed-record`; already accepted rows remain available to the scan.
- The resolver now reads the current user's Windows package catalog for the registered `Microsoft.DesktopAppInstaller` package with the expected Microsoft publisher ID, rather than enumerating protected WindowsApps. Its fixed-root, reparse-point, and Authenticode checks remain mandatory before it can return `winget.exe`.
- If a source-scoped WinGet inventory query fails but the ordinary installed inventory succeeds, the connector falls back only to exact records bearing that source label. It does not guess a source from a display name. If the availability query failed, its version remains unknown and the resulting match is review-only rather than current.
- The Review actions route now shows `Needs a decision` separately from approved updates. Application activation opens the prototype contract's full-page detail fallback: summary, installed/source comparison, plain-language reasoning, decisions, and a collapsed technical disclosure.
- `Current` now requires a trusted high-confidence identity and an equal source version. An incomplete-publisher alias remains review-only; its local installer version is not presented as a downgrade comparison against the package version.
- A review-required detail now visibly offers `Confirm package identity` for the displayed exact source package. Confirmation records a reversible manual mapping, immediately re-assesses the status, and never starts an update.
- A confirmed mapping may use the source's equal installed/available version to classify an otherwise incompatible registry installer-version string as current; it does not make the mapping actionable. Review actions can bulk-confirm only these source-reported-current identities, skipping newer, unknown, unavailable, and conflicting source records.
- The post-bulk-confirm outcome dialog is shown before the review page is re-rendered, avoiding the observed host WinUI crash. Completed mappings remain persisted and repeated confirmation does not create duplicates.
- Cancellation still propagates, so the orchestrator keeps its existing timeout and close semantics.

## Verification

- Focused offline WinGet provider tests: 16 passed.
- Resolver regression test: 1 passed.
- Current-status safety test: 1 passed.
- Detail/review route regression test: 1 passed.
- Release build: 0 warnings, 0 errors.
- Full offline suite: 205 passed.

## Recorded-host follow-up — complete

The user rechecked the rebuilt Release desktop. Repository inventory, detail decisions, bulk confirmation and the post-confirmation result flow all behaved as intended. The disclosure-gated source check remains read-only; it may send installed package identifiers to enabled package sources, and it never begins an update. No performance pass claim is made; `PO-EXC-29-PERF-01` remains unchanged.
