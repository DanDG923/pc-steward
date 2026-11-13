# Phase D stage D7 (updates): reviewed per-user updates for vendor installers (design note)

Date: 2026-11-13. Status: **proposed; owner questions Q1–Q4 open (§7).** The update half of stage D7 in the Phase D plan, after the owner's 2026-11-12 choice "Updates only, Search waits" and the completed D4b whole-PC route. Base: `phase/4-beta` at `ab951e1`.

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
| Installer chosen | With `--scope user`, only installers that declare user scope are kept, plus installer types whose scope WinGet controls itself (MSIX, portable). Installers that declare no scope are kept only when WinGet's upgrade flow allows unknown scope | `AppInstallerCommonCore/Manifest/ManifestComparator.cpp` lines 444–505; `Workflows/WorkflowBase.cpp` lines 690–698 |
| Installed scope | The installed copy's scope is also required, but an installer that declares no scope is assumed to match | `ManifestComparator.cpp` lines 400–429 |
| `ElevationRequired` while not elevated | WinGet warns and runs the installer with the `runas` verb, so Windows shows a permission prompt | `Workflows/ShellExecuteInstallerHandler.cpp` lines 288–306 and 19–36 |
| `ElevatesSelf` | WinGet warns and runs the installer normally; the installer raises its own prompt | same lines |
| What the review can read | WinGet COM 1.29 `PackageInstallerInfo` exposes `InstallerType`, `Scope` and `ElevationRequirement` (`Unknown`, `ElevationRequired`, `ElevationProhibited`, `ElevatesSelf`) from `GetApplicableInstaller` | `Microsoft.Management.Deployment.winmd` in `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 |

Most manifests leave `ElevationRequirement` out (`Unknown`). For those, PC Steward cannot know in advance whether the installer will ask for permission.

## 3. The plan

1. **Read the installer at review (G1, G2).** For a per-user, non-MSIX WinGet update, the review asks WinGet COM for the applicable installer of the exact target version with user scope, and reads its type, declared scope and elevation requirement. No download. The review shows the type in words ("Vendor installer (Inno Setup)"). Confirm reads it again from the fresh evidence and refuses if anything changed, as the mode check does today.
2. **Refuse installers that say they need administrator rights (Q1).** `ElevationRequired` and `ElevatesSelf` are refused at review with: "This app's installer asks for administrator permission, so PC Steward can't update it for your account only. Nothing was started." `ElevationProhibited` and `Unknown` go ahead.
3. **Say what PC Steward can't control (G3).** The review for these apps adds: "This update runs without administrator rights. The app's own installer decides where it writes. If Windows asks for administrator permission while it runs, choose No: this update never needs it."
4. **Check for whole-PC changes afterwards (G4, Q3).** The fresh inventory taken just before launch (it already exists: Confirm re-prepares from it) is the "before". The post-update scan is the "after". If any whole-PC uninstall entry (HKLM, both registry views) is new, removed or at a different version, the result is **Updated, with unexpected whole-PC changes**: never Success, shown as needing attention, and the history names the entries that changed. The per-user check stays as it is.
5. **Bind it into the frozen operation.** The installer facts go into the operation's evidence identity with a strict format check, as the whole-PC route does with `wgm1` (`wgu1:<type>:<scope>:<elevation>:<observation id>`), so a restored or tampered operation can't drop them. The route contract stays `WinGetUserSilentV1`; MSIX and Store selections keep today's evidence (the MSIX stage owns them).

## 4. Security notes

- **No boundary is crossed by the route itself.** It never elevates, so a user-writable download folder or settings file can only affect what the owner's account can already do. This is why it needs none of the whole-PC route's helper checks.
- **The real risk is a permission prompt the owner trusts because PC Steward started the update.** Q1 removes the prompts WinGet knows about; the review text covers the rest; the whole-PC check catches the result if the owner says Yes anyway. A per-user MSI that is elevated this way could also apply a per-user transform (the S1 finding from the self-updater), which is one more reason the text says "choose No".
- **Not caught by the whole-PC check:** services, scheduled tasks, drivers, files in Program Files without an uninstall entry, and changes made by something else on the PC during the same minutes (these would show as a false alarm, which fails safe). Disclosed in the result text.

## 5. Build stages (one PR each, Codex review before merge)

1. **Review-time installer read and refusal** (§3 items 1–2, 5): COM read in a new Provider.WinGet file, `wgu1` evidence in Domain, review and Confirm in `ReviewedUpdateExecutionWorkflow`, refusal text. Tests: each elevation value, each declared scope, a changed installer between review and Confirm, tampered `wgu1`.
2. **Review text and the whole-PC change check** (§3 items 3–4): dialog text, `ReInventoryAsync` comparison for per-user non-MSIX jobs, the new result wording. Tests: new, removed and re-versioned HKLM entries; MSIX jobs unaffected; per-user check unchanged.
3. **Release and host check** (Q4): ships in the next release; the owner updates one per-user vendor app through PC Steward and confirms the result.

## 6. Coordination

The MSIX stage changes MSIX inventory identity, `WingetCandidateCompatibility.cs`, `WingetPostUpdateRegistryLink.cs` and `InventoryWorkflow.CreateProfiles`. This stage stays out of those and rebases onto that PR if it merges first.

## 7. Owner questions

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | A per-user app whose installer says it needs administrator rights | **A.** Refuse it on this route (nothing starts). **B.** Allow it, with a warning that Windows will ask for permission | **A.** A prompt PC Steward causes but doesn't control is what the trust goal rules out |
| Q2 | A per-user app whose installer doesn't say where it installs (no declared scope) | **A.** Allow it, relying on the whole-PC change check. **B.** Refuse it | **A.** The installed copy is per-user and the update gets no administrator rights; refusing would drop many apps |
| Q3 | How to catch writes outside the owner's account | **A.** Compare whole-PC uninstall entries before and after, and flag any change. **B.** A also watches the Program Files and ProgramData folders. **C.** No extra check | **A.** It reuses scans PC Steward already runs. B is slower and noisy |
| Q4 | Which real update to use for the host check | **A.** Discord (kept back for this). **B.** Wait for another per-user vendor app to have an update | **A.** It is the per-user vendor installer case it was kept for |
