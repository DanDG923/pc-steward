# ADR-004 — GitHub Releases MVP scope

**Status:** Proposed
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-SRC-06`, trusted-source Tier C
**Related open decision:** `OD-04`

## Context

Official GitHub Releases can improve read-only awareness but is desirable, not mandatory, and must not delay or weaken the core WinGet flow.

## Options considered

### Option A — Exclude from core MVP

Keeps the mandatory source and matching surface smaller.

### Option B — Add after Phase 2 passes

User-confirmed repository mapping, version intelligence only, followed by a repeated gate.

## Decision

Propose Option A for the core path. Option B may be approved only after the first successful Task 20 review. Status remains Proposed.

## Consequences

Core delivery is lower risk; some applications remain unmanaged.

### Follow-up controls and tests

No asset download/execution, inferred repository or arbitrary search is permitted.

## Evidence the upcoming work must collect

- Task 20: core WinGet stability and remaining value/coverage gap.
- Optional Task 18B: repository identity, rate limits, malformed tags, prerelease and stale-cache fixtures.
- Repeated Task 20 review with added scope.

## Reconsider when

Core Phase 2 is green and product owner explicitly values the additional read-only coverage.
