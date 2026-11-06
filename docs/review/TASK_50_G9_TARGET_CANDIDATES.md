# Task 50 — G9 test-package candidates (step G9-R1, revision 1, draft)

Plan: [G9 target plan](TASK_50_G9_TARGET_PLAN.md) §5. Criteria are §5.1 of that plan, numbered 1–9 below. Profile rules: [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md).

**Nothing here is approved for execution.** This is read-only research done in the cloud on 2026-11-06 by reading public Chocolatey Community package pages and the package source nuspec. No package was downloaded, installed, run or queried with Chocolatey. Naming a candidate here does not approve it: the owner picks one (decision **G9-D2b**), and the install (G9-H1) still needs its own exact approval.

## 1. Recommendation

| Role | Package | Install first (older) | Upgrade target (newer, current latest) |
|---|---|---|---|
| **Primary** | [`ripgrep`](https://community.chocolatey.org/packages/ripgrep) | [14.1.0](https://community.chocolatey.org/packages/ripgrep/14.1.0) (approved 2024-01-30) | [15.2.0](https://community.chocolatey.org/packages/ripgrep/15.2.0) (approved 2026-08-03) |
| Alternate | [`fd`](https://community.chocolatey.org/packages/fd) | [10.5.0](https://community.chocolatey.org/packages/fd/10.5.0) (approved 2026-08-27) | 10.5.1 (approved 2026-11-05) |

**Why ripgrep.** It is a single command-line tool (`rg.exe`) packed as an **embedded** zip inside the nupkg, extracted with Chocolatey's own `Get-ChocolateyUnzip`. That uses the held release `7z` tools and the `helpers` module (P1, P2) with no third-party download, so `--require-checksums` has nothing to check and cannot fail it. It has no dependencies (confirmed in the source nuspec), is a "trusted package" with 1.1 million downloads, and changes slowly (the last release was three months ago), so the newer version is unlikely to move on between review and helper check.

**Why fd as the alternate.** Same shape: embedded zips, `Get-ChocolateyUnzip`, no other script actions. It is weaker in two ways: no dependency list was visible on the pages read (to be confirmed from its nuspec before it is used), and it releases often (10.5.1 appeared on 2026-11-05), which raises the chance that the candidate moves on mid-run and the helper refuses.

## 2. Candidates against the criteria

✓ = met from what was read; ? = owner or later check needed; ✗ = not met.

| # | Criterion | ripgrep 14.1.0 → 15.2.0 | fd 10.5.0 → 10.5.1 | hyperfine (rejected) |
|---|---|---|---|---|
| 1 | Community, machine, stable, not `chocolatey`/`chocolatey.extension` | ✓ | ✓ | ✓ |
| 2 | Two moderated versions, newer one is strictly newer and the latest | ✓ both "Approved"; three-part versions, no date suffix | ✓ both "Approved" | ? only 1.21.0 visible |
| 3 | Script extracts an archive with Chocolatey helpers | ✓ `Get-ChocolateyUnzip` with `FileFullPath`/`FileFullPath64` | ✓ `Get-ChocolateyUnzip` | ? script not visible; page shows a `checksum64`, which suggests a download |
| 4 | Checksums; embedded preferred | ✓ embedded, no download | ✓ embedded, no download | ✗ downloads from GitHub; its verification text pairs an x86_64 label with an `i686` file name |
| 5 | No dependencies | ✓ page: "This package has no dependencies"; nuspec has no `<dependencies>` element | ? not shown on pages read | ? not shown |
| 6 | Portable and quiet (no MSI/EXE installer, service, task, driver, shell change, reboot) | ✓ script only unzips; Chocolatey auto-shims `rg.exe` into `bin` | ✓ same; auto-shim `fd.exe` | – |
| 7 | Not relied on by the owner | **? owner check** (see §3) | **? owner check** | – |
| 8 | Removable with `choco uninstall` | ✓ files only under `lib\ripgrep` plus the `bin` shim | ✓ same | – |
| 9 | No `.pin`, no remembered arguments needed, nothing in `lib-bkp` | ✓ none needed (checked at G9-H2) | ✓ same | – |

### 2.1 ripgrep details

- Maintainer `flcdrg` (package), author BurntSushi; MIT licence. Package source: `flcdrg/au-packages`, folder `ripgrep` (automatic updater).
- 15.2.0 install script, in full: set `$ErrorActionPreference = 'Stop'`, set `$toolsDir`, then `Get-ChocolateyUnzip` with `Destination = $toolsDir`, `FileFullPath` = the i686 zip and `FileFullPath64` = the x86_64 zip. Nothing else.
- 14.1.0 install script: the same, plus two cleanup lines that stay inside the package's own folder: it removes old subfolders of `$toolsDir` before extracting and deletes the `*.zip` files after. Only this script runs at G9-H1 (the owner's install). The G9-H4 upgrade runs the **15.2.0** script.
- No `chocolateyBeforeModify.ps1` or `chocolateyUninstall.ps1` was listed for either version, so the upgrade runs no old-version script.
- Embedded zip SHA256 values, as shown on the package pages:
  - 14.1.0 x86_64: `FE4F75EDFAA50F0D4FECBF47696B7629F3449C9C2C5A4DA828753139E5A2E203`
  - 14.1.0 i686: `3B2E4776F9510D2F502FEA92F1BE45C02FF0F46DA50F034B04FE866182FFC0D6`
  - 15.2.0 x86_64: `71B2FEF860ABE467217A538FF31DE02F5258807C0129F771846F87BD029AAFC5`
  - 15.2.0 i686: `9BF73BDB3FDA9AD4B0235E1295B02C717031C986AFA4D7C05DD0AF8B74010A95`
- Nuspec (current master): no `<dependencies>`, `requireLicenseAcceptance` false, files `tools\**` only.
- Versions between the two: none on the Community feed (the list goes 14.1.0 → 15.2.0), so `outdated` should offer exactly 15.2.0. Avoid the older date-suffixed versions such as `13.0.0.20220913`: their four-part form adds store-location alternatives (Profile V2) for no benefit.

### 2.2 fd details

- Maintainer `joachim.schmidt557`, author sharkdp; the package ships an Apache-2.0 licence file.
- 10.5.0 install script: set `$ErrorActionPreference`, resolve the two embedded zips with `Get-Item`, then `Get-ChocolateyUnzip` with `destination`, `filefullpath`, `filefullpath64`. No other actions.
- 10.5.0 SHA256: nupkg `5291e329b80d5d49955b0a447ac9c36ec02631cf54ca3acb4d86f666e3e1e567`; x86_64 zip `A227701B8551C35A9931D9F6DA75503CF86D88E182D71FB849A70864C5D57CD7`.
- Release cadence: 10.4.1 (2026-03-09), 10.4.2 (2026-03-11), 10.5.0 (2026-08-27), 10.5.1 (2026-11-05).

## 3. What the owner needs to check (criterion 7)

Before choosing, Dan should confirm that **`rg` (or `fd`) is not already on this PC by another route and not something he relies on**:

- not installed with WinGet, Scoop, Cargo or by hand (`where.exe rg` in a normal window shows nothing, or only copies bundled inside other apps' own folders);
- copies bundled inside other apps (for example inside VS Code or AI coding tools) are fine: they live in those apps' own folders and are not touched. The Chocolatey shim would, however, put an `rg` on `PATH` for the first time, so a tool that falls back to `PATH` could start using it. That is harmless for a search tool, but it is a reason to uninstall it at G9-H5 if Dan does not want it to stay.

## 4. Things this research could not confirm

- **Antivirus scan note.** Both ripgrep versions and fd 10.5.0 carry the Community "Note" scan flag: "at least one file within this package has greater than 0 detections, but less than 5". Exact counts are behind a login. Low single-digit hits are common for unsigned Rust binaries, but this was not verified. Dan may want to look at the VirusTotal links on the package pages before G9-D2b, and Microsoft Defender may scan the extracted `rg.exe` during G9-H1 and G9-H4.
- **Package size.** Not shown on the pages. Each ripgrep version embeds two zips of roughly 2 MB each (estimate from upstream release sizes, not measured).
- **Shim.** Chocolatey's automatic shimming creates `bin\rg.exe` with `shimgen.exe`. This also exercises the held `shimgen.exe` release tool during G9-H4, which is useful evidence, and it changes `bin`, which G9-H2 already re-checks.
- **Moderation can change.** Approval status and "latest" were read on 2026-11-06. G9-H1's exact block should re-read both pages first and stop if either version is no longer approved or a version newer than 15.2.0 has appeared.
- **fd dependencies.** Not visible on the pages read; read the nuspec before using fd.

## 5. Next step

Owner decision **G9-D2b**: primary (ripgrep 14.1.0 → 15.2.0), alternate (fd 10.5.0 → 10.5.1), or neither. After that, the exact G9-H1 install block is written, listing every prompt, and goes to Dan for its own approval.

## Sources

- https://community.chocolatey.org/packages/ripgrep
- https://community.chocolatey.org/packages/ripgrep/14.1.0
- https://community.chocolatey.org/packages/ripgrep/15.2.0
- https://raw.githubusercontent.com/flcdrg/au-packages/master/ripgrep/ripgrep.nuspec
- https://community.chocolatey.org/packages/fd
- https://community.chocolatey.org/packages/fd/10.5.0
- https://community.chocolatey.org/packages/hyperfine
- https://docs.chocolatey.org/en-us/create/functions/get-chocolateyunzip/
