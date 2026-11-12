# Task 50 cleanup: work folders, worktrees, test certificates and one-off admin folders (revision 1)

Requirements: `TM-03/04/13` (no leftover trust or elevated residue), ADR-022 (owner-only test deployment, now ended by the
signed MSI of ADR-034). This closes the last open item of the [Task 50 handover](TASK_50_HANDOVER_2026-11-06.md) §5,
item 5: "Cleanup of the work folders, old worktrees and execution roots". The owner approved starting it on 2026-11-12.

## 1. What is left, and why it can go

Task 50 is finished on the owner's PC. The probe passed (C2, C3), ripgrep was really updated through PC Steward (G9-T1 to
T4), and the owner-only test deployment was removed when the signed 0.1.0 MSI was installed on 2026-11-11. Releases now
arrive by the signed MSI and the in-app updater (0.1.2 on 2026-11-12), so none of the redeploy tooling's work folders is
used again. An earlier paste (2026-11-11) already removed the key, backup and release work folders and 11 execution roots.

A read-only look on 2026-11-12 (folder names only, from the owner's connected device) found:

| Where | What | Plan |
|---|---|---|
| User folder | `PCSteward-Task50B`, `-Task50C`, `-Task50D`, `-Task50E` (Part B/R2, Part C, re-run and native redeploy: worktrees `febf1ac`, `r2-385efe0`, `ebb4061`, `5606da2`, `d9a754b`, plus `candidate`, `plan`, `toolchain-check` and `dbcopy`, a copy of the owner's database) | remove |
| User folder | `PCSteward-UI5`, `PCSteward-UI5-<time>`, `-UI6-<time>`, `-UI7-<time>` (UI redeploys, same layout) | remove |
| User folder | `PCSteward-Release-<time>` for 0.1.1 and 0.1.2: a worktree beside the signed `release` folder | remove the worktree, keep `release` |
| User folder | `PCSteward-Release-<time>` for 0.1.0: only `release` (tidied on 2026-11-11) | keep |
| Clone | 22 worktree records: 11 for the folders above and 11 stale ones without a `gitdir` file, which Git could not delete on 2026-11-11 | prune, then delete the stale ones |
| Clone | one stash, "pre-sync local Task 50 edits 2026-11-11" (see §2) | save as two patch files beside the clone, then drop |
| Clone | `.claude\settings.local.json` allow rule from CB3 | already gone |
| User folder | `PCSteward-CB3-runner` | already gone |
| Certificates | any `CN=PC Steward Owner-Only Test *` left in CurrentUser My, Root or TrustedPublisher (expected none) | remove; My with its key |
| Program Files, ProgramData | any `PC Steward OwnerOnlyTest Execution <32 hex>`, `PCSteward-YubicoTools-<32 hex>`, `PCSteward-DotNetSdk-<32 hex>` or `PCSteward-G9T1` folder (expected few or none) | remove, with one UAC prompt only if any exist |

**Kept, not touched:** the installed PC Steward and `%LOCALAPPDATA%\PCSteward`; every signed `release` folder;
`%LOCALAPPDATA%\PCSteward-Release` (the publishing token); the YubiKey and the `PC Steward Release Signing` certificates;
Chocolatey, its `cacheLocation` setting (H1) and ripgrep; the clone's branches and files; Downloads.

**Left as accepted residue:** the `ED6FBCE8…` key and TrustedPublisher entry visible only inside the Claude app's private
per-user view ([recovery plan](TASK_50_RECOVERY_PLAN.md) §4); a window the owner opens cannot see them, and nothing trusts
them. **Optional, not in this paste:** G9-T5 (`choco uninstall ripgrep`), which only changes which `rg` is first on PATH.

## 2. The old stash

The owner's clone held one stash, made on 2026-11-11 before the clone was synced to `phase/4-beta`. It was read
object by object from the clone's `.git` folder (read-only). Its base is `44ff0cd`, a local commit of 2026-10-31 on
`phase/5-v2-planning` from before the cloud work. It holds:

- edits to five files recording the owner's 2026-10-31 decision that Chocolatey is acceptable "as long it uses the
  official community repo": ADR-025, `V2_PLANNING_DECISIONS.md`, `V2_TASK_CONTRACT.md`,
  `TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md` and `ChocolateyReviewedUpdateArgumentsTests.cs`
  (the `ChocolateyMachineRuntimeLimitsV3` route and its disclosure test);
- three untracked files: `TASK_50_CLOUD_HANDOVER_OPUS_5_5.md` and `TASK_50_PAUSED_HANDOFF.md` (byte-identical to the
  committed files) and an early draft of `ReviewedProviderGuardContractTests.cs`.

Every one of these is already on `phase/4-beta` in the same or a later form: the ADR-025 file is byte-identical to its
version in `ec5d7dd`, the decision text and the disclosure test are in the committed files, and the route and the
guard-contract label are in the production code. **Nothing in the stash still matters.** The paste saves it anyway, as
`pc-steward-stash-2026-11-11-tracked.patch` and `-untracked.patch` next to the clone, before dropping it.

## 3. The paste

[`TASK_50_CLEANUP_PASTE.ps1`](TASK_50_CLEANUP_PASTE.ps1), run once in a new, normal (not administrator) Windows
PowerShell 5.1 window that the owner opens from the Start menu.

1. **Checks.** 64-bit Windows PowerShell 5.1, not elevated, not inside an app package (`GetCurrentPackageFullName` must
   return 15700, as in the R2 tooling), Git and the clone present.
2. **Plan.** It lists every folder, record, certificate and admin folder it would change, with sizes, and every one it
   will skip. It then waits for the owner to type `YES`; any other answer stops with nothing changed.
3. **C1, user folder.** Removes the matching work folders and tidies the release folders. A folder holding a junction
   or symbolic link is skipped and reported, so a deletion never follows a link. A release folder whose `release`
   folder is not exactly one signed release (`release.json` and one `PCSteward-*.msi`) is left as it is.
4. **C2, clone.** `git worktree prune`, then deletes records under `.git\worktrees` that have no `gitdir` file. The
   stash is saved and dropped only if it is the single stash and its commit is exactly `ac8ed600…afebf`, the patch
   files do not already exist, saving succeeded and the stash did not change meanwhile.
5. **C3, certificates.** Removes `CN=PC Steward Owner-Only Test *` from CurrentUser TrustedPublisher, My (with
   `-DeleteKey`) and Root, in that order. Skipped entirely if `PC Steward OwnerOnlyTest State` exists, which would mean
   an owner-only deployment is still installed.
6. **C4, administrator folders.** Only if any exist: one elevated Windows PowerShell, which re-lists the same name
   patterns itself, skips any folder holding a link, and removes nothing else.
7. **Check (read-only).** One `CHECK` line, then `CLEANUP RESULT: DONE` or `SOMETHING WAS LEFT`.

**Prompts, in order (anything else: No).** Only if a test certificate is still in Root: a *Root Certificate Store*
box to DELETE `PC Steward Owner-Only Test <id>` → Yes, once per certificate. Only if admin folders exist: one UAC
prompt for *Windows PowerShell*, *Microsoft Windows* → Yes.

**Expected on the owner's PC:** `CHECK work=0 releaseExtras=0 keptReleases=3 worktrees=1 staleWorktrees=0 oldStash=0
testCerts=0 adminFolders=0 skipped=0 stateRoot=False installed=True data=True` and `CLEANUP RESULT: DONE`. A stale
record Git or Windows still refuses to delete is reported as harmless.

**Output** is sanitized: the profile path, user name and computer name are replaced with `<PROFILE>`, `<USER>`, `<PC>`.

## 4. Evidence before handover

The gating Windows CI job `task50-cleanup-paste` runs `scripts/Test-Task50CleanupPasteCI.ps1`. It parses the paste under
Windows PowerShell 5.1, checks it is plain ASCII, builds the leftovers and look-alikes in the hosted runner's real
profile, clone location, certificate stores, Program Files and ProgramData, and runs the paste three times: answer `no`
(nothing changes), `YES` (every planned item goes, the junction folder, the incomplete release folder and all
look-alikes stay, the stash is saved with its tracked and untracked content, output carries no profile path or user
name), and `YES` again after the skipped items are gone (`DONE`, no admin step). Three text changes are made to a
temporary copy only: the elevated-window refusal is removed (the hosted runner is always elevated), `Read-Host` is
answered from an environment variable, and the pinned stash commit is replaced by the one made in CI. Not exercised in
CI: removal from the CurrentUser Root store, because Windows asks for confirmation in a box a runner cannot answer.

## 5. Result

Not run yet.
