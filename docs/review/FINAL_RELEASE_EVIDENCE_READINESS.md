# Final release-audit evidence readiness

**Prepared under:** Task 30A — GPT-5.6 Terra / High
**Scope:** evidence preparation only; this document does not grade release readiness or publish a release.
**Implementation baseline:** `396fbda` plus the Task 30A evidence changes in this worktree.
**Evidence date:** 2026-07-30

## Reproducible current-run evidence

- `pwsh -NoProfile -File .\scripts\Build.ps1` — Release build passed with 0 warnings and 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 209 passed: Domain 55, Application integration 124, Architecture 10, Security 20.
- `pwsh -NoProfile -File .\scripts\Benchmark.ps1 -OutputPath docs\review\raw\task-30a-performance.json` — 30-run local fixture median 1.85 ms / p95 2.34 ms; 20-run fixture connector median 15.52 ms / p95 15.96 ms. This is not a final-app or agreed-baseline measurement.
- `dotnet list PCSteward.slnx package --vulnerable --include-transitive --format json` — 17 projects reported and no vulnerable package entries.
- Local tracked-file credential-pattern scan excluding generated output — zero candidate files.
- `scripts\New-PrivateBetaPackage.ps1 -CertificateThumbprint <temporary current-user development certificate>` — generated the ZIP, dependency inventory, CycloneDX 1.5 SBOM, SHA-256 manifest, and development-signing record. The certificate was removed immediately after evidence capture and was never added to a trusted-root store.
- Archive inspection — 86 entries; desktop executable and exact cleanup script present; `PCSteward.ElevatedHelper` absent; ZIP SHA-256 `6E4AAC78E8ABF32796CC302CFB37A8EA2372AA738ADBB19B9C69660BC91B1662`.
- `scripts\Test-StandaloneTaskCleanup.ps1` — created only `PCSteward.ScheduledCheck` and an unrelated sentinel; package cleanup removed only the former; the sentinel remained until the test finally block removed it. Both task names were absent afterward.

Generated, non-source artifacts are under `artifacts\private-beta\reports\` and `docs\review\raw\task-30a-performance.json`. They contain no credentials; the signing report identifies only the temporary certificate subject/thumbprint and returned signature state.

## Criterion-to-evidence readiness

| Requirement IDs | Command, artifact, or prior committed evidence | Result | Status | Owner | Exact next action |
|---|---|---|---|---|---|
| `FR-INV-*`, `AC-INV-01–07`, `NFR-REL-02/05` | `docs/requirements/TRACEABILITY.md`; Phase 1 evidence; current 209-test suite | Provider coverage, conservative inventory and malformed-data regressions remain green. | Proven | Engineering | Independent Task 31 review must rerun critical tests. |
| `FR-ID-*`, `FR-VER-*`, `AC-MAT-01–07`, `TM-01/02/07/09/17` | Task 17–20 traceability; Task 25 review; Architecture/Security tests in current suite | Typed matching/version/evidence gates and zero false-positive fixture coverage are green. | Proven | Engineering | Task 31 independently inspect the executable-authority boundary. |
| `FR-UPD-*`, `AC-UPD-01/02/05/07–11`, `TM-03/12/13` | `docs/review/TASK_90_P3R14_P3R15_EVIDENCE.md`; current 20 security tests | Immutable review, fixed operation/arguments, command rejection, cancellation and post-update outcome regressions are green. | Proven | Engineering | Task 31 rerun the named negative/security suites. |
| `AC-UPD-03`, `AC-UPD-06` | Standalone `UserScopeOnly` composition/security review; authorized `winget upgrade --disable-interactivity --source winget`; local Chrome uninstall registration | The only current candidate is Google Chrome `151.0.7922.71` → `151.0.7922.72`, but its uninstall registration is `HKLM`; it is not eligible for the user-scope test and no update was started. | Missing | Product owner / engineering | Identify a safe, genuinely current-user candidate, then reconfirm immediately before the actual update and verify exact post-update inventory/version. |
| `AC-UPD-04` | ADR-002, ADR-018, standalone security review, machine-scope preparation/executor tests | Revised standalone package fails closed for machine scope and has no helper/UAC path. The former machine-wide UAC behavior is not applicable to this distribution model. | Approved exception | Product owner | Retain ADR-002/018 scope decision; Task 31 verify no helper or machine execution can enter the payload. |
| `FR-DAT-01–03/05–11/13–17`, `AC-DAT-01–11/14–17`, `TM-05/06/08/15/16` | Tasks 14, 24 and 28 evidence; current 209-test suite; Privacy & data accessibility inspection | Existing fixture/hostile-root evidence remains green. Current actual app displayed the category-specific delete/preserve text and did not require elevation. | Proven | Engineering | Task 31 rerun destructive-root and reset escape tests. |
| `AC-DAT-01` retain-data portion | Current standalone payload launch → normal close → relaunch; real existing state remained visible (674 inventory rows) | Package launch/relaunch did not remove `%LOCALAPPDATA%\PCSteward` state. | Proven | Engineering | Preserve this observation in the audit; do not infer factory-reset evidence from it. |
| `AC-DAT-12` remove-data lifecycle | Current two-dialog UI flow, exact `RESET` confirmation and post-reset payload relaunch | Reset closed PC Steward, removed the prior 674-row state, and the next launch produced a fresh 615-row scan. The package payload remained runnable. | Proven | Engineering | Task 31 independently inspect reset escape/partial-cleanup regressions. |
| `AC-DAT-13` post-reset onboarding | Current post-reset actual-app observation | The next launch went directly to Overview with a new scan instead of displaying first-run onboarding. This does not meet the required onboarding observation. | Missing | Engineering | Diagnose and fix the reset/onboarding state transition; repeat the approved destructive lifecycle test. |
| `AC-DAT-18`, ADR-002/018, `TM-04/18` | `scripts\Test-StandaloneTaskCleanup.ps1`; `artifacts\private-beta\reports\task-removal-isolation.json`; archive inspection | Exact task removal and positive unrelated-task isolation passed. ZIP is standard-user-only with no elevated helper; cleanup retains data and tells the user to delete the extracted folder. | Proven | Engineering | Task 31 verify package contents and cleanup script static boundary. |
| Release signing / trusted distribution | `artifacts\private-beta\reports\development-signing.json` | 13 PC Steward-owned files have a cryptographic development signature from a temporary current-user certificate. Its chain is intentionally untrusted; certificate and trust-store changes were removed/never made. | Missing | Product owner / release engineering | Provide the approved release-signing identity and trusted distribution channel; sign and independently verify the release archive/payload. |
| SBOM, dependency and advisory evidence, `TM-18` | `dependency-inventory.json`, `sbom.cdx.json`, `payload-sha256.json`; fresh `dotnet list ... --vulnerable`; local secret scan | SBOM/hash inventory generated; 0 known NuGet advisories; 0 local credential-pattern candidate files. | Proven | Engineering | Task 31 repeat current advisory/secret/static checks and record tool versions. |
| `NFR-PERF-01/02`, `AC-NFR-01/02` | `docs/review/raw/task-30a-performance.json` | Fixture performance measurements are green but host is not the agreed baseline and no live connector was used. | Missing | Product owner / engineering | Repeat on ADR-006 agreed baseline with approved healthy source protocol. |
| `NFR-PERF-05`, `AC-NFR-06` | `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md` | Known five-minute Overview idle CPU result is above budget. | Approved exception | Product owner | Retain `PO-EXC-29-PERF-01`; do not report the requirement as passed. |
| Remaining `NFR-PERF-*`, `NFR-REL-*`, `NFR-MNT-*`, `NFR-OBS-*`, `AC-NFR-03–05/07–11` | Tasks 12, 18, 23, 24, 26, 29 evidence and current full suite | Automated regressions and prior measured host evidence are recorded; current build/test suite is green. | Proven | Engineering | Task 31 rerun the required cancellation, recovery, scheduled-exit, resource and cache critical suites. |
| `docs/DESIGN_DELIVERY_PLAN.md` foundation/Tasks 26–29 visual delivery | Current actual payload visual + UI Automation inspection; Task 29 evidence | Standalone Release app launched and displayed the dark themed Overview; accessibility tree exposed named navigation, update evidence, Privacy & data, and factory-reset control. Retention was visible after restart. | Proven | Design / engineering | Task 31 include this actual-app evidence; do not substitute prototype appearance for accessibility acceptance. |
| `AC-UX-01–09` optional best effort | Current accessibility-tree observations and `PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md` | Named controls and visible text scope were observed. Keyboard, Narrator, high-contrast, scale and full visual-mode matrices remain incomplete. | Missing | Design / engineering | Execute the outstanding accessibility/visual matrix without changing unapproved system settings; retain optional status. |
| VM/manual observations | Product-owner instruction: VMs may be excluded from testing | VM-only evidence is excluded by product-owner instruction. | Approved exception | Product owner | Task 31 must cite the exception rather than infer VM coverage. |

## Current blocking evidence

This package deliberately preserves unresolved evidence rather than grading it away:

1. a production-trusted signing identity/channel is absent;
2. the destructive reset removes prior data, but the required next-launch onboarding did not appear;
3. a real controlled user-scope update is not evidence-backed: the approved candidate query found only a machine-scope Chrome update;
4. the final-app/approved-baseline performance and optional visual/accessibility matrices remain incomplete; and
5. `PO-EXC-29-PERF-01` is a deferral, not a performance pass.

Task 31 must treat any unresolved mandatory item as unverified/blocked rather than Proven. This report is ready to be committed only after the status/traceability update and any remaining approved local actions are either completed or retained as Missing with their exact next action.
