# ADR-003 — WinGet integration route

**Status:** Provisional
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-INV-05`, `FR-SRC-01–05`, `FR-UPD-02–04`, `NFR-MNT-03`
**Related open decision:** `OD-03`

## Context

WinGet is the only actionable MVP provider. Its route must support capability detection, inventory, source metadata, cancellation and typed update execution.

## Options considered

### Option A — Constrained CLI adapter

Broad availability; output/version behavior and process management require evidence.

### Option B — Supported programmatic API

Potentially stronger typing; deployment/support surface may be more complex or incomplete.

## Decision

Provisionally select Option A behind the provider interface. The adapter uses the fixed `winget.exe` identity, structured argument arrays, capability detection, bounded output, timeout/cancellation and fail-closed parsers. Table output may inform inventory display only; it never authorizes execution. An actionable job must carry an exact source and package ID already validated by deterministic rules.

Option B is rejected for the current path because Task 02 found no supported programmatic route available to prove. The abstraction remains so a later supported API can replace the adapter without changing domain behavior.

## Consequences

Adapters can change without changing domain behavior; both routes require explicit error semantics.

### Follow-up controls and tests

Golden parser/capability fixtures, locale/malformed-output rejection, source-failure isolation, timeout/process-tree cancellation, output bounds and arbitrary executable/argument rejection are mandatory in Tasks 10, 18, 21 and 25.

## Evidence the upcoming spike must collect

- Installed and available-upgrade fields, Store/source behavior, architecture/scope and stable IDs.
- Supported structured output versus locale-sensitive fallback.
- Version/capability detection, exit codes, timeouts, cancellation and sanitization.
- Non-executed update fixtures proving shell-free construction.

## Task 02 evidence

`spikes/winget-packaging/SPIKE_REPORT.md` records WinGet `v1.29.280` and confirms local help support for exact ID, source, version, architecture, scope and non-interactive arguments. The observed `list` and `upgrade` help does not advertise structured output, so table parsing is never execution authority. A supported programmatic route was not evidenced on this host. Installed/upgrade/Store probes require separate explicit authorization because they may disclose installed-software metadata to external sources. Their absence and the lack of structured output evidence make this ADR Provisional.

## Task 90 Phase 3 review remediation

`P3R-14` found that the available-version display column still crossed from the table connector through source matching into the immutable `--version` argument. Update preparation now requires a separate bounded structured-candidate authority whose live result exactly matches source ID, package ID and candidate version and is independently validated as a supported newer version. The WinGet table connector does not implement that authority; malformed, localized, injected-spacing and extra-column rows remain display evidence only. Because no supported production structured available-version route has been evidenced, the actual desktop fails closed before review/job/runner creation. This preserves the provisional CLI adapter for inventory display and post-update structured export verification without claiming actionable update support.

## Reconsider when

A supported API proves equivalent inventory, source, cancellation and execution behavior; required CLI capabilities disappear; parser compatibility breaks; or clean-environment evidence shows the route cannot meet source isolation and exact-ID execution requirements.
