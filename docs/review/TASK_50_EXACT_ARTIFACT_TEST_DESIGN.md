# Task 50 — ADR-025 exact-artifact test of the signed helper (design note, revision 2)

Requirements: `FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-018, ADR-022, ADR-025 (durable dispatch; follow-up controls), ADR-031 (decision 7). Closes the design part of gap **G5** in the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) (§6, §11, approval 2).

Status: **revision 2, after one independent review (verdict "Sound with changes"); awaiting the owner decisions in §6. It authorizes nothing.** No source change, host action, UAC prompt, deployment or route admission follows from it. Base: `182edec`. The review findings and how each was handled are in §10.

## 1. The problem in one paragraph

ADR-025 ("Follow-up controls and tests") requires "separately authorized exact-artifact Windows host tests" that "establish UAC, actual scope/mode enforcement and installed-state outcomes", and says "documentation or mocks cannot substitute". The helper-endpoint design (rev 6, §8 and §9) makes the positive linked-token path and a real `AccessCheck` on the Chocolatey tree **owner-host evidence**, because GitHub runners have no UAC split token. Admission obligation 2 in the [composition record](TASK_50_REVIEWED_DISPATCH_COMPOSITION.md) and ADR-031 decision 7 put this test before activation. Today nothing can run it:
- The helper accepts only the deployed desktop as its peer. `ReviewedHelperBootstrap.OpenVerifiedPeer` requires the pipe server to be `PCSteward.Desktop.exe` beside the helper (`ReviewedHelperHost` uses `AppContext.BaseDirectory`), in the same session, with the same SID and the same signer. Stage A3 (`HelperDeploymentCapabilityCheck` through `OwnerOnlyDeploymentCapabilityReader.ResolveHelperLayout`) enforces the `<Program Files>\PC Steward` layout and requires the peer's image hash to equal its ledger entry. So the request must come from the real deployed desktop binary.
- The desktop has no code path that sends a protocol-2 request. `HasSupportedMode` admits only `WinGetUserExactV3`, and nothing in `src` constructs `ChocolateyRuntimeLimitsRouteDispatcher` or `WindowsReviewedHelperTransport`. Architecture tests pin `HasSupportedMode`, `CapabilityOperationAllowlist` (package token only) and the dispatcher's `Supports`.
- The ADR-031 gate tests add `ReviewedRouteTestAdmission`, an `AsyncLocal` term inside `HasSupportedMode` whose scope only tests may enter (the seam reached HEAD through `b5ecc75`). It is compiled into the shipped `Application.dll`, but no `src` code can reach it. This design does **not** use it or any other `ForTests` factory: the shipped binaries must reach the test by reviewed production code.

## 2. What must be proven (the "§6 evidence list")

| # | Evidence | Source | Already proven? |
|---|---|---|---|
| E1 | §2.1 linked-token match: the helper's `TokenLinkedToken` matches the **real** desktop peer by `AuthenticationId` and SID; peer elevation type Limited; evaluation token exactly Medium, not restricted, not AppContainer, only allow-listed privileges | Helper design §2.1, §8 ("Positive linked-token path (not CI)"), §9; plan §6 item 1 | **No.** CI proves the pure `RequesterTokenPolicy` verdicts and the native "no linked token" refusal (§8 N1). Host evidence run 2 shows the owner's normal shell is Limited (`tokenElevationType 3`), but not the helper's match. |
| E2 | `AccessCheck` verdicts with that token on the real `ProgramData\chocolatey` tree, in Node and Ancestor modes | Helper design §2.2, §9; plan §6 item 2; composition obligation 2 | **No.** CI proves the evaluator on fixture DACLs. O5 (run 2) ran no `AccessCheck` and did not cover steps 4–6. |
| E3 | The real path of the signed apphost: desktop `TryHold`, final launch trust, `ShellExecute runas`, the helper's handle-based peer verification, the desktop's post-connect `VerifyPeer` with `FileIdInfo` equal to the held one, and the binding held until helper exit | Adapter gates A.4 step 3, A-Q1; plan §6 item 3 | **Partly.** AQ1N/AQ1M/AQ1C PASS on synthetic images. AQ1UAC (approval 4) has not run. Nothing has run on the signed apphost. |
| E4 | A2-L3 on the real deployment: write probes on held PC Steward files are denied | Plan §6 item 4; host-evidence plan H4 | **Analogue only** (A2L3S PASS on System32). Block H4 (`A2L3P`) can run under its own approval. |
| E5 | Nothing changed: no Chocolatey process, no package or file change, database preserved | Plan §6 item 5 | Not applicable until a test exists. |

**What no approach here proves.** ADR-025 also requires actual mode enforcement at launch and installed-state outcomes. Those need a real update: stages C, D and Launch on the real client, the A-Q1 residuals (native held DLL loads, PowerShell reads of held `helpers` files, an elevated launcher holding files), and post-update verification. With only Chocolatey 2.7.5 on the release list and no Chocolatey-managed package on the host, they **stay open under G9**. The probe in (A) proves UAC and pre-launch enforcement only.

## 3. A fact that shapes every option: byte-identical carry-over is not possible

The binding set (`DeploymentCapabilityPolicy.BindingSet`) covers the desktop and helper binaries, `Execution.dll`, `Application.dll`, `Domain.dll`, `Provider.Chocolatey.dll`, the rest of the helper closure and the capability file. Activation changes at least `Application.dll` (`HasSupportedMode`) and the desktop composition root; admitting the client route also changes `Execution.dll` (`CapabilityOperationAllowlist`) and the capability file. **So no evidence on a pre-activation build carries over byte-identically to the activation build** (G7), and every redeploy brings a new 14-day signer (G8). The test must be **cheap and safe to re-run** on each deployment, including the activation deployment.

## 4. Candidate approaches

Every approach keeps `HasSupportedMode`, `CapabilityOperationAllowlist` and `Supports` unchanged in the build under test, except (B).

### (A) Owner-started, read-only helper qualification probe (recommended)

**What it is, honestly.** An **elevated, read-only observation** started by the owner outside any update job. It runs the real desktop transport and the real signed helper's pre-launch checks, then stops.

**Desktop entry.** The desktop has no custom `Main` (WinUI generates it; startup logic is in `App.OnLaunched`). The probe is an early branch in `OnLaunched`, **after** `CurrentUserDataOperationSession.TryAcquire` (App.xaml.cs:27; the same gate `ScheduledHost/Program.cs` takes) and before reset handling, database, persistence or any window. It runs only when the process command line (`Environment.GetCommandLineArgs`) is exactly the desktop path plus `--qualify-chocolatey-helper`. It holds the gate for the probe's whole duration, refuses when the gate is busy or the process is elevated, and exits without opening the database. (A custom `Main` would avoid XAML start-up but would change start-up for every launch; it is not preferred.)

**Transport.** A desktop `ProbeAsync` reuses the existing hold, final launch trust, `runas` launch and post-connect peer code. Before UAC, `TryHold` must succeed on the owner-only capability file for every compiled allow-list token, so the probe cannot raise a prompt outside an ADR-022 owner-only test deployment. The helper command line is the existing bootstrap plus one key:
`--protocol 2 --desktop-pid <n> --pipe <name> --nonce <hex> --kind qualify-chocolatey-helper`.
The kind is visible in the UAC "details" view. The existing update transport must never emit `--kind` (architecture test).

**Helper: the kind binds the consent.** `ReviewedHelperBootstrap.TryParse` and `SelectsProtocol2` accept exactly 8 arguments, so a 10-argument probe line never reaches the update endpoint; it would fall to the protocol-1 parser, which must refuse it (test). A separate parser accepts exactly the 10-argument probe line with the single literal kind and selects a separate probe entry. Each mode refuses the other:
- An update-mode helper (8 arguments) never runs the probe.
- A probe-mode helper **reads no request frame**. Any byte from the desktop before the response ends the run with no response. So an update frame sent to a helper elevated for a probe is never parsed.

Because the kind, peer PID, pipe name and nonce are all on the elevated command line, the probe needs no request frame, no probe request codec, and no A0′/A1′ stages. Binding stays as today: the per-request pipe restricted to the user and SYSTEM, the 30-second connect bound, the server-PID check, handle-based peer verification, and the bootstrap nonce echoed in the response and checked by the desktop in fixed time.

**Helper stages after peer verification.**
- A2: the same requester-token rules as `BeginItem` (§2.1) on a probe item; the probe reports the closed `TokenVerdict`.
- A3: G1 (`HelperDeploymentCapabilityCheck`) for **every token in the compiled `CapabilityOperationAllowlist`**, with the requester as protection principal and the helper and peer image hashes. Today that is the package token only; on the activation build it also covers the client token that B′ exercises.
- B′: one Helper-role client-mode `ObserveClient` with `frozenInstalled` set to the compiled listed release (2.7.5) and no expected fingerprint. Client mode is the only target this host has; a package-mode observation would stop at step 7.
- Stop: dispose the observation, including any `ChocolateyHeldClientBinding`, then write one probe response. The probe item has no `QueryCandidateAsync`, `RevalidateBeforeLaunch` or `UpgradeAsync`.

**Probe response (closed).** Nonce; `tokenVerdict`; `g1Status` per allow-list token; `observation` (`Observed`, `Refused`, `TimedOut`, `Cancelled`); `failedStep` (null or 1–9) with the closed refusal reason and detail; `nodesEvaluated` and `ancestorsEvaluated`; the observed client version (a listed release or null); `fingerprintPresent` and its length (never the value; see Safety); a fixed diagnostic token. No paths, user names or SIDs. The desktop adds transport facts (hold, launch trust, UAC accepted or declined, peer verified both ways, `FileIdInfo` equal, binding held until exit, helper exit code) and the public deployment pins (ledger SHA-256, signer thumbprint and expiry, helper image hash).

- **Proves:** E1 on the real desktop and signed helper; E2 on the tree the Helper role checks (steps 1–9); E3 in full on the real apphost (so AQ1UAC may be skipped); E5 with before/after hashes. In ADR-025 terms: **UAC and pre-launch enforcement only.**
- **Cannot prove:** mode enforcement at launch, stages C, D and Launch, holds during a real `choco` run, installed-state outcomes and post-update verification (G9); the review → claim → dispatch workflow (`HasSupportedMode` still refuses). E4 needs block H4.

**Safety and the exceptions it needs.**
- **It conflicts with three accepted rules**, so it needs explicit owner exceptions: spec §2.3 principle 4 ("elevation is requested only for a specific reviewed operation that needs it"); AGENTS.md "Scanning never requests elevation"; and ADR-025 "Durable dispatch", which requires a unique `DispatchClaimed` commit **before any helper/UAC launch** under the current-user gate. The probe holds that gate but takes **no claim** and writes no database row. The required ADR-025 amendment names this rule.
- **ADR-018 has no generic broker.** The helper still serves one fixed request per launch, but its input surface grows by one kind. This is a **standing elevated entry point**, not "one elevation": it is restricted to ADR-022 owner-only test deployments (enforced by the pre-UAC `TryHold`) and must be removed, or re-approved, before any distribution build.
- **Same-user attack surface.** Any process running as the user can start the desktop with the flag and produce an expected-looking UAC prompt for the signed helper. After the kind binding, a consenting user gives that process only elevated reads and a few seconds of share-read holds on Chocolatey files, which could disturb a concurrent `choco` run (precondition: none in progress). The result file is in user-writable `%TEMP%`: the writer refuses an existing folder and any reparse point on the path, and its integrity assumes no same-user malware. The profile fingerprint is SHA-256 over data that includes `token.Sid` and file identities (`ChocolateyProfileObserver.cs`, step 9), so it is a stable host- and user-linked identifier; only presence and length are written.
- **No path to run Chocolatey is an intent that must be proven.** A successful Helper-role client observation builds `ChocolateyHeldClientBinding.CreateForClientUpdate` (observer step 9 tail), which can launch. Architecture tests must show the probe path never retains or exposes that binding, and a test must show it is disposed before the response is written.
- Never unattended: no scan, `ScheduledHost` or UI path calls it (architecture test). No database or remote string reaches it.

- **G7:** the probe stays in later owner-only test builds, so a re-run costs one UAC prompt. **G8:** the probe must be in the build Part B deploys, or a later redeploy adds it (§6 decision 3).

### (A-min) The existing endpoint with a compiled request that must refuse (rejected)

The desktop would forge a `ChocolateyMachineRuntimeLimitsV3` operation for an absent, reserved package ID. It proves E1, A3 and E3 but **not E2** (step 1 and step 7 refusals look identical on the wire), and shipped code would forge a `ReviewedUpdateOperation` that no user reviewed, breaking ADR-025's binding of a request to a live review.

### (B) Run only on the activation build, with Confirm on a refusing target (not viable on this host)

Proves the most (real review, claim and dispatch), but with no Chocolatey-managed package and only 2.7.5 listed there is nothing to confirm; with a target, stage C runs `choco` elevated before any arranged refusal, and making a refusing target changes packages (not authorized). It also inverts ADR-031 decision 7.

### (C) A separately signed test-harness peer (not recommended)

It must be installed **as** `PCSteward.Desktop.exe` in an ADR-022 deployment. It proves E1 and E2 but not E3 for the real transport, needs two extra redeploys against G8, and leaves a signed program able to drive the helper on the host. Restricted to the probe, it is (A) with an extra deployment.

### (D) Same-source token and `AccessCheck` check without a deployment (supplement only)

A reviewed console tool from the N1 sources, started elevated on demand. It proves the rules on a real split token but nothing about the signed artifact, and needs the SDK on the host or an unsigned elevated binary, each under separate approval.

## 5. Recommendation

Adopt **(A)**. Run it on the Part B (pre-activation) deployment as gate-2 evidence. Because admission is compiled into the activation build, the re-run on the activation deployment necessarily comes **after** admission is compiled in (an ordering gap against ADR-031 decision 7); decision 2 closes it by confirming no Chocolatey review until that re-run passes. Add block H4 (`A2L3P`) under its own approval for E4. Record G7 as "re-run, not carry over". ADR-025's real update and installed-state tests stay open under G9.

## 6. Owner decisions required

**Decision 1 (approach and exceptions):**
> I approve designing and implementing approach (A): an owner-started, read-only Chocolatey helper qualification probe (`--qualify-chocolatey-helper`), included only in ADR-022 owner-only test deployments. The probe kind is on the helper's command line, so a helper elevated for a probe cannot accept an update request. It holds the common data-operation gate and writes no database or claim. Architecture tests and an independent review must show it has no path that launches Chocolatey; its only effects are reads, a few seconds of share-read holds on Chocolatey files, and one result file in %TEMP%. It leaves HasSupportedMode, the allow-list and Supports unchanged and adds one helper request kind. I accept it as a standing exception to spec §2.3 principle 4, the AGENTS.md rule 'Scanning never requests elevation' and the ADR-025 DispatchClaimed-before-UAC rule; each run needs its own approval, and the ADR-025 amendment text needs my separate acceptance before deployment. The probe does not satisfy ADR-025's requirement for real update and installed-state tests (open under G9). — **Yes / No**

**Decision 2 (G7 and ordering):**
> Exact-artifact evidence counts only for the deployment and pins it ran on. After any redeploy, including activation, I run the probe again under a new approval before confirming any Chocolatey review, and I confirm none until it passes. A failed result stops use; any rollback needs separate approval. — **Yes / No**

**Decision 3 (timeline fallback, chosen now):**
> If the probe slices are not reviewed and green on Windows CI by 2026-11-10, I choose: (a) Part B without the probe and a later redeploy that adds it; or (b) the G8 expired-signer path; or (c) the first probe run on the activated build under Decision 2. — **(a) / (b) / (c)**

Why now: the default plan needs S1–S6 reviewed and green by about 2026-11-10, then G3, G4, G6 and Part B approved and run before **2026-11-13T20:18:09Z**, while G6 is itself blocked on the open §8.6 admission gates. That is not a realistic default. Costs:
- **(a)** Part B proceeds on time; adding the probe later needs another redeploy, a new signer and all Part B evidence re-run.
- **(b)** No rush; the expired-signer path (for example a separately approved removal, then a fresh install) must first be reviewed and defined.
- **(c)** No extra redeploy; no pre-activation exact-artifact evidence exists, so ADR-031 decision 7 is met only through decision 2's "confirm nothing until the probe passes".

**Owner decisions recorded (2026-11-03):** the owner replied "Yes, yes and (c) on the helper test": **Decision 1 — Yes**, **Decision 2 — Yes**, **Decision 3 — (c)** (the first probe run happens on the activated build, under Decision 2: no Chocolatey review is confirmed until that run passes). Consequences:
- The probe slices S1–S6 are approved for design and implementation. They are **not** needed in the build that Part B deploys before the signer expiry, so Part B can proceed on a pre-probe build commit (G6) without waiting for them.
- The ADR-025 amendment text (S1) still needs the owner's **separate acceptance before deployment**, and each probe run needs its own approval.
- No pre-activation exact-artifact evidence will exist; ADR-031 decision 7 is met through Decision 2's "confirm nothing until the probe passes" rule on the activation deployment.

## 7. Implementation slices (after a yes to decision 1)

1. **S1 Spec:** an addendum to the protocol-2 fact extension: the probe command-line grammar, the closed probe response, bounds, S-rules and versioning (additive kind under protocol "2" or a capability token); the ADR-025 amendment text naming the DispatchClaimed-before-UAC exception. Independent review.
2. **S2 Wire:** the probe **response** codec only, with goldens and mutation tests; package and client request and response frames byte-unchanged. No probe request codec.
3. **S3 Helper:** the command-line kind parser and entry dispatch (probe line refused by `TryParse` and the protocol-1 parser; update line refused by the probe parser); the probe entry with no frame read (any inbound byte → no response); A2, per-token A3, B′, stop; the probe item on the native port; the observer's `failedStep` and node counts. Architecture tests: no launch types or stage C/D/Launch members on the probe path; `ChocolateyHeldClientBinding` never retained or exposed by the probe; only the native port constructs the probe item. A test that the binding is disposed before the response.
4. **S4 Desktop:** `ProbeAsync` reusing hold, trust, launch and peer code, with `TryHold` for every allow-list token before UAC; the `OnLaunched` branch after the gate, before reset handling, database and UI; the result-file writer (fresh folder only, reparse-point refusal, fingerprint presence and length only). Architecture tests: single caller; not reachable from scan, `ScheduledHost` or UI; the update transport never emits `--kind`; admission pins unchanged.
5. **S5 Tests:** portable probe-entry tests over a pipe pair with a fake port; Windows CI native probe over a fixture tree (seams in tests only); the runner's "no linked token" verdict; gate busy, UAC decline and missing helper → clean refusal with no result claim.
6. **S6 Review and CI:** fresh independent review, canonical build, full suite, Windows CI green; merged into the G6 build commit and built by the G3 candidate builder.
7. **S7 Host plan:** revise Part C of the activation plan with pins and fill approval 2.

## 8. Host procedure outline (owner-run; for review, not approval)

Preconditions: Part B ended `validated` with `pinned-unsigned-v2` and helper protocol 2; non-elevated Windows PowerShell 5.1; no `PCSteward*` or `choco` process; no Chocolatey operation in progress; `%TEMP%\pcsteward-exact-artifact` absent. Each block is one line; S7 fixes the text.

| Step | Block (read-only unless stated) | Output to paste back |
|---|---|---|
| C1 Pins | `Get-FileHash` on the installed desktop and helper; `Get-AuthenticodeSignature` thumbprint and `NotAfter`; compare with the deployment record | `PINS-OK` or `STOP: pin differs` |
| C2 Baseline | SHA-256 of `%LOCALAPPDATA%\PCSteward\pcsteward.db`; WAL/SHM absent; one combined SHA-256 over the file list and hashes of `C:\ProgramData\chocolatey`; `choco` count 0 | `BASELINE: db=<hash> choco=<hash>` |
| C3 Probe (one UAC prompt) | `Start-Process "$env:ProgramFiles\PC Steward\PCSteward.Desktop.exe" -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru`. Exactly one prompt, for `PCSteward.ElevatedHelper.exe` signed by `CN=PC Steward Owner-Only Test <GUID>`, whose details show `--kind qualify-chocolatey-helper`. Anything else → **No**, then stop | `EXIT: n` and the result file token |
| C4 After | Repeat C2 | `UNCHANGED: db=true choco=true` or the differing token |
| C5 Evidence | Open the result JSON in Notepad, check it holds only closed fields (no fingerprint value, path, name or SID), paste it | The JSON |
| C6 Optional | Block H4 (`A2L3P`), under its own approval | `VERDICT A2L3P: …` |
| C7 Cleanup | Remove only `%TEMP%\pcsteward-exact-artifact`, if C3 created it | `CLEANUP-COMPLETE` |

**Pass criteria:** transport facts all true; `tokenVerdict = Accepted`; `g1Status = Supported` for every allow-list token; `observation = Observed` with client 2.7.5, `fingerprintPresent = true` (length 64) and non-zero node counts; `failedStep = null`; C4 unchanged; exit 0 if S4 shows WinUI propagates the exit code, otherwise the result file alone carries the verdict. Any other value is a finding, not a retry. A UAC decline gives no evidence and is not repeated without a new approval.

## 9. Open questions

1. Versioning: an additive kind under protocol "2", or its own capability token?
2. Should the probe also run a package-mode observation for a reserved absent ID? It adds little over the client-mode run.
3. Should approval 4 (AQ1UAC) still run in Part A, or may probe E3 stand in for `runas`?

## 10. Revision 2: review disposition

Each finding was checked against the code at `182edec` before it was applied.

| # | Sev. | Finding | Disposition |
|---|---|---|---|
| 1 | High | ADR-025 "Durable dispatch" requires `DispatchClaimed` before any helper/UAC launch under the gate; the probe takes no claim. "Refuses when another PCSteward* runs" is not the gate | Applied. The rule is named in Safety and decision 1. The entry now holds `CurrentUserDataOperationSession.TryAcquire` (App.xaml.cs:27, ScheduledHost) for the whole probe. |
| 2 | High | The probe kind is not bound to UAC consent; `TryParse` takes 8 arguments and no kind, and the helper never checks `HasSupportedMode` | Confirmed (`SelectsProtocol2` and `TryParse` require exactly 8; the endpoint accepts allow-listed routes only). Applied: `--kind` on the command line, a separate parser, each mode refuses the other. The probe reads no frame, so the probe request codec, A0′ and A1′ are dropped; pipe, PID and nonce binding stay. Also noted: the protocol-1 parser must refuse a probe line. |
| 3 | Med | Attack surface understated; "reveals nothing private" is wrong | Confirmed (fingerprint input includes `token.Sid` and file identities). Applied: same-user trigger, residual effects, `%TEMP%` integrity limits, fingerprint presence and length only; the phrase is removed. |
| 4 | Med | Framing: an elevated read-only observation, a standing entry point | Applied: cites AGENTS.md, spec §2.3 principle 4 and ADR-018; restricted to ADR-022 deployments by the pre-UAC `TryHold`; removal or re-approval before distribution. |
| 5 | Med | ADR-025 line 145 requires UAC, actual mode enforcement and installed-state outcomes | Applied: §2, §4 and §5 say the probe proves UAC and pre-launch enforcement only; the rest stays open under G9. |
| 6 | Med | A3 checks the package token, B′ the client route | Applied: A3 runs per compiled allow-list token, with `g1Status` per token; the desktop holds every token before UAC. |
| 7 | Med | Ordering gap against ADR-031 decision 7 | Applied: §5 states the gap; decision 2 confirms no review until the re-run passes. |
| 8 | Low | "No path to run Chocolatey" is intent; `CreateForClientUpdate` can launch | Confirmed (observer step 9 tail). Applied: architecture tests on retention and exposure, plus a disposal test (S3). |
| 9 | Low | No custom `Main`; logic is in `OnLaunched` | Confirmed. Applied: early `OnLaunched` branch after the gate; custom `Main` noted as the riskier alternative. |
| 10 | Low-Med | Timeline is not a realistic default | Applied: decision 3 asks for the fallback now, with costs of (a), (b), (c). Open question 4 is folded into it. |
| — | Minor | Peer path and `bc656b7` reference | Applied: `AppContext.BaseDirectory` for the peer path, `ResolveHelperLayout` for Program Files; the seam reached HEAD through `b5ecc75` (`bc656b7` is not an ancestor). |
