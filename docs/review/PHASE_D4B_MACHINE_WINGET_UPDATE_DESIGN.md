# Phase D stage D4b: reviewed machine-scope WinGet updates (design note)

Date: 2026-11-12. Status: **design. Owner accepted Q1–Q3 as recommended on 2026-11-12 (§9). No code change.** Stage D4b of the Phase D plan, after the owner's 2026-11-12 choice "Updates only, Search waits". It closes Task 53 gap **U1** (machine-scope WinGet updates have no reviewed-mode route) and, when the route ships, lets the helper drop protocol 1 (Task 54 finding **R2**, under the ADR-025 transition amendment drafted in stage C4).

Requirements: `FR-UPD-01–15`, `AC-UPD-01–11`, `TM-03–04/07/13`. Builds on [ADR-025](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md) (Update row of the responsibility table; "a new helper never also accepts protocol 1"), [ADR-033](../adr/ADR-033-NATIVE-AOT-ELEVATED-HELPER.md), the [install design note](PHASE_D_INSTALL_DESIGN.md) §3 and §7.2, the [Task 54 record](TASK_54_PHASE_C_REVIEW_GATE.md) and stage C4's reviewed user route (`WinGetUserSilentV1`). Base: `phase/4-beta` at `6c96081`.

**Out of scope:** installs (paused), the Microsoft Store source, per-user vendor installers (the next stage), and any action on the owner's PC. Nothing here turns a route on or approves a host step.

## 1. Summary of the recommendations

1. **A new closed route, `WinGetMachineSilentV1`, run by the protocol-2 helper.** One Windows prompt per app, like a Chocolatey update today. Silent mode is bound at review. The helper builds one fixed `winget upgrade` command line from the reviewed data and accepts no path or argument from the desktop (§4).
2. **The helper checks the job itself before it runs anything** (§5): the trusted `winget.exe`, the `winget` source, WinGet's user settings, the exact machine registration and version that was reviewed, and the 30-minute review limit. WinGet itself then pins the exact ID, version and source and refuses anything that is not newer than what is installed (§3).
3. **Close the main "user foothold to administrator" gap PC Steward can close.** Elevated WinGet downloads installers into the user's own `%TEMP%` and runs them after a hash check, with nothing stopping a program running as the owner from swapping the file in between. The helper gives WinGet a fresh, administrator-only temporary folder with an unguessable name for each run (§6, Q2).
4. **Version 1 refuses packages that declare dependencies or licence agreements,** as the owner already accepted for installs (§7, Q3).
5. **Protocol 1 goes in the same change.** The helper then accepts protocol 2 only, with a regression test that it refuses protocol 1, and the legacy elevated runner is removed (§8).

## 2. Where things stand (code facts at `6c96081`)

| Fact | Evidence |
|---|---|
| Machine-scope WinGet updates run today on protocol 1: the desktop's `ElevatedUpdateRunner` sends a mode-less `ImmutableUpdateJob`, and the helper's `RunProtocol1Async` runs `upgrade --id --version --source --exact --scope machine --architecture --disable-interactivity`, with no `--silent` and no helper-side check of the installed app, source or settings | `ElevatedUpdateRunner.cs`; `WindowsElevatedHelperTransport.cs` (`RunProtocol1Async`); `WingetMachineUpdateExecutor.BuildUpdateArguments` |
| The protocol-1 machine executor finds `winget.exe` through the WinRT `PackageManager`, although ADR-033 records that no helper code uses WinRT. Whether that path works in the Native AOT image was never tested on a real machine update | `MachineTrustedWinget.ResolveExecutable`; ADR-033 "Feasibility" |
| Protocol 2 has one route family today, Chocolatey (`ChocolateyMachineRuntimeLimitsV3`, `ChocolateyClientSelfUpdateV1`), with a helper that qualifies the exact reviewed versions itself and a desktop interpreter for its typed answer | `ReviewedHelperEndpoint.cs`, `ReviewedUpdateProtocol2Interpreter.cs`, `ReviewedUpdateWireCodec.cs` |
| Stage C4 adds `WinGetUserSilentV1`: user scope, Silent bound at review, `--scope user`, no installer-type binding, wired through `ReviewedUpdateExecutionWorkflow` → `UpdateBatchItem.Reviewed` → `ApprovedUpdateExecutor` | C4 thread (branch `claude/project-thread-t498yi`) |
| The CLI source check refuses unless `winget` is exactly the Microsoft community source | `WingetCliSourceGuard` |
| The canonical reviewed payload (schema 3) has no installer-type field | `ReviewedUpdateOperation.GetCanonicalBytes` |

## 3. What WinGet itself enforces on `upgrade` (pinned)

All references are to `microsoft/winget-cli` at tag `v1.29.380` (the COM projection the app ships since 0.1.3; the CLI must be the same build, F7). Blob SHAs identify the exact files read.

| Question | Behaviour | Source |
|---|---|---|
| Options `upgrade` accepts | `--scope`, `--architecture`, `--installer-type`, `--silent` and `--disable-interactivity` are all accepted on `upgrade` | `Commands/UpgradeCommand.cpp` `3e99f531`, lines 43–76 |
| Exact version, not newer | With `--version`, upgrade stops with `APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE` unless the installed version is strictly lower | `Workflows/UpdateFlow.cpp` `fbdeddfb`, lines 198–210 |
| Unknown installed version | Refused unless `--include-unknown` (never passed) | same file, lines 59–67 |
| Hash mismatch | Always blocked when running as administrator, whatever the admin setting says | `Workflows/DownloadFlow.cpp` `3339ace4`, lines 561–565 |
| Source data | `user_sources` and `admin_settings` are "secure" streams, hash-checked against a copy under `%ProgramData%` that only administrators can write. The packaged `winget` index is read from the installed source package after an integrity check | `Public/winget/Settings.h` `5abe0c7f`, lines 74 and 82; `Runtime.cpp` `16187768`, lines 306–325; `PreIndexedPackageSourceFactory.cpp` `4538d902`, lines 518–541 |
| Scope | `--scope machine` keeps only installers that declare Machine scope. Without `--scope`, WinGet keeps installers whose declared scope matches the installed copy's scope **or is undeclared** | `Manifest/ManifestComparator.cpp` `819b72cf`, lines 400–429 and 444–505 |
| Architecture | `--architecture` restricts applicable installers (it does not skip the applicability check, unlike `--installer-architecture`) | `Workflows/WorkflowBase.cpp` `dc60a6da`, lines 664–683 |
| Where the installer is downloaded | `GetTempPathW()` + `WinGet\<id>.<version>\`. The folder is owned by, and fully writable for, the current user, and the file is not locked between the hash check and the run | `Runtime.cpp` `16187768`, lines 127–140 and 281–286; `Filesystem.cpp` `dca435d7`, lines 598–700; `DownloadFlow.cpp` lines 274–347 |
| User settings | `settings.json` is an ordinary user file. It can change the installer preference and requirement order, skip dependencies, set the portable and default install roots, the downloader and experimental features | `Public/winget/UserSettings.h` `9909d9ac`, lines 186–244 |

So WinGet's own guards already pin the identity, version, source and "strictly newer" rule against its own correlation. What it does not protect is anything that reads from the owner's user profile while elevated: the download folder and the settings file.

## 4. The route

- **Contract:** `UpdateRouteContract.WinGetMachineSilentV1`, appended after C4's value (never reinterpret an earlier one). Schema 3, operation Update, provider WinGet, source `winget` only (not `msstore`), scope Machine, channel Stable, mode `Selected(Silent)`, architecture Observed and one of x86, x64 or arm64.
- **Installer type bound at review:** MSI, WiX, EXE, Inno, Nullsoft or Burn, from COM `GetApplicableInstaller`. Portable, zip, MSIX and Store installers are refused for this route, because their machine-scope placement depends on WinGet user settings (portable roots) or is not an elevated vendor installer at all. Schema 3 has no installer-type field, so the type is carried in the evidence identity with a strict format check, as the Chocolatey route carries its profile digest: `wgm1:<type>:<registration digest>` (§5 H6).
- **Fixed command line, built only inside the helper:**
  `upgrade --id <ID> --version <V> --source winget --exact --scope machine --architecture <a> --installer-type <t> --disable-interactivity --silent`
  Never `--force`, `--ignore-security-hash`, `--accept-package-agreements`, `--include-unknown`, `--location`, `--override`, `--custom`, `--allow-reboot` or `--skip-dependencies`.
- **Review shows:** "Updates for every user on this PC. Windows will ask for administrator permission once for this app. The update runs silently. If the app's installer shows its own window anyway, it came from the installer."
- **Result:** the desktop's own check, as today: the exact HKLM registration at the reviewed version and architecture, by structured inventory (never the exit code alone). WinGet's documented restart exit codes give Restart required (Task 51). Anything else is Failed or Unknown.

## 5. What the helper checks before it runs anything

ADR-025 requires the helper to "independently resolve the same installed instance, source and candidate eligibility and the enforceable mode, scope and architecture", or the route is unavailable. In order, each a refusal without updating:

| # | Check | How |
|---|---|---|
| H1 | Bootstrap, peer identity, nonce, two-minute request freshness, one item only | Unchanged protocol-2 bootstrap and endpoint |
| H2 | Shape: exactly the §4 route with all fixed values; installer type in the closed set | Strict wire codec, new closed request variant |
| H3 | Review age within 30 minutes | Same rule the Chocolatey route applies |
| H4 | Trusted client: the App Installer `winget.exe`, Microsoft-signed, no reparse points, found **without WinRT** (Win32 `GetPackagesByPackageFamily` and `GetPackagePathByFullName`, plain P/Invoke) | Replaces `MachineTrustedWinget`'s WinRT lookup inside the helper; keeps ADR-033's "no WinRT in the helper" true |
| H5 | Source: `winget source export --name winget` run elevated matches the fixed Microsoft source exactly | `WingetCliSourceGuard`, reused |
| H6 | Installed registration: the reviewed uninstall key exists in HKLM (64- or 32-bit view as reviewed), is not a system component, and its `DisplayVersion` and `WinGetPackageIdentifier`/product code match the digest bound at review | Plain registry reads; no WinRT or COM |
| H7 | Settings: WinGet's `settings.json` for this user, read bounded and strict. Refuse if any setting outside a small allow-list is present (visual, logging, telemetry, output, interactivity) | §6 |
| H7b | Dependencies and agreements, again inside the helper: run `winget download --id <ID> --version <V> --source winget --exact --scope machine --architecture <a> --installer-type <t> --disable-interactivity --download-directory <fresh admin-only folder>` (with the §6 protected `TEMP`), read the exported manifest with the same bounded strict parser, and refuse unless its single installer entry matches the reviewed type, architecture and scope, its `Dependencies` block and agreements are empty, and its manifest digest and installer SHA-256 equal the values bound at review. Any drift is a refusal, never a re-review | Install design §6 pre-launch comparison, run by the helper instead of the desktop |
| H8 | Run: the §4 command line with `TEMP` and `TMP` pointing at a fresh administrator-only folder (§6). Owned process tree, 15-minute deadline, bounded output | Existing owned-process handling |
| H9 | Answer: a typed, bounded result (outcome, exit code, sanitized output, which check refused). No success Boolean | New closed response variant |

The desktop still performs its own checks first (live review, fresh source consent, the 30-minute review window, claim before the prompt) and its own post-update check. Neither side trusts the other's word for success.

## 6. Elevated WinGet and the owner's profile (security)

Owner's stance (2026-11-12): findings that need an already compromised machine are low priority, but anything that turns a user-level foothold into administrator matters. These two do, and **both exist today on protocol 1 with no mitigation**, and in any `winget upgrade` run from an administrator prompt.

**S1. Installer swap in `%TEMP%` (high).** A program running as the owner can replace the downloaded installer after WinGet's hash check and before the elevated run, and so run its own code as administrator. Mitigation: the helper creates `%ProgramData%\PCSteward\HelperTemp\<random 128-bit name>` owned by Administrators, with an ACL for SYSTEM and Administrators only and a parent folder that standard users cannot list, sets `TEMP` and `TMP` for the WinGet process to it, and deletes it afterwards. WinGet will still add the owner to the ACL of its own `WinGet` subfolder (§3), so this does not make the folder unwritable; it makes it **unfindable** for a non-elevated program, which cannot list the parent, read the elevated process's environment, or guess the name. That closes the practical attack. The residual (an attacker that can already read elevated process memory) needs administrator rights already.

**S2. User settings read while elevated (medium).** A program running as the owner can edit `settings.json` to skip dependencies, change installer preferences, or turn on experimental features for the elevated run. Pinning `--scope`, `--architecture` and `--installer-type` on the command line removes most of the effect, and refusing portable and zip installers removes the install-root settings. Mitigation: H7 refuses the run unless the file holds only allow-listed settings. Residual: a program running as the owner could change the file in the moment between H7 and WinGet reading it. This is the same kind of narrow race the owner accepted for Chocolatey (ADR-025 M1), and it needs the attacker to win a timing race while the owner is approving the prompt.

**S4. Source refresh between H7b and the run (low).** H7b and the upgrade read the `winget` source seconds apart in one helper request. If WinGet refreshed its source in between and the same ID and version now had a different manifest, the upgrade would use it. A changed manifest for an already published version is rare and still comes only from the Microsoft source with WinGet's hash check, and the desktop's post-update check refuses any result that is not the exact reviewed version. Not a user-foothold path: the source package is protected (§3).

**S3. Installer behaviour (inherent).** A vendor installer running as administrator can do anything its vendor wrote. PC Steward only runs installers from the Microsoft `winget` source whose hash WinGet checked. Disclosed at review, as for every update.

## 7. Dependencies and licence agreements

- **Agreements:** never pass `--accept-package-agreements`; WinGet then stops whenever the target declares one. The review also reads COM `GetCatalogPackageMetadata().Agreements` and shows the update as not actionable, with the reason, before any prompt. Same rule the owner accepted for installs (install design Q3).
- **Dependencies:** WinGet installs declared dependencies elevated, and no COM version exposes them (install design §6, D0). Recommended for version 1, as the owner accepted for installs (Q4): at review, run `winget download` for the exact reviewed installer into an app-owned folder, read the exported manifest with a bounded strict parser, refuse if its `Dependencies` block is not empty, and bind the manifest digest and installer SHA-256 into the evidence. The helper repeats the same check immediately before the run and refuses any drift (§5 H7b), so the review-time result is never the only evidence. Cost: the installer is downloaded once more at review and once more by the helper. Most updates have no dependencies; those that do (often a Visual C++ runtime) show "Not offered in this version: this update would also install other packages".

## 8. Retiring protocol 1

In the same change, after the new route is in: `ElevatedHelperEntryPoint.RunAsync` sends any line that is not self-update, probe or `--protocol 2` to a refusal (exit 20), never to an executor; `RunProtocol1Async`, `OneShotHelperRequestValidator`, `ElevatedHelperProtocol` request handling, the protocol-1 pipe locator, `WingetMachineUpdateExecutor`'s protocol-1 entry and `ElevatedUpdateRunner`'s mode-less path are removed. Regression tests: a well-formed protocol-1 line exits 20 without connecting; an architecture test that no helper code path reaches a provider executor except through the protocol-2 interpreter. ADR-025's transition amendment (C4) is then closed by this change, and the ADR-022 capability statement keeps protocol `2` only.

## 9. Owner questions

**Q1. Installers that do not say whether they install per user or per machine.** With `--scope machine` (recommended), WinGet only offers installers that declare Machine scope, so some updates for apps that are already installed machine-wide are "not offered". The alternative drops `--scope` and relies on WinGet's own rule that the installer must match the installed copy's scope or not declare one. That covers more apps, but the scope is then decided by the installer, not enforced, and user settings can influence which installer is chosen. *Recommendation: strict for version 1; revisit with real coverage numbers from your PC.*

**Q2. Accept the remaining settings race (S2) with the S1 and H7 mitigations,** as you did for Chocolatey's M1? The alternative is to keep the machine route off until WinGet stops reading user settings while elevated, which is not planned upstream. *Recommendation: accept as written; it is recorded in ADR-025 next to M1.*

**Q3. Refuse updates that declare dependencies (and agreements), as for installs?** The alternative lets WinGet install the declared dependencies, which are also from the Microsoft `winget` source and hash-checked, but were not reviewed. *Recommendation: refuse in version 1, as you accepted for installs.*

**Owner answer (2026-11-12, 20:09 UTC).** In response to the prepared choice listing Q1–Q3 with the recommendations above, the owner chose **“Accept all three as recommended”**: Q1 strict `--scope machine`; Q2 the S2 settings race is accepted with the S1 and H7 mitigations, to be recorded in ADR-025 next to M1 when the route is built; Q3 version 1 refuses updates whose target declares dependencies or licence agreements. This accepts the design only. It does not admit the route or approve a release, a host step or a real update.

## 10. Build stages (one PR each, Codex review before merge)

The route stays **unavailable** (not admitted at the review gate, and absent from the capability statement) until stages 1–3 are all merged. Stage 3 is the only change that admits it, and it does so in the same change that removes protocol 1, so no build ever offers the new route without the Q3 refusal and the helper-side checks, or keeps the legacy entry point beside it.

1. **Route and helper, not admitted:** domain route value and evidence format; wire request and response variants; helper H1–H9 (no WinRT); desktop review text and interpreter; machine branch in C4's `ReviewedUpdateExecutionWorkflow`. Waits for C4's PR to merge.
2. **Dependency and agreement evidence** (§7) at review and in the helper (H7b), still not admitted.
3. **Admit the route and retire protocol 1** (§8) in one change, with the ADR-025 amendment closed, the S2 acceptance recorded, and STATUS/TRACEABILITY updated.
4. **Host check on the owner's PC** after the next release: one real machine-scope update of a small app through the in-app updater build, one combined paste.

## 11. Tests the stages need

Negative fixtures for every H check (changed ID, version, source, scope, architecture, installer type, mode, route, stale review, protocol-1 line, wrong peer); a registration that moved between review and launch; settings files with each refused key; protected temp folder ACL, name length, reparse-point refusal and clean-up after timeout and cancellation; the helper never launching WinGet after any refusal; exit codes 0, restart codes and failure mapping; post-update check refusing a user-scope result for a machine review.
