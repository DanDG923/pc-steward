# Task 69: evidence for GPT-6.1 Sol (readiness report)

**Status (2026-11-14): evidence gathered, product fixes made, two blockers open.** No paid call, no account query and no key were used. `AdvisoryAiModelProfiles.Reviewed` stays empty and `AdvisoryAiBuildPolicy.PaidRequests` stays `Disabled`. This report is the input to the independent Task 70 review. Neither this report nor Task 70 approves the Task 71 live call: that needs the owner's separate go-ahead.

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
| `TokenizerIsByteLevel` | **unproven: blocker B1** | S1 and S7 are silent |
| `FramingAllowanceTokens` | **unproven: blocker B2** (proposed 512, to be measured) | — |
| `MaximumTokenUtf8Bytes` | Not published. Proposed 128, which covers the longest `o200k_base` vocabulary entries with margin | Residual R2 |
| `ResponseEnvelopeBytes` | Not published. Proposed 262,144 | Residual R3 |

## 7. Blockers (the model stays unavailable until both are settled)

**B1, tokenizer.** The local input bound relies on "tokens ≤ UTF-8 bytes". That holds for any tokenizer in which every token covers at least one input byte, which includes OpenAI's byte-level BPE family (`o200k_base` for `gpt-5*`, S7). OpenAI publishes no tokenizer for `gpt-6.1-sol`. Options for the owner:
- **(a)** Accept the general argument plus the measured check in Task 71. That check compares `usage.input_tokens` against the app's own bound, and a request over the bound already pauses paid requests (above approved bounds).
- **(b)** Wait until OpenAI publishes the tokenizer.

(a) is recommended: the bound only has to be an upper bound, and any breach is caught and stops paid use.

**B2, framing allowance.** This covers hidden system and formatting tokens around the instructions and input. It isn't published. The proposal is 512 tokens. Task 71's single request measures it: the reported `input_tokens` must be at most the preview's `InputTokenUpperBound`. A breach stops paid use, and the value is corrected before ordinary use.

## 8. Residuals for Task 70 to weigh

- **R1, the key as a managed string.** `PasswordBox.Password` and the SDK's `ApiKeyCredential` hold the key as an immutable .NET string until garbage collection. This is accepted under ADR-028, because the key also has to sit in Credential Manager. Exploiting it needs code already running as the user.
- **R2/R3, response size values.** These only feed the "fits in 4 MiB" checks for the limits. The reader still enforces the 4 MiB cap strictly, and a larger body is malformed, so the whole reservation is kept and no answer is shown. In stateless mode, reasoning items can carry `encrypted_content` (S6). That counts towards the 4 MiB cap and is never saved or shown.
- **R4, prices change.** The owner re-confirms prices every 24 hours, typed fresh from S2. A charge above the reservation is recorded in full and pauses paid requests.
- **R5, the account's project settings.** `service_tier` is sent explicitly, so a project default of Fast doesn't apply. PC Steward calls only `api.openai.com`. The pages don't say whether a key for a data-residency project is refused there or billed with the 10% uplift, so the owner should use a key from a project without data residency (to be added to the Connect disclosure before ordinary use).
- **R6, explicit mode is newer API surface.** If the API refused `prompt_cache_options`, the provider's error carries no usage. The request is then recorded as uncertain and its whole reservation counted, so nothing is under-counted.

## 9. SDK and transport (stage 3, unchanged)

- OpenAI .NET 2.15.0, approved by the owner. Its Responses client is marked for evaluation (`OPENAI001`); that warning is suppressed only in `OpenAiResponsesTransport`.
- `ClientRetryPolicy(maxRetries: 0)`: no SDK retries.
- `AllowAutoRedirect = false`.
- 10 s connect and 45 s overall deadlines.
- One request at a time across processes (the dispatch claim).

Evidence for all of this is in `AdvisoryAiAdapterBoundaryTests` and the transport tests.

## 10. Task 71 plan: one tiny paid test (needs the owner's separate approval)

- **Build:** a test build with `Reviewed = [gpt-6.1-sol]` (the candidate in §6, with B1/B2 settled by the owner) and `PaidRequests = FirstLiveTest`, only after Task 70 passes.
- **Evidence:** one synthetic Diagnostics record (for example, a made-up "Contoso Editor stopped working" record with a fake code and nearby synthetic PC Steward change). Nothing from the owner's PC is sent.
- **Limits:** input 2,048 tokens, output 2,048 tokens, $0.05 per request, $1.00 per month.
- **Worst case at the listed Standard prices:** input 2,048 × $2.00/M = $0.004096, plus output 2,048 × $10.00/M = $0.020480, gives a **reservation of $0.024576**. The cumulative first-test envelope is $1.00, with $0.00 spent so far.
- **Stop policy:** one request, no retry.
  - Stop and report if anything below happens; any of these (except a malformed body) also pauses paid requests:
    - usage above the preview's bounds (B1/B2 breach);
    - any reported cache write;
    - a malformed body;
    - an uncertain outcome (the whole reservation is counted);
    - a charge above the reservation.
  - Record the reported `input_tokens`, `cached_tokens`, `cache_write_tokens`, `output_tokens` and `reasoning_tokens` against the preview's `InputTokenUpperBound`. Afterwards, the owner checks the usage page of their OpenAI account and reports the figure.
- **Preview:** the owner sees and approves the exact body and headers in the app before sending. Nothing is pre-filled here, because the preview is built at that moment.

## 11. What this report does not claim

- It makes no host or provider check: nothing was sent to OpenAI.
- Every figure comes from the official pages on the date above.
- Choosing B1 option (a) or (b), the Task 71 limits and the go-ahead for the paid call are the owner's decisions.
- Task 70 (the independent security, privacy and cost review) has not run.
