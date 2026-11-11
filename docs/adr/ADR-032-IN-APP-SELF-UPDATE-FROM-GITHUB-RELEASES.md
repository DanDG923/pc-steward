# ADR-032 — In-app self-update from GitHub Releases

**Status:** Proposed (revision 2, 2026-11-11). Revision 1 (2026-11-06, draft PR #20) was written for the per-user standalone ZIP and was never accepted. The owner then chose "installer first, then updater" (2026-11-11), and the signed protected MSI of ADR-034 is now installed on the owner's PC. This revision replaces revision 1 for that install. Nothing here is built or authorised until the owner accepts it.
**Date:** 2026-11-11
**Decision owners:** Product owner and security reviewer
**Related requirements:** spec §14.3–14.4, `FR-ONB-05` (Later), `FR-UPD-04`, `FR-UPD-15`, `FR-DAT-12–15`, `TM-03`, `TM-04`, `TM-09`, `TM-12`, `TM-13`, `TM-18`
**Related open decision:** `OD-09`
**Would supersede, once accepted and its gates pass:** the "no built-in self-update" part of ADR-009.
**Builds on:** ADR-034 (signed MSI, pinned release key, section 7 "How the later updater uses this" and its H1 content rule), ADR-018 and ADR-033 (elevated helper), ADR-017 (immutable jobs), ADR-019 (owned roots), ADR-020 (no resident process).
**Prior art:** [SELF_UPDATER_PRIOR_ART_2026-11-06.md](../review/SELF_UPDATER_PRIOR_ART_2026-11-06.md) (from draft PR #22). Its recommendations are kept where they still apply; section 11 lists the ones this revision drops and why.

## Context

The owner wants PC Steward to update itself to the newest release published on its public GitHub repository, from inside the app, with one owner approval and one UAC prompt.

What changed since revision 1:

- **Install.** PC Steward now lives in `%ProgramFiles%\PC Steward`, installed by a per-machine WiX MSI (ADR-034) that the signed-in user cannot change. Revision 1's per-user `versions\` folders and "no UAC" hand-over no longer apply: replacing a protected install needs an administrator.
- **Release key.** The owner's release key exists: an RSA 3072 key inside the owner's YubiKey (PIN and touch for every signature), plus an offline backup key. Both pins are compiled into the app (`ReleaseSigningPins`). The executables and the MSI are Authenticode-signed with it. Windows does not trust the key, so UAC shows "Unknown publisher" (ADR-034 option A).
- **Content rule (ADR-034 security review H1).** Release signing runs unelevated on the owner's PC. A program running as the owner at that moment could have the real key sign different files. A release key signature therefore proves "the owner's key signed this", not "this is the reviewed build". ADR-034 section 7 requires the updater to install a release only when its files match the CI build of a reviewed commit, checked by the helper on an administrator-only copy before `msiexec` and again after install, and leaves this ADR to decide **how the app learns which CI build is genuine**.
- **First install.** The owner's first install used `Install-PCStewardRelease.ps1`, with the trust anchor (run id, commit, artefact digest) typed into the owner's paste after it was checked against GitHub. The in-app updater must reach the same result without a paste.

Facts this design depends on:

- The `Release payload` workflow (`release-payload.yml`) runs only when started by hand, on a GitHub-hosted runner, with a read-only token, and refuses a commit without both compiled pins. It publishes the unsigned payload, the CI candidate manifest (every file's path, length and SHA-256), `release-payload.json` (run, commit, version, pins) and `msi-reference.json` (the digest of the unsigned MSI's tables).
- GitHub's public REST API, without sign-in, reports a workflow run's workflow file, event, branch, commit and conclusion, and each artefact's SHA-256 digest. Downloading an artefact needs sign-in, so the app cannot fetch artefacts directly; a release asset can be downloaded without sign-in.
- The elevated helper accepts one request frame of at most 16 KiB and returns one response frame (protocol 2). It runs from `%ProgramFiles%\PC Steward`, which is the folder an upgrade replaces.
- The MSI has no custom actions. Its major upgrade runs after `InstallInitialize`, so a failed upgrade rolls back to the previous version (proven by the CI MSI job), and a lower version is refused.

## Decision drivers

- Nothing a same-user program can write decides what runs elevated (ADR-018, ADR-033).
- A GitHub compromise alone must not be able to put code on the owner's PC; neither may a one-off misuse of a signing session alone.
- No account, token or cloud service beyond reading public GitHub data; nothing in the background; the owner approves every update.
- "Updated" means verified: the installed files are the CI build of the release, signed with a release key, under the protected folder permissions.
- A failed or interrupted update leaves the previous version installed, or nothing that runs, never a half-replaced or unverified one.
- Reuse the reviewed pieces: the pinned signature check, the MSI tables digest, the payload-integrity checks of `Install-PCStewardRelease.ps1`, and the helper launch path.

## Options considered for the CI anchor

How does the app learn which CI build a release must match?

- **A. GitHub's public Actions API checked by the desktop only; content re-checked offline by the helper.** The release carries a small CI-made check file, which the desktop accepts only when GitHub's API says it is the artefact of a genuine run. Rejected (Codex review of this revision): the offline helper cannot tell a genuine check file from one a same-user program wrote. A program holding an MSI from a misused signing session could hand the helper its own check file with matching table and file hashes, and the helper would install it. The desktop is not a security boundary.
- **B. GitHub's public Actions API, checked by the desktop and again by the elevated helper itself (chosen, owner decision 2).** The desktop's check gives the owner early, readable answers; the helper's own two read-only requests are the security decision. This puts a small HTTPS client into the elevated process, constrained in section 6 (compiled addresses, no redirects, no proxy, no credentials, machine-wide TLS roots only, bounded strict JSON), and nothing it reads is used as a path, command or argument.
- **C. The owner's key signs a statement naming the CI build.** This adds nothing: the attack the content rule exists for is a misused signing session, which could sign a false statement too.
- **D. Reproducible builds.** The desktop build is not byte-identical across build environments (Task 48), so the app cannot rebuild and compare.
- **E. GitHub artifact attestations (Sigstore).** Verifying them properly needs an online transparency log and trust-root refresh; rejected in revision 1 as a cloud dependency. Useful as extra evidence, never as the decision.

## Decision

### 1. What the owner sees

1. Settings shows "PC Steward version 0.1.x" and a **Check for updates** button. There is no background or scheduled check (owner decision 3), so PC Steward contacts GitHub only when the owner presses it.
2. The check (section 4) ends in exactly one of: "A newer version is available", "You have the newest published version (checked at …)", "No release has been published yet", "An update was found but failed a check: <reason>", or "Could not check: <reason>". Only the second says the owner is current, and only after the newest release passed every check.
3. "A newer version is available" shows the version, the publish date, the commit and the CI run it was built from (both as links built from compiled parts), the download size, and GitHub's release notes labelled as unchecked text. The owner chooses **Download and check**.
4. After the download passes section 5, the screen says the MSI is signed with the owner's release key (primary or backup) and matches the CI build, and that installing will close PC Steward and show one UAC prompt from "Unknown publisher" for `PCSteward.ElevatedHelper.exe`. The owner chooses **Install**.
5. Approval creates an immutable self-update job (ADR-017): job id, from-version, to-version, run id, commit, check-file SHA-256, MSI SHA-256 and approval time. No URL, path or command. The job is revalidated before the helper starts; any difference makes it non-actionable.
6. The helper re-checks everything (section 6) and answers "ready". PC Steward closes itself. The install finishes in the background in about a minute (section 7). The owner reopens PC Steward from the Start menu, and the new version reports the job as **Updated** only after section 8 passes.

Nothing is downloaded or installed without the owner's action, and there is no "install automatically" setting.

### 2. Three independent conditions

PC Steward installs a release only when all three hold:

1. **The owner's key signed it.** The MSI and both executables carry an Authenticode signature whose signer's key matches a compiled pin (ADR-034 section 3). Nobody without the YubiKey (PIN and touch) or the offline backup key can produce this.
2. **It is the CI build of a reviewed commit.** The release's check file is the artefact of a genuine `release-payload.yml` run of this repository on `phase/4-beta` or `main` (section 3), confirmed with GitHub by the elevated helper itself (section 6). Nobody without write access to GitHub can produce this.
3. **The MSI installs exactly that build.** The MSI's tables give the CI build's digest, and every file it installs equals the CI payload, executables compared without their signatures (section 6). This ties condition 1 to condition 2.

A GitHub compromise without the key fails condition 1. A misused signing session without a matching CI build fails condition 3. Both together need the owner's GitHub account (or a reviewed commit that is itself malicious) **and** a misused signing session.

**Limit (self-updater security review R3).** Against a program running as the owner, conditions 1 and 2 are not independent. Such a program can usually reach the owner's GitHub sign-in (stored Git credentials or the browser session), push a commit to a release branch and start `release-payload.yml`. During a signing session it can also change what the key signs (ADR-034 review H1). A patient program can therefore get a genuine run and a pinned signature for its own content, and the helper would install it as designed. The release workflow refuses any ref that is not `phase/4-beta` or `main`, so a tag named like a branch cannot pass for one. Branch and tag rules on GitHub (pull requests required, no force-push or deletion, no tags named like the release branches) make such a push visible in the history, but they do not stop a program that holds the owner's sign-in. Closing the gap needs signing or approval on another device. That is accepted for now and listed under "Reconsider when".

### 3. How the app learns which CI build is genuine (the anchor)

- The `Release payload` workflow additionally uploads a second, small artefact, **`pc-steward-release-check`**, holding one file, `release-check.json`:
  - `schema` (`pc-steward-release-check:1`), `repository`, `runId`, `sourceCommit`, `productVersion`, `msiVersion`;
  - `pins`: both compiled pins;
  - `msiTablesSha256`: the digest from `msi-reference.json`;
  - `files`: every payload file's path, length and SHA-256, and for the two executables also their PE checksum, exactly as the candidate manifest and the unsigned executables give them.
- GitHub records the artefact's SHA-256 digest. Its retention is 90 days (the longest a public repository allows); a release whose check artefact record has gone cannot be installed in-app and is installed by hand instead.
- The release carries a byte-identical copy of that artefact's zip as the asset `pc-steward-release-check.zip`.
- The desktop accepts the check file only when, through GitHub's public REST API (`api.github.com`, no sign-in):
  1. `GET /repos/DanDG923/pc-steward/actions/runs/{runId}` describes run `runId` of repository `DanDG923/pc-steward` (also as head repository), workflow file `.github/workflows/release-payload.yml`, event `workflow_dispatch`, branch `phase/4-beta` or `main`, status `completed`, conclusion `success`, and head commit `sourceCommit`;
  2. `GET /repos/DanDG923/pc-steward/actions/runs/{runId}/artifacts` lists exactly one artefact named `pc-steward-release-check`, and its digest equals the SHA-256 of the downloaded zip;
  3. the zip holds exactly `release-check.json`, which parses strictly (closed key sets, size limits, no duplicates) and names that run, commit and repository, the compiled pins, and an `msiVersion` equal to the release tag.
- The run id comes from the check file; the tag and asset names come from the release. Everything GitHub returns is untrusted input to these checks, never an instruction.
- The desktop's acceptance only decides what the owner is shown. The elevated helper repeats checks 1 to 3 itself on its administrator-only copy of the zip before anything is extracted or installed (section 6 step 3). The pure checks (run fields, artefact list, digest, check-file parsing) live in one shared place, `PCSteward.Execution`, so the desktop and the helper apply exactly the same rules.

### 4. The check (desktop, unelevated)

- A new project, `PCSteward.SelfUpdate`, holds the network client and the pure checks; it grows from the parked check core in draft PR #39 (strict parsing, compiled hosts, manual redirects to compiled GitHub hosts only, size and time limits, fail-closed results). Its ECDSA release-manifest key, signature file and sequence are removed (section 11).
- One check makes at most four unauthenticated HTTPS GETs to compiled addresses (plus GitHub's redirects to its own asset host, followed by hand): the repository's latest release, the check zip (at most 1 MiB), the run and the run's artefacts. Download addresses are built from compiled parts and the validated tag (`v` + `major.minor.patch`), never from URLs in a response. The User-Agent is `PCSteward`, with no version. No token, cookie or account is sent, so nothing counts against the owner's GitHub account; GitHub's anonymous limit (60 requests an hour per IP address) is far above an owner-pressed check plus the helper's two requests, and a rate-limit answer ends as "Could not check" (or, in the helper, a Failed job that installed nothing).
- Drafts and pre-releases are ignored (the "latest release" endpoint excludes them). The release must have exactly the two assets `PCSteward-<msiVersion>.msi` and `pc-steward-release-check.zip`.
- The release is "newer" only when `msiVersion` is above the running version. A release at or below it is "current" or "older"; the MSI refuses downgrades in any case.
- What the check reveals: the owner's IP address reaches GitHub, which learns that this address checks PC Steward's releases, but not which version. Settings says so.
- `PCSteward.ScheduledHost` gains no reference to this project (ADR-020).

### 5. Download and first check (desktop, unelevated)

After the owner's **Download and check**:

- The MSI is downloaded to `%LocalAppData%\PCSteward\Downloads\SelfUpdate\` (an ADR-019 owned root) under a name built from its SHA-256, with a hard size cap (512 MiB) and a cancellable, time-limited transfer. The check zip is stored beside it the same way.
- The desktop checks the MSI's pinned signature (`AuthenticodeIdentity`, ADR-034 section 3) and that its tables give `msiTablesSha256` (read-only `msi.dll` calls, as `PCStewardMsiTables.ps1` does). A failure is shown with its reason and the files are deleted.
- These checks let the owner see a bad release before any prompt. They are not the security boundary: section 6 repeats them where a same-user program cannot interfere.

### 6. Install: one new typed helper operation

After **Install**, the desktop starts the helper as for any update (ADR-018 launch trust, one UAC prompt) with one new operation, `Update/PCStewardSelfUpdateV1`, added to the pinned capability statement. *(Build note, 2026-11-11: built as a separate helper mode gated by a route-less installed-release check instead of a statement entry; see the [helper operation design](../review/SELF_UPDATE_HELPER_OPERATION_DESIGN.md), part 2 step 2.)* Its request carries only the job's values: to-version, run id, commit, check-file SHA-256 and MSI SHA-256. It carries no path: the helper derives both file names from the two hashes under the requesting user's fixed `Downloads\SelfUpdate` folder.

The helper, elevated:

1. Creates a new administrator-only work folder `%ProgramFiles%\PC Steward Update <32 hex>` (only administrators can create folders there; protected DACL: SYSTEM and Administrators full control, Authenticated Users read only), with a `private` folder inside it that only SYSTEM and Administrators can read, which holds the input copies, the unpacked image, every log and msiexec's temporary files *(build note, Mythos review S2)*. Once step 3 has confirmed the request with GitHub *(build note, Mythos review S4)*, it removes such folders left by earlier runs through the owned-root deletion rules, keeping the folder of the newest earlier job until its outcome has been read (section 8).
2. Reads each input once through a held, reparse-free handle with no alternate stream, checks its length bound and SHA-256 against the request, and writes it into the work folder; the copy is hashed again. From here on only the copies are used.
3. **Confirms the CI anchor with GitHub itself** (owner decision 2). From the copied zip it reads `release-check.json` (same strict rules as section 3) and requires it to name the request's run, commit and version and the compiled pins. It then makes exactly two unauthenticated GETs, `…/actions/runs/{runId}` and `…/actions/runs/{runId}/artifacts`, and applies section 3 checks 1 and 2 to the answers, with the digest compared against the SHA-256 of its own copy of the zip. The client is constrained so that nothing a same-user program controls can change the answer:
   - the host `api.github.com` and the repository path are compiled; the run id is the number from the check file; no redirect is followed (any 3xx is a refusal);
   - no proxy (the user's proxy settings and environment are ignored), no cookies, no credentials, no token;
   - the server certificate chain must build to a root in the **LocalMachine** Root store only (custom root trust built from that store), never the user's own store, which a same-user program can change; revocation is checked online, and a revocation check that cannot complete is a refusal;
   - each answer is at most 512 KiB, read within a fixed time limit (30 seconds for both), and parsed with the same strict rules as the desktop's; anything unexpected is a refusal.
   If GitHub cannot be reached or any check fails, nothing is installed and the job is Failed with "Could not confirm the release with GitHub" or the failed check. Nothing GitHub returns is used as a path, command or argument.
4. Checks the copied MSI: a compiled pin (`AuthenticodeIdentity`), tables equal to the now-authenticated `msiTablesSha256`. Equal tables mean the MSI runs no code (no custom actions beyond the downgrade message), installs only to `%ProgramFiles%\PC Steward`, and has the CI build's file table. As a separate rule that does not depend on the check file, the helper also refuses any MSI whose `CustomAction` table holds anything other than the one error-message action (type 19), or that has a `Binary`, `ServiceInstall`, `ServiceControl` or `MsiEmbeddedChainer` row, before `msiexec` touches it.
5. Extracts the copied MSI with a Windows Installer administrative install (`msiexec /a`, fixed arguments, `TARGETDIR` inside the work folder). Step 4 has proved the package has no custom actions, so this only unpacks files. The unpacked tree must be exactly the CI payload of the check file (every non-executable file byte-identical; each executable equal to the CI one apart from its single Authenticode signature, which must carry the same compiled pin as the other) plus a release manifest that lists exactly those files. This is the pre-install half of the ADR-034 H1 content rule.
6. Copies its own image into `<work>\bin\` (held read, hash compared with the running image, written, hashed again) and writes two files into the work folder: `job.json` (request values, the PID of the desktop it is serving) and **`expected-install.json`** (job id, to-version, the verified release manifest's SHA-256, and the path, length and SHA-256 of every file the install must leave in `%ProgramFiles%\PC Steward`, signed executables as extracted in step 5). Both are written by the elevated helper into the protected folder, so a same-user program can read them but not change them.
7. Starts that copy with one fixed argument, `--finish-self-update "<work folder name>"`, answers the desktop "ready" with the verified values and the work folder name (recorded in the job), and exits within the normal exit budget.

The copy is needed because `msiexec` must replace `PCSteward.ElevatedHelper.exe` in `%ProgramFiles%\PC Steward`, which cannot happen while that file is running, and an elevated process in an administrator-only folder is as protected as one in Program Files.

### 7. Finish (the helper copy)

The helper copy refuses to run unless it is elevated, its own path is `%ProgramFiles%\PC Steward Update <32 hex>\bin\PCSteward.ElevatedHelper.exe` under the work folder's protected DACL, and `job.json` there parses. It then:

1. Waits, for at most five minutes, until the desktop it served and every process whose image is under `%ProgramFiles%\PC Steward` has exited. If they have not, it installs nothing and records "PC Steward did not close".
2. Re-hashes the MSI copy and runs `msiexec /i "<work>\private\<msi>" /qn /norestart /l*v "<work>\private\install.log"` with exactly those arguments, from `%SystemRoot%\System32`, and waits for it (at most 30 minutes).
3. Records the exit code. 0 continues to step 4. 3010 (restart needed; 1641 cannot occur with `/norestart` and is treated the same) means files may be waiting for the restart to be replaced, so the helper copy skips steps 4 and 5, records "restart needed" with the current boot time, and the post-restart check of section 8 decides the outcome. Any other code is a failed install (section 9).
4. Verifies the installed tree exactly as `Install-PCStewardRelease.ps1` step 4 does, against `expected-install.json`: the protected install-folder DACL on the folder and every file, every installed file equal to its expected length and SHA-256 and no other file, both executables carrying the same compiled pin, and the installed release manifest equal to the one verified. One installed product, at the new version, owns the folder.
5. On any difference after `msiexec` changed the install, uninstalls the product (fail closed, as the first install does) and records why.
6. Writes `result.json` (job id, outcome, versions, exit code, boot time, verified hashes) into the work folder, readable by the owner's account, and exits. Nothing is launched afterwards.

### 8. Success

When PC Steward next opens, it looks for the job's work folder (its name is recorded in the job) and reads `result.json` and `expected-install.json` there. Both must be in the protected work folder (owner SYSTEM or Administrators, the protected DACL of section 6 step 1, no reparse points) and name this job id. The job becomes **Updated** only when the reopened PC Steward:

- is the job's to-version (its own assembly version), and
- passes the release capability check of its own install (ADR-034 section 4: pinned signatures and the release manifest), and
- finds the installed release manifest's SHA-256 equal to the one in `expected-install.json`, and
- either reads a `result.json` saying the helper copy verified the installed tree (exit code 0), **or**, when `result.json` says "restart needed", the PC has restarted since (current boot time later than the recorded one) and PC Steward's own read-only check of the installed tree passes: the protected install-folder DACL on the folder and every file, and every file in `%ProgramFiles%\PC Steward` equal to its entry in `expected-install.json`, with no extra or missing file. This check is bounded (the file list is fixed and capped) and runs once per launch until the job is decided.

A restart-needed job seen before a restart stays "Restart to finish". After a restart, a failed check makes the job **Failed** ("The update did not finish as checked") with the reinstall guidance of section 9; it does not stay Inconclusive. The desktop cannot uninstall (it is not elevated), so it then refuses to start any other update until the owner reinstalls a signed release by hand, as ADR-034 section 4 already does for an install that fails its capability check.

Anything else is **Failed** or **Inconclusive**, never Updated (`TM-12`). Inconclusive is used only when the job's work folder or its files are missing or unreadable. The owner is told what happened and where the install log is (readable by administrators).

### 9. Failures and rollback

- Before `msiexec` starts, any failure leaves the installed version untouched and the job Failed with its reason.
- A failed `msiexec` rolls the install back to the previous version (MSI transaction).
- A restart-needed result is reported as "Restart to finish, then reopen PC Steward". The first launch after a restart runs section 8's post-restart check, which ends the job as Updated or Failed.
- A verified mismatch after install uninstalls PC Steward (fail closed). Per-user data stays. The owner reinstalls the previous signed MSI by hand (ADR-034 section 6).
- There is no in-app downgrade: the MSI refuses lower versions. Going back means uninstalling and installing an older signed MSI by hand. Database migrations stay forward-only (ADR-015).
- The manual route stays documented and tested: the owner can always install a release with `Invoke-PCStewardReleaseInstall.ps1` and an anchor checked by hand.

### 10. Residual risks

- **A same-user program present at update time.** It can feed the helper its own inputs and the desktop its own GitHub answers, but not the helper's: the helper confirms the check file with GitHub itself, through a client the user's proxy settings, certificate store and redirects cannot steer (section 6 step 3). To get past section 6 the program therefore needs an MSI signed with a release key **and** a genuine CI run whose artefact describes the malicious content, which means a misused signing session and write access to GitHub (or a malicious reviewed commit). It can still show its own "Unknown publisher" UAC prompt, which looks the same as PC Steward's (ADR-034 option A); that residual is outside the updater and accepted.
- **A TLS certificate for `api.github.com` from a root in the machine store that is not GitHub's.** Only an administrator can add machine roots, and a mis-issued certificate would still need a release-key signature over the malicious MSI. Accepted.
- **The helper now makes network requests.** Two fixed GETs, during an owner-approved update only, through the constrained client of section 6 step 3. Its JSON parser is the same strict, size-bounded one the desktop uses and is fuzz-tested. Accepted as the price of closing the residual above.
- **Hidden newer releases (freeze).** A network attacker or a GitHub compromise can hide newer releases; the app then reports the newest release it could check, with the time of the check. The app never says "up to date" when a check failed. Accepted while PC Steward is used by the owner only.
- **Older genuine release offered.** A GitHub compromise could make "latest" point at an older genuine release; it can only be at or below the running version or a genuine newer build, and the MSI refuses downgrades. Accepted.
- **Malicious reviewed commit.** A commit merged into `phase/4-beta`, built by CI and signed by the owner is installed. The updater trusts the owner's review and merge rules; it does not review code.

### 11. Dropped from revision 1, and why

- **Separate ECDSA release-manifest key and `.sig` file.** The owner's release key already signs the MSI and the executables, and the content rule covers what a manifest signature would. A second signing step on the owner's PC adds a ceremony without a new guarantee (option C).
- **Sequence numbers and the highest-sequence file.** Windows Installer refuses downgrades, and the version must rise.
- **CI-held freshness key.** Its weekly re-signed file cannot be an asset of an immutable release, and the freeze it detects is accepted above.
- **Cooling-off window, Beta channel.** Not needed while releases are for the owner only; stable releases only.
- **Per-user `versions\` folders and shortcut repointing.** Replaced by the MSI major upgrade (ADR-034).

### 12. Publishing a release

1. The owner (or Claude, with the owner's approval) starts `Release payload` on `phase/4-beta`.
2. The owner signs with `New-PCStewardRelease.ps1`, as for the first release.
3. A new hand-started workflow, `publish-release.yml`, given the run id: checks that the run is a green `release-payload.yml` run of this repository on `phase/4-beta` or `main`, downloads its `pc-steward-release-check` artefact, checks the zip against the recorded digest, and creates a **draft** release, tag `v<msiVersion>` at the run's commit, with the zip attached. It has `contents: write` and nothing else. *(Build note, 2026-11-11: it also needs `actions: read` to download the run's artefact.)* It cannot sign, and the app trusts nothing it writes beyond what sections 3 and 6 check.
4. The owner attaches `PCSteward-<msiVersion>.msi` to the draft in the browser and publishes it. GitHub immutable releases (owner decision 1) then stop its assets and tag being changed.

## Threats

Proposed additions to `docs/architecture/THREAT_MODEL.md` once accepted:

| ID | Threat | Controls in this design |
|---|---|---|
| TM-19 | **Compromised release**: an attacker with GitHub access publishes a release, swaps an asset or pushes and builds a commit | Release key signature (YubiKey or offline backup, pins compiled), content equal to the CI build, immutable releases. GitHub access alone fails the key check. |
| TM-20 | **Misused signing session**: a program running as the owner while they sign gets the key to sign other files | CI anchor (section 3) confirmed by the elevated helper with GitHub itself, and the content rule (sections 6 and 7): the signed MSI must install exactly the CI build of the run it names. A check-file-independent MSI structure rule (an allow-list of the tables the PC Steward MSI uses, and error-message custom actions only) refuses everything else before `msiexec`. |
| TM-21 | **Stolen or lost release key** | Hardware primary key, offline backup, both compiled in; the other key signs the replacement. No online revocation (accepted). |
| TM-22 | **Downgrade, replay or freeze** | Version must rise; MSI refuses downgrade; "up to date" only after a fully checked release; freeze accepted (section 10). |
| TM-23 | **Same-user substitution between check and install** | Helper copies inputs into an administrator-only folder by hash, confirms the check file with GitHub through a client the user cannot steer, re-checks pin, tables and content on the copies, installs only the copy, verifies again after install (or after the restart, against its own protected `expected-install.json`). |
| TM-24 | **Remote metadata becomes execution authority** | No URL, path, command or argument from GitHub is used; compiled hosts, repository, workflow file, branch names, asset names; release notes are plain untrusted text; the helper request carries hashes and versions only. |
| TM-25 | **Updater becomes an unattended installer or persistence** | Owner-pressed check only; one immutable job per approval; the helper copy is one bounded, one-off process that exits; no service, task or resident updater; the scheduled host has no updater reference. |
| TM-26 | **Interrupted or failed update** | MSI transaction rolls back; restart-needed reported as such and decided by the post-restart check against the helper's protected expected tree; mismatch after install uninstalls (or, after a restart, fails the job and blocks other updates); job never Updated without section 8. |
| TM-27 | **Verification fails open** | Every error in every step ends the job before or after `msiexec` as Failed; no override; fixture test for each refusal. |
| TM-28 | **Privacy** | Only when the owner presses Check; no token, identifier or version sent; disclosed in Settings. |

Existing `TM-03`, `TM-04`, `TM-09`, `TM-12`, `TM-13` and `TM-18` also apply.

## Consequences

### Positive

- One-click, owner-approved updates of the protected install, with one UAC prompt and no paste.
- Neither a GitHub compromise nor a misused signing session alone can put code on the owner's PC.
- No new key, signing step, service or background process. The elevated helper gains one typed operation.

### Negative / trade-offs

- The app makes its first network requests (to GitHub only, on the owner's request), and the elevated helper makes two of them during an approved update (owner decision 2). An update cannot be installed while GitHub is unreachable.
- The UAC prompt says "Unknown publisher" (ADR-034); the owner recognises it by its timing and program name.
- PC Steward closes during the install and must be reopened by hand: the MSI launches nothing, and an elevated process cannot safely start the desktop as the owner.
- A mismatch found after install leaves no PC Steward until the owner reinstalls by hand. This is the same fail-closed rule as the first install.
- Releases become public assets of the public repository (owner decision 1). They contain no personal data (checked before publishing, as for every PR).

### Follow-up controls and tests

- Fixture tests for every check refusal: wrong run, workflow, event, branch, conclusion, commit, repository; artefact missing, duplicated or with another digest; check file malformed, oversized, with unknown keys or other pins; tag not matching; extra or missing assets; redirects to other hosts; oversized or truncated downloads.
- Helper tests for every section 6 and 7 refusal: hash mismatch, reparse point, alternate stream, check file not confirmed by GitHub (each section 3 refusal, a redirect, an oversized or slow answer, an untrusted or user-store-only root), unpinned or other-key MSI, tables mismatch, forbidden MSI table rows, extra, missing or changed file in the extracted image, executables with different pins, stale work folders, the helper copy run from any other place.
- Post-restart tests (section 8): restart-needed before a restart stays "Restart to finish"; after a restart a matching tree is Updated; a changed, extra or missing file, a wrong DACL, or an `expected-install.json` or `result.json` not in a protected folder is Failed.
- The Windows CI MSI job gains an end-to-end self-update run: install 0.1.1, then update to 0.1.2 through the helper operation (throwaway key), including a tampered MSI refused before `msiexec` and a failed upgrade rolling back.
- Architecture tests: `PCSteward.ScheduledHost` has no reference to `PCSteward.SelfUpdate`; `System.Net.Http` is used only by `PCSteward.SelfUpdate` and by the helper's one self-update GitHub client, which uses compiled addresses only; the helper operation has no path field.

## Build order

1. This revision reviewed and accepted (owner decisions below).
2. CI: the `pc-steward-release-check` artefact, and `publish-release.yml`.
3. The check (section 4) and download (section 5), with tests.
4. The helper operation and finish (sections 6 and 7), the success check (section 8), and the CI end-to-end job.
5. The update screen in Settings.
6. Independent security review of the updater, with fixes before any host step.
7. Release 0.1.1, which contains the updater, installed on the owner's PC by the signed-installer paste (the 0.1.0 install has no updater, so it cannot fetch its own successor).
8. The first in-app update: the held UI work, released as 0.1.2 and installed through the updater.

## Owner decisions

1. **Publishing.** Publish signed releases as GitHub releases of the public repository, and turn on immutable releases (repository Settings, General, Releases)? Recommended: yes. Without published releases there is nothing for the app to fetch. **Owner, 2026-11-11: "Yes, publish releases"** (with immutable releases).
2. **Where the CI anchor is checked.** Should the elevated helper confirm the CI build with GitHub itself (option B, recommended after the Codex review showed option A lets a same-user program forge the check file), or only the desktop (option A)? **Owner, 2026-11-11: "Yes, helper asks GitHub"** (option B).
3. **When to check.** Only when the owner presses Check for updates (recommended), or also a notify-only check during the scheduled scan?

## Reconsider when

- PC Steward is distributed to people other than the owner (freeze protection, a Beta channel and publicly trusted signing become worth their cost).
- GitHub stops publishing artefact digests through its public API, or changes run metadata.
- A release key is lost or suspected compromised.
- The owner chooses a publicly trusted certificate (ADR-034).
- The owner has a second device that could approve or sign releases away from the PC (closes the same-user limit in section 2).
