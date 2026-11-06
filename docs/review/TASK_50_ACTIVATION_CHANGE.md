# Task 50 Part C step C-A — activation change: gate inventory and record

Requirements: `FR-UPD-15`, `FR-UPD-01/03–09`, `FR-HIS-01`, `TM-03/04/07/13`, `AC-UPD-07`. ADRs: ADR-031 (owner decision 7), ADR-025 (amendment accepted 2026-11-04), ADR-018 (cross-note), ADR-022. Route: [Part C plan](TASK_50_PART_C_PLAN.md) §3 row C-A, with owner decisions D4 = G-0 and G9 = yes (§6). Specs: [probe spec](TASK_50_QUALIFICATION_PROBE_SPEC.md) §7 D4, §8, §9 (S7 obligations 1–3); [exact-artifact test design](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) §8 C1–C7.

**Status (2026-11-06): built on the local branch `task50-activation`; not pushed.** The independent review of `0c659eb` found nothing Critical or High; its fixes are applied in the follow-up commit (§8). Windows CI is still required, and nothing is deployed. No host action was taken.

**Before any deployment of this branch (review M3):** the owner must explicitly confirm obligation 8's ordering (§6), that is, the code change ships now and the final admission approval follows Part C. Until then, this branch is not deployed.

## 1. Gate inventory (before activation)

The rule for this step: activate only if every recorded gate is met, except the Part C probe run and the owner's final admission approval (obligation 8), which come after this change. A gate that is unmet and cannot be done in the cloud within this scope would stop the step.

### 1.1 ADR-031 decision 7 gates (both routes)

| # | Gate | Source | State | Evidence |
|---|---|---|---|---|
| D7-1 | Owner-host evidence: 2.7.5 eligibility, O5, A-Q1, A2-L3, AQ1UAC/runas | ADR-031 decisions 2 and 7; activation plan §0 gates 3 and 4 | **Met** | Part A PASS 2026-11-03 (host-evidence result 3: `matchesPinnedRelease = true`, 0 unlisted files; AQ1N/AQ1M/AQ1C/A2L3S/AQ1UAC PASS) |
| D7-2 | ADR-025 exact-artifact test of the signed helper | ADR-025 follow-up controls; obligation 2 | **Open by design (Part C)** | Decision 3 (c): the probe ships in this build; its first run is on the activated deployment |
| D7-3 | Approved v2 redeploy | Obligation 3; activation plan §0 gate 1 | **Met** | Part B complete 2026-11-06 (recovery R2/R3: `pinned-unsigned-v2`, protocol 2, manifest `23D7A71C…`). The activation build needs its own redeploy (C-B) |
| D7-4 | Design §8.6 gate 1: full client-route workflow test | ADR-031 design §8.6/§8.7 | **Met (CI)** | `ClientRouteWorkflowAdmissionGateTests`, `ClientRouteSqliteAdmissionGateTests`; now run against the production rule (the test-only seam is removed, §2.1) |
| D7-5 | Design §8.6 gate 2: Windows end-to-end of client stages B–Launch | ADR-031 design §8.6/§8.7 | **Met (Windows CI)** | Run 188 on `182edec`; green again at `3f5486a` |
| D7-6 | Design §8.6 gate 3: the Desktop offer comes from the verified observer | Recorded on the Task 73 branch only (`ec5d7dd`) | **Not applicable on this branch** | This branch has no Desktop client-update offer or review UI: `src/PCSteward.Desktop` shows no "Update Chocolatey to X" entry, so nothing unverified is offered. The Task 73 implementation of the gate arrives with that UI |

### 1.2 Client-route residuals (ADR-031 design §2, §8.7)

| # | Item | State | Evidence |
|---|---|---|---|
| R5 | Owner acceptance of the elevated-execution residual | **Met** | ADR-031 decision 6 (2026-11-02) |
| R1–R4, R6 | "Reviewer acceptance before admission" | **Met, to reconfirm** | The independent ADR-031 review returned PASS with fixes (fixes `5a988b4`) and escalated only R5; R6 is the A-Q1/A2-L3/O5 dependency (D7-1, G9). The C-A independent review is asked to reconfirm R1–R4 explicitly |
| W1/watchdog | Decisions 3–5 conditions | **Met** | Implemented and green on Windows CI (`d56cb26`, `9c6ecc1`) |
| §8.7 owner-host | A real client self-upgrade under the held binding (a VM run), and a second reviewed release on the compiled list | **Open, covered by G9** | The activation plan §11 G9 asked "activation with the A-Q1 real-launch residuals open, or a target or VM run first"; the owner chose activation (2026-11-06). With one listed release the client route is inert: the offer rule needs a newer listed release, and stage B, F′ and the verifier find no target. Each item blocks any real client update until exercised (a reviewed re-pin and a VM run, separately approved) |

### 1.3 Composition record admission obligations 1–8 (runtime-limit route)

| # | Obligation | State | Evidence |
|---|---|---|---|
| 1 | Host evidence: eligibility, O5/A-Q1/A2-L3, a target package, AQ1UAC/runas | **Met except the target package, which G9 covers** | Part A PASS; G9 = yes: the A-Q1 real-launch residuals (native held DLL load, PowerShell helper reads, an elevated launcher holding files) stay open, and each blocks any real Chocolatey update until exercised |
| 2 | ADR-025 exact-artifact test | **Open (Part C)** | As D7-2 |
| 3 | Approved v2 redeploy | **Met** | As D7-3 |
| 4 | N6 H1 (lingering token) | **Closed** | `94018e8` |
| 5 | Desktop review and history UI | **Closed** | `a647db6`; the Desktop shows the no-review disclosure |
| 6 | Production intent resolver | **Closed** | `219a984`; unconstructed until a Desktop composition calls it (§2.3) |
| 7 | Proxy and client-certificate disclosure decision | **Closed** | ADR-025 owner decision 2026-11-02 |
| 8 | `HasSupportedMode` and pin change, with explicit owner approval after 1–7 | **Code part: this change**, under ADR-031 decision 7. **Final admission approval: after Part C**. **The owner's explicit confirmation of this ordering is required before deployment** (review M3) | Part C plan §3 row C-A |
| L1 | Review L1 route-admission obligation (composition record): a workflow test on a `ChocolateyMachineRuntimeLimitsV3` draft with a consistent Confirmed and a consistent Refused check, asserting `result.HelperCheck == record.Terminal.HelperCheck`, a version-3 row and no marker | **Met** (review M2) | `RuntimeLimitRouteActivationTests` (Application integration): the real workflow through the real SQLite store and history reader |

### 1.4 Probe and Part C items

| Item | State | Evidence |
|---|---|---|
| ADR-025 amendment and ADR-018 cross-note | **Accepted** 2026-11-04 | ADR-025, ADR-018 |
| Probe slices S1–S6 | **Done**, reviewed, Windows CI green | S6 integrated review `2172f95` |
| D4 (code guard) | **G-0** (2026-11-06): no guard, no §5.3a reader | Part C plan §6; pinned by `ChocolateyRouteActivationBoundaryTests.UnderG0NoCodeGuardOrClosedResultReaderExists` |
| G9 | **Yes** (2026-11-06) | Part C plan §6 |
| S7 obligation 1 (probe-build mark) | **Built here** (§3) | |
| S7 obligation 2 (no C3 on the Part B deployment) | **Holds** | `febf1ac` does not descend from `2172f95`, so its build record can never carry the mark and C1 stops |
| S7 obligation 3 (host preconditions) | **Built here** as checks and a mandatory owner answer (§4) | |
| G8 time bound | **Owner step** | C-B exec must start before 2026-11-20T09:43:26Z |

**Result.** Apart from Part C and the final admission approval, every gate is met or covered by an owner decision (G9, D4). **The activation change proceeds.**

## 2. What the activation changes

### 2.1 The three admission points (spec §7 D4 pins, changed deliberately)

- **Review gate** (`ReviewedUpdateReview.HasSupportedMode`). The WinGet rule is unchanged, byte for byte. One term follows it: `ReviewedChocolateyRouteAdmission.Admits(operation)`, which checks shape only:
  - current schema;
  - Chocolatey, Community, Machine and Stable;
  - provider-script-selected architecture and `NotControllable(ProviderScriptControlled)` mode;
  - the exact `choco-profile-v2:` + 64-hex evidence form;
  - and per route: `ChocolateyMachineRuntimeLimitsV3` needs guard `CommunityScriptRuntimeLimits` and provenance `UserCacheIntent`, and **never the package ID `chocolatey`, in any case** (ADR-031: "client target refuses" for package jobs). `ChocolateyClientSelfUpdateV1` needs guard `ChocolateyClientSelfUpdate`, provenance `TrustedDesktopEvidenceRequired` and the package ID exactly `chocolatey`.

  The test-only seam `ReviewedRouteTestAdmission` (an `AsyncLocal` admission scope for the gate-1 tests) is **removed**. The gate tests now run against the production rule.
- **Capability allow-list** (`DeploymentCapabilityPolicy.CapabilityOperationAllowlist`): `[ChocolateyMachineRuntimeLimitsV3, ChocolateyClientSelfUpdateV1]`. Everything derived from it follows:
  - The checked-in statement `src/PCSteward.ElevatedHelper/PCSteward.DeploymentCapability.txt` is now 149 B, SHA-256 `50F169DD…654B`, tokens in ordinal order with the client token first.
  - The deployment scripts' parser (`OwnerOnlyPayloadIntegrity.ps1`) accepts both tokens. The installed one-token statement still parses.
  - The probe's G1 keys: the desktop holds, and the helper evaluates, both tokens before UAC. The result file's `capabilityStatus` and the probe's `g1Status` have two entries.
- **Dispatcher** `ChocolateyRuntimeLimitsRouteDispatcher.Supports` uses `TryGetRouteProfileDigest`, the package arm or the exact client arm. The capability seam is consulted for that route, and still only after the shape check (no file I/O first).

### 2.2 Pin and test changes (each cites ADR-031 decision 7)

- **Architecture tests:**
  - new `ChocolateyRouteActivationBoundaryTests` (5 tests): the exact gate body; the activation type is read only by the gate and references only Domain values and string comparison; the allow-list definition, statement bytes and script token list; the `Supports` order; G-0 (no reader, no new unavailability reason);
  - `ClientRunFaultBoundaryTests.TheClientSelfUpdateRouteIsAdmittedOnlyThroughTheThreeActivationPoints` (replaces `NothingInSrcAdmitsTheClientSelfUpdateRoute`);
  - `DesktopProducersBoundaryTests.TheRuntimeLimitRouteIsAdmittedOnlyByShapeAndStaysUncomposed` (replaces `TheRuntimeLimitRouteStaysNonAdmitted`; the non-composition checks are unchanged);
  - `AdmissionGateSeamBoundaryTests`: the gate-1 seam must be absent; the gate-2 seams are unchanged;
  - `DeploymentCapabilityBoundaryTests`: the `Supports` order.
- **Behaviour tests** that asserted non-admission now assert the admitted exact shape and the refusals that still hold:
  - the provisional route stays refused;
  - a runtime-limit job for the `chocolatey`/`Chocolatey` package stays refused;
  - each forged near miss of the client shape is refused;
  - a cancelled review is never confirmed;
  - mismatching fresh evidence never claims (`AdapterGatesPartATests`, the SQLite gate store test);
  - the client `Supports` refuses when the capability does not list the route.
- **Probe tests** use the two-token compiled contract:
  - goldens: passing frame 570 B, minimal rows 572/560/561/590/571 B, largest valid frame 626 B, and the closed result document;
  - the one-token goldens (520 B) are kept over a one-token fixture contract;
  - A3 runs one protection check per token.

### 2.3 What does not change

- **WinGet:** unchanged.
- **Release list:** still 2.7.5 only. The ADR-031 offer rule, stage-B target selection, F′ and the client verifier therefore find no client target: the client route is inert until a second reviewed release is listed.
- **Other refusals and checks** stay in place:
  - the observer's step 7 client-target refusal;
  - the R5 setup-override guard and runtime watchdog;
  - the downgrade-only client run-fault lattice;
  - verification before Success, and restart-pending never Success;
  - the fresh-evidence and profile equality;
  - the one-use permit;
  - the helper's own A1/A2/A3 checks.
- **No Desktop composition.** Nothing in `src` constructs the intent resolver, the route composition, the dispatch workflow, the route dispatcher or the protocol-2 transport (architecture pins unchanged).
  - So the Desktop prepares no Chocolatey review. The dialog still shows the no-review disclosure, whose text "It stays off until its checks have been proven on a real PC (… an exact-package test …)" states the D4 = G-0 procedure, and Confirm stays disabled.
  - **Finding:** spec §9 says "between the activation deploy and a recorded PASS, the desktop would enable Confirm". That does not hold for this build. Enabling a real Chocolatey review needs a separately reviewed Desktop composition change (design §8.5, open item "Desktop UI wiring"), which is outside C-A. G-0 holds as procedure in any case.

## 3. Probe-build mark (S7 obligation 1)

The G3 builder (`scripts/New-OwnerOnlyCandidate.ps1`) writes build record schema **`pc-steward-owner-only-candidate-build-v2`** with these fields:
- `probeIncluded` (bool);
- `probeMark = 'pc-steward-probe-build-mark:1'`;
- `probeMarkDetail` (`included`, `source-dirty`, `commit-lacks-probe`, `payload-missing:<leaf>` or `type-missing:<leaf>/<type>`);
- `probeCommit`.

**Rule `pc-steward-probe-build-mark:1`.** All three must hold:
1. **Clean build.** The source tree was clean before and after the build, so the payload was built from the commit.
2. **Lineage.** The build commit is, or descends from, the S6 integrated-review commit `2172f955a751ccc464b27075053c835790bc412f`, the last reviewed probe slice. The check is `git merge-base --is-ancestor`. Exit 0 counts as true. Exit 1, and any other exit (for example a shallow clone), count as false.
3. **Payload.** The payload assemblies carry the compiled probe entry types, each found as a NUL-terminated metadata name:
   - `PCSteward.Execution.dll`: `QualificationProbeWireCodec`, `QualificationProbeResultDocument`, `QualificationProbeEntry`, `ReviewedHelperProbeBootstrap`, `ReviewedHelperProbeEndpoint`, `ReviewedHelperProbeTransport`;
   - `PCSteward.Provider.Chocolatey.dll`: `NativeChocolateyProbePort`, `NativeChocolateyProbeItem`;
   - `PCSteward.Desktop.dll`: `QualificationProbeLaunchGate`, `QualificationProbeEntry`.

Lineage is the primary criterion. The payload check is presence evidence that the pinned bytes were built with the probe. A rewritten history fails closed. `febf1ac` (Part B) does not descend from `2172f95`.

**Git isolation (review L8).** Every builder Git call (commit, status and lineage):
- runs with `--no-replace-objects` and `GIT_NO_REPLACE_OBJECTS=1`;
- clears `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_OBJECT_DIRECTORY`, `GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_COMMON_DIR`, `GIT_NAMESPACE`, `GIT_REPLACE_REF_BASE`, `GIT_GRAFT_FILE` and `GIT_SHALLOW_FILE` for the call, and restores them afterwards;
- keeps `GIT_CONFIG_*` (the CI launcher's process-scoped `safe.directory`).

The fixture drives the lineage function through a stand-in git: exit 0 is descent only when it was called isolated; exits 1 and 128 give `probeIncluded = false`.

**Ignored files (decision).** The dirty rule stays as before: tracked changes and untracked files count; ignored files do not. The build itself writes the ignored `bin/` and `obj/` outputs into the tree, so counting them would mark every build dirty. The mark rests on lineage plus the payload check, not on the absence of ignored files.

**CI evidence.**
- `Test-OwnerOnlyCandidateBuilderCI.ps1` now fails unless the record is v2 with `probeIncluded = true`, detail `included`, the rule and the commit.
- The `owner-only-candidate-builder` job checks out full history (`fetch-depth: 0`).
- The builder fixture runs the real mark function on synthetic payloads (included, dirty, not a descendant, missing assembly, missing type, prefix-only name).

## 4. S7 host tooling: `scripts/evidence/Task50-QualificationProbe.ps1`

- **Bytes.** ASCII and CRLF, checked in with `-text`. SHA-256 (CRLF) `831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A`, 42,618 B; the approval binds these bytes.
- **Where it runs.** The owner's own non-elevated Windows PowerShell 5.1 console, after C-pre.
- **Parameters** (all mandatory):
  - `-Mode Check|Run`;
  - `-ActivityCheck NoBlockingRecord`: the owner's C-pre answer, S7 obligation 3;
  - `-BuildRecordPath`: the C-B `candidate-build.json`;
  - `-ExpectedBuildRecordSha256`: that file's SHA-256, as printed at C-B prep (review L7). The file is refused if it is over 64 KiB (checked before it is read) or its hash differs;
  - `-ExpectedManifestSha256`, `-ExpectedLedgerSha256`, `-ExpectedSignerThumbprint` and `-ExpectedSignerNotAfterUtc`, all from the C-B record.
- **What it never does.**
  - It never runs choco and never asks for elevation itself.
  - It starts exactly one process: `Start-Process -FilePath <Program Files>\PC Steward\PCSteward.Desktop.exe -ArgumentList '--qualify-chocolatey-helper' -Wait -PassThru`.
  - It writes nothing. It deletes only `%TEMP%\pcsteward-exact-artifact\qualification-result.json` and that folder, and only if C3 created them and they hold nothing else.
  - It prints only fixed tokens, hashes, counts, versions and the closed result document. Any other exception text is replaced by a fixed token, so no path, user name or SID is printed.

**Steps and output tokens** (any unexpected state prints `STOP: …` and ends the run with exit 1):

| Step | Output |
|---|---|
| Preconditions | `STOP: precondition (…)`. Stops when: not Windows PowerShell 5.1; the console is elevated; a pin is not uppercase hex; a `choco` or `PCSteward*` process is running; `TEMP` is an 8.3 path; the result folder already exists; the Chocolatey folder is absent |
| C1 pins | `C1 PINS-OK ledger=<64> manifest=<64> desktop=<64> helper=<64> capability=<64> signer=<40> notAfter=<UTC> probeBuild=true commit=<40>`. Otherwise `STOP: pin differs (ledger\|manifest\|signer\|payload-origin\|ledger-state\|ledger-<leaf>\|protected manifest\|desktop\|helper\|capability\|signature\|signer NotAfter\|signer expires within 30 minutes)` or `STOP: not a probe build (<detail>)`. C1 reads: the ADR-022 ledger (hash equal to the expected pin; schema v6, completed, `pinned-unsigned-v2`, manifest, thumbprint, the three leaf hashes); the protected manifest copy; the installed desktop, helper and capability file against the ledger, and the capability file also against the activation build's two-token statement `50F169DD…654B` (review L6); Authenticode Valid with the expected thumbprint and `NotAfter`; and the build record (at most 64 KiB, the expected SHA-256, v2, Pass, the deployed manifest, clean, the mark rule and commit, `probeIncluded = true`) |
| C2 baseline | `C2 BASELINE: db=<64> data=<64> choco=<64> chocoFiles=<n> chocoProcess=0`. Stops on `STOP: database sidecar present` or `STOP: database absent (run the C-pre first launch)`. The `data` and `choco` digests are one SHA-256 over sorted `D\|path`, `R\|path` (reparse points are not followed) and `F\|path\|length\|sha256-or-UNREADABLE` lines |
| Check mode | `CHECK-COMPLETE: nothing was launched` |
| C3 probe | `C3 LAUNCH: …` (the owner's prompt instruction), then `C3 EXIT: <n> RESULT: present\|absent` |
| C4 after | `C4 UNCHANGED: db=<bool> data=<bool> choco=<bool> chocoProcess=<n> pcstewardProcess=<n> resultFolderClean=<bool>`, or `C4 AFTER-UNAVAILABLE: <token>`. `resultFolderClean` inspects only `%TEMP%\pcsteward-exact-artifact` (absent, or holding at most the result file); the rest of `%TEMP%` is not compared (review L9) |
| C5 evidence | `C5 RESULT: <the closed JSON>`, `C5 RESULT-REJECTED: result-<token>`, `C5 RESULT-ABSENT` or `C5 RESULT-ABSENT (empty result folder)`. An empty result folder means no result file: E3 is INCONCLUSIVE ("no-result-file"), not a FAIL (review L5). The read is strict: at most 4,096 B; printable ASCII; no BOM; compact JSON with no escapes; the exact §5.3 key sets and order; the closed domains of the activation build (both tokens); presence of `probe` exactly when the outcome is `ResponseAccepted` |
| C6 interpretation | `C6 VERDICT E1:`, `A3:`, `E2:`, `E3:` and `E5:` each `PASS\|FAIL\|INCONCLUSIVE (<closed detail>)`; then `C6 OVERALL: …`; then `C6 OWNER-CHECK E3: …`. These follow spec §8. A malformed result file is an E3 FAIL. E5 is INCONCLUSIVE only when the C2 baseline is missing; a C4 observation that fails (database deleted, a sidecar present, a tree unreadable) is a difference, so E5 is a FAIL (review M1). An `OVERALL: PASS` is valid only with the owner's confirmation of the single expected prompt |
| C7 cleanup | `C7 CLEANUP-COMPLETE`, `C7 CLEANUP-NOT-NEEDED` or `STOP: cleanup refused (<reason>)` |

**Tests** (`QualificationProbeHostToolingTests`, with `Fixtures/QualificationProbeHostTooling.ps1` under StrictMode 3.0):
- **Structure:** ASCII, CRLF and `-text`; one `Start-Process` with the exact arguments; Check mode returns before the launch; the C1 probe-build stop precedes C2 and C3.
- **AST policy:** no invocation operators; no forbidden commands, `-Verb` or `-Credential`; only `Open`/`ReadAll*`/`Delete` on `IO.File` and `IO.Directory`; exactly the two checked deletes; no network, registry or Process types.
- **Contract:** the script's lists equal the C# enums, the result-document field orders, the compiled tokens and listed releases, and the builder's mark constants.
- **Pure-function rows**, run under pwsh, with the Windows PowerShell 5.1 row as Windows-CI evidence:
  - 15 result cases written with the real `QualificationProbeResultDocument.Create`, and 13 malformed mutations, each with its exact verdict tokens;
  - build-record and ledger vectors;
  - the tree digest, recomputed independently in C#, with a reparse point recorded and not followed;
  - the C7 folder rule;
  - message sanitisation.

## 5. Validation (Linux, .NET SDK 10.0.403; source projects rebuilt `--no-incremental` before the IL scans)

| Suite | Base `e2f35af` | After | Notes |
|---|---|---|---|
| ArchitectureTests | 135/135 | **140/140** | +5 activation pins |
| Domain.UnitTests | 175/175 | **175/175** | |
| Application.IntegrationTests | 874 passed, 112 failed | **886 passed, 112 failed** (after the review fixes; 874 at `0c659eb`) | The same 112 (Windows-only); no new failure. +12 `RuntimeLimitRouteActivationTests` |
| Security.FixtureTests | 1,334 passed, 240 failed, 5 skipped (of 1,579) | **1,338 passed, 240 failed, 6 skipped (of 1,584)** after the review fixes (1,337 at `0c659eb`) | The same 240 failures (Windows-only: `PlatformNotSupported`, `powershell.exe`); no new failure. New: `QualificationProbeHostToolingTests` (3 pass; the Windows PowerShell 5.1 row is inconclusive off Windows); the builder fixture also runs the probe-mark rows |

Every changed `.ps1` parses with the PowerShell 7 parser through the repository fixtures. The Windows PowerShell 5.1 rows (parse, builder installer reader, probe tooling) and the Windows-only suites need **Windows CI**.

## 6. Residual risks

- **Desktop composition.** None exists, so the activation is code-level only; see the §2.3 finding. A later Desktop composition needs its own review, plus the D4 reconsideration that Part C plan §6 already records.
- **Obligation 8 ordering.** The composition record wrote obligation 8 as "a change to `HasSupportedMode` … only with explicit owner approval after items 1–7". The Part C plan, with Decision 3 (c) and decision 7, places the code change here and the owner's final admission approval after Part C. This record follows the plan. If the owner reads obligation 8 differently, this branch must not be deployed until they confirm.
- **R1–R4** reviewer acceptance is recorded implicitly (§1.2). The C-A independent review should confirm it.
- **The probe-mark payload check** is name-presence evidence (a suffix-shared metadata string could match). Lineage is the primary criterion.
- **C1's trust basis.** C1 trusts the admin-only-writable ADR-022 ledger for the installed hashes. It does not re-derive the unsigned image identity; the C-B validator run does that.
- **What a PASS proves.** UAC and pre-launch enforcement only (spec §8). The A-Q1 real-launch residuals and the client self-upgrade residuals stay open (G9). Each blocks any real Chocolatey update until it is exercised.
- **Windows-only tests not run here:** the native probe, native end-to-end gate 2, and the Windows PowerShell 5.1 script rows.
- **Merge condition (review L10).** Merging the Task 73 client-offer UI onto an activated branch requires re-verifying design §8.6 gate 3 (the offer comes only from the verified observer) before that merge is deployed. Gate 3 is "not applicable" here only because this branch has no offer UI.
- **Re-pin for C-B.** The Part B pins no longer match this branch and must be re-pinned in the C-B blocks:
  - the capability statement `0772DAD8…D9C7` (103 B) is now `50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B` (149 B);
  - the builder pin `BC67E825…5644` (CRLF) is now `BB6E0A60A8324341A3D5DA1606977D23CF653B2480724EB33E092E1BEE7D68D7` (LF `B9341F75BBE2CC0CB22F5BFBB158F606CF7D04221C8C7E17E780FFF4EF5912FD`);
  - `OwnerOnlyPayloadIntegrity.ps1` is now CRLF `83B87BBDE621992E32C0F042D26ED0707C106E18DF5CDD0EDF0CEA84521DD09F` (LF `0874B755D88800BEBDD0D093BF90152C0B36729A6A9BBBFBE78E71136EEBA0A6`).

## 7. Next

Independent review of this change and Windows CI on the branch. Then Part C plan revision 2:
- C-B prep and exec at the reviewed commit, before 2026-11-20T09:43:26Z;
- C-pre;
- C-run, with this script's CRLF SHA-256 and the C-B pins filled into approval 2.

## 8. Independent review of `0c659eb`: dispositions (2026-11-06)

No Critical or High finding. Each fix below is applied, with tests.

- **M1, fixed.** E5 is FAIL when C4 cannot observe (`after-observation-failed`); INCONCLUSIVE only without a C2 baseline. The fixture now runs the real C2/C4 observation over synthetic roots: unchanged → PASS; sidecar, deleted database or missing Chocolatey tree → FAIL; changed database → FAIL; no baseline → INCONCLUSIVE. The earlier row that pinned the wrong behaviour is replaced.
- **M2, fixed.** The composition record's L1 obligation test: `RuntimeLimitRouteActivationTests` (§1.3).
- **M3, recorded.** The owner's explicit obligation-8 confirmation is required before deployment (Status, §1.3, §6).
- **L4, fixed.** The package-route admission refuses every client identity through the shared Domain predicate `ReviewedUpdateOperation.IsChocolateyClientIdentity` (`chocolatey`, `chocolatey.extension`, any case). The provider's `ChocolateyClientReleasePolicy.IsClientTarget` delegates to it. Tests: the extension rows in `ReviewedUpdateDispatchWorkflowTests`, and `RuntimeLimitRouteActivationTests`, which checks the predicate and `IsClientTarget` agree.
- **L5, fixed.** An empty result folder is `absent` (`Read-ProbeResultFolder`, fixture rows).
- **L6, fixed.** C1 requires the installed capability statement to be `50F169DD…654B`.
- **L7, fixed.** `-ExpectedBuildRecordSha256` (required). The build record is read only when at most 64 KiB, then checked against the hash (`Test-ProbeBuildRecordBytes`, fixture rows).
- **L8, fixed.** The builder's Git isolation and the ignored-files decision (§3).
- **L9, fixed.**
  - Runtime-limit near misses are refused (Preview channel, observed architecture, controllable mode, lowercase evidence, client identities): `RuntimeLimitRouteActivationTests`.
  - A Preview-channel row is added to the client forge rows.
  - The E5 token is renamed `resultFolderClean`; it inspects only the result folder.
- **L10, recorded.** The Task 73 merge condition (§6).
