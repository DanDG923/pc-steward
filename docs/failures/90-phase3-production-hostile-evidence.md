# Task 90 — Phase 3 production hostile-cleanup evidence resolved

**Date:** 2026-07-29

## Goal

Close `P3R-03` with the required production cleanup primitive evidence for symbolic-link, scratch-VHD/volume-mount, and concurrent ancestor/root replacement cases.

## Completed local-PC evidence

1. Added a deterministic production-cache fixture that binds the fixed root handle and only then replaces an ancestor with a junction to an outside sentinel. The operation fails closed as `UnsafePath`; the sentinel stays unchanged.
2. Retained and reran production junction fixtures for reparse root, final component, and repeated ancestor components.
3. Ran the approved elevated `HostExceptionEvidence.Tests.ps1` fixture on this PC. It created only file/directory/root links and a new 32 MB scratch VHD under a generated temporary root, then ran each against the production cache primitive: `PASS: 14 assertions; production cache and legacy spike rejected file/directory/root links and scratch VHD mount; sentinels survived.`
4. Added deterministic production root-swap coverage alongside the ancestor-swap test; focused evidence passed 22 tests.

## Resolution

The existing product-owner-approved local-PC exception supplied the required isolated elevated environment. Every fixture remained beneath a unique temporary root, the VHD carried only its sentinel and was detached, and teardown removed the temporary paths. The previous standard-host skip is retained only as a non-elevated limitation, not as a missing gate prerequisite.

## Smallest next step

Commission a fresh Task 25 Sol/Extra High review against the evidence set; do not merge Phase 3 or start Task 26 before that review passes.

## Changed files

- `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs`
- `src/PCSteward.Storage/DownloadedArtifactCache.cs`
- `tests/PCSteward.Application.IntegrationTests/WingetInstalledInventoryProviderTests.cs`
- `tests/PCSteward.Application.IntegrationTests/DownloadedArtifactCacheTests.cs`
- `docs/PLANS.md`
- `docs/STATUS.md`
- `docs/requirements/TRACEABILITY.md`
- `docs/review/TASK_90_P3RRR_EVIDENCE.md`
- `docs/failures/90-phase3-production-hostile-evidence.md`
