# PC Steward private-beta checklist

**Candidate:** `0.1.0-private-beta`  
**Release channel:** private, manual distribution only (ADR-008/009)  
**Status:** preparation only — no signed installer or clean-VM lifecycle result exists.

## Build and package

- [ ] Use Windows 11 x64 clean VM with Inno Setup 6 and the supported .NET/Windows App SDK prerequisites.
- [ ] Run `pwsh -NoProfile -File .\scripts\Restore.ps1`, then `Build.ps1` and `Test.ps1`.
- [ ] Run `pwsh -NoProfile -File .\scripts\New-PrivateBetaPackage.ps1` to stage a `win-x64` payload and CycloneDX SBOM.
- [ ] Inspect `artifacts\private-beta\reports\payload-sha256.json`; sign the desktop, helper, scheduled host, and installer only with an approved certificate.
- [ ] Run `pwsh -NoProfile -File .\scripts\New-PrivateBetaPackage.ps1 -CompileInstaller` only after Inno Setup is installed. This task does not install it.
- [ ] Verify Authenticode status and a matching approved signer for desktop/helper before installing.

## Clean-VM lifecycle evidence (record command, VM image, operator, UTC time, version and sanitized result)

- [ ] Fresh install places binaries only under `%ProgramFiles%\PC Steward`, uses the PC Steward display name/icon, and launches in light, dark and high-contrast modes.
- [ ] Inspect the 16, 24, 32, 48 and 256 px marks from `design\assets\png`; verify the installer, Start menu and executable icon show the approved mark with no external font, icon-pack, WebView or browser-runtime dependency.
- [ ] First run exposes per-user storage and a local-only scan; no scheduled task is present before scheduling is enabled.
- [ ] Create a scheduled scan, verify its exact `PCSteward.ScheduledCheck` name/action, then confirm it is scan-only and exits.
- [ ] Upgrade from the prior signed private-beta build; validate version, signed fixed root, data retention and scheduled-task continuity.
- [ ] Uninstall and choose **No**: binaries and `PCSteward.ScheduledCheck` are removed while `%LocalAppData%\PCSteward` remains byte-for-byte.
- [ ] Reinstall, uninstall and choose **Yes**: binaries, task and `%LocalAppData%\PCSteward` are removed; the next install shows first-run onboarding.
- [ ] Exercise factory reset separately: it removes only per-user PC Steward state and the task, never installed binaries.
- [ ] Reboot/relaunch after each lifecycle case; record residual processes and files. Any failed mandatory case reopens ADR-002.

## Distribution controls

- [ ] Private recipient list and rollback contact approved by product owner.
- [ ] Checksums, SBOM, dependency/licence review, vulnerability/static/secret reports and known limitations included with the candidate.
- [ ] No public upload, tag, push, automatic updater, certificate purchase, or production-signing claim.
