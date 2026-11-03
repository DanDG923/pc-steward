# Task 50 — host-evidence test plan for O5, A-Q1 and A2-L3 (revision 2.2)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03`, `TM-04`, `TM-07`, `TM-13`.

Status: **revision 2.2, ready for owner approval.** The probe script is unchanged since revision 2.1 (`e133d2b`). Review 18 (a fresh delta re-review of `e133d2b`) closed review 17's M2 and M3 and found no host-safety defect in the script; its H1-guard and wording findings are fixed below without touching the script, so the Final SHA-256 is unchanged. Revision 1 (`18a020f`, script SHA-256 `3babbbb5…233d`) was reviewed in review 15, which returned CONDITIONAL with no host-safety defect. This revision addresses every finding from that review. This file authorizes nothing by itself. Host actions happen only under the exact owner approval in §10, which binds the Final SHA-256 recorded in §4. No host action has happened.

## Changes in revision 2.2 (review 18; plan text only, script bytes unchanged)

| Review 18 finding | Severity | Change |
|---|---|---|
| The H1 guard only works if the block runs as one unit. Windows 11's PSReadLine 2.0 has no bracketed paste, so a multi-line paste runs line by line: after the `throw`, Copy-Item and the hash still ran into a pre-existing directory, and H1-X was an unconditional `Remove-Item` | Medium | Block H1 is now one `if/else` statement on one line, plus a one-line preamble. `$created` becomes `$true` only after `New-Item -ErrorAction Stop` succeeds. H1 compares the hash itself against the §4 value and prints `HASH-OK` or `STOP: hash differs`. H1b runs only if `$created -and $hashOk`. H1-X deletes only if `$created`; otherwise it prints `STOP: not created by H1; delete nothing`. Each block is a single line, so line-by-line paste cannot split it. |
| The rule that H5/H6 stay authorized after a STOP covered only H2 | Low | It now covers any STOP in H2, H3 or H4 (§5 preamble). |
| §12 told the owner to "run H6 again", which contradicts H6 "exactly once" and the no-retries rule | Low | §12 now says stop and report; a second H6 needs a separate approval. |
| The §5 preamble still mentioned a Remote Control route | Low | Replaced with "The owner runs every step; the agent runs nothing on the host." |

## Changes in revision 2.1 (review 17)

| Review 17 finding | Severity | Change |
|---|---|---|
| H1-X could delete a pre-existing directory | Medium | H1-X runs only if H1 created the directory and the hash differs, or H1b reports parse errors; if the directory already existed, stop and delete nothing (§5 table, block header, §10). |
| Listing/attribute failures recorded wrapper code 5377 | Medium | Script uses `GetBaseException().HResult` at all seven sites, so the real Windows error is recorded; §12 notes this. Windows CI re-rehearsed. |
| Appendix described the pre-`2563a7c` script | Medium | Appendix A regenerated from the final script. It includes the process-scope `PSModulePath` guard added after CI caught `CommandNotFoundException` at `Get-FileHash` under 5.1 launched from pwsh 7. |
| Lows | Low | H5/H6 stay authorized after a STOP; §7 escaping text corrected; implementation commit recorded in §4; agent/Remote Control execution excluded in §11; suspended-child note in §12. |

## Changes from revision 1

| Review 15 finding | Severity | Change in revision 2 |
|---|---|---|
| Not frozen for approval (draft header, LF draft hash in §4/§10) | High | Script header is now neutral and names revision 2. §4 has one placeholder, the Final SHA-256, which is the **CRLF** file hash the integrator computes after the fresh re-review. §10 refers to that §4 value. The Appendix-A LF hash has been removed. |
| No parse gate before the run | High | New H1b: a parse-only `[System.Management.Automation.Language.Parser]::ParseFile` check, run in the H1 window with no execution. Any parse error stops before H2. |
| R: rehearse 5.1 parse and compile on CI so the single approved run is not wasted | High | New `-Mode SelfCheck -ScratchDirectory <empty dir>`. It reports the host version and refuses anything other than Windows PowerShell 5.1. It runs the same `Import-T50` Add-Type call as Run, plus the two synthetic CodeDom builds into the scratch directory, then prints `SELFCHECK-OK`. It does no location, token, Chocolatey or PC Steward reads, no launches, no probes, and writes nothing outside the scratch directory. New `HostEvidenceScriptRehearsalTests.cs` (Security fixture tests): a portable ASCII/CRLF/`-text` byte check, plus two Windows-only tests (5.1 parser gives 0 errors; SelfCheck gives exit 0 and `SELFCHECK-OK`). |
| Volume serial in `rootIdentity` and `heldFileId` | Medium (privacy) | `rootIdentity` becomes `rootIdentityRead` (a boolean). `heldFileId` becomes `heldFileIdRead` plus `heldFileId128` (the 32-hex file ID without the volume serial). Full identities are compared in memory only. §7 is corrected. |
| Recursive `Get-ChildItem` in Cleanup | Medium | Cleanup now walks manually at a fixed depth: evidence directory → `run-*` → `tmp`. Each level is listed non-recursively and every entry is checked for reparse points before descending. Only allowlisted names are accepted at each depth, so anything deeper or unexpected is refused before `Remove-Item`. |
| Console redaction count doubled | Low | `$script:redactions` is reset before the second `Redact` pass. |
| PS 5.1 `ConvertTo-Json` escapes `<` and `>` | Low | Noted in §7: path tokens appear as `<RUN>`. JSON readers decode them. |
| Volume equality was `true` when both FileId lookups failed | Low | It is now `"unknown"` when either lookup fails. |
| H2/H6 used `cmd` `%TEMP%` | Low | H2, H3, H4 and H6 run from the H1 PowerShell window using `$d`, which is built from `GetTempPath`. |
| Transient Defender sharing violation could spoil `controlAfterRelease` | Low | The control DELETE probe is retried at most 3 times, 500 ms apart. The attempt count is recorded. |
| Unbounded csc/CodeDom wait; Read-Tree deadline granularity | Low | `Build-Synthetic` runs in its own runspace with `WaitOne` bounded by min(60 s, remaining deadline), and stops with `synthetic-build-deadline` on timeout. `Assert-Deadline` now runs before and after `Add-Type`, and before every entry in `Read-Tree`. |
| `\|` in the H1 Markdown table | Low | Commands moved to fenced code blocks (§5). |
| O5 binding scope unstated | Low | §1 and §12: the O5 evidence binds the eventual desktop file-first reader **only** while that reader reads within the probed O5 file set. |

## 0. Source pins (repository HEAD `47ec55549990282927b50f9ad0deb9e94418b7cb`; unchanged since revision 1)

| Source | SHA-256 (LF checkout) | Used for |
|---|---|---|
| `docs/review/TASK_50_ADAPTER_GATES.md` | `3e3063f3b1f4bc95c9ea0e52db41e30b246624e77a408aefcccf7f15633fa71b` | A-Q1 (A.4 step 3, A.7 "Peer identity", A.9); A2-L3 and A2-L5 (§R); binding set (A.3) |
| `docs/review/TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md` | `612d0a498a1db9717fbe64206952b04f8601fe253bbec1a939d9ce40479eba45` | O5 (§4.3, §9 O5 and its contingency, §10 N5) |
| `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs` | `b6e5c820ab531664f6c13f4b9489402882ccd0b891f9767c3355112c47e4cf2f` | `ProtectedLaunchLayout.TryProbeRights` and its three rights lists, which the probe copies exactly (re-checked for revision 2) |
| `src/PCSteward.Provider.Chocolatey/ChocolateyPostUpdateClientCheck.cs` | `43a4d8ef1ed43553bcf852cffbcdaa6ed8c559d9cdf5cf50fcd13efb77dda0d7` | The `ChocolateyClientStateData` fields |
| `docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json` | `68f0da92026905a14cb49e00f8b208202d107b7c2b303457842ebb6b458cd16b` | The 56-entry code list embedded in the script |
| `tests/PCSteward.Security.FixtureTests/DeploymentCapabilityReaderTests.cs` (`TrustProbeStillReportsAccessDeniedWhileTheShareReadHoldIsOpen`) | — | Existing Windows-CI A2-L3 evidence on a deny-ACE synthetic file. It complements A2L3S/A2L3P but is not owner-host evidence. |
| `docs/requirements/V2_HOST_TEST_PLANS.md`, `V2_TASK_CONTRACT.md`, `AGENTS.md` | — | Reviewable-record, exact-approval and no-implied-retry rules |

## 1. Questions and what each step proves

| ID | Spec question | Probe cases | What this evidence can show | What it cannot show |
|---|---|---|---|---|
| **O5** | Can the **non-elevated** desktop read every input of its own file-first postcheck (`ChocolateyClientStateData`)? | `O5` | That a standard-user token can read each input as Chocolatey is installed **now**: the root identity, `choco.exe`, all 56 pinned code-list files and their FileIdInfo, any unlisted `helpers`/`tools` files, the `extensions`/`hooks` code trees, the complete `lib` discovery set, the `chocolatey` and `chocolatey.extension` registrations, and the `.chocolatey` store markers for those two identities. | Readability of files that a **future** elevated upgrade writes. These normally inherit their ACLs, but that is unproven. Eligibility of the installed client. Any input **outside** the probed set (see the binding rule below). |
| **A-Q1** | Does a `FileShare.Read`-only hold, plus the FileIdInfo check, still let Windows map and run the image? Does the peer main-module FileIdInfo equal the held handle's? | `AQ1N` (native PE), `AQ1M` (.NET Framework loader with a lazily loaded DLL), `AQ1C` (coreclr through the installed .NET 10 `dotnet exec`). Optional: `AQ1UAC` (`ShellExecuteEx runas`). | Loader compatibility with the hold; peer FileIdInfo equality; FileIdInfo stability; DELETE and WRITE opens fail with 32 while held and succeed after release. | The real signed PC Steward apphost and its closure (that needs the ADR-025 exact-artifact host test). |
| **A2-L3** | Do the `TryProbeRights` write probes still return ACCESS_DENIED (5), not a sharing violation (32), while read-share holds exist? | `A2L3S`: a System32 protected-file analogue plus a positive control on a file the user owns. Optional: `A2L3P`, read-only holds on the installed binding-set files. | Whether NTFS evaluates the access check before the share check for each right the transport probes, on the owner's host. | — |

**O5 binding rule.** O5 evidence binds the eventual desktop file-first reader only while that reader reads **within the probed O5 file set**: the fixed `CommonApplicationData\chocolatey` root, the 56 pinned paths, the `helpers`, `tools`, `extensions` and `hooks` trees, the `lib` root and the top-level `*.nupkg` files of its immediate directories, all top-level files of `lib\chocolatey` and `lib\chocolatey.extension`, and the top-level files of `.chocolatey\chocolatey[.extension].<digit>*`. The reads use the same access shape: GENERIC_READ with FILE_SHARE_READ (FILE_READ_ATTRIBUTES for the root identity), and no elevation. A reader that adds another input, such as `config\chocolatey.config` or a different root derivation, is **not** covered. Its implementation review must either show that the input lies inside this set or obtain new evidence.

All synthetic images are self-built in the run directory, or a **copy** of `%SystemRoot%\System32\whoami.exe`. No PC Steward or Chocolatey binary is executed, opened for execution, or modified. `choco.exe` is only opened for reading.

## 2. Preconditions checklist (all must be true before approval is used)

- [ ] **H0 done:** the revision-2 script is committed as `scripts/evidence/Task50-HostEvidence.ps1` (CRLF, ASCII, `-text`). `HostEvidenceScriptRehearsalTests` passed on Windows CI against that exact commit (5.1 parse with 0 errors, `SELFCHECK-OK`). The fresh independent re-review passed. The integrator has entered the Final SHA-256 in §4.
- [ ] The owner is on the Windows 11 x64 host, logged in interactively, in a **non-elevated** Windows PowerShell 5.1 window (`%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`; the title bar does not say "Administrator"). The script stops on an elevated or full-administrator token.
- [ ] The installed PC Steward `pinned-unsigned-v1` deployment is left as it is. The Default run does not need PC Steward closed. The optional `PcStewardHolds` run needs every `PCSteward*` process closed; the script checks this.
- [ ] No Chocolatey operation is in progress. If a `choco` process exists, the script skips O5.
- [ ] `GetTempPath` is a local path with no reparse ancestor (not OneDrive) and at least 100 MB free. The script checks for a reparse ancestor.
- [ ] `%TEMP%\pcsteward-evidence-task50` does **not** exist yet.
- [ ] About 5 minutes are available. For the optional Uac run, the owner is present to answer exactly one UAC prompt for `aq1-managed.exe` (unknown publisher).
- [ ] The owner accepts the incidental OS effects in §8.
- [ ] The exact §10 approval wording has been given for each run to be performed.

## 3. Bounds

| Run | Enforced overall deadline | Expected | Child wait | Holds |
|---|---|---|---|---|
| `-Mode Run -Cases Default` | 240 s | under 90 s | 15 s per synthetic child. After that, the script terminates its **own** non-elevated synthetic child and waits 5 s. | Synthetic files; one System32 file for under 1 s |
| `-Mode Run -Cases Uac` (optional) | 300 s; `ShellExecuteEx` blocks while the UAC prompt is open | under 30 s | 15 s. An elevated child is never terminated. | Synthetic files |
| `-Mode Run -Cases PcStewardHolds` (optional) | 60 s | under 5 s | — | Binding-set files, read-only; `holdMs` recorded; over 10 s is flagged |
| `-Mode Cleanup` | 60 s | under 5 s | — | — |
| `-Mode SelfCheck` (CI only) | 150 s | under 30 s | — | — |

Each synthetic CodeDom build waits at most min(60 s, remaining deadline) and stops with `synthetic-build-deadline` on timeout. A csc.exe orphaned by a timeout can only finish writing inside `<RUN>\tmp` or `<RUN>`. Read bounds follow Profile V2: at most 64 MiB per file; 32,768 tree entries; depth 32; 1,024 `lib` directories; 2,048 `nupkg` files (64 KiB tail reads, full reads only for the two client registrations); 4,096 store directories; 4 KiB marker reads. A breach is recorded as `over-bound`, which gives INCONCLUSIVE. Bounds are never widened.

## 4. Artifact pinning

| Item | Value |
|---|---|
| Script path | `scripts/evidence/Task50-HostEvidence.ps1`. Pure ASCII, **CRLF**, `.gitattributes` `-text` (enforced by `HostEvidenceScriptRehearsalTests.ScriptIsAsciiCrlfAndCheckedInWithoutNewlineConversion`). |
| H0 implementation commit | `e133d2b0ef7d8fd02c84509692aa71b4f48a999d` (`e133d2b`). Windows CI run 81 on this exact commit: Architecture 44, Domain 167, Application 883, Security 481 passed, 0 failed, 0 skipped, including the 5.1 parse (0 errors) and SelfCheck (`SELFCHECK-OK`) rehearsals. Review 18 confirmed the committed bytes. |
| **Final SHA-256** (the only approvable value) | **`0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4`** |
| Appendix A | The revision-2 script as LF text, for review reading. Its LF hash is **not** approvable. Only the committed CRLF bytes above are. |
| Self-binding | The script records `Get-FileHash` of its own file as `scriptSha256`. A mismatch with the Final SHA-256 invalidates the evidence. |
| Validation already done (Linux, no Run/Cleanup) | The PowerShell 7 parser reports 0 errors. The three embedded C# sources compile at `-langversion:5` against .NET 10 reference assemblies. SelfCheck under pwsh 7 refuses with `SELFCHECK-FAIL: windows-powershell-5.1-required`, exit 2, and writes nothing. In a **scratch copy with only the version gate disabled**, SelfCheck under pwsh 7 printed `SELFCHECK-RELEASE-ENTRIES: 56`, passed `Import-T50` (Add-Type) and the rights-list check, and then reported `synthetic-build-failed` from the bounded runspace, because CodeDom is unsupported on .NET Core. That failure is expected and shows the runspace returns without hanging. `HostEvidenceScriptRehearsalTests.cs` builds with 0 warnings and 0 errors in an isolated project with the repository's `Directory.Build.props`, `.editorconfig` and central package versions. |
| Windows CI rehearsal (H0) | `HostEvidenceScriptRehearsalTests`: Windows PowerShell 5.1 `Parser.ParseFile` gives 0 errors, and `-Mode SelfCheck` gives exit 0 and `SELFCHECK-OK`, with outputs only under the GUID temp directory's `scratch`. |

**H0 obligations (repository and CI; no owner host).** Integrate the script and test, then convert both to the repository's line-ending conventions. Static check: no `Invoke-Expression`, `Start-Process`, registry provider or cmdlet, `Set-Acl`/`icacls`, or web/socket API. `Remove-Item` appears only in Cleanup, after the fixed-depth walk. Get Windows CI green on the exact commit, then the fresh independent re-review. Then the integrator records the Final SHA-256.

## 5. Exact step list

Every step runs as the owner's standard user. The owner runs every step; the agent runs nothing on the host. "Stop" means: run no further steps, do not retry, and report the exact console token. After any `STOPPED:`/`STOP:` result or error in H2, H3 or H4 (including a declined or unexpected UAC prompt and `NOT-RUN-PCSTEWARD-RUNNING`), H5 (share evidence, if written) and H6 (cleanup) **remain authorized**; paste only the STOP token, never other console text, because pre-`EVIDENCE` console errors can contain paths.

| Step | Command | Expected | Pass / fail | Recorded | Stop conditions | Cleanup |
|---|---|---|---|---|---|---|
| **H1** Place and verify | Block H1 (two single-line statements) | Hash line, then `HASH-OK` | `HASH-OK` / anything else | Hash | `STOP: evidence directory already exists` (delete nothing); `STOP: hash differs`; any error | On `STOP: hash differs` or an error after the directory was created: run block H1-X (it deletes only if `$created`) and stop. On `STOP: evidence directory already exists`: stop and delete nothing. |
| **H1b** Parse-only gate | Block H1b (same window) | `PARSE-OK` | `PARSE-OK` / anything else | Line | `STOP: parse errors n`: run block H1-X and stop. `STOP: H1 did not pass`: stop (run H1-X only if H1 printed `STOP: hash differs`) | As H1 |
| **H2** Default run (once) | Block H2 (same window) | `EVIDENCE: <TEMP>\pcsteward-evidence-task50\run-…\evidence.json`, `REDACTIONS: n`, then one `VERDICT …` line each for O5, AQ1N, AQ1M, AQ1C and A2L3S | Per case (§6) | `evidence.json` (§7) | Any `STOP:`/`STOPPED:` line; any error before `EVIDENCE:` | None until H5 |
| **H3** *(optional; separate approval)* Uac run (once) | Block H3 | One UAC prompt for `aq1-managed.exe` in `…\pcsteward-evidence-task50\run-…`; `VERDICT AQ1UAC: …` | §6 | Second JSON | A second prompt, or a prompt naming any other program: answer **No** and stop. A decline is `NOT-RUN-UAC-DECLINED`, with no retry. | None until H5 |
| **H4** *(optional; separate approval)* PcStewardHolds run (once) | Block H4, with PC Steward closed | `VERDICT A2L3P: …` | §6 | Third JSON | `NOT-RUN-PCSTEWARD-RUNNING`; any `FAIL-UNPROTECTED` stops everything except H5/H6 and is reported as a security finding | None until H5 |
| **H5** Hand over evidence | `notepad (Join-Path $d '<run-name>\evidence.json')` for each run | Content matches §7 and has no private text | Sanitized / not | — | Unredacted private text: do not share it; continue with H6 and report | — |
| **H6** Cleanup (once, last) | Block H6 (same window) | `CLEANUP-COMPLETE: <TEMP>\pcsteward-evidence-task50 removed` | Directory gone | Line | Cleanup refuses: leave everything and report | This step is the cleanup |

**Block H1** (in a new non-elevated Windows PowerShell 5.1 window; `<CHECKOUT>` is the owner's existing clone at the H0 commit). Each line is one complete statement, so pasting line by line cannot split a guard from the work it protects. Do not re-wrap the lines:

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'; $created = $false; $hashOk = $false; $expected = '0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4'
if (Test-Path -LiteralPath $d) { 'STOP: evidence directory already exists' } else { New-Item -ItemType Directory -Path $d -ErrorAction Stop | Out-Null; $created = $true; Copy-Item -LiteralPath '<CHECKOUT>\scripts\evidence\Task50-HostEvidence.ps1' -Destination $d -ErrorAction Stop; $h = (Get-FileHash -LiteralPath (Join-Path $d 'Task50-HostEvidence.ps1') -Algorithm SHA256 -ErrorAction Stop).Hash; $h; if ($h -eq $expected) { $hashOk = $true; 'HASH-OK' } else { 'STOP: hash differs' } }
```

`$expected` is the §4 Final SHA-256. A terminating error inside the `else` ends the whole statement: if `New-Item` fails, `$created` stays `$false`; if a later step fails, `$hashOk` stays `$false`.

**Block H1b** (parse only; nothing executes; one line):

```powershell
if ($created -and $hashOk) { $tokens = $null; $parseErrors = $null; [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $d 'Task50-HostEvidence.ps1'), [ref]$tokens, [ref]$parseErrors); if ($parseErrors.Count -ne 0) { 'STOP: parse errors ' + $parseErrors.Count } else { 'PARSE-OK' } } else { 'STOP: H1 did not pass' }
```

**Block H1-X** (only after `STOP: hash differs`, an error in H1 after the directory was created, or `STOP: parse errors n`; one line). It deletes nothing unless H1 created the directory in this window:

```powershell
if ($created) { Remove-Item -LiteralPath $d -Recurse -Force; 'H1X-REMOVED' } else { 'STOP: not created by H1; delete nothing' }
```

**Block H2** (if the window was closed after H1b printed `PARSE-OK`, first re-run only the first line of block H1 to set `$d` again; never re-run its second line):

```powershell
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Default
```

**Block H3** (optional):

```powershell
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Uac
```

**Block H4** (optional):

```powershell
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases PcStewardHolds
```

**Block H6** (last; only after H5):

```powershell
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Cleanup
```

The approved count is exact: H2 once, plus H3 once and H4 once if separately approved, then H6 once. A stopped or inconclusive run is not re-run without a new exact approval. `-Mode SelfCheck` is a CI rehearsal and is **not** part of the host steps.

## 6. Case details

### O5 — Chocolatey file-first postcheck readability (Default)

The root is `[Environment]::GetFolderPath('CommonApplicationData')\chocolatey`, which is fixed and native-derived. `%ChocolateyInstall%` is only compared, and the result is recorded as booleans.

| Sub-step | Action (read-only; GENERIC_READ + FILE_SHARE_READ, OPEN_REPARSE_POINT) | Expected on a healthy admin install | Problem |
|---|---|---|---|
| Presence | Open `choco.exe` with FILE_READ_ATTRIBUTES. Error 2 or 3 gives `not-installed`, and O5 ends with no install and no further reads. | Present | — |
| Concurrency | `Get-Process -Name choco`. A match gives `skipped-choco-running`. | Absent | — |
| Root identity | FileIdInfo of the root (backup semantics). Recorded as `rootIdentityRead` only. Reparse attribute checked. | Readable; no reparse | Error code. A reparse point is a route finding. |
| Engine and code list | Each of the 56 pinned paths: open, FileIdInfo, full SHA-256, length, compared with the pin | Readable; `matchedPin` 56 if 2.7.5 is installed | An open, file-id or read failure. A mismatch is a route finding, **not** an O5 failure. |
| Unlisted code | Bounded walk of `helpers` and `tools` (reparse points not followed; deadline checked per entry); full read of any file outside the pin | 0 unlisted | An unreadable unlisted file. Existence of an unlisted file is a route finding. |
| Extension and hook code | Walk `extensions` and `hooks`; full read of every file. Absence is allowed (`MayBeAbsent`). Names are not recorded. | Absent or readable | A list or open failure |
| Registration | List `lib` (top level) and each immediate directory's top-level `*.nupkg`. 64 KiB tail reads of other packages, whose names are never recorded. Full reads of the top-level files in `lib\chocolatey` and `lib\chocolatey.extension`. `chocolatey.nupkg` is compared with the release asset digest. | Listed; readable; `chocolatey` present; extension usually absent | A list, attribute or open failure |
| Store markers | List `.chocolatey`. For `chocolatey[.extension].<digit>*` directories, 4 KiB tail reads of top-level files | Readable | A list or open failure |

Verdict: **PASS-READABLE** when there is no problem. **FAIL-UNREADABLE** when any problem carries Win32 error 5. Otherwise **INCONCLUSIVE**. `NOT-INSTALLED` and `SKIPPED-CHOCO-RUNNING` are recorded as given. Also recorded: `matchesPinnedRelease`, `routeFindings` and `elapsedMs`.

### AQ1N — native image under a read-share hold (Default)

1. Copy `whoami.exe` to `<RUN>\aq1-native.exe`. The image directory must contain only allowlisted names (no planted DLL).
2. **Baseline, no hold.** `CreateProcessW` with an exact application path, `CREATE_SUSPENDED | CREATE_NO_WINDOW`, no inherited handles and argument `/?`. Read the peer FileIdInfo (`QueryFullProcessImageNameW`, then open with GENERIC_READ and FILE_SHARE_READ, then `FileIdInfo`). Then `ResumeThread`, wait up to 15 s, and record the exit code.
3. **Held.** Open the image with GENERIC_READ and FILE_SHARE_READ only (the `HeldHelperBinding` shape). Record `heldFileIdRead`/`heldFileId128` and whether a fresh open gives the same identity. Probe DELETE and FILE_WRITE_DATA opens (open, then close at once). Repeat the step-2 launch, peer comparison, resume and wait, then the post-exit identity and replacement probes. Release the hold. Control: a DELETE open, retried at most 3 times 500 ms apart if it gets 32, should return a handle.

Expected and PASS: the held launch succeeds with the baseline exit code; `peerEqualsHeld = true`; FileIdInfo is stable; replacement probes give `DELETE=32,WRITE_DATA=32` before launch and after exit; the control gives `DELETE=handle;attempts=1..3`.

### AQ1M — .NET Framework image and a lazily loaded held DLL (Default)

`aq1-managed.exe` and `aq1-lazy.dll` are built by the bounded runspace CodeDom build (`GenerateInMemory = false`; never loaded into the probe). The exe sleeps 1.5 s, calls `Assembly.LoadFrom` on the DLL, and exits **42** on success (43 for a wrong value, 44 for a load failure) after another 1.5 s. Baseline and held runs follow AQ1N with plain `CreateProcess`. Both files are held. The peer check runs at about 0.5 s, before the lazy load. PASS requires exit 42 in both runs plus the AQ1N conditions.

### AQ1C — coreclr loader (Default; only if `%ProgramFiles%\dotnet\dotnet.exe` and `shared\Microsoft.NETCore.App\10.0.*` exist)

The script writes `<RUN>\aq1-managed.runtimeconfig.json` (Microsoft.NETCore.App 10.0.0, `LatestPatch`) and runs `dotnet.exe exec --runtimeconfig <it> <RUN>\aq1-managed.exe`, first without a hold, then with the exe and DLL held. There is no peer comparison, because the peer is `dotnet.exe`. PASS requires exit 42 in both runs plus the hold conditions. A baseline exit other than 42 gives `INCONCLUSIVE-HARNESS`. An absent runtime gives `NOT-RUN-NO-DOTNET10-RUNTIME`. There is no SDK command, restore or network access.

### A2L3S — TryProbeRights under holds, analogue (Default)

1. **Unheld.** An exact `TryProbeRights` mirror (share RWD, OPEN_EXISTING, OPEN_REPARSE_POINT, plus BACKUP for directories): `<SYSTEM32>\whoami.exe` with `FileModificationRights`, and `<SYSTEM32>` with `AncestorReplacementRights`. Every right is recorded; the verdict follows the first decisive right. Expected: all `5`.
2. **Held.** The same while `<SYSTEM32>\whoami.exe` is held with GENERIC_READ and FILE_SHARE_READ (under 1 s, recorded in `systemFileHoldMs`). Expected: all `5`.
3. **Positive control.** `<RUN>\a2l3-own.bin` (16 bytes). Unheld: `can-modify`. Held: `probe-failed-closed:32`.

Verdict: **PASS**, **FAIL-SHARING-VIOLATION**, **FAIL-UNPROTECTED**, **INCONCLUSIVE-ANALOGUE-NOT-PROTECTED** or **INCONCLUSIVE-OTHER-ERROR** (§9).

### AQ1UAC — `ShellExecuteEx runas` (optional; separately approvable)

This run builds the synthetic pair, then runs a non-elevated baseline (exit 42). It then makes exactly one `ShellExecuteExW` call with verb `runas` and `SEE_MASK_NOCLOSEPROCESS`, with both files held. The owner answers UAC. The peer FileIdInfo is compared through the returned handle. The elevated synthetic process only sleeps and loads the held DLL from the user-writable run directory. That pattern is forbidden for real binaries; it is acceptable here only because the binary is a synthetic no-op that stays held against replacement for the whole run. Expected: exit 42 and `peerEqualsHeld = true`. Error 1223 gives `NOT-RUN-UAC-DECLINED`.

### A2L3P — installed binding-set files (optional; separately approvable)

This requires no `PCSteward*` process and an existing `<PF>\PC Steward`. It records which of the 12 binding-set leaves are present (A.3 plus A2-L4 `PCSteward.Desktop.dll`; the capability file is expected to be absent on v1). It mirrors `CallerCannotModify` over every ancestor and every present leaf, first unheld, then while all present leaves are held with GENERIC_READ and FILE_SHARE_READ. No content is read, nothing is hashed, launched or modified, and the holds are then released. A2L3S rules apply. `FAIL-UNPROTECTED` is a security finding against the installed deployment.

## 7. Evidence format (sanitized JSON, UTF-8 without BOM)

One `evidence.json` file per run.

- **Paths:** reduced to the tokens `<RUN>`, `<EVIDENCE>`, `<CHOCO>`, `<SYSTEM32>`, `<PF>` and `<PROGRAMDATA>`; anything else becomes `<OTHER>`. Windows PowerShell 5.1 `ConvertTo-Json` escapes `<` and `>`, so the file contains `\u003cRUN\u003e` and similar. Any JSON parser decodes these back to `<RUN>`.
- **Redaction:** after serialization, the username, computer name, domain and SID are replaced with `<REDACTED>` (whole-token match, so hex values are unaffected). The count of the first pass is stored in `redactions`. The counter is reset before the final pass, so the console count equals that stored count.
- **Errors:** only exception type names, HRESULTs and Win32 codes are recorded, never messages.
- **Names:** non-client package names and extension or hook file names are never recorded.
- **Identities:** **no volume serial appears anywhere.** O5 records `rootIdentityRead` (a boolean) and `fileIdRead` per file. A-Q1 records `heldFileIdRead` and `heldFileId128` (the 128-bit file ID of a synthetic image in the run directory, as 32 hex digits), plus the equality booleans `freshFileIdBeforeEqualsHeld`, `freshFileIdAfterEqualsHeld` and `peerEqualsHeld`. The full volume-plus-ID identities are compared only in memory. `evidenceVolumeEqualsProgramFilesVolume` is `true`, `false` or `"unknown"` (if either lookup failed).

```json
{
  "schema": "pc-steward-task50-host-evidence:1",
  "cases": "Default",
  "scriptSha256": "<64 hex; must equal the §4 Final SHA-256>",
  "runDirectory": "<EVIDENCE>\\run-20261102T101500Z-1a2b3c4d",
  "startedUtc": "2026-11-02T10:15:00Z",
  "hostFacts": { "osVersion": "10.0.26100.0", "powerShell": "5.1.26100.1", "clr": "4.0.30319.42000",
                 "tokenElevationType": 3, "tokenElevated": 0, "evidenceVolumeEqualsProgramFilesVolume": true,
                 "evidenceFileSystem": "NTFS" },
  "results": {
    "O5": { "case": "O5", "status": "run", "fixedRoot": "<PROGRAMDATA>\\chocolatey", "rootIdentityRead": true,
            "engine": { "path": "choco.exe", "ok": true, "error": 0, "length": 12022880, "fileIdRead": true, "reparse": false, "sha256": "…", "matchesPin": true },
            "codeList": { "expected": 56, "readable": 56, "matchedPin": 56, "bytesRead": 15258758, "failures": [] },
            "helpersTree": { }, "toolsTree": { }, "extensionsTree": { }, "hooksTree": { },
            "registration": { "libListed": true, "chocolatey": "present", "extension": "absent", "errorCodes": [] },
            "store": { "present": true, "clientDirectories": 1, "errorCodes": [] },
            "problems": [], "routeFindings": [], "matchesPinnedRelease": true, "elapsedMs": 0, "verdict": "PASS-READABLE" },
    "AQ1N": { "baseline": { "launcher": "CreateProcessSuspended", "held": false, "exit": "0", "peerImagePath": "<RUN>\\aq1-native.exe", "peerFileIdRead": true },
              "held": { "held": true, "heldFileIdRead": true, "heldFileId128": "<32 hex>", "freshFileIdBeforeEqualsHeld": true,
                        "replacementWhileHeldBeforeLaunch": ["aq1-native.exe:DELETE=32,WRITE_DATA=32"], "peerEqualsHeld": true, "exit": "0",
                        "freshFileIdAfterEqualsHeld": true, "replacementWhileHeldAfterExit": ["aq1-native.exe:DELETE=32,WRITE_DATA=32"],
                        "controlAfterRelease": ["aq1-native.exe:DELETE=handle;attempts=1"] },
              "verdict": "PASS" },
    "AQ1M": { }, "AQ1C": { },
    "A2L3S": { "case": "A2L3-S", "unheld": [ { "path": "<SYSTEM32>\\whoami.exe", "outcomes": ["00000002=5"], "verdict": "cannot-modify" } ],
               "held": [ ], "ownUnheld": { }, "ownHeld": { }, "positiveControl": "as-expected", "systemFileHoldMs": 3, "verdict": "PASS" }
  },
  "verdicts": { "O5": "PASS-READABLE", "AQ1N": "PASS", "AQ1M": "PASS", "AQ1C": "PASS", "A2L3S": "PASS" },
  "stop": null,
  "elapsedMs": 41234,
  "redactions": 0
}
```

A later repository task (not authorized here) commits the pasted JSON with the run date, the Final SHA-256 and the interpretation, and updates `docs/STATUS.md` and `TRACEABILITY.md`.

## 8. Incidental effects disclosed (not written by the script; inherent to running PowerShell and executables)

- Windows PowerShell housekeeping under `%LOCALAPPDATA%\Microsoft\Windows\PowerShell` (startup profile data, module analysis cache). PowerShell event-log entries, and script-block logging of the script text if the host enables it. The script contains no secrets.
- `csc.exe`/`cvtres.exe` run for the synthetic builds. Their scratch files go to `<RUN>\tmp`.
- Prefetch, AmCache and BAM execution records for `aq1-native.exe`, `aq1-managed.exe`, `csc.exe` and `dotnet.exe`. Defender may scan the new binaries, possibly with an OS-initiated cloud lookup. The script makes no network call.
- A FILE_SHARE_READ hold on `<SYSTEM32>\whoami.exe` for under 1 s. It blocks only writers and deleters of that file during the window.
- The Uac run produces a UAC consent event. The PcStewardHolds run blocks writers to the installed PC Steward files for a few seconds; none runs while PC Steward is closed and no redeploy is happening.

## 9. Interpretation table (outcome → spec decision)

| Case | Outcome | Decision under the reviewed specs |
|---|---|---|
| O5 | **PASS-READABLE** | O5 native evidence is satisfied **for this host and the current Chocolatey state** (fact extension §10 N5), subject to the §1 binding rule. The O5 contingency is not triggered. If `matchesPinnedRelease = false` or `routeFindings` is non-empty, the route would still refuse on this host (an eligibility fact). No Chocolatey change is authorized. |
| O5 | **FAIL-UNREADABLE** | Every update would end `ClientUnverifiable`, giving the outcome **Unverified** (§4.3). The §9 owner contingency is triggered: **(a)** accept the route as permanently unusable, or **(b)** approve a helper-side, negative-only client-check field, which needs a new protocol version, a spec and a review. Changing Chocolatey ACLs is not an option. There is no adapter admission until the owner decides. |
| O5 | **INCONCLUSIVE** | O5 stays open and admission is blocked. Diagnose from the recorded codes. A re-run needs a new approval. A persistent sharing violation is treated as FAIL for the design (safe, because the result is Unverifiable, but the route is unusable). |
| O5 | **NOT-INSTALLED** | O5 cannot be tested here, and the route cannot be admitted on this host on evidence. No install follows. Any install would be a separate, out-of-scope approval. |
| O5 | **SKIPPED-CHOCO-RUNNING** | Nothing is learned. Re-run only under a new approval. |
| A-Q1 | AQ1N, AQ1M and AQ1C all **PASS** | The A.4 step-3 hold design is viable for direct process creation with the native, .NET Framework and coreclr loaders. runas is closed only by an AQ1UAC PASS; otherwise it remains an inference. Admission still needs the ADR-025 exact-artifact host test. |
| A-Q1 | **FAIL-LAUNCH:n** / **FAIL-EXIT:n** | The hold is incompatible with that loader. Use the **spec fallback** (A.4, A2-L5): no hold across launch; ACL plus `VerifyPeer`, then re-hash the binding set and run the FileIdInfo check after peer verification and before writing the request. The reference identity is the FileIdInfo captured before launch; the fallback detects replacement only. Record a spec amendment for review. |
| A-Q1 | **FAIL-PEER-IDENTITY** / **FAIL-IDENTITY-UNSTABLE** | FileIdInfo equality (A-M1) is unusable as the binding on this host. The fallback is limited to ACL plus `VerifyPeer` plus re-hash, and A.4 step 3.4 needs re-review. |
| A-Q1 | **FAIL-HOLD-NOT-EXCLUSIVE** | A `FileShare.Read` hold gives no replacement protection. Use the fallback, and correct the A.7 lazy-load claim. |
| A-Q1 | **INCONCLUSIVE-*** | A-Q1 stays open and admission is blocked. Causes such as AppLocker/WDAC or a Defender quarantine are recorded. No policy change and no retry without approval. |
| A-Q1 | **AQ1C NOT-RUN** | coreclr is unproven. The owner either accepts the exact-artifact test as the coreclr evidence, or the fallback is adopted for the managed closure. |
| A-Q1 | **AQ1UAC** PASS / NOT-RUN-UAC-DECLINED / FAIL | PASS closes runas for a synthetic image. Declined: no evidence and no retry. FAIL: the fallback applies, because runas is the helper's only launch path. |
| A2-L3 | **PASS** | `TryHold` followed by `validatePreLaunchTrust` (A.4 step 3.1–3.2) is acceptable. This closes §R A2-L3 for this host (as analogue evidence unless A2L3P also passes). It is consistent with the existing Windows-CI deny-ACE test. |
| A2-L3 | **FAIL-SHARING-VIOLATION** | The probes fail closed (`LaunchRefusedBeforeElevation` → NotStarted, no UAC), so every launch would be refused while held: safe but unusable. A spec amendment and review are needed, for example running trust validation before `TryHold` and then re-verifying FileIdInfo and hashes under the hold. |
| A2-L3 | **FAIL-UNPROTECTED** | Stop. For A2L3P this is a High security finding against the deployment. Report it; take no further host action. |
| A2-L3 | **INCONCLUSIVE-ANALOGUE-NOT-PROTECTED** / **-OTHER-ERROR** | A2-L3 stays open. Consider A2L3P under its own approval. |
| A2-L3 | A2L3S PASS, A2L3P not run | Analogue evidence only. Share-access semantics are file-system-level; ownership differs (TrustedInstaller versus Administrators). The owner decides whether it is sufficient. The exact-artifact test is still required. |

## 10. Owner approval wording to request

Each approval quotes the §4 Final SHA-256 verbatim, at the position marked `[§4 Final SHA-256]`. No other hash is valid.

**Default (required for any run):**
> I approve, on my Windows 11 PC as my normal non-elevated user, in one Windows PowerShell 5.1 window: blocks H1 and H1b exactly as written in revision 2.2 of the Task 50 host-evidence plan (creating `%TEMP%\pcsteward-evidence-task50`, copying `Task50-HostEvidence.ps1`, requiring SHA-256 `[§4 Final SHA-256]`, and a parse-only check), with block H1-X only after `STOP: hash differs`, an error in H1 after it created the directory, or `STOP: parse errors` (H1-X itself deletes nothing unless H1 created the directory; never when the directory already existed); block H2 exactly once (`-Mode Run -Cases Default`: read-only Chocolatey file reads for O5, the synthetic A-Q1 native/.NET Framework/.NET 10 launches, and the A2-L3 System32 analogue probes); and, after I have shared the evidence, block H6 exactly once (`-Mode Cleanup`). No UAC, no PC Steward files, no running of Chocolatey, no retries, nothing else.

**Optional — UAC sub-case:**
> I also approve block H3 exactly once, before the cleanup, with the same script (SHA-256 `[§4 Final SHA-256]`): one UAC prompt for the synthetic `aq1-managed.exe` in its run directory, which I will answer myself. No other elevation.

**Optional — installed binding files:**
> I also approve block H4 exactly once, before the cleanup, with the same script (SHA-256 `[§4 Final SHA-256]`), while PC Steward is closed. It opens the installed PC Steward binding-set files read-only with read-share holds for a few seconds and probes their write rights, without reading content, launching or modifying anything.

**Approval record (2026-11-01).** The owner replied **"Approved"** to a message that quoted the complete **Default** approval text above, with the Final SHA-256 `0a1c4377d3a0ebf5ed2778a38aa9ba4c05bb477f3aea9f54b03790bd930584d4` written in. That approves blocks H1, H1b (H1-X only under its stated conditions), H2 once, H5, and H6 once. The optional **H3** and **H4** sub-cases were presented as separate approvals and are **not** approved. The owner runs every step; the agent runs nothing on the host. No host step has been reported yet.

**Approval record 2 (2026-11-02): re-run after the owner aligned Chocolatey to 2.7.5.** The owner replied **"Approved"** to a request to re-run the same **Default** set: H1, H1b (H1-X only under its stated conditions), H2 once, H5, and H6 once. The script bytes are unchanged (SHA-256 `0a1c4377d3a0ebf5ed2778a38aa9ba4c05bb477f3aea9f54b03790bd930584d4` at branch head `6de0a29`). H3 and H4 are **not** approved. The owner runs every step; the agent runs nothing on the host.

**Approval record 3 (2026-11-03): confirming run, Part A of the activation host-session plan.** The owner replied **"Go ahead with Part A approval 3 and 4"**, referring to the complete approval wordings 3 and 4 in §9 of the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md). That approves, on the owner's PC as the normal non-elevated user in one Windows PowerShell 5.1 window: H1 and H1b (script SHA-256 `0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4`, unchanged at branch head `0835801`), H1-X only under its stated conditions, **H2 once** (Default, the third run), **H3 once** (one UAC prompt for the synthetic `aq1-managed.exe`, answered by the owner; no other elevation), H5, and H6 once after the evidence is shared. **H4 is not approved.** No PC Steward files, no running of Chocolatey, no retries. The owner runs every step; the agent runs nothing on the host.

**Result 3 (2026-11-03): Part A confirming run — PASS.** Run by a local Claude agent on the owner's PC under handover revision 2 (script read from `origin/claude/sweet-franklin-j3mw4a` by `git cat-file`, `SOURCE-EXIT 0`, then hashed). Evidence: [Default](TASK_50_HOST_EVIDENCE_RESULT_3_DEFAULT.json) and [Uac](TASK_50_HOST_EVIDENCE_RESULT_3_UAC.json), as returned (redactions 0; the local agent's privacy check found nothing).
- Script SHA-256 `0A1C4377…84D4` (`HASH-OK`, `PARSE-OK`); `stop = null` in both runs; host token Limited (`tokenElevationType` 3, not elevated).
- **O5 `PASS-READABLE` with `matchesPinnedRelease = true`:** engine `matchesPin`; code list 56/56 matched; `helpers` 41 files and `tools` 13 files, **0 unlisted** in each (the `.old` leftover of run 2 is gone); registration `chocolatey` present with `chocolatey.nupkg` `matchesPin`; extension absent; `routeFindings = []`; `problems = []`.
- AQ1N, AQ1M, AQ1C and A2L3S `PASS`, as in runs 1 and 2.
- **AQ1UAC `PASS`:** the RunAs launch of the held synthetic image kept the hold (`peerEqualsHeld`, replacement refused before, during and after), so runas preserves the held-image identity for a synthetic image and the A2-L5 fallback is not triggered. The owner's count of UAC prompts is to be confirmed (expected: exactly 1).
- Cleanup (H6 and the step-0 source folder) awaits the owner's OK.
- This result feeds review and owner decisions only; it is not admission or a gate closure by itself.

## 11. Explicitly NOT authorized by this plan or by any approval above

- Executing `choco.exe` or any Chocolatey script or shim; changing Chocolatey configuration, cache, ACLs or packages; installing, repairing or upgrading Chocolatey.
- Launching, scanning, refreshing or updating PC Steward; any UAC for PC Steward; redeploying, removing or elevated validation of the deployment; reading the PC Steward database or `%LOCALAPPDATA%\PCSteward`.
- Registry writes; ACL or owner changes; service, scheduled-task or policy changes (AppLocker, WDAC, Defender exclusions); installs (SDK, runtime, package, module).
- Network access by the probe. Fetching the script is outside the test; the Final SHA-256 binds the bytes however they arrive.
- Writes or deletes outside `%TEMP%\pcsteward-evidence-task50`; deletion except through H1-X or H6.
- Running `-Mode SelfCheck` on the owner host (CI only), extra runs, retries, other `-Cases` values, edits after hashing, or running elevated.
- Treating any result as route admission, success, a gate pass, or acceptance of a spec amendment. The results feed an independent review and owner decisions only.
- Sending the evidence anywhere other than this session; push, merge, tag or publication.
- Execution of any step by the agent or through Remote Control. No approval names that route; the owner runs every step.
  - **Exception (owner request, 2026-11-03):** for approval record 3 only, a **local Claude agent on the owner's PC**, started by the owner in the owner's clone under the owner's normal account, may run the approved blocks H1, H1b, H1-X (its conditions), H2 once, H3 once, H5 (read-only, printing the files instead of opening Notepad) and H6 once, exactly as written in the [local-agent handover](TASK_50_PART_A_LOCAL_AGENT_HANDOVER.md). Each block runs in its own Windows PowerShell 5.1 process, re-setting `$d` with the first H1 line (the plan's own rule). The owner answers the single H3 UAC prompt personally and checks the report before pasting it back. This cloud session still runs nothing on the host, and Remote Control is still excluded. **Revision 2 of the handover (2026-11-03), approved by the owner ("Go ahead with the revised handover", 2026-11-03):** the owner's clone is on another branch, so a step 0 reads the script's committed bytes from the fetched remote-tracking branch (`git cat-file blob`, no Git state change) into a new `%TEMP%\pcsteward-task50-src` folder, which H1 then hashes; that one extra folder is removed once after H6.

## 12. Residual limits

- A-Q1 uses synthetic images. A pass shows OS and loader semantics, not the signed apphost, its WinVerifyTrust path, or the real closure. The ADR-025 exact-artifact host test is still required.
- O5 covers the current state only. Files that an elevated upgrade rewrites inherit their parents' ACLs, and a future unreadable file still fails safe as `ClientUnverifiable` → Unverified. **O5 binds the desktop file-first reader only while that reader stays within the probed O5 file set and access shape (§1).** Any extra input needs a coverage argument in its implementation review or new evidence.
- A2L3S is analogue evidence unless A2L3P passes. The existing Windows-CI deny-ACE test is hosted-runner evidence, not owner-host evidence.
- **Windows CI supplies the 5.1 parse and compile rehearsal** (`HostEvidenceScriptRehearsalTests`: `Parser.ParseFile` gives 0 errors; `-Mode SelfCheck` compiles the Run-mode Add-Type source and both CodeDom synthetics). The rehearsal runs as the CI runner's (typically administrator) user under Windows Server. It does not exercise the Run probes, the token or location gates, or Windows 11 specifics. H1b repeats the parse on the owner host.
- Same-user races (TOCTOU) against the evidence directory are outside the threat model. The directory is fresh and inherits user-private ACLs; reparse points are refused and the image directory is allowlisted before any launch, and Cleanup re-checks every level.
- If an exception or Ctrl+C lands between `CreateProcessSuspended` and `Resume` for `aq1-native.exe`, the suspended child can remain until log-off and Cleanup then refuses partway. This is unlikely. Stop and report; do not end processes or run H6 a second time without a separate approval.
- Listing and attribute failures record the underlying Windows error code (`GetBaseException().HResult & 0xFFFF`, e.g. 5 = access denied, 3 = path not found), not the PowerShell wrapper code 5377.
- A csc.exe orphaned by a `synthetic-build-deadline` stop can outlive the probe briefly. It only writes into the run directory, which Cleanup's allowlist accepts.

## Appendix A — probe script, revision 2 (LF text for review; the approvable bytes are the committed CRLF file in §4)

```powershell
#Requires -Version 5.1
<#
  Task 50 host-evidence probe for O5, A-Q1 and A2-L3 -- revision 2.
  Plan: docs/review/TASK_50_HOST_EVIDENCE_PLAN.md (revision 2). Approval binds the CRLF file SHA-256 recorded in plan section 4.
  Run and Cleanup write only inside %TEMP%\pcsteward-evidence-task50. SelfCheck writes only compiler output under -ScratchDirectory.
  Never runs choco.exe or any PC Steward binary. No registry writes, ACL changes, installs, network calls or elevation
  (except the separately approved Uac case). Pure ASCII.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Run', 'Cleanup', 'SelfCheck')][string]$Mode,
    [ValidateSet('Default', 'Uac', 'PcStewardHolds')][string]$Cases = 'Default',
    [string]$ScratchDirectory   # SelfCheck only: caller-supplied, existing, empty directory for compiler output
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Process scope only: use Windows PowerShell's own modules even when launched from PowerShell 7, which passes a module
# path where 5.1 cannot auto-load Get-FileHash, ConvertTo-Json and similar (same guard as the approved redeploy script).
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')

function Stop-Probe([string]$Reason) { throw ('STOP: ' + $Reason) }

$deadlineSeconds = @{ Run = @{ Default = 240; Uac = 300; PcStewardHolds = 60 }[$Cases]; Cleanup = 60; SelfCheck = 150 }[$Mode]
$clock = [Diagnostics.Stopwatch]::StartNew()
function Assert-Deadline { if ($clock.Elapsed.TotalSeconds -gt $deadlineSeconds) { Stop-Probe 'overall-deadline' } }

$nativeSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

public sealed class T50Open
{
    public SafeFileHandle Handle;
    public int Error;
    public bool Ok { get { return Handle != null && !Handle.IsInvalid; } }
}

public sealed class T50File
{
    public bool Ok;
    public string Failure;
    public int Error;
    public long Length;
    public string Sha256;
    public string FileId;
    public bool Reparse;
}

public sealed class T50Child
{
    public IntPtr Process;
    public IntPtr Thread;
    public int Error;
    public bool Ok { get { return Process != IntPtr.Zero; } }
}

public static class T50
{
    public const uint GenericRead = 0x80000000;
    public const uint FileReadAttributes = 0x00000080;
    public const uint ShareRead = 1, ShareAll = 7;
    public const uint FlagBackup = 0x02000000, FlagOpenReparse = 0x00200000;
    public const uint FileWriteData = 0x2, FileAppendData = 0x4, FileWriteEa = 0x10, FileDeleteChild = 0x40;
    public const uint FileWriteAttributes = 0x100, Delete = 0x10000, WriteDac = 0x40000, WriteOwner = 0x80000;
    // Exact copies of the ProtectedLaunchLayout lists in WindowsElevatedHelperTransport.cs (pinned in the plan).
    public static readonly uint[] FileModificationRights = { FileWriteData, FileAppendData, FileWriteEa, FileWriteAttributes, Delete, WriteDac, WriteOwner };
    public static readonly uint[] DirectoryModificationRights = { FileWriteData, FileAppendData, FileDeleteChild, Delete, WriteDac, WriteOwner };
    public static readonly uint[] AncestorReplacementRights = { FileDeleteChild, Delete, WriteDac, WriteOwner };

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo { public ulong Volume; public ulong IdLow; public ulong IdHigh; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars;
        public int dwFillAttribute; public int dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public string lpVerb; public string lpFile;
        public string lpParameters; public string lpDirectory; public int nShow; public IntPtr hInstApp; public IntPtr lpIDList;
        public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIconOrMonitor; public IntPtr hProcess;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int infoClass, out FileIdInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle h, StringBuilder volumeName, uint volumeNameSize, IntPtr serial, IntPtr maxComponent, IntPtr flags, StringBuilder fsName, uint fsNameSize);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo si, out ProcessInformation pi);
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr p, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr p, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value, int length, out int returned);

    public static T50Open Open(string path, uint access, uint share, uint flags)
    {
        SafeFileHandle h = CreateFileW(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        T50Open r = new T50Open();
        if (h.IsInvalid) { r.Error = Marshal.GetLastWin32Error(); h.Dispose(); } else { r.Handle = h; }
        return r;
    }

    // Opens and immediately closes the handle. Never reads, writes, renames or deletes content.
    public static string ProbeOpen(string path, uint access, bool directory)
    {
        T50Open o = Open(path, access, ShareAll, FlagOpenReparse | (directory ? FlagBackup : 0u));
        if (o.Ok) { o.Handle.Dispose(); return "handle"; }
        return o.Error.ToString();
    }

    // Mirrors ProtectedLaunchLayout.TryProbeRights (share RWD, OPEN_EXISTING, OPEN_REPARSE_POINT, BACKUP for
    // directories). The verdict follows the first decisive right exactly as TryProbeRights; every right is recorded.
    public static string[] ProbeRights(string path, bool directory, uint[] rights, out string verdict)
    {
        List<string> outcomes = new List<string>();
        verdict = null;
        foreach (uint right in rights)
        {
            string outcome = ProbeOpen(path, right, directory);
            outcomes.Add(right.ToString("X8") + "=" + outcome);
            if (verdict == null && outcome == "handle") verdict = "can-modify";
            else if (verdict == null && outcome != "5") verdict = "probe-failed-closed:" + outcome;
        }
        if (verdict == null) verdict = "cannot-modify";
        return outcomes.ToArray();
    }

    public static string FileIdOf(SafeFileHandle h, out int error)
    {
        FileIdInfo info;
        if (!GetFileInformationByHandleEx(h, 18, out info, (uint)Marshal.SizeOf(typeof(FileIdInfo))))
        { error = Marshal.GetLastWin32Error(); return null; }
        error = 0;
        byte[] id = new byte[16];
        Buffer.BlockCopy(BitConverter.GetBytes(info.IdLow), 0, id, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(info.IdHigh), 0, id, 8, 8);
        return info.Volume.ToString("X16") + ":" + BitConverter.ToString(id).Replace("-", "");
    }

    public static string FileSystemOf(SafeFileHandle h)
    {
        StringBuilder fs = new StringBuilder(261);
        return GetVolumeInformationByHandleW(h, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fs, 261) ? fs.ToString() : null;
    }

    // GENERIC_READ + FILE_SHARE_READ (the desktop read and HeldHelperBinding shape). Records identity and either a
    // full SHA-256 or a bounded tail read. Content is never retained or written anywhere.
    public static T50File ReadFile(string path, long maxBytes, bool fullHash, int tailBytes)
    {
        T50File r = new T50File();
        try { r.Reparse = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) { r.Failure = "attributes"; r.Error = ex.HResult & 0xFFFF; return r; }
        T50Open o = Open(path, GenericRead, ShareRead, FlagOpenReparse);
        if (!o.Ok) { r.Failure = "open"; r.Error = o.Error; return r; }
        try
        {
            int e;
            r.FileId = FileIdOf(o.Handle, out e);
            if (r.FileId == null) { r.Failure = "file-id"; r.Error = e; return r; }
            using (FileStream fs = new FileStream(o.Handle, FileAccess.Read, 65536))
            {
                r.Length = fs.Length;
                if (r.Length > maxBytes) { r.Failure = "over-bound"; return r; }
                if (fullHash)
                {
                    using (SHA256 sha = SHA256.Create()) { r.Sha256 = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", ""); }
                }
                else if (r.Length > 0)
                {
                    int n = (int)Math.Min((long)tailBytes, r.Length);
                    fs.Seek(-n, SeekOrigin.End);
                    byte[] buffer = new byte[n];
                    int total = 0;
                    while (total < n) { int k = fs.Read(buffer, total, n - total); if (k == 0) break; total += k; }
                    if (total != n) { r.Failure = "short-read"; return r; }
                }
            }
            r.Ok = true;
            return r;
        }
        catch (Exception ex) { r.Failure = "read"; r.Error = ex.HResult & 0xFFFF; return r; }
        finally { o.Handle.Dispose(); }
    }

    // Direct process creation of a fixed synthetic image; the command line is built only from compiled constants.
    public static T50Child Start(string exe, string arguments, string directory, bool suspended)
    {
        StartupInfo si = new StartupInfo();
        si.cb = Marshal.SizeOf(typeof(StartupInfo));
        ProcessInformation pi;
        StringBuilder cmd = new StringBuilder("\"" + exe + "\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
        uint flags = 0x08000000u | (suspended ? 0x4u : 0u); // CREATE_NO_WINDOW | CREATE_SUSPENDED
        T50Child c = new T50Child();
        if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, directory, ref si, out pi))
        { c.Error = Marshal.GetLastWin32Error(); return c; }
        c.Process = pi.hProcess; c.Thread = pi.hThread;
        return c;
    }

    // Used only by the separately approved Uac case.
    public static T50Child RunAs(string exe, string arguments, string directory)
    {
        ShellExecuteInfo info = new ShellExecuteInfo();
        info.cbSize = Marshal.SizeOf(typeof(ShellExecuteInfo));
        info.fMask = 0x00000040u | 0x00000100u; // SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC
        info.lpVerb = "runas"; info.lpFile = exe; info.lpParameters = arguments; info.lpDirectory = directory; info.nShow = 0;
        T50Child c = new T50Child();
        if (!ShellExecuteExW(ref info)) { c.Error = Marshal.GetLastWin32Error(); return c; }
        c.Process = info.hProcess;
        if (c.Process == IntPtr.Zero) c.Error = -1;
        return c;
    }

    public static int Resume(T50Child c) { return ResumeThread(c.Thread) == 0xFFFFFFFFu ? Marshal.GetLastWin32Error() : 0; }

    public static string WaitExit(T50Child c, uint milliseconds)
    {
        if (WaitForSingleObject(c.Process, milliseconds) != 0) return "timeout";
        uint code;
        return GetExitCodeProcess(c.Process, out code) ? code.ToString() : "exit-code-unavailable";
    }

    // Only for this probe's own non-elevated synthetic child after its bounded wait expired.
    public static bool TerminateOwnSyntheticChild(T50Child c) { return TerminateProcess(c.Process, 0xDEAD); }

    public static void Release(T50Child c)
    {
        if (c.Thread != IntPtr.Zero) { CloseHandle(c.Thread); c.Thread = IntPtr.Zero; }
        if (c.Process != IntPtr.Zero) { CloseHandle(c.Process); c.Process = IntPtr.Zero; }
    }

    // Adapter-gates A.4 step 3.4: image path from the process, opened with no write or delete share, FileIdInfo read.
    public static string PeerImageFileId(T50Child c, out string imagePath, out int error)
    {
        StringBuilder buffer = new StringBuilder(32768);
        uint size = (uint)buffer.Capacity;
        imagePath = null;
        if (!QueryFullProcessImageNameW(c.Process, 0, buffer, ref size)) { error = Marshal.GetLastWin32Error(); return null; }
        imagePath = buffer.ToString(0, (int)size);
        T50Open o = Open(imagePath, GenericRead, ShareRead, 0);
        if (!o.Ok) { error = o.Error; return null; }
        try { return FileIdOf(o.Handle, out error); } finally { o.Handle.Dispose(); }
    }

    // 18 = TokenElevationType (1 default, 2 full, 3 limited); 20 = TokenElevation (0/1).
    public static int TokenInt(int infoClass)
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token)) return -1;
        try { int value, returned; return GetTokenInformation(token, infoClass, out value, 4, out returned) ? value : -1; }
        finally { CloseHandle(token); }
    }
}
'@

$lazySource = @'
public static class Aq1Lazy { public static int Value() { return 7; } }
'@

$managedSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
public static class Aq1Program
{
    public static int Main(string[] args)
    {
        Thread.Sleep(1500);
        int result = 42;
        try
        {
            string directory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            Assembly lazy = Assembly.LoadFrom(Path.Combine(directory, "aq1-lazy.dll"));
            object value = lazy.GetType("Aq1Lazy", true).GetMethod("Value").Invoke(null, null);
            if (!(value is int) || (int)value != 7) result = 43;
        }
        catch (Exception) { result = 44; }
        Thread.Sleep(1500);
        return result;
    }
}
'@

$runtimeConfigText = '{"runtimeOptions":{"tfm":"net10.0","rollForward":"LatestPatch","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'

# Pinned 56-entry release list generated from docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json (path|length|sha256).
$releaseText = @'
choco.exe|12022880|4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0
choco.exe.manifest|2997|D715D6071E5CDD6447D46ED8E903B9B3AD5952ACC7394EE17593D87A546C17FC
helpers/Chocolatey.PowerShell.dll|40544|B36F60CB783329F341ED0CD0075D9A0BA2863F835FE46A944CCE9F693E9C2480
helpers/Chocolatey.PowerShell.dll-help.xml|59470|A0BCB2523147215E66A126F8B69E578043C14FEE3D72DF085F54C53E0D3CFBD9
helpers/chocolateyInstaller.psm1|22385|4AF7D8DCDBA7335F96D4D7F9B7AB75B29A890380D8C7C35C59F60739DB8A604E
helpers/chocolateyProfile.psm1|15622|ED69CDE7544EFE46ECBC66B10EDC55140E49CD2FA17F5CCF0E214D769E3CAD2B
helpers/chocolateyScriptRunner.ps1|17777|859CFA5D9DC747A5BC5651331977BEEF2177CF8335A24A8F0A26D7965FD66944
helpers/ChocolateyTabExpansion.ps1|37972|1A9137DE5F6B86E6BF4E478E7ACDF95433107E010FED460FAAD88D94EABB50E6
helpers/functions/Format-FileSize.ps1|16054|D47A44752FD6A983F9AB0E48AA8B12A2B0BC772EA0BB380C64723BB8E0B2CCBB
helpers/functions/Get-CheckSumValid.ps1|26167|5F88F0BEAA6CCE518833111F16B532E33F80BA3EA5B878C2690B4FC1218CF3B2
helpers/functions/Get-ChocolateyConfigValue.ps1|16318|83EB927EFCD495E15FD4FF5D043E1F0CF4B2DCEDED9AEB5A4AF3DB0CDE2BFD8B
helpers/functions/Get-ChocolateyPath.ps1|17022|18F1E1DC7EA4C3DAAE3FC51FD1373330C0132270180ED93BCAC7A1D2843353F5
helpers/functions/Get-ChocolateyUnzip.ps1|25780|BB77491D99FA16F09048E81A2CEDC29F3E6397D0D166BA2F72317ACA04347C25
helpers/functions/Get-ChocolateyWebFile.ps1|31820|5A763E6F6895FB36C99C942C56B2E5860E316978CE61FFB6D5A4599B357EAE4E
helpers/functions/Get-FtpFile.ps1|23075|A0D1FCEC293A9D8B1340BBF54194884EF1C7495C3CBE9D4D5673EDF2E5CCFB43
helpers/functions/Get-OSArchitectureWidth.ps1|17145|30F831B5189132D642EDFD7CC9E4F44B11AE357652E1748073D94206544D4B77
helpers/functions/Get-PackageParameters.ps1|21718|D537580623CA8088692AD463E8913A83EDB50963BD4B3B2B7B579E4E2B3B71F9
helpers/functions/Get-ToolsLocation.ps1|17518|10EF1BD8A810EE08F601A207AC83A4C7D9EBAD1A4777378CF3749E3C56B98C48
helpers/functions/Get-UACEnabled.ps1|16240|3F0C1C8C91696C6AE9C0E41589319D200D2C4BD16CABF4E2F1A11FC947A72F91
helpers/functions/Get-UninstallRegistryKey.ps1|20286|573FD644BEE61BF85053989C7111BE4A33223CE9BFD0AE5F95E05382FA08A1F5
helpers/functions/Get-VirusCheckValid.ps1|16118|73D85AA2297033F106A0C8C3138EFB9AD36F97ED108E040F12348FAE94C56F74
helpers/functions/Get-WebFile.ps1|28730|2DC9AE2D1DE175E6B867FF89F84BA25D08DD5F41B84E2818318CA23F3EB5797E
helpers/functions/Get-WebFileName.ps1|24253|DB062D9D09D7DAE751E626BF97138EAE6E9350112E2738CB3BE9EF78DBDACE1A
helpers/functions/Get-WebHeaders.ps1|20634|1D15D738C319132C792AC6F8820F50CCB0FC32597E9C886746BCC31FCCE2C683
helpers/functions/Install-BinFile.ps1|20895|767FD247F1F93CAC6188BA1A0C3398B87CF3178E25DED4A16CED7E9BB3CD27F6
helpers/functions/Install-ChocolateyEnvironmentVariable.ps1|18759|223F17F84D214C9FA9478817EFF65A2681D505DFBFB6B81A2121E446E9614000
helpers/functions/Install-ChocolateyExplorerMenuItem.ps1|19037|1E2FB44E0BE817B5E16A03A30502C65F61DDDC551BD3923EA571E3F83980E049
helpers/functions/Install-ChocolateyFileAssociation.ps1|17879|9C7C53D9EF8B902F60D5448ADBE96FC1EDA319C1DD6E6E7C8A4FFB3EB46C28DB
helpers/functions/Install-ChocolateyInstallPackage.ps1|29245|09E1973A0286C5912C7F233FCE89B2EFD9347EFDD085869437D9FCBE69A5C5D7
helpers/functions/Install-ChocolateyPackage.ps1|31292|16B5DF14198360715D06A5F12F2B1976D38E729BBE37748E0CBB17F57C4F367E
helpers/functions/Install-ChocolateyPinnedTaskBarItem.ps1|17373|DBA9B9E9329FA5D918B1E941DBFED9363A616033CDFCAD4A0C60AF9C41C4C4AC
helpers/functions/Install-ChocolateyPowershellCommand.ps1|23698|AEEE93F35724D656A73D1572522FE9B985FA1CAE6978B0405398EF9327A1580D
helpers/functions/Install-ChocolateyShortcut.ps1|22580|3410AEB9BC5106B29F2C4CBC74C9FEBDC229C569153DDB1E41188A7396079A3B
helpers/functions/Install-ChocolateyVsixPackage.ps1|23306|4E982CE84C225C6870CC78120E5F85FB622756FEFF4C7E8EB7088473A2538620
helpers/functions/Install-ChocolateyZipPackage.ps1|24593|B0211091D5FE20E5E917855550EEFB2961BE6556A6F4F8A9C98619723A062CB7
helpers/functions/Install-Vsix.ps1|16518|DAD0EF31EB232C6C189E0AD947E62E71C5239BF2DAD8F9D72A06CF3544A427A4
helpers/functions/Set-PowerShellExitCode.ps1|16048|FFFB05319B00EFB87D2705760EF351C11AD2B1913469635B980D386310BF0E1F
helpers/functions/Start-ChocolateyProcessAsAdmin.ps1|32129|694C4DAED9ADD47D4ECE4BD07568AA57DBC1F3316426F78CE5FD1EF2F2CE2625
helpers/functions/Uninstall-BinFile.ps1|18078|F706530F0CDABB2F02C9D5B70D7DE77D1F02FC4F6730C815FF8410DCF208B9FC
helpers/functions/Uninstall-ChocolateyEnvironmentVariable.ps1|17361|C20B736BCE859734C4497C6D5AAEC13BFA3C201461CC02F48A7539FEA54BE59E
helpers/functions/Uninstall-ChocolateyPackage.ps1|20524|07CED451A144A7F6E3FD24D19BFCB2E2A5EA49A969A036754CB833DC2D2986CB
helpers/functions/UnInstall-ChocolateyZipPackage.ps1|17275|599EA45533DC1AB68A9646C6A88B71F4FC11A8669FA3EE8F41360435CA8816DC
helpers/functions/Write-FunctionCallLogMessage.ps1|16311|117E7BBFD11DA2F5BD00F66AA004837DD774485E96334FB42B8AC537F4FB012B
tools/7z.dll|1319424|1C57100CC237DB626259F68FE91C5A632A5128E0E01BAB1452BAA1104FF84311
tools/7z.dll.manifest|513|6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033
tools/7z.exe|358912|C3598543C41918B5C94DAFB9DD724C16C9A217D36B5EE530E3BC10C9F12ACA6C
tools/7z.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/7z.exe.manifest|513|6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033
tools/7zip.license.txt|6031|519AC0A4BDED9C18EA02E0AFB71F663D8C47373BD9FACD3AC96A79F51D77765D
tools/checksum.exe|39008|04DDF72E7D930D3001D8111E1C5EA0A408A56D5BF4FFCF9C6680427375E31E1D
tools/checksum.exe.config|150|9B250C32CBEC90D2A61CB90055AC825D7A5F9A5923209CFD0625FCA09A908D0A
tools/checksum.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/checksum.license.txt|95|29472B6BE2F4E7134F09CC2FADF088CB87089853B383CA4AF29C19CC8DFC1A62
tools/shimgen.exe|568928|9033F75F25BAE75853BCA02AAB9BBF1EA73D52CD256822F744820052B1CB70AF
tools/shimgen.exe.ignore|0|E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
tools/shimgen.license.txt|3758|D4D2EF2C520EC3E4ECFF52C867EBD28E357900E0328BB4173CB46996DED353F4
'@
$release = @(foreach ($line in ($releaseText -split "`r?`n")) {
    if ($line.Length -gt 0) { $f = $line.Split('|'); [pscustomobject]@{ Path = $f[0]; Length = [long]$f[1]; Sha256 = $f[2] } }
})
if ($release.Count -ne 56) { Stop-Probe 'embedded-release-list-count' }
$engineSha256 = '4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0'
$assetSha256 = 'F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839'

# ---- Shared by Run and SelfCheck: the one Add-Type call and the bounded synthetic build -----------
function Import-T50 {
    Assert-Deadline
    Add-Type -TypeDefinition $nativeSource -Language CSharp   # the same call in Run and SelfCheck
    Assert-Deadline
}

# CodeDom (csc.exe) build in a separate runspace so that the wait is bounded. Outputs are never loaded into this process.
# On timeout the probe stops; an orphaned csc.exe can only finish writing into the given scratch/output directory.
$compileScript = @'
param($Source, $Output, $Executable, $Scratch)
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
$parameters.GenerateExecutable = $Executable
$parameters.GenerateInMemory = $false
$parameters.OutputAssembly = $Output
$parameters.TempFiles = New-Object System.CodeDom.Compiler.TempFileCollection($Scratch, $false)
$result = $provider.CompileAssemblyFromSource($parameters, $Source)
-not $result.Errors.HasErrors
'@
function Build-Synthetic([string]$Source, [string]$Output, [bool]$Executable, [string]$Scratch) {
    Assert-Deadline
    $shell = [PowerShell]::Create()
    $async = $null
    try {
        [void]$shell.AddScript($compileScript).AddArgument($Source).AddArgument($Output).AddArgument($Executable).AddArgument($Scratch)
        $async = $shell.BeginInvoke()
        $remaining = [int][Math]::Max(1000, [Math]::Min(60000, ($deadlineSeconds - $clock.Elapsed.TotalSeconds) * 1000))
        if (-not $async.AsyncWaitHandle.WaitOne($remaining)) { Stop-Probe 'synthetic-build-deadline' }
        $ok = @($shell.EndInvoke($async))
        if ($shell.HadErrors -or $ok.Count -ne 1 -or -not $ok[0]) { Stop-Probe 'synthetic-build-failed' }
    } finally {
        if ($null -ne $async -and $async.IsCompleted) { $shell.Dispose() }
    }
}

# ---- SelfCheck (CI rehearsal): host version, the Run-mode Add-Type compile and the synthetic compiles only -------
# No reads outside this script's own directory (other than .NET/compiler binaries), no launches, no probes; writes only
# compiler output and scratch under -ScratchDirectory.
if ($Mode -eq 'SelfCheck') {
    'SELFCHECK-HOST: ' + $PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
        'SELFCHECK-FAIL: windows-powershell-5.1-required'; exit 2
    }
    if ([string]::IsNullOrEmpty($ScratchDirectory) -or -not [IO.Path]::IsPathRooted($ScratchDirectory)) { 'SELFCHECK-FAIL: absolute-scratch-directory-required'; exit 2 }
    $scratchInfo = New-Object IO.DirectoryInfo $ScratchDirectory
    if (-not $scratchInfo.Exists -or ($scratchInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or @($scratchInfo.GetFileSystemInfos()).Count -ne 0) {
        'SELFCHECK-FAIL: scratch-directory-must-exist-empty-and-not-be-a-reparse-point'; exit 2
    }
    $env:TEMP = $scratchInfo.FullName   # process scope only
    $env:TMP = $scratchInfo.FullName
    try {
        'SELFCHECK-SHA256: ' + (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        'SELFCHECK-RELEASE-ENTRIES: ' + $release.Count
        Import-T50
        if ([T50]::FileModificationRights.Length -ne 7 -or [T50]::DirectoryModificationRights.Length -ne 6 -or [T50]::AncestorReplacementRights.Length -ne 4) { Stop-Probe 'rights-lists' }
        Build-Synthetic $lazySource (Join-Path $scratchInfo.FullName 'aq1-lazy.dll') $false $scratchInfo.FullName
        Build-Synthetic $managedSource (Join-Path $scratchInfo.FullName 'aq1-managed.exe') $true $scratchInfo.FullName
    } catch {
        $message = [string]$_.Exception.Message
        if ($message.StartsWith('STOP: ')) { 'SELFCHECK-FAIL: ' + $message.Substring(6) } else { 'SELFCHECK-FAIL: ' + $_.Exception.GetType().Name }
        exit 1
    }
    'SELFCHECK-OK'
    exit 0
}

# ---- S0: interpreter, platform, fixed location and token preconditions ----------------
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) { Stop-Probe 'windows-powershell-5.1-required' }
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { Stop-Probe 'x64-process-required' }
if ([Environment]::OSVersion.Version.Major -ne 10 -or [Environment]::OSVersion.Version.Build -lt 22000) { Stop-Probe 'windows-11-required' }
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$evidenceDir = Join-Path $tempRoot 'pcsteward-evidence-task50'
$scriptPath = [IO.Path]::GetFullPath($PSCommandPath)
if (-not [string]::Equals([IO.Path]::GetDirectoryName($scriptPath), $evidenceDir, [StringComparison]::OrdinalIgnoreCase)) { Stop-Probe 'script-not-in-dedicated-evidence-directory' }
$cursor = New-Object IO.DirectoryInfo $evidenceDir
while ($null -ne $cursor) {
    if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { Stop-Probe 'reparse-point-in-evidence-ancestry' }
    $cursor = $cursor.Parent
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ((New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Stop-Probe 'elevated-or-full-administrator-token' }

# ---- Cleanup mode: fixed-depth manual walk (evidence dir -> run-* -> tmp), reparse refused at every level ----
if ($Mode -eq 'Cleanup') {
    $runPattern = '^run-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}$'
    $runFiles = @('aq1-native.exe', 'aq1-managed.exe', 'aq1-lazy.dll', 'aq1-managed.runtimeconfig.json', 'a2l3-own.bin', 'evidence.json')
    function Get-Level([string]$Directory, [int]$Limit) {
        $items = @(Get-ChildItem -LiteralPath $Directory -Force)   # one level only; never -Recurse
        if ($items.Count -gt $Limit) { Stop-Probe 'cleanup-unexpected-entry-count' }
        foreach ($item in $items) { if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { Stop-Probe 'cleanup-reparse-point' } }
        return $items
    }
    foreach ($top in (Get-Level $evidenceDir 16)) {
        if (-not $top.PSIsContainer) { if ($top.Name -ne 'Task50-HostEvidence.ps1') { Stop-Probe 'cleanup-unexpected-file' }; continue }
        if ($top.Name -notmatch $runPattern) { Stop-Probe 'cleanup-unexpected-directory' }
        foreach ($inRun in (Get-Level $top.FullName 16)) {
            if (-not $inRun.PSIsContainer) { if ($runFiles -notcontains $inRun.Name) { Stop-Probe 'cleanup-unexpected-file' }; continue }
            if ($inRun.Name -ne 'tmp') { Stop-Probe 'cleanup-unexpected-directory' }
            foreach ($inScratch in (Get-Level $inRun.FullName 256)) { if ($inScratch.PSIsContainer) { Stop-Probe 'cleanup-unexpected-directory' } }
        }
    }
    Remove-Item -LiteralPath $evidenceDir -Recurse -Force   # reached only when every level was checked and no reparse point exists
    'CLEANUP-COMPLETE: <TEMP>\pcsteward-evidence-task50 removed'
    return
}

# ---- Run mode setup -------------------------------------------------------------------
$scriptSha256 = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
$runName = 'run-' + [DateTime]::UtcNow.ToString('yyyyMMdd\THHmmss\Z') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $evidenceDir $runName
$scratch = Join-Path $run 'tmp'
if (Test-Path -LiteralPath $run) { Stop-Probe 'run-directory-exists' }
[void][IO.Directory]::CreateDirectory($scratch)
$env:TEMP = $scratch   # process scope only: compiler scratch files stay inside the run directory
$env:TMP = $scratch
Import-T50
# ---- Sanitizing helpers -----------------------------------------------------------------
$programFiles = [IO.Path]::GetFullPath([Environment]::GetFolderPath('ProgramFiles')).TrimEnd('\')
$programData = [IO.Path]::GetFullPath([Environment]::GetFolderPath('CommonApplicationData')).TrimEnd('\')
$system32 = [IO.Path]::GetFullPath([Environment]::SystemDirectory).TrimEnd('\')
$chocoRoot = Join-Path $programData 'chocolatey'
$roots = New-Object 'System.Collections.Generic.List[object]'
foreach ($pair in @(@('<RUN>', $run), @('<EVIDENCE>', $evidenceDir), @('<CHOCO>', $chocoRoot), @('<SYSTEM32>', $system32), @('<PF>', $programFiles), @('<PROGRAMDATA>', $programData))) { $roots.Add($pair) }
function San([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $Path }
    foreach ($pair in $roots) { if ($Path.StartsWith($pair[1], [StringComparison]::OrdinalIgnoreCase)) { return $pair[0] + $Path.Substring($pair[1].Length) } }
    return '<OTHER>'
}
function Get-FailureToken($ErrorRecord) {
    $message = [string]$ErrorRecord.Exception.Message
    if ($message.StartsWith('STOP: ')) { return $message.Substring(6) }
    return $ErrorRecord.Exception.GetBaseException().GetType().Name + ':0x' + ('{0:X8}' -f $ErrorRecord.Exception.GetBaseException().HResult)
}
$redactions = 0
function Redact([string]$Text) {
    foreach ($secret in @($env:USERNAME, $env:COMPUTERNAME, $env:USERDOMAIN, $identity.User.Value)) {
        if (-not [string]::IsNullOrEmpty($secret) -and $secret.Length -ge 3) {
            $pattern = '(?<![A-Za-z0-9])' + [regex]::Escape($secret) + '(?![A-Za-z0-9])'
            $script:redactions += ([regex]::Matches($Text, $pattern, 'IgnoreCase')).Count
            $Text = [regex]::Replace($Text, $pattern, '<REDACTED>', 'IgnoreCase')
        }
    }
    return $Text
}
function Get-PathFileId([string]$Path, [bool]$Directory) {
    $flags = if ($Directory) { [T50]::FlagBackup -bor [T50]::FlagOpenReparse } else { [T50]::FlagOpenReparse }
    $o = [T50]::Open($Path, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]$flags)
    if (-not $o.Ok) { return 'error:' + $o.Error }
    try { $e = 0; $id = [T50]::FileIdOf($o.Handle, [ref]$e); if ($null -eq $id) { 'error:' + $e } else { $id } } finally { $o.Handle.Dispose() }
}
function Get-Replacement([string[]]$Paths) {
    @(foreach ($p in $Paths) { (Split-Path -Leaf $p) + ':DELETE=' + [T50]::ProbeOpen($p, [T50]::Delete, $false) + ',WRITE_DATA=' + [T50]::ProbeOpen($p, [T50]::FileWriteData, $false) })
}

# ---- O5: non-elevated readability of every file-first post-update check input ------------
function Read-Tree([string]$TopName, $KnownSet, [bool]$RecordNames, $Problems) {
    $r = [ordered]@{ tree = $TopName; present = $false; listError = $null; directories = 0; files = 0; unlistedFiles = 0; readable = 0; unreadable = 0; reparse = 0; overBound = $false; failures = @() }
    $top = Join-Path $chocoRoot $TopName
    $probe = [T50]::Open($top, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]([T50]::FlagBackup -bor [T50]::FlagOpenReparse))
    if (-not $probe.Ok) {
        if ($probe.Error -eq 2 -or $probe.Error -eq 3) { return $r }
        $r.present = $true; $r.listError = $probe.Error; [void]$Problems.Add($TopName + ':open:' + $probe.Error); return $r
    }
    $probe.Handle.Dispose(); $r.present = $true
    $failures = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    $stack.Push([object[]]@($top, 0))
    $count = 0
    while ($stack.Count -gt 0 -and -not $r.overBound) {
        Assert-Deadline
        $item = $stack.Pop(); $dir = [string]$item[0]; $depth = [int]$item[1]
        $info = New-Object IO.DirectoryInfo $dir
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $r.reparse++; continue }
        try { $children = @($info.EnumerateFileSystemInfos()) }
        catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$failures.Add('list:' + $code); [void]$Problems.Add($TopName + ':list:' + $code); continue }
        foreach ($child in $children) {
            Assert-Deadline
            $count++
            if ($count -gt 32768) { $r.overBound = $true; [void]$Problems.Add($TopName + ':over-bound'); break }
            $relative = $child.FullName.Substring($chocoRoot.Length + 1).Replace('\', '/')
            $label = if ($RecordNames) { $relative } else { $TopName + '/*' }
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $r.reparse++; [void]$failures.Add($label + ':reparse'); continue }
            if ($child -is [IO.DirectoryInfo]) {
                $r.directories++
                if ($depth + 1 -gt 32) { $r.overBound = $true; [void]$Problems.Add($TopName + ':depth') } else { $stack.Push([object[]]@($child.FullName, $depth + 1)) }
                continue
            }
            $r.files++
            if ($null -ne $KnownSet -and $KnownSet.Contains($relative.ToUpperInvariant())) { continue }
            $r.unlistedFiles++
            $f = [T50]::ReadFile($child.FullName, 64MB, $true, 0)
            if ($f.Ok) { $r.readable++ } else { $r.unreadable++; [void]$failures.Add($label + ':' + $f.Failure + ':' + $f.Error); [void]$Problems.Add($TopName + ':' + $f.Failure + ':' + $f.Error) }
        }
    }
    $r.failures = @($failures | Select-Object -First 50)
    return $r
}

function Format-File($File, [string]$Label, [string]$ExpectedSha256) {
    $o = [ordered]@{ path = $Label; ok = $File.Ok; failure = $File.Failure; error = $File.Error; length = $File.Length; fileIdRead = ($null -ne $File.FileId); reparse = $File.Reparse }
    if ($File.Sha256) { $o.sha256 = $File.Sha256 }
    if ($ExpectedSha256) { $o.matchesPin = ($File.Sha256 -eq $ExpectedSha256) }
    return $o
}

function Invoke-O5 {
    $started = [Diagnostics.Stopwatch]::StartNew()
    $problems = New-Object System.Collections.ArrayList
    $findings = New-Object System.Collections.ArrayList
    $r = [ordered]@{ case = 'O5'; status = 'not-run'; fixedRoot = '<PROGRAMDATA>\chocolatey' }
    $r.environmentVariableSet = -not [string]::IsNullOrEmpty($env:ChocolateyInstall)
    $r.environmentVariableEqualsFixedRoot = $r.environmentVariableSet -and [string]::Equals(([IO.Path]::GetFullPath($env:ChocolateyInstall)).TrimEnd('\'), $chocoRoot, [StringComparison]::OrdinalIgnoreCase)
    $engine = Join-Path $chocoRoot 'choco.exe'
    $presence = [T50]::Open($engine, [T50]::FileReadAttributes, [T50]::ShareAll, [T50]::FlagOpenReparse)
    if ($presence.Ok) { $presence.Handle.Dispose() }
    elseif ($presence.Error -eq 2 -or $presence.Error -eq 3) {
        $r.status = 'not-installed'; $r.rootDirectoryPresent = [IO.Directory]::Exists($chocoRoot); return $r
    }
    if (@(Get-Process -Name 'choco' -ErrorAction SilentlyContinue).Count -gt 0) { $r.status = 'skipped-choco-running'; return $r }
    $r.status = 'run'
    $rootId = Get-PathFileId $chocoRoot $true   # used only as a readability check; never recorded
    $r.rootIdentityRead = -not $rootId.StartsWith('error:')
    if (-not $r.rootIdentityRead) { [void]$problems.Add('root:file-id:' + $rootId.Substring(6)) }
    try { if (([IO.File]::GetAttributes($chocoRoot) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { [void]$findings.Add('root:reparse') } }
    catch { [void]$problems.Add('root:attributes:' + ($_.Exception.GetBaseException().HResult -band 0xFFFF)) }

    # Engine and the pinned 56-file code list (engine is entry 1; read once per entry).
    $known = New-Object 'System.Collections.Generic.HashSet[string]'
    $codeFailures = New-Object System.Collections.ArrayList
    $readable = 0; $matched = 0; $bytes = 0L
    foreach ($entry in $release) {
        Assert-Deadline
        [void]$known.Add($entry.Path.ToUpperInvariant())
        $f = [T50]::ReadFile((Join-Path $chocoRoot $entry.Path.Replace('/', '\')), 64MB, $true, 0)
        if ($entry.Path -eq 'choco.exe') { $r.engine = Format-File $f 'choco.exe' $engineSha256 }
        if ($f.Ok) {
            $readable++; $bytes += $f.Length
            if ($f.Length -eq $entry.Length -and $f.Sha256 -eq $entry.Sha256) { $matched++ } else { [void]$codeFailures.Add($entry.Path + ':mismatch') }
        } else {
            [void]$codeFailures.Add($entry.Path + ':' + $f.Failure + ':' + $f.Error); [void]$problems.Add('code:' + $f.Failure + ':' + $f.Error)
        }
        if ($f.Reparse) { [void]$findings.Add('code:reparse:' + $entry.Path) }
    }
    $r.codeList = [ordered]@{ expected = 56; readable = $readable; matchedPin = $matched; bytesRead = $bytes; failures = @($codeFailures) }

    # Unlisted helpers/tools files (would make the code list differ) and optional extension/hook code trees.
    $r.helpersTree = Read-Tree 'helpers' $known $true $problems
    $r.toolsTree = Read-Tree 'tools' $known $true $problems
    $r.extensionsTree = Read-Tree 'extensions' $null $false $problems
    $r.hooksTree = Read-Tree 'hooks' $null $false $problems

    # Registration: complete lib discovery set (lib root and immediate directories, top-level *.nupkg only),
    # full reads of the chocolatey and chocolatey.extension registrations, names of other packages never recorded.
    $lib = Join-Path $chocoRoot 'lib'
    $reg = [ordered]@{ libListed = $false; libListError = $null; directories = 0; rootNupkg = 0; nupkgFiles = 0; nupkgReadable = 0; nupkgUnreadable = 0; reparse = 0; overBound = $false; errorCodes = @(); chocolatey = 'absent'; chocolateyFiles = @(); extension = 'absent'; extensionFiles = @() }
    $codes = New-Object System.Collections.ArrayList
    try {
        $directories = @([IO.Directory]::EnumerateDirectories($lib) | Select-Object -First 1025)
        $rootPackages = @([IO.Directory]::EnumerateFiles($lib, '*.nupkg') | Where-Object { $_.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 2049)
        $reg.libListed = $true
    } catch { $reg.libListError = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$problems.Add('lib:list:' + $reg.libListError); $directories = @(); $rootPackages = @() }
    if ($directories.Count -gt 1024 -or $rootPackages.Count -gt 2048) { $reg.overBound = $true; [void]$problems.Add('lib:over-bound') }
    $reg.directories = $directories.Count; $reg.rootNupkg = $rootPackages.Count
    $packageFiles = New-Object System.Collections.ArrayList
    foreach ($p in $rootPackages) { [void]$packageFiles.Add(@($p, '')) }
    foreach ($d in $directories) {
        Assert-Deadline
        $name = (Split-Path -Leaf $d).ToLowerInvariant()
        try { $isReparse = ([IO.File]::GetAttributes($d) -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
        catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$codes.Add($code); [void]$problems.Add('lib-dir:attributes:' + $code); continue }
        if ($isReparse) { $reg.reparse++; [void]$findings.Add('lib:reparse'); continue }
        $client = if ($name -eq 'chocolatey' -or $name -eq 'chocolatey.extension') { $name } else { '' }
        if ($client -eq 'chocolatey') { $reg.chocolatey = 'present' } elseif ($client -eq 'chocolatey.extension') { $reg.extension = 'present' }
        try {
            $pattern = if ($client) { '*' } else { '*.nupkg' }
            foreach ($file in @([IO.Directory]::EnumerateFiles($d, $pattern) | Select-Object -First 65)) {
                if ($client -or $file.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase)) { [void]$packageFiles.Add(@($file, $client)) }
            }
        } catch { $code = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$codes.Add($code); [void]$problems.Add('lib-dir:list:' + $code) }
    }
    $chocolateyFiles = New-Object System.Collections.ArrayList; $extensionFiles = New-Object System.Collections.ArrayList
    foreach ($pair in $packageFiles) {
        Assert-Deadline
        $file = [string]$pair[0]; $client = [string]$pair[1]
        $isPackage = $file.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase)
        if ($isPackage) { $reg.nupkgFiles++ }
        if ($reg.nupkgFiles -gt 2048) { $reg.overBound = $true; [void]$problems.Add('lib:over-bound'); break }
        $f = if ($client) { [T50]::ReadFile($file, 64MB, $true, 0) } else { [T50]::ReadFile($file, 64MB, $false, 65536) }
        if ($isPackage) { if ($f.Ok) { $reg.nupkgReadable++ } else { $reg.nupkgUnreadable++ } }
        if (-not $f.Ok) { [void]$codes.Add($f.Error); [void]$problems.Add('lib-file:' + $f.Failure + ':' + $f.Error) }
        if ($f.Reparse) { $reg.reparse++; [void]$findings.Add('lib:reparse') }
        if ($client) {
            $leaf = (Split-Path -Leaf $file).ToLowerInvariant()
            $expected = if ($client -eq 'chocolatey' -and $leaf -eq 'chocolatey.nupkg') { $assetSha256 } else { $null }
            $formatted = Format-File $f ($client + '/' + $leaf) $expected
            if ($client -eq 'chocolatey') { [void]$chocolateyFiles.Add($formatted) } else { [void]$extensionFiles.Add($formatted) }
        }
    }
    $reg.chocolateyFiles = @($chocolateyFiles); $reg.extensionFiles = @($extensionFiles)
    $reg.errorCodes = @($codes | Sort-Object -Unique)
    if ($reg.libListed -and $reg.chocolatey -eq 'absent') { [void]$findings.Add('lib:chocolatey-registration-absent') }
    $r.registration = $reg

    # Store residue for the two client identities: directory listing plus bounded 4 KiB tail reads.
    $store = Join-Path $chocoRoot '.chocolatey'
    $st = [ordered]@{ present = $false; listError = $null; directories = 0; clientDirectories = 0; markerFiles = 0; markerReadable = 0; errorCodes = @() }
    $stCodes = New-Object System.Collections.ArrayList
    if ([IO.Directory]::Exists($store)) {
        $st.present = $true
        try {
            $storeDirs = @([IO.Directory]::EnumerateDirectories($store) | Select-Object -First 4097)
            $st.directories = $storeDirs.Count
            if ($storeDirs.Count -gt 4096) { [void]$problems.Add('store:over-bound') }
            foreach ($d in $storeDirs) {
                if ((Split-Path -Leaf $d) -notmatch '^(?i)chocolatey(\.extension)?\.[0-9]') { continue }
                $st.clientDirectories++
                foreach ($file in @([IO.Directory]::EnumerateFiles($d) | Select-Object -First 64)) {
                    $st.markerFiles++
                    $f = [T50]::ReadFile($file, 1MB, $false, 4096)
                    if ($f.Ok) { $st.markerReadable++ } else { [void]$stCodes.Add($f.Error); [void]$problems.Add('store-file:' + $f.Failure + ':' + $f.Error) }
                }
            }
        } catch { $st.listError = $_.Exception.GetBaseException().HResult -band 0xFFFF; [void]$problems.Add('store:list:' + $st.listError) }
    }
    $st.errorCodes = @($stCodes | Sort-Object -Unique)
    $r.store = $st

    $r.problems = @($problems | Select-Object -First 100)
    $r.routeFindings = @($findings | Sort-Object -Unique | Select-Object -First 100)
    $r.matchesPinnedRelease = ($matched -eq 56) -and ($r.helpersTree.unlistedFiles -eq 0) -and ($r.toolsTree.unlistedFiles -eq 0)
    $r.elapsedMs = $started.ElapsedMilliseconds
    $denied = @($problems | Where-Object { $_ -match ':5$' }).Count
    $r.verdict = if ($denied -gt 0) { 'FAIL-UNREADABLE' } elseif ($problems.Count -gt 0) { 'INCONCLUSIVE' } else { 'PASS-READABLE' }
    return $r
}

# ---- A-Q1: launch of a synthetic image while read-share holds are held ------------------------
function Invoke-Aq1Run([string]$Launcher, [string]$Image, [string]$Arguments, [string[]]$HoldPaths, [bool]$CheckPeer) {
    $r = [ordered]@{ launcher = $Launcher; held = ($HoldPaths.Count -gt 0) }
    $heldId = $null
    $holds = New-Object System.Collections.ArrayList
    try {
        foreach ($p in $HoldPaths) {
            $o = [T50]::Open($p, [T50]::GenericRead, [T50]::ShareRead, 0)   # FileShare.Read only, as HeldHelperBinding
            if (-not $o.Ok) { $r.holdError = $o.Error; return $r }
            [void]$holds.Add($o)
        }
        if ($holds.Count -gt 0) {
            $e = 0
            $heldId = [T50]::FileIdOf($holds[0].Handle, [ref]$e)   # volume serial + file id; kept in memory only
            $r.heldFileIdRead = ($null -ne $heldId)
            if ($null -ne $heldId) { $r.heldFileId128 = $heldId.Split(':')[1] }
            $r.freshFileIdBeforeEqualsHeld = ($null -ne $heldId) -and ((Get-PathFileId $HoldPaths[0] $false) -eq $heldId)
            $r.replacementWhileHeldBeforeLaunch = Get-Replacement $HoldPaths
        }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $child = switch ($Launcher) {
            'CreateProcessSuspended' { [T50]::Start($Image, $Arguments, $run, $true) }
            'CreateProcess' { [T50]::Start($Image, $Arguments, $run, $false) }
            'RunAs' { [T50]::RunAs($Image, $Arguments, $run) }
        }
        $r.launchMs = $watch.ElapsedMilliseconds
        if (-not $child.Ok) { $r.launchError = $child.Error; return $r }
        try {
            if ($Launcher -ne 'CreateProcessSuspended') { Start-Sleep -Milliseconds 500 }
            if ($CheckPeer) {
                $path = $null; $e = 0
                $peer = [T50]::PeerImageFileId($child, [ref]$path, [ref]$e)
                $r.peerImagePath = San $path
                $r.peerFileIdRead = ($null -ne $peer)
                if ($null -eq $peer) { $r.peerError = $e }
                if ($holds.Count -gt 0) { $r.peerEqualsHeld = ($null -ne $peer) -and ($null -ne $heldId) -and ($peer -eq $heldId) }
            }
            if ($holds.Count -gt 0) { $r.replacementWhileRunning = Get-Replacement $HoldPaths }
            if ($Launcher -eq 'CreateProcessSuspended') { $r.resumeError = [T50]::Resume($child) }
            $r.exit = [T50]::WaitExit($child, 15000)
            if ($r.exit -eq 'timeout' -and $Launcher -ne 'RunAs') { $r.terminatedOwnSyntheticChild = [T50]::TerminateOwnSyntheticChild($child); [void][T50]::WaitExit($child, 5000) }
        } finally { [T50]::Release($child) }
        if ($holds.Count -gt 0) {
            $r.freshFileIdAfterEqualsHeld = ($null -ne $heldId) -and ((Get-PathFileId $HoldPaths[0] $false) -eq $heldId)
            $r.replacementWhileHeldAfterExit = Get-Replacement $HoldPaths
        }
    } finally { foreach ($h in $holds) { $h.Handle.Dispose() } }
    if ($HoldPaths.Count -gt 0) {
        # Bounded re-probe (at most 3 attempts, 500 ms apart) absorbs a transient scanner open; attempts are recorded.
        $r.controlAfterRelease = @(foreach ($p in $HoldPaths) {
            $outcome = $null
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                $outcome = [T50]::ProbeOpen($p, [T50]::Delete, $false)
                if ($outcome -ne '32') { break }
                if ($attempt -lt 3) { Start-Sleep -Milliseconds 500 }
            }
            (Split-Path -Leaf $p) + ':DELETE=' + $outcome + ';attempts=' + [Math]::Min($attempt, 3)
        })
    }
    return $r
}

function Test-HeldRun($Baseline, $Held, [bool]$PeerRequired) {
    if (-not $Baseline.Contains('exit') -or $Baseline.exit -eq 'timeout') { return 'INCONCLUSIVE-BASELINE' }
    if ($Held.Contains('holdError')) { return 'INCONCLUSIVE-HOLD' }
    if ($Held.Contains('launchError')) { return 'FAIL-LAUNCH:' + $Held.launchError }
    if ($Held.exit -ne $Baseline.exit) { return 'FAIL-EXIT:' + $Held.exit }
    $all = @($Held.replacementWhileHeldBeforeLaunch) + @($Held.replacementWhileHeldAfterExit)
    if (@($all | Where-Object { $_ -match '=handle' }).Count -gt 0) { return 'FAIL-HOLD-NOT-EXCLUSIVE' }
    if (@($all | Where-Object { $_ -notmatch 'DELETE=32,WRITE_DATA=32$' }).Count -gt 0) { return 'INCONCLUSIVE-REPLACEMENT-PROBE' }
    if (@($Held.controlAfterRelease | Where-Object { $_ -notmatch 'DELETE=handle;attempts=[123]$' }).Count -gt 0) { return 'INCONCLUSIVE-CONTROL' }
    if (-not ($Held.freshFileIdBeforeEqualsHeld -and $Held.freshFileIdAfterEqualsHeld)) { return 'FAIL-IDENTITY-UNSTABLE' }
    if ($PeerRequired -and -not ($Held.Contains('peerEqualsHeld') -and $Held.peerEqualsHeld)) { return 'FAIL-PEER-IDENTITY' }
    return 'PASS'
}

function Assert-ImageDirectory {
    $allowed = @('aq1-native.exe', 'aq1-managed.exe', 'aq1-lazy.dll', 'aq1-managed.runtimeconfig.json', 'a2l3-own.bin')
    foreach ($item in @(Get-ChildItem -LiteralPath $run -Force)) {
        if ($item.PSIsContainer) { if ($item.Name -ne 'tmp') { Stop-Probe 'unexpected-directory-in-run' } }
        elseif ($allowed -notcontains $item.Name) { Stop-Probe 'unexpected-file-in-image-directory' }
    }
}

# ---- A2-L3: TryProbeRights results while read-share holds are held ------------------------------
function Get-Probe([string]$Path, [bool]$Directory, [uint32[]]$Rights) {
    $v = $null
    $outcomes = [T50]::ProbeRights($Path, $Directory, $Rights, [ref]$v)
    return [ordered]@{ path = (San $Path); outcomes = @($outcomes); verdict = $v }
}

function Get-A2L3Verdict($Unheld, $Held) {
    if (@($Unheld | Where-Object { $_.verdict -ne 'cannot-modify' }).Count -gt 0) { return 'INCONCLUSIVE-ANALOGUE-NOT-PROTECTED' }
    $outcomes = @($Held | ForEach-Object { $_.outcomes })
    if (@($outcomes | Where-Object { $_ -match '=handle$' }).Count -gt 0) { return 'FAIL-UNPROTECTED' }
    if (@($outcomes | Where-Object { $_ -match '=32$' }).Count -gt 0) { return 'FAIL-SHARING-VIOLATION' }
    if (@($Held | Where-Object { $_.verdict -ne 'cannot-modify' }).Count -gt 0) { return 'INCONCLUSIVE-OTHER-ERROR' }
    return 'PASS'
}

function Invoke-A2L3Synthetic {
    $r = [ordered]@{ case = 'A2L3-S' }
    $target = Join-Path $system32 'whoami.exe'
    $unheld = @((Get-Probe $target $false ([T50]::FileModificationRights)), (Get-Probe $system32 $true ([T50]::AncestorReplacementRights)))
    $hold = [T50]::Open($target, [T50]::GenericRead, [T50]::ShareRead, 0)
    if (-not $hold.Ok) { $r.holdError = $hold.Error; $r.verdict = 'INCONCLUSIVE-HOLD'; return $r }
    $holdWatch = [Diagnostics.Stopwatch]::StartNew()
    try { $held = @((Get-Probe $target $false ([T50]::FileModificationRights)), (Get-Probe $system32 $true ([T50]::AncestorReplacementRights))) }
    finally { $hold.Handle.Dispose(); $r.systemFileHoldMs = $holdWatch.ElapsedMilliseconds }
    $r.unheld = $unheld; $r.held = $held
    $own = Join-Path $run 'a2l3-own.bin'
    [IO.File]::WriteAllBytes($own, [byte[]](1..16))
    $r.ownUnheld = Get-Probe $own $false ([T50]::FileModificationRights)
    $ownHold = [T50]::Open($own, [T50]::GenericRead, [T50]::ShareRead, 0)
    try { $r.ownHeld = Get-Probe $own $false ([T50]::FileModificationRights) } finally { if ($ownHold.Ok) { $ownHold.Handle.Dispose() } }
    $r.positiveControl = if ($r.ownUnheld.verdict -eq 'can-modify' -and $r.ownHeld.verdict -eq 'probe-failed-closed:32') { 'as-expected' } else { 'unexpected' }
    $r.verdict = Get-A2L3Verdict $unheld $held
    if ($r.verdict -eq 'PASS' -and $r.positiveControl -ne 'as-expected') { $r.verdict = 'INCONCLUSIVE-CONTROL' }
    return $r
}

function Invoke-A2L3PcSteward {
    $r = [ordered]@{ case = 'A2L3-P' }
    if (@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count -gt 0) { $r.verdict = 'NOT-RUN-PCSTEWARD-RUNNING'; return $r }
    $install = Join-Path $programFiles 'PC Steward'
    if (-not [IO.Directory]::Exists($install)) { $r.verdict = 'NOT-RUN-NOT-INSTALLED'; return $r }
    $names = @('PCSteward.Desktop.exe', 'PCSteward.Desktop.dll', 'PCSteward.ElevatedHelper.exe', 'PCSteward.ElevatedHelper.dll',
        'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json', 'PCSteward.Execution.dll',
        'PCSteward.Application.dll', 'PCSteward.Domain.dll', 'PCSteward.Provider.Chocolatey.dll', 'PCSteward.Provider.WinGet.dll',
        'PCSteward.DeploymentCapability.txt')
    $present = @($names | Where-Object { [IO.File]::Exists((Join-Path $install $_)) })
    $r.bindingFilesPresent = $present
    $ancestors = New-Object System.Collections.ArrayList
    $cursorDir = New-Object IO.DirectoryInfo $install
    while ($null -ne $cursorDir) { [void]$ancestors.Insert(0, $cursorDir.FullName); $cursorDir = $cursorDir.Parent }
    $probeAll = {
        $list = New-Object System.Collections.ArrayList
        foreach ($a in $ancestors) {
            $rights = if ([string]::Equals($a, $install, [StringComparison]::OrdinalIgnoreCase)) { [T50]::DirectoryModificationRights } else { [T50]::AncestorReplacementRights }
            [void]$list.Add((Get-Probe $a $true $rights))
        }
        foreach ($n in $present) { [void]$list.Add((Get-Probe (Join-Path $install $n) $false ([T50]::FileModificationRights))) }
        return , $list.ToArray()
    }
    $unheld = & $probeAll
    $holds = New-Object System.Collections.ArrayList
    $holdWatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        foreach ($n in $present) {
            $o = [T50]::Open((Join-Path $install $n), [T50]::GenericRead, [T50]::ShareRead, 0)
            if (-not $o.Ok) { $r.holdError = $n + ':' + $o.Error; break }
            [void]$holds.Add($o)
        }
        if (-not $r.Contains('holdError')) { $held = & $probeAll }
    } finally { foreach ($h in $holds) { $h.Handle.Dispose() }; $r.holdMs = $holdWatch.ElapsedMilliseconds }
    $r.unheld = $unheld
    if ($r.Contains('holdError')) { $r.verdict = 'INCONCLUSIVE-HOLD'; return $r }
    $r.held = $held
    if ($r.holdMs -gt 10000) { $r.holdBoundExceeded = $true }
    $r.verdict = Get-A2L3Verdict $unheld $held
    return $r
}

# ---- Main ----------------------------------------------------------------------------------
$runVolumeId = Get-PathFileId $run $true
$programFilesVolumeId = Get-PathFileId $programFiles $true
$volumeEquality = if ($runVolumeId.StartsWith('error:') -or $programFilesVolumeId.StartsWith('error:')) { 'unknown' } else { $runVolumeId.Split(':')[0] -eq $programFilesVolumeId.Split(':')[0] }
$evidence = [ordered]@{
    schema = 'pc-steward-task50-host-evidence:1'
    cases = $Cases
    scriptSha256 = $scriptSha256
    runDirectory = '<EVIDENCE>\' + $runName
    startedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    hostFacts = [ordered]@{
        osVersion = [Environment]::OSVersion.Version.ToString()
        powerShell = $PSVersionTable.PSVersion.ToString()
        clr = [Environment]::Version.ToString()
        tokenElevationType = [T50]::TokenInt(18)
        tokenElevated = [T50]::TokenInt(20)
        evidenceVolumeEqualsProgramFilesVolume = $volumeEquality
    }
    results = [ordered]@{}
    verdicts = [ordered]@{}
    stop = $null
}
$runHandle = [T50]::Open($run, [T50]::FileReadAttributes, [T50]::ShareAll, [uint32]([T50]::FlagBackup -bor [T50]::FlagOpenReparse))
if ($runHandle.Ok) { $evidence.hostFacts.evidenceFileSystem = [T50]::FileSystemOf($runHandle.Handle); $runHandle.Handle.Dispose() }
if ($evidence.hostFacts.tokenElevated -ne 0) { Stop-Probe 'token-elevated' }

try {
    if ($Cases -eq 'Default') {
        $evidence.results.O5 = Invoke-O5
        $evidence.verdicts.O5 = if ($evidence.results.O5.status -eq 'run') { $evidence.results.O5.verdict } else { $evidence.results.O5.status.ToUpperInvariant() }
        Assert-Deadline
    }
    if ($Cases -eq 'Default' -or $Cases -eq 'Uac') {
        $native = Join-Path $run 'aq1-native.exe'
        $managed = Join-Path $run 'aq1-managed.exe'
        $lazy = Join-Path $run 'aq1-lazy.dll'
        if ($Cases -eq 'Default') { [IO.File]::Copy((Join-Path $system32 'whoami.exe'), $native, $false) }
        Build-Synthetic $lazySource $lazy $false $scratch
        Build-Synthetic $managedSource $managed $true $scratch
        Assert-ImageDirectory
    }
    if ($Cases -eq 'Default') {
        $nb = Invoke-Aq1Run 'CreateProcessSuspended' $native '/?' @() $true
        $nh = Invoke-Aq1Run 'CreateProcessSuspended' $native '/?' @($native) $true
        $evidence.results.AQ1N = [ordered]@{ baseline = $nb; held = $nh; verdict = (Test-HeldRun $nb $nh $true) }
        Assert-Deadline
        $mb = Invoke-Aq1Run 'CreateProcess' $managed '' @() $true
        $mh = Invoke-Aq1Run 'CreateProcess' $managed '' @($managed, $lazy) $true
        $evidence.results.AQ1M = [ordered]@{ baseline = $mb; held = $mh; verdict = (Test-HeldRun $mb $mh $true) }
        if ($mb.Contains('exit') -and $mb.exit -ne '42') { $evidence.results.AQ1M.verdict = 'INCONCLUSIVE-BASELINE' }
        Assert-Deadline
        $dotnet = Join-Path $programFiles 'dotnet\dotnet.exe'
        $sharedRoot = Join-Path $programFiles 'dotnet\shared\Microsoft.NETCore.App'
        $runtime10 = [IO.Directory]::Exists($sharedRoot) -and (@([IO.Directory]::EnumerateDirectories($sharedRoot, '10.0.*')).Count -gt 0)
        if ([IO.File]::Exists($dotnet) -and $runtime10) {
            $config = Join-Path $run 'aq1-managed.runtimeconfig.json'
            [IO.File]::WriteAllText($config, $runtimeConfigText, (New-Object Text.UTF8Encoding $false))
            Assert-ImageDirectory
            $arguments = 'exec --runtimeconfig "' + $config + '" "' + $managed + '"'
            $cb = Invoke-Aq1Run 'CreateProcess' $dotnet $arguments @() $false
            $ch = Invoke-Aq1Run 'CreateProcess' $dotnet $arguments @($managed, $lazy) $false
            $evidence.results.AQ1C = [ordered]@{ baseline = $cb; held = $ch; verdict = (Test-HeldRun $cb $ch $false) }
            if ($cb.Contains('exit') -and $cb.exit -ne '42') { $evidence.results.AQ1C.verdict = 'INCONCLUSIVE-HARNESS' }
        } else { $evidence.results.AQ1C = [ordered]@{ verdict = 'NOT-RUN-NO-DOTNET10-RUNTIME' } }
        $evidence.verdicts.AQ1N = $evidence.results.AQ1N.verdict
        $evidence.verdicts.AQ1M = $evidence.results.AQ1M.verdict
        $evidence.verdicts.AQ1C = $evidence.results.AQ1C.verdict
        Assert-Deadline
        $evidence.results.A2L3S = Invoke-A2L3Synthetic
        $evidence.verdicts.A2L3S = $evidence.results.A2L3S.verdict
    }
    if ($Cases -eq 'Uac') {
        $ub = Invoke-Aq1Run 'CreateProcess' $managed '' @() $true
        $uh = Invoke-Aq1Run 'RunAs' $managed '' @($managed, $lazy) $true   # exactly one UAC prompt
        $evidence.results.AQ1UAC = [ordered]@{ baseline = $ub; held = $uh; verdict = (Test-HeldRun $ub $uh $true) }
        if ($uh.Contains('launchError') -and $uh.launchError -eq 1223) { $evidence.results.AQ1UAC.verdict = 'NOT-RUN-UAC-DECLINED' }
        $evidence.verdicts.AQ1UAC = $evidence.results.AQ1UAC.verdict
    }
    if ($Cases -eq 'PcStewardHolds') {
        $evidence.results.A2L3P = Invoke-A2L3PcSteward
        $evidence.verdicts.A2L3P = $evidence.results.A2L3P.verdict
    }
} catch {
    $evidence.stop = Get-FailureToken $_
} finally {
    $evidence.elapsedMs = $clock.ElapsedMilliseconds
    $null = Redact ($evidence | ConvertTo-Json -Depth 12)   # first pass counts redactions only
    $evidence.redactions = $redactions
    $script:redactions = 0
    $json = Redact ($evidence | ConvertTo-Json -Depth 12)
    [IO.File]::WriteAllText((Join-Path $run 'evidence.json'), $json, (New-Object Text.UTF8Encoding $false))
    'EVIDENCE: <TEMP>\pcsteward-evidence-task50\' + $runName + '\evidence.json'
    'REDACTIONS: ' + $redactions
    foreach ($k in $evidence.verdicts.Keys) { 'VERDICT ' + $k + ': ' + $evidence.verdicts[$k] }
    if ($evidence.stop) { 'STOPPED: ' + $evidence.stop }
}
```
