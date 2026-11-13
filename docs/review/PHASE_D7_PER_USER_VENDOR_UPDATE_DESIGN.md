# Phase D stage D7 (updates): reviewed per-user updates for vendor installers (design note)

Date: 2026-11-13. Status: **owner answered 2026-11-13 (§7): Q1-B (offer admin-needing installers with a warning), Q2 moot, Q3-A, Q4-A. Building stage 1.** The update half of stage D7 in the Phase D plan, after the owner's 2026-11-12 choice "Updates only, Search waits" and the completed D4b whole-PC route. Base: `phase/4-beta` at `ab951e1`.

Requirements: `FR-UPD-01–15`, `AC-UPD-01–11`, `TM-03/04/13`. Builds on the [install design note](PHASE_D_INSTALL_DESIGN.md) §10 Q2 item 3 ("vendor user-scope installers … the review must say so, and the result check must catch any write outside the user's profile and HKCU"), [Task 47](TASK_47_PHASE_BRIEFS_AND_PROMPTS.md) finding **H3** (unexpected elevation cannot be prevented), and stage C4's per-user route [`WinGetUserSilentV1`](C4_REVIEWED_WINGET_USER_MODE.md).

**Out of scope:** installs (paused), MSIX and Microsoft Store apps (their own stage, running in parallel), Chocolatey, and any action on the owner's PC. Nothing here approves a host step.

## 1. Where things stand

Per-user updates for vendor installers **already run today.** In stage C4 the owner chose "Silent for all per-user apps", so every WinGet app installed for the current user (EXE, MSI, Inno, Nullsoft, WiX, Burn, portable and MSIX alike) is offered on `WinGetUserSilentV1` and run without elevation as:

```
upgrade --id <id> --version <target> --source <winget|msstore> --exact --scope user --architecture <a> --disable-interactivity --silent
```

Discord on the owner's PC is one of these (a per-user EXE installer), and it shows as ready to update.

What C4 did **not** add is the safety work the install design note said this kind of installer needs:

| Gap | Today |
|---|---|
| G1. The installer type is not known at review | The review binds the mode only. The vendor installer WinGet will pick is never read, shown or bound |
| G2. Installers that need administrator rights | WinGet itself raises a Windows permission prompt (`ShellExecute` `runas`) for an installer whose manifest says `ElevationRequired`, and an `ElevatesSelf` installer raises its own. A prompt that appears during a PC Steward update looks like PC Steward asked for it |
| G3. The review says nothing about what PC Steward can't control | The owner isn't told that a vendor installer decides where it writes, or what to do if Windows asks for permission |
| G4. The result check can't see whole-PC changes | The check needs exactly one per-user entry at the target version. If the installer also wrote a new or changed whole-PC entry (it elevated, or installed a shared component), the update can still be recorded as verified |

## 2. What WinGet does on a per-user upgrade (pinned)

All references are to `microsoft/winget-cli` at tag `v1.29.380`, the version the app ships.

| Question | Behaviour | Source |
|---|---|---|
| Installer chosen | With `--scope user` on the command line, only installers that declare user scope are kept, plus the types whose scope WinGet controls itself (portable, MSIX, Store, font). An installer that declares **no** scope is not applicable: the command line never sets "allow unknown scope" (only the COM API does, from `PackageInstallScope.UserOrUnknown`) | `AppInstallerCommonCore/Manifest/ManifestComparator.cpp` lines 444–505; `AppInstallerCommonCore/Manifest/ManifestCommon.cpp` lines 940–947; `Workflows/WorkflowBase.cpp` lines 690–698; `Microsoft.Management.Deployment/PackageManager.cpp` lines 538–544 |
| Installed scope | The installed copy's scope is also required, but an installer that declares no scope is assumed to match | `ManifestComparator.cpp` lines 400–429 |
| `ElevationRequired` while not elevated | WinGet warns and runs the installer with the `runas` verb, so Windows shows a permission prompt | `Workflows/ShellExecuteInstallerHandler.cpp` lines 288–306 and 19–36 |
| `ElevatesSelf` | WinGet warns and runs the installer normally; the installer raises its own prompt | same lines |
| What the review can read | WinGet COM 1.29 `PackageInstallerInfo` exposes `InstallerType`, `Scope` and `ElevationRequirement` (`Unknown`, `ElevationRequired`, `ElevationProhibited`, `ElevatesSelf`) from `GetApplicableInstaller` | `Microsoft.Management.Deployment.winmd` in `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 |

Most manifests leave `ElevationRequirement` out (`Unknown`). For those, PC Steward cannot know in advance whether the installer will ask for permission.

So an installer that doesn't say where it installs is already never run on this route: WinGet itself finds no applicable installer and the update fails before anything runs. The review must read the installer the same way (`PackageInstallScope.User`, not `UserOrUnknown`), or it would review an installer the command line will not pick.

## 3. The plan

1. **Read the installer at review (G1, G2).** For a per-user WinGet update from the `winget` source, the review asks WinGet COM for the applicable installer of the exact target version with user scope (strict, as the command line does) and the reviewed architecture, and reads its type and elevation requirement. No download. An MSIX installer keeps today's path (the MSIX stage owns it). If no installer can be read, the review is refused. The review shows the type in words ("Vendor installer (Inno Setup)"). Confirm reads it again from the fresh evidence and refuses if anything changed, as the mode check does today.
2. **Warn about installers that say they need administrator rights (Q1, owner chose B).** `ElevationRequired` and `ElevatesSelf` are bound at review and offered with a warning: "This app's installer asks for administrator permission. Windows will ask you to allow it while the update runs; PC Steward itself never asks for it." `ElevationProhibited` and `Unknown` get the §3 item 3 text. The whole-PC change check (item 4) applies to both.
3. **Say what PC Steward can't control (G3).** The review for apps whose installer declares no elevation need adds: "This update runs without administrator rights. The app's own installer decides where it writes. If Windows asks for administrator permission while it runs, choose No: this update shouldn't need it."
4. **Check for whole-PC changes afterwards (G4, Q3).** The fresh inventory taken just before launch (it already exists: Confirm re-prepares from it) is the "before". The post-update scan is the "after". If any whole-PC uninstall entry (HKLM, both registry views) is new, removed or at a different version, the result is **Updated, with unexpected whole-PC changes**: never Success, shown as needing attention, and the history names the entries that changed. The per-user check stays as it is.
5. **Bind it into the frozen operation and the command line.** The installer facts go into the operation's evidence identity with a strict format check, as the whole-PC route does with `wgm1` (`wgu1:<type>:<elevation>:<inventory|review>:<id>`), and the command line gains `--installer-type <type>` so WinGet can only run the installer type that was reviewed. The route contract stays `WinGetUserSilentV1`; MSIX and Store selections keep today's evidence (the MSIX stage owns them).

## 4. Security notes

- **No boundary is crossed by the route itself.** It never elevates, so a user-writable download folder or settings file can only affect what the owner's account can already do. This is why it needs none of the whole-PC route's helper checks.
- **The real risk is a permission prompt the owner trusts because PC Steward started the update.** Under the owner's Q1-B, prompts the manifest declares are announced at review rather than refused; for the rest the review text says to choose No; the whole-PC check catches the result if the owner says Yes anyway. A per-user MSI that is elevated this way could also apply a per-user transform (the S1 finding from the self-updater), which is one more reason the text says "choose No".
- **Not caught by the whole-PC check:** services, scheduled tasks, drivers, files in Program Files without an uninstall entry, and changes made by something else on the PC during the same minutes (these would show as a false alarm, which fails safe). Disclosed in the result text.

## 5. Build stages (one PR each, Codex review before merge)

1. **Review-time installer read and refusal** (§3 items 1–2, 5): COM read in a new Provider.WinGet file, `wgu1` evidence in Domain, review and Confirm in `ReviewedUpdateExecutionWorkflow`, refusal text. Tests: each elevation value, a changed installer between review and Confirm, tampered `wgu1`.
2. **Review text and the whole-PC change check** (§3 items 3–4): dialog text, `ReInventoryAsync` comparison for per-user non-MSIX jobs, the new result wording. Tests: new, removed and re-versioned HKLM entries; MSIX jobs unaffected; per-user check unchanged.
3. **Release and host check** (Q4): ships in the next release; the owner updates one per-user vendor app through PC Steward and confirms the result.

### 5.1 Stage 1 build note

- **Domain:** `WinGetUserInstallerType` (msi, wix, exe, inno, nullsoft, burn, portable, zip), `WinGetUserElevation` (unknown, prohibited, required, elevatesself), `WinGetUserInstaller` and the strict `WinGetUserEvidence` identity `wgu1:<type>:<elevation>:<inventory|review>:<guid>`. A `WinGetUserSilentV1` operation whose evidence starts with `wgu1` (any case) must parse exactly and come from the `winget` source, on construction and on restore. `UpdateJobReview.UserInstaller` carries the bound installer.
- **Provider:** `WingetUserInstallerEvidenceSource` reads COM `GetApplicableInstaller` for the exact target version with `PackageInstallScope.User` and only the reviewed architecture, within a 30-second budget, and `WingetUserInstallerRules` decides on plain values: trusted predefined `winget` catalog, one exact package, exactly one catalog version with the target text, and a declared user scope unless WinGet places the app itself (portable, or zip around a portable app). MSIX and Store installers keep the C4 path. Anything unreadable or unexpected refuses the review.
- **Workflow:** composed in `App.xaml.cs`. The review binds the installer; Confirm reads it again and refuses on any difference (including MSIX ↔ vendor) before approval; the frozen operation carries `wgu1`; the command line gains `--installer-type <type>`. A review that bound an installer is refused by a workflow that can't read it again. The Store source is never read.
- **Review sheet:** an installer that declares `ElevationRequired` or `ElevatesSelf` shows "Windows will ask for administrator permission" with the Q1-B warning and the administrator shield; any other vendor installer shows the §3 item 3 text. The installer type appears in words with the app's evidence. Batch reviews count both.
- **Tests:** `WingetUserVendorInstallerTests` (19 cases). Cloud run: Domain, Application and Provider.WinGet compile with the repository analyzers; the WinGet route, arguments, workflow and presentation suites pass except the Windows-only rows; the architecture suite has the same failures as the base (compiled-IL scans). Windows CI is the proof for the Desktop project.

## 6. Coordination

The MSIX stage changes MSIX inventory identity, `WingetCandidateCompatibility.cs`, `WingetPostUpdateRegistryLink.cs` and `InventoryWorkflow.CreateProfiles`. This stage stays out of those and rebases onto that PR if it merges first.

## 7. Owner questions

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | A per-user app whose installer says it needs administrator rights | **A.** Refuse it on this route (nothing starts). **B.** Allow it, with a warning that Windows will ask for permission | Recommended A. **Owner chose B (2026-11-13):** offer them with the warning |
| Q2 | A per-user app whose installer doesn't say where it installs (no declared scope) | **A.** Allow it, relying on the whole-PC change check. **B.** Refuse it | **Moot (found while building):** WinGet's command line already refuses these with `--scope user` (§2), so they never run on this route. Nothing to decide |
| Q3 | How to catch writes outside the owner's account | **A.** Compare whole-PC uninstall entries before and after, and flag any change. **B.** A also watches the Program Files and ProgramData folders. **C.** No extra check | **A.** It reuses scans PC Steward already runs. B is slower and noisy. **Owner accepted A** |
| Q4 | Which real update to use for the host check | **A.** Discord (kept back for this). **B.** Wait for another per-user vendor app to have an update | **A.** It is the per-user vendor installer case it was kept for. **Owner accepted A** |
