# Task 30 release evidence

**Requirements:** `AC-DAT-18`, ADR-002, ADR-008–010/018, `TM-04/18`
**Status:** Task 90 local-only candidate reconciled; external distribution remains prohibited pending a fresh Task 31 verdict and the documented exception boundaries.

| Area | Evidence | Result |
|---|---|---|
| Package model | `New-PrivateBetaPackage.ps1` publishes `win-x64`, adds required WinUI XBF/PRI resources, emits reports and creates a ZIP | Exact final archive SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF` mechanically validated and passed packaged launch, About/support rendering and clean-close smoke. |
| Privilege boundary | Desktop composes `UserScopeOnly`, executor has no elevated transport, project/payload omit elevated helper | Focused application/security/architecture regressions pass. |
| Machine-scope denial | Preparation stops before structured authority/job creation; executor independently rejects machine scope | Controlled regressions pass. The live Task 90 update was current-user scope and produced no observed UAC or helper launch. |
| Current-user update | Immutable reviewed job, official WinGet community source, exact source/ID/scope/architecture/version binding and post-update inventory | VS Code `Microsoft.VisualStudioCode` updated 1.129.0 to 1.130.0 successfully; job/events and independent HKCU installed-state evidence are retained under `artifacts/host-evidence/20260731T225802Z-task90-vscode-update/`. |
| Removal | `Remove-PCSteward.ps1` refuses while product processes run and removes only `PCSteward.ScheduledCheck`; folder deletion removes binaries | Implementation/regressions pass. Omitted destructive and supported-host lifecycle observations are accepted only for Task 90 under `PO-EXC-90-RESET-LIFECYCLE-OMISSIONS`; they remain unproven for release. |
| Data choice | Data retained by default; two-confirmation factory reset is the explicit remove-data path | Reset coordination, marker validation, exact scheduler verification and fresh-store onboarding are implemented and tested; the omitted final destructive observation is covered only by the same local-only exception. |
| SBOM/inventory | Generated payload-scoped CycloneDX 1.5 SBOM with 15 package components, 16 dependency-graph nodes and 32 exact edges; exact licence metadata; portable SHA-256 manifest; `licence-inventory.json`; and distributable `THIRD-PARTY-NOTICES.txt` | Product-owner engineering acceptance covers the mapped MIT, Apache-2.0, WebView2 BSD-style, and Microsoft redistribution terms subject to shipping the notices. This is not legal advice. Generation fails closed for an unmapped package/version, changed declaration or missing/empty source text; the integrated validator reconciles every package/licence/SBOM identity and edge, then recomputes every payload and ZIP-entry length/hash. |
| Vulnerability scan | `dotnet list PCSteward.slnx package --vulnerable --include-transitive --format json` via NuGet | 2026-07-31: 0 known vulnerable package rows across 17 projects. |
| Signing | No approved production certificate was supplied | Missing for external distribution; no signing claim. |
| Icons/WebView boundary | Checked-in 16/24/32/48/256 PNG marks and `.ico`; no product WebView composition | Package inspection and native launch smoke passed. Approved design revision permits unused transitive WinUI/WebView2 support files but no embedded browser UI. |

The package script downloads no tool, creates no certificate, installs nothing, requests no UAC and modifies no scheduled task. VM evidence is not required. The unsigned archive is local-only under `PO-EXC-90-SIGN-LOCAL-ONLY`; omitted observations are not promoted to Pass.
