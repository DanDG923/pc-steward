# Task 50 Part A — handover for a local Claude agent on the owner's PC

Requirements: `FR-UPD-15`, `TM-03/04/07/13`. Authority: approval record 3 in the [host-evidence plan](TASK_50_HOST_EVIDENCE_PLAN.md) (approvals 3 and 4 of the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) §9), plus the owner's request of 2026-11-03 that a local Claude agent run those approved blocks on the owner's behalf. Nothing else is authorized.

Paste everything below the line into a Claude Code session started **in the root of the owner's PC Steward clone**, as the owner's normal (non-administrator) Windows account. The owner stays at the PC to answer one UAC prompt.

---

You are running a pre-approved, read-only host-evidence check for the PC Steward project on this Windows 11 PC. The owner has approved exactly the steps below. Follow them literally. You have no authority beyond them.

## Rules (all mandatory)

1. Run **only** the commands in "Steps", in order, each **once**. Never retry, re-run, edit, reformat, re-wrap or "fix" a command. No other commands except the read-only checks named here.
2. Do **not**: fetch, pull, commit, push or change any Git state or config; edit, create or delete any file except what the steps themselves do inside `%TEMP%\pcsteward-evidence-task50`; run `choco` or anything Chocolatey; start, scan or update PC Steward; read `%LOCALAPPDATA%\PCSteward`; change the registry, ACLs, services, scheduled tasks or policies; install anything; run anything elevated or use `runas`/`Start-Process -Verb RunAs` yourself; access the network.
3. **Stop rule:** if any output line starts with `STOP:` or `STOPPED:`, or a command errors, run no further step except the ones the step's "On stop" says remain allowed (H1-X, H5, H6). Never retry. Report the exact `STOP`/`STOPPED` token only.
4. **Privacy:** never copy console text from before an `EVIDENCE:` line, user names, computer names, profile paths or SIDs into your final report. The evidence files are already tokenized and redacted by the script; you still check them (step 5).
5. **UAC:** step 4 shows the owner **one** UAC prompt. You cannot see or answer it. Before running step 4, tell the owner and wait until they say they are ready. If the owner reports a second prompt, or a prompt naming any program other than `aq1-managed.exe`, they answer **No** and you stop after steps 5 and 6.
6. Give each command a tool timeout of at least 10 minutes.

## How to run each step

Each step is a separate Windows PowerShell 5.1 process fed by a single-quoted heredoc, so the lines run exactly as written and nothing is expanded by the outer shell. From Git Bash (Claude Code's default shell on Windows) use exactly:

```bash
powershell.exe -NoLogo -NoProfile -NonInteractive -Command - <<'PS'
<the step's lines, unchanged>
PS
```

If your shell tool is PowerShell instead, run the step's lines unchanged in one call. Every step after step 1 starts by setting `$d` again with the first H1 line; this is the plan's own rule ("re-run only the first H1 line to set `$d` again").

Before step 1, find `<CHECKOUT>`: the absolute Windows path of this repository's root (in Git Bash, `pwd -W`). Check read-only that `<CHECKOUT>\scripts\evidence\Task50-HostEvidence.ps1` exists. Substitute that path for `<CHECKOUT>` in step 1 only; change nothing else.

## Steps

**Step 1 — H1 and H1b** (expected: a 64-hex hash line, `HASH-OK`, then `PARSE-OK`)

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'; $created = $false; $hashOk = $false; $expected = '0A1C4377D3A0EBF5ED2778A38AA9BA4C05BB477F3AEA9F54B03790BD930584D4'
if (Test-Path -LiteralPath $d) { 'STOP: evidence directory already exists' } else { New-Item -ItemType Directory -Path $d -ErrorAction Stop | Out-Null; $created = $true; Copy-Item -LiteralPath '<CHECKOUT>\scripts\evidence\Task50-HostEvidence.ps1' -Destination $d -ErrorAction Stop; $h = (Get-FileHash -LiteralPath (Join-Path $d 'Task50-HostEvidence.ps1') -Algorithm SHA256 -ErrorAction Stop).Hash; $h; if ($h -eq $expected) { $hashOk = $true; 'HASH-OK' } else { 'STOP: hash differs' } }
if ($created -and $hashOk) { $tokens = $null; $parseErrors = $null; [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $d 'Task50-HostEvidence.ps1'), [ref]$tokens, [ref]$parseErrors); if ($parseErrors.Count -ne 0) { 'STOP: parse errors ' + $parseErrors.Count } else { 'PARSE-OK' } } else { 'STOP: H1 did not pass' }
```

On stop: `STOP: evidence directory already exists` → delete nothing, stop, report. `STOP: hash differs`, `STOP: parse errors n`, or an error after the directory was created → run **H1-X once**, then stop and report:

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'; $created = Test-Path -LiteralPath (Join-Path $d 'Task50-HostEvidence.ps1')
if ($created) { Remove-Item -LiteralPath $d -Recurse -Force; 'H1X-REMOVED' } else { 'STOP: not created by H1; delete nothing' }
```

**Step 2 — H2, the Default run, once** (expected: `EVIDENCE: …\evidence.json`, `REDACTIONS: n`, then `VERDICT` lines for O5, AQ1N, AQ1M, AQ1C and A2L3S)

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Default
```

On stop or error: skip step 3; steps 5 and 6 remain allowed.

**Step 3 — tell the owner** "Step 4 will show one UAC prompt for `aq1-managed.exe` (unknown publisher) in a `pcsteward-evidence-task50\run-…` folder. Please answer it yourself; answer No to anything else or to a second prompt. Tell me when you are ready." Wait for the owner.

**Step 4 — H3, the Uac run, once** (expected: one UAC prompt for the owner; then `VERDICT AQ1UAC: …`)

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Run -Cases Uac
```

A declined prompt gives `NOT-RUN-UAC-DECLINED`: do not retry. Steps 5 and 6 remain allowed.

**Step 5 — H5, read the evidence (read-only)**

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'
Get-ChildItem -LiteralPath $d -Directory | Sort-Object LastWriteTime | ForEach-Object { '=== ' + $_.Name; Get-Content -LiteralPath (Join-Path $_.FullName 'evidence.json') -Raw }
```

Privacy check before reporting: search each file's text for this account's user name, the computer name, `C:\Users`, `S-1-5-21-` and anything that looks like an e-mail address. Paths should appear only as tokens such as `\u003cRUN\u003e`, and identities as `\u003cREDACTED\u003e`. If a file contains private text, do **not** include it; write `PRIVATE-TEXT-FOUND in <run folder name>` instead and tell the owner.

**Step 6 — H6, cleanup, once, last** (expected: `CLEANUP-COMPLETE: … removed`)

Run this only after you have shown the owner your final report (step 7) and the owner has said it is OK to clean up:

```powershell
$d = Join-Path ([IO.Path]::GetTempPath()) 'pcsteward-evidence-task50'
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $d 'Task50-HostEvidence.ps1') -Mode Cleanup
```

If cleanup refuses, leave everything and report the token.

## Step 7 — the report to pass back

Print exactly one fenced block, nothing private, in this shape, and ask the owner to paste it into the PC Steward cloud session:

````
PART-A-RESULT (Task 50, approvals 3 and 4)
script-hash: <the 64-hex line from step 1>
tokens: HASH-OK | PARSE-OK            (or the STOP token)
H2: REDACTIONS: <n>
H2: VERDICT O5: … / AQ1N: … / AQ1M: … / AQ1C: … / A2L3S: …
H3: VERDICT AQ1UAC: …                 (or NOT-RUN-UAC-DECLINED / not run)
UAC prompts seen by owner: <number, as the owner reports it>
cleanup: CLEANUP-COMPLETE: … removed  (or not run / refusal token)
notes: <one line per anything unexpected; no paths or names>
=== <run folder 1>
<evidence.json 1, verbatim>
=== <run folder 2>
<evidence.json 2, verbatim>
````

Then stop. Do not interpret the results, do not suggest fixes, and do not run anything else.
