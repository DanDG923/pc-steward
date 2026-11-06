# Task 50 — handover (2026-11-06)

This note is for the next session, cloud or local, picking up Task 50. Requirements: `FR-UPD-15`, `TM-03/04/07/13`. Read `AGENTS.md` first.

**Nothing here is approved for execution.** Every host step needs the owner's own approval, given in the session.

## 1. Branches and PRs

| Branch | Head | PR | State |
|---|---|---|---|
| `claude/sweet-franklin-j3mw4a` (Task 50) | `5606da2` | DanDG923/pc-steward#12 (draft) | Activated in code, but inert in the UI (see §3). Windows CI green up to `bc24a20`, except one unrelated timing flake (`InheritedWriterUsesOneBudgetAndDoesNotClaimDescendantExit`), whose failed job passed on its single re-run. |
| `claude/task-73-ui-overhaul` (Task 73) | `ec9340f` | DanDG923/pc-steward#13 (draft, stacked on #12) | **Deliberately 28 commits behind Task 50.** It has not taken the activation change. See §5, item 4. |

Never merge to `main`. Merging is not authorized.

## 2. Owner's PC: current state (end of 2026-11-06)

- **Installed.** The **activated** owner-only test deployment, built from `ebb40618debccac3f489571f9c6f7e709f3a2e93` (probe included):

| Field | Value |
|---|---|
| Origin and helper protocol | `pinned-unsigned-v2`, protocol 2 |
| Manifest | `9F6CC13668DE533CF070731C30E12E006D66EB9E1B3C3AB3E8C8A566FA0DF84A` (103 files) |
| Capability statement | `50F169DD…654B` (two tokens) |
| Ledger | `CBAB8C0AEB29DB34076F16A9176674CA08DA4A655447988B1368B456733AC852` |
| Build record | `04D4BF87810EE2D89DBF78913592E14BFAA6099770AF1AEE5EC98E52C990EBB9` |

- **Signer.** `67B59B4C9BC3A006FBE39A72967E05F7B8C375DA`. It is present once in CurrentUser My, Root and TrustedPublisher, and **expires 2026-11-20T19:53:44Z**. No older test signer remains (`14F34EFE…`, `ED6FBCE8…`, `473DB701…`).
- **Database.** `%LOCALAPPDATA%\PCSteward\pcsteward.db` is `78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE`, 200,704 B. It migrated at the first launch, C0; there are no sidecars and the scheduled check is absent. Backups: the CB.4 copy under `%USERPROFILE%\PCSteward-Task50C\dbcopy\` and the B2 copy (pre-migration, `CEB917F4…`) under `PCSteward-Task50B\dbcopy\`.
- **Work folders** (user-writable; kept as evidence):
  - `%USERPROFILE%\PCSteward-Task50B\`: worktrees `febf1ac\` and `r2-385efe0\`, plus the candidate, plan, plan-r2 and dbcopy folders;
  - `%USERPROFILE%\PCSteward-Task50C\`: worktree `ebb4061\`, plus the candidate, plan and dbcopy folders.
- **Protected execution roots** (Program Files, admin-only; kept as evidence): `…OwnerOnlyTest Execution 44f5b22d…` (the failed B4), `ae66a440…` (R2.5) and `3cf323d3…` (CB.8).
- **Chocolatey.** 2.7.5, matching the pinned release tree, with no Chocolatey-managed target package. `chocolatey.config` contains `containsLegacyPackageInstalls`, value unobserved, and an **empty `cacheLocation`**.

## 3. What has been proven, and what has not

**Done:**

| Item | Record |
|---|---|
| Part A host evidence | PASS (2026-11-03) |
| Part B | Complete through the [recovery plan](TASK_50_RECOVERY_PLAN.md): R1, then a fresh R2 install, then R3 |
| C0 smoke launch | PASS |
| Activation change | `d423dff` and `ebb4061`, owner-approved under obligation 8 |
| C-B redeploy | Validated |
| C-pre | No reviewed update outcomes |
| C-check | PASS |

**C-run, the exact-artifact probe: OVERALL FAIL** ([Part C plan](TASK_50_PART_C_PLAN.md) §11).
- E1 (token), A3 (capability tokens), E3 (signed-helper transport across the real UAC prompt) and E5 (nothing changed) all **passed**.
- E2 **failed**: the observer refused at step 6, `Configuration`, with `MachineInputsUnsupported`.
- Decision 2 applies: use stops, and that run is not repeated.

**Diagnosed by D1** (read-only; §12): two independent causes.
1. **Model gap: fixed in code** as Profile V2 revision 2.1 (`bc24a20`, with review fixes in `5606da2`; record `TASK_50_PROFILE_V2_LEGACY_KEY_REVISION.md`). `containsLegacyPackageInstalls` is now admitted only when absent or exactly `true`. Chocolatey 2.7.5 never reads it; this was independently re-verified at `194ac626`. The fix is **not deployed**.
2. **Host precondition:** `cacheLocation` is empty, which Profile V2 refuses by design. Only the owner can change it, outside PC Steward.

**The UI stays inert after activation.** Nothing in the desktop composes a Chocolatey review or dispatch, so Confirm stays disabled. D4 = G-0, so the guard is procedure only, and real Chocolatey use needs a probe PASS plus the owner's final admission approval (obligation 8 terms). The A-Q1 real-launch residuals stay open (G9).

## 4. Lessons that must carry over

- **Elevated and certificate-store steps run only from a Windows PowerShell window the owner opens.** A session started by the Claude desktop app is a packaged process with a private per-user view, and the B4 failure came from that. The tooling now **refuses a packaged process** in NewPlan, CheckOnly and the elevated run (`GetCurrentPackageFullName` must return 15700). Read-only checks may run anywhere, but a packaged reader can see stale per-user state.
- **List every possible prompt in a host plan, including the rollback's own Root-delete prompt.** Saying No to that prompt left B4's rollback incomplete.
- Every step runs exactly once in a new window, with hash-pinned scripts. Outputs are sanitized: no names, paths or SIDs. Plan files are never pasted.
- Pushing goes to the designated branches only.
- A host block the owner runs without typing an approval first is recorded as the owner's go-ahead, and noted as such.

## 5. Open items (each needs an owner decision)

1. **Cause 2: `cacheLocation`.** The owner may set a protected, existing cache directory with Chocolatey's own tooling, outside PC Steward. PC Steward never changes Chocolatey.
2. **Re-run the probe after both fixes.** This needs a new candidate build and redeploy including `bc24a20`, then C-pre, C-check and C-run under new approvals. The C-B flow in Part C plan §10 is the template; refresh every pin.
   - **Deadline:** the redeploy's preflight needs the current signer to be valid, so it must start before **2026-11-20T18:53:44Z** (one-hour CheckOnly margin).
   - **After that:** use a separately approved remover, then a fresh install (the R1/R2 pattern).
   - The host's actual `containsLegacyPackageInstalls` value is unobserved. A no-values read-only check could confirm it is `true` before a re-run.
3. **Desktop composition of a Chocolatey review.** This is unbuilt future scope, needed before any real Chocolatey update from PC Steward. It needs explicit task scope.
4. **Task 73 merge.** Merging Task 73's "Update Chocolatey to X" offer UI with the activated Task 50 requires re-verifying design §8.6 gate 3 first (`TASK_50_ACTIVATION_CHANGE.md`).
5. **Cleanup** of the work folders, old worktrees and execution roots. Each needs its own approval.
6. **A target package (G9).** None exists on the host. With only 2.7.5 listed, no real Chocolatey upgrade can be exercised.

## 6. Where to read

- `docs/STATUS.md` (top entries)
- [Part C plan](TASK_50_PART_C_PLAN.md), §1–§13 and every result
- [Recovery plan](TASK_50_RECOVERY_PLAN.md)
- [Failure record](../failures/task-50-part-b-exec.md)
- [Activation change record](TASK_50_ACTIVATION_CHANGE.md)
- [Qualification probe spec](TASK_50_QUALIFICATION_PROBE_SPEC.md), §8–§9
- [Activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md), for history only; its §5.3 B4/B5 blocks are superseded
