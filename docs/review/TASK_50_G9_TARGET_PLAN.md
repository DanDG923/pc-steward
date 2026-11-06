# Task 50 — G9 real-launch residuals and target choice: plan (revision 1.1, draft)

Requirements: `FR-UPD-15`, `FR-UPD-01/08/09`, `TM-03/04/07/13`. ADRs: ADR-025 (real-update and installed-state tests), ADR-031 (design §5 and §8.7), ADR-022. Sources: [handover](TASK_50_HANDOVER_2026-11-06.md) §3 and §5, [Part C plan](TASK_50_PART_C_PLAN.md) §4 and §6, [activation change record](TASK_50_ACTIVATION_CHANGE.md) §1.2–§1.3, [exact-artifact test design](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) §2, [ADR-031 client self-update design](TASK_50_ADR031_CLIENT_SELF_UPDATE_DESIGN.md) §8.7 note, [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md), [roadmap](../product/PC_STEWARD_2_ROADMAP.md) §6, [delta](../product/PC_STEWARD_2_DELTA.md) §6.

**Nothing here is approved for execution.** This is a planning record written in the cloud. It changes no code. It asks the owner for decisions (§9). Every host step that follows needs its own exact approval, and the owner runs each one in his own normal Windows PowerShell window (handover §4).

## 1. Summary

G9 was decided "yes" on 2026-11-06: activation went ahead with the real-launch residuals open, and each one blocks any real Chocolatey update until it is exercised. Closing them needs a **real Chocolatey launch through PC Steward**, and today three things stand in the way:

1. **No target.** The host has no Chocolatey-managed package, and the reviewed release list holds only 2.7.5, which is already installed.
2. **No driver.** Nothing in the desktop composes a Chocolatey review or dispatch, so Confirm stays disabled (handover §5 item 3). Even with a target, the real path cannot be reached.
3. **Earlier gates.** The probe has to pass on a redeploy that carries `bc24a20`, `cacheLocation` has to be fixed by the owner, and obligation 8's final admission approval has to follow (handover §5 items 1–2).

**Recommendation in one line.** Close the **package-route** residuals on the owner's own PC with **one owner-approved test package**, driven by a **real desktop review composition** built as its own task. **Defer the client-route** (Chocolatey self-upgrade) residuals until a newer Chocolatey release is reviewed; only then choose a VM.

## 2. What G9 covers

### 2.1 Package route (a Chocolatey-managed package upgraded by the helper)

| # | Residual | Why it needs a real launch | Source |
|---|---|---|---|
| P1 | Native held DLL load: the release tools (`7z.exe` and its native `7z.dll`) load while the helper holds them with `FileShare.Read` | Only a package script that extracts an archive makes Chocolatey run them | Exact-artifact design §2; activation plan §6 |
| P2 | PowerShell reads of held `helpers` files (`chocolateyInstaller.psm1` and the helper scripts) while held | Only `powershellHost` running a package script imports them | As P1 |
| P3 | An elevated launcher holding files: the helper keeps the client binding held across its own launch of `choco.exe` until exit | Only a real `choco upgrade` launch | As P1; `ChocolateyHeldClientBinding` has **no** fallback (helper endpoint record §9) |
| P4 | ADR-025: mode enforcement at launch, stages C, D and Launch on the real client, installed-state outcome, and post-update verification before Success | Needs a real upgrade with a newer Community version available | ADR-025 follow-up controls |

### 2.2 Client route (Chocolatey upgrading itself)

| # | Residual | Source |
|---|---|---|
| CR1 | A real client self-upgrade under the held binding | ADR-031 design §8.7 (as cited in the activation record §1.2) and §5 |
| CR2 | A second reviewed release on the compiled list (a reviewed re-pin) | As CR1 |
| CR3 | Residue names after a real self-upgrade, `lib\chocolatey\chocolatey.nupkg` byte-equality with the asset, and the release-at-resume run end to end | ADR-031 design §5 "Host evidence still needed" |

ADR-031 design §5 says CR1 and CR3 run "on a disposable VM (never the owner host without approval)". With one listed release, the client route is inert by design, so **CR1–CR3 cannot start before CR2**.

## 3. What has to happen first (dependency chain)

These are already open in the handover. G9 depends on them; it does not replace them.

| Order | Item | Who | Note |
|---|---|---|---|
| 1 | Owner sets a protected, existing `cacheLocation` with Chocolatey's own tooling (handover §5 item 1) | Owner, outside PC Steward | Profile V2 refuses an empty value by design. PC Steward never changes Chocolatey |
| 2 | Redeploy a build that carries `bc24a20` and `5606da2`, then C-pre, C-check and C-run (handover §5 item 2) | Owner, under new approvals | **Must start before 2026-11-20T18:53:44Z**, or the remover plus fresh-install route applies |
| 3 | Recorded overall probe PASS | Owner | Until then, no Chocolatey review is confirmed (D4 = G-0) |
| 4 | Obligation 8: the owner's final admission approval | Owner | Comes after the probe PASS |
| 5 | Desktop review composition (handover §5 item 3) | Cloud task, needs explicit scope | The driver for every G9 real launch (§6) |

**Recommendation:** do not wait for G9 work before items 1–3, and do not change the host's Chocolatey tree (G9-T1) until item 2's probe re-run has a recorded result. The signer deadline applies to item 2 only, and the G9 work below cannot be ready in time to share that redeploy safely.

## 4. Target options

| Option | What it is | Closes | Costs and risks | Fits the existing owner direction? |
|---|---|---|---|---|
| **A. Approved test package on the owner's PC** | The owner installs one small Community package at an **older** version with Chocolatey's own tooling. PC Steward later upgrades it once, through the real review and dispatch path | P1–P4 on the real host, with the real UAC prompt and the real requester token | Installs one ordinary program on the owner's PC; Chocolatey runs that package's scripts elevated during the owner's install; the Chocolatey tree changes (a new `lib\<id>`, possibly shims in `bin`), so host evidence is re-run afterwards | Yes: roadmap §6 allows "a short owner-approved list of small, harmless test packages, each live run approved separately" |
| **B1. Local VM** (VirtualBox or VMware Workstation on the host) | A disposable Windows 11 guest with Chocolatey 2.7.5 and a test package | P1–P4 in the guest, and later CR1–CR3 | Installs a hypervisor and its drivers on the owner's PC; a Windows guest licence; evidence is from the guest, not the owner's host; the host is Windows 11 Home, so there is no Hyper-V or Windows Sandbox | **No**: roadmap §6 says "VM testing is excluded (owner direction)". Choosing B1 reverses it |
| **B2. Cloud Windows VM** | A rented Windows VM, used over Remote Desktop | As B1 | Needs a cloud account and spending; PC Steward and its test signer leave the owner's PC; evidence is not from the owner's host | **No**, as B1; also adds an account and spending |
| **B3. GitHub-hosted Windows runner** | Already used for Windows CI | Partial loader and sharing evidence only | Runners run as an administrator with UAC off, so there is no linked token and the real requester path (E1) refuses; preinstalled Chocolatey and packages differ from the pinned release | Allowed, but it **cannot close any residual by itself**; at most a supplement |
| **C. Defer** | Keep the residuals open | Nothing | The Chocolatey routes stay unusable for real updates | Yes |

**Recommendation: A for the package route; C (defer) for the client route.**
- A is the only option that produces evidence on the host the product runs on, with the real UAC prompt and requester token that the earlier work was built around.
- A needs no reversal of the "no VM" direction and no new account or spending.
- The client route cannot start until a newer release is reviewed (CR2). The VM question can wait until then; §9 decision G9-D3 records that.

## 5. Choosing the test package (option A)

### 5.1 Criteria

A candidate must meet **all** of these, checked from the package's public Community page, nuspec and scripts (read only; nothing is downloaded and run):

1. **Route fit.** A Chocolatey **Community** package, machine scope, stable versions only; not `chocolatey` or `chocolatey.extension` (Profile V2 deny-set).
2. **Two moderated versions.** At least two approved versions, so the owner installs the older one and PC Steward offers the newer one. The newer one must parse to the supported normalized stable version and compare strictly newer.
3. **Exercises P1 and P2.** Its `chocolateyInstall.ps1` uses Chocolatey's own helpers to **extract an archive** (for example `Install-ChocolateyZipPackage` or `Get-ChocolateyUnzip`), so the held `7z` tools and `helpers` modules are really used. A package with no script would leave P1 and P2 open.
4. **Checksums.** Every download in its script carries a checksum, so the fixed `--require-checksums` does not fail it after the claim. An embedded package (the archive inside the nupkg) is preferred: no third-party download at all.
5. **No dependencies**, so `extensions`, `hooks` and the profile fingerprint stay as they are. In particular, no `chocolatey-core.extension` or other extension dependency.
6. **Portable and quiet.** No MSI or EXE installer, no service, no scheduled task, no driver, no shell or file-association change, no reboot, no network use when started. Small (a few MB).
7. **Not relied on.** Not already installed on the host by any route (WinGet, Store, manual). Delta §6: never use a program the owner already relies on as a disposable target.
8. **Removable.** `choco uninstall` removes it cleanly, run later by the owner if he wants it gone. PC Steward never uninstalls Chocolatey packages.
9. **No `.pin`, no remembered arguments needed**, and nothing in `lib-bkp` that starts with its ID (Profile V2 store rules).

**If nothing meets all nine.** Relax criterion 4's "embedded preferred" first (a checksummed download is acceptable). Keep criterion 3: without it P1 and P2 stay open. Any further relaxation is an owner decision in G9-D2b.

7-Zip, chosen as D3 for the WinGet live tests, fails criteria 6–7 here: it installs shell integration, and as the archive tool the owner may rely on it.

### 5.2 Research step (cloud, read-only)

Step **G9-R1**: read the public Community pages and package sources for a short list of portable candidates, record each against §5.1 with links and version numbers, and propose **one** primary and **one** alternate in a record `TASK_50_G9_TARGET_CANDIDATES.md`. No package is downloaded, installed, run or queried with Chocolatey. The owner then picks one (decision G9-D2b). Nothing in this plan names a package as approved.

## 6. Driving the real launch

The only path to a real launch is PC Steward's own review → claim → dispatch. Two ways to get there:

| Way | What | Assessment |
|---|---|---|
| **6a. Build the desktop review composition** (handover §5 item 3) | Compose the Chocolatey package-route review, consent and dispatch in the desktop, using the existing production intent resolver (obligation 6), review UI (obligation 5) and runtime-limit route (L1). The first real update of the target package is the G9 run | **Recommended.** It is needed before any real Chocolatey update anyway, and it tests the production path, not a test-only one |
| 6b. A test-only "real update" mode, like the probe | An owner-only command-line mode that builds one fixed request | Not recommended. It would need a new standing exception, its evidence would not be the production path, and 6a is still needed later |

For 6a, the composition task must also:
- record evidence for **each** residual P1–P4 separately (not only "update succeeded");
- keep D4 = G-0 safe by deploying the composition **only after** a recorded probe PASS (§3 item 3), so the window D4 would guard stays empty;
- reconcile with the Task 73 branch, which has the client-route offer UI and must re-verify design §8.6 gate 3 before merging (handover §5 item 4).

## 7. Proposed route (each host step under its own approval)

| Step | What | Where | Approval |
|---|---|---|---|
| G9-0 | Owner decisions in §9 | Owner | Decision text only |
| G9-R1 | Target candidate research (§5.2) | Cloud, read-only | Covered by G9-D2a |
| G9-R2 | Desktop review composition task (§6a): code, tests, independent review, Windows CI | Cloud | Needs explicit task scope (G9-D4) |
| G9-T1 | Owner installs the chosen package at the older version with `choco install <id> --version <old>` in his own elevated Windows PowerShell window. Runs **only after the probe re-run's C-run has a recorded result** (handover §5 item 2), and never in the same window as the `cacheLocation` change, so the deadline-bound re-run observes the host state D1 diagnosed | Owner's PC, outside PC Steward | Its own approval; the exact block, with every prompt listed, is written after G9-D2b |
| G9-T2 | Straight after G9-T1: re-run the read-only host evidence (Default cases) to confirm the tree still matches the pinned release and the profile still admits it | Owner's PC, non-elevated | Its own approval |
| G9-T3 | Redeploy the composition build (C-B pattern; refresh every pin; new signer); C-pre; probe re-run if the binding set changed | Owner's PC | Its own approvals, as for C-B |
| G9-T4 | **One** real update of the target package through PC Steward: scan, review, confirm, **exactly one UAC prompt** for the signed helper, then post-update verification; evidence for P1–P4 | Owner's PC | Its own approval, after obligation 8 |
| G9-T5 | Optional cleanup: owner uninstalls the target with Chocolatey; work folders as in handover §5 item 5 | Owner's PC | Each its own approval |

Every host plan written from this route must list every possible prompt (including any rollback prompt), run each step exactly once in a new window with hash-pinned scripts, sanitize outputs, and refuse a packaged process (handover §4).

Step names: `G9-T1`–`G9-T5` are this plan's own host steps; they are not the `H1`–`H6` blocks of the host-evidence plan or of other Task 50 plans. Residual IDs: `P1`–`P4` (package route) and `CR1`–`CR3` (client route) are not the Part C plan's C-A/C-B/C-run steps or the C1–C7 probe tooling.

## 8. Stop rules

- Any step that does not match its plan stops. No retry without a new approval.
- A G9-T4 failure leaves the durable claim and conflict record as designed. PC Steward does not retry or repair. The owner recovers the package, if needed, with Chocolatey's own tooling.
- If G9-T1 or G9-T2 shows the tree no longer matches the pinned release, or the profile refuses, the route stops and is re-planned.
- A residual that the run did not demonstrably exercise stays **open**, even if the update itself succeeded.

## 9. Decisions Dan needs to make

| # | Question | Options | Recommendation |
|---|---|---|---|
| **G9-D1** | Where are the **package-route** residuals (P1–P4) closed? | (A) one approved test package on your own PC; (B1) a local VM; (B2) a cloud VM; (C) defer | **A.** Real host, real UAC and token; no change to the "no VM" direction; no new account or cost |
| **G9-D2a** | May the cloud research the test-package candidates now (§5.2, read-only web reading of Community pages)? | Yes / No | **Yes.** No host action, nothing downloaded and run |
| **G9-D2b** | (After G9-R1) Which candidate is the target? | Primary / alternate / neither | Decided after the research record |
| **G9-D3** | What happens to the **client-route** residuals (CR1–CR3)? | Defer until a newer Chocolatey release is reviewed and re-pinned, then decide VM; or decide on a VM now | **Defer.** They cannot start before a second reviewed release exists |
| **G9-D4** | How is the real launch driven? | (6a) build the desktop review composition as its own task; (6b) a test-only mode | **6a**, with the evidence and ordering rules in §6 |
| **G9-D5** | Does the probe re-run redeploy (handover §5 item 2) go ahead before 2026-11-20 on its own, without waiting for G9? | Yes, separately / wait and combine | **Yes, separately.** G9 cannot be ready in time to share it, and missing the date means the remover plus fresh-install route |
| **G9-D6** | Does D4 = G-0 (no code guard) stay once a target package exists? | Keep G-0, on condition the composition deploys only after a recorded probe PASS; or build a guard | **Keep G-0 with that condition.** The guarded window stays empty |

None of these decisions approves a host step. Each host step in §7 still needs its own exact approval.

## 10. Revision history

- **1.1 (2026-11-06).** Independent review of `76e0752` (one Medium, one Low): G9-T1 is ordered after the probe re-run's recorded result and no longer offered alongside the `cacheLocation` change (Medium); the CR3 source is corrected to ADR-031 design §5; residual and step IDs are renamed `CR1`–`CR3` and `G9-T1`–`G9-T5` to avoid clashes (Low); the §5.1 relaxation order is stated (owner point).

## 11. Owner decisions (2026-11-06): "Agree all"

- **G9-D1 = A.** The package-route residuals are closed on the owner's own PC with one approved test package.
- **G9-D2a = yes.** Done separately: the candidate research is in draft PR #16 (`TASK_50_G9_TARGET_CANDIDATES.md`). Primary: ripgrep, 14.1.0 → 15.2.0. Alternate: fd.
- **G9-D2b = ripgrep, provisionally.** The owner asked whether the package could be forked to resolve security issues. A fork is not used: Profile V2 admits only the Community source, so a forked package could not be the target. The security concern is handled instead by a read-only check that the package's embedded archives match the upstream release digests. Criterion 7 (not already installed or relied on) is still for the owner to confirm before G9-T1.
- **G9-D3 = defer.** CR1–CR3 wait for a reviewed newer Chocolatey release.
- **G9-D4 = 6a.** The desktop review composition is built as its own task.
- **G9-D5 = yes, separately.** The probe re-run redeploy goes ahead before 2026-11-20, independently of G9.
- **G9-D6 = keep G-0**, on the condition that the composition is deployed only after a recorded probe PASS.

None of these approves a host step.
