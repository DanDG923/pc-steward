# ADR-008 — Licence and source availability

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner
**Related requirements:** release definition of done and dependency licence review
**Related open decision:** `OD-08`

## Context

Repository visibility and product licensing affect dependency choices, notices and external distribution but not runtime trust controls.

## Options considered

### Option A — Private repository through private beta

Preserves decision time and matches the specification default.

### Option B — Public source under an approved licence

Improves transparency/contribution potential but needs legal and disclosure readiness.

## Decision

Accept Option A through private beta. The repository and builds make no public-source or open-source claim. Dependency selection must be compatible with private distribution, and any public repository or licence requires an explicit product-owner decision plus updated notices before publication.

## Consequences

No public-source claim; dependency licences must still be compatible with private distribution.

### Follow-up controls and tests

Maintain third-party notices and machine-readable dependency/licence inventory.

## Evidence the upcoming work must collect

- Task 06 dependency/licence constraints.
- Task 30 SBOM, licence scan, notices and distribution model.
- Product-owner approval before any repository visibility or licence change.

## Reconsider when

External beta/publication is planned or a dependency imposes incompatible obligations.
