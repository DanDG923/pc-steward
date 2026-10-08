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
  - detection patterns that are anchored, at most 128 characters, use a restricted grammar and are compiled with the interpreted engine and a 50 ms per-match timeout. The restricted grammar refuses quantified groups other than `?`, backreferences and every `(?` construct. This is the Task 45 amendment, applied in Task 46: `NonBacktracking` construction cost about 5.8 ms per pattern;
  - bounded process names (letters, digits, space, `.`, `_`, `+`, `-`, ending `.exe`);
  - flags (`selfUpdating`, `restartLikely`, `channel`, `automationAllowed`).

  Any validation failure disables catalog features and reports the reason. Inventory and updates continue.
- **User layer.** `user_catalog_entries` rows are written only by the reviewed **Add from search** flow. Search covers WinGet and the fixed Chocolatey Community feed only. The rows go through the same validator. A malformed row is ignored and reported.
- **Not authority.** Neither layer is execution authority. Before approval, each operation is resolved live with an exact ID and version, and the elevated helper revalidates it. The database stores only catalog IDs, versions and hashes, never a copy that later becomes authority. Stage B automation (ADR-025) never reads the user layer directly.
- **Mapping evidence.** Catalog detection is deterministic mapping evidence with origin `catalog`. Ambiguous or non-unique detection yields Review required.
- **Chocolatey confirmation.** A catalog or user-confirmed Chocolatey Community ID, disclosed in the batch review sheet, counts as the explicit Chocolatey mapping confirmation (Q-008).
- **Tooling.**
  - `scripts/Catalog-Validate.ps1` (through `tools/PCSteward.CatalogTool`) runs offline. CI applies the same validator to the bundled file in `PCSteward.Catalog.UnitTests`.
  - `scripts/Catalog-Seed.ps1` is owner-run and opens the database read-only. It writes a draft for review and never overwrites a file.
  - Live verification is owner-run and read-only. It is provided by the Task 45 probe (`winget-preflight`) until Task 48 adds it to the catalog tool, reusing the COM preflight authority.
  - The `msstore` source is refused until a source-agreement review exists (Task 45 §2.5).

## Consequences

### Positive

The owner gets Patch-style install and status for exactly the apps they care about, without a new trust channel.

### Negative / trade-offs

The catalog changes only on redeploy or **Add from search**. Detection rules need occasional owner maintenance.

### Follow-up controls and tests

Task 46 adds hostile catalog fixtures: oversize, duplicate, unknown fields, wrong hash, pathological patterns and pattern timeout. Task 47 proves detection is unique/ambiguous/none and that no low-confidence or ambiguous detection becomes actionable. Task 53 proves that **Add from search** cannot store an unreviewed or non-exact ID.

## Reconsider when

The owner wants catalog updates without redeploying, which would need a signed channel and a new ADR, or a provider stops supporting exact live resolution.

## Task 45 evidence

`spikes/patch-style/SPIKE_REPORT.md` §5 measured `RegexOptions.NonBacktracking` construction at about 5.8 ms per pattern: 3.5 s for 600 patterns, which is too slow for startup.

**Amendment, to be applied by Task 46:** compile detection patterns with the interpreted engine and a 50 ms per-match timeout. Before compiling, the validator rejects restricted grammar:
- quantified groups other than `?`;
- backreferences;
- every `(?` construct.

Version tails use bounded character classes.

**Measured with this design:**
- a 300-entry catalog validated in 12 ms and the detector built in 11 ms;
- 700 records checked in 63–70 ms;
- a pathological pattern that bypassed validation timed out at 49.6 ms;
- all 18 hostile catalog mutations disabled the catalog;
- 0 false positives on the illustrative detection set.

**Further findings:**
- The process-name allowlist must accept `+` (`notepad++.exe`).
- Detection must use the instance publisher from SQLite, because the JSON export has none.
