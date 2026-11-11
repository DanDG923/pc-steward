# Self-update helper operation — implementation design

**Implements:** [ADR-032 revision 2](../adr/ADR-032-IN-APP-SELF-UPDATE-FROM-GITHUB-RELEASES.md) sections 6–9 (build order step 4).
**Requirements:** `FR-UPD-04/15`, `TM-03/04/12/13/20/23/24/25/26/27`.
**Status:** part 1 (pure rules and the helper's GitHub client) is in code; parts 2 and 3 below are the plan for the native helper and desktop wiring.

## Parts

| Part | Contents | Where |
|---|---|---|
| 1 | Pure rules: request codec, MSI structure rule, extracted and installed tree checks, `expected-install.json`, and the helper's constrained GitHub client. Fixture tests. | `SelfUpdateInstallPolicy`, `MsiStructurePolicy`, `HelperGitHubConfirmation`, `ReleaseRunEvidence` (all `PCSteward.Execution`) |
| 2 | The elevated session (section 6) and the finish copy (section 7). | `PCSteward.Execution`, helper entry point |
| 3 | Desktop: download, job, launch, result reading and the post-restart check (sections 5 and 8), and the CI end-to-end job. | `PCSteward.SelfUpdate`, `PCSteward.Desktop`, `scripts/Test-PCStewardMsiCI.ps1` |

## Part 2: command lines and dispatch

The entry point keeps its order: the runtime-environment guard first, then dispatch on the exact argument shape.

1. **Finish copy:** exactly two arguments, `--finish-self-update <leaf>`, where `<leaf>` matches `^PC Steward Update [0-9a-f]{32}$`. This is checked before any other mode.
2. **Self-update session:** exactly ten arguments, `--kind pcsteward-self-update --protocol 2 --desktop-pid <n> --pipe <pcsteward-reviewed-[a-f0-9]{32}> --nonce <64 uppercase hex>`, in this order. The probe keeps `--kind qualify-chocolatey-helper`. Dispatch looks at the kind value (index 1) before the probe's own parse, so neither mode can accept the other's line.
3. Everything else is unchanged: protocol 2, then protocol 1.

Both new modes refuse with exit code 20 before connecting or reading anything when the process is not elevated or not on Windows.

## Part 2: the session (ADR-032 §6)

The desktop starts the installed helper through the existing launch path (ADR-018 pre-launch trust, one UAC prompt) with the session line. The helper then:

1. **Connect and verify the peer:** it connects with `TokenImpersonationLevel.Identification` within 30 s. Then it applies the same peer checks as protocol 2: the server PID is the desktop PID, a held peer handle is opened, and through it the helper checks the fixed desktop path, the same session, the same SID and a trusted same signer.
2. **Install check:** the Helper-role check must report the installed release intact (`HelperDeploymentCapabilityCheck.EvaluateInstalledRelease`, with the requester token as protection principal). This is the release reader of ADR-034 §4 without a route: the manifest, its signed capability statement (protocol 2), every binding file's hash and length, the protected tree and the two executables' shared pin must all pass, but no operation is looked up in the statement. An owner-only install answers Absent. *Change from ADR-032 §6:* the self-update is **not** added as a capability route (`Update/PCStewardSelfUpdateV1`). A new route would change the pinned statement bytes, the probe's G1 keys and the owner-only scripts, and the session is already a separate helper mode that no package job or protocol-2 request can reach, so the route would add no protection.
3. **Request:** it reads one frame of at most 16 KiB, which `SelfUpdateInstallPolicy.TryReadRequest` must accept in canonical bytes. The request's nonce must equal the command-line nonce.
4. **Inputs:** the requesting user's profile folder comes from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\<peer SID>\ProfileImagePath`, which only administrators can change. It never comes from the helper's own environment. The inputs are `<profile>\AppData\Local\PCSteward\Downloads\SelfUpdate\<MSI SHA-256>.msi` and `<check-zip SHA-256>.zip`. Each is opened once with `FILE_FLAG_OPEN_REPARSE_POINT`, refused if it is a reparse point, not a regular file, has a second data stream, or is above its size bound (512 MiB MSI, 1 MiB zip). Each is hashed through that handle, copied into the work folder's `private` folder (step 5), and hashed again from the copy.
5. **Work folder:** `%ProgramFiles%\PC Steward Update <job id as 32 hex>` is created new (it must not exist) with this security descriptor at creation: `O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;AU)`. `%ProgramFiles%` is the known folder, never an environment value. Inside it, `private` is created new with `O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)` (SYSTEM and Administrators only): the input copies, the administrative image, every msiexec log and msiexec's temporary folder go there, so no other account can read them (Mythos review S2). Only `job.json`, `expected-install.json`, `result.json` and `bin` stay readable by users. Older `PC Steward Update *` folders are removed through the owned-root deletion rules, except the newest earlier one, which keeps its result until the desktop has read it; this happens only after step 6 has confirmed the request with GitHub (Mythos review S4).
6. **Anchor:** the zip copy goes through `ReleaseCheckPolicy.TryReadZip` and `TryRead`. `SelfUpdateInstallPolicy.RecordMatchesRequest` must pass against `ReleaseSigningPins.Compiled`. `HelperGitHubConfirmation.ConfirmAsync` must answer Confirmed for the zip copy's SHA-256. Any other answer refuses with its reason; a failure to reach GitHub is reported as "Could not confirm the release with GitHub".
7. **MSI:** the copy must carry a compiled pin (`AuthenticodeIdentity`). `MsiTableDigest.Read` must give `msiTablesSha256`, and `MsiStructurePolicy.Check` must return Allowed.
8. **Extract:** it runs `%SystemRoot%\System32\msiexec.exe /a "<private>\<msi>" /qn TARGETDIR="<private>\image" /l*v "<private>\extract.log"` with no shell, working directory System32, and a limit of 10 minutes; the exit code must be 0. The image root must hold only the administrative package copy and folders. Exactly one folder in it holds `PCSteward.ReleaseManifest.json`, and it is the install image. No file may lie outside that folder except the package copy, and no reparse point may appear anywhere.
9. **Content:** it enumerates the install image and hashes each file through a held handle. `SelfUpdateInstallPolicy.CheckExtracted` must return Matches, and the two extracted executables must carry the same compiled pin (the host's `BothCarrySameReleasePin`, through `AuthenticodeIdentity`).
10. **Helper copy:** it reads its own running image through a held handle and checks its hash against the installed release manifest's entry. It writes the image to `<work>\bin\PCSteward.ElevatedHelper.exe` and hashes it again.
11. **Job files:** it writes `job.json` (the request without the nonce, plus the desktop PID and the MSI's ProductCode) and `expected-install.json` (`SelfUpdateInstallPolicy.WriteExpected`) into the work folder. If the session stops from here on before the finish copy has started (a desktop disconnect, a spent budget, a failed start), it writes a `result.json` that refuses the job (`refused-before-install`, `session-stopped`), so the finish copy can never install it later (Mythos review S3).
12. **Start the finish copy:** `CreateProcessW` with the fixed application path and command line `"<path>" --finish-self-update "<leaf>"`, working directory System32, no window, the helper's own (already checked) environment and no handle inheritance.
13. **Answer:** one response frame, either `{"nonce","outcome":"ready","workFolder","toVersion","manifestSha256"}` or `{"nonce","outcome":"refused","reason"}`, where `reason` is one compiled code. Then it exits within the 5 s exit budget.

Budgets: the existing 11-minute execution budget covers steps 3–13 (GitHub 30 s, extract 10 min).

## Part 2: the finish copy (ADR-032 §7)

1. **Refuse unless:**
   - the process is elevated;
   - its own path is exactly `%ProgramFiles%\<leaf>\bin\PCSteward.ElevatedHelper.exe`;
   - the work folder and `bin` carry the protected descriptor of step 5, `private` carries the administrators-only one, all are owned by SYSTEM or Administrators and have no reparse point;
   - `job.json` and `expected-install.json` parse and name the same job.
2. **Wait:** for at most 5 minutes until the desktop PID has exited and no running process has an image under `%ProgramFiles%\PC Steward`. If that does not happen, it records `pc-steward-did-not-close` and installs nothing.
3. **Re-hash** the MSI copy against the job.
4. **Install:** it runs `%SystemRoot%\System32\msiexec.exe /i "<private>\<msi>" /qn /norestart /l*v "<private>\install.log"` with a limit of 30 minutes, its temporary folder `<private>`.
5. **Exit 0:** it enumerates `%ProgramFiles%\PC Steward` through held handles. The install passes only if all of these hold:
   - `CheckInstalled` returns Matches;
   - the install folder and every file carry the ADR-034 protected DACL;
   - the installed executables carry the same compiled pin;
   - the machine registration (never the owner's own `HKCU` one) lists exactly one PC Steward product, the MSI's own ProductCode, at the to-version, and no per-user registration of it has appeared. Only that ProductCode is ever uninstalled.

   On any difference it runs `msiexec /x <ProductCode> /qn /norestart` and records why.
6. **Exit 3010 or 1641:** it records `restart-needed` and the boot time (UTC, from `GetTickCount64`), then skips step 5.
7. **Any other exit:** it records `install-failed` with the code. The MSI has rolled back.
8. **Result:** it writes `result.json` (job id, outcome, exit code, versions, boot time, manifest SHA-256) and exits. Nothing is launched.

## Part 3: desktop (ADR-032 §5 and §8)

- **Download:** the download and its first check run in `PCSteward.SelfUpdate`; file writes go through the desktop's owned-root writer for `Downloads\SelfUpdate`.
- **Job:** the self-update job is persisted with job id, versions, run, commit, both SHA-256 values, approval time and, after "ready", the work folder name.
- **On launch:** for an open job, the desktop reads `result.json` and `expected-install.json`. Both must be in a protected work folder; it checks the descriptor, owner and reparse points read-only. It applies section 8:
  - **Updated** needs the to-version, the release capability check, the manifest SHA-256, and either a verified result or, after a restart (boot time later than recorded), its own read-only `CheckInstalled` and DACL check.
  - **Restart to finish** is shown when the result is restart-needed and no restart has happened yet.
  - **Failed** covers every other outcome: a mismatch, or an install that failed.
  - **Inconclusive** is used only when the work folder or its files are missing or unreadable.

### As built (part 3)

- `PCSteward.Execution.PcStewardSelfUpdate` is the desktop's public façade: the per-user folder, the first MSI check, the one elevated launch and the job record. `PCSteward.SelfUpdate` still only reads GitHub.
- The desktop job file is `%LocalAppData%\PCSteward\downloads\SelfUpdate\job.json` (canonical, read strictly), written only after the helper answers "ready". It sits inside the downloads root so a factory reset removes it; a downloads clean-up during a pending update loses only the result display.
- The launch (`SelfUpdateDesktopTransport`) follows the reviewed protocol-2 transport: fixed paths, launch trust, restricted pipe, the Desktop-role installed-release check with its binding set held until the helper exits, the runtime-environment guard, one `runas`, peer and file-identity checks, then `ReviewedHelperExchangeSession` with budgets 30 s / 12 min / 10 s. The answer is accepted only with this launch's nonce, the job's own work folder and to-version.
- §8 (`SelfUpdateDesktop.Decide`): a missing `result.json` is **In progress** for 45 minutes after approval (the 5-minute close wait, the 30-minute install limit and a 10-minute margin), then Inconclusive. A restart counts once the boot time is more than 120 s later than the recorded one.
- The screen is a "PC Steward updates" card on About (Check for updates, Download and check, Install with a confirmation dialog) and a Home info bar for a recorded update. After "ready" PC Steward closes itself.
- `publish-release.yml` also needs `actions: read` to download the run's artefact; it has no other permission.

## Part 3: CI test of the install step (Windows MSI job, section 6)

The owner chose (2026-11-11) to have CI test the install step on a real install and to leave the full run through the screens to the first real update (0.1.1 to 0.1.2 on the owner's PC). So CI does not replace GitHub and does not run the elevated session. Instead:

- `PCSteward.InstallCheck --self-update-prepare <msi> match|mismatch` (elevated) lays out a job through the production `WindowsSelfUpdateHost` exactly as session steps 5 and 8–11 leave it: a new protected work folder, the MSI copied by hash, its administrative image, the expected tree read from the image, `job.json`, `expected-install.json` and a copy of the installed helper in `bin`. In the `mismatch` case one file's expected hash is changed.
- The script then starts the real helper copy with `--finish-self-update <leaf>`, with the .NET runtime names removed from its environment, as the session's compiled environment guarantees.
- Cases, each from an installed 0.1.1:
  - mismatch: 0.1.2 is installed, found different, and uninstalled (`mismatch-uninstalled`, nothing left installed);
  - the installed helper run as a finish copy outside the work folder: exit 20, nothing run or written;
  - match: 0.1.2 installed and verified (`installed`, one product at 0.1.2).
- `PCSteward.InstallCheck --self-update-decide <leaf> 0.1.1`, run as the disposable standard user, applies the desktop's §8 read through `SelfUpdateDesktopFiles` and must report Failed (`mismatch-uninstalled`) and then Updated (`installed`, running 0.1.2).

## Security review

The [security review](ADR_032_SELF_UPDATER_SECURITY_REVIEW.md) found two Medium and two Low issues, all fixed in this branch:

- refused input copies are deleted and hard-linked inputs refused;
- `msiexec` and the finish copy get a compiled environment;
- the helper accepts only a newer release (`version-not-newer`);
- a finish copy refuses a job that already has a result.

The owner's independent review (preliminary, same document, section 3) led to three more changes:

- installed products come from the machine registration only;
- `msiexec` never runs while the owner's registry registers PC Steward (`per-user-registration`), and, after the helper code review (S1), never unless the `DisableUserInstalls` machine policy is set (`user-installs-allowed`);
- the structure rule is an allow-list.

It also added a branch guard in the release workflow, and recorded two limits for the owner.

## Tests already in part 1

- `SelfUpdateInstallPolicyTests`: the request is canonical and carries no path. The record must be the approved one. The extracted tree matches only the CI build plus its manifest, refusing extra, missing or changed files and executables. The installed tree must be exact. The expected file round-trips and is read strictly. The structure rule allows only an error-message custom action.
- `HelperGitHubConfirmationTests`: two fixed anonymous GETs; redirects, errors, oversized answers and TLS failures are refused; the chain policy trusts only the given roots, with online revocation; the handler uses no proxy, cookies, credentials or redirects (Windows).
- `ReleaseRunEvidenceTests`, `SelfUpdateBoundaryTests` (network code only in the two GitHub clients; the helper client is constrained).
- The Windows MSI job's `release-check-csharp-matches-powershell` check also runs `MsiStructurePolicy` on the real signed MSI.
