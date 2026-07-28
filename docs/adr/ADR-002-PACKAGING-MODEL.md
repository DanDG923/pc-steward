# ADR-002 — Packaging model

**Status:** Proposed
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

More integration flexibility; requires disciplined installer/update/signing controls.

## Decision

Prefer Option A only if the Task 02 spike proves every required interaction; otherwise choose Option B. Status remains Proposed.

## Consequences

### Positive

The choice is evidence-based and preserves a common application/domain core.

### Negative / trade-offs

Phase 0 must maintain two disposable packaging probes.

### Follow-up controls and tests

Packaging-specific code remains outside domain; Task 30 reruns lifecycle evidence.

## Evidence the upcoming spike must collect

- Install/launch/upgrade/uninstall and retain/remove-data behavior.
- Current-user storage, Task Scheduler, notifications and WinGet access.
- Separate helper launch/UAC/signing boundary and no resident process.
- Factory reset without deleting binaries.
- Tooling and clean Windows 11 VM prerequisites.

## Task 02 evidence

`spikes/winget-packaging/SPIKE_REPORT.md` records that WinGet is available to the current user, but this Windows 10 host lacks the target OS evidence and native packaging/signing tooling. Neither MSIX nor unpackaged PC Steward lifecycle behavior was executed. The spike provisionally favours Option B because a separate signed helper and an explicit retain/remove-data uninstall choice fit its boundary more directly; this is a recommendation only, not a status change. A clean Windows 11 VM must prove both lifecycle matrices before Task 05 can lock this ADR.

## Reconsider when

Windows packaging capabilities or deployment requirements materially change.
