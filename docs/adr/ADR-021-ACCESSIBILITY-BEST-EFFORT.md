# ADR-021 — Accessibility best-effort priority

**Status:** Accepted

**Date:** 2026-07-28

**Decision owner:** Product owner

**Related requirements:** `AC-UX-01–09`

## Context

PC Steward's primary user does not require accessibility support as a release gate. The existing implementation should keep accessible names, keyboard support, textual status, focus visibility, and Windows display compatibility wherever practical, but missing evidence or an incomplete accessibility exercise must not fail a phase or release gate.

## Decision

All `AC-UX-01–09` acceptance criteria are **Optional — best effort**. Implementation and evidence are encouraged and recorded honestly, but a missing or incomplete `AC-UX-*` criterion is not a gate failure. This decision does not weaken safety, privacy, security, data-loss, integrity, or non-accessibility usability requirements.

## Consequences

Future task and review prompts should attempt accessibility work proportionately but must not mark a phase failed solely for absent `AC-UX-*` evidence. Existing accessibility evidence remains valid as positive quality evidence, not mandatory compliance evidence.

## Reconsider when

The product is distributed beyond its current primary user, a deployment/customer requires accessibility conformance, or applicable policy requires it.
