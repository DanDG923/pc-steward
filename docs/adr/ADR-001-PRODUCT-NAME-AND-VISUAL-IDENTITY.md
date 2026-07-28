# ADR-001 — Product name and visual identity

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `OD-01`, stable application identity in section 14.3
**Related open decision:** `OD-01`

## Context

Code, package identity, data paths and user-facing copy need stable internal naming before release branding is final.

## Decision drivers

- Stable upgrade/data identity
- Avoiding costly code/path renames
- Accessible, non-alarmist presentation

## Options considered

### Option A — Keep PC Steward throughout

Lowest implementation churn; trademark and user research remain unproven.

### Option B — Stable technical identity with replaceable display branding

Use `PCSteward` for namespaces/package/data identifiers and isolate user-facing name/assets.

## Decision

Accept Option B. Use `PCSteward` for namespaces, package identity, executable identity and app-owned roots. Keep the display name and visual assets centralized and replaceable; display “PC Steward” until product-owner approval. A display-brand change must not migrate or rename execution authority, package identity or data roots.

## Consequences

### Positive

Branding can change without migrating execution authority or data roots.

### Negative / trade-offs

Technical identifiers may differ from the eventual display name.

### Follow-up controls and tests

Task 06 must centralize display metadata; package upgrade tests must prove identifier stability.

## Required follow-up evidence

- Task 06: count hard-coded display-name occurrences and prove centralization.
- Task 30: package/upgrade identity and accessible visual-asset evidence.
- Product owner: name/trademark and visual approval before external beta.

## Reconsider when

The product owner approves a different name before package identity is externally distributed.
