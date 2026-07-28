# ADR-003 — WinGet integration route

**Status:** Proposed
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

Keep a provider interface and choose the most stable supported route proven by Task 02. CLI is acceptable only with a fixed executable, structured argument list, bounded output and capability-gated parsers. Status remains Proposed.

## Consequences

Adapters can change without changing domain behavior; both routes require explicit error semantics.

### Follow-up controls and tests

Golden parsers/capability fixtures, timeout/cancellation and arbitrary-argument rejection.

## Evidence the upcoming spike must collect

- Installed and available-upgrade fields, Store/source behavior, architecture/scope and stable IDs.
- Supported structured output versus locale-sensitive fallback.
- Version/capability detection, exit codes, timeouts, cancellation and sanitization.
- Non-executed update fixtures proving shell-free construction.

## Reconsider when

A supported API clearly supersedes the selected route or WinGet output compatibility breaks.
