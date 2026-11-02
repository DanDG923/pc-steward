# Task 50 — ADR-025 exact-artifact test of the signed helper (design note, revision 1, draft)

Requirements: `FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-025 (follow-up controls), ADR-031 (decision 7). Closes the design part of gap **G5** in the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) (§6, §11, approval 2).

Status: **draft for independent review and an owner decision. It authorizes nothing.** No source change, host action, UAC prompt, deployment or route admission follows from it. Base: `b9ed090`.

## 1. The problem in one paragraph

ADR-025 ("Follow-up controls and tests") requires "separately authorized exact-artifact Windows host tests" and says "documentation or mocks cannot substitute". The helper-endpoint design (rev 6, §8 and §9) makes the positive linked-token path and a real `AccessCheck` on the Chocolatey tree **owner-host evidence**, because GitHub runners have no UAC split token. Admission obligation 2 in the [composition record](TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) and ADR-031 decision 7 put this test before activation. Today nothing can run it:
- The helper accepts only the deployed desktop as its peer. `ReviewedHelperBootstrap.OpenVerifiedPeer` requires the pipe server to be `<Program Files>\PC Steward\PCSteward.Desktop.exe`, in the same session, with the same SID and the same signer. Stage A3 (`HelperDeploymentCapabilityCheck`) also requires the peer's image hash to equal its ledger entry. So the request must come from the real deployed desktop binary.
- The desktop has no code path that sends a protocol-2 request. `HasSupportedMode` admits only `WinGetUserExactV3`, and nothing in `src` constructs `ChocolateyRuntimeLimitsRouteDispatcher` or `WindowsReviewedHelperTransport`. Architecture tests pin `HasSupportedMode`, `CapabilityOperationAllowlist` (package token only) and the dispatcher's `Supports`.
- The ADR-031 gate tests (branch head `bc656b7`) add `ReviewedRouteTestAdmission`, an `AsyncLocal` term inside `HasSupportedMode` whose scope only tests may enter. It is compiled into the shipped `Application.dll`, but no `src` code can reach it. This design does **not** use it or any other `ForTests` factory: the shipped binaries must reach the test by reviewed production code.

## 2. What must be proven (the "§6 evidence list")

| # | Evidence | Source | Already proven? |
|---|---|---|---|
| E1 | §2.1 linked-token match: the helper's `TokenLinkedToken` matches the **real** desktop peer by `AuthenticationId` and SID; peer elevation type Limited; evaluation token exactly Medium, not restricted, not AppContainer, only allow-listed privileges | Helper design §2.1, §8 ("Positive linked-token path (not CI)"), §9; plan §6 item 1 | **No.** CI proves the pure `RequesterTokenPolicy` verdicts and the native "no linked token" refusal on the runner (§8 N1). Host evidence run 2 shows the owner's normal shell has `tokenElevationType 3` (Limited), the right *kind* of principal, but not the helper's match. |
| E2 | `AccessCheck` verdicts with that token on the real `ProgramData\chocolatey` tree, in Node and Ancestor modes | Helper design §2.2, §9; plan §6 item 2; composition obligation 2 | **No.** CI proves the evaluator on fixture DACLs with a derived LUA token. O5 (run 2) shows only that the *desktop* user can read the probed file set; it ran no `AccessCheck` and did not cover steps 4–6 (HTTP cache, loader/licensing, configuration). |
| E3 | The real path of the signed apphost: desktop `TryHold` of the binding set, final launch trust (WinVerifyTrust, same signer), `ShellExecute runas`, the helper's handle-based peer verification, the desktop's post-connect `VerifyPeer` with `FileIdInfo` equal to the held one, and the binding held until helper exit. This settles the A2-L5 fallback for the real helper | Adapter gates A.4 step 3, A-Q1; plan §6 item 3; evidence interpretation "Still open" | **Partly.** AQ1N/AQ1M/AQ1C PASS (synthetic images, direct creation). AQ1UAC (runas, synthetic) is approval 4 and has not run. Nothing has run on the signed apphost. |
| E4 | A2-L3 on the real deployment: write probes on held PC Steward files are denied | Plan §6 item 4; host-evidence plan H4 | **Analogue only** (A2L3S PASS on System32). Block H4 (`A2L3P`) exists in the committed script and can run against the v2 install under its own approval. |
| E5 | Nothing changed: no Chocolatey process, no package or file change, the desktop closed normally, database preserved | Plan §6 item 5 | Not applicable until a test exists. |

Out of scope for any approach in this note (they need a real client launch): the A-Q1 residuals (native held DLL loads, PowerShell reads of held `helpers` files, an elevated launcher holding files), stages C, D and Launch on the real client, and post-update verification. With only Chocolatey 2.7.5 on the release list and no Chocolatey-managed package on the host, these stay open under **G9**.

## 3. A fact that shapes every option: byte-identical carry-over is not possible

The binding set (`DeploymentCapabilityPolicy.BindingSet`) is the desktop exe and dll, the helper exe, dll, deps and runtimeconfig, `Execution.dll`, `Application.dll`, `Domain.dll`, `Provider.Chocolatey.dll` and the rest of the helper closure, plus the capability file. Activation changes at least `Application.dll` (`HasSupportedMode`) and the desktop composition root. Admitting the client route also changes `Execution.dll` (`CapabilityOperationAllowlist`) and the capability file. **So no approach can produce evidence on a pre-activation build that carries over byte-identically to the activation build** (G7). Every redeploy also brings a new 14-day signer (G8). Whatever is chosen must therefore be **cheap and safe to re-run** on each deployment, including the activation deployment.

## 4. Candidate approaches

Every approach keeps `HasSupportedMode`, `CapabilityOperationAllowlist` and `Supports` unchanged in the build under test, except (B), which is listed because the brief asked for it.

### (A) Read-only qualification probe in the shipped desktop and helper (recommended)

**What it is.** A new, closed protocol-2 request kind, `QualificationProbe`, with no package, version, argument, path or route field. The desktop sends it from one owner-started command, `PCSteward.Desktop.exe --qualify-chocolatey-helper`, through the **real** `WindowsReviewedHelperTransport` launch code. The helper runs the same bootstrap and peer verification as for an update, then:
- A0′: read one probe frame; fixed-time nonce check; a frame of the other kind is rejected (no response).
- A1′: issued at most 2 minutes ago and not in the future.
- A2: the same `port.BeginItem(peer, sid)`, with the §2.1 rules. The probe reports the closed `TokenVerdict`.
- A3: the same G1 check (`HelperDeploymentCapabilityCheck`) for the package-route token already in the allow-list, with the requester as the protection principal, plus the helper and peer image hashes. This also runs requester `AccessCheck` on the PC Steward install and state roots.
- B′: one Helper-role **client-mode** observation (`ObserveClient`, the existing observer) with `frozenInstalled` set to the compiled listed release (2.7.5) and no expected fingerprint. Client mode is used because it is the only target this host has (`lib` holds only `chocolatey`). A package-mode observation would always stop at step 7 and could not reach steps 8–9.
- Then it **stops**: it disposes the held client binding and writes one probe response. The probe item type has no `QueryCandidateAsync`, `RevalidateBeforeLaunch` or `UpgradeAsync`.

**Probe response (closed, sanitized by construction).** Nonce; `tokenVerdict` (closed enum); `g1Status` (closed); `observation` (`Observed`, `Refused`, `TimedOut` or `Cancelled`); `failedStep` (null or 1–9) with the closed refusal reason and detail; `nodesEvaluated` and `ancestorsEvaluated` (integers); the observed client version (a listed release or null); the profile fingerprint (64 hex or null); a fixed diagnostic token. No paths, user names or SIDs. The desktop adds its own transport facts to the result file: capability hold, launch trust, UAC accepted or declined, helper peer verified, `FileIdInfo` equal, binding held until helper exit, helper exit code, plus the public deployment pins (ledger SHA-256, signer thumbprint and expiry, helper image hash).

- **Proves:** E1 on the real desktop and the real signed helper; E2 on the whole tree the Helper role checks (steps 1–9, including the HTTP cache, loader/licensing and configuration that O5 did not cover). `Observed` means every evaluated node and ancestor returned Denied; `failedStep` locates any refusal. E3 in full on the real apphost, which also answers runas for the helper (AQ1UAC can be skipped if the owner wishes). E5 by construction: there is no launch path, the desktop opens no database, and before/after hashes are taken.
- **Cannot prove:** stages C, D and Launch; holds during a real `choco` run (the A-Q1 residuals); the review → claim → dispatch workflow (`HasSupportedMode` still refuses); package-mode steps 7–9 for a real target (G9). E4 needs block H4.
- **Safety.** It elevates outside an update job: product spec §2.3 principle 4 says "elevation is requested only for a specific reviewed operation that needs it". The probe is fixed and owner-started, with exactly one UAC prompt and no inputs (spec §6.2: the elevated component accepts no arbitrary command lines), but it still needs an **explicit owner exception, recorded as an ADR-025 amendment**. It is never unattended: no scan, `ScheduledHost` or UI path calls it (architecture test). No string from the database or a remote source reaches it: the request is a compiled constant. Holding Chocolatey files share-read for a few seconds could disturb a concurrent Chocolatey run, so a precondition is "no Chocolatey operation in progress". A same-user process could trigger the UAC prompt; it already can by starting the helper, and the result reveals nothing private.
- **Code needed (in the G6 build).** Wire codec probe request and response with goldens and bounds; an endpoint branch after A0; a probe method on the port (`IChocolateyHelperQualificationPort.BeginProbe` or similar) whose item exposes only the observation; a small observer change to report `failedStep` and node counts; a transport `ProbeAsync` that reuses the hold, trust, launch and peer code; the desktop command-line entry, which runs before persistence or UI composition, refuses when elevated or when another `PCSteward*` process runs, and writes one result file under a fresh `%TEMP%\pcsteward-exact-artifact` folder (never inside PC Steward's data roots). Architecture tests: the probe path references no `LaunchJob`, `NativeLaunchSpec`, `ChocolateyCompiledCommand`, `Process` or stage C/D/Launch member; the entry is the only caller of `ProbeAsync`; no `ForTests` member is reachable; and the three admission pins are unchanged.
- **CI vs host.** CI: wire goldens; the endpoint over a real pipe pair with a fake port; native probe ordering; the runner's "no linked token" verdict (a useful negative); the no-launch architecture tests. Host: every positive (E1–E3, E5).
- **G7:** the probe stays in every later build, so re-running it on the activation deployment costs one UAC prompt and no new tooling. **G8:** the probe must be in the build Part B deploys. Part B must run before **2026-11-13T20:18:09Z**, or a second redeploy (from the new, valid signer) is needed to add it later. Each run must happen within its deployment's 14-day signer window.

### (A-min) The existing endpoint with a compiled request that must refuse (rejected)

The desktop would build a `ChocolateyMachineRuntimeLimitsV3` operation for an absent, reserved package ID with an all-zero profile digest. This needs no helper change. Stage B would stop at step 7 (no registration), and step 9 could never match.
- **Proves** E1 and A3 (the wire diagnostics `a2-item-refused` and `a3-…` tell them apart from `b-refused`), and E3.
- **Cannot prove** E2: a refusal at step 1 and a refusal at step 7 give the same `MachineInputsUnsupported` with null fields, so no Chocolatey-tree verdict is observable.
- **Rejected also on principle:** shipped code would forge a `ReviewedUpdateOperation` that no user reviewed. That breaks the request type's meaning (ADR-025: the request binds a live review).

### (B) Run only on the activation build, with Confirm on a refusing target (not viable on this host)

Flip admission on a separate branch, deploy it, and drive the real workflow to a refusal.
- **Proves** the most: the real review, claim and dispatch path, and the same E1–E3.
- **Cannot run here.** With no Chocolatey-managed package (G9) and only 2.7.5 listed, neither route produces a reviewable item, so there is nothing to confirm. With a target, stage C runs `choco list`/`outdated` **elevated** before any refusal the owner could arrange; that breaks approval 2's "no Chocolatey process". Creating a refusing target means changing Chocolatey packages, which is not authorized.
- **Breaks the gate order:** the build under test is the admitted build, which inverts ADR-031 decision 7 (gates before activation) and the brief's constraint.
- **G7:** no carry-over issue. **G8:** same window as Part B.

### (C) A separately signed test-harness peer (not recommended)

Because of peer pinning, a harness must be installed **as** `PCSteward.Desktop.exe` in a deployment signed by the ADR-022 installer.
- **Proves** E1 and E2 for helper bytes identical to the real build, if the harness payload reuses them.
- **Cannot prove** E3 for the real desktop transport. The harness ledger differs from the real one, so nothing carries over (G7).
- **Costs and risks:** two extra redeploys (harness in, real build back) and signer churn against G8; a trusted, signed program able to drive the elevated helper sits on the host between them. Unless it is restricted to the probe request, it is more dangerous than (A); once restricted, it is (A) with an extra deployment.

### (D) Same-source token and `AccessCheck` check without a deployment (supplement only)

A reviewed console tool built from the same N1 sources (`RequesterToken`, `RequesterAccess`) that the owner starts elevated on demand, peered with a non-elevated child.
- **Proves** the linked-token rule on a real split token and real tree verdicts, from the same source.
- **Cannot prove** anything about the signed exact artifact, so ADR-025 stays open.
- **Costs:** it needs the SDK on the host or a CI-built unsigned binary run elevated (an unknown-publisher UAC prompt); both need separate approval.
- It does not depend on Part B or the signer, so it could reduce risk early. On its own it closes nothing.

## 5. Recommendation

Adopt **(A)**. Run it once on the Part B (pre-activation) deployment as gate-2 evidence, and **re-run it on the activation deployment before the owner approves any real Chocolatey update**. Add block H4 (`A2L3P`) under its own approval for E4. Record G7 as "re-run, not carry over". Skip (B), (C) and (D) unless the owner asks.

## 6. Owner decisions required

**Decision 1 (approach and elevation exception):**
> I approve approach (A) of the Task 50 exact-artifact test design: a read-only Chocolatey helper qualification probe in the shipped desktop and helper, started only by me with `PCSteward.Desktop.exe --qualify-chocolatey-helper`. It shows one UAC prompt for the signed helper, runs the real peer, linked-token, deployment and Helper-role Chocolatey checks, writes a sanitized result file, and then stops. It has no code path that runs Chocolatey or changes anything, and it does not change `HasSupportedMode`, the capability allow-list or the dispatcher's `Supports`. I accept this one elevation outside an update job, to be recorded as an ADR-025 amendment. — **Yes / No**

**Decision 2 (G7):**
> Exact-artifact evidence counts only for the deployment it ran on. After any redeploy, including the activation redeploy, the probe is run again on the new deployment before I approve any real Chocolatey update. — **Yes / No**

## 7. Implementation slices (after a yes to decision 1)

1. **S1 Spec:** an addendum to the protocol-2 fact extension (probe frames, closed fields, bounds, S-rules, versioning: an additive kind under protocol "2", following O1 and W1, or a capability token if reviewers prefer); the ADR-025 amendment text for the owner. Independent review.
2. **S2 Wire:** codec probe request and response, golden and mutation tests; package and client frames byte-unchanged.
3. **S3 Helper:** the endpoint probe branch (A0′, A1′, A2, A3, B′, stop); the probe item on the native port; the observer's `failedStep` and node counts; architecture tests (no launch types or stage C/D/Launch members on the probe path; only the native port constructs the probe item).
4. **S4 Desktop:** transport `ProbeAsync` that reuses the hold, trust, launch and peer code; the command-line entry before persistence and UI; the result file writer (closed fields only); architecture tests (single caller; not reachable from scan, `ScheduledHost` or UI; admission pins unchanged).
5. **S5 Tests:** portable endpoint tests over a pipe pair with a fake port; Windows CI native probe over a fixture tree (test seams in tests only); the runner's "no linked token" verdict; UAC decline and a missing helper → a clean refusal with no result claim.
6. **S6 Review and CI:** fresh independent review, canonical build, full suite, Windows CI green; the slice merged into the G6 build commit and built by the G3 candidate builder.
7. **S7 Host plan:** revise Part C of the activation plan with pins (installed manifest, signer, helper image hash) and fill approval 2.

**Timing:** S1–S6 must finish by about **2026-11-10** so that G3, G4 and Part B can run before the current signer expires (2026-11-13T20:18:09Z). If they cannot, the owner chooses: Part B without the probe and a later second redeploy, or the G8 expired-signer path.

## 8. Host procedure outline (owner-run; for review, not approval)

Preconditions: Part B ended `validated` with `pinned-unsigned-v2` and helper protocol 2; non-elevated Windows PowerShell 5.1; no `PCSteward*` or `choco` process; no Chocolatey operation in progress; `%TEMP%\pcsteward-exact-artifact` absent. Each block is one line, and the exact text is fixed in S7.

| Step | Block (read-only unless stated) | Output to paste back |
|---|---|---|
| C1 Pins | `Get-FileHash` on the installed desktop and helper exe; `Get-AuthenticodeSignature` thumbprint and `NotAfter`; compare with the Part B record | `PINS-OK` or `STOP: pin differs` |
| C2 Baseline | SHA-256 of `%LOCALAPPDATA%\PCSteward\pcsteward.db`; WAL/SHM absent; one combined SHA-256 over the file list and hashes of `C:\ProgramData\chocolatey` (no writes); `choco` process count 0 | `BASELINE: db=<hash> choco=<hash>` |
| C3 Probe (one UAC prompt) | `Start-Process "$env:ProgramFiles\PC Steward\PCSteward.Desktop.exe" -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru` and print the exit code. Exactly one prompt, for `PCSteward.ElevatedHelper.exe` signed by `CN=PC Steward Owner-Only Test <GUID>`; the owner answers. Any other or second prompt → **No**, then stop | `EXIT: n` and the result file path token |
| C4 After | Repeat C2 | `UNCHANGED: db=true choco=true` or the differing token |
| C5 Evidence | Open the result JSON in Notepad, check it for private text, paste it | The JSON |
| C6 Optional | Block H4 (`A2L3P`) of the committed host-evidence script, under its own approval | `VERDICT A2L3P: …` |
| C7 Cleanup (after sharing) | Remove only `%TEMP%\pcsteward-exact-artifact`, if C3 created it | `CLEANUP-COMPLETE` |

**Pass criteria:** exit 0; transport facts all true (hold, launch trust, UAC accepted, peer verified both ways, `FileIdInfo` equal, binding held to exit); `tokenVerdict = Accepted`; `g1Status = Supported`; `observation = Observed` with client 2.7.5, a fingerprint and non-zero node counts; `failedStep = null`; C4 unchanged. Any other value is a finding for review, not a retry. A UAC decline gives no evidence and is not repeated without a new approval.

## 9. Open questions

1. Versioning: is the probe an additive kind under protocol "2", or does it need its own capability token in the capability file? (Either way the binding set already changes with this build.)
2. Should the probe also run a package-mode observation for a reserved absent ID, to show step 7 refuses in Helper role? It adds little: the client-mode run already exercises steps 1–7.
3. Should approval 4 (AQ1UAC) still run in Part A, or does the owner let probe E3 stand in for runas?
4. If S1–S6 cannot finish before 2026-11-10: Part B without the probe, or wait for the G8 expired-signer path?
