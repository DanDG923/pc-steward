# ADR-002 — Packaging model

**Status:** Provisional
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-DAT-12`, `AC-DAT-18`, sections 13–15
**Related open decision:** `OD-02`

## Context

Deployment must support per-user storage, WinGet, Task Scheduler, notifications, on-demand UAC, a separately signed helper, upgrade identity, uninstall data choice and factory reset.

## Decision drivers

- Least privilege and signing
- Windows App SDK compatibility
- Reliable install/upgrade/uninstall/reset

## Options considered

### Option A — Signed MSIX main application

Strong identity/lifecycle; package boundaries may complicate helper, WinGet, storage or uninstall-data choice.

### Option B — Signed unpackaged application with traditional installer

More integration flexibility; requires disciplined installer/update/signing controls and installation-time elevation for a protected binary root.

## Decision

Provisionally select Option B: a signed unpackaged Windows App SDK application installed by a signed traditional installer, with a separately signed helper executable. Per-user data, settings and lifecycle choices remain per-user, but executable binaries that participate in elevation must be installed below the fixed machine-protected `%ProgramFiles%\PC Steward` root. A caller-writable per-user binary root is prohibited because pre-launch signature validation cannot prevent same-user replacement before `runas`. Task 06 may scaffold unpackaged application and helper boundaries, but must not claim install, signing, upgrade or uninstall support.

Option A is rejected for the current implementation path because no evidence shows that MSIX can satisfy the separate helper, current-user Task Scheduler and retain/remove-data uninstall requirements on the target lifecycle. This is a provisional choice, not permission to distribute unsigned binaries.

## Consequences

### Positive

The choice is evidence-based and preserves a common application/domain core.

### Negative / trade-offs

The installer lifecycle, protected-root ACL and code-signing path remain unproven until supported clean Windows environment evidence exists. The current developer output is intentionally non-actionable for elevation.

### Follow-up controls and tests

Packaging-specific code remains outside domain. Before external beta, Tasks 25/30 must prove install, protected-root ownership/ACL and non-reparse layout, launch, helper/UAC, scheduled-task, upgrade, rollback, uninstall-retain-data and uninstall-remove-data cases on supported clean Windows 11 x64 environments. The environment may be physical or virtual. Failure of any required interaction reopens this ADR before distribution.

## Evidence the upcoming spike must collect

- Install/launch/upgrade/uninstall and retain/remove-data behavior.
- Current-user storage, Task Scheduler, notifications and WinGet access.
- Separate helper launch/UAC/signing boundary and no resident process.
- Factory reset without deleting binaries.
- Tooling and supported clean Windows 11 environment prerequisites.

## Task 02 evidence

`spikes/winget-packaging/SPIKE_REPORT.md` records that WinGet is available to the current user, but its environment report identified Windows 10 while Task 04 later captured Windows 11 Home. Neither capture is a recorded clean acceptance environment, and the discrepancy is unresolved. No PC Steward package lifecycle was executed. The spike favours Option B because a separate signed helper and an explicit retain/remove-data uninstall choice fit its boundary more directly.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` demonstrates a strict authenticated, nonce/job-bound, two-minute no-op helper message with fixed operation/provider/source and a reviewed package allowlist. Unknown fields, request mutation, replay, expiry and cancellation were exercised without starting a process or requesting UAC. This supports the selected separate short-lived helper boundary but does not prove packaging, signing, IPC ACLs or UAC lifecycle; those limitations are why this ADR is Provisional.

## Task 90 Phase 3 review remediation

`P3R-15` proved the prior current-build sibling-helper path was unsigned and caller-writable, so post-connect signer validation occurred too late to prevent arbitrary elevation. The production launch policy now resolves only `%ProgramFiles%\PC Steward\PCSteward.ElevatedHelper.exe` from a desktop running at the exact sibling installed path. It checks the whole path for reparse components, probes the current token for write/delete/DACL/owner authority over the files and every ancestor, verifies both Authenticode signatures and exact signer equality, and repeats the check immediately before `runas`. Current developer output and an absent protected install fail closed before UAC. A clean installed-path replay remains required; this ADR stays Provisional.

## Reconsider when

MSIX proves the full required lifecycle without weakening helper, task or data-choice semantics; the traditional installer cannot meet a required clean-environment lifecycle case; or Windows packaging capabilities materially change.
