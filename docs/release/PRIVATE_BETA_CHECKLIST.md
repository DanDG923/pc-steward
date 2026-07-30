# PC Steward private-beta checklist

**Candidate:** `0.1.0-private-beta`  
**Release channel:** private, manual distribution only (ADR-008/009)  
**Package:** standalone Windows x64 ZIP; no installation or elevated helper

## Build and package

- [ ] Use a supported Windows x64 test machine with the supported .NET/Windows App SDK runtime. VM execution is excluded by product-owner direction.
- [ ] Run `Restore.ps1`, `Build.ps1`, `Test.ps1`, then `New-PrivateBetaPackage.ps1`.
- [ ] Verify the ZIP contains `PCSteward.Desktop.exe`, `PCSteward.ScheduledHost.exe`, `Remove-PCSteward.ps1`, local visual assets and no `PCSteward.ElevatedHelper` file.
- [ ] Inspect `payload-sha256.json`, `dependency-inventory.json` and `sbom.cdx.json`.
- [ ] Sign distributed executable/script/archive artifacts only with an approved certificate; no production-signing claim is made by this repository task.

## Standalone lifecycle evidence

- [ ] Extract to a caller-writable test folder and launch without installation or administrator rights.
- [ ] Confirm machine-scope update evidence is labelled non-actionable and cannot create a review job or UAC prompt.
- [ ] Exercise one controlled user-scope update fixture through explicit review and exact post-update verification.
- [ ] Create a scheduled scan, verify exact task name/action and scan-only behavior, then run `Remove-PCSteward.ps1`; prove only `PCSteward.ScheduledCheck` is removed.
- [ ] Replace the extracted package while retaining `%LocalAppData%\PCSteward`; verify settings/history remain.
- [ ] Run two-confirmation factory reset, close the app, run task cleanup and delete the extracted folder; verify state, task and binaries are absent.
- [ ] Inspect 16, 24, 32, 48 and 256 px marks; verify light/dark/high-contrast launch and no external font, icon-pack, WebView or browser-runtime dependency.
- [ ] Record machine description, operator, UTC time, exact commands, hashes and sanitized results. An unexecuted case is Missing, not Pass.

## Distribution controls

- [ ] Private recipient list, approved download channel, checksums, rollback package and support contact are recorded.
- [ ] Dependency/licence, vulnerability, static-analysis, secret-scan and SBOM evidence are attached.
- [ ] No public upload, tag, push, automatic updater, certificate purchase, helper restoration or machine-scope execution.
