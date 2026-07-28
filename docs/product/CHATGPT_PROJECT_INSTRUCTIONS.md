# ChatGPT Project Instructions — PC Steward

Use these instructions in the ChatGPT Project that contains `PC_STEWARD_PRODUCT_SPEC.md`.

## Project objective

Plan and build **PC Steward**, a local-first Windows 11 application that inventories installed software, checks enabled trusted update sources, explains match confidence and provenance, and performs only explicitly approved supported updates.

## Source of truth

- Treat `PC_STEWARD_PRODUCT_SPEC.md` as the authoritative product specification.
- Preserve its requirement and acceptance-criterion IDs.
- When proposing a change, name the affected IDs and explain whether the specification must be revised.
- Do not silently expand the MVP.

## Working method

1. Separate product decisions, architecture decisions and implementation tasks.
2. Before code, produce an architecture proposal, threat-model update, repository plan and test strategy.
3. Record material decisions as architecture decision records under `docs/adr`.
4. Build the smallest end-to-end vertical slice first.
5. Prefer deterministic rules over AI for inventory matching, version comparison and update authorization.
6. Make assumptions explicit. Use the documented default where an open decision has one.
7. Keep a decision log and a list of known limitations.

## Non-negotiable boundaries

- Windows 11 x64 and one local PC for the MVP.
- No PC Steward account or cloud backend.
- No default telemetry or inventory upload.
- Normal scanning and review run without administrator rights.
- Elevation is on demand and limited to a specific approved update.
- No arbitrary website scraping, raw download URLs or search-engine-selected installers.
- No drivers, BIOS, firmware or Windows Update management.
- No unattended installation in the MVP.
- No continuously elevated service and no permanently running background or tray process.
- Do not use `Win32_Product`.
- Do not accept or persist executable command lines from remote metadata.
- Low-confidence package matches must never become actionable updates.
- Update success requires post-installation verification or a reliable restart-pending state.
- Scan speed, idle resource use, active-scan CPU/memory use, connector concurrency and cache size must meet the product specification's measurable MVP budgets.
- **Clear scan data**, **Clear downloaded files** and **Factory reset PC Steward** are separate mandatory MVP actions; do not collapse them into a generic clear-all function.
- Destructive cleanup may target only internally allowlisted PC Steward data roots and must resist path traversal, junction and reparse-point attacks.
- Factory reset removes all local PC Steward state and the current-user scheduled task, returns to onboarding, and leaves the app binaries installed.

## Engineering expectations

- Use C# and the current supported .NET 10 LTS patch unless an approved ADR changes this.
- Prefer WinUI through the current stable Windows App SDK for the desktop UI.
- Use SQLite for local persistence.
- Isolate inventory providers, source connectors, matching/version logic and update execution behind interfaces.
- Treat registry values, package metadata, command output and the local database as untrusted input.
- Avoid shell command concatenation; use structured arguments and allowlisted provider operations.
- Write tests for normalization, version comparison, identity matching and provider parsing before enabling update execution.
- Build repeatable benchmark tests for cold start, idle CPU/memory, local scan, online scan, cancellation and process exit.
- Model scan-derived data, user decisions, source metadata, logs/history and PC Steward-owned downloaded files as separate lifecycle categories.
- Write hostile-fixture tests for cleanup paths, locked files, junctions, symbolic links and partial factory reset.
- Use transactional migrations, structured sanitized logging and bounded retention.
- Add no production dependency without explaining its purpose, licence and maintenance status.

## Review standard

A proposed implementation is not acceptable merely because it works on one PC. It must preserve:

- source provenance;
- zero false-positive actionable matches in the fixture test set;
- least privilege;
- local privacy;
- failure isolation;
- post-update verification;
- user control;
- measurable resource efficiency; and
- path-safe, accurately reported data deletion and factory reset.

## First task to give Codex

> Read `PC_STEWARD_PRODUCT_SPEC.md` and these project instructions. Do not write production code yet. Produce: (1) a proposed repository structure, (2) a component architecture and data-flow description, (3) a threat model covering package identity, elevation and destructive data cleanup, (4) the first ADRs that need decisions, including performance measurement, cache ownership and factory-reset mechanics, (5) a fixture-based functional, security and performance test strategy, (6) a data-lifecycle design that keeps scan data, decisions, metadata, logs and downloaded files separate, and (7) an ordered implementation backlog that delivers the inventory-only vertical slice first. Map every item to the relevant requirement IDs and call out any specification conflict before resolving it.
