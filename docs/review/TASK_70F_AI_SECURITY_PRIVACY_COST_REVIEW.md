# Task 70F — Sixth AI security, privacy and cost review

**PASS — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, exact commit `bb3c049d49831abed1f126e51413298e5b69e2f5`, and Task 69 §10's Task 71 packet.** Reviewed 2026-11-14.

This pass covers **this disabled commit and the packet only**. `Reviewed` remains empty; paid requests remain `Disabled`. It never covers an enabled test build, which requires its own exact independent review before separate action-time key/send approvals. No key, provider call, paid request, deployment, release, ordinary use or test-mode exit is authorized here.

All four 70E reproductions now fail safely or preserve the intended evidence, and the earlier accounting/cache-write fixes remain effective. No unresolved privacy, paid-bound or continuity finding was established within the accepted one-request policy. There is **one Low documentation finding, T70F-01**, about N2's automatic-pause wording. The packet's explicit one-attempt, $0-cap, stop-and-report procedure already covers the reproduced outcome; this pass does not rely on every malformed reply automatically pausing. Ordinary-use physical-resource evidence remains open.

## 1. Scope, identity and input evidence

- Fetched `phase/4-beta`; remote head and clean implementation checkout matched **`bb3c049d49831abed1f126e51413298e5b69e2f5`** at entry, and the remote head matched again before publication. Review branch: `codex/task-70f-ai-security-review`. This is the sole implementation target; a later two-file documentation commit is not a different implementation under review.
- Read AGENTS, STATUS, Task 70 prompt, Phase G/shared contract, ADR-027/028/029 including A1/A2, the Task 67 plan, Task 69 including §§6/7/10, Task 90 including §§4–7, and the five owner-local earlier reports. The original independent reports were not present; F1–F6/N1–N4 are assessed using the supplied meanings and Task 90's record, followed by source inspection and independent verification. No unseen original wording or verdict is claimed as evidence.
- Earlier report SHA-256 values are preserved: 70 `C640DFA9F47BF63A2A8A4D683EA60376710D797A090EA528678B1A4C93F06396`; 70B `692D6258E9158498E6B6CA21BE97311A6A14D05C29FD0B74C285B5D731A619E3`; 70C `9D4BB6376DB2176C662E22B3D4D0B7260DF271FFAE31DA9D0729C338C42EE79C`; 70D `938B70B9EE955EB131B691950476807B450974EE183F045B60E18DF678D60269`; 70E `EE8F4EC74D32C0D7B9B9E73B1E0CF28B9229FCD30921850849E0D2F22EFDCE60`. All remain local, unchanged and outside this PR.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`, `TM-03/04/07/13`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05` and applicable `NFR-PERF-01–10`. `FRA-14–16` remain independently open. Optional accessibility evidence alone does not fail this gate.
- Reviewed the scoped Domain/Application AI files, clock watch, Windows adapter, SQLite AI schema/migrations/compaction, desktop dialogs/composition and matching tests including the candidate tests. Compared #127's response, presentation and redaction changes with their consumers; earlier unchanged controls were rechecked rather than accepted from the remediation record.
- Only this report and its STATUS entry change. No product code, committed tests, packet, ADR or TRACEABILITY edit; no subagents/worktrees. The explicit two-file boundary and explicit PR instruction control over generic traceability/no-publication instructions. Temporary ignored review fixtures are verification artifacts.
- No real key, native credential access, AI-provider network request, paid request, account query, OS-record collection or installed-app action. Task 69's dated provider facts are input evidence, not a fresh provider check. GitHub repository operations and new synthetic local fixtures are separate from prohibited provider operations.

## 2. T70F-01 — Low: N2 overstates automatic pausing for a model-name mismatch

**File/line:** `docs/review/TASK_69_AI_EVIDENCE_GPT_6_1_SOL.md:131`; implementation at `src/PCSteward.Domain/AdvisoryAiResponse.cs:228` and `src/PCSteward.Domain/AdvisoryAiBudget.cs:414–419`. **Requirements:** `FR-AI-06`, `FR-BUD-03`; Task 71 evidence/stop accuracy. Related note: N2.

Item 5 says a small reply-shape surprise ends the test “as uncertain and paused”, giving a model name with a date suffix as an example. A pure model mismatch without an independent billing/state departure is uncertain but does not set the automatic review block.

**Concrete independently reproduced scenario:** a bounded synthetic response reports `synthetic-review-model-2026-11-14` instead of the expected exact model, otherwise with ordinary zero-cache usage. Result:

```text
dated-model-surprise=ModelMismatch; state=Uncertain; held=12000; auto-review=False
```

No answer is accepted and the full reservation stays committed. There is no automatic retry or release. The mismatch reason is displayed in closed terms. The same distinction applies generally to ordinary malformed/unknown outcomes without a detected departure.

**Recommended wording correction:** distinguish “retains uncertainty and requires the owner's stop” from an automatically persisted review pause; name `background:null` and unknown usage counters as automatic-pause examples, and a model suffix as a manual-stop example. Add a packet/presenter regression for that distinction.

**Why this does not block this scoped pass:** item 6 explicitly says ordinary uncertainty does not pause automatically and requires the owner, after the one attempt **whatever its outcome**, to set the request cap to $0 and stop/report. Any retry requires a new task/approval. That existing instruction covers this result without an automatic pause. No additional send, refunded allowance, trusted wrong-model settlement or new privacy/continuity exposure was demonstrated. This is a documentation-accuracy issue, not a waiver of a paid bound or permission to rely on the overbroad sentence.

## 3. Disposition of every earlier finding and note

All references are to **bb3c049**. SQLite references mean `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs`; other leaf names identify their scoped Domain/Application/Desktop component. Earlier severities are retained; notes are not promoted to findings merely because host evidence remains future work.

| Item | Disposition | Independently checked evidence and scenario/limit |
|---|---|---|
| **T70-01 — High**, detailed commitment inconsistency | **Fixed.** | SQLite `291–306,455–482` checks state/reservation/usage/frozen-rate consistency. Lowered Uncertain/claimed or inconsistent settled commitments invalidate admission, summary and recovery and persist review. Seven corruption cases and restart behavior passed. Coordinated internally consistent rewriting remains the disclosed external-restore limit. |
| **T70-02 — High**, B2/coupled profile | **Fixed for the one-request policy.** | Candidate tests and independent byte/cost arithmetic pass; A2 records the owner's B1(a)/B2 exceptions. These remain policy choices, not model measurements or ordinary-use support (§6). |
| **T70-03 — High**, effective project prices | **Fixed under owner-confirmed pricing.** | `AdvisoryAiSetup.cs:212–230` refuses missing project-effective-price attestation; desktop checkbox starts unticked. Packet requires effective rates and a non-residency project before the test. No account/pricing API verification is claimed. |
| **T70-04 — Medium**, forward-clock guard | **Fixed.** | `AdvisoryAiClockWatch.cs:22–54`, workflow `142–147` and `App.xaml.cs:214–216` wire the wall/monotonic check and sticky pending jump into a durable review block before reserve. Jump/elapsed-time/failed-block-write fixtures pass. Stored block survives restart; watch baseline is not trusted persistent time. |
| **T70-05 — Medium**, compaction/quota/resources | **Partly fixed overall.** | Compaction, conservation, tariff pruning, logical ceiling and populated migration checks pass (§5). CPU/working-set/UI/idle-I/O and complete physical DB/WAL/backup acceptance remain ordinary-use gates, not silently marked complete. Their absence alone need not block one supervised bounded experiment (§6). |
| **T70-06 — Medium**, secret warning | **Fixed.** | Both draft and exact-request screens show explicit best-effort/residual-secret wording (`AdvisoryAiExplain.cs:190`; desktop `124,203`). Data-controls disclosure is repeated at final review. Exact inspection still matters; no perfect secret recognition is promised. |
| **T70-07 — Medium**, stop/capture mismatch | **Fixed for the original cases.** | Ordinary timeout/uncertainty is explicitly subject to manual $0 stop. Exact/missing/unreadable cache figures are available in counts-only output; tier metadata is now closed too. The new N2 wording accuracy issue is separately retained as T70F-01, not an automatic-pause guarantee. |
| **T70B-01 — High**, compacted test total | **Fixed.** | SQLite `118–129,328–387` retains/recomputes compacted charge/test/five-count proofs. Original $0.99 compact/tamper reproduction now returns review and `LedgerNeedsReview`; seven compacted-corruption cases pass. |
| **T70B-02 — Medium**, cache write in refused body | **Fixed.** | Reader `192–210` propagates independently observed nonzero/unreadable writes into untrusted/above-bounds status even after early refusal. Five refusal reasons × write/unreadable/zero cases and the original wrong-model/write reproduction pass. |
| **T70C-01 — Medium**, repeated cache selectors | **Fixed.** | Reader `347–373` enumerates all usage/details/counter occurrences. Original parser → settlement → store reproduction persists review/refuses next reserve; independent 364-case cache matrix passes. |
| **T70E-01 — Medium**, raw tier in evidence | **Fixed.** | Reader `155,390–402` retains only an app-owned known-tier constant or a closed report enum. Workflow forwards the enum; presenter `270–283` independently revalidates a Named value. Original `reviewaccount` reproduction and a forged Named report no longer expose that text (§4). |
| **T70E-02 — Low**, null tier duplicates | **Fixed.** | Every occurrence counts before null handling (`AdvisoryAiResponse.cs:390–402`). Default/null in both orders and null/null now report Repeated, keep the reservation and pause; new store instance retains the block and next reservation returns `LedgerNeedsReview`. Four repository cases and all ordered pairs in the tier matrix pass. |
| **T70E-03 — Low**, leading IPv6 | **Fixed for the reported forms.** | `AdvisoryAiRedaction.cs:37–38,175` adds boundary-aware leading compression. Original mapped `::ffff:c0a8:117`, `::abcd:ef12` and dotted mapped-address cases redact; `std::vector` control remains unchanged. This is reported-case coverage, not exhaustive address/secret recognition. |
| **T70E-04 — Low**, version false positives | **Fixed for the reported boundary.** | `AdvisoryAiRedaction.cs:42–46,176–178` validates octets and requires `:`/`=` after ambiguous host/server/client labels. Original SQL Server/host versions, invalid-octet version, TeamViewer Host and Zoom Client controls remain intact; explicit IP/address/gateway/DNS forms redact. Unlabelled four-part numbers intentionally remain ambiguous. |
| **F1 — Medium**, returned service tier | **Fixed.** | Approval reader passes actual requested tier/no-cache settings (`158–162`). Known wrong tier, unknown/malformed/repeated tier, null duplicate and early-refused cases invalidate usage/flag review. Absent/single-null is the deliberate N3 test allowance, not proof of the billed tier. |
| **F2 — Low**, usage/cache/state/zero input | **Fixed for the reported cases.** | Reader checks all occurrences for unknown usage/detail keys, nonzero/non-plain cached counts on no-cache requests, and unrequested background/previous-response/conversation state. Departure remains visible on early refusal; stateful text is discarded. Zero-input completed answers retain uncertainty; provider-terminal failure may honestly report zero. Fresh fixtures pass. |
| **F3 — Low**, indistinguishable failures | **Fixed.** | HTTP number, closed malformed reason, tier-report enum/known constant and fixed departure words reach the outcome without provider messages. Forged unknown Named value is also rejected by the presenter. Original metadata privacy exception is fixed; N4 adds cost warning. |
| **F4 — Low**, redaction gaps | **Fixed for the supplied cases; best-effort residuals remain disclosed.** | Basic credentials, password-is/PIN/passcode/PSK, tested IPv6/IPv4 forms and contextual version negative controls pass. Paths with spaces/display names and ambiguous prose remain inspection responsibilities, not promises of complete anonymity. |
| **F5 — Low**, no cache-write rate | **Answered.** | A2 reserves for approved default-tier/no-cache semantics; deviations are detected afterwards, not prepaid at every possible provider rate. A reported departure retains uncertainty and pauses; this cannot guarantee the first invoice (§6). |
| **F6 — Low**, byte-bound wording | **Answered.** | A2 records B1(a)/B2 as scoped one-request exceptions to F7 proof requirements. No measurement or ordinary-use exception is inferred. |
| **N1 — note**, contextual version false positives | **Fixed.** | Same source/reproductions as T70E-04; product words without separators no longer turn versions into addresses. Valid explicit-address controls remain. |
| **N2 — Info**, strict response shape surprise | **Partly answered.** | Closed reply reasons explain refusals. `background:null` and unknown usage counters pause; a plain model suffix retains uncertainty without automatic review. Item 6 supplies the required manual test stop. Low wording issue remains T70F-01. |
| **N3 — Info**, absent tier accepted | **Answered as a scoped test policy.** | Missing/single-null remains NotReported; no tier is invented. Confirm returned-tier presence live; the record requires tightening before ordinary use if the route always reports it. This is not provider-tier proof, nor an ordinary-use waiver. |
| **N4 — Info**, kept amount may understate a dearer tier | **Answered.** | Presenter `291–293` explicitly says the real charge may exceed the kept amount for tier/unpriced-counter departures and points to the service's usage page. This is disclosure, not a corrected invoice, refund or account-query integration. |

Historical 70D PASS and the independent PASS at 094bcfb are not substituted for these checks. No earlier item is silently dropped or treated as host evidence.

## 4. Response, metadata and redaction verification

**Closed tier projection.** `KnownServiceTiers` is a finite set of application constants. The parser resolves JSON values to those constants, otherwise emits Unrecognised/Repeated/NotReported. Knowing another tier's name never accepts its price: it still differs from requested default and triggers review. The presenter guards the Named case again, so manually constructing an unknown Named value does not expose it. Actual workflow projection includes `ServiceTierReport`; the synthetic reproduction uses that same projection and actual presenter. Unknown/account-/secret-shaped text is absent from output, including early-refused bodies.

**Repetition and malformed billing.** The independent **240-case tier matrix** covers all 225 ordered pairs of 15 values (null, all known names, arbitrary account-/secret-shaped names, number, boolean, array and object), plus 15 single-value controls. Every repeated pair reports Repeated, retains 12,000 micro-USD and pauses; single default/null behaves as the explicit policy allows. The original default/null store reproduction now persists review across a new store instance and blocks the next reserve. The separate **364-case cache matrix** covers repeated root usage/details/counters, zero/nonzero/malformed/overflow counts, missing/nonobject containers in both orders, escaped property names and absent-field controls. All assertions pass; ambiguous usage is never trusted to reduce liability.

Unknown keys are checked only within the usage/detail objects that the reader understands; unknown arbitrary top-level data is not interpreted or executed. Positive/non-plain cached input on a no-cache profile, unrequested state and unknown billing counters are departure flags even when strict answer validation refuses early. Invalid JSON, excess depth, oversized or transport-truncated bodies cannot yield trusted usage; ordinary uncertainty is retained. Not every malformed body is claimed to create an automatic review pause (T70F-01).

**Redaction trade-off and limits.** The original 70E address and version cases now pass for evidence; the existing answer/edit fixtures cover the shared sanitizer and re-redaction path. Full and fe80-compressed IPv6 controls still pass. Leading `::` matching has a preceding-boundary rule, preserving the C++ namespace/time control. Dotted IPv4 candidates validate octets; only IP/IPv4/IP-address labels can precede directly, with separators required for ambiguous host/server/client/address vocabulary. This deliberately preserves version prose and can leave ambiguous unlabelled address-like text for exact preview. It does not promise every address spelling, display name or secret is recognizable. No new concrete privacy or authority regression was established in the reviewed changes; optional broad redactor redesign is not inferred.

**Software result versus external result.** These are hostile synthetic-response and text tests. They do not prove the provider actually returned a tier, respected no-cache/storage controls, charged particular rates, or disclosed any real identity. No real provider body or secret was used or saved.

## 5. Accounting, compaction, quota and migration

**Transaction/proofs.** SQLite `408–448,609–636` uses an immediate write transaction and compaction savepoint. It first releases only unclaimed Reserved rows and makes unfinished claimed rows Uncertain. Compaction requires a readable ledger, no review block and accepted current period, moves only Settled/NotSent rows strictly older than current month minus 12, and never compacts unresolved liability. In November 2026, November 2025 remains detailed. SQL records original-period monthly totals and settled identities/response IDs/tariff/charge/test/five usage counts, then deletes eligible detail.

Every snapshot validates each compacted charge at its referenced rates and reconciles monthly money/test money/settled-request count; missing month/request and corrupt bindings fail closed (`328–387`). Before releasing the savepoint it also conserves current-period settled/outstanding, cumulative test settled/outstanding, in-flight status and all-time settled money. Released counts carry no spending authority. An independent trigger adding one micro-dollar caused **`Compacted=0`, detailed/month/identity/review = `1|0|0|1`**: original detail retained, new structures rolled back, durable review set.

**Later reads and dedup.** The original $0.99 historic first-test settlement compacts once and correctly refuses a $0.045056 next test. Lowering only its compacted month's test total now returns `LedgerNeedsReview`; the unreadable summary's zero is accompanied by review and never grants allowance. Seven compacted-corruption fixtures and restart checks pass. Response-ID reuse checks both detailed/compacted tables (`782–787`); conflicting repeats pause. A stored row never grants a send: fresh memory-only approval and unique durable claim remain necessary.

**Tariff pruning.** SQLite `193–214` creates an immutable rate version and removes older same-model/tier versions only when referenced by neither detailed nor compacted requests. Thirty confirmations retain the historically referenced rate plus newest rate; unresolved/compacted liabilities retain their frozen rates. Pruning changes no spend. Exact-model/tier positive prices remain owner-confirmed, current only in `[confirmed,confirmed+24h)`, not software/account verified.

**16 MiB arithmetic.** `AdvisoryAiAccounting.cs:148–162` counts detailed rows at 1,024 B, tariffs/compacted requests at 512 B, months at 64 B with checked arithmetic. Compacted normal values still fit: 36-byte GUID + at most 256 B opaque ID + nine 8-byte integers = **364 B**; incoming IDs are additionally ASCII/128-character bounded. With 32,766 compacted rows plus one tariff/month, **448 B** remains; another 1,024 B request is refused. Removing two identities with matching monthly count correction frees 1,024 B and permits a row. The boundary fixture passes alongside the 10,000-detail ceiling. This logical estimate is not a physical cap on SQLite pages/indexes/WAL/backups, singleton metadata or arbitrary external corruption. Price confirmation/pruning is not itself request admission.

**Populated migration 14 → 15.** `SqliteDatabase.cs:86–95` applies 15 transactionally after 14 and validates schema, maximum known version 15. Independent synthetic migration-14 state included connected settings, continuity, tariff, Uncertain 12,000-micro first-test liability and unrelated metadata. Injected abort at marker 15 left **`14|0|1|kept`** (version/new tables/connected/metadata); retry after removing the fault gave **`15|2|1|kept`**, preserving **`Uncertain|12000|1|1`** (state/commitment/test/tariff). No installed database was touched. Source describes 15 as unreleased. An experimental old-shape DB already stamped 15 is not a demonstrated compatible enabled-test baseline; do not silently reset/downgrade it or invent continuity. Broader external-backup recovery is not proved by this upgrade fixture.

**Other budget/continuity checks.** Fresh fixtures cover checked upward-rounded micro-USD, exact/one-unit-over caps, zero disabling, prospective editable limits, frozen UTC period/rates, settings/consent/key/newest-tariff rechecks inside reserve/claim transactions, unique durable claim before one send, crash recovery without replay/refund, reliable terminal-incomplete/empty/refused-answer usage independent of saving, and conflict/known-bound-breach review. Clear/disconnect/default changes preserve accounting. Full reset deliberately removes all local spending knowledge/key without an external counter; prior-use declarations, recognized-recovery guards and outside cumulative-test accounting remain necessary. Coordinated consistent rewrites or undetected external restores are the disclosed limit, not mechanically solved continuity.

## 6. B1/B2, priced request and physical resources

The owner's **B1(a)** byte-bound/live-check choice and **B2 “Use wide safety margins”** remain accepted policy, expressly scoped in A2. The candidate remains outside `Reviewed`: context 1,050,000/max output 128,000, default tier, low reasoning, standard-price input threshold 272,000, explicit no-cache, 8,192 framing tokens, 192 bytes/output token and a 1 MiB envelope. Coherence/fixture success is not evidence of an unpublished tokenizer or measured hidden framing. One returned input count cannot isolate framing from visible-text byte slack.

| Packet bounds: 12,288 input / 2,048 output | Fresh independent result |
|---|---:|
| Complete request allowance | 74,752 B |
| Complete response allowance | 3,481,600 B |
| Sanitized saved-item allowance | 3,932,672 B |
| Canonical evidence room after instructions/framing | 3,399 B |
| Full-ceiling reservation at supplied $2/$0.10/$10 per-million rates | $0.045056 |
| Reservation with explicitly entered 10% higher rates | $0.049562 |

The three byte allowances fit 4 MiB; saved aggregate remains 8 MiB. Both costs fit $0.05; the actual preview reserves its own possibly smaller input bound. Project-effective prices must be explicitly entered/attested; no silent premium is assumed.

**Judgment:** these B2 values adequately bound the accepted short one-request experiment on its stated provider-compliance assumptions. They are safety margins, not a universal first-invoice or framing guarantee and not an ordinary-use waiver. A2 prices requested default/no-cache semantics; detected departures stop later paid use but cannot retroactively bound/refund a provider's first charge. F5's roughly $0.006 is only the 30-minute cache-write example (12,288 × $0.50/M = $0.006144), not a universal breach maximum. N4 now makes that invoice/kept-reservation distinction visible. No provider/account check was performed. The suggested separate throwaway-key/project/small-balance backstop is not software-proved hard billing enforcement, credit availability, or permission to create/fund/query a project.

**Still open for ordinary use:** process-tree CPU/peak working set, full read/parse/sanitize/save/display timing and cancellation, WinUI responsiveness, idle I/O and complete physical DB/WAL/sidecar/backup storage acceptance under ADR-006. Task 90's Linux accounting-size observation and the passing size fixture do not prove these. Even 2,048 output tokens admit a 3.32 MiB response under this profile. Byte/deadline/one-attempt controls and the supervised stop procedure make one separately reviewed synthetic experiment proportionate without ordinary-use saturation measurements; their absence alone need not block that experiment. They remain ordinary-use gates and inputs to the enabled artifact's host/readiness review. Optional accessibility evidence is not used to fail this review.

## 7. Task 71 packet against original Task 70 §7

| Required item | Assessment of current Task 69 §10 |
|---|---|
| 1. B1/B2/profile/effective prices | Addressed for the scoped experiment: accepted choices, exact candidate, non-residency project and effective-price attestation. Separate throwaway-key backstop does not replace local bounds or continuity and is not independently proved hard provider enforcement. |
| 2. Exact enabled artifact and review | Addressed as a future prerequisite: `Reviewed=[gpt-6.1-sol]`, `FirstLiveTest`, source SHA, artifact SHA-256/manifest, exact profile and SDK closure require a new independent review before action approvals. No release/updater publication of that enabled artifact. This pass covers none of those future edits/artifacts. |
| 3. Synthetic preparation and exact preview | One retained editable item with the specified Contoso text; omit other items. Real chosen-record interval/omission metadata is explicitly disclosed and must be approved. Use an existing separately authorized record; no improvised OS collection. Cancel on unapproved PC data or missing prerequisites. Redaction remains best effort. |
| 4. Separate key/send approvals and prior spend | Addressed: masked key entry and one exact send separately approved; current rates and zero settled/uncertain test totals checked, compacted spending validated, and outside prior/reset/restored use accounted for. Declaration/reset cannot create new $1 authority. |
| 5. Sanitized capture | Addressed for privacy: HTTP/counts/closed reasons and known-tier constants or fixed unknown/repeated/absent terms. Original arbitrary tier text no longer reaches evidence. No body/hash/header/key/raw error/answer text kept. N2's automatic/manual pause wording has the Low accuracy issue above. Provider usage page remains external corroboration, not invoice finality or continuity proof. |
| 6. One request then stop | Addressed: exactly one attempt, then request cap $0 and stop/report **whatever the outcome**; no retry without a new task/approval. Automatic departure/breach pauses supplement this. Ordinary malformed/timeout/uncertain results retain liability and may require that manual stop, including a model suffix. No automatic one-request fuse is assumed. |
| 7. No promotion | Addressed: success, month change, reconnect, reset or cap edit does not end FirstLiveTest. Ordinary use requires another decision/artifact and remaining measurements, including the recorded missing-tier follow-up where applicable. |

The packet passes as a **plan with an explicit manual stop**, not completed host evidence or action-time authority. T70F-01 recommends tightening the explanatory sentence; it does not override the existing mandatory item 6 or create an authorized retry. Any enabled artifact must be reviewed independently.

## 8. Egress, credentials, isolation and lifecycle

| Boundary | Pinned software and passing fixture result | Provider/host limit |
|---|---|---|
| Route/body/headers | `AdvisoryAiProviders.cs:61–75,92–125`: compiled OpenAI-only POST `https://api.openai.com:443/v1/responses`, no custom origin/query/project/organization routing. Canonical model/instructions/redacted editable input/max_output_tokens/store:false/truncation:disabled/default-tier/low-reasoning/explicit-no-cache body. Exact approved byte equality/provider match. Guard closes headers to Bearer Authorization (placeholder preview), JSON Accept/Content-Type, fixed `PCSteward-AdvisoryAI/1` User-Agent, length and standard Host/framing. | No real DNS/TLS/proxy/wire or provider enforcement/retention test. `store:false` is disclosed as no ZDR guarantee, including final review. No tools/history/conversation/uploads/background/streaming/counting/account/purchase capability. |
| Transport | Official OpenAI 2.15.0 / System.ClientModel 1.16.0 protocol method, zero retries and first-attempt guard; 3xx intercepted before SDK handling. Standard TLS/system proxy; no cookies/default credentials/decompression; SDK logging/tracing/runtime Activity propagation off. 10 s connect/45 s attempt including bounded success/error reads. | Mocks prove their cases, not actual provider billing or uncompromised host behavior. Attempt deadline excludes some DB/sanitizer/UI work. Cutoff/oversize rejection retains uncertainty, not proof of useful answer or no billing. |
| Credentials | Exact current-user generic `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence, printable ASCII <=2,048 bytes, save readback and deletion/fresh absence verification. No enumeration/environment/config/SQLite/log/export fallback. Masked box cleared, mutable buffers cleared where implemented; deliberate orphan/unknown handling and generation invalidation on replacement. | Native API faked; no real credential operation. SDK/PasswordBox immutable strings/OS memory cannot be securely erased. Actual storage/removal/logon behavior needs separate host authority. |
| Disconnect/reset/recovery | Durable revoke/version invalidation first, exact removal/verification, preserved content/accounting and truthful incomplete result. DB-independent reset callback before DB deletion; every recovery/onboarding/finalization path checks absence, old category pending, residual blocks completion (`App.xaml.cs:65–85,228–248`; reset fixtures). | Local deletion is not provider revocation. Removal-incomplete UI is session-only; disconnected after restart is not verified absence. No actual reset was run. Reset removes local spend knowledge; declarations and external-restore limits remain explicit. |
| Consent/advisory isolation | Evidence minimization and edit re-redaction, residual-secret warning, exact Cancel-default preview; binding includes bytes/profile/draft/source/limits/settings/consent/key/tariff generations. Plain TextBlock advice has no matching/correlation/job/helper/executable/link authority. ScheduledHost/Execution/helper lack AI capability. | Best effort does not guarantee anonymity, truthful advice or obedient output. Exact owner inspection remains required. Fixed metadata projection is now separate from provider text and tested defensively. |
| Response/cost | Exact model/type/status/ID/output checks; admitted bytes/depth 16; all cache-write/tier/usage/state departure occurrences examined. Reliable terminal-incomplete/refused/empty usage settles independently of saving. Unknown/ambiguous usage never proves a lower liability; known higher charge/overflow/breach and departures pause. | Absent/single-null tier deliberately remains allowed for the experiment (N3), not billed-tier proof. Ordinary malformed/model-mismatch uncertainty does not always automatically pause (T70F-01). No invoice/refund/account-reconstruction guarantee. |
| Content/accounting | 30-day startup/list/save expiry, source-specific scan/history clear, 4 MiB item/8 MiB aggregate cap, non-refunding separate ledger/key. No submitted prompt/body/hash/raw response/reasoning persisted. Answer redaction/saving is independent of settlement. | Logical deletion is not forensic erasure of free pages/WAL/external backups. Comprehensive backup filtering/resource acceptance remains ordinary-use work; no new general recovery feature is inferred. |

## 9. Fresh verification results

Native Windows x64; PowerShell 7; installed .NET SDK **10.0.404**, accepted by `global.json` 10.0.403/latestPatch; Release and existing pinned assets. Tests used the normal Windows test host outside the known sandbox connection limitation. No warning/dependency/product/test-policy change was made for a pass. Ignored review console fixtures used new synthetic databases and constructed no credential store or transport.

| Command | Actual result |
|---|---|
| `git fetch origin phase/4-beta`; `git rev-parse HEAD origin/phase/4-beta` | Exact `bb3c049d49831abed1f126e51413298e5b69e2f5` at entry; remote rechecked before publication. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **238 passed**, no failures/skips. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~SqliteDatabaseTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0: **215 passed**, no failures/skips. Actual filter uses literal pipes; backslashes are Markdown table escaping only. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **16 passed**, no failures/skips; **469 focused total**. |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0: **zero warnings/errors**, 12.61 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | Exit 0: **4,340 passed, 4 skipped, 0 failed; 4,344 total**. Domain 533; Architecture 270; Application 1,722 passed/1 skipped; Security 1,815 passed/3 skipped. |
| `dotnet run --project artifacts/task70f/ReviewHarness.csproj -c Release --no-restore` | Exit 0: original reproductions and migration/compaction failures rechecked; **364 cache-field plus 240 tier cases asserted**, forged metadata presenter and contextual redaction controls passed; N2 model-suffix result reproduced. Fixture-only restore used existing cached dependencies. |

Full-suite skips were `TheRunnersOwnTokenWithoutASplitTokenIsNoSplitToken`, `SymbolicLinkLeafIsRefusedEvenWhenTheProtectionCheckIsBypassed`, `TheProtectedRunFolderIsFreshAdministratorOnlyAndRemoved`, and the Windows PowerShell row of `RedeployToolingFunctionsHoldTheirContracts`. They remain unproven, not converted to passes; none skips an AI fixture or supplies the future enabled artifact's host evidence. These are fresh Windows counts, not earlier reviews' or the remediation record's results.

Selected independent outputs (all synthetic):

```text
intact-test-settled=990000
intact-next-test=AboveFirstLiveTestEnvelope
lowered-test-settled=0; review-block=True
lowered-next-test=LedgerNeedsReview
migration15-rollback=14|0|1|kept
migration15-retry=15|2|1|kept
migration15-preserved-ledger=Uncertain|12000|1|1
compaction-fault-compacted=0
compaction-fault-rollback=1|0|0|1
logical-near-ceiling-room=448
duplicate-usage-stored-review=True
duplicate-usage-next-reservation=LedgerNeedsReview
independent-billing-matrix-passed=364
metadata-details=Reply details: HTTP status 200, a service tier PC Steward doesn't recognise. ...
nullable-tier-stored-review=True
nullable-tier-next-reservation=LedgerNeedsReview
mapped-ipv6-redaction=IP address: [address]
invalid-ip-version-redaction=SQL Server 8.0.300.0 stopped responding
independent-tier-matrix-passed=240
dated-model-surprise=ModelMismatch; state=Uncertain; held=12000; auto-review=False
```

No source/mocked/synthetic result is presented as real secret deletion, provider retention, invoice finality, actual Windows UI performance or live one-request evidence.

## 10. Handoff and explicit gate

Keep this commit disabled. T70F-01 recommends a narrow documentation clarification; it does not authorize editing product code or a live action. A separately instructed next task may prepare an exact FirstLiveTest source/artifact with the packet's profile and SDK closure; that enabled artifact requires its **own independent review** before separate exact-key and one-send approvals. The test still stops after one attempt for every outcome, including uncertain/model-mismatch results without an automatic pause. Ordinary-use measurements, N3 follow-up where applicable, test-mode exit and `FRA-14–16` remain separate open gates. No next task starts from this report alone.

**PASS — model `gpt-6.1-sol`, default tier, route `POST https://api.openai.com/v1/responses`, exact commit `bb3c049d49831abed1f126e51413298e5b69e2f5`, and the Task 71 packet in Task 69 §10. This pass covers this disabled commit and the packet only; it never covers an enabled test build, which requires its own review.**
