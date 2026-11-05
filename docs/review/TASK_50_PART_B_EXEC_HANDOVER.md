# Task 50 Part B — Stage B-exec handover for the host session on the owner's PC (revision 1)

Requirements: `FR-UPD-15`, `TM-03/04/06/07/13`; ADR-022. Authority: **approval 1b** of the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) revision 2.1 (§9, the filled form), once the owner has given it. Blocks: §5.3 B4 and B5 with every placeholder filled from the [B-prep result](TASK_50_PART_B_PREP_RESULT.md); line V as in §5.1. Nothing else is authorized.

The text below the line is that session's prompt, verbatim.

---

You are running the pre-approved redeploy stage (B-exec) for the PC Steward project on the owner's Windows PC, as the owner's normal (non-administrator) Windows account. The owner approved exactly the steps below (approval 1b of the Task 50 activation host-session plan, revision 2.1). Follow them literally. You have no authority beyond them.

## What this stage does

One approved, elevated run replaces the installed v1 owner-only test deployment of PC Steward with the v2 candidate built in the previous stage, keeping the owner's data in `%LOCALAPPDATA%\PCSteward` untouched, then checks the result read-only. The owner answers every prompt personally. PC Steward is **not** launched.

## Rules (all mandatory)

1. Run **only** step 1 (B4) once and then step 2 (B5) once, exactly as written. Never retry, re-run, edit, reformat or "fix" a block. No substitutions.
2. Do **not**: run any other command or script, elevate anything yourself, use `runas`, change Git state, delete, move or rename anything (including the work folder, the plan or the attempt record), launch or scan PC Steward, change scheduled tasks, or use the network.
3. **Before step 1**, tell the owner exactly this, and wait until they say they are ready: "Step 1 will show, in order: (1) a UAC prompt for **Windows PowerShell**, verified publisher **Microsoft Windows** — answer **Yes**; (2) a **Root Certificate Store** prompt asking to **DELETE** the certificate for subject `PC Steward Owner-Only Test <GUID>` with thumbprint (sha1) `14F34EFE FA84C367 F25917B4 B28803DC ECE85623` — answer **Yes**; (3) a **Security Warning** about installing a certificate claiming to represent `PC Steward Owner-Only Test <GUID>` with a **new** thumbprint — answer **Yes**. Any other prompt, a second UAC prompt, or a different subject or thumbprint — answer **No**. Please answer promptly and keep PC Steward closed." Also ask them to confirm PC Steward is still closed and has not been opened since the previous stage.
4. **Runner:** step 1 waits up to 15 minutes for the elevated part. Start it **in the background** (for example the Bash tool's background option) with a limit of **at least 30 minutes**, then wait for it to finish. Never stop, kill or restart it, and never start it twice.
5. **Stop rule:** if step 1 does not print an attempt JSON with `"status": "Validated"`, `"nativeExitCode": 0`, `"resultExitCode": 0` and `"resultPhase": "validated"`, or prints `STOP:` or a PowerShell error, do not run step 2 — report exactly what it printed (sanitized, rule 6). `NotStarted`, `StoppedOrUnknown`, `ValidatedPostcheckFailed` and `PendingOrUnknown` are final: no retry, no kill, no deletion of the attempt record.
6. **Privacy:** your report must not contain user names, the computer name, profile paths or SIDs; replace them with `<USER>`, `<PC>`, `<PROFILE>`, `<SID>`. Hashes, thumbprints and the attempt JSON values may be copied.
7. If anything is unclear, stop and ask the owner. Do not improvise.

## How to run each block

Each block runs in its own fresh 64-bit Windows PowerShell 5.1 process fed by a single-quoted heredoc, so the lines run exactly as written. From Git Bash use exactly:

```bash
powershell.exe -NoLogo -NoProfile -NonInteractive -Command - <<'PS'
<line V>
<the block's lines, unchanged>
PS
```

If your shell tool is PowerShell instead, run `& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command -` with line V and the block's lines on its standard input, unchanged. Every block starts with **line V**:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50B'; $wt = Join-Path $wk 'febf1ac'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

## Steps

**Step 1 — B4, the one approved redeploy (in the background, rule 4).** Expected: `CALLER 0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D`, then the owner's three prompts (rule 3), then the attempt JSON with `"status": "Validated"`, `"nativeExitCode": 0`, `"resultExitCode": 0`, `"resultPhase": "validated"`, `userDatabaseSha256` `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`, `"retry": false`, `"launched": false`. Task 48's comparable run took about 1.5 minutes.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D') { 'STOP: caller hash differs' } else { 'CALLER ' + $ch; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 'D9CAA3D8CDE5EFB6648885CCA518F8E2B3EBF5BDB4E531777B4AEDFAF55D1A6E' -ApprovedEncodedCommandSha256 '230C88E2DEA729C6E7037E62ABD58BB2A41485E5A9C21B12DE211B65CCE4A668' }
```

**Step 2 — B5 post-checks (read-only).** Only after a Validated step 1. Expected: `DB … equalsPin=True sidecars=0 pcstewardProcesses=0`; `VALIDATOR-EXIT 0`; `NEW-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 manifest=23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B thumbprint=<new 40 hex, not 14F34EFE…> launched=False`; `STORE My new=1 old=0`, `STORE Root new=1 old=0`, `STORE TrustedPublisher new=1 old=0`; `NEW-SIGNER notAfterUtc=<about 14 days from now> ownerOnlySubject=True`.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + ($dh -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99' -and $dl -eq 159744) + ' sidecars=' + $sd + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2') { 'STOP: validator hash differs' } else { $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifest=' + $v.candidateManifestSha256 + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; $nt = [string]$v.certificateThumbprint; foreach ($sn in @('My', 'Root', 'TrustedPublisher')) { $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); 'STORE ' + $sn + ' new=' + @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count + ' old=' + @($all | Where-Object { $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' }).Count }; $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt }); if ($nc.Count -eq 1) { 'NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *') } else { 'STOP: new signer not found once in CurrentUser My' } } else { 'STOP: validator did not pass' }
```

Then stop. Do not launch PC Steward, do not clean up, and do not turn scheduled checks back on.

## Step 3 — the report to pass back

Print exactly one fenced block in this shape, with nothing private (rule 6), show it to the owner, and pass it back to the cloud session that started you:

````
PART-B-EXEC-RESULT (Task 50, approval 1b, B-exec handover revision 1)
B4: CALLER <hash> | <the attempt JSON, verbatim or compact>
prompts seen by owner (in order): <e.g. UAC PowerShell/Microsoft Windows=Yes; Root delete 14F34EFE…=Yes; Root install new=Yes; other=none>
B4 minutes ≈ <n>
B5: <DB …> | VALIDATOR-EXIT <n> | <NEW-DEPLOYMENT …> | <STORE My …> | <STORE Root …> | <STORE TrustedPublisher …> | <NEW-SIGNER …>
notes: <one line per anything unexpected; no paths or names>
````

After a stop, fill the lines up to the stopping step, put the exact (sanitized) output on that line, and write `not run` for the rest. Then stop. Do not interpret the results, suggest fixes or run anything else.
