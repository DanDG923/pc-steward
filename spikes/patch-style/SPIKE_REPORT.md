# Task 45 — Patch-style spikes

**Status:** Spike complete for everything observable from the cloud environment. The Windows-only facts need one owner-run, read-only probe (§7).
**Date:** 2026-10-08
**Requirement IDs informed:** `FR-CAT-01–07`, `FR-INS-01–06`, `FR-BAT-03–08`, `FR-AUT-02`, `FR-PRV-03`, `AC-V3-01/02/05/06/11`, `TM-19/21/22/23`
**Host boundary:**
- **Environment:** a Linux cloud container (.NET SDK 10.0.112, used outside the repository's 10.0.302 `global.json` pin). It has no Windows, WinGet, Chocolatey or owner data.
- **Blocked by the egress policy:** `patchmypc.com`, `learn.microsoft.com`, `docs.chocolatey.org`, `community.chocolatey.org` and the GitHub REST API.
- **Reachable:** GitHub web pages and `raw.githubusercontent.com`. Microsoft and Chocolatey documentation was therefore read from their public GitHub sources.
- **Nothing changed on any PC:** nothing was installed, upgraded or uninstalled, no agreement was accepted, and no source was changed.

## Results at a glance

| Question | Answer | Evidence | Feeds |
|---|---|---|---|
| Can WinGet give us install/upgrade with progress, exact versions, agreements and an admin indicator **before** approval? | **Yes**, through the COM API already shipped in-process by the desktop | §2.1 winmd dump | Tasks 48, 49, 52 |
| Which installers need the elevated helper? | `ElevationRequirement` = `elevationRequired`, **or `elevatesSelf`**, or machine scope. `elevationProhibited` must never run in the helper. | §2.3 | Task 48 (`FR-BAT-05`) |
| Does a COM call from the elevated helper work? | **Not proven here.** Microsoft documents that the CLI is unsupported as SYSTEM; ADR-025 runs as the owner, not SYSTEM. Owner/VM evidence is needed. | §2.4 | Tasks 48, 49 |
| Can one approved batch be bound so the helper rejects substitution, reordering, appending, truncation, replay and expiry? | **Yes.** Prototype protocol v2 rejects all 11 tamper cases. Worst-case frame 1,132 B (bound 16,384 B). | §4 | Task 49 (`AC-V3-05/06`) |
| Is the planned catalog pattern engine fast enough? | **No** for `NonBacktracking`: about 5.8 ms per pattern (3.5 s for 600). **Yes** for the interpreted engine with a grammar check plus a 50 ms timeout: 300 entries load in 12 ms, and 700 records are checked in about 65 ms. | §5.3 | Task 46 (amends ADR-023 wording) |
| Do anchored patterns plus publisher separate look-alikes? | **Yes, on the illustrative set:** 18/18 true positives, 0 false positives, 14/14 decoys rejected. A name match without publisher is never Exact. | §5.4 | Task 47 |
| Do current allowlists accept real IDs? | **No for `+`.** `Notepad++.Notepad++` and `notepad++.exe` fail the production package-ID pattern. | §5.5 | Task 48 |
| Chocolatey setup through WinGet? | `Chocolatey.Chocolatey` exists (WiX/MSI, machine scope, `UpgradeBehavior: deny`). | §3.3 | Task 50 |

## 1. Patch My PC reference re-check

The vendor site is blocked by the egress policy. These points come from web search results that quote the vendor's product page and release notes, and are **not** a direct read.

| Patch My PC Home Updater behaviour (search-reported) | PC Steward v0.3 position |
|---|---|
| Silent install/update by default, with a per-app option to disable silent install | Silent mode through WinGet/Chocolatey. No interactive mode in v0.3. |
| Scheduler: daily/weekly, run missed updates as soon as possible, can run when not logged in | Stage A/B schedules. Stage B runs **only while the owner is signed in, with no stored password** (ADR-025). This is a deliberate difference. |
| "Don't update these apps" skip list; per-app "disable scanning" | Ignore, Pin, **Skip this version** (`FR-POL-06`) |
| Automatically closes programs before updating; suppresses restarts | Close only with consent, never in automation (`FR-BAT-08`). Restarts are reported, never forced. |
| Bulk uninstall | Optional Phase 10 (`FR-UNI`) |
| Self-update through its own scheduled task | Out of scope (ADR-009, manual) |
| "Over 500" supported apps; installers from its own repository | Personal catalog. Installers only through WinGet/Chocolatey. |

No new requirement arises. The run-when-not-logged-in and auto-close differences are intentional safety choices.

## 2. WinGet: COM versus CLI

### 2.1 COM API surface (from the shipped package)

`Microsoft.Management.Deployment.winmd` from `Microsoft.WindowsPackageManager.ComInterop` 1.10.340 was dumped with `System.Reflection.Metadata`:

- `PackageManager.InstallPackageAsync` / `UpgradePackageAsync` / `UninstallPackageAsync` return `IAsyncOperationWithProgress<…Result, …Progress>`.
- `InstallProgress` has `State` (`Queued`, `Downloading`, `Installing`, `PostInstall`, `Finished`), `BytesDownloaded`, `BytesRequired`, `DownloadProgress` and `InstallationProgress`. This gives structured per-item progress (`FR-BAT-06`), which the CLI does not offer.
- `InstallResult` has `Status`, `RebootRequired`, `InstallerErrorCode` and `ExtendedErrorCode`. `InstallResultStatus` includes `PackageAgreementsNotAccepted`, `NoApplicableInstallers`, `NoApplicableUpgrade`, `BlockedByPolicy`, `DownloadError` and `InstallError`. These map one-to-one onto the history taxonomy (`FR-HIS-03`).
- `InstallOptions.PackageVersionId` pins the exact version (from `CatalogPackage.AvailableVersions` / `GetPackageVersionInfo`). Also available: `PackageInstallScope` (Any/User/System/…), `AllowedArchitectures`, `PackageInstallMode` (Silent), `AcceptPackageAgreements`.
- `PackageVersionInfo.GetCatalogPackageMetadata().Agreements` returns `PackageAgreement` (`Label`, `Text`, `Url`), so agreements are available for the review sheet **before** approval (`FR-INS-03`).
- `PackageVersionInfo.GetApplicableInstaller(InstallOptions)` returns `PackageInstallerInfo` with `InstallerType`, `Scope`, `Architecture` and `ElevationRequirement` (Unknown/ElevationRequired/ElevationProhibited/ElevatesSelf). This is the data source for the review sheet's "needs admin" grouping.
- `PackageVersionInfo.ProductCodes` and `PackageFamilyNames` provide correlation evidence for catalog detection.
- `PackageCatalogReference.SourceAgreements` / `AcceptSourceAgreements` cover source-level agreements (relevant to `msstore`).

### 2.2 Options that must never be set

The application must never set these `InstallOptions` members, or their CLI twins. An architecture test in Task 48 should enforce this:

| COM member | CLI twin |
|---|---|
| `ReplacementInstallerArguments` | `--override` |
| `AdditionalInstallerArguments` | `--custom` |
| `AllowHashMismatch` | `--ignore-security-hash` |
| `Force` | `--force` |
| `AdditionalPackageCatalogArguments` | — |
| `AuthenticationArguments` | — |
| `BypassIsStoreClientBlockedPolicyCheck` | — |
| `PreferredInstallLocation` | `--location` |
| `SkipDependencies` | `--skip-dependencies` |
| `AllowUpgradeToUnknownVersion` | — |

`LogOutputPath` may only be set to an app-owned path derived in code.

### 2.3 Elevation routing (design finding for `FR-BAT-05`)

| `ElevationRequirement` / scope | Route |
|---|---|
| `ElevationRequired`, or machine scope | Elevated helper session |
| **`ElevatesSelf`** (for example `Git.Git`, an Inno installer) | **Elevated helper session.** Running it unelevated would raise its own UAC prompt per item and break "one prompt per batch". |
| `ElevationProhibited` | Unelevated phase only. A machine-scope request for such an installer is not actionable. |
| `Unknown`, user scope | Unelevated phase. If the installer still prompts, the result is recorded, and the next review shows the item as admin. |

WinGet manifests carry the same `ElevationRequirement` enum (schema 1.12, installer `ElevationRequirement` field). For example, `Git.Git` 2.55.0.5 declares `elevatesSelf` with both user and machine installers.

### 2.4 Elevated and SYSTEM context — open

- **Documented:** Microsoft's WinGet troubleshooting guidance states the CLI is not supported in the SYSTEM context, and that `Microsoft.WinGet.Client` can be used there for machine-wide apps (search-reported; `learn.microsoft.com` blocked).
- **Community reports:** COM factory selection differs for administrator callers (winget-cli issue #4944).
- **Repository facts:** the desktop already ships the **in-process** engine (`Microsoft.Management.Deployment.InProc.dll` linked as `Microsoft.Management.Deployment.dll`, plus `WindowsPackageManager.dll`). The ADR-018 helper and the ADR-025 Stage B host both run as the **owner's elevated token, not SYSTEM**, which avoids the documented SYSTEM limitation.
- **Required evidence (Tasks 48/49, owner or VM):** in-process COM `FindPackagesAsync` and a harmless `GetApplicableInstaller` from an elevated process.
  - If that fails, the helper falls back to the CLI with structured arguments: `install|upgrade --id <id> --exact --version <v> --source winget --scope <s> --architecture <a> --silent --disable-interactivity`, plus `--accept-package-agreements` only under the rule in §2.5.

### 2.5 Agreements

- The review sheet shows `Agreements` (label, text, URL) from the default version's metadata. Acceptance records a SHA-256 over the canonical agreement text, which becomes the item's `agreementHash`.
- The helper or runner re-fetches the agreements immediately before execution. It sets `AcceptPackageAgreements` (or `--accept-package-agreements`) **only** if the hash matches. Otherwise it stops with `PackageAgreementsNotAccepted` (`TM-22`).
- **`msstore`:** it has **source** agreements, and accepting them changes WinGet's settings. Catalog defaults should therefore use the `winget` source. `msstore` catalog installs wait until a separate source-agreement review exists.

### 2.6 CLI facts (MicrosoftDocs source and winget-cli `returnCodes.md`)

- **`winget install` options:** `--id`, `--exact`, `--version`, `--source`, `--scope`, `--architecture`, `--silent`, `--accept-package-agreements`, `--accept-source-agreements`, `--disable-interactivity`. The dangerous options are listed in §2.2.
- **Return codes for the history taxonomy:**

| Code | Meaning |
|---|---|
| `0x8A150101` | Package in use |
| `0x8A150103` | File in use |
| `0x8A150109` | Restart required to finish |
| `0x8A15010A` | Restart required for install |
| `0x8A15010B` | Restart initiated |
| `0x8A150041` | Package agreements not accepted |
| `0x8A150046` | Source agreements not accepted |
| `0x8A15010D` / `0x8A150061` | Already installed |
| `0x8A15010E` | Downgrade |
| `0x8A15002B` | No applicable update |
| `0x8A150010` | No applicable installer |
| `0x8A150011` | Installer hash mismatch |

**Recommendation for the ADR-003 amendment in Task 48:**
- **Preflight:** COM is the authority for preflight (exact match, available versions, applicable installer, elevation and agreements), extending today's candidate authority.
- **Execution:** COM `InstallPackageAsync`/`UpgradePackageAsync` with `PackageVersionId` for progress and typed results, in both the desktop (user scope) and the helper (machine scope). Fall back to the CLI only if §2.4 fails.
- **Table output:** it never becomes authority.

## 3. Chocolatey

### 3.1 Documented facts

These come from the `chocolatey/docs` GitHub source, because `docs.chocolatey.org` is blocked.

- **`search`:**
  - Exact-match and output flags: `--exact`, `--limit-output`, `--include-headers` (2.5.0+), `--all-versions`, `--version`, `--by-id-only`.
  - Community-only filters: `--approved-only`, `--not-broken`, `--download-cache-only`.
  - Exit codes: 0 means OK, -1 or 1 means error, and 2 means no results when enhanced exit codes are enabled.
- **`install`:**
  - Safe options: `--version`, `--source`, `--yes`, `--limit-output`, `--no-progress`, `--execution-timeout`, `--require-checksums`.
  - **Never used:** `--install-arguments`, `--override-arguments`, `--params`, `--force`, `--skip-powershell`, `--ignore-dependencies`.
- **Exit codes:**

| Code | Meaning |
|---|---|
| 0 | Success |
| 1641 | Success, reboot initiated |
| 3010 | Success, reboot required |
| 350 | Pending reboot detected, nothing done |
| 1604 | Install suspended |
| -1 / 1 | Error |

  Package-specific codes apply only when `usePackageExitCodes` is on.

### 3.2 Not observed here

- The live Community feed is blocked, so the exact `--limit-output` line format of `search` was not seen. The expectation is `id|version`, as with the existing `list` parser. The §7 probe captures it, with and without headers.

**Recommended preflight:** `search <id> --exact --version <v> --limit-output --source <fixed feed> --approved-only --not-broken`. Exactly one row is required. A version still awaiting moderation is therefore not actionable (fail closed).

**Recommended install:** `install <id> --version <v> --source <fixed feed> --yes --limit-output --no-progress --no-color --execution-timeout 300`. Whether to add `--require-checksums` is decided after the owner probe, because it may reject packages that download over HTTPS without checksums.

**Mapping:**
- 0 leads to verification.
- 3010 and 1641 lead to Restart required, still subject to verification.
- 350 and 1604 are never Success.

### 3.3 Chocolatey setup through WinGet (`FR-PRV-03`)

`Chocolatey.Chocolatey` 2.7.4.0 in `winget-pkgs`:
- WiX/MSI installer, `Scope: machine`, ARP name "Chocolatey (Install Only)";
- `UpgradeBehavior: deny` and `RequireExplicitUpgrade: true`: WinGet will not upgrade it later;
- return code 1603 is mapped to `alreadyInstalled`.

Consequences:
- Setup is a machine-scope WinGet Install inside a batch.
- Later Chocolatey client updates use the Chocolatey `chocolatey` package through the normal machine-scope flow.
- The trusted-client gate (canonical `%ProgramData%\chocolatey\bin\choco.exe`, signer "Chocolatey Software, Inc") must be re-run after setup and after any client update.
- The owner probe confirms the MSI's install root before Task 50 relies on it.

## 4. Helper protocol v2 prototype

- **Code:** `prototype/HelperProtocolV2.cs`.
- **Tests:** `tests/HelperProtocolV2Tests.cs`.
- **Run:** `dotnet test spikes/patch-style/tests` (needs an SDK accepted by `global.json` on the owner PC).

**Design:**
- The desktop computes the approved batch digest over **all** items in order.
- Only the machine-scope subset is framed for the helper. The frames are `BatchHeader` (nonce, issued time, approved time, batch digest, machine-subset digest, count), `BatchItem` × N and `BatchEnd`.
- The helper accepts exactly that frame order, recomputes the subset digest, and enforces the 2-minute request lifetime, the 30-minute batch lifetime and nonce single use.
- Execution cancels only between items.

**Results:**

| Check | Result |
|---|---|
| Mixed batch: user items stay with the desktop; machine items reach the helper intact | Pass |
| Worst-case 50 items with maximum-length fields | 52 frames, largest **1,132 B**, total 57,146 B; each frame is far under the 16 KiB bound |
| Substitute package ID | Rejected: `digest-mismatch` |
| Reorder items | Rejected: `item-out-of-order` |
| Append an item | Rejected: `item-appended` |
| Truncate (drop an item) | Rejected: `item-truncated` |
| User-scope item sent to helper | Rejected: `user-scope-item-sent-to-helper` |
| Unknown field (`arguments`) | Rejected: `item-fields` |
| Duplicate JSON field | Rejected: `duplicate-field` |
| Wrong nonce / wrong batch ID / old protocol / oversize frame | Rejected: `nonce-mismatch` / `item-batch-mismatch` / `protocol-version` / `frame-too-large` |
| Nonce replay | Rejected: `nonce-replay` |
| Expired batch (30 min) / stale request (>2 min) | Rejected: `batch-expired` / `request-expired` |
| Duplicate identity, sequence gap, more than 50 items, Chocolatey user scope, Install with an installed version | Construction rejected |
| Cancel while an item runs | Takes effect after that item; remaining items are reported as not started |

**Note:** the helper verifies the machine-subset digest. The full-batch digest is carried for audit correlation, matching v1, where the desktop is the trust anchor for what the user approved.

## 5. Catalog prototype

### 5.1 Files

| Path | Contents |
|---|---|
| `prototype/CatalogPrototype.cs` | Loader, validator and detector |
| `fixtures/sample-catalog.v1.json` | 20 sample entries. IDs are believed correct but **unverified live**; the §7 probe verifies them. |
| `fixtures/illustrative-installed-records.json` | 32 labelled records: 18 positives (3 by provider identity, 15 by name), 14 decoys. Illustrative, **not owner data**. |

### 5.2 Hostile catalog

All 18 mutations disable the catalog with zero entries returned:
- Integrity and schema: wrong hash, unknown field (`installerUrl`), `silentArguments` field, schema version 2.
- IDs and providers: duplicate ID, malformed ID, Chocolatey user scope, unknown provider, shell metacharacters in the package ID, two default providers, duplicate package.
- Patterns: unanchored pattern, pattern longer than 128 characters, backreference, lookahead.
- Other fields: `http` homepage, `..\` in a process name, document larger than 1 MiB.

### 5.3 Pattern engine benchmark (600 patterns × 700 inputs, 4 cores)

| Engine | Build | First pass | Second pass |
|---|---:|---:|---:|
| Interpreted + 50 ms timeout | 9 ms | 93 ms | 89 ms |
| `NonBacktracking` | **3,483 ms** | 260 ms | 97 ms |
| `NonBacktracking`, case-sensitive | 3,611 ms | 111 ms | 95 ms |
| Compiled + timeout | 24 ms | 1,291 ms | 48 ms |

**Decision input (amends ADR-023's engine wording in Task 46):**
- Use the interpreted engine with a 50 ms per-match timeout.
- Validate a restricted grammar that rejects quantified groups other than `?`, backreferences, and all `(?` constructs (lookarounds, inline options, atomic or named groups, conditionals).
- The timeout is the backstop: a pathological `^(a+)+$` that bypassed validation times out at **49.6 ms** and counts as no match.
- With this, a 300-entry catalog validated in **12 ms**, the detector was built in **11 ms**, and 700 records were checked in **63–70 ms**.
- Version tails must be written as bounded character classes (`[0-9.]{1,32}`), not repeated groups. Two sample patterns needed this change.

### 5.4 Detection (illustrative set)

- **Overall:** TP 18, FP 0, FN 0, TN 14, ambiguous 0.
- **Decoys correctly rejected:**
  - other channels or editions of a catalog app: Firefox ESR, Chrome Beta, VS Code Insiders, Discord PTB, Python 3.13 against a 3.12 entry;
  - components and plugins: Mozilla Maintenance Service, Python Launcher, Steamworks Common Redistributables, Zoom Outlook Plugin;
  - other products with similar names: Git LFS, GitHub Desktop, a "7-Zip ZS" fork, Notepad3;
  - a same-name app from the wrong publisher ("Brave").
- **Not Exact without a publisher:** the one name match without publisher evidence (VLC) is detected but **not** Exact.
- **Inventory export gap:** the JSON export (`FR-DAT-01`) has **no publisher field**, while `installation_instances.publisher` exists in SQLite. In-app detection (Task 47) must read instance publishers. The probe's `detect-db` command reads the database read-only for the same reason.
- **Detection order:** provider identity (WinGet correlation or Chocolatey registration) first. Name and publisher patterns are the fallback. Name-only matches are never Exact.
- **Probe smoke test:** `detect-db` on a fake database built from the fixture gave 3 provider-identity, 14 name+publisher, 1 name-only and 14 none, matching the unit test.

### 5.5 Allowlist gaps (input to Task 48)

- **Package IDs:**
  - The production package-ID pattern `^[A-Za-z0-9][A-Za-z0-9.-]{0,255}$` rejects `Notepad++.Notepad++`.
  - WinGet's own schema allows almost any non-separator character, up to 32 per segment and 8 segments.
  - Proposal: allow `+` and `_` (the prototype uses `^[A-Za-z0-9][A-Za-z0-9+_-]{0,31}(\.[A-Za-z0-9+_-]{1,32}){1,7}$`). Any other character stays non-actionable.
  - This is safe because arguments are never shell-parsed. Task 48 adds tests.
- **Process names:** the pattern needs `+` (`notepad++.exe`).
- **Versions:** WinGet's `PackageVersion` schema allows spaces and most punctuation. The production version pattern stays strict, so unusual versions remain non-actionable (fail closed).

## 6. Not done here, by design

- No Windows execution of WinGet/Chocolatey, COM activation, UAC or named pipes. These remain Tasks 48–50 plus owner evidence.
- No precision measurement on the real 658-app inventory. It needs the owner probe (§7).
- The sample catalog IDs are not live-verified.

## 7. Owner-run, read-only probe

```powershell
pwsh -NoProfile -File .\spikes\patch-style\Invoke-PatchStyleProbe.ps1 -OutputPath .\spikes\patch-style\results
```

It writes to the output folder only. It never installs, accepts agreements or changes sources. It captures:
- WinGet and Chocolatey versions;
- `winget show --exact` for three IDs;
- `choco search --exact --limit-output` (plain, with headers and with all versions) and `choco info` against the fixed feed for three IDs;
- `winget-preflight.json`: per sample entry, the match count, versions, applicable installer for user and machine (type, scope, architecture, elevation) and agreement labels;
- `detect-db.json`: catalog-detection counts from the local database, opened read-only. Other app names are not written.

Review the folder before sharing it. Its results close §3.2 and part of §5.4, and confirm the §2.3 routing data on real packages. COM-from-elevated-process evidence (§2.4) stays in Task 48/49 scope.

## 8. Recommendations by task

| Task | Recommendation |
|---|---|
| 46 | Implement the catalog as prototyped: interpreted engine plus grammar check plus timeout. Amend the ADR-023 wording ("NonBacktracking" → "restricted grammar + timeout"). Widen the process-name pattern to allow `+`. Write version tails as character classes. |
| 47 | Detection order: provider identity, then name+publisher (Exact), then name-only (Review required). Use the instance publisher from SQLite. Consider adding publisher to the JSON export (`FR-DAT-01`) with the existing privacy warning. |
| 48 | COM preflight and execution with `PackageVersionId`. Forbid the §2.2 options. Agreement hash rule (§2.5). `winget` source only for catalog installs. Widen the package-ID pattern to allow `+` and `_`. Chocolatey preflight and install argument sets (§3.2). Map return codes (§2.6, §3.1). Amend ADR-003. |
| 49 | Implement protocol v2 as prototyped. Route `elevatesSelf` items into the helper. Prove in-process COM from the elevated helper, or fall back to the CLI. |
| 50 | `Chocolatey.Chocolatey` setup as a machine-scope WinGet Install. Re-run the trusted-client gate after setup and after `chocolatey` package upgrades. Confirm the MSI root from the owner probe. |
| 52 | The review sheet shows agreements and the admin grouping from `GetApplicableInstaller` and `ElevationRequirement`. Progress comes from `InstallProgress.State`. |

## 9. Evidence and sources

**Commands (Linux, scratch copy without `global.json`):**
- `dotnet build spikes/patch-style/tests -c Release`: 0 warnings, 0 errors.
- `dotnet test spikes/patch-style/tests -c Release`: **50/50 passed**, run twice with identical counts.
- `dotnet build spikes/patch-style/probe -c Release -p:EnableWindowsTargeting=true`: 0 warnings, 0 errors.
- `PatchStyle.Probe detect` and `detect-db` smoke runs: as reported in §5.4. `winget-preflight` was not run, because it needs Windows.

**Sources read:**
- `Microsoft.Management.Deployment.winmd` (`Microsoft.WindowsPackageManager.ComInterop` 1.10.340, from the NuGet cache)
- [winget-cli COM API spec #888](https://github.com/microsoft/winget-cli/blob/master/doc/specs/%23888%20-%20Com%20Api.md)
- [winget-cli return codes](https://raw.githubusercontent.com/microsoft/winget-cli/master/doc/windows/package-manager/winget/returnCodes.md)
- WinGet manifest schema 1.12.0 ([installer](https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/JSON/manifests/v1.12.0/manifest.installer.1.12.0.json), [defaultLocale](https://raw.githubusercontent.com/microsoft/winget-cli/master/schemas/JSON/manifests/v1.12.0/manifest.defaultLocale.1.12.0.json))
- [`winget install` docs source](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/package-manager/winget/install.md)
- `winget-pkgs` manifests for `Git.Git` 2.55.0.5 and `Chocolatey.Chocolatey` 2.7.4.0
- winget-cli issues [#4944](https://github.com/microsoft/winget-cli/issues/4944) and [#3146](https://github.com/microsoft/winget-cli/discussions/3146), search-reported
- Chocolatey docs source: [install](https://github.com/chocolatey/docs/blob/master/src/content/docs/en-us/choco/commands/install.mdx), [search](https://github.com/chocolatey/docs/blob/master/src/content/docs/en-us/choco/commands/search.mdx)
- Patch My PC [Home Updater product page](https://patchmypc.com/product/home-updater/) and [release notes](https://patchmypc.com/release-notes/production-release/home-updater-releases/), search-reported because the site is blocked
