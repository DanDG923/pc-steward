# Task 70E — Fifth AI security, privacy and cost review

**FAIL — `gpt-6.1-sol`, default tier, `POST https://api.openai.com/v1/responses`, exact commit `094bcfb42667655ed79e32d8552c5f5f6e8f7852`, and Task 69 §10's Task 71 packet.** Reviewed 2026-11-14.

The original wrong-tier settlement is corrected, and the earlier ledger/cache-write fixes still pass their reproductions. This review finds **one Medium and three Low findings** in the new metadata/redaction paths. The Medium privacy finding means the packet's new “numbers and fixed words, never provider text” capture claim is false. An arbitrary provider tier string bypasses ordinary answer redaction and reaches the screen the owner is instructed to record. This unresolved privacy issue blocks the gate independently of the Low findings.

`Reviewed` remains empty and paid requests remain `Disabled`. A PASS would cover only this disabled tree and the packet, never an enabled test build. No enabled artifact, key operation, paid send, deployment, ordinary use or release is approved here. Task 70D's historical PASS missed the independent review's F1 and is not carried forward as proof.

## 1. Identity, inputs and boundaries

- Fetched `phase/4-beta`; both its head and the clean implementation checkout were **`094bcfb42667655ed79e32d8552c5f5f6e8f7852`** at entry and the remote head matched again before publication. Review branch: `codex/task-70e-ai-security-review`. This is the sole implementation target; a later documentation commit changes no implementation baseline.
- Read AGENTS, STATUS, `prompts/70_AI_SECURITY_PRIVACY_COST_GATE.md`, Phase G/shared task contract, ADR-027/028/029 including A1/A2, the Task 67 plan, Task 69 including §§6/7/10, Task 90 including §§4–6, and all four owner-local earlier Task 70 reports. The independent review's original report was not present; its F1–F6 meanings are available from the owner's instruction and Task 90 §6. Its claimed fixes were independently checked, not accepted from that record.
- Earlier report SHA-256 values were preserved: 70 `C640DFA9F47BF63A2A8A4D683EA60376710D797A090EA528678B1A4C93F06396`; 70B `692D6258E9158498E6B6CA21BE97311A6A14D05C29FD0B74C285B5D731A619E3`; 70C `9D4BB6376DB2176C662E22B3D4D0B7260DF271FFAE31DA9D0729C338C42EE79C`; 70D `938B70B9EE955EB131B691950476807B450974EE183F045B60E18DF678D60269`. They remain local, unchanged and outside this PR.
- Requirements: `FR-AI-01–06`, `FR-BUD-01–04`, `TM-03/04/07/13`; related `FR-DAT-06–07/12–15`, `NFR-REL-02`, `NFR-MNT-01–05` and applicable `NFR-PERF-01–10`. `FRA-14–16` remain independently open. Optional accessibility evidence alone is not a failure reason.
- Inspected the scoped Domain/Application AI implementation (including the clock watch), Windows adapter, SQLite migrations/accounting/compaction, desktop dialogs/composition and matching fixtures, including the candidate tests. Only this report and its STATUS entry change. No product code, committed tests, task packet, ADR or TRACEABILITY edit; no subagents/worktrees. The explicit two-file boundary and explicit PR request control over the generic traceability/no-publication instructions.
- No real key, native credential access, AI-provider network request, paid request, account query, OS-record collection, installed-app change or release occurred. Task 69's dated provider facts are input evidence, not a fresh provider check. GitHub repository operations and new isolated synthetic fixtures are separate from prohibited provider operations.

## 2. Findings

### T70E-01 — Medium: raw tier text enters supposedly closed evidence without redaction

**Location:** `src/PCSteward.Application/AdvisoryAiExplain.cs:275`; originating validation at `src/PCSteward.Domain/AdvisoryAiResponse.cs:360,387–388`, forwarding at `src/PCSteward.Application/AdvisoryAiRequestWorkflow.cs:215`, display at `src/PCSteward.Desktop/MainWindow.AdvisoryAiExplain.cs:272`. **Requirements:** `FR-AI-02/05/06`, `TM-03/13`; ADR-028 output/sink minimization and Task 69 §10 item 5's fixed-word capture contract. Related independent finding: F3.

`IsTierName` accepts any <=32-character string of lower-case letters, digits and underscores. That is a character/length restriction, not a closed service-tier vocabulary or redaction. `ReplyDetails` interpolates the accepted value unchanged. The normal answer path redacts text with the PC's identity context; the new metadata path does not. It also runs for bodies refused for another reason.

**Concrete independently reproduced scenario:** a bounded response for the requested model has `service_tier:"reviewaccount"`, where `reviewaccount` is a synthetic identity in the redaction context. The reader correctly flags a tier departure and prevents settlement at default prices, but retains that exact arbitrary string in `ServiceTier`. Projecting the report exactly as the workflow does and invoking the actual `ReplyDetails` gives:

```text
Reply details: HTTP status 200, service tier "reviewaccount". ...
normal-answer-redaction=[redacted]
```

The first line contains `reviewaccount` unchanged; the same token passed through normal answer redaction is removed. A provider-controlled field could therefore carry an echoed name or a short secret into the UI and, when the owner follows §10 item 5, the supposedly sanitized test record. No actual private token, real provider response, log write or external disclosure was used to prove this flow. There is no HTML/command execution claim: plain-text rendering does not make arbitrary text safe as closed evidence.

**Required correction/evidence:** map only reviewed finite tier names to fixed display values and use a fixed unknown/unreadable label for everything else. Preserve mismatch/presence/duplicate signals without carrying arbitrary remote strings into the capturable record. Test provider strings matching known identities/secret-shaped values, unknown names, early-refused bodies and the actual result presenter. Update the packet's capture claim to match the corrected closed projection. A lexically restricted raw string is insufficient.

**Gate consequence:** unresolved privacy/sanitized-capture risk; FAIL even though the tier departure correctly retains liability and pauses paid calls.

### T70E-02 — Low: null tier occurrences disappear before duplicate detection

**Location:** `src/PCSteward.Domain/AdvisoryAiResponse.cs:358–362`; settlement at `src/PCSteward.Domain/AdvisoryAiBudget.cs:414–419`. **Requirements:** `FR-AI-04`, `FR-BUD-03`; independent F1's repeated-tier pause policy.

The tier list filters out JSON nulls before checking occurrence count. A body with `"service_tier":"default","service_tier":null` (or the reverse, or two nulls) therefore has no tier departure. Strict root validation still returns `DuplicateProperty`; usage remains untrusted and the reservation is retained. However, it does not pause as promised for a repeated tier.

**Concrete independent result:** both default/null orders returned `Malformed/DuplicateProperty`, `Departures=None`, `Uncertain`, committed **12,000 micro-USD**, `NeedsReview=False`. Applying the result to a new SQLite store gave `ReviewBlock=False`; the next reservation returned `None` (admitted). Two nulls reproduced the reader/settlement result too. No higher tier was hidden in these fixtures, no liability was released and no automatic second send occurred; this is the incomplete repeated-field interlock, not a demonstrated wrong-price settlement.

**Required correction/evidence:** count all tier occurrences before applying the single-null/absent allowance. Exercise null/default in both orders, null/null, duplicate known values, malformed values and an early-refused body through settlement and the durable review gate.

### T70E-03 — Low: leading-compression IPv6 addresses bypass the new address redactor

**Location:** `src/PCSteward.Domain/AdvisoryAiRedaction.cs:36,167`. **Requirements:** `FR-AI-02/05`; independent F4's IPv6 redaction coverage.

The compressed-address alternative requires a hexadecimal group before `::`. Valid IPv6 forms beginning with `::` are not matched.

**Concrete independently reproduced scenario:** `SanitizeEvidence("IP address: ::ffff:c0a8:117", 2048)` returned **`IP address: ::ffff:c0a8:117`**, unchanged. The fixture independently confirmed with `IPAddress.TryParse` that this is a valid IPv4-mapped IPv6 address. The complete mapped address remains available for final preview/egress; there is no need to infer whether the text is a version because it has an explicit address label. `::ffff:c000:0280` and `::abcd:ef12` also remained. Full eight-group and `fe80::...` repository cases pass.

**Required correction/evidence:** cover valid leading compression and mapped-address forms with bounded, boundary-aware recognition, including answer and edited-evidence paths; retain time/version negative controls. This does not demand perfect recognition of arbitrary secrets. Paths with spaces and an owner's display name remain separately disclosed best-effort residuals; this finding concerns a readily recognizable member of the newly claimed address class.

### T70E-04 — Low: broad IPv4 labels erase ordinary version evidence

**Location:** `src/PCSteward.Domain/AdvisoryAiRedaction.cs:37–39,168`. **Requirements:** `FR-AI-02/05`; independent F4's version false-positive boundary.

`server` and `host` count as address labels with optional punctuation, and the expression does not validate octet ranges. This silently replaces common dotted version forms before the final preview.

**Concrete independent results:** `SQL Server 8.0.33.0 stopped responding` became **`SQL Server [address] stopped responding`**; `host 4.2.1.0 failed` became `host [address] failed`. Even `SQL Server 8.0.300.0 stopped responding` lost its version although 300 is not a valid IPv4 octet. `version 8.0.33.0` remained intact, explaining why the existing unlabelled-version controls pass. Editing the same wording is sanitized again, so an owner cannot simply restore the version under those labels.

**Required correction/evidence:** distinguish explicit address context from product/version prose, validate candidate address structure and add these contextual version controls alongside real labelled-address cases. The exact final preview remains truthful about the redacted text, and no post-approval rewrite or action-authority defect is claimed. The impact is loss of useful diagnostic evidence and misleading redaction, not direct execution or spending.

## 3. All earlier findings: independent disposition

SQLite line references below mean `src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs`. Domain/Application/Desktop leaf names refer to those scoped source directories. Severities are those of the earlier findings.

| Earlier finding | Disposition at 094bcfb | Checked scenario/evidence and limit |
|---|---|---|
| **T70-01 — High**, detailed commitment inconsistency | **Fixed.** | State/reservation/usage/frozen-price checks at SQLite `291–306,455–482` reject lowered Uncertain and inconsistent settled charges and persist review. Seven corruption cases, restart checks and fresh ledger fixtures passed. No claim to detect coordinated consistent rewriting of all evidence. |
| **T70-02 — High**, B2/profile proof | **Fixed for the scoped one-request policy.** | Candidate tests and independent arithmetic still pass; B1(a)/B2 are now explicit in A2. These are accepted policy exceptions, not measurements or ordinary-use profile proof (§5). |
| **T70-03 — High**, effective prices | **Fixed under the accepted owner-confirmed-price policy.** | `AdvisoryAiSetup.cs:212–230` refuses an unticked project-price attestation; desktop checkbox is unticked by default. Packet requires effective rates and a non-residency project before the experiment. Software cannot verify account configuration/invoice. |
| **T70-04 — Medium**, forward-clock guard | **Fixed.** | `AdvisoryAiClockWatch.cs:22–54`, current workflow `139–144` and `App.xaml.cs:214–216` persist the monotonic-versus-wall jump block before reserve. Jump, ordinary elapsed time and failed-block-write fixtures pass. Stored block survives; the in-process baseline is not persistent time authority. |
| **T70-05 — Medium**, compaction/quota/resources | **Partly fixed.** | Compaction, pruning, 16 MiB arithmetic and populated migration checks pass (§4). CPU/working-set/UI/idle-I/O and complete physical-storage/backup evidence remain ordinary-use gates. They are not this review's reason to reject the one-request packet (§5). |
| **T70-06 — Medium**, secret warning | **Fixed.** | Both draft and exact-request screens still show `ResidualSecrets` (`AdvisoryAiExplain.cs:190`; desktop `124,203`). New data-controls disclosure also appears on final review. Warning and re-redaction do not remove the concrete metadata gap in T70E-01. |
| **T70-07 — Medium**, stop/capture mismatch | **Partly fixed.** | Timeout/uncertainty is accurately described and counts retain absent/zero/unreadable distinctions. Manual one-request/$0 stop remains explicit. The newly extended reply-details capture is not all closed data (T70E-01); packet item 5 remains unsafe as claimed. |
| **T70B-01 — High**, compacted test total | **Fixed.** | SQLite `118–129,328–387` retain and recompute charge/test/usage proofs. Original independent $0.99 compaction/tamper now blocks with review; seven compacted-corruption fixtures pass. |
| **T70B-02 — Medium**, cache write in refused body | **Fixed.** | Reader `162–180` carries the independently read cache signal into untrusted/above-bounds status. Original wrong-model/write reproduction has `NeedsReview=True`; all five refusal reasons × write/unreadable/zero cases pass. |
| **T70C-01 — Medium**, repeated cache selectors | **Fixed.** | Reader `316–342` enumerates all usage/details/counter occurrences. Original parser → settlement → SQLite reproduction blocks the next reserve; the independent 364-case cache matrix passes. T70E-02 is the distinct newly added tier path. |
| **F1 — Medium**, returned tier | **Partly fixed; original priced-tier scenario fixed.** | Approval overload now passes the actual expected tier (`AdvisoryAiResponse.cs:128–132`); departures invalidate usage and force review. Different tiers, malformed values, repeated non-null values, early-refused bodies and the workflow fixture pass. Null duplicates miss the promised pause (T70E-02). |
| **F2 — Low**, usage/cache/state/zero-input | **Fixed for the reported cases.** | Reader `225–228,344–382` checks unknown usage/detail keys, every cached-count occurrence on a no-cache request and background/previous-response/conversation state. Departures are retained even on early refusal; conversation text is discarded. Zero-input answers retain reservation; a terminal provider failure can reliably report zero. Matching fresh fixtures passed. |
| **F3 — Low**, indistinguishable failures | **Partly fixed.** | HTTP number, closed malformed reason and departure flags are forwarded/displayed (`AdvisoryAiRequestWorkflow.cs:209–217`, presenter `270–289`). Provider error messages remain excluded. The raw tier is not a closed word (T70E-01), so capture is not fully sanitized. |
| **F4 — Low**, redaction gaps | **Partly fixed.** | Basic credentials, password-is labels, PIN/passcode/PSK, full/fe80-compressed IPv6 and labelled IPv4 fixtures pass. Paths with spaces/display names remain disclosed residuals. Leading-compression IPv6 and contextual version errors remain T70E-03/04. Existing negative controls are too narrow for those cases. |
| **F5 — Low**, no cache-write tariff | **Answered as a request-contract assumption.** | A2 explicitly reserves for the approved no-cache/default-tier request; an observed departure is handled afterwards, not prepaid at every possible provider price. No cache-write rate was added. This is not proof against a provider violating the request or an invoice guarantee; see §5. |
| **F6 — Low**, byte-bound wording conflict | **Answered.** | ADR-028 A2 explicitly records the owner's B1(a)/B2 one-request exceptions to the earlier F7 proof rule. Ordinary use still requires evidence or another explicit recorded decision. This is policy alignment, not a tokenizer/framing measurement. |

The original independent report is unavailable, so this table does not claim to reconstruct unprovided wording or all of its additional notes. It assesses the six supplied finding meanings and the current implementing code. No earlier item is silently dropped.

## 4. Accounting, compaction and migration recheck

**Conservation and transaction.** SQLite `408–448,609–636` uses an immediate transaction and compaction savepoint. Only Settled/NotSent rows strictly older than current month minus 12 move; current plus previous 12 months remain detailed and unresolved liability remains indefinitely. Eligibility requires readable accounting, no review block and accepted current month. SQL records monthly totals and settled per-request ID/response-ID/tariff/charge/test/five usage counts, then deletes eligible detail. It validates every compacted charge and month/count binding and conserves current-period commitments, cumulative test amounts, in-flight state and all-time settled money. Released counts have no spending authority.

Fresh independent fault injection added one micro-dollar to a new monthly total. Result: **`Compacted=0`, detailed/month/identity/review = `1|0|0|1`**. Original detail remains; no partial compacted structures survive; review persists. The original $0.99 historical test compacts once, refuses another $0.045056, and after lowering only the monthly test total returns `LedgerNeedsReview`. The unreadable summary's zero is accompanied by review and does not release allowance. Response-ID dedup checks detailed and compacted tables (`782–787`); identical repeats and conflicts are separately tested.

**Tariffs.** SQLite `193–214` assigns immutable versions and prunes older same-model/tier rates only when referenced by neither detailed nor compacted rows. Thirty confirmations retain the historical referenced rate and newest rate. Unresolved/compacted liabilities retain frozen rates; pruning changes no money. Tariffs are positive, exact-model/default-tier, owner-confirmed, with half-open 24-hour freshness. The effective-project-price checkbox and final preview do not query the provider account.

**16 MiB ceiling.** `AdvisoryAiAccounting.cs:148–162` uses checked row estimates: detailed 1,024 B, tariff 512 B, compacted 512 B, month 64 B. Expanded compacted values fit 512 B: GUID 36 + normal provider ID at most 256 B + nine 8-byte integers = **364 B** (incoming IDs are additionally ASCII/128-character bounded). At 32,766 compacted rows plus one tariff/month, **448 B** remains; SQLite `389–392` correctly refuses another 1,024 B row. Removing two identities with matching monthly count correction frees 1,024 B and allows it. The 10,000-detail ceiling also remains. These are logical value estimates, not limits on SQLite page/index/WAL/backup overhead, arbitrary corrupted text or singleton metadata; price confirmation itself is not request admission.

**Populated migration 14 → 15.** SQLite migration sequencing `SqliteDatabase.cs:86–95` is transactional with maximum known version 15 and schema validation. In a new synthetic migration-14 DB containing connected settings, continuity, a tariff, Uncertain 12,000-micro first-test liability and unrelated metadata, an injected abort at marker 15 left **`14|0|1|kept`** (version/new tables/connected/metadata). Removing the trigger and retrying gave **`15|2|1|kept`** and preserved **`Uncertain|12000|1|1`** (state/commitment/test/tariff). No owner database was touched. As source states, the expanded migration 15 is unreleased; an experimental older-shape DB already marked 15 is not a demonstrated compatible enabled-test baseline and must not be silently reset/downgraded to invent continuity.

**Other retained invariants.** Focused fixtures cover checked upward-rounded micro-USD, exact/one-unit-over caps, prospective limits, zero disabling, frozen UTC period/rates, settings/consent/key/newest-tariff rechecks inside reserve/claim transactions, unique durable claim before one send, no automatic retry/replay/refund, crash recovery, reliable incomplete/refused/empty-answer usage independent of saving, and conflict/known-bound-breach review. Clear/disconnect/default changes preserve accounting. Full reset intentionally removes local accounting/key with no surviving spend marker. Prior-use declaration, recognized-recovery blocking and outside cumulative-test accounting remain necessary; undetected consistent external restores or coordinated rewrites are not claimed detectable.

## 5. B1/B2, first-request price and resources

The owner's B1(a) and B2 choices are preserved, now explicitly in A2. The candidate is still `gpt-6.1-sol`, context 1,050,000/max output 128,000, default tier, low reasoning, standard-price input threshold 272,000, explicit no-cache, framing 8,192, 192 bytes/output token and a 1,048,576-byte envelope. The candidate and three tests remain outside `Reviewed`; checks do not turn policy choices into measured tokenizer/framing facts.

| Packet bound for 12,288 input / 2,048 output | Fresh independent result |
|---|---:|
| Complete request allowance | 74,752 B |
| Complete response allowance | 3,481,600 B |
| Sanitized saved-item allowance | 3,932,672 B |
| Input evidence room after fixed instructions/framing | 3,399 B |
| Full-ceiling reservation at supplied $2/$0.10/$10 per-million rates | $0.045056 |
| Reservation with explicitly entered 10% higher rates | $0.049562 |

The three byte allowances fit 4 MiB and aggregate saved content remains 8 MiB. Both costs fit $0.05; actual preview reserves its own potentially smaller bound. Project-effective rates must actually be confirmed; no silent premium is assumed.

**Judgment:** the B2 values remain reasonable margins for the owner's scoped, short, one-request experiment. A single reported input count cannot isolate hidden framing from visible-text byte slack, and these values are not ordinary-use support or a theorem about the provider's first invoice. They are not reopened as a new finding. A2's request-contract framing means default-tier/no-cache pricing assumes provider compliance; detecting a departure after payment cannot bound or refund an already incurred charge. F5's roughly $0.006 is the 30-minute cache-write example (12,288 × $0.50/M = $0.006144), not a universal maximum if a provider violates other controls. The new separate-project/throwaway-key/small-balance suggestion is a backstop, not software-proved hard invoice enforcement, free credits or authority to create/query/fund a project. There was no provider/account check here.

**Open ordinary-use measurements:** process-tree CPU/peak working set; complete read/parse/sanitize/save/display timing and UI/cancellation; idle I/O; physical SQLite/WAL/sidecar/backup storage acceptance. Task 90's Linux accounting-size observation and the passing size fixture do not prove these. Even 2,048 output tokens admit a 3.32 MiB response under the wide profile. Logical byte/deadline/one-attempt controls plus supervision make an experiment proportionate; these measurement gaps alone need not block one separately reviewed synthetic test. They remain ordinary-use gates and inputs to an enabled artifact's own host/readiness review. This FAIL concerns concrete privacy/capture defects, not a reversal of that resource judgment.

## 6. Task 71 packet versus original Task 70 §7

| Required item | Assessment of current Task 69 §10 |
|---|---|
| 1. Profile/B1/B2/effective prices | Addressed for the scoped experiment; A2 aligns the policy wording. Non-residency project, effective rates and suggested separate throwaway-key backstop do not replace bound/continuity checks. No provider balance/budget guarantee was proved. |
| 2. Exact enabled artifact and review | Addressed as a future prerequisite: exact FirstLiveTest source SHA, artifact SHA-256/manifest, profile and pinned SDK closure require a new independent review before approvals. The packet now excludes publishing that enabled artifact through release/updater channels. This disabled-tree review cannot cover it. |
| 3. Synthetic preparation/exact preview | Procedure remains one retained item with the specified Contoso text and all other items omitted. It explicitly includes real selected-record interval/omission metadata, requiring owner approval. Use a separately authorized existing record; do not improvise OS collection or send unapproved PC data. Redaction false positives/misses remain visible risks in exact preview, not proof of anonymity. |
| 4. Separate key/send approvals and prior use | Addressed procedurally: masked key entry and one exact send separately approved, current rates and both settled/uncertain test totals checked, compacted spending validated, and outside prior/reset/restored use accounted for. A declaration/reset cannot grant new $1 authority. |
| 5. Sanitized evidence capture | **Not fully addressed: T70E-01.** HTTP/count/enum reasons are closed; arbitrary `ServiceTier` is raw provider-controlled text. The new instruction to copy reply details as “fixed words” is unsafe as claimed. Preserve no body/hash/header/key/raw error/answer text. Provider dashboard remains outside corroboration, not invoice finality or continuity proof. |
| 6. One request then stop | Manual one-attempt/$0 request-cap/stop-and-report procedure is explicit for every outcome, no retry without a new task. Original cache-write and wrong-tier cases now pause. Nullable repeated-tier handling remains inconsistent (T70E-02). Ordinary timeout/uncertainty correctly retains reservation without automatic pause. |
| 7. No promotion | Addressed: success, month, reconnect, reset or cap edits do not end test mode. Ordinary use and outstanding measurements require another owner decision/artifact. |

The revised packet is **FAIL** while its sanitized-capture claim remains false. It is not executed or enabled by this report.

## 7. Egress, credentials, isolation and lifecycle

| Area | Pinned software and passing fixture result | Provider/host limit |
|---|---|---|
| Route/payload/headers | OpenAI-only compiled `POST https://api.openai.com:443/v1/responses`; no custom origin/query/project routing. Canonical model/instructions/redacted editable input/max_output_tokens/store:false/truncation:disabled/default-tier/low-reasoning/explicit-no-cache body. Exact byte equality and provider-adapter match. Guard rebuilds Bearer Authorization (placeholder preview), JSON Accept/Content-Type, fixed `PCSteward-AdvisoryAI/1` User-Agent, length and standard Host/framing (`AdvisoryAiProviders.cs:61–75,92–125`; transport `159–182`). | No actual DNS/TLS/proxy/provider capture or retention enforcement. `store:false` is correctly disclosed as no ZDR guarantee, now repeated at final review. No chat/history/tools/uploads/background/streaming/counting/account/purchase call. |
| Attempts/transport | Official OpenAI 2.15.0 / System.ClientModel 1.16.0 protocol method; zero retry and second-attempt guard, 3xx interception before SDK handling, standard TLS/system proxy, no cookies/default credentials/decompression; SDK logging/tracing/runtime Activity propagation off. 10 s connect/45 s attempt including bounded success/error reads. | Mocks prove their cases; attempt deadline excludes some DB/sanitizer/UI work. Oversize/cutoff rejection retains uncertainty and does not prove no charge or useful answer. |
| Credentials | Exact current-user generic `PCSteward/OpenAI/ApiKey.v1`, local-machine persistence, printable ASCII <=2,048 bytes, save readback and deletion followed by fresh absence verification. No enumeration/env/config/SQLite/log/export fallback. Masked box cleared, mutable buffers cleared where implemented. | Fake native API, no real key operation. SDK/PasswordBox immutable strings/OS memory are not securely erasable. Real storage/removal/logon behavior needs separate host authority. |
| Disconnect/reset/recovery | Durable revoke/version invalidation first, exact key removal/verification, preserved content/accounting and incomplete-removal result. DB-independent reset callback before DB deletion; every recovery/onboarding/finalization rechecks absence; old marker category pending and residual blocks completion (`App.xaml.cs:65–85,228–248`; reset fixtures). | No provider revocation or real reset claim. Removal-incomplete UI is session-only; disconnected after restart is not verified absence. Full reset removes local spend knowledge; external restore limits remain explicit. |
| Advisory isolation/redaction | Evidence/edits are redacted, explicit residual warning and exact Cancel-default preview; approvals bind bytes/profile/draft/limits/settings/consent/key/tariff generations. Plain TextBlock advice has no execution/link/job/matching/correlation authority; ScheduledHost/helper/Execution lack AI capability. New Basic/named-secret tests pass. | Best effort is disclosed. T70E-01 adds an unredacted metadata path; T70E-03/04 qualify the new address patterns. Prompts do not guarantee useful/obedient advice. |
| Response/usage | Size/depth/output-type/model/status checks; all cache-write occurrences; returned-tier and usage/cache/state departure checks even on early refusal. Complete reliable terminal-incomplete usage can settle independently of saving. Zero-input completed answer retains uncertainty; provider-terminal failure may report zero. | Unknown/malformed/unreadable evidence never proves a lower invoice. Null-tier repetition is T70E-02. Absent/single-null tier is deliberately allowed, not proof of actual provider tier. Provider service-tier/retention/cache/billing behavior remains external evidence. |
| Content separation | 30-day startup/list/save expiry, source-specific scan/history clear, 4 MiB item/8 MiB aggregate cap and independent non-refunding accounting/key. No submitted prompt/body/hash/raw response/reasoning persisted. Answer saving/redaction stays independent of settlement. | Logical deletion is not forensic erasure of free pages/WAL/external backups. Comprehensive host/backup/resource evidence remains open. The new reply-details capture needs correction before being described as closed evidence. |

## 8. Fresh commands and actual results

Native Windows x64, PowerShell 7, installed .NET SDK 10.0.404 accepted by `global.json` 10.0.403/latestPatch; Release, existing pinned assets. Tests ran outside the known sandbox testhost connection limitation. No warning policy, dependency version, production code or committed test filter was weakened. Independent console fixtures under ignored `artifacts/task70e` used fresh synthetic databases and constructed no key store or transport.

| Command | Result |
|---|---|
| `git fetch origin phase/4-beta`; `git rev-parse HEAD origin/phase/4-beta` | Exact `094bcfb42667655ed79e32d8552c5f5f6e8f7852` at entry; rechecked before publication. |
| `dotnet test tests/PCSteward.Domain.UnitTests/PCSteward.Domain.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **222 passed**, no failures/skips. |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AdvisoryAi\|FullyQualifiedName~FactoryResetTests\|FullyQualifiedName~SqliteDatabaseTests\|FullyQualifiedName~CurrentUserDataOperationLeaseTests' --logger 'console;verbosity=minimal'` | Exit 0: **214 passed**, no failures/skips. Actual PowerShell filter has literal pipes; table backslashes are Markdown escaping only. |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj -c Release --no-restore --filter FullyQualifiedName~AdvisoryAi --logger 'console;verbosity=minimal'` | Exit 0: **16 passed**, no failures/skips; **452 focused total**. |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | Exit 0: **zero warnings/errors**, 10.69 s. |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | Exit 0: **4,323 passed, 4 skipped, 0 failed; 4,327 total**. Domain 517; Architecture 270; Application 1,721 passed/1 skipped; Security 1,815 passed/3 skipped. |
| `dotnet run --project artifacts/task70e/ReviewHarness.csproj -c Release --no-restore` | Exit 0: old reproductions, 364 asserted cache-field variants, and new metadata/null-tier/redaction reproductions. After adding mapped-address/store checks, built only the fixture with `dotnet build ... -c Release --no-restore --no-dependencies`, then reran with `--no-build --no-restore`: exit 0. No product dependency changed. |

The four full-suite skips were `TheRunnersOwnTokenWithoutASplitTokenIsNoSplitToken`, `SymbolicLinkLeafIsRefusedEvenWhenTheProtectionCheckIsBypassed`, `TheProtectedRunFolderIsFreshAdministratorOnlyAndRemoved`, and the Windows PowerShell row of `RedeployToolingFunctionsHoldTheirContracts`. They remain unproven, not converted to passes; none skips an advisory AI fixture or supplies future enabled-artifact host evidence. These are fresh results, not earlier reviews' or the remediation record's counts.

Selected independent results (all inputs synthetic):

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
metadata-tier=reviewaccount
normal-answer-redaction=[redacted]
nullable-tier-stored-review=False
nullable-tier-next-reservation=None
mapped-ipv6-redaction=IP address: ::ffff:c0a8:117
invalid-ip-version-redaction=SQL Server [address] stopped responding
```

Passing existing suites do not exercise or refute the newly reproduced cases. No provider behavior, actual secret disclosure, invoice finality, real credential deletion or host UI/performance success is inferred from source/mocks/fixtures.

## 9. Handoff and explicit gate

An explicitly requested Task 90 should address the findings, then a fresh independent Task 70 should review a new immutable snapshot and corrected packet. Keep the model registry empty and paid requests disabled; do not begin Task 71 or prepare/approve an enabled build automatically. Any future enabled test source/artifact requires its own exact independent review, then separate action-time key/send approvals. Preserve the accepted B1/B2 policy and verified accounting fixes. Ordinary-use resource acceptance, test-mode exit and `FRA-14–16` remain separate gates.

**FAIL — model `gpt-6.1-sol`, default tier, route `POST https://api.openai.com/v1/responses`, exact commit `094bcfb42667655ed79e32d8552c5f5f6e8f7852`, and the Task 71 packet in Task 69 §10. A PASS could cover only this disabled commit and the packet; it would never cover an enabled test build, which requires its own review.**
