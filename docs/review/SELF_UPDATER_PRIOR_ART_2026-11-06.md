# How other apps self-update, measured against ADR-032

**Date:** 2026-11-06
**Status:** Research note (input to ADR-032; changes nothing by itself)
**Scope:** Read-only web and GitHub research. No code. ADR-032 (draft PR #20) is owned by the updater design thread and is not edited here; this note only recommends changes to it.

## Summary

ADR-032 is already stronger than most real-world updaters on the two things that matter most: its trust anchor is a key the owner holds offline, not the hosting service, and the signed object covers the version, sequence and asset hash together. Almost every compromise in the case studies below happened because one of those two things was missing.

The research suggests nine changes or additions (section 5). The most important are:

1. **Freeze protection is the one real gap.** ADR-032 accepts that a GitHub compromise could silently hide newer releases. TUF solves this with a short-lived, low-privilege "timestamp" signature. A cheap version of that fits PC Steward without putting the release key online.
2. **Compile in two release keys from day one** (a primary and an offline backup kept somewhere else), so losing one key does not strand every install on manual updates.
3. **Turn on GitHub immutable releases** for the repository. It is free and stops assets or tags being swapped after publication.
4. **Write down "fail closed, no override" as a rule**, and use native Windows APIs for any Authenticode check. electron-updater's past bypass came from shelling out to PowerShell and failing open on an error.
5. **Do not send the version in the User-Agent.** The comparison is local, so GitHub does not need it.

Nothing found argues for adopting an off-the-shelf updater framework as the trust layer. The ones that fit Windows best (Velopack, electron-updater, WinSparkle, Tauri) either do not sign their release metadata or only recently started to, and all would add a dependency. Their mechanics are worth borrowing; their trust models are not.

## 1. What was surveyed

| Project | Kind |
|---|---|
| Squirrel.Windows, Velopack | .NET / cross-platform installer and update frameworks, commonly fed from GitHub Releases |
| Sparkle (macOS), WinSparkle (Windows) | Appcast (RSS feed) updaters |
| electron-updater (electron-builder) | Electron's de facto updater, often fed from GitHub Releases |
| Tauri updater plugin | Rust/WebView app updater, often fed from GitHub Releases |
| MSIX + App Installer (`.appinstaller`) | Windows' built-in package update mechanism |
| winget | Microsoft's community package manager (also PC Steward's own provider) |
| The Update Framework (TUF), go-tuf | Academic and CNCF update-security framework |
| Chrome / Omaha (Google Update) | Large-scale browser updater |
| Firefox (Balrog + MAR + Maintenance Service) | Large-scale browser updater |
| PowerToys | Microsoft desktop app that updates from GitHub Releases |
| VS Code | Microsoft desktop app with a per-user installer |
| Notepad++ (WinGUp) | Windows desktop app; update infrastructure hijacked 2025 |

## 2. Comparison

Legend: "metadata" means the "what is the latest version, where is it, what is its hash" document; "payload" means the downloaded installer or archive.

| | Release discovery | What is signed, and by whom | Rollback / freeze protection | Elevation and install location | Staged rollout | Health check / rollback | Phones home |
|---|---|---|---|---|---|---|---|
| **Squirrel.Windows** | `RELEASES` file next to packages (often on GitHub Releases) | Payload hashes (SHA-1) listed in an unsigned `RELEASES` file; relies on Authenticode of binaries if the developer signs | None in the client beyond "newer version" | Per-user, `%LocalAppData%\<app>\app-<ver>`, no UAC | No | Old version folder kept; no health check | Only the feed fetch |
| **Velopack** | `releases.<channel>.json` feed; built-in GitHub Releases source | SHA-256 per asset in the feed; feed itself is not signed; binaries Authenticode-signed if the developer passes signing parameters | Version comparison only | Per-user, `%LocalAppData%\<app>\current` (stable path; shortcuts repaired on first update), no UAC | Not built in | Previous version kept for delta base; no health check | Only the feed fetch |
| **WinSparkle / Sparkle** | Appcast RSS feed over HTTPS | Payload signed with EdDSA (Ed25519) by a developer key; DSA deprecated. Sparkle had a 2.6.4 fix for a signed-payload substitution bypass | Version comparison; appcast freshness not signed | WinSparkle runs the developer's installer (which may prompt UAC); Sparkle 2 uses a separate privileged installer helper (had an LPE fix in 2.9.6) | Phased rollout via appcast attributes (Sparkle) | No | Optional "system profile" sent with the feed request (off by default) |
| **electron-updater** | `latest.yml` on GitHub Releases / S3 / generic server | Historically: only a SHA-512 in an unsigned `latest.yml`, plus an Authenticode subject-name check on Windows. Current docs state `latest*.yml` is now Ed25519-signed and verified against an embedded key, failing closed | `allowDowngrade` defaults to off | NSIS per-user or per-machine (per-machine prompts UAC); default is install on quit | `stagingPercentage` in `latest.yml`, using a random per-install ID | No | Feed fetch; staging ID is local |
| **Tauri updater** | Static `latest.json` or a dynamic endpoint | Payload signed with minisign (Ed25519); key in app config. Before updater v2.12.0 the version in the manifest was not covered by the signature (CVE-2026-95625), allowing downgrade to an old signed build | Fixed only when `require_signed_version` is turned on (off by default) | Runs the NSIS/MSI installer (`passive` mode by default) | No (server-side if dynamic) | No | Feed fetch |
| **MSIX + App Installer** | `.appinstaller` XML at a fixed URI | Package must be Authenticode-signed by a certificate Windows trusts; the `.appinstaller` file is not signed | Higher version only, unless `ForceUpdateFromAnyVersion` is set | Per-user, OS-managed `WindowsApps`, no UAC | No | OS rolls back a failed package install | `OnLaunch` or an 8-hourly `AutomaticBackgroundTask`; can force the update before launch (`UpdateBlocksActivation`) |
| **winget** | Community manifest repo (`winget-pkgs`), signed source index | `InstallerSha256` in a manifest approved by Microsoft's validation pipeline and moderators; trust anchor is Microsoft's repo, not the publisher | Upgrades to the manifest's latest | Whatever the installer does | No | No | Source index fetch |
| **TUF / go-tuf** | Signed metadata repository | Separate roles: offline **root** (threshold of keys), **targets** (what files exist and their hashes), **snapshot** (consistent set), online low-privilege **timestamp** (freshness). Every role has an expiry | Rollback: client remembers last trusted version of each role. Freeze: short-lived timestamp expires, so a frozen mirror is detected | Out of scope | Out of scope | Out of scope | Metadata fetch |
| **Chrome / Omaha** | Omaha protocol request to Google's server | Payload Authenticode-signed; server responses additionally signed with **CUP** (ECDSA, key compiled in, rotated yearly, request nonce binds response to request) | Server decides; CUP stops replay of old responses | Per-user install in `%LocalAppData%\Google\Chrome\Application\<ver>` with side-by-side swap, or per-machine via the updater service; scheduled tasks and a service run in the background | Yes, server-side percentage rollouts and holdbacks | Server can halt a rollout; client keeps old version until restart | Yes: sends app IDs, versions, OS, and install/usage pings |
| **Firefox** | `update.xml` from Balrog, built from a URL template | MAR packages signed with Mozilla keys compiled into the updater; verified by the updater before applying | Balrog serves only valid paths; signed MARs carry product and channel | Per-machine; the **Mozilla Maintenance Service** (SYSTEM service) applies updates to avoid UAC, otherwise a UAC prompt. Updates are staged then swapped | Yes, Balrog throttling and rules | Partial MAR failure falls back to full MAR; failure is recorded and user is sent to the download page | Yes: update URL includes version, build ID, OS version, locale, channel, CPU features, memory |
| **PowerToys** | GitHub REST API latest release; picks asset by name | Relies on Authenticode of the Microsoft-signed installer; the public update-process doc describes no hash or metadata signature | Version comparison | Runs the WiX installer (per-user or per-machine variant) | No | No | Yes, a periodic background worker checks and can auto-download; Group Policy can turn this off |
| **VS Code** | Microsoft update service JSON (`url`, `sha256hash`) | Microsoft Authenticode plus hash from the update service | Server-controlled | "User setup" installs per-user to `%LocalAppData%\Programs\Microsoft VS Code` with no UAC; system setup needs admin | Yes, server-side | No | Yes, background checks unless `update.mode` is `none` or `manual` |
| **Notepad++ (WinGUp)** | XML from Notepad++'s own update server | Before 8.8.9: insufficient verification of the update XML and installer. From 8.8.9: signed update XML and installer certificate/signature checks; mandatory certificate check planned for 8.9.2 | None | Installer prompts UAC | No | No | Update check to the project's host |

Sources are listed in section 7. Where a cell says "not built in" or "no", that is what the public documentation describes; it is not proof the feature cannot be added.

## 3. Compromise case studies and their lesson for PC Steward

| Case | What happened | Lesson | ADR-032 status |
|---|---|---|---|
| **Notepad++ / WinGUp, June–Dec 2025** | The shared hosting provider was compromised. Attackers selectively redirected update requests from chosen targets to their own server and served tampered update metadata. Access survived a server rebuild because internal credentials were not rotated. Fixed by signing the update XML (8.8.9) and enforcing certificate checks. | Hosting is not a trust anchor. Sign the metadata, not just the payload. Selective targeting means a user cannot spot an attack by comparing notes, so every user must see the same signed statement. | Covered: owner-key-signed manifest, nothing trusted from the host. |
| **eScan antivirus, 2019–2024 (GuptiMiner) and Jan 2026** | First, updates fetched over plain HTTP were replaced by a man-in-the-middle for years. Then in January 2026 the update server itself delivered a trojanised update for about two hours; the malware disabled further updates. | Transport security and server security both fail. Also: a bad update can disable the updater, so recovery must not depend on the updater still working. | Covered by the signed manifest. Partly covered for recovery: ADR-032 tells owners to update manually if needed; see recommendation 9. |
| **ASUS Live Update "ShadowHammer", 2018–19** | Attackers inside ASUS shipped a malicious update signed with ASUS's real certificate, targeting about 600 MAC addresses. | A signing key in the vendor's build environment signs whatever the build produces. Keep the key away from CI and have the owner check what they sign. | Covered: key never in GitHub; owner signs after checking (decision 3). |
| **CCleaner, 2017** | The build environment was compromised; a legitimately signed build carried a backdoor to millions of users. | Signing proves who released it, not that the build was clean. Reproducible or locally checked builds matter. | Covered as a gate (Task 48 reproducibility note, decision 3). |
| **M.E.Doc / NotPetya, 2017** | The update server of a Ukrainian accounting package was taken over and used to push destructive malware. | Same as Notepad++: an unsigned or host-trusted update channel is a remote-code-execution channel. | Covered. |
| **SolarWinds Orion, 2020** | Build system injected a backdoor into signed releases, delivered by normal updates. | Same as CCleaner. A cooling-off window gives time for detection before installs accept a release. | Covered: cooling-off window (decision 6). |
| **3CX, 2023** | A trojanised upstream dependency led to signed, malicious desktop builds auto-updated to customers. | Dependency review and SBOM (TM-18) protect the release, not the updater. | Out of scope here; covered by Task 30/31. |
| **Flame, 2012** | Attackers forged a Microsoft code-signing certificate (MD5 collision) and impersonated Windows Update on local networks. | Do not trust a broad public PKI chain alone; pin your own key. | Covered: compiled key list. |
| **electron-updater signature bypass, 2020** | The Windows Authenticode check shelled out to PowerShell with an unescaped file name taken from `latest.yml`. A quote in the name made the check error out, and the error path skipped verification (fail open). It also allowed command injection. | Never pass downloaded strings to a shell. Every error in verification must mean "reject". | Not stated explicitly; see recommendation 4. |
| **Tauri updater, CVE-2026-95625** | The signature covered the binary but not the version in the manifest, so an attacker could pair a high version number with an old, genuinely signed build. | The signature must cover the version and the hash together. | Covered: version, sequence and asset hash are all inside the signed manifest. |
| **Sparkle 2.6.4 and 2.9.6 fixes** | A signed update could be replaced with another payload after verification; separately, a root-run helper allowed local privilege escalation. | Verify the exact bytes you install (no time-of-check/time-of-use gap). A privileged helper is new attack surface. | Covered: verify staged files, then promote; no elevation. See recommendation 6 for the check-then-use gap. |

## 4. Patterns, and where ADR-032 sits

**Release discovery.** Most GitHub-fed updaters (PowerToys, Velopack, electron-updater, Tauri static) read the GitHub API or a JSON/YAML file in the release. ADR-032 does the same and, unlike most, treats the API response as untrusted pointers only. Adopt as is.

**Signing and key custody.** There are three models:

1. *Trust the platform PKI* (MSIX, PowerToys, VS Code, Chrome payloads, WinSparkle installers): needs a certificate Windows trusts; a stolen certificate or a compromised build signs anything.
2. *Developer-held update key compiled into the app* (Sparkle, Tauri, newer electron-updater, Firefox MAR, Chrome CUP, Notepad++ 8.8.9+): independent of the host and of Windows PKI. This is ADR-032's model.
3. *Role separation with thresholds and expiry* (TUF): the strongest, and the only one that defends against freezes.

The serious failures in model 2 came from what the key did not cover (Tauri), from verification code that failed open (electron-updater), and from keys kept where the build runs (ASUS, CCleaner). ADR-032 avoids all three on paper.

Chrome's CUP shows the cost of key rotation done properly: one compiled key per build, rotated yearly, and the server keeps every old private key to answer old clients. For PC Steward, two compiled keys are the practical equivalent.

**Rollback and freeze attacks.** Only TUF fully handles both. Most updaters handle rollback with a version comparison and nothing for freezes. ADR-032's strictly increasing `sequence` plus a remembered highest sequence is exactly TUF's "client remembers what it trusted" rule. Freezes are the gap (recommendation 1).

**Elevation and install location.** Per-user, side-by-side, no UAC is the mainstream choice for apps that update themselves (Squirrel, Velopack, Chrome per-user, VS Code user setup, MSIX). Firefox and Chrome per-machine need a resident SYSTEM service or scheduled tasks, which conflict with ADR-020 and have had their own privilege-escalation bugs. ADR-032's per-user `versions\<ver>\` layout matches Squirrel and Chrome per-user. Velopack instead uses one stable `current\` path, which would avoid re-registering the scheduled task on every update (recommendation 7).

**Staged rollout.** Chrome, Firefox, VS Code and electron-updater stage rollouts by percentage, which needs either a server or a per-install random ID. For a product with one owner and a small user base the useful part is time, not percentage: the cooling-off window. ADR-032's decision 6 is the right shape.

**Health check and rollback.** None of the GitHub-fed frameworks surveyed verify that the new version works before declaring success; MSIX and Firefox only roll back failed installs, not failed launches. ADR-032's "shortcut and task point at the old version until the new one passes its health check" is ahead of all of them. Adopt as is.

**Privacy.** The big browsers send OS, locale, hardware and usage data; PowerToys and VS Code check in the background by default; Sparkle's profile is opt-in. ADR-032's "off until the owner turns it on, no identifiers" is at the private end. One small improvement: drop the version from the User-Agent (recommendation 5).

**Auto-install defaults to avoid.** Squirrel and electron-updater install on quit by default; PowerToys can download in the background; MSIX can force an update before launch (`UpdateBlocksActivation`) or allow downgrades (`ForceUpdateFromAnyVersion`). ADR-032 has none of these and should keep it that way.

## 5. Recommendations for ADR-032

### Adopt (already in ADR-032, confirmed by the research)

- Owner-held release key compiled into the app, never in CI or GitHub (Notepad++, ASUS, CCleaner, M.E.Doc lessons).
- One signed manifest that binds product, channel, version, sequence and asset hash (Tauri CVE lesson).
- Strictly increasing `sequence` with the highest accepted value remembered locally (TUF rollback rule).
- Per-user, side-by-side install with no UAC (Squirrel, Chrome per-user, VS Code user setup).
- Shortcut and task repointed only after a health check (ahead of every surveyed framework).
- Cooling-off window instead of percentage rollout.
- Checking off by default, no identifiers, no background download or install.
- GitHub attestations and release metadata as display-only evidence, never the trust decision.

### Add or change

1. **Add a lightweight freshness signature to close the freeze gap (new TM-22 control).** Borrow TUF's timestamp role in reduced form: a small `pcsteward-latest.json` stating `{ latestSequence, latestVersion, issuedUtc, expiresUtc }`, signed by a *separate* freshness key that is allowed to live in CI and is re-signed on a schedule (for example weekly, expiring after 30 days). The client accepts the freshness statement only if it is not expired and its `latestSequence` is not below what it has seen. If it is expired, missing or older, the app says "Could not confirm you are up to date" rather than "Up to date". The freshness key is compiled in with a distinct purpose, so stealing it lets an attacker *withhold* updates for at most one expiry period but never *install* anything. If Dan prefers not to run a scheduled CI job, the fallback is to keep ADR-032's accepted residual risk but show "newest signed release seen: X, signed on date Y" on the update screen so a freeze is at least visible.
2. **Compile in two release keys from the first version that ships the updater.** One primary (hardware token such as a YubiKey's PIV slot, which supports ECDSA P-256), one backup generated at the same time and stored offline in a different place. Either one signs a valid manifest. This is the solo-developer version of Chrome's key rotation and TUF's root threshold, and it removes the "lose the key, everyone updates manually" failure in ADR-032's negative consequences.
3. **Enable GitHub immutable releases on the repository before the first updater release.** Generally available since 28 October 2025; once on, a published release's assets and tag cannot be changed. It does not replace the manifest signature (a GitHub compromise can still publish a *new* release), but it stops after-the-fact asset swaps and tag moves, which is the Notepad++-style attack against people who have not fetched yet. Its Sigstore attestations can be shown as extra evidence under Option E.
4. **State "fail closed, no override" and "no shell" as explicit rules.** Any exception, timeout, parse error or unexpected value during discovery or verification means "Update found but not trusted" (or "Could not check"), never "proceed". There is no "install anyway" button. Authenticode checks, once a certificate exists, call `WinVerifyTrust` (or the .NET equivalent) directly, never PowerShell or `signtool`. Treat `assetName` as untrusted: it must match a compiled pattern such as `PCSteward-<version>-win-x64.zip`, and it is never used to build a path or a command. Add fixture tests for each error path to the ADR's test list.
5. **Remove the version from the User-Agent.** Send a fixed `PCSteward` User-Agent. The version comparison happens locally, so GitHub learns only that the IP runs PC Steward, not which version (which would also tell an observer which machines are behind on fixes).
6. **Close the check-then-use gap explicitly.** After hashes are verified in staging, the promoted folder is verified again by the new process (ADR-032 section 8 already does this), and promotion is a rename within the same volume, not a copy. Say in the ADR that the bytes launched are the bytes verified, with no re-download or re-extract in between (Sparkle 2.6.4 lesson).
7. **Consider a stable launch path.** Velopack keeps one `current\` folder so shortcuts and the scheduled task never change. ADR-032 instead re-registers the ADR-020 task action on each update. Both are workable; the stable path means less to repoint after Success but needs a rename-while-not-running step. This is a design choice for the updater thread, not a security issue.
8. **Store the highest accepted sequence where factory reset does not clear it, or accept that limit in writing.** ADR-013 factory reset deletes per-user state, which includes the database row ADR-032 uses. The compiled sequence of the running build is still a floor, so the exposure is small (only after an owner rollback followed by a factory reset). Either note it as accepted or keep that one value in a small file outside the reset manifest.
9. **Plan for "the updater itself is broken or disabled".** eScan's 2026 malware turned off updates; a buggy PC Steward release could do the same by accident. The manual path (verified release page plus documented hash check) should stay documented and tested for every release, and the notify-only check should live in code paths that the install code cannot break. ADR-032 already keeps manual update as the key-loss fallback; make it a standing requirement.

### Avoid

- Any third-party updater framework as the trust layer. Velopack and Squirrel do not sign their feed; WinSparkle and Tauri sign only payloads (Tauri's version gap is opt-in fixed); electron-updater is Electron-only. All add a dependency to the riskiest code path.
- MSIX App Installer for now, for the reasons ADR-032 already gives (needs a Windows-trusted certificate; can update in the background or block launch).
- winget as the updater for PC Steward itself. Its trust anchor is Microsoft's community repo and moderators, not the owner's key. Listing PC Steward in winget later as an *install* channel is fine.
- A resident service or scheduled task with install rights (Firefox Maintenance Service, Omaha), and any helper that runs elevated (Sparkle's LPE history).
- Install-on-quit, background download, forced update before launch, and allowing downgrades as a setting.
- Percentage rollouts based on a per-install ID that is sent anywhere.
- Full TUF via a library. go-tuf is Go, and there is no widely used .NET TUF client; ADR-032 plus recommendation 1 captures the parts that matter for a single-product, single-owner repository.

## 6. Suggested additions to the ADR-032 decision list

For the updater design thread to consider adding to "Decisions for the owner":

- **Freshness signature (recommendation 1):** add a CI-held freshness key with a 30-day expiry, or accept freezes as residual risk and only show the newest signed release seen?
- **Backup key (recommendation 2):** generate and compile in a second, offline backup key from the start?
- **Immutable releases (recommendation 3):** turn on GitHub immutable releases for `DanDG923/pc-steward`? (This is a repository setting Dan would change; it affects only new releases.)

## 7. Sources

- Notepad++ hijack: [BleepingComputer](https://www.bleepingcomputer.com/news/security/notepad-plus-plus-update-feature-hijacked-by-chinese-state-hackers-for-months/), [The Register](https://www.theregister.com/2026/02/02/notepad_plusplus_intrusion/), [Infosecurity Magazine](https://www.infosecurity-magazine.com/news/notepad-update-hijacked/)
- eScan: [BleepingComputer (GuptiMiner, 2024)](https://www.bleepingcomputer.com/news/security/hackers-hijack-antivirus-updates-to-drop-guptiminer-malware/), [Help Net Security (January 2026)](https://www.helpnetsecurity.com/2026/01/29/escan-antivirus-update-supply-chain-compromised/)
- electron-updater: [Doyensec signature bypass write-up](https://blog.doyensec.com/2020/02/24/electron-updater-update-signature-bypass.html), [electron-builder security docs](https://www.electron.build/docs/features/security)
- Tauri: [Tauri updater docs](https://jonaskruckenberg.github.io/tauri-docs-wip/distributing/updater.html), [CVE-2026-95625](https://www.sentinelone.com/vulnerability-database/cve-2026-95625/)
- Sparkle: [Security and reliability changelog](https://sparkle-project.github.io/documentation/security-and-reliability/), [EdDSA migration](https://sparkle-project.org/documentation/eddsa-migration); WinSparkle: [repository](https://github.com/vslavik/winsparkle)
- Velopack: [Migrating from Squirrel](https://docs.velopack.io/migrating/squirrel), [Delta packages](https://docs.velopack.io/packaging/deltas), [VelopackAsset.SHA256](https://docs.velopack.io/reference/cs/Velopack/VelopackAsset/properties/SHA256)
- MSIX: [App Installer update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings)
- TUF: [TUF specification](https://docsearch.algolia.com/mcp/docs/repo/theupdateframework/specification), [Rollback and freeze defences depend on trusted client state](https://dev.to/ioneyrezende/tuf-rollback-and-freeze-defenses-depend-on-trusted-client-state-2bg6)
- Chrome: [Client Update Protocol (CUP)](https://chromium.googlesource.com/chromium/src/+/main/docs/updater/cup.md), [Omaha protocol 3.1](https://chromium.googlesource.com/chromium/src/+/2f7dfeb1/docs/updater/protocol_3_1.md)
- Firefox: [In-app update process](https://firefox-source-docs.mozilla.org/toolkit/mozapps/update/docs/InAppUpdateProcess.html), [MAR signing and verification](https://wiki.mozilla.org/Software_Update:MAR_Signing_and_Verification)
- PowerToys: [Update process](https://microsoft.github.io/PowerToys/processes/update-process/)
- GitHub: [Immutable releases GA](https://github.blog/changelog/2025-10-28-immutable-releases-are-now-generally-available/)
- winget: [winget-cli issue 1375 (hash verification output)](https://github.com/microsoft/winget-cli/issues/1375)

Items stated from general, well-documented knowledge rather than a source fetched for this note: the ASUS ShadowHammer, CCleaner, M.E.Doc, SolarWinds, 3CX and Flame incidents; Squirrel.Windows' `RELEASES` format; VS Code's per-user install path and `update.mode` setting; electron-updater's `stagingPercentage`, `allowDowngrade` and install-on-quit defaults; Chrome's per-user install layout. They should be rechecked before being quoted in the ADR itself.
