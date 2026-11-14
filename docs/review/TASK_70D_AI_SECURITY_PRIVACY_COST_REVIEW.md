# Task 70D — Fourth AI security, privacy and cost review

**PASS — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, exact commit `14ef51b916aed66423a8f37d61da0b13a0d3e049`, and the Task 71 packet in Task 69 §10.** Reviewed 2026-11-14.

This pass covers **the disabled implementation and the packet only**. `AdvisoryAiModelProfiles.Reviewed` is empty and `AdvisoryAiBuildPolicy.PaidRequests` is `Disabled`. It does **not** cover an enabled test build: that future source/artifact must receive its own independent exact review. No key entry, paid send, application launch, deployment, release, ordinary use or test-mode exit is authorized here.

All ten earlier findings were checked against this tree. Nine are fixed for this scoped gate; T70-05 is partly fixed because ordinary-use physical-resource acceptance remains open. No unresolved privacy, paid-bound or continuity finding remains within the accepted one-request policy and this review's scope. The accepted B1/B2 residuals and provider/host limits below remain explicit; a pass is not an invoice, tokenizer or live-host guarantee.

## 1. Identity, requirements and evidence boundary

- Fetched `phase/4-beta`, verified its head and the review checkout as **`14ef51b916aed66423a8f37d61da0b13a0d3e049`**, and started `codex/task-70d-ai-security-review` there with no tracked overlay. This is the sole implementation target. The later two-file documentation commit is not another implementation under review.
- Read `AGENTS.md`, STATUS, the Task 70 prompt, `V2_PHASE_G.md`, `V2_TASK_CONTRACT.md`, accepted ADR-027/028/029 including ADR-028 A1, the Task 67 plan, Task 69 including revised §§6/7/10, and Task 90 including §§4/5. Read all three owner-local earlier reports, including original Task 70 §7's seven packet requirements. The remediation record's statements were checked against source, fresh fixtures and independent reproductions.
- Preserved earlier report SHA-256 values: Task 70 `C640DFA9F47BF63A2A8A4D683EA60376710D797A090EA528678B1A4C93F06396`; Task 70B `692D6258E9158498E6B6CA21BE97311A6A14D05C29FD0B74C285B5D731A619E3`; Task 70C `9D4BB6376DB2176C662E22B3D4D0B7260DF271FFAE31DA9D0729C338C42EE79C`. They remain local, unchanged and outside this PR.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`, `TM-03/04/07/13`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05` and applicable `NFR-PERF-01–10`. Public-distribution/signing gates `FRA-14–16` remain open independently. Optional `AC-UX-01–09` evidence is not a failure reason.
- Inspected the named Domain/Application AI files (including `AdvisoryAiClockWatch.cs`), Windows adapter, SQLite AI schema/migrations/compaction, desktop dialogs/composition, and matching tests including `AdvisoryAiTask71CandidateTests`. The implementation change since 70C is confined to the response reader and its tests; accounting and Task 69's packet are unchanged. All conclusions here concern their current pinned versions.
- Only this report and its STATUS entry change. No product code, committed tests, ADR, task packet or TRACEABILITY change. The explicit two-file task boundary overrides the generic traceability-update instruction; the explicit PR request provides publication authority, without merge/release authority. No subagents or worktrees.
- No real key, native credential access, AI-provider network call, paid request, account query, OS-record collection or installed-app action. Task 69's dated provider facts were treated as supplied evidence, not refreshed by contacting a provider. GitHub operations and isolated synthetic fixtures are separate from prohibited provider operations.

## 2. Disposition of all ten earlier findings

Severity is the earlier finding's severity. File/line references in this report refer to **14ef51b**. SQLite references below are to `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs`; other paths identify their owning component.

| Earlier finding / severity | Disposition | Concrete scenario and independently checked current evidence |
|---|---|---|
| **T70-01 — High**, inconsistent detailed commitment | **Fixed.** | Lowering an Uncertain commitment below its reservation, or lowering a settled charge without its usage, now invalidates the snapshot (`SqliteAdvisoryAi.cs:291–306,455–482`). Admission/summary/recovery persist review instead of granting allowance. All seven detailed-corruption cases passed, including test spending and restart. A coordinated internally consistent whole-state rewrite remains the disclosed external-restore limit, not this original defect. |
| **T70-02 — High**, B2/coupled profile | **Fixed for this one-request policy.** | The owner expressly selected 8,192 framing tokens, 192 bytes/output token and a 1 MiB envelope. `src/PCSteward.Domain/AdvisoryAiPolicy.cs:195–211,250–270` enforces coupled allowances; the three candidate tests and independent arithmetic pass (§5). Hidden framing exceeding the margin remains the expressly accepted scoped B2 residual, not a measurement. Ordinary-use model support is unproven. |
| **T70-03 — High**, effective project prices | **Fixed under the accepted owner-confirmed-price policy.** | Entering base prices without acknowledging the key project's effective rates is refused (`src/PCSteward.Application/AdvisoryAiSetup.cs:212–230`; unticked desktop checkbox at `MainWindow.AdvisoryAi.cs:363–378`). The packet requires a non-residency project and effective rates before the test. Setup/desktop fixtures passed. Software does not inspect the provider account or certify the bill. |
| **T70-04 — Medium**, unused forward-clock guard | **Fixed.** | A 25-hour wall advance beyond monotonic elapsed time now sets a sticky pending jump and persists review before reserve (`src/PCSteward.Application/AdvisoryAiClockWatch.cs:22–54`, `AdvisoryAiRequestWorkflow.cs:123–130`; `src/PCSteward.Desktop/App.xaml.cs:214–216`). Three clock scenarios and failed-block-write behavior passed. The durable block survives restart; the in-process clock baseline does not. |
| **T70-05 — Medium**, retention/quota/resources | **Partly fixed overall.** | Old eligible settled/released detail compacts without freeing liability; prices prune safely; the 16 MiB logical admission ceiling works (§4). At large answers, parser/sanitizer/WinUI copies and SQLite/WAL/backups can still exceed logical-value sizes. CPU, working-set, UI, idle-I/O and full physical-storage acceptance remain open for ordinary use, not waived. These alone need not block the separately reviewed, bounded one-request experiment (§5). Locations: `SqliteAdvisoryAi.cs:193–214,389–448`; `src/PCSteward.Application/AdvisoryAiAccounting.cs:148–162`; `src/PCSteward.Domain/AdvisoryAiRedaction.cs:82–97`. |
| **T70-06 — Medium**, missing residual-secret warning | **Fixed.** | A secret the redactor cannot recognize remains the owner's inspection responsibility. Both draft and exact-request screens now show the explicit best-effort warning (`src/PCSteward.Application/AdvisoryAiExplain.cs:190`; `src/PCSteward.Desktop/MainWindow.AdvisoryAiExplain.cs:124,203`). Matching boundary fixture passed; no complete secret-recognition claim. |
| **T70-07 — Medium**, pause/evidence mismatch | **Fixed for the packet.** | A timeout retains the reservation and does not automatically pause; the packet now explicitly requires one attempt, manual $0 request cap and stop/report. Counts-only output distinguishes missing, zero and unreadable cache-write figures (`AdvisoryAiRequestWorkflow.cs:192–202`, `AdvisoryAiExplain.cs:251–263`, desktop outcome line 270). The cache-write pause exceptions from 70B/70C are now fixed (§3). Fresh response/explain fixtures passed. |
| **T70B-01 — High**, compacted test spending | **Fixed.** | Lowering only a compacted month's test total after a $0.99 settlement now gives review and `LedgerNeedsReview`, rather than admitting another $0.045056. Retained per-request charge/test/five-count proof at `SqliteAdvisoryAi.cs:118–129,328–387` validates every monthly sum/count on reads. Seven compacted-corruption cases and the original independent reproduction passed. |
| **T70B-02 — Medium**, cache write in an early-refused body | **Fixed.** | A wrong-model body reporting 12 cache-write tokens still has no trusted usage, keeps the full reservation and now has `NeedsReview=True`. The outer reader propagates the billing signal before returning (`src/PCSteward.Domain/AdvisoryAiResponse.cs:120–129`; settlement `AdvisoryAiBudget.cs:414–419`). Five refusal reasons × nonzero/unreadable/zero write variants passed, plus the independent original reproduction. |
| **T70C-01 — Medium**, duplicate billing selectors | **Fixed.** | An earlier write can no longer disappear behind a later zero/null/counter-free container. Every selector/counter occurrence is enumerated, ambiguity makes the figure unreadable, and settlement pauses (`src/PCSteward.Domain/AdvisoryAiResponse.cs:263–292`). The original parser → settlement → SQLite reproduction now persists review across a fresh store and refuses the next reservation. Eleven new repository cases plus an independent 364-case matrix passed (§3). |

No new actionable finding was established. T70-05's ordinary-use resource scenario is retained explicitly instead of silently marked complete.

## 3. Repeated and malformed billing fields: fix verification

[Current reader, lines 263–292](https://github.com/DanDG923/pc-steward/blob/14ef51b916aed66423a8f37d61da0b13a0d3e049/src/PCSteward.Domain/AdvisoryAiResponse.cs#L263) enumerates all decoded-name `usage` objects, all `input_tokens_details` occurrences within each, and all `cache_write_tokens` occurrences within each object. It reports an exact count only for one non-negative Int64 count on one unambiguous path. Repeated selectors next to a cache-write field, repeated counters, or a non-count value produce `(null, unreadable:true)`. The outer wrapper raises the above-bounds flag even when strict response validation returned early. Settlement retains at least the reservation and marks review; SQLite persists that block.

The independent matrix covered **364 cases**: all ordered pairs of zero/nonzero/null/string/negative/fraction/exponent-overflow/Int64-overflow values at each of the three repeated-field levels; selector objects paired with null, false, number, array or empty object in both orders; JSON-escaped equivalent property names; single-count controls; and duplicate usage with no cache-write field anywhere. Assertions checked readability, untrusted usage, above-bounds/review and retained liability. Original 70C null/missing-selector variants also passed separately.

Important distinctions:

- A single zero is an exact zero and does not itself pause. An absent counter is not an exact zero. A malformed count or ambiguous path is displayed as unreadable, never selected as an exact counter or trusted to reduce liability.
- Duplicate usage with no cache-write field anywhere remains malformed/unreliable and retains its reservation; there is no unexpected cache category to assert, so ordinary uncertainty alone does not pause. A nested duplicate can leave the answer status non-Malformed while usage is unreliable: settlement still stays Uncertain. This is not trusted billing evidence.
- Invalid JSON, excess depth or an oversized/truncated body cannot yield a trusted complete usage object. They retain uncertainty; the implementation does not claim to recover every counter from arbitrary non-JSON bytes or automatically pause every uncertain outcome.
- Parsing remains capped by the admitted response size and depth 16. The new scan is over that bounded JSON, with no provider call or regex extraction of broken JSON. Its allocations/timing are not host performance measurements.

The original 70C end-to-end result is now:

```text
duplicate-usage-cache=absent; unreadable=True; malformed=DuplicateProperty
state=Uncertain; committed=12000; needs-review=True
duplicate-usage-stored-review=True
duplicate-usage-next-reservation=LedgerNeedsReview
independent-billing-matrix-passed=364
```

These are synthetic hostile-response tests, not evidence that a provider actually returned duplicate JSON or billed a cache write.

## 4. Compaction, tariffs, logical quota and migration

**Transaction and conservation.** `RecoverInterruptedAsync` (`SqliteAdvisoryAi.cs:609–636`) starts an immediate write transaction, releases only unclaimed Reserved rows, makes unfinished claimed rows Uncertain, validates the ledger and uses a compaction savepoint. Compaction requires readable accounting, no review block and an accepted current month. It moves only Settled/NotSent detail strictly older than current month minus 12; November 2025 remains detailed in November 2026. Unresolved liability is never compacted. SQL first updates original-period monthly totals, retains settled IDs/tariff/charge/test/usage, then deletes eligible detail (`408–435`).

Every later snapshot recomputes compacted charges at their referenced rates and checks monthly money, test money and settled-request count against compacted requests. Missing months/requests and mismatched sums are rejected (`328–387`). The transaction additionally conserves current-period settled/outstanding, cumulative test settled/outstanding, in-flight state and all-time settled money (`437–448`). Released-request counts carry no spending authority. An independent trigger that added one micro-dollar to a monthly total caused rollback: `Compacted=0`, detailed/month/identity/review = **`1|0|0|1`**. Original detail survived and the durable review flag was set.

**Later corruption and dedup.** The original $0.99 historic test correctly blocks another $0.045056 before tampering. Lowering only the compacted test total now yields `LedgerNeedsReview`; the unreadable summary's zero is accompanied by review and cannot grant allowance. Seven fixtures cover altered money/count/charge/test flag and missing request/month, including restart. Provider response-ID reuse checks detailed and compacted records (`782–787`); duplicate/conflicting settlement pauses. Neither compacted nor detailed database records create approval or replay authority.

**Tariff pruning.** `ConfirmTariffAsync:193–214` creates a new immutable version, then removes older same-model/tier prices only if referenced by neither detailed nor compacted requests. The daily-confirmation fixture passed: a historically referenced rate plus the newest rate survive thirty confirmations. Uncertain and compacted requests retain frozen rates; pruning changes no spending. Prices are positive exact-model/tier owner-confirmed values, current only in `[confirmed, confirmed+24h)`. The store/UI never claims an account or billing API verified them.

**16 MiB arithmetic.** `AdvisoryAiAccounting.cs:148–162` counts detailed rows at 1,024 B, tariffs at 512 B, compacted requests at 512 B and months at 64 B, using checked arithmetic. The expanded compacted values fit their estimate: 36-byte GUID + at most 256 bytes of normal opaque provider ID + nine 8-byte integer values = **364 B**, below 512 B. Normal incoming provider IDs are further restricted to ASCII and 128 characters by the reader. Snapshot admission (`389–392`) refuses when another 1,024 B row cannot fit, or when 10,000 detailed rows already exist. At 32,766 compacted rows plus one tariff/month the room is **448 B**, so admission refuses; removing two identities with corresponding monthly count correction frees 1,024 B and permits a row. The boundary fixture passed. This is a logical value estimate, not a cap on file pages, indexes, WAL, singletons, arbitrary external DB corruption or backups. Tariff confirmation/pruning is not itself the request-admission gate.

**Migration 14 → 15 and failure.** `SqliteDatabase.cs:86–95` applies 15 after 14 inside the migration transaction and validates the expected schema; 15 is the maximum known version. The independent fixture used a populated synthetic migration-14 database with connected settings, continuity, a tariff, an Uncertain 12,000-micro first-test row and unrelated metadata. An injected abort at insertion of the 15 marker left **`14|0|1|kept`** (version/new tables/connected/metadata). Retry after removing the injected fault gave **`15|2|1|kept`**, preserving **`Uncertain|12000|1|1`** (state/commitment/test/tariff). No installed database or recovery backup was touched.

The source explicitly describes 15 as unreleased; the added charge/test/count columns are in that migration. Compatibility with an experimental developer database already stamped 15 with an older shape is not demonstrated. Such a database is not an approved enabled-test baseline: do not silently downgrade/reset it or infer continuity from it. Its own reviewed baseline/recovery plan would be required. The requested populated-14 upgrade and rollback are proved; comprehensive external-backup recovery is not.

**Other accounting invariants.** Fresh fixtures cover exact/one-micro-over caps, checked upward-rounded micro-USD, higher input-rate reservation, reasoning counted once within output, settings/consent/key/newest-price rechecks in reserve and claim transactions, no send without unique durable claim, no automatic replay/refund, frozen period/rates, and complete reliable usage settling incomplete/empty/refused answers independently of saving. Ordinary cancellation/timeout/HTTP failure retains uncertainty. Conflicts, known above-reservation charge and detected unexpected cache writes pause; no automatic reconciliation or account/purchase/recharge path exists. Clear/disconnect/default changes preserve accounting. Full reset deliberately removes local spend knowledge and key; declaration, recognized-recovery checks and external prior-test accounting remain necessary. Coordinated consistent whole-state rewrites and undetected external restores remain the accepted local-first limitation, not software-proved continuity.

## 5. Owner choices, first-request bounds and physical resources

**B1(a)** accepts the byte-bound argument with a later live comparison. **B2 “Use wide safety margins”** selects 8,192 hidden-framing tokens, 192 bytes/output token and a 1,048,576-byte envelope. These remain owner-selected policy values, not measured facts. A single returned input count cannot isolate hidden framing from slack in the visible-text byte estimate. This review does not reopen either choice or present coherence tests as evidence of an unpublished tokenizer.

For the packet's 12,288-input/2,048-output limits:

| Check | Independent result |
|---|---:|
| Complete request allowance | 74,752 B |
| Complete response allowance | 3,481,600 B |
| Sanitized saved-item allowance | 3,932,672 B |
| Canonical input evidence room after instructions/framing | 3,399 B |
| Full-ceiling reservation at supplied $2/$0.10/$10 per-million prices | $0.045056 |
| Full-ceiling reservation with explicitly entered 10% higher rates | $0.049562 |

All three byte allowances fit 4 MiB; aggregate saved content remains capped at 8 MiB. Both reservations fit $0.05, without silently granting a premium: the owner must enter the actual effective rates. The real preview reserves its own bound, which may be smaller than the full input ceiling. Fixed `low` reasoning, explicit `default` tier, no-cache policy and the 272,000 standard-price input threshold remain part of the candidate; it stays outside `Reviewed`.

**Judgment:** the B2 values and resulting small input/2,048-output, one-attempt, $0.05 packet are adequate safety margins for the accepted single supervised experiment. This is not a universal first-invoice guarantee: the expressly accepted risk of the route adding more hidden framing remains, and the provider must honor tier/output/no-cache semantics. Software can reject oversized intake and flag reported breaches; it cannot retrospectively make a provider's first charge bounded if that provider breaks the reviewed assumptions. That disclosed, scoped B2 policy is accepted here, not relabelled as a new unresolved finding or an ordinary-use waiver.

**Still open for ordinary use:** process-tree CPU/peak working set, UI responsiveness, complete read/parse/sanitize/save/display timing and cancellation, idle I/O, and physical DB/WAL/sidecar/backup-inclusive storage acceptance under ADR-006. Task 90's Linux 10,498,048-byte accounting-size observation and the passing Windows size-bound fixture do not prove these broader limits. Even 2,048 output tokens allow a 3.32 MiB response under this profile; token count alone proves no small-memory result. The enforced byte/deadline/one-attempt limits and supervised stop procedure make one separately reviewed synthetic experiment proportionate without ordinary-use saturation measurements. These absences alone need not block that experiment; they remain ordinary-use gates and inputs to the enabled artifact's own host/readiness review.

## 6. Revised Task 71 packet against original Task 70 §7

| Required packet item | Assessment of Task 69 §10 at this commit |
|---|---|
| 1. B1/B2/profile and effective prices | **Addressed for one experiment:** accepted choices, exact candidate values, key-project effective-price statement and non-residency project prerequisite. No measurement/invoice claim. |
| 2. Pinned enabled artifact and independent review | **Addressed as a future prerequisite:** separate `Reviewed=[gpt-6.1-sol]`/`FirstLiveTest` source SHA, artifact SHA-256/manifest, pinned SDK closure and exact independent review before approval. This pass covers none of those future edits/artifacts. |
| 3. Synthetic preparation and exact preview | **Addressed:** retain one editable item, replace it with the specified Contoso text and omit the rest. The packet expressly discloses real chosen-record interval/omission metadata. Owner approval must cover that visible metadata; it is not a wholly synthetic snapshot. Use a separately authorized existing Diagnostics record; no OS collection is authorized by this review. Cancel if unapproved PC data remains or prerequisites are absent. |
| 4. Separate key/send authority and prior spending | **Addressed:** masked key entry and exact one send require separate approvals. Both displayed settled and uncertain test commitments must be zero; compacted charges now count and validate. The owner separately accounts for prior/reset/restored/uncertain use that the local ledger might not know about. Reset or no-prior-use declaration cannot create new $1 authority. |
| 5. Sanitized capture | **Addressed:** read counts/cost/reliability/cache-write figure off the outcome screen; distinguish absence, zero and unreadable. No body/body hash/header/key/raw error/answer text retained in test evidence. Outcome labels/counts are software reports; provider usage-page comparison is outside corroboration, not invoice finality or continuity proof. |
| 6. One request, stop and retry | **Addressed:** bounds and full-ceiling cost are explicit. Reported cache-write ambiguity now pauses even on early-refused bodies. Ordinary uncertainty retains commitment without automatic pause. After the one attempt, whatever the outcome, set request cap to $0 and stop/report; no retry without a new task/approval. The manual stop is not replaced by an assumed automatic one-request fuse. |
| 7. No promotion | **Addressed:** no success, month change, reconnect or cap change ends FirstLiveTest. Ordinary use needs a separate decision/artifact and remaining measurements. |

The packet passes this review as a plan, not as completed host evidence or action-time authority. No Task 71 step was performed.

## 7. Egress, credentials, isolation and lifecycle

| Boundary | Source and software/fixture result | Provider/host limit |
|---|---|---|
| Exact route/body | `AdvisoryAiProviders.cs:61–75,92–125`: compiled OpenAI-only POST `https://api.openai.com:443/v1/responses`; no custom URL/query/organization/project routing. Canonical body contains model, fixed instructions, canonical redacted editable input, `max_output_tokens`, `store:false`, `truncation:"disabled"`, default tier, low reasoning and explicit no-cache mode without a breakpoint. `AdvisoryAiRequest.cs:151–169,208–220` binds final bytes/versions. | No live DNS/TLS/proxy/wire capture, provider enforcement or retention test. `store:false` is disclosed as no Zero Data Retention guarantee. Task 69's model/API facts are dated input evidence. |
| Closed headers/transport | `OpenAiResponsesTransport.cs:74–94,137–198`: exact destination/body/key and first-attempt guard; rebuilt Bearer Authorization (fixed placeholder in preview), JSON Accept/Content-Type, fixed `PCSteward-AdvisoryAI/1` User-Agent, exact Content-Length and standard Host/framing. Official OpenAI 2.15.0/System.ClientModel 1.16.0 protocol call; zero retries; 3xx intercepted before SDK handling. Standard TLS/system proxy, no cookies/default credentials/decompression; SDK logging/tracing and runtime Activity propagation disabled. 10 s connect/45 s attempt including bounded error/success reads. | Mocks prove closed semantics and attempts, not actual provider behavior or an uncompromised host. The attempt deadline does not cover every DB/sanitizer/UI operation. Oversize/cutoff rejection retains uncertainty; it does not prove a useful full answer or no billing. No tools, history/conversation, uploads, background, streaming, counting, account or secondary-provider call. |
| Consent/redaction/isolation | Evidence minimization and re-redaction after edits; residual-secret warning on both consent screens; approval tied to draft/source/profile/limits/settings/consent/key/tariff generations. `MainWindow.AdvisoryAiExplain.cs:197–224,237–274` presents exact preview, defaults Cancel, sends under the common lease and displays plain TextBlocks/advice. Architecture fixtures exclude AI capability from ScheduledHost, Execution and helper. | Redaction and instructions cannot recognize every secret or prevent misleading advice. Owner inspection remains essential. No output path into matching, eligibility, deterministic correlation, jobs, executables, scripts or automatic links was found. |
| Key lifecycle | `WindowsCredentialManagerStore.cs:31–40,46–141,164–207`: exact current-user generic `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence, printable ASCII <=2,048 bytes, save readback, exact deletion/fresh absence verification. No enumeration/environment/config/SQLite/export/log key fallback. Masked box is cleared; mutable buffers cleared where implemented. Deliberate connect handles orphan/unknown presence, replacement invalidates generations without refund. | Native calls were faked, never invoked with a real credential. Actual Windows storage/removal/logon behavior requires separate host authority. SDK/PasswordBox immutable strings and OS memory are not securely erasable. Local removal is not provider revocation. |
| Disconnect/reset/recovery | `AdvisoryAiSetup.cs:330–352` revokes durably first, removes/verifies exact keys, preserves content/accounting and reports incomplete removal. `App.xaml.cs:65–85,228–248` and reset fixtures cover DB-independent credential removal before DB deletion, every recovery/onboarding recheck, old-marker compatibility and residual blocking. Ordinary startup does not probe credentials. | Removal-incomplete UI status is session-only; disconnected after restart is not verified key absence. Real reset/key deletion was not run. Full reset removes local accounting without an external spending marker; declarations and outside test-authority checks remain necessary. |
| Content/accounting separation | Migration 14 separates saved explanations from accounting. 30-day startup/list/save expiry, 4 MiB item/8 MiB aggregate cap, source-specific scan/history clear, preserved independent ledger and credential, redaction again on read. No submitted prompt/body/hash/raw response/reasoning persisted. Usage settlement is independent of answer saving. | Logical deletion is not forensic erasure of SQLite free pages/WAL or user-managed backups. Comprehensive backup filtering/resource acceptance remains open for ordinary use; no new recovery feature is inferred from a passing migration fixture. |

## 8. Fresh verification

Native Windows x64, PowerShell 7, installed SDK **10.0.404** accepted by `global.json` 10.0.403/latestPatch. Release configuration, existing pinned package assets. Tests used the normal Windows test host outside the known sandbox connection limitation. No package/SDK/product change, weakened warning policy or test exclusion was introduced. Independent console fixtures lived under ignored `artifacts/task70d`, used new synthetic databases and constructed no credential store or transport.

| Command | Actual result |
|---|---|
| `git fetch origin phase/4-beta`; `git rev-parse HEAD origin/phase/4-beta` | Exact `14ef51b916aed66423a8f37d61da0b13a0d3e049` at entry. Rechecked before publication. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **184 passed**, no failures/skips. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~SqliteDatabaseTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0: **212 passed**, no failures/skips. Actual PowerShell filter has literal pipes; backslashes escape the Markdown table only. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **16 passed**, no failures/skips. **412 focused total.** |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0: **zero warnings/errors**, 7.26 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | Exit 0: **4,283 passed, 4 skipped, 0 failed; 4,287 total**. Domain 479; Architecture 270; Application 1,719 passed/1 skipped; Security 1,815 passed/3 skipped. |
| `dotnet run --project artifacts/task70d/ReviewHarness.csproj -c Release --no-restore` | Exit 0: original reproduction/rollback checks below plus **364 independent billing cases asserted**. Fixture-only restore used cached dependencies; no product dependency version changed. |

The four full-suite skips were `TheRunnersOwnTokenWithoutASplitTokenIsNoSplitToken`, `SymbolicLinkLeafIsRefusedEvenWhenTheProtectionCheckIsBypassed`, `TheProtectedRunFolderIsFreshAdministratorOnlyAndRemoved`, and the Windows PowerShell row of `RedeployToolingFunctionsHoldTheirContracts`. They remain unproven, not converted to passes. They do not skip an AI review fixture or supply the future enabled artifact's host evidence. These are fresh Windows results, not the remediation record's cloud counts or a previous review's totals.

Additional independently rerun outputs:

```text
historic-reserve=None
compacted=1
intact-test-settled=990000
intact-next-test=AboveFirstLiveTestEnvelope
lowered-test-settled=0; review-block=True
lowered-next-test=LedgerNeedsReview
migration15-injected-failure=refused
migration15-rollback=14|0|1|kept
migration15-retry=15|2|1|kept
migration15-preserved-ledger=Uncertain|12000|1|1
compaction-fault-compacted=0
compaction-fault-rollback=1|0|0|1
logical-near-ceiling-room=448
mismatched-cache-write=12; malformed=ModelMismatch; state=Uncertain; needs-review=True
```

No mock, source inspection, synthetic performance observation or local suite result is presented as provider enforcement, actual key deletion, invoice finality or WinUI/process-resource proof.

## 9. Handoff and explicit gate

No remediation is requested by this review for the disabled tree/single-request packet. Keep this commit disabled. A separately instructed next task may prepare a pinned FirstLiveTest source/artifact with exactly the packet's profile and SDK closure; that enabled artifact needs its **own independent review** before the separate exact-key and one-send approvals. Nothing here starts that task, supplies those approvals or allows an immediate provider call. Missing prior-test continuity or incompatible effective prices at action time still means no send. Ordinary-use resource acceptance, test-mode exit and `FRA-14–16` remain separate open gates.

**PASS — model `gpt-6.1-sol`, default tier, route `POST https://api.openai.com/v1/responses`, exact commit `14ef51b916aed66423a8f37d61da0b13a0d3e049`, and the Task 71 packet in Task 69 §10. This pass covers this disabled commit and the packet only; it never covers an enabled test build, which requires its own review.**
