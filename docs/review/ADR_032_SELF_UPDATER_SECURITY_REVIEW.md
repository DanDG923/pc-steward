# ADR-032 revision 2 — Security review of the in-app self-updater (2026-11-11)

Requirements: `FR-UPD-04/15`, `TM-03/04/12/13/19/20/23/24/26/27`; [ADR-018](../adr/ADR-018-HELPER-IPC-AND-ELEVATION.md), [ADR-032](../adr/ADR-032-IN-APP-SELF-UPDATE-FROM-GITHUB-RELEASES.md), [ADR-033](../adr/ADR-033-NATIVE-AOT-ELEVATED-HELPER.md), [ADR-034](../adr/ADR-034-SIGNED-PROTECTED-MSI-WITH-PINNED-RELEASE-KEY.md), [design](SELF_UPDATE_HELPER_OPERATION_DESIGN.md), [ADR-034 review](ADR_034_SIGNED_INSTALLER_SECURITY_REVIEW.md).

ADR-032's build order asks for a security review of the whole updater once it is built, with fixes made before the 0.1.1 install or any update on the owner's PC. This is that review. It was done on the helper operation branch (parts 1–3 of build step 4, on top of #55), and its fixes are part of the same pull request.

## 1. Scope and method

Read in full:

- **Publishing:** `.github/workflows/release-payload.yml` (the check artefact), `.github/workflows/publish-release.yml`, `scripts/New-PCStewardReleaseCheck.ps1`.
- **Desktop check and download:** `PCSteward.SelfUpdate` (`GitHubReleaseClient`, `GitHubReleaseEvidence`, `ReleaseVersion`, `SelfUpdateText`), and in `PCSteward.Execution`: `ReleaseCheckPolicy`, `ReleaseRunEvidence`, `MsiTableDigest`, `ReleaseImageComparison`.
- **Desktop launch and result:** `PcStewardSelfUpdate`, `SelfUpdateDesktop`, `SelfUpdateDesktopFiles`, `SelfUpdateDesktopTransport`, `MainWindow.SelfUpdate.cs`.
- **Elevated session:** `SelfUpdateHelperBootstrap`, `SelfUpdateSession`, `SelfUpdateInstallPolicy`, `MsiStructurePolicy`, `HelperGitHubConfirmation`, `SelfUpdateHelperProtocol`, `SelfUpdateAclPolicy`, `WindowsSelfUpdateHost`, and the route-less release check in `OwnerOnlyDeploymentCapabilityReader` and `HelperDeploymentCapabilityCheck`.
- **Finish copy:** `SelfUpdateFinish` and its Windows host.
- **Tests:** the fixture and architecture tests named in the design, and section 6 of the Windows MSI CI job (the real finish copy on a real install).

Threat model: the product's own, as in the ADR-034 review. The attacker is any program running as the owner's account without elevation. It can write the owner's profile, `HKCU` (including `HKCU\Environment`, which Windows copies into the environment of a process the owner elevates with `runas`), and the owner's temp folder. It can inject into the unelevated desktop, so it controls everything the desktop sends, including the request. It cannot approve a UAC prompt by itself, and it cannot write `%ProgramFiles%`, `HKLM` or the machine certificate stores. Administrator changes and a compromised GitHub account or YubiKey are outside the model. A release key misuse inside a tampered signing session is inside it: that is the case ADR-034 H1 and the CI anchor exist for.

## 2. Findings

| ID | Severity | Finding | Status |
|---|---|---|---|
| M1 | Medium | A refused input copy stayed in the work folder, which every user can read, and a hard-linked source was not refused | **Fixed** here |
| M2 | Medium | `msiexec` and the finish copy inherited the elevated helper's environment, which the owner's `HKCU\Environment` feeds | **Fixed** here |
| L1 | Low | The helper accepted a release that is not newer than the installed one, relying on Windows Installer's downgrade rule | **Fixed** here |
| L2 | Low | The finish copy of an old job could be started again (with a UAC approval) and would run `msiexec` again | **Fixed** here |
| I1 | Info | The helper's GitHub check fails closed when revocation cannot be checked online | No change: availability only |
| I2 | Info | The desktop's job file and its download folder are writable by any same-user program | No change: they only steer what the owner sees, never what is installed |
| I3 | Info | A same-user program can make the finish copy wait (keep a PC Steward process open) or fail (start a second install) | No change: denial of service only; every outcome is recorded and nothing unchecked is installed |
| I4 | Info | `publish-release.yml` holds `contents: write` | No change: hand-started, inputs passed through the environment and checked before use, no third-party action |

### M1 — refused copies stayed readable, and hard links were not refused (Medium)

**What.** Step 5 of the session copies the two downloads from the owner's download folder into the new work folder, opening the source as the elevated helper. The open already refuses a reparse point, a second data stream and any path whose final name differs from the one asked for, so a junction or symbolic link anywhere on the path is never followed. Two gaps remained:

- A hard link is not a reparse point and has the asked-for final path, so a link in the download folder to a file elsewhere was opened and copied.
- When the copy's hash differed from the approved one, the copy was left in the work folder. The work folder grants read access to Authenticated Users, because the desktop must read `result.json` and `expected-install.json` without elevation.

**Impact.** A same-user program that can create a hard link to a file the owner cannot read, but administrators can, could have the helper copy that file into a folder every user can read. Current Windows builds limit hard links to files the caller can write, which makes this hard to reach, but the helper should not depend on that.

**Fixed here.**

- `OpenPlainFile` (every file the host opens) also requires exactly one name (`FileStandardInfo.NumberOfLinks == 1`) and no pending delete.
- `CopyInput` deletes its copy whenever the verdict is not `Copied`.

### M2 — msiexec and the finish copy inherited a user-controlled environment (Medium)

**What.** Windows builds the environment of a process started with `runas` from the owner's environment, including `HKCU\Environment`, which any same-user program can write. The helper itself is a Native AOT image and refuses .NET runtime control names (ADR-033 and the H1 guard). But `msiexec` was started with the inherited environment, and the finish copy was started by `CreateProcess` with no environment block, so it inherited the same one. That passed `TEMP`, `TMP`, `PATH`, `windir`, `SystemRoot` and the rest to `msiexec` running as administrator. User-set `windir` and `SystemRoot` values have been used for elevation attacks against other elevated programs before.

**Impact.** No direct path to code execution was found: `msiexec` is started from a fixed System32 path, and the install itself runs in the Windows Installer service. But the elevated `msiexec` client, which performs the administrative extract, could use the user's `TEMP` for its temporary files. A same-user program could then race those files. The file-by-file comparison with the CI build would refuse a changed file, so the harm found was limited to denial of service. Even so, the helper should pass none of that environment on, as the Chocolatey runner already does (`ChocolateyCompiledEnvironment`).

**Fixed here.** `WindowsSelfUpdateHost.CompiledEnvironment` builds the whole environment from the system directory and the machine's known folders. It never reads an environment value. The temporary folder is the protected work folder.

- `RunMsiexecAsync` now takes the work folder, refuses one that is not a protected work folder, clears the environment and applies the compiled one.
- `StartFinish` passes the same environment as an explicit block.
- Section 6 of the Windows MSI CI job runs the administrative extract, the install and the uninstall through this host, so a variable Windows Installer needs and the compiled set lacks would fail CI.

### L1 — no newer-version rule in the helper (Low)

**What.** The helper checked that the record, the request and the MSI agree on the version, but not that the version is newer than the installed one. A same-user program controlling the desktop can ask for any genuine, signed release. Windows Installer refuses an older version (the MSI's `MajorUpgrade` downgrade rule, proven in CI). A same-version request was left to the package rules.

**Fixed here.** Right after the installed-release check, the session reads the installed release manifest and refuses with the new code `version-not-newer` unless the requested version is greater (`SelfUpdateInstallPolicy.IsNewerVersion`, which ignores the installed version's suffix, such as `-private-beta`).

### L2 — an old job's finish copy could run again (Low)

**What.** The work folder of the newest earlier job is kept until the desktop has read its result. Its `bin` still holds a helper copy. Started again elevated with `--finish-self-update <leaf>`, which takes a UAC approval from the owner, it would wait for PC Steward to close and run `msiexec /i` on that job's MSI again. Windows Installer would refuse it as a downgrade or same-version install, and the new `result.json` could not be written. So no harm was found, but a finish copy has no reason to run twice.

**Fixed here.** The finish copy refuses (exit 20, nothing run, nothing written) when its job already has a `result.json`.

### I1–I4

- **I1.** The helper's TLS check to `api.github.com` uses only the machine root stores and online revocation for the whole chain. A network that blocks the revocation servers makes the update refuse with "could not be confirmed with GitHub". That is the intended fail-closed behaviour.
- **I2.** `%LocalAppData%\PCSteward\downloads\SelfUpdate` and its `job.json` belong to the owner's account. A same-user program can change what the About card and Home banner say, or delete a download. It cannot change what the helper installs: the helper re-derives everything from the request's hashes, the record and GitHub.
- **I3.** The finish copy installs only once no process runs an image from the install root. A same-user program can therefore delay it until it records `pc-steward-did-not-close`. Starting a second install at the same time makes Windows Installer refuse one of them (`install-failed`). Both outcomes are recorded and shown, and nothing unchecked is installed.
- **I4.** `publish-release.yml` needs `contents: write` to create the draft. It runs only by hand, with `actions: read` as its only other permission. It reads the run id from the environment and requires digits only. It checks the run, the artefact digest and the record before creating anything. It uses only `gh` and `jq` on the hosted runner. Publishing remains the owner's step in the browser, after attaching the YubiKey-signed MSI.

## 3. What holds

These properties were checked and need no change:

- **No path from the desktop.** The request is one canonical JSON object of job values and the launch nonce. Both inputs are found by hash under the requesting user's profile, which is read from the administrator-only ProfileList key.
- **Trust comes after the copy.** Everything is checked on the copies in the new administrator-only work folder, in this order: the compiled pins and the record, GitHub's own answer (from the helper, not the desktop), the MSI's pin, structure and table digest, and the unpacked image against the CI build file by file. Only then are the job files written and the finish copy started.
- **Install and verify.** The finish copy runs from the work folder's `bin`, never from the install root it replaces. It re-hashes the MSI before installing it. After the install it checks the installed tree, its DACLs, both executables' pin and the product registration, and uninstalls the new product on any difference.
- **The launch is the reviewed one.** The desktop side reuses the protocol-2 launch checks: fixed paths, launch trust before and after the prompt, the runtime-environment guard, a restricted per-launch pipe, and peer checks by PID, path, session, SID, signer and the held helper image. The binding set is released only after the helper's exit has been observed.
- **No account.** All GitHub requests are anonymous, to compiled addresses. The desktop follows redirects only to GitHub hosts. The helper follows none.

## 4. Evidence

- **Fixture tests:** `SelfUpdateHelperSessionTests` now covers the two `version-not-newer` refusals and the finish copy's refusal of a job with a result, and `SelfUpdateInstallPolicyTests` covers `IsNewerVersion`.
- **Windows CI (section 6 of the MSI job):** the real finish copy, through the production host and its compiled environment, uninstalls a mismatching install and installs and verifies a matching one. The installed helper refuses to act as a finish copy. The desktop's read reports Failed and Updated as a standard user.

Some of the fixes are Windows-only paths in `WindowsSelfUpdateHost` that CI reaches only partly:

- The hard-link refusal runs on every file the host opens in section 6, but only on the accepting side: no test offers a hard link.
- No test offers a refused copy, so the deletion of a refused copy is not run either.
- Section 6 starts the finish copy from the script, so `StartFinish`'s environment block first runs in the owner's first real update.

A Windows host test for the refused branches is a possible follow-up.
