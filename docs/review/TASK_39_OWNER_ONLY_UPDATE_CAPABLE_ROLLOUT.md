# Task 39 — owner-only update-capable rollout

**Date:** 2026-08-05
**Branch:** `phase/4-beta`
**Candidate commit:** `1298d4ae5a747d7ee489c32746f5154049800263`
**Requirements:** `FR-UPD-01–09`, `FR-UI-01–07`, `FR-ID-04–06`, `FR-POL-01–05`, `AC-UPD-01–08`, `AC-MAT-02/05/07`, `TM-04`

## Outcome

**PASS for the exact owner-only protected installation.** The Task 35–38 implementation was frozen at commit `1298d4a`, built, fully tested, removed from its previous protected root under explicit owner approval, and reinstalled at `C:\Program Files\PC Steward` through the ADR-022 owner-only deployment route. This is an installed owner-only candidate with the existing reviewed WinGet/Chocolatey update execution paths; it is not a public, private-distribution, automatic-update-channel, or release-readiness claim.

## Authority and preserved state

The owner requested rollout of the resolved redesign as the version that can update applications and explicitly approved full removal of the old protected version if required. The supported installer refuses replacement, so the guarded removal/reinstall path was used. Before removal, the old protected deployment passed its read-only validator, PC Steward was closed, and the per-user database was copied to the ignored local rollback folder `artifacts/task-39-rollout-backup/pre-replacement`.

The database copy passed SQLite `quick_check`, contained 17 application tables, and had SHA-256 `A494B6471A5E6B3B75D54898C9D9CCA80C97ADE443FFB818CE67AD89BD77B4AA`. The same hash was observed after removal and after installation. No clear-data, reset, migration, schedule change, source refresh, application update, or application launch was part of the deployment workflow.

## Candidate and deployment evidence

| Check | Result |
|---|---|
| Canonical Release build | PASS — 0 warnings, 0 errors |
| Canonical full suite on the normal Windows host | PASS — 271/271: Domain 55, Application 170, Architecture 25, Security 21 |
| Scope/secret/whitespace review | PASS — no actionable secret match or diff-check error |
| Local source checkpoint | PASS — commit `1298d4ae5a747d7ee489c32746f5154049800263` |
| Old protected removal | PASS — install/state roots removed; installer-owned certificate absent from CurrentUser My, Root, and TrustedPublisher; user data retained |
| New protected install | PASS — 102 manifest files, required WinUI XBF/PRI resources, protected ACLs, same-signer desktop/helper, complete hashes |
| Separate read-only installed validator | PASS |
| Installed source identity | PASS — installed `PCSteward.Desktop.dll` SHA-256 equals the local commit build: `30D86BDC2590818C403249FBDAB587B8A6E4D17151CB15868F2247CB3BE49DAC` |
| Installed product version | `0.1.0-private-beta+1298d4ae5a747d7ee489c32746f5154049800263` |
| Temporary owner-only signer | `08C366C4412265629D8B28370C8ED5A9C5BA06BC` |
| Exact scheduled task | ABSENT — `PCSteward.ScheduledCheck` was not created or changed |
| Residual PC Steward process | NONE after validation |

The elevated install exceeded the outer five-minute orchestration wait, but its child process completed successfully. The protected ledger was complete, the local installer log ended with `validation: Pass`, and the separate read-only validator passed afterward. Two earlier elevation-launch attempts made no change: one rejected a non-elevated token and one malformed the script path; a third unsupported redirection parameter set did not launch. The old installation was revalidated before the successful guarded removal.

ADR-022 intentionally limits the generated owner-only certificate to 14 days. The installed build is trusted and update-capable within that local test window, but this certificate is not a permanent signing or distribution channel; continued use after expiry requires another explicitly approved protected reinstall or an approved production-signing route.

## Update boundary

This installed build exposes the real update workflow and retains its existing safety boundary: a fresh trusted package/version must be selected, reviewed, and explicitly confirmed in PC Steward before a provider runs; machine-scope work then requests UAC. The earlier Task 33 owner-observed WinGet update remains the live execution evidence for that path. Task 39 did not update an application, and live Chocolatey update execution remains uncollected.

The deployment tool intentionally creates no Start menu or desktop shortcut. The installed executable is `C:\Program Files\PC Steward\PCSteward.Desktop.exe`.
