# ADR-023 — Personal catalog

**Status:** Accepted
**Date:** 2026-10-08
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-CAT-01–08`, `FR-INS-01/02/06`, `FR-ID-05/06`, `FR-SRC-09`, `AC-V3-01/02`
**Related decisions:** D-006, D-007, Q-004, Q-008

## Context

The v0.3 Patch-style updater needs a list of known apps that includes apps that are not installed, so the owner can install them, see colour-coded status and opt them into automatic updates. PC Steward today knows only installed software and the package identities that providers match. `AGENTS.md` forbids executing package IDs obtained blindly from remote metadata or the database.

## Decision drivers

- No new execution authority: a catalog must not become a command or URL channel.
- A small, personal maintenance burden for one owner on one PC.
- Deterministic detection that never makes a low-confidence match actionable.
- Fail-closed behaviour that never breaks inventory or updates.

## Options considered

### Option A — Bundled personal catalog plus reviewed user additions

A JSON catalog is embedded in the owner-signed payload and seeded from the owner's own exact WinGet/Chocolatey matches. The owner extends it in the app by adding reviewed WinGet or Chocolatey Community search results.

### Option B — Repository search only

There is no catalog, and every install starts from a live search. Search offers no stable detection rules, no categories and no curated `automationAllowed` switch. Every session repeats the review.

### Option C — Large generic catalog with a signed download channel

A large catalog suits many users, but needs curation tooling, key management and an update channel that a personal tool does not need.

## Decision

Accept Option A.

- **Bundled layer.** `catalog/pcsteward-catalog.v1.json` is an embedded resource in the new `PCSteward.Catalog` project, which validates it on every load:
  - schema version, bounded fields and unique `pcs:` IDs;
  - provider/source/package identity allowlists;
  - detection patterns that are anchored, at most 128 characters, compiled with `RegexOptions.NonBacktracking` and given a timeout;
  - bounded process names;
  - flags (`selfUpdating`, `restartLikely`, `channel`, `automationAllowed`).

  Any validation failure disables catalog features and reports the reason. Inventory and updates continue.
- **User layer.** `user_catalog_entries` rows are written only by the reviewed **Add from search** flow. Search covers WinGet and the fixed Chocolatey Community feed only. The rows go through the same validator. A malformed row is ignored and reported.
- **Not authority.** Neither layer is execution authority. Before approval, each operation is resolved live with an exact ID and version, and the elevated helper revalidates it. The database stores only catalog IDs, versions and hashes, never a copy that later becomes authority. Stage B automation (ADR-025) never reads the user layer directly.
- **Mapping evidence.** Catalog detection is deterministic mapping evidence with origin `catalog`. Ambiguous or non-unique detection yields Review required.
- **Chocolatey confirmation.** A catalog or user-confirmed Chocolatey Community ID, disclosed in the batch review sheet, counts as the explicit Chocolatey mapping confirmation (Q-008).
- **Tooling.**
  - `scripts/Catalog-Validate.ps1` runs offline and in CI.
  - `scripts/Catalog-Seed.ps1` and `scripts/Catalog-VerifyLive.ps1` are owner-run and read-only.

## Consequences

### Positive

The owner gets Patch-style install and status for exactly the apps they care about, without a new trust channel.

### Negative / trade-offs

The catalog changes only on redeploy or **Add from search**. Detection rules need occasional owner maintenance.

### Follow-up controls and tests

Task 46 adds hostile catalog fixtures: oversize, duplicate, unknown fields, wrong hash, pathological patterns and pattern timeout. Task 47 proves detection is unique/ambiguous/none and that no low-confidence or ambiguous detection becomes actionable. Task 53 proves that **Add from search** cannot store an unreviewed or non-exact ID.

## Reconsider when

The owner wants catalog updates without redeploying, which would need a signed channel and a new ADR, or a provider stops supporting exact live resolution.
