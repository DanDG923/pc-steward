# PC Steward private-beta checklist

**Candidate:** `0.1.0-private-beta`
**Evaluation boundary:** product-owner-only local use; no distribution channel
**Package:** standalone Windows x64 ZIP; no installation or elevated helper

## Build and package

- [x] Use a supported Windows x64 test machine with the supported .NET/Windows App SDK runtime. VM execution is excluded by product-owner direction.
- [x] Run `Restore.ps1`, `Build.ps1`, `Test.ps1`, then `New-PrivateBetaPackage.ps1`.
- [x] Verify the ZIP contains `PCSteward.Desktop.exe`, `PCSteward.ScheduledHost.exe`, `Remove-PCSteward.ps1`, local visual assets and no `PCSteward.ElevatedHelper` file.
- [x] Run `pwsh -NoProfile -File .\scripts\Test-PrivateBetaPackageEvidence.ps1`; require a pass before launch smoke, then inspect `payload-sha256.json`, `dependency-inventory.json`, `licence-inventory.json`, `sbom.cdx.json` and `THIRD-PARTY-NOTICES.txt`.
- [ ] Sign distributed executable/script/archive artifacts only with an approved certificate. This is waived only for the exact owner-only local candidate; no signing claim is made.

## Standalone lifecycle evidence

- [x] [Historical — SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`] Extracted to a caller-writable test folder and launched without installation or administrator rights.
- [ ] The extracted package launch/update/lifecycle for `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` was not run; launch/update/lifecycle observations remain historical only.
- [ ] Confirm machine-scope update evidence is labelled non-actionable and cannot create a review job or UAC prompt.
- [x] [Historical — SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`] Exercised one controlled user-scope update fixture through explicit review and exact post-update verification.
- [ ] Create a scheduled scan, verify exact task name/action and scan-only behavior, then run `Remove-PCSteward.ps1`; prove only `PCSteward.ScheduledCheck` is removed.
- [ ] Replace the extracted package while retaining `%LocalAppData%\PCSteward`; verify settings/history remain.
- [ ] Run two-confirmation factory reset, close the app, run task cleanup and delete the extracted folder; verify state, task and binaries are absent.
- [ ] Inspect 16, 24, 32, 48 and 256 px marks; verify light/dark/high-contrast launch and no external font, icon-pack, WebView or browser-runtime dependency.
- [x] Record machine description, operator, UTC time, exact commands, hashes and sanitized results. An unexecuted case is `Unverified`, not Pass.

## Distribution controls

- [ ] Private recipient list, approved download channel, checksums, rollback package and support contact are recorded.
- [x] Dependency/licence, vulnerability, static-analysis, secret-scan and SBOM evidence are attached.
- [x] No public upload, tag, push, automatic updater, certificate purchase, helper restoration or machine-scope execution.
- [x] SHA-256 `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` is bound to the owner-only non-public exceptions; no other recipient or distribution is authorized.

The exceptions make only these named gaps non-blocking for the exact product-owner-only local candidate: `FRA-14` final-app performance evidence; `FRA-15` exact-current launch/update/replacement/task/reset/removal/final-timeout lifecycle evidence; and `FRA-16` signer/channel/recipient/support/distributable-rollback inputs. Those gaps remain `Unverified`. Every other unchecked checklist item retains its independent mandatory or optional status and is not waived. All three exceptions expire if the hash changes, another recipient is proposed or any distribution/release claim is considered. The unsigned archive is not an external or public release candidate.
