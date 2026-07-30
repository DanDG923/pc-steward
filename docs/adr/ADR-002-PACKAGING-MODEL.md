# ADR-002 — Packaging model

**Status:** Accepted
**Date:** 2026-07-28; revised 2026-07-30
**Decision owners:** Product owner and security reviewer
**Related requirements:** `FR-DAT-12`, `AC-DAT-18`, sections 14–15
**Related open decision:** `OD-02`

## Context

The first private beta needs a reproducible Windows x64 package, per-user storage, WinGet inventory/source access, Task Scheduler integration, explicit removal semantics and no ambient privileged process. Earlier work provisionally selected a traditional protected-root installer because machine-scope updates used a separately signed elevated helper. `P3R-15` proved that helper is safe only from a signed, non-caller-writable `%ProgramFiles%\PC Steward` layout; a caller-writable portable helper cannot safely cross UAC.

The product owner subsequently selected a standalone package that does not require installation and approved revision and security re-review. That choice cannot retain machine-scope update execution.

## Options considered

### Option A — Signed standalone ZIP, standard-user-only

The desktop and scheduled host run from an extracted folder. The package omits the elevated helper. User-scope updates retain the existing immutable review/execution/verification path; machine-scope updates are non-actionable.

### Option B — Signed traditional protected-root installer

Supports the previously reviewed elevated helper but requires installer, signing, upgrade and uninstall lifecycle evidence.

### Option C — Caller-writable standalone package with sibling elevation helper

Rejected. A same-user attacker can replace caller-writable code before `runas`; signature checks cannot make that layout a protected installation.

## Decision

Accept Option A for the MVP private beta.

- Deliver a reproducible `win-x64` ZIP; no install registration is required.
- Omit `PCSteward.ElevatedHelper` from the desktop project graph and release payload.
- Compose `UpdateExecutionCapabilities.UserScopeOnly` in the production desktop.
- Reject a machine-scope candidate before structured execution authority or immutable job creation.
- Retain a second executor-level denial if a machine-scope job reaches the standalone executor.
- Preserve read-only machine inventory and update awareness; “not actionable in this standalone package” is not “current” or “safe to run.”
- Remove the exact `PCSteward.ScheduledCheck` task with the bundled cleanup script. The script does not delete data.
- Retain per-user data by default. The existing two-confirmation in-app factory reset is the only remove-data path before deleting the extracted package folder.
- Replace the package manually for self-update; no automatic updater is added.

Option B remains a possible future deployment only after a new scoped decision, protected-root lifecycle evidence and security review. Option C is prohibited.

## Security consequences

The private-beta package contains no product path that requests UAC. The historical elevated-helper implementation remains tested source code but is not production-composed or packaged. A modified caller-writable binary can ask Windows for UAC like any arbitrary local program, but it receives no trusted PC Steward helper capability; Windows consent remains the operating-system boundary. Remote metadata, persisted jobs and package mappings cannot enable elevation because both preparation and execution fail closed for machine scope.

The standalone package does not protect its own binaries from same-user modification. Distribution signing and published hashes remain required outside the developer’s machine, and users must replace the complete package from the approved channel rather than mixing files.

## Removal and replacement evidence

Before private beta, Task 30/30A must prove on the approved test machine:

- ZIP extraction and launch as standard user;
- payload contains desktop/scheduled host and no elevated helper;
- user-scope update negative/controlled fixtures remain constrained;
- machine-scope review creates no job and invokes no execution authority or UAC;
- exact scheduled-task cleanup is idempotent and does not delete unrelated tasks;
- retain-data replacement preserves the per-user root;
- factory reset followed by package deletion removes app state, task and extracted binaries;
- light/dark/high-contrast identity assets and release hashes/SBOM are present.

VM execution is not required by product-owner direction. Unexecuted machine observations remain Missing, not Pass.

## Historical disposition

The former provisional traditional-installer decision and the exact protected-root `P3R-15` evidence remain valid for that historical/future boundary. They do not authorize a helper in the standalone package and no longer block the MVP package choice.

## Reconsider when

Machine-scope update execution becomes mandatory, user-scope-only behavior is unacceptable, a Store/MSIX deployment is selected, or standalone integrity/removal evidence fails. Any restoration of elevation requires a new protected deployment decision and fresh security gate.
