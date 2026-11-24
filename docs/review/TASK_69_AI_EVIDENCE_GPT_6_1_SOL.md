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

## 6. Candidate profile (in `Reviewed` only in the Task 71 test build, §12)

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

`AdvisoryAiTask71CandidateTests` checks `AdvisoryAiFirstLiveTestBuild.Candidate` against these exact values. The profile is coherent. The §10 limits pass every request, response and saved-item byte ceiling, and leave room for evidence. Only the test build lists it in `Reviewed` (§12).

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

Task 70 did not accept the earlier plan (§7 of its report). This version answers the seven items it listed. Nothing here is approved or used. The test build in §12 is the only build that enables it.

1. **Profile and B1/B2.** The §6 candidate, with B1 option (a) and the B2 wide margins as recorded in §7 and in ADR-028 addendum A2. Prices are entered for the key's own project and the project-prices statement is ticked (T70-03). The key comes from a project without data residency. It should be a throwaway key from a separate OpenAI project with a small prepaid balance, or the lowest budget limit OpenAI offers, and it is deleted at OpenAI after the test. This is a backstop that doesn't depend on PC Steward's own ledger.
2. **Pinned test build.** A separate test build with `Reviewed = [gpt-6.1-sol]` (exactly the §6 values) and `PaidRequests = FirstLiveTest`. Its source SHA, artifact SHA-256 and manifest, and SDK closure (OpenAI 2.15.0 per `scripts/release-toolchain-pins.json`) are recorded before approval is sought. That build gets its own independent review. A pass for the disabled commit doesn't cover it. Because it has paid requests switched on, the test build is never published through the release or in-app updater channel. How it is built and run is in §12.
3. **Synthetic evidence only.** In the test build, open Explain on one Diagnostics record. Tick "Leave this out" on every item but one. Replace that item's text with exactly: `Contoso Editor 4.2 stopped responding after an update from 4.1. Error code 0x00000ABC appeared at start-up.` The exact request screen then shows everything that would be sent. Besides the synthetic text, that includes the chosen record's time window and the count of left-out items, which are real metadata. The owner checks the screen and cancels if anything else from the PC appears.
4. **Key, then one send, each separately authorized.** The owner types the key into the masked box only after approving that step. The key never appears in chat or in a log. Before the send, the owner confirms the current prices and the limits page's first-live-test figures. Settled and uncertain test commitments must both be $0.00 on the current ledger, which also counts months that were compacted and checks them against their requests (T70B-01). A reset, a restored backup or a no-prior-use declaration never grants new test allowance: the owner accounts separately for any earlier test use that the ledger might not know about. **A later request (the Task 71C retest, Task 90 §12).** The ledger already holds the earlier request, so before it the limits page shows exactly that and nothing else: in November 2026, spent $0.00, reserved and not settled yet $0.0401, and first live test $0.0401 of $1.00. The earlier pause, recorded before migration 17, shows the general notice; the owner acknowledges only that known event with "I've reviewed this" and checks the amounts didn't change. The owner first checks the provider's usage page shows only the earlier request. Any other amount, another pause cause, a reset or restore, or a new prior-use question stops the procedure. The estimated provider charge is never subtracted, no-prior-use is never declared again, and a new key is never a new allowance. After a clean retest the page shows the retest's own actual charge as spent, $0.0401 still reserved, and their sum as the first-live-test figure. Only a reply with the same usage as the first (491 in, 321 out) gives exactly $0.004192, $0.0401 and $0.044292; a longer or shorter answer gives a different charge, which is correct. If the retest itself is uncertain, its own kept amount is added to the $0.0401 instead. The owner reads these figures in the test build before closing it (Task 71C review 71C-R5).
5. **Evidence capture.** After the send, the owner reads the outcome screen and notes what it says: sent once or not, the stop reason if any, the charge or "kept as uncertain", and the reported counts: input (cached), output (reasoning), total, and the cache-write figure or "no cache-write figure" (T70-07). The reply-details line gives the HTTP status number, the service tier, the fixed reason the reply wasn't trusted, if any, and anything that departed from the request. A tier is shown by name only when it is one PC Steward knows, as its own fixed word. Anything else shows as "a service tier PC Steward doesn't recognise", "more than one service tier" or "no service tier reported" (T70E-01). These are numbers and fixed words, never provider text. The reader is strict about the reply's shape, and a small surprise ends the test as uncertain: the whole reservation is kept and the reply-details line says why. Only a departure from the priced request also pauses paid requests automatically, for example `"background": null` instead of false, an unknown usage counter or another service tier. Since Task 90 §29 (AG1), so does anything the request never allowed: another model (a date suffix included), a stored response, tools, a status PC Steward doesn't support, a tool call item, a message part that isn't text, or (Task 90 §30) a citation in the answer. Since Task 90 §31 (AJ1), so does a reply too large to read, or one that isn't strict JSON but names usage or output. Other surprises keep the reservation without an automatic pause, for example a reply with no id, an error envelope, or a gateway's error page. Either way the owner stops as item 6 says (T70F-01). Nothing else is kept: no body, body hash, header, key, provider error text or answer text. The owner may compare the usage page of their OpenAI account later; that comparison is outside corroboration, not proof of the invoice.
6. **One request, then stop.** Limits: input 12,288 tokens, output 2,048 tokens, $0.05 per request, $1.00 per month. Worst case at the listed Standard prices: 12,288 × $2.00/M = $0.024576 plus 2,048 × $10.00/M = $0.020480, a **reservation of $0.045056** ($0.049562 with a 10% uplift). The software pauses paid requests by itself for usage above the bounds, any reported cache write (also in a body refused for another reason, T70B-02), or a charge above the reservation. It also pauses for a reply naming another service tier, an unknown usage counter, cached input, or background or conversation state. **An ordinary timeout or other uncertain outcome keeps the whole reservation but does not pause paid requests** (T70-07), so stopping is the owner's procedure. After the one send, whatever the outcome, the owner sets the limit per request to $0 (which turns paid requests off). Then the owner reports. Any retry is a new task with a new approval.
7. **No promotion.** No success, month change, reconnect or limit change ends first-live-test mode. Ordinary use is a separate owner decision. It also needs the measurements still open in [Task 90](TASK_90_TASK_70_AI_REMEDIATION.md) §2.

## 11. What this report does not claim

- It makes no host or provider check: nothing was sent to OpenAI.
- Every figure comes from the official pages on the date above.
- The owner chose B1 option (a) and the B2 wide margins on 2026-11-14. The go-ahead for the paid call is still the owner's decision.
- Task 70 failed at `db4bc76`, `669c8c7` and `b11da9b`, and passed at `14ef51b` (70D). An independent review at `14ef51b` failed on one Medium finding, fixed in [Task 90](TASK_90_TASK_70_AI_REMEDIATION.md) §6. Both reviews are re-run at the fixed commit.

## 12. The Task 71 test build

Built for the owner's go-ahead of 2026-11-14 to build the one-request test build and have it reviewed. That go-ahead covers no key and no paid request.

**How it differs from a release.** The MSBuild property `PCStewardAiFirstLiveTest=true` defines `PCSTEWARD_AI_FIRST_LIVE_TEST`. Only `scripts/New-AdvisoryAiFirstLiveTestBuild.ps1` sets it. With the symbol, and only then:

- `AdvisoryAiModelProfiles.Reviewed` is `[AdvisoryAiFirstLiveTestBuild.Candidate]`, the §6 profile.
- `AdvisoryAiBuildPolicy.PaidRequests` is `FirstLiveTest`, so every request stays inside the cumulative $1 envelope.
- `PCSteward.Domain.dll` carries the type `AdvisoryAiFirstLiveTestBuildMark`.

Every other build compiles the candidate as inert data that nothing offers. `Reviewed` stays empty there, and paid requests stay disabled.

**Kept off the release path.**

- The release candidate builder (`New-OwnerOnlyCandidate.ps1`, which builds the release payload) refuses any payload whose `PCSteward.Domain.dll` carries the mark. That also catches the property arriving from outside the script. MSBuild reads it from an environment variable or a `Directory.Build.rsp` as well, and the Task 71 review showed both turn an ordinary build into a marked test build. So "only the script sets it" is a rule for the checked-in files, and the mark check on the release path is what enforces it.
- The release workflows never name the script or the property, which an architecture test checks.
- The test build has no MSI, release check, signature or updater entry.

**Built on demand.** The "AI first live test build" workflow runs only when started by hand, only from `phase/4-beta`, with a read-only token and no secrets. It:

- runs the AI suites with the property on;
- builds the desktop app the way the launch smoke does (framework-dependent .NET, with the Windows App SDK in the folder);
- checks that the mark is present and that no elevated helper is in the folder;
- writes `ai-first-live-test-manifest.json`, which records the source commit, the run, the OpenAI package version and every file's size and SHA-256, hidden and system files included;
- checks that the upload folder holds exactly those files and the manifest;
- uploads the artifact for 7 days, hidden files included.

A CI job builds the same thing on every pull request and uploads nothing.

**What the test build refuses.** It runs from a folder the user can write to, and it shares the installed copy's data, so:

- It never prepares, approves or starts an app update, WinGet or Chocolatey. It also composes no elevated runner.
- It never saves or registers the scheduled check, so the Windows task keeps starting the installed copy.
- It never arms a factory reset, which would delete the AI ledger the test is measured against.
- It never finishes or rechecks a reset that the installed copy started (Task 71 review T71-01). If any reset state is present (pending, partial, part-way through, waiting for first-run setup, or unreadable), it stops at start-up. It does that before any scheduled-task or Credential Manager step, before the database is opened or migrated and before a fresh store is made, and it shows "A factory reset is still pending". `AdvisoryAiFirstLiveTestStartupResetTests` proves this for every state, with a populated ledger and counted callbacks.
- It clears none of the shared data: scan data, downloads, source metadata, logs, update history, or mappings and policies (T71-02).
- It never checks for, downloads or installs a PC Steward update, and it never reads or clears the installed copy's update record.

Its title and title bar say "PC Steward — AI test build, not a release".

**What still works, and must not be used during the test.** A normal start still creates or migrates the shared database, removes saved AI explanations older than 30 days and reconciles an interrupted update. Scanning and refreshing, connector consent, matching decisions (mappings, pins and ignores), diagnostic collection and its source settings, and the exports and copy actions all still work against the installed copy's data. None of them gains send authority or gets past the update refusal, but they do change shared state. The test uses only the AI set-up, the one Explain and the steps in §10.

**The software's bound and the procedure's.** "One request" and the Task 71 limits are the owner's procedure. The software enforces the cumulative $1 envelope, the per-request and monthly caps, one request in flight, and no retry. It does not stop a second, separately approved request inside those limits. That is why §10 item 6 sets the per-request cap to $0 after the one attempt, whatever its result.

**What it shares with the installed copy.** It uses the same data folder, database and Credential Manager entry as the installed PC Steward. The test is therefore recorded in the same ledger, and any earlier use shows up there. Build it from a `phase/4-beta` commit whose database migrations match the installed release: 0.1.17 has migrations 1 to 15, and 0.1.18 has 1 to 17. On an older installed copy, the test build would migrate the shared database past it, and that copy would then refuse it until it was updated. So step 3 checks the installed version. Deleting the extracted folder removes neither the database nor the key entry.

**Owner's steps, once the test build's review has passed.** Each one is separate from the key step and from the send.

1. Download the artifact from the run that the passing review names. Don't start the workflow again: a new run makes a new artifact and a new manifest, and no review covers them. If the artifact has expired (after 7 days), a new run from the same commit keeps the code review valid, but its artifact has to be checked again (Task 71 review F2).
2. Check that the download's SHA-256 matches the digest the review recorded. Extract it to a new folder in your user profile. Then check the folder against the manifest with the check below, using the manifest SHA-256 from the review, not the copy in the folder. The check must print that the folder matches. Windows SmartScreen may warn about the unsigned program. Go on only after the check passes.
3. In the installed PC Steward, check that About shows the release the test build was made for (0.1.17 for Task 71, 0.1.18 for the Task 71C retest). Start it and close it normally once, so that no reset is pending. Then start `PCSteward.Desktop.exe` from the extracted folder, and check that the title names the AI test build. If it shows "A factory reset is still pending", close it and stop.
4. Follow items 3 to 7 above. Before closing the test build, select Disconnect in its AI settings and check that it says the key is gone. That removes the key from Credential Manager and keeps the ledger. Then delete the key at the provider. Close the test build and delete the folder. Start the installed PC Steward again. It keeps the same ledger and sends nothing, but in a release AI explanations show only "Not available in this version", so the spending figures can't be read there: read them in the test build first (Task 71C review 71C-R5).

The folder check (PowerShell, no administrator rights, reads only):

```powershell
$folder = '<the extracted folder>'
$reviewedManifest = '<manifest SHA-256 from the review>'
$manifestPath = Join-Path $folder 'ai-first-live-test-manifest.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $reviewedManifest) { throw 'The manifest is not the reviewed one.' }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$app = Join-Path $folder 'app'
$listed = @{}; foreach ($entry in $manifest.files) { $listed[$entry.path] = $entry }
$actual = @(Get-ChildItem -LiteralPath $app -Recurse -File -Force)
foreach ($file in $actual) {
    $path = $file.FullName.Substring($app.Length + 1).Replace('\', '/')
    $entry = $listed[$path]
    if ($null -eq $entry) { throw "Not in the manifest: $path" }
    if ($entry.bytes -ne $file.Length -or $entry.sha256 -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Changed: $path" }
}
if ($actual.Count -ne @($manifest.files).Count) { throw 'A file in the manifest is missing.' }
if (@(Get-ChildItem -LiteralPath $folder -Force).Count -ne 2) { throw 'The folder holds more than app and the manifest.' }
'The folder matches the reviewed manifest.'
```

**Accepted residuals.** `New-PrivateBetaPackage.ps1`, which is not on the release path, has no mark check (Task 71 review F8). Code already running as the owner could replace files in the extracted folder before it starts. That code could also read the key, so this needs a PC that is already compromised (the owner's rule of 2026-11-12). The workflow digest and the manifest let the owner check the folder before starting it. The test build cannot ask Windows for administrator rights.
