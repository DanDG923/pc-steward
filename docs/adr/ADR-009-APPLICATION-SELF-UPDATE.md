# ADR-009 — Application self-update

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** section 14.3, compromised-product-update threat
**Related open decision:** `OD-09`

## Context

An automatic self-updater creates a second executable supply chain before signing/distribution is settled.

## Options considered

### Option A — Manual signed release

User obtains and runs a signed new package through documented steps.

### Option B — Built-in automatic updater

Convenient but needs channel, signing, rollback and privileged replacement controls.

## Decision

Accept Option A for the MVP and first private beta. PC Steward has no built-in self-update execution path; releases use a documented manual upgrade with signed artifacts once signing and distribution exist.

## Consequences

Updates are less convenient; attack surface and Phase 0 scope stay bounded.

### Follow-up controls and tests

Display current version/channel and document authenticated release verification.

## Evidence the upcoming work must collect

- Task 30 signing availability, package upgrade behavior and release-channel evidence.
- Isolated-environment manual upgrade preserving data.
- A new threat model/ADR before automatic self-update.

## Reconsider when

Signing and stable distribution are established and manual updates impair beta safety.
