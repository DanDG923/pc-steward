# Task 70 — Advisory AI security, privacy and cost review

**Verdict: FAIL.** Reviewed on 2026-11-14. This is an independent review of **`db4bc76adbfb87e9b64a66256f8694b656f342f1`**, on `phase/4-beta`, for **`gpt-6.1-sol`, `service_tier: "default"`, `POST https://api.openai.com/v1/responses`**, and Task 69 report §10's Task 71 plan. It does not approve a live test or ordinary use.

The compiled build is safely unavailable: `AdvisoryAiModelProfiles.Reviewed` is empty and `AdvisoryAiBuildPolicy.PaidRequests` is `Disabled` (`src/PCSteward.Domain/AdvisoryAiPolicy.cs:221,233`). That prevents present exposure; it does not prove the dormant path is ready to enable. There are unresolved continuity, privacy, paid-bound and resource gates below.

## 1. Authority, identity and entry evidence

- Read `AGENTS.md`, `docs/STATUS.md`, `prompts/70_AI_SECURITY_PRIVACY_COST_GATE.md`, `V2_PHASE_G.md`, `V2_TASK_CONTRACT.md`, accepted ADR-027/028/029 including ADR-028 A1, `TASK_67_ADVISORY_AI_PLAN.md`, and `TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md`. Consulted the cited specification requirement rows, ADR-006/014/015, the exact-host AI contract, and directly relevant lifecycle/gate source and tests.
- Local HEAD and GitHub `refs/heads/phase/4-beta` both resolved to the full SHA above before review. GitHub still resolved to that SHA at the final check. The worktree was initially clean. Task 69's report is committed, with its B1 decision last changed by `0cd07ac399196ea6be97e199a03776e3344881e8`; Task 69 profile/cache/price changes are in this pinned tree. The immutable-source entry condition is met; the report's own B2 blocker is still open.
- All source/test line references below are **at the reviewed SHA**, not the later report commit. No other implementation commit was reviewed. The documentation branch starts at the reviewed SHA. No dirty product overlay is part of the review.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05`, `NFR-PERF-01–10`; threat boundaries `TM-03/04/07/13`. `FRA-14–16` remain separate public-distribution gates, not passed by this review.
- No real key, Credential Manager operation, OS-record collection, AI-provider call, paid request, account query, deployment, application launch or release occurred. Network use was limited to GitHub repository operations and pinned NuGet restore. Provider documentation was **not contacted**: Task 69's cited pages and dated factual claims are reviewed evidence inputs, not independently refreshed provider proof.
- Task-specific output authority is the report and its STATUS entry only. Therefore TRACEABILITY, product code and committed tests are unchanged. The owner's explicit PR request authorizes publishing these two documentation changes despite the generic prompt's no-push default. It authorizes no merge or next task. No subagents or worktrees were used.

## 2. Findings

Severity indicates the consequence when the advisory path is enabled. High findings and unproven mandatory acceptance block the gate even though this build disables paid use.

### T70-01 — High: an inconsistent uncertain commitment frees allowance without review

**Files/lines:** `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs:246–274` (especially 252–263); schema at 60–65. **Requirements:** `FR-BUD-02/03`, `NFR-REL-02`, `TM-07`; ADR-029 untrusted reads and retained uncertainty.

`SnapshotAsync` reads only `period,state,committed,first_live_test`. A non-negative `committed` is accepted without checking it against `reservation`, the state transition, frozen tariff/bounds or settlement evidence. An `Uncertain` row with a positive reservation and zero commitment passes both the schema and read validation. The same weakness affects cumulative first-test totals when `first_live_test=1`.

**Concrete reproduced scenario:** in an isolated migration-14 database, connect synthetically, declare continuity, confirm synthetic rates, and set both monetary caps to 12,000 micro-USD. Reserve/claim a 12,000-micro request and settle it as `Uncertain/TimedOut`, still committed at 12,000. A second reservation correctly returns `AboveMonthlyBudget`. Inject `UPDATE ai_requests SET committed=0 WHERE state='Uncertain';` without disabling any CHECK constraint. The second reservation now returns `None`; the spending summary reports `ReviewBlock=False`. Recovery does not repair or flag this row.

This is a detectable internal inconsistency, not a demand to detect an internally consistent external rollback or defeat a compromised Windows user. The existing `AnAlteredLedgerRowBlocksPaidRequestsInsteadOfGrantingThem` fixture (`AdvisoryAiLedgerTests.cs:325`) instead corrupts the continuity enum with CHECK constraints disabled; it does not cover this case.

**Required remediation/evidence:** validate state-dependent reservation/commitment invariants and settlement bindings before admission and summary/recovery. A claimed or uncertain request must never contribute less than its unreconciled liability. Inconsistent, missing or partial accounting must persistently disable paid use for continuity review; do not repair it by inventing a zero. Add both monthly and cumulative-test fixtures, including restart. No product fix is made here.

### T70-02 — High: the candidate profile and B2 still cannot bound the first paid request

**Files/lines:** `docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md:85–99,121`; `src/PCSteward.Domain/AdvisoryAiRequest.cs:162–171`; `AdvisoryAiPolicy.cs:195–211`. **Requirements:** `FR-AI-04`, `FR-BUD-02`; ADR-028 F1/F7 and ADR-029 reservation arithmetic.

**B1 is settled by the owner's option (a), and is not reopened here.** It accepts the byte-level upper-bound argument with a later live check. It does not settle B2. Task 69 labels the proposed 512 hidden-framing tokens unproven, while §10 requires a passing Task 70 before the paid call that would measure them. No separate acceptance/evidence for B2 is supplied. `IsCoherent` checks numbers and a Boolean, not their evidentiary provenance.

**Concrete scenario:** enable the proposed profile and approve a request whose local bound is text bytes plus 512. If the route adds more hidden tokens, the first send can exceed its reservation before returned usage flags the breach. A timeout can withhold that usage entirely. Stopping subsequent requests does not bound that first liability. One reported token count below the byte bound cannot isolate hidden framing from the slack in the visible-text byte estimate, or prove the bound for all permitted inputs.

The proposed 128 bytes/output-token and 262,144 response-envelope bytes are also unevidenced for this model (Task 69 §6 and R2/R3). The strict reader caps memory intake, but rejecting a larger otherwise permitted answer after paying is not proof that all admitted token settings have compatible response/saved-item allowances under ADR-028 F1.

**Required remediation/evidence:** resolve B2 and the coupled profile with evidence for the exact permitted shape, or obtain an explicit scoped policy change before treating an experiment as an accepted bound. Owner acceptance of B1 alone supplies neither. Keep the profile unavailable. An enabled test build is a new artifact requiring its own immutable review; a pass cannot be pre-issued for an unspecified edit of this commit.

### T70-03 — High: an acknowledged account-specific premium is outside the tariff and test preconditions

**Files/lines:** `docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md:106,121–124`; `src/PCSteward.Application/AdvisoryAiSetupPresentation.cs:97–110`; `src/PCSteward.Domain/AdvisoryAiBudget.cs:71–75`. **Requirements:** `FR-BUD-01/02`, `FR-AI-04`; ADR-029 Q7's no unreviewed regional/tier premium.

Task 69 R5 explicitly leaves unknown whether a data-residency project's key is refused at the compiled host or carries a 10% uplift. The reservation has only the three entered token rates. Neither Connect nor §10 requires a verified compatible project/pricing configuration before the first test; the report defers a disclosure until ordinary use.

**Concrete scenario:** the owner supplies a residency-project key and enters Task 69's base Standard rates. If the provider accepts it with an uplift, even usage within every token bound costs more than reserved. The token-usage reader cannot infer this premium from counts, and the later dashboard check is retrospective. This is an **unresolved provider-dependent risk**, not a claim that OpenAI actually charges that key at the compiled endpoint.

**Required remediation/evidence:** establish a compatible project/rate condition for the exact test and ordinary-use path, with truthful disclosure and owner-confirmed effective prices for every allowed category; otherwise block. Do not use an account API or a paid experiment during remediation without separate authority.

### T70-04 — Medium: the running-process forward-clock-jump guard is never used

**Files/lines:** `src/PCSteward.Domain/AdvisoryAiBudget.cs:316–324`; `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs:273–274`; desktop composition at `App.xaml.cs:214–217`. **Requirements:** `FR-BUD-01–03`; ADR-029 Q7 clock/rollover.

`IsSuspiciousForwardJump` has no caller in production `src`; only its Domain fixture calls it. The composition supplies wall-clock callbacks and no monotonic elapsed-time check. Admission and Settings notices use `Evaluate`, which accepts any forward movement inside the same month.

**Concrete reproduced scenario:** a running process advances 25 wall-clock hours while one monotonic minute elapses, remaining in November. The pure jump rule returns `True`; the rule actually used for admission returns `Accepted`. After entering fresh prices at that erroneous time, this guard provides no owner-review barrier. Crossing a month is still confirmation-gated, so this is not an automatic new-month refund claim.

**Required remediation/evidence:** wire monotonic observation into the common admission path and persist/clear the resulting review barrier deliberately. Add an integration fixture through production composition/admission, not only the pure rule.

### T70-05 — Medium: ledger retention and physical resource acceptance are unfinished

**Files/lines:** `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs:159–174,246–274,542–568`; `src/PCSteward.Application/AdvisoryAiAccounting.cs:149–151`; `src/PCSteward.Domain/AdvisoryAiRedaction.cs:82–97`. **Requirements:** `FR-AI-04/06`, `FR-BUD-02/03`, `NFR-REL-02`, applicable `NFR-PERF-01–10`; ADR-027/028/029 and ADR-006.

The 10,000-request-row ceiling blocks instead of deleting liability, which is safe. However, there is no current-plus-previous-12-month settled-detail compaction with dedup evidence. Every price confirmation appends a tariff even without a request, outside that row ceiling; there is no 16 MiB logical-ledger admission check. The staged plan explicitly leaves compaction for later, but Phase G requires it at this gate.

**Concrete scenarios:** an owner reaches 10,000 historical requests and remains blocked despite eligible old settled detail; repeated price confirmations continue adding rows independently of that ceiling. At allowed large answers, transport/JSON copies, regex sanitization, line splitting, saved-content reads and WinUI text layout can cost substantially more than the logical byte quota. A 4 MiB wire cap or 8 MiB content sum does not bound DB/WAL/sidecars/backups or process working set.

No AI saturation/warm-up/raw-sample/median/p95/process-tree CPU/peak-working-set/UI/cancellation/idle-I/O and physical storage evidence is supplied by Task 69. This review's tiny synthetic database measured **274,432 bytes**; that is only an observation for that fixture, not an NFR acceptance measurement. Tests and old non-AI baselines cannot fill this gap.

**Required remediation/evidence:** complete transactional, dedup-safe compaction while retaining unresolved liability; enforce the logical soft ceiling across the relevant accounting state; provide source-backed saturated physical-resource measurements under ADR-006, including migration/backup/clear/expiry behavior. Keep unmeasured mandatory acceptance open. This is not an instruction to launch the app or read owner data now.

### T70-06 — Medium: the consent screens omit the required residual-secret warning

**Files/lines:** `src/PCSteward.Application/AdvisoryAiExplain.cs:185`; `src/PCSteward.Desktop/MainWindow.AdvisoryAiExplain.cs:122–149,195–216`; `src/PCSteward.Application/AdvisoryAiSetupPresentation.cs:97–110`. **Requirements:** `FR-AI-02`; ADR-028 consent/redaction contract.

The draft states that account/domain/computer names were removed and invites editing. Neither it, the final review nor Connect explicitly warns that redaction is best effort and secrets may remain, as the specification requires. A code comment saying this is not user-facing disclosure.

**Concrete reproduced scenario:** the owner edits in `shared passcode 7391`. `SanitizeEvidence` preserves it verbatim. That is not itself a defect in a best-effort redactor; the missing warning makes the residual risk undisclosed at consent. The final exact-body preview still gives the owner an opportunity to remove it, which reduces severity.

**Required remediation/evidence:** show a plain residual-secret warning beside the draft/final consent and test its presence after edits; keep exact-body review. Do not promise complete secret recognition or silently broaden collection.

### T70-07 — Medium: Task 71's pause and evidence claims do not match the implementation

**Files/lines:** `docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md:125–132`; `src/PCSteward.Domain/AdvisoryAiBudget.cs:166–170,389,405–426`; `src/PCSteward.Domain/AdvisoryAiResponse.cs:51,222–235`; `src/PCSteward.Desktop/MainWindow.AdvisoryAiExplain.cs:263–273`. **Requirements:** `FR-AI-06`, `FR-BUD-02/03`; Task 71 exact-envelope/evidence contract.

The plan says uncertain outcomes also pause paid requests. An ordinary timeout instead becomes `Uncertain` with `AboveReservation=false`; it retains money but is neither in flight nor review-blocking. New explicit requests can still be admitted within remaining caps. The one-request stop must therefore be an enforced test/human procedure, not a claimed automatic pause.

**Concrete scenario:** the first test times out at a small reservation. FirstLiveTest still has most of its $1 allowance, so another newly approved request is technically possible. Separately, the plan asks to record all five reported usage fields including the exact `cache_write_tokens` count, but `AdvisoryAiUsage` retains only input/cached/output/reasoning/total, the cache-write count is collapsed to a Boolean, and the result screen shows cost/text rather than the raw counters. The committed path supplies no reviewed sanitized evidence-capture procedure for those observations.

**Required remediation/evidence:** correct the stop description; specify and review how a single test is controlled and sanitized counters are captured without a raw prompt/header/response sink. Pin the enabled build, artifact digest, profile, current tariff and prior-test commitments before seeking separate approval. Do not add instrumentation or enable anything as part of Task 70.

## 3. Exact egress and payload audit

| Boundary | Software evidence at the pinned SHA | Limit of proof |
|---|---|---|
| Provider registry / A1 | `AdvisoryAiProviders.cs:61–75`: OpenAI only; compiled HTTPS host, implicit port 443, exact `/v1/responses`, no query/user-info/custom endpoint; one exact credential target. Model names its provider, effort and own tariff. Missing/mismatched adapter pair refuses before reserving. | Adding a provider/model/picker still needs A1's own consent/generation/profile reviews; no Claude route is enabled. |
| Actual body | `AdvisoryAiProviders.cs:101–122`: `model`, fixed `instructions`, canonical draft JSON as the `input` string, `max_output_tokens`, `store:false`, `truncation:"disabled"`, `service_tier`; optional profile `reasoning.effort`; for the candidate, `prompt_cache_options:{"mode":"explicit"}` with no breakpoint. | This build has no enabled candidate. Provider honoring these controls is not proved by a mock. |
| Evidence inside input | `AdvisoryAiRequest.cs:43–86`, `AdvisoryAiEvidence.cs:115–138,191–210,243–255`: closed source kind, selected incident/job facts, bounded source coverage, known interval, nearby records and omitted counts. Origin IDs are local, not serialized. Failed-update evidence is confined to that job. | Best-effort redaction is not anonymity. B1 acceptance is an owner risk decision; B2 remains open. |
| Absent capabilities | No `tools`, `tool_choice`, conversation, previous response, streaming, background, file/image/audio, metadata/user/account/project routing, safety identifier, auxiliary token-count call, prompt-cache key or cache breakpoint. | Absence of tools prevents execution authority; fixed instructions alone are not prompt-injection protection. |
| Headers | `AdvisoryAiRequest.cs:101–116`, `OpenAiResponsesTransport.cs:164–183`: `Authorization: Bearer <key>` (preview placeholder only), `Accept: application/json`, `Content-Type: application/json`, `User-Agent: PCSteward-AdvisoryAI/1`, exact byte `Content-Length`, standard Host/framing. Guard clears SDK headers and compares body and key before sending. Runtime Activity propagation is disabled at line 148. | Mock captures semantic headers before the real socket stack. OS/proxy/TLS metadata, DNS and certificate trust depend on the host; no raw-wire/host claim. |
| Transport | Official OpenAI 2.15.0 / System.ClientModel 1.16.0 protocol method; zero SDK retries; guard refuses attempt 2; TLS validation unchanged; no redirects/cookies/default credentials/decompression; 10 s connect and 45 s attempt including response read. Guard intercepts 3xx before SDK redirect handling. Logging/tracing off. | The 45 s starts in transport, not the whole user interaction or all DB/postprocessing. No real DNS/TLS/proxy/deadline/latency observation was made. Compressed bodies are not decompressed or requested and fail JSON parsing. |
| Response | Admitted-byte stream cap on success/errors, depth 16, output-item cap 64; exact model, ID, terminal status, duplicate fields and output types checked; tool output rejected. Complete usage is independent of incomplete/empty/refused text. | Size exhaustion safely retains uncertainty; it does not prove a usable model-specific response profile. Unknown top-level provider fields are ignored, never acted on. |

Task 69's no-cache and <=272,000-input-price changes are coherent **conditional** protections: the candidate must actually request no cache and cap the fully framed input at standard-price size. A reported cache write makes usage unreliable and flags review. The software cannot prove provider-side cache/retention deletion or actual billing policy. `store:false` is correctly disclosed at Connect as not a Zero Data Retention guarantee.

## 4. Credential, isolation and lifecycle audit

| Area | Observed software behavior / fixtures | Remaining proof or risk |
|---|---|---|
| Credential identity | `WindowsCredentialManagerStore.cs:31–40,160–178,191–207`: registered provider object only; generic `PCSteward/OpenAI/ApiKey.v1`, current-user local-machine persistence, no username; only CredWriteW/ReadW/DeleteW/Free. Printable ASCII <=2,048 bytes; save reads back; native/mutable buffers released/cleared where implemented. | Tests use a fake native API. Real Credential Manager permissions, persistence, exact deletion and logon behavior need separately approved host evidence. No real credential was accessed. |
| Key entry / lifetime | Masked non-revealing PasswordBox, clear on dialog close, disposable mutable key holder; dispatch reads only after local reservation prerequisites, before durable claim. No environment-key discovery or config/SQLite/export/log key sink found. | PasswordBox/SDK immutable strings and OS memory are not securely erasable; ADR-028 expressly disclaims managed-memory/dump secure erasure. No guarantee against same-user malware/admin compromise. |
| Connect / replace | Prior-use question has no selected default. First deliberate connect treats exact orphan/unknown presence as yes/unsure; versions invalidate old approvals. Save/record failure removes/verifies the key; replace advances generation without refund. | No connectivity/balance/test call. Readback proves local storage, not provider validity. |
| Disconnect | `AdvisoryAiSetup.cs:330–352`: durably disconnect/invalidate first, optional stop callback, remove/verify exact targets, advance generation; accounting and saved explanations retained. Incomplete removal is truthful and retryable. Desktop changes and sends share the exclusive lease. | Removal-incomplete UI state is session-only; after restart ordinary Settings says disconnected and does not probe the key. This disclosed limitation is not evidence that the credential is gone. Factory reset remains a verification route; no provider revocation claim. |
| Factory reset | `FactoryReset.cs:84–102,135–175,210–239,492–493` and `App.xaml.cs:65–85,228–234`: remove key before DB deletion, recheck on recovery/onboarding/Continue, old marker category pending, residual blocks completion, exact compiled targets independent of DB. | Synthetic root/fake credential fixtures prove orchestration, not real key removal or a reset of this PC. Full reset deliberately deletes ledger/key with no surviving spending marker. |
| Content lifecycle | Migration 14 separates explanations and ledger with no cascading FK. Scan clear deletes DiagnosticTimeline explanations; update-history clear deletes FailedUpdate only; independent expiry/8 MiB eviction and startup/list/save cleanup. Body/prompt/hash/raw-response/reasoning are not persisted. | Logical deletion is not forensic erasure of SQLite free pages/WAL/external backups. Backup lifecycle/recovery filtering and failures need the accepted evidence; no new backup/recovery feature was inferred or tested. Quota eviction is silent in the save return, so shortened retention is not affirmatively evidenced by the result UI. |
| Answer isolation | Plain TextBlock output and advice-only labels; no markup/webview, link opening, execution, package matching, job authority or helper calls from answers. ScheduledHost/Execution/ElevatedHelper have no AI adapter capability; architecture fixtures pin it. | Hostile instructions may still produce misleading advice. Best-effort output redaction and no action authority are the controls, not an assurance the answer obeys instructions. |

ADR-027's source-specific manifest, ADR-028's credential/composition/redaction extensions and ADR-029's separate ledger are present in the affected code seams; ADR-014/015 boundaries are preserved. The remaining detailed baseline alignment, physical/backup evidence and accepted compaction behavior cannot be declared complete merely from the staged plan. Optional `AC-UX-01–09` manual accessibility evidence is not used to fail this gate.

## 5. Accounting and settings audit

- **Limits/prices:** all four limits are editable; initial 8,192/1,024 tokens and $0.25/$200 remain defaults. Checked micro-USD, no floating point, no extra dollar ceilings, zero disabling, positive request<=month, model/context/coupled-byte checks and deliberate old/new comparison are implemented. Price fields start empty; positive exact-model/default-tier tariffs are immutable/versioned with a half-open 24-hour validity interval. Opening pricing links is not confirmation and no automatic refresh/account lookup is composed.
- **Freeze and admission:** approval holds exact bytes, draft/source/origin/profile/limits and settings/consent/key/tariff generations. Reserve and claim recheck the current binding/newest tariff inside immediate SQLite transactions. Prospective edits do not refund commitments or rewrite frozen rates. Full worst-case arithmetic reserves every bounded input and output token at the higher allowed input rate; cached/uncached actual input is rounded together, reasoning counted once as output.
- **Claim/recovery:** `Reserved -> NotSent` can release only before claim; durable `DispatchClaimed` precedes the single send. Repeated claims are not send authority. Recovery releases unclaimed rows and marks claimed rows uncertain, with no replay. Desktop uses the common Global current-user gate plus legacy gate on a dedicated owner thread; native mutex ownership is not released across an arbitrary await. SQLite multi-connection fixtures are not themselves full multi-process/logon proof; gate fixtures provide separate synthetic coverage.
- **Settlement:** reliable complete terminal usage can settle an incomplete, empty or refused answer; malformed/cutoff/oversize/cancellation/connect/HTTP-error-without-valid-usage keeps uncertainty. Saving failure is separate. Exact repeated evidence is idempotent; conflicts/reused response IDs block. Above-bound/above-reservation usage records a higher known charge and flags review rather than clamps it. No automatic reconciliation or purchase/recharge API exists. T70-01 qualifies the validity of commitments read later.
- **Continuity:** no-prior-use is expressly a declaration; yes/unsure waits for a confirmed future month, which cannot reopen FirstLiveTest when test spending is unknown. Prior periods retain liability; first-test spending is cumulative across months. Reset/reconnect/defaults do not authorize a new $1 experiment. Missing/unreadable state fails admission; there is no demonstrated comprehensive migration-14 corrupt/partial/recognized-backup recovery matrix. T70-01 shows one detectable corrupt-row case that does not fail closed. Undetectable *internally consistent external restores* are separately disclosed and are not represented as solved.

## 6. Offline verification and measured evidence

Environment: native Windows x64; installed SDK **10.0.404**, accepted by `global.json` 10.0.403 with `latestPatch`; PowerShell 7; Release configuration. No warnings/errors were suppressed for the canonical checks. Tests use synthetic evidence, counting fake transport and fake credential APIs.

| Command | Actual result |
|---|---|
| `git rev-parse HEAD`; `git ls-remote origin refs/heads/phase/4-beta` | Both `db4bc76adbfb87e9b64a66256f8694b656f342f1` at entry. |
| `pwsh -NoProfile -File .\scripts\Restore.ps1` | Exit 0; pinned package restore. Initial pre-restore assets were MSTest 4.1.0 / Test SDK 18.0.1 rather than the tree's 4.5.2 / 18.11.0. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **165 passed**, 0 failed/skipped. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0; **167 passed**, 0 failed/skipped. In the actual PowerShell command the filter contains literal `|` separators, without the Markdown table escapes shown here. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **15 passed**, 0 failed/skipped. |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0; **0 warnings, 0 errors**, 17.63 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | **Exit 1: 4,236 passed, 3 failed, 3 skipped; 4,242 total.** Domain 460/460; Architecture 269/269; Integration 1,694 passed/1 skipped; Security 1,813 passed/3 failed/2 skipped. |
| Isolated offline C# reproduction against the pinned Domain/Application/SQLite projects | Exit 0; outputs below. No credential/transport constructed. The temporary fixture is not a product change or committed deliverable. |

Initial diagnostic attempts are not hidden: the pre-restore Domain build failed MSTEST0032 with stale assets; sandbox VSTest then could not connect to testhost within 90 s. Correct pinned restore and rerunning outside that sandbox resolved the focused-check environment problem, without editing code or weakening analyzer/test policy. An initial sandbox integration invocation exited 1 without diagnostics. Temporary harness execution used normal checks for the successful recorded run; a failed sandbox harness build was not counted as validation.

The full-suite failures at the unchanged baseline are:

| Fixture and source line | Exact observed failure | Interpretation |
|---|---|---|
| `RedeployToolingScriptTests.RedeployToolingFunctionsHoldTheirContracts("windows-powershell")`, `tests/PCSteward.Security.FixtureTests/RedeployToolingScriptTests.cs:386` | Expected substring `OwnerOnlyDeploymentCandidates`; actual `A fresh install requires absent PC Steward install and state roots (no file, folder or link).` | The fixture encounters an earlier fresh-install precondition on this host. No owner install/state root was changed to make it pass. |
| `RequesterTokenNativeTests.TheHelperFactoryRefusesWithoutALinkedTokenOrWithAMismatchedSplitHalf`, `tests/PCSteward.Security.FixtureTests/RequesterTokenNativeTests.cs:45` | Expected `EvaluationNotLimited`; actual `AuthenticationIdMismatch`. | Native refusal differs from the asserted runner-token verdict. Both refuse; root cause not established by this AI review. |
| `ReviewedHelperBootstrapTests.TheNativePortRefusesTheItemWithTheSpecificTokenVerdict`, `tests/PCSteward.Security.FixtureTests/ReviewedHelperBootstrapTests.cs:293` | Expected `EvaluationNotLimited`; actual `AuthenticationIdMismatch`. | Same observed verdict mismatch; no helper/token change made. |

These failures are outside the advisory AI source path, but the required full suite is **not passing** and is an additional open gate. The three skips also remain disclosed; no host-specific skipped scenario is claimed proven. No second product implementation attempt, host cleanup or waiver of a safety gate is implied.

Selected source-backed fixture coverage: `AdvisoryAiRequestTests` (redraft/edit/omission/order/preview binding); `AdvisoryAiPolicyTests` (coupled limits/context/overflow); `AdvisoryAiRedactionTests` (identifier/control/secret classes and answer growth); `AdvisoryAiResponseTests` (usage, no-text incomplete, cache-write and tool/duplicate rejection); `AdvisoryAiBudgetTests` (exact/one-unit-over, period/test/rounding); `AdvisoryAiLedgerTests` (real SQLite transactions, 8 separate-connection reservation contenders, claim/crash recovery, price/settings races, duplicate/conflicting usage, clears/expiry/saving independence); `AdvisoryAiAdapterTests` (counting network, redirects/errors/no retry/size/deadline and fake Win32 errors); setup/explain/reset/gate and five AI architecture fixture classes. These passing fixtures do not cover every mandatory hostile/migration/resource scenario; findings identify the limits.

### Reproduced outputs and reproducibility

The temporary console fixture references the pinned Persistence project, constructs its test-only database constructor by reflection, calls `MigrateAsync`, and invokes the public settings/ledger methods. The inconsistency injection is performed only in its own newly created synthetic DB. Rates are 2,000,000 / 100,000 / 10,000,000 micro-USD per million; request bounds 1,000 input / 1,000 output; reservation and both caps 12,000 micro-USD. Claim then `SettleAsync(... Uncertain, 12000, TimedOut, false ...)`, try a second request, inject the SQL in T70-01, and try again. This is sufficient to reproduce the finding without a key, network or app launch.

```text
initial-reserve=None
intact-second=AboveMonthlyBudget
inconsistent-row-written=1
inconsistent-second=None
summary-outstanding=12000; review-block=False
candidate-test-limits=None; request-bytes=13312; response-bytes=1847296; saved-bytes=2621952
full-ceiling-reservation=24576
forward-rule=True; production-wall-only-verdict=Accepted
synthetic-db-physical-bytes=274432
unrecognized-secret=shared passcode 7391
```

Candidate size calculations use Task 69's **proposed**, not proven, profile: context 1,050,000; output max 128,000; framing 512; byte-level flag true under B1(a); token bytes 128; envelope 262,144; standard-price input cap 272,000; low effort; no-cache request. At 2,048/2,048, request allowance is **13,312 B**, response **1,847,296 B**, saved item **2,621,952 B**, each arithmetically within 4 MiB. These are derived allowances, not actual provider response sizes or peak process memory. At Task 69's stated rates, **24,576 micro-USD = $0.024576** is the reservation for the *full 2,048 input ceiling*; the implementation reserves the preview's actual conservative input bound, so a smaller draft can reserve less. The calculation is conditional on B2 and the billing profile.

## 7. Task 71 plan disposition

**Not accepted.** Preserve the useful parts: one synthetic diagnostic, exact editable preview, one request/no retry, 2,048/2,048 proposed limits, $0.05 request/$1 month and separately enforced cumulative $1 test envelope. These limits are arithmetically compatible with the candidate; that is not authorization or a complete paid bound.

Before asking for a live-call approval, Task 90 and a fresh independent Task 70 must resolve the findings and produce a concrete reviewable test packet:

1. Proven/explicitly resolved B2 and coupled profile; preserve B1(a) accurately. Resolve the account/pricing premium condition before *test* use, not just before ordinary use.
2. Pin the enabled test-build SHA and artifact SHA-256/manifest, SDK closure, exact profile and compiled `FirstLiveTest` policy. This disabled commit is not that artifact; no release or build-switch edit is authorized by this review.
3. Provide a synthetic-only diagnostic injection/selection procedure; do not improvise an OS collection to create evidence. Freeze the precise body/header semantics at action time with no secret in evidence.
4. Separately authorize exact-key entry/storage using the masked box, then the exact one paid send; no secret in chat. Verify compatible owner-confirmed current rates. Confirm prior settled **and uncertain** test commitments across resets/restarts/months outside ordinary settings; Task 69's assertion of $0 spent is not a current owner/accounting audit.
5. Review a sanitized usage/cost capture method, including optional/missing versus zero cache-write count, response/attempt binding, terminal incomplete and not-saved outcomes. Do not log body, body hashes, headers/key, raw provider errors or echoed evidence.
6. Make the one-request stop operationally explicit. Ordinary uncertainty retains reservation but does not automatically disable all future requests. Treat a breach, unexpected category/tier/cache, malformed/oversize/timeout/unknown outcome as stop-and-report; any retry needs a new task/approval. An owner dashboard observation is external corroboration, not software proof of invoice finality or a refund.
7. Maintain the separate ordinary-use/test-mode-exit decision. No success, month change, reconnect or cap edit promotes the build.

## 8. Handoff and final gate

Task 70 produces a failed review, not an implementation instruction. The safe next action is an explicitly requested **Task 90** addressing these findings, followed by a fresh Task 70 against a new immutable snapshot and concrete revised Task 71 packet. Keep reviewed models empty and paid requests disabled meanwhile. Do not start Task 71, access a key or make an AI-provider request. Public release/signing/distribution `FRA-14–16` remain open independently.

**Publication status:** the owner chose **“Keep the report local for now”** after being shown the three full-suite failures and AGENTS.md's “Commit only when required tests pass” restriction. The report and STATUS entry remain uncommitted on local branch `codex/task-70-ai-security-review`; no push or PR was made. The requested Opus 5.5 consultation could not be initiated: the session's collaboration model allowlist exposes GPT presets only, with no Opus 5.5 or cross-provider consultation tool. No model was substituted and no Opus dialogue is claimed. This availability limitation and the failing full suite remain disclosed; no additional output/failure file was created outside the owner's two-file boundary.

**FAIL — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, commit `db4bc76adbfb87e9b64a66256f8694b656f342f1`, and Task 69 §10's Task 71 plan.**
