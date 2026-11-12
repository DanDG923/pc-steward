# Phase D stage D4b: reviewed machine-scope WinGet updates (design note)

Date: 2026-11-12. Status: **built (stages 1–3; stage 4, the host check, follows the next release). Owner accepted Q1–Q3 as recommended on 2026-11-12 (§9).** Stage D4b of the Phase D plan, after the owner's 2026-11-12 choice "Updates only, Search waits". It closes Task 53 gap **U1** (machine-scope WinGet updates have no reviewed-mode route) and, when the route ships, lets the helper drop protocol 1 (Task 54 finding **R2**, under the ADR-025 transition amendment drafted in stage C4).

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

**Build note (stage 1): the helper's WinGet has no package identity.** The helper starts `winget.exe` by its full path inside the App Installer package folder, not through the app execution alias, so the process has no package identity and WinGet uses its *unpackaged* paths (`Runtime.cpp` `16187768`, lines 393–427; `Filesystem.cpp` `dca435d7`, lines 297–316 and 742–753):

- user settings are `<LocalAppData known folder>\Microsoft\WinGet\Settings\defaultState\settings.json` (and `.backup`), not the package's `LocalState`. H7 checks both locations, so it holds whichever context WinGet ends up in;
- the `winget` index is the desktop-context copy under `...\WinGet\State\defaultState`. WinGet write-locks that package, validates its signature and Store origin, then copies the index into a write-locked file under `TEMP` before opening it (`PreIndexedPackageSourceFactory.cpp` `4538d902`, lines 720–765). With the §6 protected `TEMP`, that copy is administrator-only;
- `TEMP` comes from `GetTempPathW()`, as in the table above.

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
| H7 | Settings: WinGet's `settings.json` (and `.backup`) for this user, in both the unpackaged and the packaged location (§3 build note), read bounded and strict. Refuse if any setting outside a small allow-list is present (visual, logging, telemetry, output, interactivity) | §6 |
| H7b | Dependencies and agreements, again inside the helper: run `winget download --id <ID> --version <V> --source winget --exact --scope machine --architecture <a> --installer-type <t> --disable-interactivity --download-directory <fresh admin-only folder>` (with the §6 protected `TEMP`), read the exported manifest with the same bounded strict parser, and refuse unless its single installer entry matches the reviewed type, architecture and scope, its `Dependencies` block and agreements are empty, and its manifest digest and installer SHA-256 equal the values bound at review. Any drift is a refusal, never a re-review | Install design §6 pre-launch comparison, run by the helper instead of the desktop |
| H8 | Run: the §4 command line with `TEMP` and `TMP` pointing at a fresh administrator-only folder (§6). Owned process tree, 15-minute deadline, bounded output | Existing owned-process handling |
| H9 | Answer: a typed, bounded result (outcome, exit code, sanitized output, which check refused). No success Boolean | New closed response variant |

The desktop still performs its own checks first (live review, fresh source consent, the 30-minute review window, claim before the prompt) and its own post-update check. Neither side trusts the other's word for success.

### 5.1 How the route runs on the protocol-2 helper (build note, stage 1b)

The route uses the existing protocol-2 stage machine unchanged (A0 to A3, stages B, C and D, one launch). It does not add a second helper entry point. The single helper item picks the WinGet work when stage B sees the route. The A2 and A3 gates, the one-use launch permission, settlement and disposal stay with the item for both routes.

- **Shape (H2).** `WingetMachineReviewedRoute.TryGetShape` (Application) is the one shape check. The fixed command lines, the helper's A1 and the desktop's reply reader all call it. `Admits` adds the review gate, which stays closed until stage 3. A3 also refuses the route: it is not on the capability allow-list, and the desktop dispatcher requires that capability.
- **Profile digest.** The route's profile fingerprint is the reviewed HKLM registration digest from the `wgm1` evidence. Stage B receives it as the expected value, and the reply's `observedProfileFingerprint` must equal it.
- **Reply shape.** The route has its own reply shape, `WinGetMachine`. It has the same 20 properties and bytes as the package reply. Only predicate S12 differs: the version fields are checked with the reviewed version grammar (`UpdateJobSelection.IsVersionText`: already trimmed, at most 128 characters, the allow-listed characters) instead of Chocolatey's normalized grammar. The helper writes it with `CreateWinGetMachineResponse`, and the desktop reads and re-checks it with the shape chosen by the frozen route. A frame valid in one shape is never accepted for a different route.
- **Stages.**

  | Stage | Machine WinGet route |
  |---|---|
  | B (static) | H2, then H4 (trusted `winget.exe` and its C++ runtime folder), H7 (settings) and H6 (the one registration, in either view, whose digest equals the reviewed one). Reports the frozen installed text and the registration digest. |
  | C | `winget source export --name winget` through the owned launcher with its own protected `TEMP`. It must be exactly the trusted source (H5). Reports the frozen candidate text. Then H7b (§5.2). |
  | D | Stage B again. It must find the same client path, the same registry view and the same key name. |
  | Launch | The fixed §4 `upgrade` line, its own protected `TEMP`, and the fixed environment below. |

- **Refusals** use the existing closed reasons:
  - a registration that is gone or changed: `InstalledMismatch`;
  - a source query that fails or shows anything but the trusted source, or an H7b download that fails or does not match (§5.2): `PreflightQueryFailed`;
  - everything else: `MachineInputsUnsupported`.
- **Environment.** The WinGet process never inherits the helper's environment. It gets a fixed block whose values all come from system and known-folder APIs, never from environment values:
  - the Windows and program folders, and the requester's profile folders (resolved the way WinGet resolves them);
  - `TEMP` and `TMP` set to the run folder;
  - a `PATH` of system folders plus the App Installer C++ runtime framework folder (`Microsoft.VCLibs.140.00.UWPDesktop`, same architecture, under `WindowsApps`, no reparse point). The `winget.exe` that the helper starts by path loads the C++ runtime from that framework, because it has no package identity to supply it. Stage 4 confirms this on the owner's PC.
- **Run folders.** Each launch creates a fresh S1 folder. It is deleted once that launch is known to have settled. A launch that is not known to have settled keeps its folder (administrator-only and unfindable), and the work is reported unsettled.

### 5.2 The exported manifest and the H7b re-check (build note, stage 2a)

- **What `winget download` leaves.** WinGet 1.29.380 writes the installer and a merged manifest for that one installer (`YamlWriter::OutputYamlFile`) into `--download-directory`. The manifest has the root keys `PackageIdentifier`, `PackageVersion`, optional `Channel` and `Moniker`, the default-locale fields (including `Agreements` when declared), `Localization`, `Installers` (one item), `ManifestVersion` and `ManifestType: merged`. Installer fields appear only inside the item: `Architecture` (lowercase), `InstallerType`, `NestedInstallerType`, `InstallerSha256` (lowercase hex), `Scope` (lowercase, omitted when the manifest does not declare it), and `Dependencies` inside the item. The emitter is libyaml's block emitter: UTF-8 without a byte-order mark, indentless sequences, long values folded onto lines indented further than their key, and CRLF line ends from the text-mode file stream.
- **The reader** (`WingetExportedManifest`) recognises that layout and refuses anything else. It does not implement YAML:
  - at most 1 MiB, strict UTF-8, no byte-order mark, one line-end style throughout, a final line end, and no tab or other control character;
  - fail closed first: any line anywhere whose first key-like word is `Dependencies`, `PackageDependencies`, `WindowsFeatures`, `WindowsLibraries`, `ExternalDependencies`, `Agreements`, `Agreement`, `AgreementLabel` or `AgreementUrl` refuses the manifest (Q3), even inside a long description, so a false match can only refuse;
  - column-0 lines are unique root keys; an installer field at the root (`Scope`, `Architecture`, `InstallerType`, `InstallerSha256`, `NestedInstallerType`, `NestedInstallerFiles`, `InstallerUrl`) refuses;
  - `PackageIdentifier` (case-insensitive, as WinGet matches IDs), `PackageVersion` (exact), `ManifestVersion` and `ManifestType: merged` are single-line values, and `Installers` holds exactly one item;
  - the item's fields are its first line and the lines indented by exactly two spaces, each at most once; `Architecture`, `InstallerType` and `InstallerSha256` (64 hex digits) are required single-line values and must be the reviewed ones; `Scope` must be present and exactly `machine` (Q1); `NestedInstallerType` or `NestedInstallerFiles` refuses (the route has no nested installer types);
  - values are plain, or quoted with nothing to unescape.
- **The manifest digest** is the SHA-256 of the file's exact bytes. The review and the helper run the same WinGet against the same source for the same user, so the same manifest gives the same bytes; any difference is drift and refuses.
- **The download folder** (`WingetDownloadFolder`) must hold exactly two plain files and nothing else: one `.yaml` manifest and one installer. No directory, reparse point or extra file is accepted. The installer's streamed SHA-256 must equal the manifest's `InstallerSha256`. When there is exactly one manifest it is read first, so its own refusal is the one reported (WinGet also downloads declared dependencies into a `Dependencies` sub-folder, which would otherwise read as an unexpected folder).
- **Installer type.** The helper passes the reviewed type, which must match exactly. The review lets WinGet choose and records the manifest's type, which must be one of the route's six; any other type (portable, zip, MSIX, Store) refuses. This replaces the COM `GetApplicableInstaller` reading in §4 with the same WinGet selection, read from the file whose digest is bound.
- **H7b in the helper**, inside stage C after the source export passes: `winget download` with the fixed §4 line into the `d` sub-folder of its own fresh S1 run folder (a second folder, separate from the source query's and the upgrade's), with its own 10-minute budget capped by what is left of the endpoint's. Only after the download settled and exited 0 does the helper read the folder, then it deletes it. The manifest digest and the installer SHA-256 must equal the reviewed `wgm1` evidence (compared in fixed time). A download that fails, a folder that does not read, or any mismatch is `PreflightQueryFailed`; cancellation and time-out stay `Undetermined`.
- **Budget (residual, for stage 3).** The endpoint's whole execution budget is 11 minutes today, and the installer is now downloaded twice inside it (H7b, then the upgrade). A large installer on a slow link can time out before the upgrade starts. That fails safe (nothing ran). Stage 3 revisits the budget before the route is admitted.

### 5.3 The review and Confirm on the desktop (build note, stage 2b)

- **Composition.** `ReviewedUpdateExecutionWorkflow` takes an optional `IWingetMachineInstallerEvidenceSource`. Without it (production until stage 3), a machine-scope WinGet review binds no mode and runs on protocol 1 exactly as before. With it, a machine-scope WinGet review takes the whole-PC route below.
- **Review.**
  1. The selection must have the route's shape (WinGet, the `winget` source, machine scope, not Preview, x86, x64 or arm64); it then binds `Selected(Silent)`.
  2. The registration digest comes from the same fresh inventory the selection was prepared from: exactly one product with the selection's key, and exactly one of its instances read from the Windows uninstall registry for the machine with the selection's architecture (x86 is the 32-bit view, x64 the 64-bit view). The digest binds the view, the key name and the version as the inventory read them. An arm64 app has no matching registry view yet and is refused. The helper finds the registration again by this digest (H6); a value the inventory trimmed would not match there, which fails safe.
  3. `WingetMachineInstallerEvidenceSource` checks that the effective `winget` source is the trusted default, then runs one `winget download` of the exact package and version with `--scope machine --architecture <a>` and no `--installer-type` (WinGet chooses), without elevation, into a fresh random folder under `%LOCALAPPDATA%\PCSteward\downloads\WinGetReview` (inside the download cache root, so Clear downloaded files removes anything left). Storage creates and deletes that folder (`ReviewDownloadFolders`): each create and delete re-checks the cache root and every ancestor from the volume down for reparse points, accepts only `WinGetReview\<32 hex>`, and deletes each object handle-bound relative to the verified root, the same way Clear downloaded files does; a reparse point found inside the folder is never entered or removed, and the folder is then left for Clear downloaded files (Codex review of PR #75). It reads the folder with the §5.2 reader, records the installer type, manifest digest and installer SHA-256, and has the folder deleted (a download that did not settle leaves it). PC Steward never accepts agreements, so a package with one stops the download and the review shows that it couldn't check the installer and that a licence agreement may be the reason. Declared dependencies, no machine scope, and other installer types are shown as "Not offered in this version" with the reason.
  4. The review holds `wgm1:<type>:<registration>:<manifest>:<installer>` and the inventory's observation time. This is comparison data, not a security boundary: a same-user program could change what the review read, but the helper's own download must match it exactly (H7b), and the helper only trusts what it downloads itself.
- **Confirm.** After the existing fresh re-preparation and evidence comparison, the fresh inventory's registration digest must equal the reviewed one, and the requester's SID must be readable; both are checked before approval. After approval the job is frozen with the mode and evidence into a `WinGetMachineSilentV1` operation (`WingetMachineReviewedRoute.Freeze`). The review gate does not admit the route before stage 3, so Freeze refuses and nothing reaches an executor, and never protocol 1.
- **Cost.** A batch review of whole-PC apps downloads each installer once at review, one after another. Stage 3 revisits this with the budget residual in §5.2.
- **Stage 2c** routes a frozen whole-PC operation to the protocol-2 helper, still not admitted (§5.4).

### 5.4 Dispatch of the frozen operation (build note, stage 2c)

The whole-PC route reuses the path every approved WinGet job already takes (the batch coordinator, `ApprovedUpdateExecutor` and `ElevatedUpdateRunner`), so the existing durable claim, fresh-evidence check, history and post-update inventory check apply unchanged. It does not use the Chocolatey `ReviewedUpdateDispatchWorkflow`.

- **Routing.** `ApprovedUpdateExecutor` sends a machine-scope job that carries a reviewed operation only to `ElevatedUpdateRunner.RunReviewedAsync`. A machine-scope job without one stayed on protocol 1 until stage 3 removed it (§5.5); there was never a fallback from one to the other.
- **Runner checks.** The job's digest and the coordinator's fresh evidence are checked first, as on protocol 1. The operation must then be admitted by the review gate and bound to this exact job (`BindsJob`), and the runner must have been composed with the whole-PC exchange; anything else is denied before elevation is requested.
- **One exchange.** `WingetMachineReviewedExchange` checks the gate, the route's reply shape (`WinGetMachine`) and the protected deployment capability (in that order, so nothing is read from disk for an operation the gate refuses), draws one fresh nonce, runs exactly one protocol-2 exchange, and returns `ReviewedUpdateProtocol2Interpreter`'s observation. The helper repeats every check itself (H1–H9).
- **Result.** Work that may have started but did not settle is a helper failure whose owned work stays unresolved, never "nothing ran". A refusal before anything ran is a denial. An exit of 0 waits for the inventory check, which must find the exact registration at the candidate version before anything is called a success. WinGet's documented restart codes report a restart (Task 51); a restart needed before installing is a failure. Any other exit is a provider failure.
- **Composition.** `WingetMachineUpdateComposition.CreateElevatedRunner` builds the runner over one parameterless (Desktop-role) capability reader shared by the exchange and the protocol-2 transport. The architecture tests pin it as the only other composition of that transport. Production does not call it until stage 3, which also wires the review evidence source (`ReviewDownloadFolders`) into the desktop.

### 5.5 Admitting the route and retiring protocol 1 (build note, stage 3)

- **Admitted.** The review gate (`ReviewedUpdateReview.HasSupportedMode`) admits an operation with the `WinGetMachineSilentV1` shape, and the route is the third entry of `DeploymentCapabilityPolicy.CapabilityOperationAllowlist`; the checked-in capability statement (`PCSteward.DeploymentCapability.txt`, now 188 B) and the owner-only payload and probe scripts list the same three tokens.
- **Desktop.** `App.xaml.cs` composes the review evidence source (`WingetMachineInstallerEvidenceSource` over storage's `ReviewDownloadFolders` under the download cache) and `WingetMachineUpdateComposition.CreateElevatedRunner()`. A machine-scope WinGet review without the evidence source (only possible in tests) is refused at review with the route's reason; there is no other machine route.
- **Protocol 1 removed** (§8): every line that is not self-update, probe or `--protocol 2` exits 20 before the helper connects or reads anything; the restricted pipe accepts only the protocol-2 locator; `ElevatedUpdateRunner` has only `RunReviewedAsync` and no public constructor. ADR-025's transition amendment is closed and the S2 acceptance is recorded there next to M1.
- **Qualification probe.** The probe evaluates G1 for every allow-list token, so its frames and result file now carry three entries (the passing frame on the compiled contract is 613 B; the largest valid frame 669 B, inside the spec's 1,341-B bound). A deployment's probe PASS needs all three `Supported`, which the next release's capability statement provides.
- **Residual carried over:** the double download (review and helper, §7) stays as accepted cost for version 1.

## 6. Elevated WinGet and the owner's profile (security)

Owner's stance (2026-11-12): findings that need an already compromised machine are low priority, but anything that turns a user-level foothold into administrator matters. These two do, and **both exist today on protocol 1 with no mitigation**, and in any `winget upgrade` run from an administrator prompt.

**S1. Installer swap in `%TEMP%` (high).** A program running as the owner can replace the downloaded installer after WinGet's hash check and before the elevated run, and so run its own code as administrator. Mitigation: the helper creates `%SystemRoot%\Temp\PCSteward-<random 128-bit name>` owned by Administrators, with a protected ACL for SYSTEM and Administrators only, sets `TEMP` and `TMP` for the WinGet process to it, and deletes it afterwards. Standard users can add entries to the system `Temp` folder but cannot list it or rename or delete entries they do not own, and the folder is checked after creation (owner, ACL, no reparse point, empty), so a folder that already existed is refused rather than repaired. *(Build note, stage 1: this replaces an earlier `%ProgramData%\PCSteward\HelperTemp` parent, which a standard user could create first and keep a handle to.)* Each WinGet process (the H7b `download` and the `upgrade`) gets its own folder, so the download folder named on one command line says nothing about the other. WinGet will still add the owner to the ACL of its own `WinGet` subfolder (§3), so this does not make the folder unwritable; it makes it **unfindable** for a non-elevated program, which cannot list the parent, read the elevated process's environment, or guess the name. That closes the practical attack. The residual (an attacker that can already read elevated process memory) needs administrator rights already.

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
2. **Dependency and agreement evidence** (§7) at review and in the helper (H7b), still not admitted. *(Build note: split into 2a, the manifest reader and H7b (§5.2); 2b, the desktop review evidence and Confirm freeze (§5.3); and 2c, the protocol-2 dispatch of the frozen operation (§5.4).)*
3. **Admit the route and retire protocol 1** (§8) in one change, with the ADR-025 amendment closed, the S2 acceptance recorded, and STATUS/TRACEABILITY updated.
4. **Host check on the owner's PC** after the next release: one real machine-scope update of a small app through the in-app updater build, one combined paste.

## 11. Tests the stages need

Negative fixtures for every H check (changed ID, version, source, scope, architecture, installer type, mode, route, stale review, protocol-1 line, wrong peer); a registration that moved between review and launch; settings files with each refused key; protected temp folder ACL, name length, reparse-point refusal and clean-up after timeout and cancellation; the helper never launching WinGet after any refusal; exit codes 0, restart codes and failure mapping; post-update check refusing a user-scope result for a machine review.
