# Task 70C — Third AI security, privacy and cost review

**FAIL — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, commit `b11da9bc8056c4baacd89b9e5daa88b53879ea5d`, and Task 69 §10's current Task 71 packet.** Reviewed 2026-11-14.

The original T70B-01 compaction corruption and the five T70B-02 early-refusal cases now fail safely. One related malformed-metadata case remains: duplicate JSON containers can hide an earlier nonzero cache-write count from the review-block decision. The body/usage is untrusted and the reservation is retained, but another reservation is admitted without the promised automatic pause. This is **one Medium finding, T70C-01**, below. It is the reason for FAIL; the owner's B1/B2 choices and outstanding ordinary-use performance measurements are not being reopened as additional blockers.

This build is still disabled (`Reviewed=[]`, `PaidRequests=Disabled`). No enabled test build, deployment, key entry, paid call or ordinary-use capability is approved by this review.

## 1. Identity, inputs and authority

- Exact implementation target: **`b11da9bc8056c4baacd89b9e5daa88b53879ea5d`**, verified against fetched `origin/phase/4-beta` at entry. The new branch starts at that SHA, with no dirty product/test overlay. References below are to that tree; the later documentation commit is not a different implementation baseline.
- Read `AGENTS.md`, the Task 70 prompt, STATUS, `V2_PHASE_G.md`, `V2_TASK_CONTRACT.md`, accepted ADR-027/028/029 including ADR-028 A1, Task 67 plan, Task 69 including revised §§6/7/10, and Task 90 including §4. Read both owner-local earlier reports. Preserved report hashes: Task 70 **`C640DFA9F47BF63A2A8A4D683EA60376710D797A090EA528678B1A4C93F06396`**; Task 70B **`692D6258E9158498E6B6CA21BE97311A6A14D05C29FD0B74C285B5D731A619E3`**. Neither earlier report is modified or included in this PR.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05`, applicable `NFR-PERF-01–10`, and `TM-03/04/07/13`. Public-distribution gates `FRA-14–16` remain independent and open.
- Only this report and its STATUS entry change. No product code, committed tests, ADR, task packet or TRACEABILITY edit. The task's explicit two-file boundary controls over the generic traceability-update instruction; its explicit PR request supplies publication authority, not merge/release authority. No subagents or worktrees were used.
- No real credential, AI-provider request, paid request, account query, OS-record collection, installed-app change, deployment or release. Provider documentation was not contacted: Task 69's dated facts are input evidence, not a fresh provider check. GitHub repository operations and isolated local fixtures are separate from prohibited provider operations.

## 2. Finding

### T70C-01 — Medium: duplicate billing containers hide cache-write evidence from the pause rule

**Location:** [AdvisoryAiResponse.cs:260](https://github.com/DanDG923/pc-steward/blob/b11da9bc8056c4baacd89b9e5daa88b53879ea5d/src/PCSteward.Domain/AdvisoryAiResponse.cs#L260), specifically lines 120–129, 140–141 and 258–264; downstream `src/PCSteward.Domain/AdvisoryAiBudget.cs:414–419`. **Requirements:** `FR-AI-04`, `FR-BUD-02/03`; ADR-028 malformed/duplicate-field handling, ADR-029 unexpected billing-category review and Task 69 §10 item 6's automatic cache-write pause.

The strict reader rejects duplicate top-level properties. The separate `CacheWriteReport` then calls `TryGetProperty("usage")` and `TryGetProperty("input_tokens_details")`, which select the last occurrence. It checks duplicates inside the chosen containers only after locating a cache-write field there. It does not conservatively handle ambiguity in the containers that select the counter. An earlier nonzero report can therefore disappear behind a later zero, null or counter-free container.

**Concrete independently reproduced scenario:** the approved route returns a bounded JSON body containing two top-level `usage` objects. The first reports 12 cache-write tokens; the second reports zero. The inner reader returns `Malformed/DuplicateProperty`; the outer report returns `CacheWriteTokens=0`, `CacheWriteUnreadable=False`, and no above-bounds flag. Settlement keeps the full 12,000-micro-USD reservation as Uncertain, but `NeedsReview=False`. After applying that settlement through the real SQLite store, `ReviewBlock=False` and another fresh reservation returns `None` (admitted). There is no automatic retry or automatic second send: the defect is the missing interlock before a later explicitly approved request.

The exact synthetic body used was:

```json
{"object":"response","id":"resp_duplicate","model":"synthetic-review-model","status":"completed","output":[],"usage":{"input_tokens":50,"input_tokens_details":{"cached_tokens":0,"cache_write_tokens":12},"output_tokens":0,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":50},"usage":{"input_tokens":50,"input_tokens_details":{"cached_tokens":0,"cache_write_tokens":0},"output_tokens":0,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":50}}
```

Two further independent variants show the same selector problem: an earlier `usage` with a cache write followed by `usage:null`; and duplicate `input_tokens_details` with a write in the first and no cache-write field in the last. In the nested variant the answer status is not classified as malformed, but usage is unreliable and settlement is still Uncertain without review. No variant is trusted to reduce the reservation. This finding does **not** assert that OpenAI actually returned such a body or charged a cache write; it identifies the required hostile/malformed response behavior when billing metadata is ambiguous.

**Required remediation/evidence:** identify ambiguous billing containers before using a last-occurrence accessor, and preserve an unreadable/unexpected billing signal for review even when the selected last container omits or zeros the counter. Retain uncertainty and do not treat any ambiguous count as settlement evidence. Cover duplicate top-level `usage`, duplicate nested `input_tokens_details`, zero/missing/null/unreadable last values and both property orders; keep the existing malformed/refusal cases. The current fifteen early-refusal/write combinations exercise different reasons and do not cover this class.

**Gate consequence:** Task 71's manual one-request stop remains useful and limits exposure, but it does not make the software's “any reported cache write” pause claim true. The unexpected-category paid-control gap remains unresolved. No fix is made in this review.

## 3. Disposition of all nine earlier findings

| Earlier finding | Disposition at the pinned tree | Independently checked evidence and remaining limit |
|---|---|---|
| **T70-01 — detailed commitment inconsistency** | **Fixed.** | `SqliteAdvisoryAi.cs:291–306,455–482` validates held/released/uncertain states and settled charge against five usage counts at frozen rates. Seven detailed-row corruption fixtures, restart/refusal checks and the corrected compacted checks below passed. Coordinated internally consistent rewriting remains outside the accepted local continuity guarantee. |
| **T70-02 — B2/profile proof** | **Fixed for the scoped one-request policy; not measured ordinary-use support.** | The owner explicitly accepts B1(a) and B2 wide margins. The three `AdvisoryAiTask71CandidateTests` passed; independent allowances/cost calculations are in §5. This is a scoped policy choice, not tokenizer/framing measurement or preapproval of an enabled build. |
| **T70-03 — effective project price** | **Fixed under the accepted owner-confirmed tariff policy.** | `AdvisoryAiSetup.cs:212–230` refuses unacknowledged effective prices; desktop prices checkbox starts unticked (`MainWindow.AdvisoryAi.cs:363–378`). Packet requires a non-residency project and confirmation of rates actually paid by that key's project. No software/account API verification of the bill is claimed. |
| **T70-04 — unwired forward-clock guard** | **Fixed.** | `AdvisoryAiClockWatch.cs:22–54`, `AdvisoryAiRequestWorkflow.cs:123–130`, `App.xaml.cs:214–216`: wall/monotonic comparison, sticky pending jump, durable review block before reserve, and production composition. Three workflow clock cases passed; restarts retain the stored block, not the watch baseline. |
| **T70-05 — compaction, quota and physical resources** | **Partly fixed overall.** | Accounting/compaction/pruning/quota controls and independent upgrade/rollback checks pass (§4); T70B-01 is fixed. CPU/working-set/UI, idle I/O and backup-inclusive storage acceptance remain open for ordinary use. Their absence alone is not treated as blocking the bounded supervised one-request packet (§5). |
| **T70-06 — residual-secret disclosure** | **Fixed.** | `AdvisoryAiExplain.cs:190` and `MainWindow.AdvisoryAiExplain.cs:124,203`: best-effort warning on draft and final consent. Boundary fixture passed. Secret recognition is still best effort; exact owner inspection is required. |
| **T70-07 — pause/evidence claims** | **Partly fixed.** | Exact/missing/unreadable cache-write metadata is displayed with input/cached/output/reasoning/total and reliability, through a counts-only report (`AdvisoryAiExplain.cs:251–263`, desktop result line 270). Ordinary timeout is correctly described as retaining reservation without pausing. T70C-01 still qualifies the automatic unexpected-category pause. |
| **T70B-01 — compacted test spending** | **Fixed.** | Migration 15 now retains charge, test flag and five usage counts (`SqliteAdvisoryAi.cs:118–129`); every read validates charges against tariffs and totals/counts against compacted requests (`328–387`). Seven compacted-corruption fixtures passed. The earlier independent $0.99 reproduction now returns `LedgerNeedsReview`, with review shown and persisted rather than allowance released. |
| **T70B-02 — cache write in refused body** | **Partly fixed.** | Wrapper at `AdvisoryAiResponse.cs:120–129` now propagates a detected nonzero/unreadable write into the above-bounds flag. Five early-refusal reasons × write/unreadable/zero combinations pass; the original wrong-model reproduction now has `NeedsReview=True`. Duplicate selector ambiguity remains T70C-01. |

The remediation record is not used as proof in place of these source checks, passing fixtures and independent reproductions. Six earlier findings are fixed for this scoped gate; three remain partly fixed for the distinct reasons stated above. No earlier finding is silently dropped.

## 4. Compaction, migration and accounting audit

**Compaction transaction/proofs.** `RecoverInterruptedAsync` (`SqliteAdvisoryAi.cs:609–636`) uses an immediate write transaction, releases only unclaimed Reserved rows and marks claimed unfinished rows Uncertain. Compaction has a savepoint. It requires readable accounting, no review block and an accepted current period. Cutoff is strictly earlier than the current month minus 12: November 2025 is retained in detail in November 2026. Unresolved rows remain detailed regardless of age. SQL groups eligible Settled/NotSent rows by their original period, records compacted settled identities/charges/test flags/usage/tariff references, and deletes only eligible detail (`408–435`).

After compaction, the snapshot checks every compacted charge against usage at its tariff; recomputes each month's settled money, test money and settled-request count; rejects orphan compacted months, missing requests and mismatched totals; and includes cumulative test spend. Current-period totals, test commitments, in-flight state and all-time settled money are additionally conserved (`437–448`). Released-request counts have no monetary authority. An injected trigger adding one micro-dollar to a month's total caused rollback: **`Compacted=0`, detailed/month/identity/review = `1|0|0|1`**. The original detail remained, neither new compacted structure remained, and the review block persisted.

**Later read/recovery and dedup.** The original test settles $0.99 in an old month, compacts it, and correctly refuses another $0.045056 against the cumulative $1 envelope. Lowering only the monthly test total now produces an unreadable snapshot and a reservation refusal, not a refund. The seven store fixtures also cover lowered monthly total/count, deleted compacted request, changed charge/test flag and deleted month, with restart. Provider response-ID reuse checks both detailed and compacted tables; the existing duplicate-settlement fixture returns Conflict and review. No stored row is authority to send: fresh memory-only approval and a durable unique claim remain necessary.

**Tariffs.** `ConfirmTariffAsync:193–214` creates an immutable version and removes older same-model/tier prices only if referenced by neither detailed nor compacted requests. Thirty reconfirmations retain the historical referenced tariff plus newest tariff. Prices used by unresolved or compacted requests remain. Pruning does not alter spending. Effective prices are owner-confirmed, positive exact-model/default-tier rates with a half-open 24-hour validity period, not software-verified current provider prices.

**16 MiB ceiling.** `AdvisoryAiAccounting.cs:155–162` counts checked row estimates: detailed 1,024 B, tariff 512 B, compacted 512 B, monthly total 64 B. The expanded compacted representation still fits 512 B: 36-byte GUID + at most 256-byte normal provider ID + nine 8-byte integer values = **364 B** of bounded normal values. Metadata/page/index overhead is a separate physical limit. Snapshot at `SqliteAdvisoryAi.cs:389–392` admits only when another 1,024 B detailed row fits and the 10,000-detail ceiling has not been reached. At 32,766 compacted rows, one tariff and one month, arithmetic leaves **448 B**; admission refuses. Removing two identities with the corresponding count correction frees 1,024 B and permits one request. The boundary fixture passed with the new compacted columns. This is a logical soft ceiling; singletons and SQLite/WAL/index/backup overhead are not a 16 MiB physical promise. Reconfirmation/pruning is not itself the request-admission gate.

**Populated migration 14 → 15.** `SqliteDatabase.cs:86–95` creates 15 after 14 in the migration transaction, with schema validation and maximum known version 15. In an independent synthetic migration-14 database containing connected settings, continuity declaration, tariff, an uncertain 12,000-micro first-test liability and unrelated metadata, an injected abort at version-15 insertion left **`14|0|1|kept`** (version/new-table-count/connected/metadata). Removing the injected fault and retrying produced **`15|2|1|kept`** and preserved the liability as **`Uncertain|12000|1|1`** (state/commitment/test/tariff). The new columns are added to unreleased migration 15, as the source states. A developer database with the superseded experimental 15 shape is not demonstrated compatible; it must not be silently downgraded, reset or used for a future enabled test without its own reviewed recovery/baseline plan. This review proves the requested populated-14 path and rollback, not every external backup scenario.

**Reservation, settlement and continuity.** Checked micro-USD and upward rounding; exact/one-unit-over caps; zero disabling; prospective editable limits; settings/consent/key/tariff binding checks in reserve and claim transactions; no send before claim; no replay/refund after claim; original UTC period and frozen rates; reliable terminal-incomplete usage settlement independent of answer saving; duplicate/conflict handling; above-reservation review; and cumulative test spending across months remain covered by the focused fixtures. Clear scan/history/download/log controls do not refund accounting. Full reset intentionally removes local spending knowledge and key without an external marker. Prior-use declaration, orphan handling, unknown-test continuity refusal and the disclosed external-restore limitation remain prerequisites; a coordinated rewrite of mutually consistent values or undetected whole-file restore is not claimed detectable.

## 5. B1/B2, resources and the seven-part Task 71 packet

The owner's choices are preserved: **B1(a)** accepts the byte-level input upper-bound argument plus later live comparison; **B2 “Use wide safety margins”** selects 8,192 framing tokens, 192 bytes/output token and a 1,048,576-byte response envelope for one experiment. These are risk-policy values, not measurements. A single later usage count cannot isolate framing from visible-text byte-estimate slack. The profile remains outside `Reviewed` and the build remains disabled.

For the proposed 12,288-input/2,048-output limits, independent results are:

| Bound | Result |
|---|---:|
| Complete request allowance | 74,752 B |
| Complete response allowance | 3,481,600 B |
| Sanitized saved-item allowance | 3,932,672 B |
| Canonical evidence room after fixed instructions/framing | 3,399 B |
| Reservation at listed $2/$0.10/$10 per-million rates, full input ceiling | $0.045056 |
| Reservation at explicitly entered 10% higher rates | $0.049562 |

All three byte allowances fit their 4 MiB maxima and content remains capped at 8 MiB aggregate. Both monetary calculations fit $0.05 per request; the implementation reserves the actual preview upper bound, which can be smaller than the full input ceiling. A 10% premium is not silently assumed: the owner must confirm that project's effective rates. Low reasoning and the standard-price input threshold of 272,000 remain fixed in the candidate.

**Judgment of B2:** these margins are reasonable for the accepted short, supervised one-request experiment, subject to provider honoring the reviewed request controls. They are not a theorem about hidden framing or provider invoices and do not authorize ordinary use. I do not fail this review merely because the owner chose a scoped margin policy rather than waiting for measurements. T70C-01 is a separate unexpected-category control gap.

| Earlier report §7 requirement | Assessment of current Task 69 §10 packet |
|---|---|
| 1. Profile/B2/effective prices | Addressed for the scoped experiment: explicit accepted margins, current effective-project-price attestation and non-residency project. No silent measurement claim. |
| 2. Pinned enabled artifact and review | Correctly a future prerequisite: source SHA, artifact SHA-256/manifest, SDK closure and exact FirstLiveTest profile/policy must be recorded and independently reviewed before approvals. A pass for this disabled tree could never cover that new artifact. |
| 3. Synthetic preparation and exact preview | Explicit one retained item with the specified Contoso text and final inspection. The packet discloses real selected-record interval/omission metadata; it is synthetic text plus previewed real metadata, not a wholly artificial snapshot. Cancel if any unapproved content remains. No OS collection is authorized by this review. |
| 4. Separate key/send authority and prior spend | Addressed procedurally, and compacted checks now work. Both displayed test commitments must be zero; the owner must separately account for prior/reset/restored/uncertain use the current ledger might not know about. No-prior-use declaration or reset cannot grant a new $1 test authority. |
| 5. Sanitized evidence capture | Counts/cost/reliability/cache-write absence versus zero are available without a log sink. Record no body, body hash, header/key, provider error or answer text. Provider dashboard is outside corroboration, not invoice finality or local continuity proof. |
| 6. One request, stop and retry | Ordinary timeout/uncertainty correctly retains reservation without claiming automatic pause; the owner must set request cap to $0 after one attempt and report. **Partly addressed:** automatic cache-write pause still misses ambiguous duplicate containers (T70C-01). No retry is authorized. |
| 7. No promotion | Explicit: no success, rollover, reconnect or cap edit ends FirstLiveTest. Ordinary use requires a separate decision, artifact and remaining measurements. |

**Open resources:** CPU/process-tree peak working set, UI responsiveness, full read/sanitize/save/display timings, idle I/O and backup-inclusive physical storage remain unmeasured acceptance work for **ordinary use**. Task 90's Linux accounting-size result is historical input, not a new Windows performance result. Even 2,048 output tokens permit a 3.32 MiB response envelope under the wide profile; token count alone is not a small-memory proof. The enforced one-attempt byte/deadline bounds make a separately reviewed and supervised single synthetic test a proportionate experiment; absent large-answer measurements alone need not block it. They remain open, and the enabled artifact still needs its own host/readiness review. Optional accessibility evidence is not a failure reason.

## 6. Egress, credential and advisory boundary checks

| Area | What the pinned software/fixtures prove | What remains provider/host evidence |
|---|---|---|
| Route/payload/headers | OpenAI-only compiled registry; exact POST `https://api.openai.com:443/v1/responses`. Canonical model/instructions/input/max_output_tokens/store:false/truncation:disabled/default-tier, low reasoning and explicit no-cache without breakpoint. Body equality before dispatch. Closed Bearer authorization (placeholder in preview), JSON Accept/Content-Type, fixed `PCSteward-AdvisoryAI/1` User-Agent, exact length and standard Host/framing. | No actual DNS/TLS/proxy/socket capture here. Provider behavior, retention and invoice correctness are not proved by mocks or `store:false`; Connect discloses abuse-monitoring retention. |
| Transport | Official OpenAI 2.15.0/System.ClientModel 1.16.0 protocol path, zero retries, first-attempt guard, redirect interception before SDK handling, standard certificate validation, no cookies/default credentials/decompression, logging/tracing/Activity propagation disabled; 10 s connect and 45 s attempt including response read; bounded success/error bodies. No tools/conversation/history/background/streaming/uploads/images/audio/counting/account call or additional provider. | No real network/deadline/provider observation. The attempt deadline does not cover all database, sanitizer or UI work. Byte rejection keeps uncertainty, not proof of a useful full answer or non-billing. |
| Redaction/approval/isolation | Incident+coverage/nearby bounded evidence or only selected failed job; edits re-redacted; explicit residual-secret warning; immutable preview bound to bytes/profile/draft/limits and settings/consent/key/tariff generations. Output is redacted plain TextBlock advice with no executable/link affordance or effect on matching/jobs/correlation/helper. ScheduledHost/Execution/helper have no AI adapter capability. | Redaction cannot recognize every secret and instructions cannot prevent all misleading model advice. Exact owner review remains required. |
| Credentials | Exact current-user generic `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence, printable ASCII <=2,048 bytes, save readback, exact removal/fresh absence verification; no enumeration/environment fallback/config/SQLite/export/log key sink. Masked box cleared on close, mutable buffers cleared where implemented. | Fake native API fixtures do not prove real Windows key storage/removal/logon behavior; immutable SDK/PasswordBox strings and OS memory are not securely erasable. No real key operation occurred. |
| Disconnect/reset/recovery | Revoke/invalidate first, exact verified removal, preserved accounting/content, visible incomplete removal. Reset deletes credential before database and rechecks every recovery/onboarding/finalization path independent of database; old marker category pending, residual blocks completion, ordinary startup does not probe credentials. | No provider revocation or real reset is claimed. Removal-incomplete UI state is session-only; disconnected is not a claim of verified key absence after restart. |
| Saved content/lifecycle | 30-day startup/list/save expiry, source-specific scan/history clears, no cascading ledger refund, 4 MiB item/8 MiB aggregate content rules, no submitted prompt/body/hash/raw response/reasoning persistence. Reliable usage can settle an incomplete/empty/refused answer independently of saving. | Logical deletion is not forensic erasure of free pages/WAL/external backups. Comprehensive backup/host/resource proof remains open. T70C-01 qualifies cache-write metadata handling. |

Inspected the named Domain/Application AI files, Windows adapter, SQLite AI schema/migration/compaction, desktop composition/dialogs and matching fixtures, including the candidate, ledger, response, adapter, setup/explain, reset/gate and architecture cases. This is software and synthetic-fixture evidence; no live provider or owner-data result is inferred.

## 7. Fresh verification results

Native Windows x64; installed SDK **10.0.404** satisfies `global.json` 10.0.403/latestPatch; PowerShell 7; Release configuration. Tests ran outside the known sandbox testhost-connection limitation. Existing pinned assets were used; no package/SDK version, warning policy, test filter policy or product code was changed to obtain a pass. Temporary review console fixtures use isolated databases and no credential or transport.

| Command | Actual result |
|---|---|
| `git fetch origin`; identity checks | Exact requested local tree and fetched branch head `b11da9bc8056c4baacd89b9e5daa88b53879ea5d` at entry. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **173 passed**, no failures/skips. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~SqliteDatabaseTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0; **212 passed**, no failures/skips. Actual PowerShell filter has literal pipes; backslashes here escape the Markdown table. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **16 passed**, no failures/skips. |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0; **0 warnings/errors**, 11.25 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | Exit 0: **4,272 passed, 4 skipped, 0 failed** (4,276 total). Domain 468; Architecture 270; Application 1,719 passed/1 skipped; Security 1,815 passed/3 skipped. |
| Independent offline console referencing this exact tree | Exit 0; original 70B reproductions, upgrade/rollback/savepoint and duplicate-metadata matrix below. One temporary fixture formatting analyzer error (CA1305) was corrected with invariant formatting before the successful run; no analyzer was disabled. |

Selected outputs:

```text
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
candidate-request=74752; response=3481600; saved=3932672; evidence=3399
mismatched-cache-write=12; malformed=ModelMismatch; state=Uncertain; needs-review=True
duplicate-usage-cache=0; unreadable=False; malformed=DuplicateProperty; state=Uncertain; needs-review=False
duplicate-usage-stored-review=False
duplicate-usage-next-reservation=None
ambiguous-cache=absent; unreadable=False; malformed=DuplicateProperty; needs-review=False
ambiguous-cache=absent; unreadable=False; malformed=None; needs-review=False
```

The unreadable-summary fallback's zero is accompanied by `ReviewBlock=True` in the corrected compaction case; it is not usable allowance. The new duplicate-metadata reproduction instead has a valid readable ledger, an Uncertain row with retained reservation, no review block and another admission. Reproductions were performed only in new synthetic databases, not the installed app's state.

## 8. Handoff

This is a failed gate, not an implementation instruction. An explicitly requested Task 90 should address T70C-01 across the duplicate-container class, followed by another independent review of a new immutable snapshot. Preserve the accepted B1/B2 policy and all verified fixes. Keep the model registry empty and paid requests disabled. Do not start Task 71, enter a key, call a provider, enable a test build or promote ordinary use from this report. Any enabled test artifact requires its own exact review and the packet's separate action-time approvals; `FRA-14–16` remain open independently.

**FAIL — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, commit `b11da9bc8056c4baacd89b9e5daa88b53879ea5d`, and Task 69 §10's Task 71 packet.**
