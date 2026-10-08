# Personal catalog

`pcsteward-catalog.v1.json` is the bundled personal catalog (`FR-CAT-01–08`, ADR-023). It is embedded in `PCSteward.Catalog` at build time.

## Rules

Any validation error disables catalog features. Inventory and updates keep working.

- **Document:** `schemaVersion` must be `1`. `catalogVersion` uses the form `YYYY.MM.DD[.n]`. There can be 1–1000 entries and the file is at most 1 MiB.
- **Strict JSON:** no unknown fields, duplicate properties, comments or trailing commas.
- **Entry ID:** `pcs:` followed by lower-case letters, digits and `-`, unique across the catalog.
- **Category:** one of Browsers, Communication, Media, Imaging, Documents, Developer, Utilities, Compression, CloudStorage, Security, Runtimes, Gaming.
- **Providers:** one or two, at most one per kind, exactly one `"default": true`.
  - `winget` with source `winget`, scopes `user` and/or `machine`. The ID has 2–8 dot-separated segments of letters, digits, `+`, `_` and `-`.
  - `chocolatey` with source `chocolatey-community`, scope `machine` only.
  - `msstore` is refused for now.
- **Detection:**
  - Optional anchored `arpDisplayName`/`arpPublisher` patterns, at most 128 characters each. A publisher pattern needs a name pattern.
  - Patterns use a restricted grammar: no quantified groups other than `?`, no backreferences, no `(?` constructs.
  - Write version tails as `[0-9.]{1,32}`.
  - Provider identity (WinGet correlation or Chocolatey registration) is always used first.
- **Never include** commands, installer switches, scripts, paths or execution URLs. `homepage` is display-only and must be `https`.

## Editing workflow

1. Draft entries from your own PC. This step is read-only and never overwrites an existing file:
   `pwsh -NoProfile -File .\scripts\Catalog-Seed.ps1 -OutputPath .\catalog-draft.json`
2. Review the draft. Fix names, publishers, categories and scopes, and add process names. Then merge the entries you want into `pcsteward-catalog.v1.json` and bump `catalogVersion`.
3. Validate the result:
   `pwsh -NoProfile -File .\scripts\Catalog-Validate.ps1`
4. Run the full test suite, which re-validates the bundled file, then rebuild and redeploy.

## Current content

**Status:** the file holds 20 starter entries from the Task 45 sample. Their IDs have **not** been live-verified. The owner should replace or confirm them using the seed draft and the read-only probe (`spikes/patch-style/Invoke-PatchStyleProbe.ps1`). The catalog never grants execution authority: each item is resolved live with an exact ID and version before any approval.
