# Phase D catalogue installs: install design note (stage D1)

Date: 2026-11-12. Status: **design. Owner answers recorded 2026-11-12 (§12): Q1 and Q4 accepted, Q3 accepted for version 1, Q2 widened to a coverage goal. No code change.** Stage D1 of the Phase D plan the owner agreed on 2026-11-12 ("Close C, D design in parallel": user scope first, search ships early). It prepares Task 56 and the Search screen of Task 55.

Requirements: `FR-CAT-01–06`, `FR-UPD-15`, `FR-SRC-02/07–10`, `FR-HIS-01–06`, `TM-02–04/07/13`. Builds on [ADR-023](../adr/ADR-023-CATALOGUE-INSTALL-OPERATIONS.md), [ADR-025](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md), the [Q1 and Q5 policies](../requirements/V2_PLANNING_DECISIONS.md), the [Phase D brief](../requirements/V2_PHASE_D.md) and the WinGet findings F6/F7 in [provider mapping revision 2](TASK_50_PROVIDER_MAPPING_V2.md). Base: `phase/4-beta` at `c045fe5`.

**In scope:** how an install runs, the "is it already installed?" rule (the absence predicate Task 56 must define and version), licence agreements, dependencies, user versus machine scope, what the review and the result check bind, and a sketch of the Search screen.

**Out of scope:** any change under `src/`, the WinGet COM version choice (stage D0, running in parallel; §10 lists where its outcome matters), turning any route on, and any action on the owner's PC. Nothing here admits an install route, closes a Task 55–59 gate, or approves a host step. Install stays switched off in every release until the D5 security review passes, because releases are public.

## 1. Summary of the recommendations

1. **Run installs through the `winget` command line, not the COM install call.** Use COM only for evidence (search, metadata, the applicable installer, agreements), as the update route already does (F7). The command line has a real "don't upgrade" guard (`--no-upgrade`); the COM `InstallPackageAsync` call has none and simply installs over an existing copy (§3).
2. **The user-scope route (D3) covers MSIX and portable packages only.** For those two types WinGet itself decides where the app goes, so the scope can be enforced. Vendor EXE and MSI installers that only *say* they install per user stay unavailable in version 1 (§7.1).
3. **Version 1 refuses any package that declares a licence agreement.** PC Steward never passes `--accept-package-agreements`, so WinGet itself stops if an agreement appears later. Showing and accepting agreements can come in a later version, with the agreement text bound to the review by digest (§5).
4. **Version 1 refuses any package whose installer declares a dependency.** WinGet's COM API exposes no dependency data in 1.10, 1.11 or 1.12. The dependency list is read from the exact manifest that `winget download` exports into an app-owned folder (§6).
5. **The absence predicate `AbsenceV1` blocks on any exact or plausible match in any view, and on any incomplete view.** It errs towards blocking. A possible match shows what was found and offers no override in version 1 (§4).
6. **Machine scope (D4) runs the same command line from the protected helper, one Windows prompt per app,** only for installers that declare machine scope, and in version 1 only on a PC with a single Windows user profile (§7.2).

## 2. Where things stand (code facts at `c045fe5`)

| Fact | Evidence |
|---|---|
| WinGet updates run through the trusted `winget.exe`, resolved from the App Installer package, never PATH | `MachineTrustedWinget.ResolveExecutable`, `WindowsWingetProcess` |
| The only WinGet update route is the user MSIX route. Its fixed arguments end `--scope user --architecture <a> --disable-interactivity --silent --installer-type msix` | `WingetReviewedUpdateArguments`, `UpdateRouteContract.WinGetUserExactV3`; mapping F6 |
| COM is evidence only: an exact-ID, two-result query on the predefined trusted `winget` catalogue | `WinGetComStructuredCatalogQuery`; ADR-003 |
| The COM projection is `Microsoft.WindowsPackageManager.ComInterop` 1.10.340 with the in-process server | `Directory.Packages.props`, `PCSteward.Desktop.csproj` |
| The CLI source check refuses unless `winget` is exactly the Microsoft community source | `WingetCliSourceGuard` |
| Installed views exist for HKLM 64-bit, HKLM 32-bit, HKCU and current-user MSIX | `RegistryInventoryProvider` (`RegistryViewKind`), `MsixInventoryProvider` |
| Existing matching (`ProductPackageMatching`) is for updates: it picks the strongest single candidate and treats name-only matches as not actionable. It does not answer "is anything like this installed?" | `ProductPackageMatching.Resolve`, `NormalizeAlias` |
| The elevated helper is Native AOT with no WinRT or COM | `PCSteward.ElevatedHelper.csproj` (`PublishAot`); ADR-033 |
| No install operation, route, job variant or UI exists | Repository search |

## 3. How an install runs

### 3.1 What the WinGet source shows (pinned)

All references are to `microsoft/winget-cli` at tag `v1.10.340`, the projection the app ships, unless stated. Blob SHAs identify the exact files read.

| Question | Command line (`winget install`) | COM (`PackageManager.InstallPackageAsync`) |
|---|---|---|
| Already installed? | Without `--no-upgrade`, install quietly **turns into an upgrade**. With it, WinGet stops with `APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED`. `Workflows/UpdateFlow.cpp` blob `99d0a82e`, lines 293–318 | No such option. The install path skips every installed-version check and runs the installer anyway, which for EXE/MSI means a reinstall, repair or downgrade. `Microsoft.Management.Deployment/PackageManager.cpp` blob `99ccf488`, lines 818–855 (`isUpgrade` false) |
| Agreements | Without `--accept-package-agreements` and with interactivity disabled, WinGet stops with `APPINSTALLER_CLI_ERROR_PACKAGE_AGREEMENTS_NOT_ACCEPTED` whenever the manifest declares one. `Workflows/PromptFlow.cpp` blob `5e298f5c`, lines 136–145 and 186–219 | `AcceptPackageAgreements` false gives `PackageAgreementsNotAccepted`. Same flow |
| Dependencies | Installed automatically unless `--skip-dependencies` **or the user setting `installBehavior.skipDependencies`** is set. No option makes WinGet refuse instead. `Workflows/DependenciesFlow.cpp` blob `4457ec2b`, lines 238–256 | Same, through `SkipDependencies` |
| Scope | `--scope user` requires the installer's declared scope to be User. Installers with no declared scope are refused. MSIX, portable and Store packages ignore the manifest scope because WinGet controls it. `Workflows/ManifestComparator.cpp` blob `96d617b5`, lines 537–547; `AppInstallerCommonCore/Manifest/ManifestCommon.cpp` blob `16e75944`, lines 920–926 | `PackageInstallScope.User` or `System` behave the same. `UserOrUnknown` lets undeclared installers through |
| Evidence before install | None structured. `winget download` exports the merged manifest of the selected installer as YAML next to the download. `Workflows/DownloadFlow.cpp` blob `119e2d1b`, lines 325–334 and 748–757 | `GetApplicableInstaller(options)` gives type, nested type, architecture, scope, locale and elevation requirement. `GetCatalogPackageMetadata()` gives licence fields and agreements. No dependencies. `PackageManager.idl` blob `29f2e916`, lines 421–427, 474–523, 1390–1393 |
| Usable from the elevated helper | Yes: it is a process | No: the helper has no WinRT/COM |

The COM interface definition is the same in 1.11.510 apart from comments (blob `16576de5`; contract version still 12). 1.12.470 (blob `d931ff6b`, contract 13) adds fonts, Store licence and platform download options and a version property. **No version adds a no-upgrade option or dependency data.**

### 3.2 Options

| Option | For | Against |
|---|---|---|
| **A. Command line for both scopes; COM for evidence only (recommended)** | Has the provider-level no-upgrade guard ADR-023 requires. One mechanism for user and machine scope, and the helper can run it. Reuses the trusted client resolution, source check and owned-process handling of the update route (F7). | No structured progress; results come from exit codes. Two engines (CLI and COM evidence) must be the same build, which F7 already requires. |
| B. COM install for user scope, command line for machine scope | Structured progress and `RebootRequired`. | No no-upgrade guard, so ADR-023's "documented, tested provider no-upgrade guard" cannot be met. Two install mechanisms to review. |
| C. Wait for a future COM contract | — | Nothing in 1.12 suggests it is coming; Phase D would stall. |

### 3.3 Fixed argument lists (recommended)

User route (`WinGetUserInstallV1`, run unelevated by the desktop runner):

`install --id <ID> --version <V> --source winget --exact --scope user --architecture <x86|x64|arm64> --installer-type <msix|portable> --disable-interactivity --silent --no-upgrade`

Machine route (`WinGetMachineInstallV1`, run by the protected helper):

`install --id <ID> --version <V> --source winget --exact --scope machine --architecture <x86|x64|arm64> --installer-type <msi|wix|exe|inno|nullsoft|burn> --disable-interactivity --silent --no-upgrade`

If the reviewed applicable installer declares a locale, add `--locale <reviewed locale>` so WinGet cannot pick a different installer. Each value comes from the reviewed canonical payload after validation; nothing is passed through as free text.

**Never representable** (ADR-025's compiled allowlist): `--accept-package-agreements` (in version 1), `--accept-source-agreements`, `--skip-dependencies`, `--dependency-source`, `--force`, `--override`, `--custom`, `--location`, `--ignore-security-hash`, `--ignore-local-archive-malware-scan`, `--log`, `--rename`, `--uninstall-previous`, `--allow-reboot`, `--interactive`, `--header`, authentication options, `--manifest` and any name or moniker query.

**Pre-launch checks of the client settings.** WinGet's user settings file can change behaviour without any argument: `installBehavior.skipDependencies`, `installBehavior.requirements.*` (scope, architectures, installer types, locale) and `installBehavior.defaultInstallRoot` (where portable apps go). `winget settings export` returns the admin settings and the path of that file, not its contents (`Workflows/SettingsFlow.cpp` blob `49eca405`, lines 17–45; the keys are in `AppInstallerCommonCore/Public/winget/UserSettings.h` blob `f791c5f3`, lines 170–182). Before each launch the runner takes the path from that bounded export, reads the file with a bounded parser that accepts WinGet's comment syntax, and refuses if `skipDependencies` is true, if any `requirements` value is set, if `defaultInstallRoot` is set for a portable install, or if the admin settings enable local manifests, hash override or archive malware-scan override. A race remains between that read and the launch; the result check (§8) is the backstop.

**Results.** Exit 0 is never success on its own (§8). The known `APPINSTALLER_CLI_ERROR_*` codes map to typed outcomes: already installed (route to Updates, not a failure of PC Steward), agreements not accepted, no applicable installer, download or hash failure, installer failure, restart required. Any other code is Unknown. Restart detection reuses the C1 work for Task 51.

## 4. "Is it already installed?": the absence predicate `AbsenceV1`

ADR-023 requires a deterministic, versioned predicate before any install, defined and fixture-tested in Task 56. This is the proposed version 1. Its identifier `AbsenceV1` and its evidence time are bound into the install payload, so a later version cannot silently reinterpret an old review.

### 4.1 Views and completeness

The predicate reads five views, all fresh (collected for this review, not from the last scan):

| View | Source |
|---|---|
| W | WinGet's installed correlation: the COM composite catalogue with installed packages, exact-ID query on the selected package |
| M64, M32 | HKLM uninstall keys, 64-bit and 32-bit |
| U | HKCU uninstall keys of the requesting user |
| X | Current-user MSIX packages |

A view that fails, is denied, times out, is cut short by a bound, or holds an unreadable entry makes the result **Unknown**, never Absent. Other Windows users' HKCU and MSIX are not visible to an unelevated reader; §7 says how each route handles that.

### 4.2 Signals

Candidate side (from COM for the exact reviewed version): package ID, catalogue name, catalogue publisher, `PackageFamilyNames`, `ProductCodes`, and the ID's last segment (for `Git.Git`, `Git`).

Installed side, per entry: display name, publisher, display version, key name, MSI product code (key name when `WindowsInstaller=1`), WinGet portable markers (`WinGetPackageIdentifier`, `WinGetSourceIdentifier`), MSIX family name, MSIX display name and publisher. Entries with `SystemComponent=1` or a `ParentKeyName` still count: hiding is not absence.

### 4.3 Rules

`Words(s)`: remove the entry's own display version string; remove the whole-word markers `x64`, `x86`, `amd64`, `arm64`, `64-bit` and `32-bit` (any case); split on every character that is not a letter or digit; upper-case each word with the invariant culture; drop empty words. Values over 256 characters make the view incomplete rather than being cut.

| Rule | Match when | Result |
|---|---|---|
| E1 | W reports an installed version for the exact ID | **Installed**: send the owner to Updates |
| E2 | An X family name equals one of the candidate's `PackageFamilyNames` (ordinal, case-insensitive) | Installed |
| E3 | An M64/M32/U product code or key name equals one of the candidate's `ProductCodes` | Installed |
| E4 | A U or M entry's `WinGetPackageIdentifier` equals the ID (with source `winget`) | Installed |
| P1 | `Words(installed name)` and `Words(catalogue name)` are equal, or the shorter list is the start of the longer one, word for word, and the shorter list joined has at least 3 characters | **Possible match**: blocked |
| P2 | Same as P1 against `Words(ID last segment)` | Possible match |
| — | No rule matches in any view, every view complete | **Absent** |

Publisher is deliberately **not** used to clear a P1/P2 match: ADR-023 says a possible match at any confidence blocks. So "Notepad++" (words `NOTEPAD`) is blocked while Windows Notepad is installed. Architecture, scope and version never clear a match either: an x86 copy blocks an x64 install, and a linked copy of any version means Installed. That costs some false blocks, which are visible and honest; a missed match would install a second copy.

Order: Unknown wins over everything, then Installed, then Possible match, then Absent. The UI lists the entries that matched (name, publisher, version, where found) so the owner can see why.

### 4.4 Fixtures Task 56 must pin

Linked WinGet install (E1); vendor-installed, unlinked MSI with matching product code (E3); unlinked EXE with only a matching name (P1); `7-Zip 24.08 (x64)` against catalogue `7-Zip` (both become `7 ZIP`); x86 copy present when x64 is reviewed (blocked, never Absent); per-user MSIX against a family name (E2); WinGet portable marker (E4); hidden `SystemComponent` entry; shared names (`Notepad` versus `Notepad++` is matched; `Git` versus `GitHub Desktop` is not, because `GIT` is not the word `GITHUB`; `Zoom` versus `Zoom Workplace` is matched); one view denied, one view over its bound, one entry unreadable (Unknown); names with non-BMP characters; names of exactly 256 and 257 characters.

### 4.5 When it runs

At review; again immediately before launch (inside the 30-minute review window, ADR-025 N1); and, for the machine route, again inside the helper over the views the helper can read itself (§7.2). Any change in the result or in the matched entries needs a new review.

### 4.6 Not in version 1

No "this is a different app" override. If it is added later, it must name the exact installed entry and catalogue ID, be re-checked at dispatch, and come with its own review. No AI or fuzzy scoring ever grants authority.

## 5. Licence agreements

**What exists.** WinGet manifests have an `Agreements` list (label, text, URL), mostly used by Store packages, and separate informational `License` and `LicenseUrl` fields. COM returns both through `GetCatalogPackageMetadata()`, in the user's locale. Many vendor installers also accept their own licence silently when run with silent switches; that is an installer effect, not a WinGet agreement.

| Option | Effect |
|---|---|
| **A. Version 1 refuses packages that declare agreements (recommended)** | The review shows "This package asks you to accept an agreement. PC Steward can't install it yet." No accept flag is ever passed, so if an agreement is added to the manifest after the review, WinGet itself stops. Licence and licence URL are still shown as information, with the line "Installing silently accepts the publisher's licence terms as their installer applies them." |
| B. Show and accept, bound by digest | The review shows each agreement's label, text and URL as plain text. The owner ticks "I accept these agreements". The payload binds the locale and a SHA-256 of the canonical agreement list. Before launch PC Steward re-reads them and refuses on any difference, then passes `--accept-package-agreements`. A small race remains between that re-read and WinGet's own read. |

Option B can be added later without changing anything in A.

## 6. Dependencies

ADR-023 version 1 allows only an empty dependency set, or one where every dependency is already satisfied, and forbids skipping. Neither WinGet API reports dependencies (§3.1), and a WinGet user setting can skip them silently.

| Option | Effect |
|---|---|
| **A. Read the exported manifest; refuse any declared dependency (recommended)** | At review, run `winget download --id <ID> --version <V> --source winget --exact --scope <user|machine> --architecture <a> --installer-type <t> [--locale <l>] --download-directory <app-owned folder> --disable-interactivity` with the same reviewed scope, architecture, type and locale as the install (`download` accepts `--scope`: `Commands/DownloadCommand.cpp` blob `0848ecda`, line 31), then read only the exported YAML (bounded, strict parser, no anchors or tags). Its single installer entry must match the reviewed applicable installer exactly (installer type, nested type, architecture, scope and locale); otherwise refuse, because the dependencies inspected would belong to a different installer than the one run. Refuse if its `Dependencies` block is not empty, and bind the installer SHA-256 it names into the review. Before launch, run it again into the same folder: WinGet reuses an existing file whose hash still matches the manifest and exports the manifest again (`DownloadFlow.cpp` lines 262–279, 325–334 and 337–360), and any change in the manifest is drift. Cost: the installer is downloaded twice (once here, once by the install). The downloaded file is never run, and is deleted after the item ends. |
| B. Refuse-only scan of `winget show` text | Cheap, but the text depends on locale and layout; a format change could hide a dependency. Not recommended. |
| C. Keep installs off until Microsoft exposes dependency data | Safe; no install at all for now. |

**MSIX framework packages.** An MSIX can pull in frameworks (for example VCLibs) through its own manifest, outside WinGet's list. With option A the downloaded `.msix` is available: read its `AppxManifest.xml` (bounded) and require every `PackageDependency` to be already installed for the user at or above its minimum version, otherwise refuse. After install, any new MSIX package other than the target makes the result "Installed with extra changes" (Unsafe), not verified.

Allowing "already satisfied" WinGet package dependencies (which ADR-023 permits) is left for a later version; it needs its own matching rule.

## 7. User versus machine scope

### 7.1 User route (D3, first)

- **Installer types:** MSIX and portable (including a zip whose nested installer is portable). For these WinGet places the app itself (§3.1), so `--scope user` is enforced by WinGet, not by vendor code. EXE, MSI, Inno, Nullsoft, WiX and Burn installers that declare user scope are **not** offered in version 1: their own code decides where they write and can ask for elevation.
- **Applicable installer:** at review, `GetApplicableInstaller` with scope `User`, the reviewed architecture and installer type must return exactly the reviewed type, architecture and locale, with elevation requirement Unknown or ElevationProhibited. `ElevationRequired` or `ElevatesSelf` blocks.
- **Run:** the unelevated desktop runner launches the trusted `winget.exe` with the §3.3 list. PC Steward never requests elevation and never retries as machine scope. If a Windows prompt appears anyway, it came from the package, and the review says this can happen.
- **Effects to show at review:** MSIX: "Installed for your Windows account only, as a Windows app package." Portable: "Files go into your user folder under WinGet's packages folder; WinGet adds a command link and may add that links folder to your user PATH."
- **Absence views:** all five. A machine-wide copy blocks a user install too, so there are never two copies.
- **Updates afterwards:** today's update route covers user MSIX only. A portable app installed by PC Steward could not be updated by PC Steward until a portable update route exists. Recommendation: build the portable update route inside D3, so everything PC Steward installs, it can also update (question Q2).

### 7.2 Machine route (D4, after D3)

- **Installer types:** MSI, WiX, EXE, Inno, Nullsoft, Burn, with declared scope **Machine**. Undeclared scope is refused (WinGet refuses it too with `--scope machine`).
- **Run:** a new closed install variant on the protocol-2 helper, one Windows prompt per app, exactly like a Chocolatey update today. The helper resolves the trusted `winget.exe` from the App Installer package path and runs the §3.3 list. It never accepts a path or argument from the desktop.
- **Helper checks:** the helper re-runs `AbsenceV1` over HKLM 64/32 and the requesting user's hive (`HKU\<requesting SID>`, not the elevated `HKCU`), and repeats the CLI source and settings checks. It cannot read the MSIX view or WinGet's COM correlation, because it has no WinRT. The `--no-upgrade` guard covers WinGet's own correlation inside the install process. Closing the MSIX gap in the helper (or proving it irrelevant for these installer types) is a D4 design item; until it is closed, the machine route stays off.
- **Other Windows users:** an unelevated reader cannot see other users' per-user apps, and ADR-023 does not let a disclosure replace that evidence. Proposed version 1 rule: the machine route is available only when `ProfileList` holds exactly one ordinary user profile (an `S-1-5-21-…` SID with a profile path), and that profile is the requesting user. Otherwise it is unavailable, with the reason shown. The owner confirms this rule at D4.
- **Elevation identity:** if the Windows prompt is answered with a different administrator account, the helper's elevated user differs from the requesting user; refuse, as the update helper already does for its peer checks.

## 8. Review, job and result check

**Install payload (closed ADR-025 variant).** Operation `Install`; route `WinGetUserInstallV1` or `WinGetMachineInstallV1`; source `winget`; exact ID and version; installer type, architecture, scope and locale from the applicable installer; mode Silent; predicate `AbsenceV1` with evidence ID and time; agreements "none" (version 1); dependency set "none" plus the manifest digest and installer SHA-256 from §6; requesting user SID; job and correlation IDs; approval time. No path, URL, argument or free text.

**Result check (PC Steward's own, never the exit code alone):**

| Route | Verified installed only when |
|---|---|
| User MSIX | Exactly one current-user MSIX package in a reviewed family, at the reviewed version and architecture, and no other new MSIX package |
| User portable | Exactly one HKCU entry with `WinGetPackageIdentifier` = ID and the reviewed version, files under the user's WinGet packages folder, nothing new in HKLM |
| Machine | Exactly one new HKLM entry linked to the ID (product code or WinGet correlation) at the reviewed version and architecture, and the requesting user's HKCU unchanged |

Anything else (more than one new entry, wrong version or architecture, a changed existing entry) is **Installed with unexpected changes** (Unsafe) or **Unknown**, never verified and never in Recent installs. Restart needed is its own outcome until a later check proves the installed state. Recent installs (newest 50, Q5) derive only from verified results with this job's invocation evidence; otherwise **Installed; PC Steward attribution unknown**.

## 9. Search screen sketch (D2, ships before install)

A new **Search** page in the navigation, between Updates and Installed apps. It follows Q1 exactly.

```
┌ Search ─────────────────────────────────────────────────────────────┐
│ [ Search the WinGet catalogue…                     ] [Search] [Stop] │
│ Sources: ● WinGet community   ○ Microsoft Store (information only)   │
│                                                                      │
│ 37 results from WinGet community · searched 12:04                    │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ ☆  7-Zip                                   24.09   WinGet         │ │
│ │    Igor Pavlov (catalogue publisher) · ID 7zip.7zip               │ │
│ │    Tags: compression, archive, zip                                │ │
│ │    Already on this PC: see Updates            [Details]           │ │
│ ├──────────────────────────────────────────────────────────────────┤ │
│ │ ★  Example Tool                            3.2.1   WinGet         │ │
│ │    Publisher unavailable · ID Example.Tool                        │ │
│ │    No tags                                                        │ │
│ │    [Install: not available yet]               [Details]           │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│ More than 100 matches. Showing the first 100; try a longer search.   │
└──────────────────────────────────────────────────────────────────────┘
```

- **Search runs only on the button or Enter.** No search as you type, no background refresh, no retries. 1–200 characters; empty does nothing. One search at a time. Stop answers within 2 seconds.
- **Status line is honest:** how many results, which sources answered, "partial: Microsoft Store did not answer in time", "More than 100 matches", time of search. Results are not stored as authority and have no lifetime.
- **Rows** show name, catalogue publisher (labelled as the catalogue's, not verified), exact ID, version, source, tags (up to 20, labelled as tags). Missing values say "unavailable". No icons, no categories, no sizes. Long descriptions are cut with a visible "(shortened)".
- **"Already on this PC"** uses the last scan only, as a hint. It is display, never authority; install review always re-runs `AbsenceV1`.
- **Install button:** in D2 it reads "Install: not available yet" and is disabled for every row. Store rows always read "Microsoft Store: information only". From D3 it becomes "Review install…" for rows the user route can serve, with a short reason on the others ("Needs administrator install: coming later", "Asks for an agreement", "Has dependencies", "Possible match on this PC").
- **Details** shows description, homepage, licence and licence URL as plain text (not clickable in version 1), agreements if any, installer types and scopes from `GetApplicableInstaller`.
- **Favourites:** the star saves the source and exact ID locally (at most 500; adding a 501st is refused with a message, never evicting one). A Favourites filter lists them; opening one runs a fresh exact-ID search. Favourites survive scan, cache and log clears; Factory reset removes them (Q5).

## 10. Where D0's outcome matters

| Topic | If D0 keeps COM 1.10 | If D0 moves to 1.11+ |
|---|---|---|
| Install mechanism (§3) | Command line | Command line. 1.11's interface is unchanged and 1.12 adds no no-upgrade or dependency data |
| Evidence calls used here (`GetApplicableInstaller`, `GetCatalogPackageMetadata`, `PackageFamilyNames`, `ProductCodes`) | Available (contract 6) | Available |
| Bounded search (Q1: the source-side query itself must be bounded) | D0 must show `ResultLimit` bounds the native enumeration in 1.10 | Same proof for the new version |
| Same-engine rule (F7: evidence engine byte-identical to the CLI's) | Holds only while App Installer ships that engine | D0 picks the version that matches current App Installer best |
| Minimum Windows build | 22000 | Whatever D0 records |

If D0 finds a COM version with a real no-upgrade option, §3.2 is worth revisiting.

## 11. Tests the build stages need

- **D2:** Q1 boundaries and one-over for query length, 100 candidates, 1 MiB and 8 KiB, 15 seconds; Stop and late results; hostile, duplicate, fuzzy, oversize and non-BMP metadata; disabled and revoked sources; Store information only; Install disabled for every row; favourites capacity, clear and reset.
- **D3:** every §4.4 fixture; argument-list goldens for each type and architecture with every forbidden option rejected even after a recomputed digest; settings-export refusals; agreements present at review, appearing after review; dependency block present, MSIX framework missing; exit-code mapping; result-check mismatches; restart outcome; crash after dispatch claim (Uncertain, never retried); Recent installs derivation.
- **D4:** helper refuses each mismatch independently of the desktop; requesting-user hive versus elevated HKCU; second user profile blocks; different administrator account refuses; one item per helper request; no user-to-machine fallback.

## 12. Owner questions and answers (2026-11-12)

| # | Question | Recommendation | Owner answer |
|---|---|---|---|
| Q1 | How should installs run? | Through the `winget` command line with `--no-upgrade`; COM only for evidence (§3) | **Accepted** |
| Q2 | Which apps can the first, just-for-me route install? | MSIX and portable packages, with a portable update route built in the same stage (§7.1) | **Widened:** "I'd want it to be able to update anything winget & chocolatey can." Recorded as a coverage goal; see below |
| Q3 | What about packages that ask you to accept an agreement? | Not offered in version 1; show-and-accept later (§5, option A) | **Accepted for now.** The owner asked about a single blanket disclaimer instead; see below |
| Q4 | What about packages that need other packages first? | Not offered in version 1, checked from the exact manifest via `winget download` (§6, option A) | **Accepted** |

**Q2: coverage goal.** The MSIX and portable limit applies only to the first user route (D3), which stays as designed. Reaching "anything WinGet and Chocolatey can" takes these further pieces, each with its own review:

1. **Machine-scope installs (D4)**, as in §7.2. This covers most EXE and MSI packages in WinGet.
2. **Machine-scope WinGet updates.** Today the only WinGet update route is user MSIX (`WinGetUserExactV3`). A machine update route uses the same helper and CLI machinery as D4 (`upgrade` in place of `install`), so it should be planned alongside D4. This goes beyond ADR-023 and Phase D, so it needs its own stage in the plan.
3. **Vendor user-scope installers** (EXE/MSI/Inno/Nullsoft/WiX/Burn that declare user scope): a later user route, for installs and updates. PC Steward cannot enforce what these write or stop them asking for elevation, so the review must say so, and the result check (§8) must catch any write outside the user's profile and HKCU.
4. **Chocolatey installs** are excluded by ADR-023 and the Phase D brief. Allowing them needs an ADR-023 amendment and the owner's explicit decision. Chocolatey package updates already have reviewed routes.

**Q3: a blanket disclaimer.** One up-front statement ("installing through PC Steward means you accept each package's licence") would mean always passing `--accept-package-agreements`, accepting terms the owner never saw. ADR-023 forbids that: acceptance must be bound to the exact agreement and displayed terms, and "automatic acceptance options are absent". As a general observation (not legal advice), click-through acceptance is strongest when the terms are shown for the specific item and accepted with a specific action, and weakest as advance acceptance of unseen terms. The simple version that stays within ADR-023 is §5 option B with one click: when a package declares agreements, the review sheet shows them with their links, and the button reads **Accept and install**. That one press is the explicit acceptance, bound by digest. This is the planned follow-up to version 1.

Later, at D4: the single-profile rule and the helper's MSIX gap (§7.2). At D6: the test packages for each route.
