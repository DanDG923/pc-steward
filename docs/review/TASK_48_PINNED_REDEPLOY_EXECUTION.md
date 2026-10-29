# Task 48 — Approved pinned redeployment execution

Date: 2026-10-29. Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`; retained `TM-04/TM-06`.

**Deployment PASS; Task 48 remains open for separately approved responsive launch/normal close.** The owner replied **“Approved”** to the final [exact removal/redeployment plan](TASK_48_EXACT_REDEPLOY_PLAN.md), including its disclosed trusted owner-account/session assumption. This record supersedes the earlier pending-host and retained-old-install statements in the preparation/history reports. It does not authorize launch or Task 49.

## Approved execution and independent result

Before execution, all **22 final approval pins** matched snapshot `CE1C6EE7062082195E81EEAABEBBF91F4CF7EBDDAE380570E2CC9346FFCB7A2F`, and HEAD remained `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342`. The caller's raw bytes were captured, checked against `92B8DB08DD9CE23ECD9448B4E624F921C41140E56AA59523AC90F0EE4E55A21A`, then executed as a script block with `-ExecuteApprovedPlan`. No mutable caller path was executed after checking.

The caller recorded **attempt 1**, elevated wrapper PID 300, start `2026-10-29T19:14:12.6307940Z`, completion `2026-10-29T19:15:47.4961403Z`, **native exit 0 / Validated**, `retry=false`, `launched=false`. The fixed RunAs route successfully returned a native exit code; the former null-exit issue was not reproduced. No second attempt or forced termination occurred.

Protected evidence is retained under `C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8`:

| Evidence | Actual result / SHA-256 |
|---|---|
| `removal-result.json` | Old guarded deployment removed; owned old certificate removed; user data preserved. `21B3D16EB539B671B37DDC9C5AE5EDFE84A98F28292C089D6B12411DD710A927` |
| `installation-result.json` | New install Pass, launch false, application updates 0. `8CAA7B948D3712E05400F11809F5CDB94B1A9F455F24DEAA1A644EBECD589D20` |
| `independent-validator.json` | Pass, exactly `pinned-unsigned-v1`, approved manifest. `695B47D02F331DB8E4D9FF8032041A4530E5D2FE927CECE1579CAA60674F6756` |
| `result.json` | Phase `validated`, exit 0, approved manifest/new signer, user data absent, launch false. `BE03A396D2AEC3A25A3E00DEB483D5FEC9B1E3C79EDB3B05CBF5BA52FA586AF5` |

The parent independently ran the protected `scripts\Test-OwnerOnlyProtectedTestDeployment.ps1 -RequirePinnedPayload` from the non-elevated installing user's session: **exit 0 / Pass / pinned-unsigned-v1**. A further read-only verification at `19:18:27Z` rechecked the six protected script hashes and execution/code ACLs, full candidate, installed file set, strict signed-image reconstruction/CMS/Windows trust, complete ledger, certificate stores, data absence and process absence.

| Installed evidence | Verified value |
|---|---|
| Approved unsigned manifest | `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE` |
| File identity | **102 exact paths; all 100 non-signed files exact in size/hash; both signed apphosts reconstruct to their original raw hashes; six XBF files** |
| Desktop DLL | `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D` |
| Desktop PDB | `96B6B8C862FF42BDD626ADB7C26E59C7B415294123A6C4A04FB93C1659C4F2CC` |
| Helper DLL | `9FC90C8A4FCF9D695BAB3ADD1B5FE6B0AF8028D97EB4F409105C011139954D7D` |
| Signed desktop EXE | `5B4BEC38BD02904021139B6EE6B22719706FB2FC8DECD9402144AD403706B6A2` |
| Signed helper EXE | `DF0B07F622E943D9AA40877AD1A62AD0A28162C9A00E367624787F0C42A71B67` |
| Full installed manifest, including signed bytes | `688CD86BF9A24944CA03AEC3F5BCB66EA6618937705FD8EFF5935EFFB6F4C7F8` |
| Completed protected ledger | `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20` |
| Signer | `49D695BA9CA2DEA922659699E5F40710F2B5B264`; RSA-2048, SHA-256 certificate and both CMS/image digests; both embedded signatures **Valid / Authenticode / same signer** |
| Signer expiry | `2026-11-12T19:15:42Z`; no timestamp; current-time validation will fail after expiry |
| Certificate stores | Old `61FC6E8B6F34488247EB799B54456BD1EC671B1C` absent from CurrentUser My/Root/TrustedPublisher; fresh signer present in those three stores |
| Protections | Full install/state descendant exact DACL/administrator ownership/no reparse checks Pass; execution root/scripts/six code files have the reviewed ACLs and exact hashes |
| Data/process preservation | `%LOCALAPPDATA%\PCSteward` still absent; no database/sidecars to back up or integrity-check; zero `PCSteward*` processes; app never launched |

The earlier DLL/PDB identity failure is resolved by consuming the pinned bytes. No deterministic-build claim is made: CsWinRT cross-process nondeterminism remains a hypothesis and no longer controls the installation artifact.

Ignored local evidence: `artifacts/task-48-redeploy-plan-v2/caller-execution.json`, `artifacts/task-48-deployment/Verify-ActualRedeployment.ps1`, and `artifacts/task-48-redeployment-result/{verification-summary,installed-manifest,independent-validator}.json`. The verification summary SHA-256 is `F2E5604999EB17F55686CEE12A2D2E1E036ECCEE049231CF758E969CD5250460`. Protected originals remain authoritative; local copies are inspection conveniences.

## Limits and retained boundaries

The real route now demonstrates elevated creation followed by passing final ACL checks, real SHA-256 signing/trust, exact new-profile acceptance and native RunAs exit capture. Final ACL inspection does not independently observe every creation instant. The privileged helper checked private compiler-root policy before compilation, but a separate audit of actual compiler descendant inheritance was **not** performed. A non-elevated `Get-Acl` of the private compiler folder failed with **“Attempted to perform an unauthorized operation.”** No second elevation was started to inspect it. Compiler artifacts remain retained; no empty-directory or per-file ACL proof is inferred from denial.

Certificate expiry behavior was not time-shifted or otherwise exercised on the host; code/fixture rejection and the absence of timestamps are separate evidence. Native certificate-consent completion is evidenced by the successful transaction and store state; no prompt-count/UI transcript was captured. Private-key non-exportability follows the reviewed fresh-certificate creation call; no key export was attempted. The accepted trusted owner-account/session assumption remains necessary.

No runtime source changed during execution. Existing local gates remain applicable: **85 focused assertions; focused 1/1; Release 0 warnings/errors; full 291/291, zero skipped**. They were not unnecessarily repeated after a host-only run and documentation updates. No scan, source refresh, application update, schedule change, data reset, release or Task 49 occurred. The candidate copy, protected execution/compiler/evidence roots and new deployment/signer are retained; no implicit cleanup is authorized. No commit: Git user.name/email remain unset and were not changed.

## Next action

Review and obtain the separate exact approval in [the bounded launch plan](TASK_48_RESPONSIVE_LAUNCH_PLAN.md). Task 48's prompt and host-test contract explicitly require that additional launch authority; approval of the deployment expressly excluded launch.
