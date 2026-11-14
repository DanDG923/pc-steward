# Task 70B — Fresh AI security, privacy and cost review after Task 90

**FAIL — review of `669c8c7e1e4e3e7489fe01b254f9b0cbd0b49aa9` on `phase/4-beta`, for `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, and Task 69 §10's revised Task 71 packet.** Date: 2026-11-14.

Task 90 fixes the original detailed-row corruption, adds the clock guard, project-price attestation and consent warning, and improves the test packet and usage display. It also adds functioning transactional compaction. Independent checks found a remaining compacted-accounting continuity gap and a cache-write pause gap. Neither is resolved by the disabled build. Reviewed models remain empty and paid requests remain `Disabled`; no enabled test artifact is reviewed or approved.

## 1. Scope, prerequisites and authority

- Exact reviewed tree: `669c8c7e1e4e3e7489fe01b254f9b0cbd0b49aa9`. GitHub `origin/phase/4-beta` matched it at entry. The fresh review branch starts at that SHA and initially had no tracked changes. No other implementation commit is the review target.
- Read the repository instructions and Task 70 prompt, STATUS, Phase G/shared contract, ADR-027/028/029 including A1, Task 67 plan, Task 69 report including revised §§6/7/10, and Task 90 remediation record. The earlier owner-local Task 70 report was read and preserved; its seven findings and §7's seven packet requirements are the comparison baseline. Its original bytes have SHA-256 `C640DFA9F47BF63A2A8A4D683EA60376710D797A090EA528678B1A4C93F06396`. Prior uncommitted report/STATUS work was preserved in a named Git stash before switching branches, with a local report copy retained for this review.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05`, applicable `NFR-PERF-01–10`, and `TM-03/04/07/13`. Public distribution `FRA-14–16` remains separate and unpassed.
- Only the new report and its STATUS entry are task changes. Product code, committed tests, TRACEABILITY, accepted decisions and the earlier report are unchanged. No subagents or worktrees were used. Temporary ignored synthetic review fixtures are verification artifacts, not product changes.
- No real key, Credential Manager access, AI-provider network request, paid call, provider-account query, OS-record collection, installed-app change, deployment or release occurred. GitHub repository operations are separate from prohibited provider calls. Task 69's dated provider-documentation claims were reviewed as inputs, not refreshed by contacting an AI provider.
- B1(a) and B2 **“Use wide safety margins”** are accepted owner choices. B2 is a scoped risk policy for one experiment, not a measurement or an ordinary-use waiver. This review does not reopen either choice.

## 2. Prior findings — independent disposition

All file/line references below are in the exact reviewed tree.

| Earlier finding | Disposition | Independent source/fixture evidence and limit |
|---|---|---|
| **T70-01: inconsistent detailed commitment** | **Partly fixed overall; original detailed-row scenario fixed.** | `SqliteAdvisoryAi.cs:281–297,409–436` now checks state-dependent commitment/reservation/usage and settled cost at frozen rates. `ReserveAsync:244–247` persists a review block on unreadable detail. The seven `ALedgerRowThatDoesNotAddUpStopsPaidRequestsForReview` cases cover lowered uncertain/test/settled/claimed charges and missing usage, plus restart and attempted acknowledgement. All passed. New compacted totals lack equivalent binding: **T70B-01** below. |
| **T70-02: B2/coupled profile** | **Fixed for the scoped one-request packet by the new owner policy and verified arithmetic; not measured model support.** | Task 69 §§6–7 and `AdvisoryAiTask71CandidateTests` use framing 8,192, 192 bytes/output token and 1 MiB envelope. The three candidate fixtures passed. Independent arithmetic and effective evidence space are below. Neither the Boolean coherence check nor a future single usage observation proves a universal tokenizer/framing profile. Ordinary use and an enabled artifact still need their own gates. |
| **T70-03: effective project prices** | **Fixed under the accepted owner-confirmed-pricing policy.** | `AdvisoryAiSetup.cs:207–225` refuses an unticked effective-price statement; `MainWindow.AdvisoryAi.cs:362–378` starts the checkbox unticked and passes it into validation. Task 69 §10 additionally requires a non-residency project. Setup/architecture fixtures passed. Attestation does not verify the provider's billing configuration; exact effective rates remain an action-time owner prerequisite. |
| **T70-04: unwired forward-clock guard** | **Fixed.** | `AdvisoryAiClockWatch.cs:22–54` compares wall and monotonic time and retains a pending jump until recorded. `AdvisoryAiRequestWorkflow.cs:123–130` persists the block before reservation; `App.xaml.cs:214–216` supplies `ForSystem()`. Three integration cases exercise the workflow. Restart does not preserve the watch baseline, but the stored review block survives and monthly-boundary confirmation remains required. |
| **T70-05: compaction/quota/resources** | **Partly fixed.** | Migration 15, compaction/savepoint proof, tariff pruning and conservative row-size admission exist and execute. Positive and fault-injected checks below independently verify them. **T70B-01** qualifies compacted continuity. CPU, working set, UI, idle I/O and backup-inclusive physical storage remain open for ordinary use; they are not automatically blockers for one bounded supervised experiment. |
| **T70-06: residual-secret disclosure** | **Fixed.** | `AdvisoryAiExplain.cs:190` supplies explicit best-effort/password/key/personal-detail wording. Both draft and exact-request screens show it at `MainWindow.AdvisoryAiExplain.cs:124,203`. The matching architecture fixture passed. The redactor still cannot recognize every secret; exact owner inspection remains essential. |
| **T70-07: pause/evidence mismatch** | **Partly fixed.** | `AdvisoryAiResponse.cs:117–122,249–256` preserves exact/missing/unreadable cache-write metadata. `AdvisoryAiRequestWorkflow.cs:192–202` forwards a counts-only report; `AdvisoryAiExplain.cs:251–263` and the desktop result screen at line 270 display it without a log sink. Task 69 §10 correctly distinguishes ordinary uncertainty from automatic pause and requires a manual stop/$0 setting. Its “any cache write” automatic-pause claim still has the malformed-response exception in **T70B-02**. |

## 3. Findings requiring remediation

### T70B-01 — High: compacted test spending can be lowered without a continuity block

**Location:** `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs:108–119,318–345,376–395`. **Requirements:** `FR-BUD-02/03`, `NFR-REL-02`, `TM-07`; ADR-029 retained cumulative test accounting, untrusted reads and compaction evidence.

Migration 15 retains only request ID, period, tariff version and optional provider response ID per compacted request. It discards that request's test-mode flag, charge and usage. `SnapshotAsync` subsequently validates monthly amounts only as non-negative, with `test_settled <= settled`; it cannot recompute either amount or test attribution from the retained request records. It also does not reconcile the stored settled-request count to the compacted identities. Compaction's before/after checks demonstrate conservation during that transaction, not validity of those totals on later reads.

**Concrete independently reproduced scenario:** create a coherent synthetic first-test request in October 2024, reserve $1, claim it and settle reliable usage at **$0.99**. Confirm the current November 2026 period and prices, then call recovery. It compacts exactly one request and correctly reports 990,000 micro-USD of cumulative test spend. Another 45,056-micro test reservation correctly returns `AboveFirstLiveTestEnvelope`. Now change **only** `ai_monthly_totals.test_settled` to zero, without disabling CHECK constraints, removing the retained request identity, altering its tariff or restoring settings. The summary reports test spend zero and `ReviewBlock=False`; the same reservation is admitted. Known test use plus that reservation is **$1.035056**.

This is a corrupt-row/read-validation gap in the new accounting representation. It is not a claim that PC Steward can defeat a compromised Windows user or detect every internally consistent whole-file external rollback. The material regression is that compaction removes the charge/test bindings that supported the detailed-row check, while continuing to use the surviving total to grant allowance. The zero displayed by Settings can therefore also satisfy the revised packet's “both test figures are $0.00” check without proving continuity.

**Required correction/evidence:** retain sufficient compacted charge/test/dedup bindings to validate totals and counts before using them for admission, or fail closed for continuity review when those bindings cannot be established. Exercise lowered totals and test attribution, missing/partial identities and restart after compaction. Keep the accepted external-restore limitation explicit; no hidden reset counter or account query is requested. Do not restore allowance by silently treating uncertain/missing evidence as zero.

### T70B-02 — Medium: an early-malformed response can report a cache write without pausing paid use

**Location:** `src/PCSteward.Domain/AdvisoryAiResponse.cs:120–122,132–141,249–256`; `src/PCSteward.Domain/AdvisoryAiBudget.cs:414–419`. **Requirements:** `FR-AI-04`, `FR-BUD-02/03`; Task 69 §10's unexpected-cache stop policy.

The outer response reader captures cache-write metadata even when the inner strict reader returns early for a model mismatch, invalid ID, stored response or another early shape failure. However, it only copies `CacheWriteTokens`/`CacheWriteUnreadable`; it does not carry that observation into `UsageAboveApprovedBounds`. Settlement checks the latter flag, not the new cache-write report.

**Concrete independently reproduced scenario:** a bounded JSON response has a different model and otherwise well-formed usage reporting `cache_write_tokens:12`. The result is `Malformed/ModelMismatch`, with `CacheWriteTokens=12`, but settlement is `Uncertain` with `NeedsReview=False`. The whole reservation is retained and no answer is shown, which is correct. The accounting state is not automatically paused for this observed unexpected category, contrary to the revised packet's “any cache write” statement; fresh requests remain technically possible within remaining caps.

**Required correction/evidence:** make the explicit cache-write/unreadable-category signal conservatively trigger review even when the response is rejected earlier, without trusting its usage to lower a charge. Add fixtures combining unexpected billing categories with each early-malformed result. Keep the one-request manual stop regardless; do not claim every malformed body triggers an automatic pause.

## 4. Compaction, pruning, quota and migration proofs

**Transaction and rollback:** `RecoverInterruptedAsync` opens one immediate transaction, marks unclaimed requests NotSent and claimed requests Uncertain, validates the ledger, then creates a compaction savepoint (`SqliteAdvisoryAi.cs:563–590`). Compaction requires readable accounting, no review block and an accepted current month. The cutoff is strictly before the current month minus 12: in November 2026, November 2025 stays detailed; October 2025 and earlier settled/released detail is eligible. Unresolved rows stay detailed. SQL groups eligible rows by their original period, records settled IDs/tariff references, then deletes only settled/NotSent detail. Current-period commitments, cumulative test totals, in-flight state and all-time settled money are compared before release; this does not establish each historical total's later integrity.

I independently injected an AFTER INSERT trigger that adds one micro-dollar to a newly compacted month's settled amount. The conservation check refused the result; rollback retained the original detailed row, removed both new compacted structures and stored the review block. Observed result: `Compacted=0`, detailed/month/identity/review counts **`1|0|0|1`**. No partial transfer or released liability was observed. This verifies the savepoint failure path, not T70B-01's later-read guarantee. Fault injection occurred only in an isolated synthetic DB.

**Dedup:** `ResponseIdUsedAsync` queries both detailed and compacted tables (`SqliteAdvisoryAi.cs:736–740`); the passing compaction fixture reuses an old compacted response ID and gets Conflict plus a review block. Tariff versions remain referenced for disputes. The original amount/test attribution is not retained alongside that ID, which is the limit above. Replay from a restored database is not authorized by any row: fresh in-memory approval and a unique claim are still required.

**Tariff pruning:** each new confirmation is inserted in a write transaction, then older prices for the same model/tier are deleted only when referenced by neither detailed nor compacted requests (`SqliteAdvisoryAi.cs:191–203`). The 30-confirmation fixture retains the referenced historical tariff plus newest tariff, and passes. This prevents daily reconfirmation alone growing an unlimited chain for the one supported model/tier. A price used by an unresolved or compacted request is preserved. Pruning is not permission to delete commitments.

**16 MiB arithmetic:** `IAdvisoryAiLedger.LogicalBytes` uses checked multiplication/addition of request 1,024 B, tariff 512 B, compacted identity 512 B and month 64 B estimates (`AdvisoryAiAccounting.cs:155–162`). `SnapshotAsync:342–346` also reserves room for the next request row and blocks at 10,000 detailed rows. The source-generated normal row strings are bounded ASCII IDs/enums/profile names/timestamps; their value lengths and integer fields fit those rounded estimates. These are logical value estimates, not SQLite page/index/WAL/backup limits. At 32,766 compacted IDs, one tariff and one month, independent arithmetic leaves **448 B**, so another 1,024 B request is blocked; the existing fixture also proves that boundary and a later admission after 1,024 B is freed. Singletons and physical database overhead are not included in the formula; admission's headroom is not a physical storage guarantee. Price confirmation itself is not constrained by the request-admission ceiling, though pruning normally bounds unused versions.

**Migration 14 to 15:** migration 15 executes after 14 in the existing migration transaction (`SqliteDatabase.cs:86–95`); the maximum recognized schema version is 15. I reconstructed the unchanged migration-14 table shape in an isolated DB, with connected settings, prior-use declaration, an immutable tariff, a 12,000-micro uncertain first-test liability and unrelated metadata. An injected abort on inserting schema version 15 left **version 14, neither new table, connected=1 and metadata preserved**. Removing the injected fault and retrying produced **version 15, both tables, connected=1 and metadata preserved**. The liability remained **`Uncertain|12000|1|1`** (state/commitment/test/tariff). No old key, prompt or approval was created by this test. Existing store tests mainly create fresh schema; this review's explicit populated-14/rollback check is separate evidence, not claimed coverage of every corrupt/partial backup-recovery shape.

## 5. B1/B2, cost and physical resources

The owner-authorized candidate has context 1,050,000, maximum output 128,000, `low` reasoning, `default` tier, standard-price input threshold 272,000 and explicit no-cache mode. B1(a) accepts the byte-level upper-bound argument and later live check. B2 increases the framing allowance 16-fold to **8,192 tokens**, output-token byte allowance to **192**, and envelope to **1,048,576 B**. These are accepted safety margins for one short synthetic experiment, not measured facts about this model.

For the packet's **12,288 input / 2,048 output**, the independent results are:

| Quantity | Result | Meaning |
|---|---:|---|
| Derived complete-request allowance | 74,752 B | Below 4 MiB; final bytes must still fit. |
| Derived complete-response allowance | 3,481,600 B | Below 4 MiB; transport and parser enforce it. |
| Derived sanitized saved-item allowance | 3,932,672 B | Below 4 MiB; aggregate saved content still 8 MiB. |
| Evidence space after framing/fixed instructions | 3,399 B | Ample for the single edited synthetic text and small canonical envelope. |
| Reservation at listed Standard rates, full input ceiling | 45,056 micro-USD ($0.045056) | Below the $0.05 cap; implementation uses the actual preview upper bound, which can be lower. |
| Reservation at explicitly entered 10% higher rates | 49,562 micro-USD ($0.049562) | Also below cap; this does not silently apply an unentered uplift. |

**Assessment:** the B2 margins and small evidence shape are reasonable for the owner's explicitly scoped **one-request** experiment, subject to the stated provider assumptions and separate enabled-artifact review. They are not a strict provider-invoice theorem. A future observed count below the bound still cannot isolate hidden framing from byte-estimate slack. I do not repeat T70-02 as a blocker merely because the owner chose a risk policy instead of waiting for measurement. The failures here concern separate continuity and stop-policy defects.

The transport's strict byte/read caps and 45-second attempt deadline limit one response, including errors, but do not bound total UI/sanitizer time or OS memory copies. Even 2,048 output tokens permit a **3.32 MiB wire envelope** under the candidate; “2,048 tokens” is not itself a small-memory proof. Task 90's Linux 10,000-row accounting measurement is dated evidence; no Windows backup-inclusive storage or CPU/working-set/UI result is inferred from it. Remaining large-response CPU/peak-working-set/UI/cancellation, idle I/O, backup/sidecar and complete quota-saturation measurements block **ordinary-use acceptance**. On their own, they need not block a single supervised, bounded synthetic test with explicit stop/failure handling and an enabled build's own host-readiness review. They remain open, not waived or marked passing. Optional accessibility evidence is not used to fail this gate.

## 6. Egress, credentials, advisory isolation and settlement

| Boundary | Source-backed software result at this SHA | Provider/host limit |
|---|---|---|
| Exact egress | OpenAI-only registry at `AdvisoryAiProviders.cs:61–75`: POST to compiled `https://api.openai.com:443/v1/responses`, no query/custom route/organization/project routing. Exact body comparison before send; provider adapter pair must match model. | No actual DNS/TLS/proxy/wire capture or provider request here. System certificate/proxy trust is a host boundary. |
| Body/headers | Canonical `model`, fixed instructions, redacted editable draft JSON as `input`, `max_output_tokens`, `store:false`, `truncation:"disabled"`, default tier, low reasoning, explicit no-cache mode without breakpoint. Guard closes headers to Bearer authorization (placeholder in preview), JSON Accept/Content-Type, fixed app User-Agent, body length and standard Host/framing. | Documented provider enforcement/retention/prices are assumptions from dated evidence; `store:false` is correctly disclosed as no ZDR guarantee. |
| No auxiliary capability | No tools, conversation/history, streaming/background, uploads/images/audio, extra counting request, cache key, SDK automatic retry or redirect. Handler/SDK logging/tracing and runtime Activity propagation disabled; 3xx intercepted before SDK response handling. 10 s connect / 45 s attempt with bounded error bodies; no decompression requested. | Byte rejection preserves uncertainty; it does not prove provider charging or a complete useful answer. TLS/SDK runtime behavior beyond counting mocks needs exact host evidence. |
| Preview/redaction | Incident+coverage then nearby changes/records, or only selected failed-update job facts. Edits are redacted again. The new residual-secret banner is on both consent screens. Approval binds exact body/profile/draft/limits/settings/consent/key/tariff generations. | Best effort; the owner must inspect the actual body and disclose/approve any real metadata in the packet. No complete anonymity claim. |
| Credential lifecycle | Compiled current-user generic `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence, printable ASCII <=2,048 bytes, exact read/write/delete/absence verification. Masked key box cleared on close; no environment/config/SQLite/log/export key sink. Deliberate first connection handles orphan/unknown presence; replacement invalidates approvals without refund. | Tests use fake Credential Manager calls. Actual Windows storage/removal/logon behavior needs separately authorized host proof. Immutable managed strings/OS memory are not securely erasable. |
| Disconnect/reset/recovery | Disconnect durably revokes first, removes/verifies key and preserves content/accounting. Reset uses a DB-independent compiled key-removal callback, category compatibility and every recovery/onboarding recheck; incomplete removal blocks completion. No ordinary startup key probe or remote revocation. | Full reset intentionally erases local spending knowledge and key; next setup uses an explicit declaration. Undetectable external restore is disclosed. Real reset/key deletion was not performed. |
| Isolation/content | Answers are redacted plain TextBlocks with advice-only labels, no executable/link/tool affordance, no change to eligibility/jobs/helper/correlation. ScheduledHost/Execution/helper lack AI capability. Source-specific scan/history clears and 30-day expiry remain separate from accounting. | Logical deletion is not forensic erasure of SQLite/WAL/external backups. Backup lifecycle and comprehensive hostile-resource/host UI evidence remain open. |
| Accounting | Immediate transaction reserve, binding recheck, key read, unique durable claim, one send; claimed crash retains uncertainty with no replay. Complete reliable terminal usage settles incomplete/empty/refused answers independently of saving; checked frozen rates, rounding, exact duplicate/conflict handling and new month/test continuity rules remain. | T70B-01 limits compacted accounting trust; T70B-02 limits automatic cache-write pause. No account query, assumed credits, purchase or recharge path. |

Focused adapter, setup, explain, response, redaction, policy, budget, ledger, reset/gate and architecture fixtures passed. These prove their mocked/synthetic cases, not live provider retention, bill correctness, key validity or performance on this PC.

## 7. Revised Task 71 packet versus earlier report §7

| Earlier required item | Revised packet assessment |
|---|---|
| 1. Resolve B2/profile and effective project price | **Addressed for scoped test policy.** Explicit wide margins, no-residency project and effective-price attestation replace the earlier circular measurement gate. Defaults remain unavailable; no ordinary-use acceptance. |
| 2. Pin and independently review enabled build | **Correctly retained as a future prerequisite.** SHA, artifact SHA-256/manifest, SDK closure and exact FirstLiveTest policy must exist and be reviewed before approval. A pass for a disabled commit can never cover that later artifact. |
| 3. Synthetic-only preparation and exact payload | **Materially clarified.** Keep one item and replace it with the exact Contoso text; inspect final body. The packet expressly acknowledges real selected-record interval/omission metadata. This is synthetic text plus explicitly previewed real metadata, not a wholly fabricated diagnostic snapshot. If metadata is unacceptable, cancel; no new OS collection is authorized here. |
| 4. Separate key/send approval and prior commitments | **Procedurally clearer, continuity proof incomplete.** Separate masked-key authority, owner-confirmed rates and both zero test figures are required. T70B-01 means displayed zeros cannot by themselves clear compacted continuity. The owner must account for prior/reset/restored/uncertain test use outside ordinary settings; a reset or no-prior-use declaration is not new test authority. |
| 5. Sanitized usage/cost capture | **Addressed.** Outcome shows counts, optional/malformed cache write, reliability and cost/uncertainty, with no prompt/header/hash/raw-response log. The report must retain absence versus zero and incomplete answer versus incomplete accounting. No answer text is to be recorded in the test evidence. |
| 6. One request and stop | **Partly addressed.** Ordinary uncertainty is correctly described; the owner must set request cap to $0 after exactly one attempt and cannot retry without a new task. T70B-02 leaves “any cache write automatically pauses” inaccurate on early-malformed output. The manual stop remains mandatory for every outcome. |
| 7. No ordinary-use promotion | **Addressed.** Success, month change, reconnect or setting change does not end FirstLiveTest. Remaining host/performance gates and a separate owner decision are explicit. |

The packet is substantially improved but **not accepted as a passing gate** while the continuity and unexpected-category gaps remain. Neither an enabled build nor action-time key/call approval exists. Nothing in this review starts Task 71.

## 8. Commands, actual results and evidence limits

Native Windows x64; installed .NET SDK 10.0.404 satisfies `global.json` 10.0.403/latestPatch; PowerShell 7. Existing pinned dependency assets were usable. No SDK/package version, test policy or product source was modified to obtain these results. Tests ran outside the known sandbox VSTest-connection limitation and used synthetic roots/records and fake transport/credential APIs.

| Command | Result |
|---|---|
| `git fetch origin`; `git rev-parse HEAD origin/phase/4-beta` | Both baseline identities `669c8c7e1e4e3e7489fe01b254f9b0cbd0b49aa9` at entry. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **168 passed**, no failures/skips, including three candidate fixtures. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~SqliteDatabaseTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0; **205 passed**, no failures/skips. Actual PowerShell filter uses literal pipe characters; backslashes here escape the Markdown table only. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0; **16 passed**, no failures/skips. |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0; **0 warnings/errors**, 12.44 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | **Exit 0; 4,260 passed, 0 failed, 4 skipped; 4,264 total.** Domain 463; Architecture 270; Integration 1,712 passed/1 skipped; Security 1,815 passed/3 skipped. |
| Offline review console, referencing this tree's Domain/Application/SQLite projects | Exit 0; no credential or transport constructed; independent outputs below. |

Full-suite skips were `TheRunnersOwnTokenWithoutASplitTokenIsNoSplitToken`, `SymbolicLinkLeafIsRefusedEvenWhenTheProtectionCheckIsBypassed`, `TheProtectedRunFolderIsFreshAdministratorOnlyAndRemoved`, and the Windows PowerShell row of `RedeployToolingFunctionsHoldTheirContracts`. They are not claimed as host proof or silently converted to passes. They do not erase the independent AI findings or authorize privileged host steps. The four full-suite totals above are results of this fresh run, not Task 90's cloud results or the earlier Task 70 run.

### Independent synthetic outputs

```text
historic-reserve=None
compacted=1
intact-test-settled=990000
intact-next-test=AboveFirstLiveTestEnvelope
lowered-test-settled=0; review-block=False
lowered-next-test=None
compacted-identities=1
migration15-injected-failure=refused
migration15-rollback=14|0|1|kept
migration15-retry=15|2|1|kept
migration15-preserved-ledger=Uncertain|12000|1|1
compaction-fault-compacted=0
compaction-fault-rollback=1|0|0|1
logical-near-ceiling-room=448
candidate-request=74752; response=3481600; saved=3932672; evidence=3399
mismatched-cache-write=12; malformed=ModelMismatch; state=Uncertain; needs-review=False
```

Reproduction of T70B-01 uses public store methods with a coherent synthetic profile and rates $2/$0.10/$10 per million. October 2024 request: input bound 495,000, output ceiling 1,000, reservation 1,000,000 micro-USD, complete reported input 495,000/output 0, settled 990,000, FirstLiveTest true. After confirming November 2026 and recovering, issue a current test reservation of 45,056 micro-USD. Inject only `UPDATE ai_monthly_totals SET test_settled=0;` in that isolated database and repeat admission. T70B-02 uses an in-memory JSON response with model `different-model`, valid-shaped usage and cache-write count 12; no HTTP request is involved. Migration and savepoint faults are isolated SQL triggers, never modifications of owner data.

## 9. Handoff

Keep the compiled model registry empty and paid requests disabled. An explicitly requested Task 90 should address the findings, followed by a fresh independent Task 70 on another immutable snapshot. Preserve the accepted B1/B2 choices and the packet improvements; do not enable a model or start Task 71 from this failed review. CPU/working-set/UI and backup-inclusive physical-resource acceptance remain ordinary-use work, not measurements produced here. `FRA-14–16`, deployment, key entry and a live call remain separately gated.

**FAIL — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, commit `669c8c7e1e4e3e7489fe01b254f9b0cbd0b49aa9`, and Task 69 §10's revised Task 71 packet.**
