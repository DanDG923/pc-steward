# Task 29C — Up-to-date repository evidence

## Outcome

The Desktop app now has an **Up to date** route for installed applications whose version has an exact `Current` match from a named trusted repository package. The view presents the installed version, repository, package ID, and the source-specific live/cached freshness statement.

## Evidence boundary

- `InventoryExperienceViewModel.CurrentRepositoryRows` requires the update source to remain enabled, `Current` status, and a non-empty source ID and package ID.
- `Current` is produced by `UpdateIntelligence` only after an equal-version comparison against a `Live` or `Cached` trusted-source snapshot.
- Local-only, unmatched, unavailable, review-required, and update-available rows are excluded.
- Refresh is user initiated, only enabled when an update source is enabled, and reuses the existing source-check path. It does not download or start an update.

## Verification

- Focused Application integration tests: 117 passed, including `CurrentRepositoryRowsRequireExactCurrentStatusAndRepositoryPackageEvidence`.
- Release build: 0 warnings, 0 errors.
- Full suite: 193 passed.
- Release UI Automation observed the named **Up to date** navigation item and the Overview entry point. The observed source evidence stated that the WinGet result was cached and potentially stale; no refresh or update action was invoked. The app closed normally with no residual `PCSteward.Desktop` window/process.

## Requirement coverage

`FR-SRC-03–04`, `FR-UI-01/03–05`, `NFR-REL-01/04`, and `AC-UX-02–06` are recorded in the traceability matrix.

## Residual risk

Cached evidence intentionally remains visible but is labelled stale. This view is evidence-only and does not supersede the separate reviewed-update confirmation path. `PO-EXC-29-PERF-01` remains unchanged and is not a pass claim.
