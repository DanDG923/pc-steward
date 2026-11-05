# Task 50 Part B — Stage B-prep result (2026-11-05)

Requirements: `FR-UPD-15`, `TM-03/04/06/07/13`; ADR-022. Approval 1a (owner, 2026-11-05). Run by a child Claude session in the owner's Remote Control bridge environment under the [B-prep handover](TASK_50_PART_B_LOCAL_AGENT_HANDOVER.md) revision 2; the owner approved each command in the Claude app.

**Outcome: PASS — every step B0.1–B3c returned its OK token; B4 was not run.** No UAC or certificate prompt appeared; no Git sign-in. The work folder `%USERPROFILE%\PCSteward-Task50B` is kept unchanged for B-exec.

| Step | Result |
|---|---|
| B0.1 preflight | `PREFLIGHT-OK` (1 run): Windows PowerShell 5.1.26100.9818 x64, not elevated, 0 PC Steward processes; `PCSteward.ScheduledCheck` Absent; database present, 0 sidecars; SDK 10.0.403 only; no Git hooks; execution policy Undefined/Undefined; old signer in My, NotAfter 2026-11-13T20:18:09Z |
| B0.2 fetch + worktree | `FETCH-EXIT 0`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0` |
| B0.3 verify | HEAD `febf1ac95c207aadf0f3efe742b854d81f903037`, DIRTY 0, all ten scripts in the CRLF column, capability `0772DAD8…D9C7`, `B0-OK` |
| B1 build | `BUILD-EXIT 0`, validation Pass, `pinned-unsigned-v2`, helper protocol 2, **103 files, 51,474,164 bytes**, commit febf1ac, not dirty, SDK 10.0.403 exact pin, unsigned, framework-dependent; **manifest SHA-256 `23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B`** (file hash equal); build 44 s |
| B2 database copy | 159,744 bytes, **SHA-256 `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`** (copy equal; the same hash as the Task 49 baseline), no sidecars, valid SQLite header (WAL journal mode recorded in the header; no `-wal`/`-shm` files present) |
| B3a NewPlan | schema `pc-steward-exact-redeploy-v3`, **plan SHA-256 `D9CAA3D8CDE5EFB6648885CCA518F8E2B3EBF5BDB4E531777B4AEDFAF55D1A6E`**, database pin equals the copy, manifest/signer/origins checked, not executed, not elevated |
| B3b CheckOnly | `Pass`; **encoded-command SHA-256 `230C88E2DEA729C6E7037E62ABD58BB2A41485E5A9C21B12DE211B65CCE4A668`**, length 29,296 (≤ 30,000); program SHA-256 `B79E4F52…11B5`; bootstrap template `A3EF83B7…DE4A`; six scripts equal B0.3; old ledger `AC1BF975…80CB`, old thumbprint `14F34EFE…5623`; scheduled check AbsentOrDisabled; SQLite not loaded; not elevated; execution root and attempt record absent; **execution root id `44f5b22d4271463eb91f9a00cda9ecdc`** |
| B3c old-deployment dry run | `VALIDATOR-EXIT 0`, validation Pass, `pinned-unsigned-v1`, thumbprint `14F34EFE…5623`, not launched |

Notes: the session re-serialised the JSON reports compactly (values verbatim). The CheckOnly report contains only hashes, counts and leaf names. Approval 1b is now filled from these values (activation plan §9).
