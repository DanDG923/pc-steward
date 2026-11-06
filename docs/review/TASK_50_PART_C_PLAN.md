# Task 50 Part C — exact-artifact qualification run: route and decisions (revision 1, draft)

Requirements: `FR-UPD-15`, `TM-03/04/07/13`. ADRs: ADR-018 (cross-note), ADR-022, ADR-025 (amendment accepted 2026-11-04). Sources: the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) §6 and §9 approval 2, the [qualification probe spec](TASK_50_QUALIFICATION_PROBE_SPEC.md) §8–§9 (S7 obligations, D4), the [exact-artifact test design](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) C1–C7, and the [recovery plan](TASK_50_RECOVERY_PLAN.md) results. **Nothing here is approved.** This revision sets out the route and asks for decisions. Exact host blocks follow only after those decisions and the code they need.

## 1. Where things stand (2026-11-06)

- **Installed.** The v2 owner-only test deployment (`pinned-unsigned-v2`, helper protocol 2, manifest `23D7A71C…`), built from `febf1ac`.
  - Signer `473DB701…53FD`, which expires **2026-11-20T10:43:26Z**.
  - The database is unchanged (`CEB917F4…`). PC Steward has never been launched on this deployment.
- **Done.** Part A PASS (2026-11-03) and Part B (2026-11-06, through the recovery route).
- **The probe (S1–S6)** is implemented, reviewed and Windows-CI-tested on the Task 50 branch. It first landed in `5ae2de1`, **after** `febf1ac`.

## 2. Why Part C cannot run on the current install

The installed desktop predates the probe. Started with `--qualify-chocolatey-helper`, an old desktop ignores the flag, starts its normal UI and writes no result file (spec §6, "Old desktop, new helper"). S7 obligation 2 therefore applies: **C3 is never offered on the Part B deployment**, and C1 stops with `STOP: not a probe build`.

Owner Decision 3 (c) (2026-11-03) fixes the order: the probe ships **in the activation build**, and its first run is on the **activated deployment**. No Chocolatey review is confirmed until it passes.

## 3. The route, in order

| Step | What | Who / where | Approval |
|---|---|---|---|
| **C-D** | Owner decisions **D4** (code guard) and **G9** (activate with the A-Q1 real-launch residuals open?); see §4 | Owner | Decision text only |
| **C-A** | **Activation change** (code). Admit the Chocolatey routes: `HasSupportedMode`, `CapabilityOperationAllowlist` and dispatcher `Supports`, with their pins changed deliberately and reviewed. Add the D4 guard if one is chosen. The G3 builder record gains the "probe included" mark (S7 obligation 1). Add the S7 host tooling (C1–C7) as a reviewed, pinned script. Independent review and Windows CI | Cloud | ADR-031 decision 7 already approves activation once its gates are met. The owner's final admission approval (obligation 8) comes after Part C passes |
| **C-B** | **Redeploy the activated build**, keeping user data. This repeats the Part B stages with the reviewed tooling now in place. The v2 deployment is complete, so the normal v3 redeploy mode applies (`pinned-unsigned-v2` → `pinned-unsigned-v2`). Prep: worktree at the activation commit, G3 build, database copy, NewPlan, CheckOnly. Exec: one elevated run | Owner's own Windows PowerShell windows | Two approvals, as for 1a and 1b |
| **C-pre** | Host preconditions (S7 obligation 3). **This is the first normal launch.** The owner opens PC Steward, reads Activity → "Reviewed update outcomes", and closes it. No scan, no refresh, no scheduled check. A first launch may migrate the database, so the C2 baseline is taken **after** this step | Owner | Approval 2a |
| **C-run** | C1 pins, including the probe-build mark. C2 baseline (database plus `ProgramData\chocolatey` tree hash). C3 probe: one launch of `PCSteward.Desktop.exe --qualify-chocolatey-helper`, with exactly **one UAC prompt**, for `PCSteward.ElevatedHelper.exe`. C4 repeats C2. C5 reads the result file. C6 interprets it under spec §8. C7 cleans up `%TEMP%\pcsteward-exact-artifact`. With G-a or G-a′, the PASS-record copy | Owner's own console | Approval 2 (activation plan §9 template, filled) |

**Time bound.** C-B's preflight needs the current signer's signatures to be Valid. CheckOnly requires at least one hour of margin, so C-B exec must start before **2026-11-20T09:43:26Z**. If that is missed, the route is the R1/R2 pair instead (remover, then a fresh install of the activated build). The tooling for it now exists, but it needs its own approvals.

**Prompts.**
- **C-B exec:** UAC for Windows PowerShell. Then a Root-store **DELETE** prompt for the current signer `473DB701 E6349AF1 8F672151 E44AC166 F95C53FD` (the remover takes out the old deployment). Then the Security Warning for the new signer. Only on failure: a Root **DELETE** prompt for that same new signer, so the rollback completes.
- **C3:** exactly one UAC prompt. It names the signed helper, and its details show `--kind qualify-chocolatey-helper` right after the program path.
- Anything else → **No**.

## 4. Decisions needed now

**D4: code guard between the activation deploy and a recorded probe PASS** (spec §9).

| Option | Summary |
|---|---|
| G-0 | Procedure only. No code: until the probe passes, the owner does not confirm any Chocolatey review. |
| G-a | The PASS record lives in `%LOCALAPPDATA%\PCSteward`. Confirm stays disabled and elevation is refused without it. This needs factory-reset and ADR changes, and a reset deletes the record. |
| G-a′ | The PASS record lives in the ADR-022 state root, which only administrators can write. An elevated copy step is needed per PASS, plus validator, remover and ADR-022 note changes. |

**Recommendation: G-0.** On this host nothing can reach a real Chocolatey update before the probe runs:
- the reviewed release list holds only 2.7.5, which is already installed;
- there is no Chocolatey-managed target package (G9).

So the window that G-a or G-a′ would guard is empty in practice. Both also add reviewed code, ADR changes and, for G-a′, an extra elevated step. The guard can be reconsidered before any target package or newer release is added.

**G9: may activation proceed with the A-Q1 real-launch residuals open?** (activation plan §11).

The residuals are a native held DLL load, PowerShell helper reads and an elevated launcher holding files. They need a real Chocolatey client launch, which cannot happen without a target package or a newer listed release.

**Recommendation: yes, proceed.** The residuals stay recorded as open. Each one blocks any real Chocolatey update until it is exercised, for example in a VM or with a separately approved target package. Activation plus the probe proves UAC and pre-launch enforcement only (spec §8), and every later step and review will say so.

**Optional C0: an app-only smoke launch of the current v2 install now.** Open PC Steward, look at Overview and Activity, and close it within about a minute. No scan, no refresh, no scheduled check. It would catch a v2 UI problem before the activation work starts. It is the deployment's first launch, which may migrate the database (C-B re-pins it anyway). **Recommendation: optional, low risk.** It needs its own short approval.

## 5. What happens after the decisions

1. **Cloud.** The activation change (C-A) on a new branch from the Task 50 head: code, tests, the C1–C7 script with pins, independent review and Windows CI. The G9 decision is recorded in the admission record.
2. **Plan revision 2.** The exact C-B prep and exec blocks (with the redeploy prompts above), the C-pre instructions and the C-run blocks, with approvals 2a and 2 filled in.
3. **Owner runs.** C-B prep, then exec, before 2026-11-20 09:43Z; then C-pre and C-run. Each step runs once from the owner's own console, under the stop rules already used in Parts A and B.

## 6. Owner decisions (2026-11-06): "D4 G-0, G9 yes, C0 yes"

- **D4 = G-0.** No code guard; no §5.3a reader is built. Until a recorded overall probe PASS on the activated deployment, the owner confirms **no** Chocolatey review (Decision 2 as procedure). Reconsider before any target package or newer listed release is added.
- **G9 = yes.** Activation may proceed with the A-Q1 real-launch residuals open: a native held DLL load, PowerShell helper reads and an elevated launcher holding files. They stay recorded as open, and each blocks any real Chocolatey update until it is exercised.
- **C0 = yes.** One app-only smoke launch of the current v2 install, under the scope in §7.

## 7. C0 — app-only smoke launch (approved scope)

The owner does this personally:
1. Open `C:\Program Files\PC Steward\PCSteward.Desktop.exe` normally, as the normal user, not elevated.
2. Look at **Overview** and **Activity** only.
3. Close the window normally within about a minute.

Not part of C0:
- **Not allowed:** scan, source refresh, review, update, clear or reset; turning on scheduled checks; any change in Settings.
- **Answer No:** any UAC prompt.
- **Dismiss:** any onboarding choice that would turn on scheduled checks.

After closing, one read-only check runs in a normal Windows PowerShell window (block C0-check below). A first launch may migrate the database, so a changed hash is reported, not treated as a failure. C-B re-pins the database at its NewPlan.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; 'DB sha256=' + $dh + ' bytes=' + (Get-Item -LiteralPath $db).Length + ' unchanged=' + ($dh -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99') + ' sidecars=' + @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count + ' scheduledTask=' + @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count
```

Report back: what the windows showed (anything unexpected), any prompt that appeared, and the C0-check line.

### Result C0 (2026-11-06)

The owner launched the installed v2 desktop once and closed it. C0-check printed `DB sha256=78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE bytes=200704 unchanged=False sidecars=0 pcstewardProcesses=0 scheduledTask=0`.

The database changed from 159,744 to 200,704 bytes. This is consistent with the first-launch schema migration that §7 expects. It closed cleanly: no WAL/SHM/journal sidecar, no PC Steward process left, and no scheduled task created. The new database pin for C-B is `78697727…55BE` / 200,704 B. C-B's NewPlan re-reads it, and B2's copy step backs it up first. Owner's note: "two screens looked good". Overview and Activity rendered normally, and the owner confirmed no UAC prompt appeared. **C0 PASS** (app-only smoke).
