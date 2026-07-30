# Task 30 release evidence

**Requirements:** `AC-DAT-18`, ADR-002, ADR-008–010/018, `TM-04/18`
**Status:** standalone-package preparation; on-machine lifecycle observations remain pending.

| Area | Evidence | Result |
|---|---|---|
| Package model | `New-PrivateBetaPackage.ps1` publishes `win-x64`, adds required WinUI XBF/PRI resources, emits reports and creates a ZIP | Implemented; initial payload crashed with missing XBF/PRI resources; corrected payload remained running for the five-second observation. |
| Privilege boundary | Desktop composes `UserScopeOnly`, executor has no elevated transport, project/payload omit elevated helper | Focused application/security/architecture regressions pass. |
| Machine-scope denial | Preparation stops before structured authority/job creation; executor independently rejects machine scope | Proven by controlled tests; on-machine no-UAC observation pending. |
| Removal | `Remove-PCSteward.ps1` refuses while product processes run and removes only `PCSteward.ScheduledCheck`; folder deletion removes binaries | Exact task query returned absent; cleanup completed twice as an idempotent no-op. Positive exact-task removal and unrelated-task isolation remain pending. |
| Data choice | Data retained by default; existing two-confirmation factory reset is the explicit remove-data path | Implemented previously; combined removal observation pending. |
| SBOM/inventory | Generated CycloneDX 1.5 SBOM, resolved dependency inventory and SHA-256 payload manifest | Locally reproducible. |
| Vulnerability scan | `dotnet list PCSteward.slnx package --vulnerable --include-transitive --format json` via NuGet | 2026-07-30: 0 known vulnerable packages across 17 projects. |
| Signing | No approved production certificate was supplied | Missing for external distribution; no signing claim. |
| Icons | Checked-in 16/24/32/48/256 PNG marks and `.ico` | Package inspection and visual-mode observation pending. |

The package script downloads no tool, creates no certificate, installs nothing, requests no UAC and modifies no scheduled task. VM evidence is not required. Task 30A must not promote pending observations to Pass.
