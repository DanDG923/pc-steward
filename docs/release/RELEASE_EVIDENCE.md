# Task 30 release evidence

**Task:** 30 — Packaging, release evidence, SBOM, and private beta preparation  
**Requirements:** `AC-DAT-18`, ADR-002, ADR-008–010, `TM-18`  
**Status:** evidence preparation; no installer lifecycle is claimed as passed.

| Area | Evidence / command | Result |
|---|---|---|
| Package model | `packaging/PCSteward.iss`; guarded `scripts/New-PrivateBetaPackage.ps1` | Reproducible Inno Setup configuration stages a fixed Program Files payload and offers explicit retain/remove-data choice; compilation requires locally installed ISCC. |
| Scheduled task cleanup | Installer deletes only `PCSteward.ScheduledCheck`, matching the product constant | Configured; clean-VM execution required. |
| Per-user data choice | Uninstall confirmation defaults to retaining `%LocalAppData%\PCSteward`; deletion is an explicit Yes choice | Configured; clean-VM execution required. |
| SBOM/inventory | `New-PrivateBetaPackage.ps1` emits CycloneDX 1.5 `sbom.cdx.json`, dependency inventory and SHA-256 payload manifest | Locally reproducible after publish. |
| Signing | Environment report: `signtool.exe` absent; no certificate creation/use was authorized | Unproven; no production-signing claim. |
| Installer toolchain | Environment report and Task 30 check: no `ISCC.exe`, WiX or equivalent | Unproven; no download/install attempted. |
| Privacy/secret review | ADR-010 and repository history reviewed; release artifacts contain no machine-specific paths or credentials | Source review only; final scan recorded by Task 30A. |
| Licence/dependency review | ADR-008; central package versions and resolved dependency inventory are included in generated reports | Private distribution only; final legal review remains product-owner work. |
| Vulnerability advisory scan | `dotnet list ... --vulnerable` was not run: it would send dependency inventory to an external advisory service and no approval was supplied | Missing; Task 30A must rerun only with explicit egress approval or record an approved exception. |
| Icons/runtime dependency | Checked-in 16/24/32/48/256 PNG marks and `.ico`; Desktop uses local icon asset | Compile/clean-VM visual verification pending. |

The package script intentionally does not download tooling, create a certificate, sign, install, publish, push, tag, upload, or modify a real scheduled task. Task 30A must run the final safe checks and classify this unexecuted lifecycle evidence as Missing unless a clean-VM record is attached.
