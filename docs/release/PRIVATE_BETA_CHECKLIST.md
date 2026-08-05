# PC Steward private-beta checklist

**Candidate:** `0.1.0-private-beta`  
**Release channel:** private, manual distribution only (ADR-008/009)  
**Package:** standalone Windows x64 ZIP; no installation or elevated helper

## Build and package

- [x] Use a supported Windows x64 test machine with the supported .NET/Windows App SDK runtime. VM execution is excluded by product-owner direction.
- [x] Run `Restore.ps1`, `Build.ps1`, `Test.ps1`, then `New-PrivateBetaPackage.ps1`.
- [x] Verify the ZIP contains `PCSteward.Desktop.exe`, `PCSteward.ScheduledHost.exe`, `Remove-PCSteward.ps1`, local visual assets and no `PCSteward.ElevatedHelper` file.
- [x] Run `pwsh -NoProfile -File .\scripts\Test-PrivateBetaPackageEvidence.ps1`; require a pass before launch smoke, then inspect `payload-sha256.json`, `dependency-inventory.json`, `licence-inventory.json`, `sbom.cdx.json` and `THIRD-PARTY-NOTICES.txt`.
- [ ] Sign distributed executable/script/archive artifacts only with an approved certificate; no production-signing claim is made by this repository task.

## Standalone lifecycle evidence

- [x] [Historical — SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`] Extracted to a caller-writable test folder and launched without installation or administrator rights.
- [ ] The extracted package launch/update/lifecycle for `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` was not run; launch/update/lifecycle observations remain historical only.
- [ ] Confirm machine-scope update evidence is labelled non-actionable and cannot create a review job or UAC prompt.
- [x] [Historical — SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`] Exercised one controlled user-scope update fixture through explicit review and exact post-update verification.
- [ ] Create a scheduled scan, verify exact task name/action and scan-only behavior, then run `Remove-PCSteward.ps1`; prove only `PCSteward.ScheduledCheck` is removed.
- [ ] Replace the extracted package while retaining `%LocalAppData%\PCSteward`; verify settings/history remain.
- [ ] Run two-confirmation factory reset, close the app, run task cleanup and delete the extracted folder; verify state, task and binaries are absent.
- [ ] Inspect 16, 24, 32, 48 and 256 px marks; verify light/dark/high-contrast launch and no external font, icon-pack, WebView or browser-runtime dependency.
- [x] Record machine description, operator, UTC time, exact commands, hashes and sanitized results. An unexecuted case is Missing, not Pass.

## Distribution controls

- [ ] Private recipient list, approved download channel, checksums, rollback package and support contact are recorded.
- [x] Dependency/licence, vulnerability, static-analysis, secret-scan and SBOM evidence are attached.
- [x] No public upload, tag, push, automatic updater, certificate purchase, helper restoration or machine-scope execution.

The prior Task 90 exceptions are bound to SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF` and do not apply to the remediated archive `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523`. Unchecked signing/channel, performance, and destructive/supported-host lifecycle items remain mandatory; the unsigned local archive is not a release candidate.
