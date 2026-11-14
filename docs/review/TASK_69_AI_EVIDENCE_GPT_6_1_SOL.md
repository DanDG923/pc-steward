# Task 69: evidence for GPT-6.1 Sol (readiness report)

**Status (2026-11-14): evidence gathered, product fixes made; B1 and B2 settled by owner decisions; Task 70 failed at `db4bc76` and its findings are fixed by [Task 90](TASK_90_TASK_70_AI_REMEDIATION.md), awaiting a fresh Task 70.** No paid call, no account query and no key were used. `AdvisoryAiModelProfiles.Reviewed` stays empty and `AdvisoryAiBuildPolicy.PaidRequests` stays `Disabled`. This report is the input to the independent Task 70 review. Neither this report nor Task 70 approves the Task 71 live call: that needs the owner's separate go-ahead.

Requirements: `FR-AI-01–06`, `FR-BUD-01–04` ([Phase G](../requirements/V2_PHASE_G.md), [Task 69 prompt](../../prompts/69_AI_EVIDENCE_READINESS.md), [plan](TASK_67_ADVISORY_AI_PLAN.md)). Owner choice (2026-11-14): `gpt-6.1-sol` only, with no model picker.

## 1. Sources (official OpenAI pages, read 2026-11-14)

| Ref | Page | Used for |
|---|---|---|
| S1 | https://developers.openai.com/api/docs/models/gpt-6.1-sol (and its `.md` form) | Model ID, snapshots, context, input and output maxima, reasoning efforts, prices, long-context rule |
| S2 | https://developers.openai.com/api/docs/pricing | Prices by tier, the cache-write columns, regional uplift, the Priority→Fast rename |
| S3 | https://developers.openai.com/api/docs/guides/prompt-caching | Automatic caching, cache-write rates, explicit mode, usage fields |
| S4 | https://developers.openai.com/api/reference/cli/resources/responses/methods/create | `service_tier`, `truncation`, `store`, `max_output_tokens` and `prompt_cache_options` semantics |
| S5 | https://developers.openai.com/api/reference/resources/responses/methods/create/ | The response object and the `usage` fields in its example |
| S6 | https://developers.openai.com/api/docs/guides/reasoning | Effort values and default, reasoning billed as output, incomplete at `max_output_tokens` |
| S7 | https://github.com/openai/tiktoken (`tiktoken/model.py`, main branch) | Tokenizer mapping (it has no `gpt-6` entry) |

Prices and model facts change. The app never relies on this table for money: the owner types today's prices from S2 every 24 hours (ADR-029 F6), and the reservation uses those.

## 2. Model facts

| Fact | Value | Source |
|---|---|---|
| Model ID | `gpt-6.1-sol`. It is the only snapshot listed, and there is no dated snapshot. | S1 |
| Endpoint | `POST /v1/responses` (the Responses API) is listed | S1 |
| Context window | 1,050,000 tokens | S1 |
| Maximum input | 922,000 tokens | S1 |
| Maximum output | 128,000 tokens | S1 |
| Reasoning effort | `low`, `medium` (default), `high`, `xhigh` and `max`. `none` and `minimal` are not offered for this model. | S1, S6 |
| Input and output | Text and image in, text out | S1 |
| Tokenizer | **Not published** for this model. tiktoken maps `gpt-5*` to `o200k_base`, but has no `gpt-6*` entry. | S1, S7 |

## 3. Prices (Standard, per 1M tokens, as listed 2026-11-14)

| Input | Cached input | Cache write, 30 min | Cache write, 12 h | Output |
|---|---|---|---|---|
| $2.00 | $0.10 | $2.50 | $5.00 | $10.00 |

- **Above 272K input tokens**, the *whole request* is billed at 2x the input and cache rates and 1.5x the output rate (S1).
- **Cache writes** for GPT-5.6 and later cost 1.25x the uncached input rate (30-minute default) or 2.5x (12 h). These are total rates, not surcharges (S3).
- **Other tiers:** Flex and Batch are 50% of Standard. Fast (formerly Priority) is 2x. Ultrafast is 6x (S1, S2).
- **Regional processing** (data-residency endpoints) adds 10% (S2). PC Steward only ever calls `api.openai.com`.
- Reasoning tokens are billed as output (S6).

## 4. Findings that changed the product (this change)

**F1, cache writes cost more than input (fixed).** Prompt caching is on by default (S3). A prompt of 1,024 tokens or more would be written to the cache at $2.50/M, above the $2.00/M input rate the reservation used. So the reservation would not have been a true worst case. A cache entry would also have kept derived state at the provider for 30 minutes. The fix has three parts:
- The profile gains `RequestNoPromptCache`. When it is set, the body sends `"prompt_cache_options":{"mode":"explicit"}` and never a breakpoint. S3 says: "When no explicit breakpoints are placed, the request does not use prompt caching or create cache writes."
- The response reader now reads the optional `input_tokens_details.cache_write_tokens`. Any write, or a value that isn't a count, makes the usage untrusted and sets the "above approved bounds" flag. The ledger then keeps at least the whole reservation and pauses paid requests for review.
- The architecture test pins `prompt_cache_options` and `mode` as reviewed body fields. It forbids `prompt_cache_breakpoint`, `prompt_cache_retention` and `ttl`.

**F2, the long-context price applies to the whole request (fixed).** With the 4 MiB request limit, an input limit of up to about 699,000 tokens passed validation. Above 272K the request costs 2x / 1.5x, so a reservation at the confirmed prices would have been too low. The profile gains `StandardPriceInputTokens`. Limit validation now refuses an input limit above it (`InputAboveStandardPrice`, shown on the input field), so every admitted request is billed at the confirmed prices. The profile is incoherent if this value is not above its framing allowance or exceeds its context window.

**F3, reasoning effort.** The lowest effort this model offers is `low` (S1). The profile fixes `ReasoningEffort = "low"`, so reasoning uses as little of the output allowance as the model allows.

**F4, reasoning can use up the whole output allowance (disclosed, not changed).** `max_output_tokens` covers reasoning, visible output and formatting tokens. Hitting it can end the response as `incomplete` (`max_output_tokens`) before any visible text, and that is still billed (S6). This is already handled: the outcome says "the answer stopped at your output limit", and the cost is settled from the usage. OpenAI suggests leaving 25,000 tokens of room while experimenting. **For the owner:** the accepted 1,024-token default may often give empty answers at `low` effort. The Task 71 plan below uses 2,048, which is an owner choice (FR-BUD-01).

## 5. Request fields checked against the API

| Field sent | Why | Source |
|---|---|---|
| `store: false` | `store` defaults to `true` when omitted | S4 |
| `truncation: "disabled"` | Also the default. It is sent explicitly, so an over-long input fails instead of being silently cut | S4 |
| `service_tier: "default"` | When unset the default is `auto`, which uses the *project's* tier (it could be Fast at 2x). `default` is "the standard pricing and performance for the selected model", which is what the owner's prices state | S4 |
| `reasoning: {"effort":"low"}` | F3 | S1, S6 |
| `prompt_cache_options: {"mode":"explicit"}` | F1 | S3, S4 |
| `max_output_tokens` | The owner's output limit, reasoning included | S4, S6 |
| Never sent | `stream`, `background`, `tools`, `tool_choice`, `previous_response_id`, `conversation`, `include`, `metadata`, `user`, `prompt_cache_key`, `safety_identifier` and any breakpoint (architecture test) | — |

The usage fields the reader trusts match the response example in S5: `input_tokens`, `input_tokens_details.cached_tokens` (and now `cache_write_tokens`), `output_tokens`, `output_tokens_details.reasoning_tokens` and `total_tokens`.

## 6. Candidate profile (not added to `Reviewed`)

| Profile value | Candidate | Evidence |
|---|---|---|
| `ModelId` | `gpt-6.1-sol` | S1 |
| `ContextWindowTokens` | 1,050,000 | S1 |
| `MaximumOutputTokens` | 128,000 | S1 |
| `ServiceTier` | `default` | S4 |
| `ReasoningEffort` | `low` | S1, S6 |
| `StandardPriceInputTokens` | 272,000 | S1 |
| `RequestNoPromptCache` | true | S3 |
| `TokenizerIsByteLevel` | true, on the owner's decision for B1 (a), checked live in Task 71 | S1 and S7 are silent; owner decision 2026-11-14 |
| `FramingAllowanceTokens` | 8,192, the owner's wide margin for B2 (16x the earlier 512 estimate). It is a policy decision, not a measurement. | Owner decision 2026-11-14 |
| `MaximumTokenUtf8Bytes` | Not published. 192, the wide margin (earlier 128, which already covered the longest `o200k_base` entries) | Residual R2, owner decision |
| `ResponseEnvelopeBytes` | Not published. 1,048,576, the wide margin (earlier 262,144) | Residual R3, owner decision |

`AdvisoryAiTask71CandidateTests` checks a copy of this profile. The copy is coherent. The §10 limits pass every request, response and saved-item byte ceiling, and leave room for evidence. The profile itself is still not added to `Reviewed`.

## 7. Blockers (both settled by owner decisions)

**B1, tokenizer.** The local input bound relies on "tokens ≤ UTF-8 bytes". That holds for any tokenizer in which every token covers at least one input byte, which includes OpenAI's byte-level BPE family (`o200k_base` for `gpt-5*`, S7). OpenAI publishes no tokenizer for `gpt-6.1-sol`. Options for the owner:
- **(a)** Accept the general argument plus the measured check in Task 71. That check compares `usage.input_tokens` against the app's own bound, and a request over the bound already pauses paid requests (above approved bounds).
- **(b)** Wait until OpenAI publishes the tokenizer.

(a) is recommended: the bound only has to be an upper bound, and any breach is caught and stops paid use.

**Decision (owner, 2026-11-14): (a).** The candidate profile sets `TokenizerIsByteLevel = true`. Task 71 checks it live, and a breach stops paid use. B1 is settled. The model still stays unavailable until B2 is measured, Task 70 passes and the owner approves Task 71.

**B2, framing allowance.** This covers hidden system and formatting tokens around the instructions and input. It isn't published. Task 70 (T70-02) found that one live request can't prove it. A single count below the bound can't separate hidden framing from the slack in the byte estimate. And the first request's liability has to be bounded before that request is sent.

**Decision (owner, 2026-11-14): "Use wide safety margins".** This is a scoped policy choice for the Task 71 test, not evidence. Framing is set to 8,192 tokens (16x the earlier 512 estimate). Bytes per output token and the response envelope get generous values (§6). The test limits are sized so the worst case stays under $0.05 per request. The residual risk is accepted: the route might add more than 8,192 hidden tokens, which would make the first request cost more than its reservation. In that case the full reported charge is recorded, the breach flag pauses paid requests, and the value is corrected before any further use. Ordinary use needs its own decision.

## 8. Residuals for Task 70 to weigh

- **R1, the key as a managed string.** `PasswordBox.Password` and the SDK's `ApiKeyCredential` hold the key as an immutable .NET string until garbage collection. This is accepted under ADR-028, because the key also has to sit in Credential Manager. Exploiting it needs code already running as the user.
- **R2/R3, response size values.** These only feed the "fits in 4 MiB" checks for the limits. The reader still enforces the 4 MiB cap strictly, and a larger body is malformed, so the whole reservation is kept and no answer is shown. In stateless mode, reasoning items can carry `encrypted_content` (S6). That counts towards the 4 MiB cap and is never saved or shown.
- **R4, prices change.** The owner re-confirms prices every 24 hours, typed fresh from S2. A charge above the reservation is recorded in full and pauses paid requests.
- **R5, the account's project settings.** `service_tier` is sent explicitly, so a project default of Fast doesn't apply. PC Steward calls only `api.openai.com`. The pages don't say whether a key for a data-residency project is refused there or billed with the 10% uplift. **Fixed for T70-03:** the owner must now tick that the confirmed prices are the ones their key's project pays, including any data-residency, regional or other uplift, before prices are saved. The Task 71 test also uses a key from a project without data residency.
- **R6, explicit mode is newer API surface.** If the API refused `prompt_cache_options`, the provider's error carries no usage. The request is then recorded as uncertain and its whole reservation counted, so nothing is under-counted.

## 9. SDK and transport (stage 3, unchanged)

- OpenAI .NET 2.15.0, approved by the owner. Its Responses client is marked for evaluation (`OPENAI001`); that warning is suppressed only in `OpenAiResponsesTransport`.
- `ClientRetryPolicy(maxRetries: 0)`: no SDK retries.
- `AllowAutoRedirect = false`.
- 10 s connect and 45 s overall deadlines.
- One request at a time across processes (the dispatch claim).

Evidence for all of this is in `AdvisoryAiAdapterBoundaryTests` and the transport tests.

## 10. Task 71 packet: one tiny paid test (needs the owner's separate approval)

Task 70 did not accept the earlier plan (§7 of its report). This version answers the seven items it listed. It is still a plan: nothing here is approved, built or enabled.

1. **Profile and B1/B2.** The §6 candidate, with B1 option (a) and the B2 wide margins as recorded in §7 and in ADR-028 addendum A2. Prices are entered for the key's own project and the project-prices statement is ticked (T70-03). The key comes from a project without data residency. It should be a throwaway key from a separate OpenAI project with a small prepaid balance, or the lowest budget limit OpenAI offers, and it is deleted at OpenAI after the test. This is a backstop that doesn't depend on PC Steward's own ledger.
2. **Pinned test build.** A separate test build with `Reviewed = [gpt-6.1-sol]` (exactly the §6 values) and `PaidRequests = FirstLiveTest`. Its source SHA, artifact SHA-256 and manifest, and SDK closure (OpenAI 2.15.0 per `scripts/release-toolchain-pins.json`) are recorded before approval is sought. That build gets its own independent review. A pass for the disabled commit doesn't cover it. Because it has paid requests switched on, the test build is never published through the release or in-app updater channel.
3. **Synthetic evidence only.** In the test build, open Explain on one Diagnostics record. Tick "Leave this out" on every item but one. Replace that item's text with exactly: `Contoso Editor 4.2 stopped responding after an update from 4.1. Error code 0x00000ABC appeared at start-up.` The exact request screen then shows everything that would be sent. Besides the synthetic text, that includes the chosen record's time window and the count of left-out items, which are real metadata. The owner checks the screen and cancels if anything else from the PC appears.
4. **Key, then one send, each separately authorized.** The owner types the key into the masked box only after approving that step. The key never appears in chat or in a log. Before the send, the owner confirms the current prices and the limits page's first-live-test figures. Settled and uncertain test commitments must both be $0.00 on the current ledger, which also counts months that were compacted and checks them against their requests (T70B-01). A reset, a restored backup or a no-prior-use declaration never grants new test allowance: the owner accounts separately for any earlier test use that the ledger might not know about.
5. **Evidence capture.** After the send, the owner reads the outcome screen and notes what it says: sent once or not, the stop reason if any, the charge or "kept as uncertain", and the reported counts: input (cached), output (reasoning), total, and the cache-write figure or "no cache-write figure" (T70-07). The reply-details line gives the HTTP status number, the service tier the reply named (or "no service tier reported"), the fixed reason the reply wasn't trusted, if any, and anything that departed from the request. These are numbers and fixed words, never provider text. Nothing else is kept: no body, body hash, header, key, provider error text or answer text. The owner may compare the usage page of their OpenAI account later; that comparison is outside corroboration, not proof of the invoice.
6. **One request, then stop.** Limits: input 12,288 tokens, output 2,048 tokens, $0.05 per request, $1.00 per month. Worst case at the listed Standard prices: 12,288 × $2.00/M = $0.024576 plus 2,048 × $10.00/M = $0.020480, a **reservation of $0.045056** ($0.049562 with a 10% uplift). The software pauses paid requests by itself for usage above the bounds, any reported cache write (also in a body refused for another reason, T70B-02), or a charge above the reservation. It also pauses for a reply naming another service tier, an unknown usage counter, cached input, or background or conversation state. **An ordinary timeout or other uncertain outcome keeps the whole reservation but does not pause paid requests** (T70-07), so stopping is the owner's procedure. After the one send, whatever the outcome, the owner sets the limit per request to $0 (which turns paid requests off). Then the owner reports. Any retry is a new task with a new approval.
7. **No promotion.** No success, month change, reconnect or limit change ends first-live-test mode. Ordinary use is a separate owner decision. It also needs the measurements still open in [Task 90](TASK_90_TASK_70_AI_REMEDIATION.md) §2.

## 11. What this report does not claim

- It makes no host or provider check: nothing was sent to OpenAI.
- Every figure comes from the official pages on the date above.
- The owner chose B1 option (a) and the B2 wide margins on 2026-11-14. The go-ahead for the paid call is still the owner's decision.
- Task 70 failed at `db4bc76`, `669c8c7` and `b11da9b`, and passed at `14ef51b` (70D). An independent review at `14ef51b` failed on one Medium finding, fixed in [Task 90](TASK_90_TASK_70_AI_REMEDIATION.md) §6. Both reviews are re-run at the fixed commit.
