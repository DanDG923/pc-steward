# Task 50 — activation host-session plan for the Chocolatey routes (revision 1, draft)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; host contract `V2_HOST_TEST_PLANS.md`.

Status: **draft for review; not approved; authorizes nothing.** Part A (the confirming evidence run and the optional H3 runas probe) reuses the committed, reviewed host-evidence script and is ready for owner approval. Parts B (v2 redeploy) and C (ADR-025 exact-artifact test) **cannot run yet**: the tooling they need is missing or does not fit the current host (§11). No host action has happened. The owner runs every step; the agent runs nothing on the host, including through Remote Control.

**Owner decision (2026-11-02):** "You can build the tool, but we'll run the tests on my PC later today." This approves building the missing cloud tooling (G1–G4 data-preserving redeploy, caller and database preservation; G3 candidate builder; G5 exact-artifact test design). It approves no host action: the owner runs Part A on the PC later today under the §9 wordings, and Parts B and C still need their own exact approvals once the tooling is reviewed and green on Windows CI. G7 (evidence carry-over) and G9 (real-update test) remain open owner decisions.

## 0. Purpose and gate map

ADR-031 owner decision 7 approves the activation step (a `HasSupportedMode`/allow-list change for the Chocolatey routes), but only after the recorded gates are met. This plan puts the owner-host gates into one ordered session. Activation itself is a later, separately reviewed code change. It is not part of this session.

| Gate (owner numbering) | Source | Part | State |
|---|---|---|---|
| 1. Approved v2 redeploy of the updated build, including the signed helper | Composition record, admission obligation 3; adapter gates §A.3.1 | B | **Blocked** (G1–G4, G6, G8) |
| 2. ADR-025 exact-artifact test of the signed helper (§2.1 linked-token match against the real desktop; `AccessCheck` on the real Chocolatey tree) | ADR-025 follow-up controls; helper-endpoint design §8–§9; obligation 2 | C | **Blocked**: no script, harness or procedure exists (G5, G7) |
| 3. Confirming Default evidence run: `matchesPinnedRelease: true` on Chocolatey 2.7.5 | ADR-031 decision 2; evidence interpretation ("Leftover removed") | A | Ready (approval 3) |
| 4. AQ1UAC / runas probe (H3) | Host-evidence plan §6; owner decisions 2026-11-02, item 3 | A | Ready (approval 4, optional) |

Other admission items are outside this session: the §8.6 client-route admission gates (CI work), a Chocolatey-managed target package (G9), and the owner's final admission approval (obligation 8). Obligation 7 (the proxy disclosure) was settled in ADR-025 on 2026-11-02, and R5 was accepted under ADR-031 decision 6.

## 1. Source pins (repository commit `feeb156627a1003f0e4b4e351b1e7b167e01ea30`)

Hashes were computed from the committed blobs (`git show feeb156:<path> | sha256sum`). **Line endings:** `Task50-HostEvidence.ps1` and the capability file are `-text` in `.gitattributes`, so every checkout has the stored bytes. The six owner-only scripts are stored as LF and are **not** `-text`. A Windows clone with `core.autocrlf=true` writes CRLF, which changes the hash. The CRLF value is the one expected on the owner's clone: Task 48 pinned the unchanged remover at `3703F0CA…0B67`, which equals its CRLF form below. For `Task50-HostEvidence.ps1`, a hash that matches neither column is a stop. The other rows are a feeb156 drift record only (see §2).

| File | SHA-256 (stored / LF) | SHA-256 (CRLF checkout) |
|---|---|---|
| `scripts/evidence/Task50-HostEvidence.ps1` (CRLF, ASCII, `-text`; unchanged since `e133d2b`) | `0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4` | same (stored as CRLF) |
| `scripts/Invoke-OwnerOnlyApprovedRedeployment.ps1` (template) | `E4647BCEC501A0109F1137860CFD3D30DB262C6F44B0341964477B1B58AB2B7D` | `B5799A2D914768A047E6669FD1069040704750D5A75F3B3FF018D38DC579A3A1` |
| `scripts/Install-OwnerOnlyProtectedTestDeployment.ps1` | `EFD9C1D78240E521F8CFE169DBCDE824FD90A3AC7413FCA153664468938886C7` | `B994236329346B8E77823BA72E2926D8F7D1D296821993F043C77E5E6A2AB044` |
| `scripts/Test-OwnerOnlyProtectedTestDeployment.ps1` | `CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447` | `2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2` |
| `scripts/Remove-OwnerOnlyProtectedTestDeployment.ps1` | `A331D80CDE3F791B9C3B9F907078FFB9AAEF2FDBC7F3D946FFD2888D25C37126` | `3703F0CA5CB837C4E784E1BB42D18ABAA013CFAA75F9A3A1B5FFF5C6E81F0B67` |
| `scripts/OwnerOnlyPayloadIntegrity.ps1` | `57245268969A81F28D4DE0C3AB9F1D8F63195B29D3E9EDCFF49D63EFF2F9F9AB` | `EFF3840697893C8CF176435E3D4F5FB843811965FDBAE2883E97371E0CB6D987` |
| `scripts/OwnerOnlyExecution.ps1` | `EC25B55B9B2FD15F8915D0C6770D897626A0808D695786C80F423CA41553BC46` | `50E246DD6779AD7806CE212B238CBC4AF32083B738BD0BD624A081042458B83C` |
| `scripts/Wait-OwnerOnlyDeploymentProcess.ps1` | `F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59` | `2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B` |
| `src/PCSteward.ElevatedHelper/PCSteward.DeploymentCapability.txt` (`-text`, 103 B, protocol 2) | `0772DAD8365A8313125A364FBBD70DB2AE7BFC6C203732EA0751D75E579DD9C7` | same (`-text`) |

The redeploy hashes are recorded only so that drift can be detected. They are **not** approvable for Part B, which needs changed scripts (G1, G2) that will have new pins.

## 2. Prerequisites (owner side)

- [ ] **Part A** needs only `scripts/evidence/Task50-HostEvidence.ps1` at its §1 hash (unchanged at every later commit so far; H1 checks it). Any commit of the owner's clone works for Part A, and no fetch is required. The redeploy-script hashes in §1 are for drift detection only: the G1/G2 tooling (commits `a22a623`, `cfb044b`) deliberately changed the template and `OwnerOnlyExecution.ps1`, so a later clone does not match them, and that is **not** a stop for Part A. Part B will carry its own new pins.
- [ ] Windows 11 x64, interactive login as the installing user, in a **non-elevated** Windows PowerShell 5.1 window. Only Part B's single UAC prompt elevates.
- [ ] **No secrets or certificates are needed or requested.** The ADR-022 installer creates a fresh signer during the install: a 14-day, non-exportable, CurrentUser RSA-2048/SHA-256 code-signing certificate, `CN=PC Steward Owner-Only Test <GUID>`, trusted only in CurrentUser Root/TrustedPublisher. There is no owner-held certificate, PFX, password or key export, and none may be supplied. This is not production signing.
- [ ] **Signer expiry:** the deployed signer `14F34EFEFA84C367F25917B4B28803DCECE85623` expires **2026-11-13T20:18:09Z** and has no timestamp (Task 49 recovery record). After that time the redeploy preflight (`Test-…-RequirePinnedPayload`, which requires Valid Authenticode) fails closed before anything is removed (G8). The new signer's 14 days bound Part C.
- [ ] Part B only (once G3 exists): .NET SDK 10.0.403 (`global.json`) at its previously approved command-only location. No SDK or package installation is part of this plan.
- [ ] No Chocolatey operation is in progress; every `PCSteward*` process is closed before Parts B and C; `%TEMP%\pcsteward-evidence-task50` does not exist.

## 3. Session order and global stop rules

Order: **A → B → C.** A runs first because it touches no PC Steward file. If Chocolatey does not match 2.7.5, redeploying cannot lead to activation. Part A may also be run on its own before B and C exist, under the same approval wording.

- "Stop" means: run no further step in any part, do not retry, and report only the console token. A stopped part is never re-run without a new exact approval.
- Do not proceed from A to B unless O5 is `PASS-READABLE`, `matchesPinnedRelease` is `true`, and (if H3 ran) AQ1UAC is not `FAIL`.
- Do not proceed from B to C unless B ends with native exit 0, protected phase `validated`, the independent validator reports `pinned-unsigned-v2` with `helperProtocol` 2, and the database hash is unchanged.
- An unexpected UAC or certificate prompt (wrong program, wrong publisher, or a second prompt) gets **No**, followed by stop.
- No result in this session is admission, success or a gate closure by itself. Results feed an independent review, and then the owner decides.

## 4. Part A — confirming Default run and optional H3 (existing script; ready)

These are the blocks of the [host-evidence plan](TASK_50_HOST_EVIDENCE_PLAN.md) revision 2.2, §5, unchanged. Its §5–§9 rules (bounds, case details, sanitizing, interpretation) apply in full. Each block is one line; do not re-wrap. `<CHECKOUT>` is the owner's clone path. It is never pasted back.

| Step | Block | Expected | Stop conditions |
|---|---|---|---|
| A1 | H1 | hash line, then `HASH-OK` | `STOP: evidence directory already exists` (delete nothing); `STOP: hash differs` or an error after creation → H1-X, then stop |
| A2 | H1b | `PARSE-OK` | `STOP: parse errors n` → H1-X, then stop; `STOP: H1 did not pass` → stop |
| A3 | H2 (once) | `EVIDENCE: …`, `REDACTIONS: n`, then `VERDICT` lines for O5, AQ1N, AQ1M, AQ1C, A2L3S | any `STOP:`/`STOPPED:` line; any error before `EVIDENCE:` |
| A4 | H3 (once; approval 4 only) | one UAC prompt for `aq1-managed.exe` (unknown publisher) in `…\pcsteward-evidence-task50\run-…`; `VERDICT AQ1UAC: …` | a second prompt, or any other program named → **No**, then stop. A decline is `NOT-RUN-UAC-DECLINED`, with no retry |
| A5 | H5 | each `evidence.json` opened in Notepad, checked, and pasted | unredacted private text: do not share it; continue with A6 and report |
| A6 | H6 (once, last) | `CLEANUP-COMPLETE: … removed` | cleanup refuses: leave everything and report |

After a STOP in A3 or A4, A5 (if evidence was written) and A6 remain authorized.

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'; $created = $false; $hashOk = $false; $expected = '0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4'
if (Test-Path -LiteralPath $d) { 'STOP: evidence directory already exists' } else { New-Item -ItemType Directory -Path $d -ErrorAction Stop | Out-Null; $created = $true; Copy-Item -LiteralPath '<CHECKOUT>\scripts\evidence\Task50-HostEvidence.ps1' -Destination $d -ErrorAction Stop; $h = (Get-FileHash -LiteralPath (Join-Path $d 'Task50-HostEvidence.ps1') -Algorithm SHA256 -ErrorAction Stop).Hash; $h; if ($h -eq $expected) { $hashOk = $true; 'HASH-OK' } else { 'STOP: hash differs' } }
if ($created -and $hashOk) { $tokens = $null; $parseErrors = $null; [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $d 'Task50-HostEvidence.ps1'), [ref]$tokens, [ref]$parseErrors); if ($parseErrors.Count -ne 0) { 'STOP: parse errors ' + $parseErrors.Count } else { 'PARSE-OK' } } else { 'STOP: H1 did not pass' }
if ($created) { Remove-Item -LiteralPath $d -Recurse -Force; 'H1X-REMOVED' } else { 'STOP: not created by H1; delete nothing' }
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Default
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Uac
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Cleanup
```

The lines above are, in order: H1 (two lines), H1b, H1-X (only under its stated conditions), H2, H3, and H6. If the window closes after `PARSE-OK`, re-run only the first H1 line to set `$d` again. Never re-run the second line. H4 (`PcStewardHolds`) is **not** part of this session.

**Activation-relevant pass criteria (Default JSON):**
- `verdicts.O5 = PASS-READABLE`;
- `results.O5.matchesPinnedRelease = true`, which the script computes as `codeList.matchedPin = 56`, `helpersTree.unlistedFiles = 0` and `toolsTree.unlistedFiles = 0`;
- `engine.matchesPin = true`; `registration.chocolatey = present` with `chocolatey.nupkg` `matchesPin = true`; `routeFindings = []`;
- `scriptSha256` equals §1; `stop = null`;
- AQ1N, AQ1M, AQ1C and A2L3S `PASS`, as in the two earlier runs.

Any other value blocks activation and goes to the host-evidence plan §9 interpretation. For AQ1UAC: `PASS` closes runas for a synthetic image. `NOT-RUN-UAC-DECLINED` gives no evidence, and the owner may let Part C stand in for runas. `FAIL` triggers the A2-L5 fallback, a spec amendment and a new build, so the session stops before B.

## 5. Part B — v2 redeploy (blocked; outline only, no runnable blocks)

**What exists:**
- The template `Invoke-OwnerOnlyApprovedRedeployment.ps1` accepts plan schema `pc-steward-exact-redeploy-v2`, with `expectedOldOrigin` `pinned-unsigned-v1` and `expectedNewOrigin` `pinned-unsigned-v2`. It requires `helperProtocol = 2` after the install.
- The six protected scripts: the guarded remover; the installer, which signs with a fresh owner-only signer; the independent validator; payload integrity; execution ACLs; and the waiter.
- The ADR-022 alignment note (owner-accepted 2026-11-01).
- The Task 48 execution record (one approved run, PASS) and the Task 49 recovery record (PASS) as precedent.

**Why it cannot run on the current host:**
1. The template refuses when `%LOCALAPPDATA%\PCSteward` exists, both before removal and after installation. The host has had a live database since Task 49 (G1).
2. The non-elevated caller, the encoded-command generator and CheckOnly that Tasks 48 and 49 used were never committed. They are host-local, ignored artifacts built for v1 plans, and Task 49 found two defects in them (G2).
3. No committed script builds the v2 unsigned candidate and its manifest (G3).
4. No committed database-preservation procedure exists (G4).
5. The build commit is not yet chosen (G6).

**Intended shape once G1–G4 and G6 are reviewed** (for review, not approval):
- **B1.** The owner runs the reviewed candidate builder from the pinned build commit and records the candidate manifest SHA-256. The capability file must be at the payload root.
- **B2.** The owner runs the reviewed database-preservation step: closed app, no WAL/SHM, a consistent copy, hashes of the original and the copy, and an integrity check on the copy only.
- **B3.** The caller runs in CheckOnly mode. It verifies every pin, the old ledger and signer, a valid old signature, no `PCSteward*` process and a fresh execution root.
- **B4.** One `-ExecuteApprovedPlan` run with one UAC prompt for system Windows PowerShell. It performs: the protected bootstrap; the old-deployment preflight (`-RequirePinnedPayload`, v1); one guarded removal, including the old signer's three CurrentUser entries; one install with a fresh signer; and the independent validator, which must report `pinned-unsigned-v2` with `helperProtocol` 2.
- **B5.** The owner checks that the database hash is unchanged.

Expected Windows prompts, as in Task 48: UAC for Windows PowerShell; deletion of the old Root certificate `14F34EFE…`; installation of the new `CN=PC Steward Owner-Only Test <GUID>` Root certificate. Allow 15 minutes. A timeout means pending or unknown: no kill and no retry. A failure after removal leaves no deployment, and recovery needs its own approval. **No launch.**

## 6. Part C — ADR-025 exact-artifact test (blocked; required evidence only)

**No exact-artifact test script, harness or procedure exists in the repository.** It is a separate future task: design, independent review, implementation, Windows CI, then a host plan in this format.

A design must deal with two facts:
- The helper accepts only the protected desktop as its peer (ADR-018 identities; peer verification).
- The routes are not admitted: `HasSupportedMode` admits only WinGet, and nothing in `src` constructs the composition or the dispatcher. The real signed desktop therefore cannot send a protocol-2 request today.

The run must produce, against the Part B deployment (pinned installed manifest, signer thumbprint and unsigned helper raw image hash), sanitized evidence of:
1. the §2.1 linked-token match: the helper's `TokenLinkedToken` matches the real desktop peer by `AuthenticationId` and SID; Medium IL; not restricted; not AppContainer; allow-listed privileges only;
2. `AccessCheck` verdicts on the real `ProgramData\chocolatey` tree (node and ancestor modes) for the requester;
3. the signed apphost's WinVerifyTrust path and peer identity under `HeldHelperBinding` across the real `runas` launch, including the A2-L5 decision;
4. A2-L3 on the real deployment (for example the existing block H4 against the v2 install, under its own approval);
5. no Chocolatey process started, nothing changed, the desktop closed normally, and the database preserved.

The A-Q1 residuals (a native held DLL load, PowerShell helper reads, an elevated launcher holding files) need a real client launch. With no target package and only 2.7.5 on the list, they cannot be exercised (G9).

## 7. Evidence to paste back (sanitized)

- **Part A:** each `evidence.json` in full, after the owner has checked it for private text. Paths are already tokenized and identities redacted. Also the console tokens `HASH-OK`, `PARSE-OK`, the `VERDICT …` lines and `CLEANUP-COMPLETE`. After a STOP, only the STOP token.
- **Parts B and C (later):** only the fields the reviewed tooling marks as sanitized: exit codes, phase, origin, `helperProtocol`, manifest and ledger hashes, the signer thumbprint and expiry, and the database hash equality as a boolean. **Never** the plan JSON (it holds the raw installer SID), user names, profile paths, certificate private material, or console text from before the evidence was written.

## 8. Cleanup

Part A: H6 only. Delete nothing by hand. Part B retains the protected execution root, the compiler area and the results by design. Their deletion, and any recovery, needs separate exact authority. Part C cleanup is defined by its future plan.

## 9. Owner approval wording to request (four separate approvals)

Approvals 3 and 4 are complete. Approvals 1 and 2 are **templates**: they are not approvable while any `[…]` placeholder remains, or before G1–G8 are closed and reviewed.

**Approval 1 — v2 redeploy (template):**
> I approve exactly one run of the v2 redeploy in revision [R] of the Task 50 activation host-session plan: the caller SHA-256 [caller] in CheckOnly mode and then once with `-ExecuteApprovedPlan`; the encoded command SHA-256 [encoded], embedding plan SHA-256 [plan] (`pc-steward-exact-redeploy-v2`, `pinned-unsigned-v1` → `pinned-unsigned-v2`); the six protected scripts [hashes]; the unsigned candidate manifest SHA-256 [manifest] built from commit [commit]; one UAC prompt for Windows PowerShell; creation of `C:\Program Files\PC Steward OwnerOnlyTest Execution [32 hex]`; one guarded removal of the deployment with ledger SHA-256 [ledger] and signer [thumbprint], including its three owned CurrentUser certificate entries; one install that creates and trusts a fresh 14-day owner-only signer (I answer the expected certificate prompts myself); the independent validator requiring `pinned-unsigned-v2` and helper protocol 2; and my database at `%LOCALAPPDATA%\PCSteward` preserved at SHA-256 [db]. No launch, scan, update, retry, cleanup or anything else.

**Approval 2 — exact-artifact test (template):**
> I approve exactly one run of the ADR-025 exact-artifact test in revision [R] of the Task 50 activation host-session plan, using [procedure/probe, SHA-256], against the v2 deployment with installed manifest SHA-256 [installed] and signer [thumbprint]: one normal non-elevated launch of `C:\Program Files\PC Steward\PCSteward.Desktop.exe`; [the exact probe action]; exactly one UAC prompt for `PCSteward.ElevatedHelper.exe` signed by `CN=PC Steward Owner-Only Test [GUID]`, which I answer myself; read-only access evaluation of `C:\ProgramData\chocolatey`; then a normal close. No Chocolatey process, no package change, no scan, no source refresh, no retry, nothing else.

**Approval 3 — confirming evidence run, Default (complete):**
> I approve, on my Windows 11 PC as my normal non-elevated user, in one Windows PowerShell 5.1 window: blocks H1 and H1b exactly as written in revision 2.2 of the Task 50 host-evidence plan, reproduced in §4 of the activation host-session plan (creating `%TEMP%\pcsteward-evidence-task50`, copying `Task50-HostEvidence.ps1`, requiring SHA-256 `0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4`, and a parse-only check), with block H1-X only after `STOP: hash differs`, an error in H1 after it created the directory, or `STOP: parse errors`; block H2 exactly once (`-Mode Run -Cases Default`: read-only Chocolatey file reads for O5, the synthetic A-Q1 launches and the A2-L3 System32 analogue probes), as the third run, to confirm `matchesPinnedRelease`; H5; and, after I have shared the evidence, block H6 exactly once. No UAC, no PC Steward files, no running of Chocolatey, no retries, nothing else.

**Approval 4 — AQ1UAC / runas, H3 (complete; only together with approval 3):**
> I also approve block H3 exactly once, after H2 and before the cleanup, in the same window and with the same script (SHA-256 `0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4`): one UAC prompt for the synthetic `aq1-managed.exe` in its run directory, which I will answer myself. No other elevation.

## 10. Explicitly NOT authorized by this plan or by any approval above

- The activation code change (`HasSupportedMode`, the allow-list or the architecture pins), merging, pushing, tagging or publishing.
- Running `choco.exe`, any Chocolatey script or shim; changing Chocolatey files, configuration, ACLs or packages; deleting anything under `ProgramData\chocolatey`.
- Any PC Steward launch, scan, source refresh or update outside a filled and approved approval 2; H4; `-Mode SelfCheck` on the host; extra runs, retries or other `-Cases` values.
- Supplying, importing or exporting any certificate, key or password; production signing; changing trust stores beyond what the approved installer itself does.
- Registry, ACL, policy, service or scheduled-task changes; installing an SDK, runtime, package or module; network access by any step.
- Running the uncommitted Task 48 or Task 49 callers or wrappers, or any script whose bytes do not match an approved pin.
- Execution of any step by the agent.

## 11. Gap list (must be built and independently reviewed before Parts B and C can be approved)

| ID | Gap | Needed |
|---|---|---|
| G1 | The committed template refuses an existing `%LOCALAPPDATA%\PCSteward`, before and after. The host has a live database (Task 49 hash `CEB917F4…6B99`, possibly changed since by app use) | A reviewed template or plan-schema change allowing a data-present redeploy with database hash preservation, no WAL/SHM and no process; Windows CI fixtures |
| G2 | The caller, encoded-command generator and CheckOnly are uncommitted. The Task 49 defects (the 5.1 `pins` serialization; the result ACL with 3 entries versus 4) were fixed only in ignored artifacts | Commit, review and Windows-CI-test them, including the 19-scenario wrapper and result-ACL fixtures; owner-run only |
| G3 | No committed builder for the v2 unsigned candidate. Task 48's candidate was hand-assembled: the ScheduledHost RID prerequisite, desktop publish, XBF/PRI resource build, helper publish, then the manifest | A reviewed builder that emits the `[{bytes,path,sha256}]` manifest with the root capability file, built once and pinned (no deterministic-build claim) |
| G4 | No committed database-preservation tooling (Task 49's copy and integrity check were local) | A reviewed read-only procedure or script, run on the copy only |
| G5 | **No exact-artifact test exists**, and the non-admitted route plus helper peer pinning mean the real desktop cannot drive the signed helper today | Design note with the §6 evidence list, an owner decision on the approach (one that keeps `HasSupportedMode` unchanged), implementation, review and CI Design drafted: [exact-artifact test design](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) recommends approach (A), a read-only qualification probe; revision 2 applies the independent review ("Sound with changes"); awaiting the owner decisions 1–3 in its §6 |
| G6 | The build commit is unchosen. The §8.6 gates (client-route workflow test; Windows end-to-end B/C/D/Launch) are open; whether the Task 73 UI is included needs a merge decision | Owner choice of the commit; Windows CI green on it |
| G7 | Activation changes the desktop, so a later redeploy produces new signed bytes and a new signer | Owner decision: evidence carries over only if the unsigned helper image and binding set are byte-identical (compared and recorded); otherwise Part C is re-run |
| G8 | The current signer expires 2026-11-13T20:18:09Z; after that, B3/B4 fail closed | Complete G1–G4 and G6 and run B before then, or review a defined expired-signer path (for example a separately approved removal, then a fresh install) |
| G9 | No Chocolatey-managed target package; the release list has only 2.7.5, so neither route can launch a real upgrade | Owner decision on whether activation may proceed with the A-Q1 real-launch residuals open, or a target or VM run first |

## 12. Residual limits

- Part A shows the current Chocolatey state only. A later change to Chocolatey makes the route refuse again (fail closed).
- AQ1UAC uses a synthetic image. It does not show the signed apphost or its closure. That is Part C.
- The trusted owner-account/session assumption from Task 48 still applies. A same-user compromise of the signer key or the CurrentUser trust stores is out of scope.
