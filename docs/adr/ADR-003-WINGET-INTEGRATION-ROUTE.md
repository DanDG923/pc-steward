# ADR-003 — WinGet integration route

**Status:** Provisional
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-INV-05`, `FR-SRC-01–05`, `FR-UPD-02–04`, `NFR-MNT-03`; v0.3 amendment: `FR-INS-01–04`, `TM-22`
**Related open decision:** `OD-03`

## Context

WinGet is the only actionable MVP provider. Its route must support capability detection, inventory, source metadata, cancellation and typed update execution.

## Options considered

### Option A — Constrained CLI adapter

Broad availability; output/version behavior and process management require evidence.

### Option B — Supported programmatic API

Potentially stronger typing; deployment/support surface may be more complex or incomplete.

## Decision

Provisionally select a split route behind provider interfaces:

- Option A remains the constrained execution and display adapter. It uses the fixed `winget.exe` identity, structured argument arrays, capability detection, bounded output, timeout/cancellation and fail-closed parsers. Table output may inform inventory display only and never authorizes execution.
- Option B is now the sole candidate-version authority. The production adapter uses Microsoft's supported `Microsoft.WindowsPackageManager.ComInterop` projection, selects only the predefined `winget` or `msstore` catalog, requires a predefined trusted catalog, queries an exact package-ID filter with a two-result bound, rejects zero/duplicate/limit-exceeded/fuzzy results, and preserves structured installed and default-candidate versions from the exact source catalog.

The authority accepts only bounded source/ID/version shapes, requires exactly one newer version, propagates caller cancellation, applies a 20-second query bound and converts API/activation/catalog failures to no authority. The application then independently requires exact source, package ID, installed version and candidate version equality with current local evidence and the fresh display mapping before creating review/job/provider authority. The actual desktop composes this adapter; no display parser implements `IStructuredUpdateCandidateAuthority`.

## Consequences

Adapters can change without changing domain behavior; both routes require explicit error semantics. Windows-targeted projects compile against the Windows 11 `22000` minimum, matching the documented x64 MVP baseline and the compatible official COM projection.

### Follow-up controls and tests

Golden parser/capability fixtures, locale/malformed-output rejection, source-failure isolation, timeout/process-tree cancellation, output bounds and arbitrary executable/argument rejection are mandatory in Tasks 10, 18, 21 and 25.

## Evidence the upcoming spike must collect

- Installed and available-upgrade fields, Store/source behavior, architecture/scope and stable IDs.
- Supported structured output versus locale-sensitive fallback.
- Version/capability detection, exit codes, timeouts, cancellation and sanitization.
- Non-executed update fixtures proving shell-free construction.

## Task 02 evidence

`spikes/winget-packaging/SPIKE_REPORT.md` records WinGet `v1.29.280` and confirms local help support for exact ID, source, version, architecture, scope and non-interactive arguments. The observed `list` and `upgrade` help does not advertise structured output, so table parsing is never execution authority. A supported programmatic route was not evidenced on this host. Installed/upgrade/Store probes require separate explicit authorization because they may disclose installed-software metadata to external sources. Their absence and the lack of structured output evidence make this ADR Provisional.

## Task 90 Phase 3 review remediation

`P3R-14` found that the available-version display column still crossed from the table connector through source matching into the immutable `--version` argument. The first remediation removed that authority but left the desktop universally non-actionable. The final remediation adds and composes `WingetStructuredUpdateCandidateAuthority` over the official WinGet COM API while retaining all table-only negatives. Production-adapter tests cover trusted/predefined/exact/single/newer acceptance and untrusted, wrong-source, wrong-ID, duplicate, limit, malformed version, stale/equal/older and stalled-query rejection. Architecture evidence fixes the actual desktop composition; application integration keeps exact installed/candidate matching, explicit review and targeted post-update verification separate.

## Reconsider when

The official COM contract or compatible projection disappears; required CLI execution capabilities disappear; parser compatibility breaks; supported Windows baselines change; or clean-environment evidence shows the split route cannot meet source isolation and exact-ID execution requirements.

## Task 45 evidence (v0.3)

`spikes/patch-style/SPIKE_REPORT.md` §2 dumped the shipped `Microsoft.Management.Deployment.winmd`, version 1.10.340. The COM API provides:
- `InstallPackageAsync` and `UpgradePackageAsync` with structured `InstallProgress`, and typed `InstallResultStatus` (including `PackageAgreementsNotAccepted`);
- `InstallOptions.PackageVersionId` for exact versions;
- `CatalogPackageMetadata.Agreements`;
- `GetApplicableInstaller`, which returns `ElevationRequirement`, `Scope`, `InstallerType` and `Architecture`.

**Amendment (applied by Task 48, 2026-10-09):**
- **Preflight: COM.** `WingetInstallPreflightAuthority` resolves installs through the in-process COM engine. It requires:
  - one exact `Id` match in the trusted predefined `winget` catalog, with no installed instance;
  - the default (or, on revalidation, the exact approved) version from the `winget` source, with no channel;
  - an applicable installer under a strict scope filter and the requested architecture;
  - bounded agreements, hashed into the job (`TM-22`).

  The COM query sets only `PackageInstallScope`, `PackageInstallMode` and `AllowedArchitectures`.
- **Execution: structured CLI for now.** Install execution uses `winget install --id <id> --exact --version <v> --source winget --scope <s> --architecture <a> --silent --disable-interactivity`. `--accept-package-agreements` is added only when the job carries an accepted agreement hash and fresh preflight evidence matches the job. COM execution (`InstallPackageAsync` with `PackageVersionId`) replaces the CLI only after the elevated-COM evidence in spike §2.4 exists; Task 49 or the owner probe decides.
- **Forbidden.** The `InstallOptions` members and CLI flags in spike §2.2, plus `AcceptSourceAgreements` and `LogOutputPath`, are forbidden. `ProviderOperationBoundaryTests` enforces this across all product sources.
- **Routing.** `ElevationRequired` and `ElevatesSelf` installers route to the elevated session (`UpdateJobSelection.RunsElevated`). `ElevationProhibited` with machine scope is not actionable.
- **Display output** never becomes authority. The existing upgrade path is unchanged.
