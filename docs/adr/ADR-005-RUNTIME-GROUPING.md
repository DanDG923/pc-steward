# ADR-005 — Runtime and redistributable grouping

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-INV-07`, `FR-INV-09`, `AC-INV-02`
**Related open decision:** `OD-05`

## Context

Aggressive grouping can hide distinct architecture, scope, side-by-side or servicing instances.

## Options considered

### Option A — Conservative identity-first grouping

Preserve raw and install instances; group only with stable evidence.

### Option B — Product-family heuristics

Cleaner UI but higher merge/error risk.

## Decision

Accept Option A. Preserve provider records and distinct install instances. Classification and reversible presentation grouping may label runtimes, but only stable provider identity may deduplicate the same instance; name, publisher or family heuristics cannot merge scope, architecture, channel or side-by-side instances.

## Consequences

Inventory can look verbose; correctness and diagnosis are preserved.

### Follow-up controls and tests

Architecture/scope/side-by-side fixtures and reversible presentation grouping.

## Evidence the upcoming work must collect

- Task 04 fixture catalog for runtime, duplicate, x86/x64 and user/machine cases.
- Task 11 deduplication precision and stable-identity tests.
- Task 15 review proving no distinct fixture is hidden.

## Reconsider when

Stable provider identifiers demonstrate a safe family relationship and UI evidence shows material usability harm.
