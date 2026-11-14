# Phase G — Advisory AI: staged build plan and interfaces (Tasks 67–71)

**Status:** In progress (stages 1–3 of 6 built; stage 4a built). The owner started Phase G on 2026-11-13 after Phase F's timeline stages merged, keeping the agreed order (Phases D installs and E removals stay paused by the owner). ADR-027, ADR-028 and ADR-029 are Accepted; nothing here widens them. OpenAI is the first provider (D2).

**What stays true at every stage:** AI is off by default and optional; every core feature works without a key. Nothing is sent without the owner approving an exact preview of that request. No model is chosen from memory: a model becomes usable only with the Task 69 evidence for it. Paid requests stay disabled by build policy until the Task 70 review passes, and the first live call (Task 71) needs the owner's separate approval inside the cumulative $1 envelope.

## Stages

| Stage | Task | Scope | Network / host |
|---|---|---|---|
| 1 | 67 | Domain and Application core: limits and checked micro-dollars, coupled token/byte bounds, redaction, editable evidence drafts, the canonical request body and header preview, approvals, strict response reading; key holder and the credential/transport ports | None |
| 2 | 68 | Budget: tariff (owner-confirmed, 24-hour), worst-case reservation arithmetic, Reserved → DispatchClaimed → Settled/Uncertain, UTC months and clock checks, SQLite migration 14 (settings, tariffs, ledger, saved explanations), clears and factory reset, one request at a time across processes | None |
| 3 | 67 | Adapters: the official OpenAI .NET SDK behind `IAdvisoryAiTransport` (exact approved bytes through the protocol method, zero retries, no redirects, 10 s connect / 45 s overall, closed header profile, bounded response read), and the exact Credential Manager entry behind `IAdvisoryAiCredentialStore`. Tested with a counting mock transport and a fake credential API only | None (no real key or call) |
| 4 | 67/68 | Desktop: an "AI explanations" section in Settings (connect with disclosure and prior-use question, disconnect, AI limits, AI pricing), "Explain with AI…" on a Diagnostics incident and on a failed update, the preview with edit/remove and the exact body and headers, the result with its cost status | None |
| 5 | 69/70 | Evidence report (exact model, SDK, tokenizer and framing evidence, prices from official pages, measured sizes) and the independent security, privacy and cost review | None |
| 6 | 71 | One preview-approved live explanation of synthetic evidence within $1, only with the owner's exact approval and the owner's own key | Owner-approved |

## Stage 1 interfaces (implemented)

Domain (pure: no network, credential, file, registry or process access; pinned by `AdvisoryAiBoundaryTests`):

- `AdvisoryAiPolicy.cs`
  - `MicroUsd` — checked fixed-point USD (1,000,000 per dollar). `TryParseDollars` accepts only plain digits with at most six decimals; anything more precise, signed, grouped or too large is refused, never rounded.
  - `AdvisoryAiLimits` — the four editable limits; `Defaults` = 8,192 input tokens, 1,024 output tokens, $0.25 per request, $200 per month. Either money value at zero disables paid requests and keeps the other.
  - `AdvisoryAiResourcePolicy` — the accepted v1 maxima: 4 MiB request, response and saved item; 8 MiB saved content; 45 s overall and 10 s connect; 30-day saved explanations.
  - `AdvisoryAiModelProfile` — one exact model on the one route: context window, output maximum, framing allowance, byte-level tokenizer flag, longest token in bytes, response envelope, service tier. `RequestBytes`, `ResponseBytes` and `SavedItemBytes` are the ADR-028 F1 coupled functions, computed with checked arithmetic and assuming every text byte needs the longest JSON escape (six bytes).
  - `AdvisoryAiModelProfiles.Reviewed` — **empty**. `AdvisoryAiBuildPolicy.PaidRequests` — **Disabled**.
  - `AdvisoryAiLimitValidation.Validate(limits, model?, instructionTokens)` — money rules always; with a model, context, output maximum and all three byte bounds, so a larger token setting is admitted only when every coupled allowance fits (no cosmetic increase defeated by a smaller cap). `Explain` gives the binding limit in words.
- `AdvisoryAiRedaction.cs` — `SanitizeEvidence` runs the Phase F `DiagnosticTextSanitizer` (control/format/bidi characters, paths, links, SIDs, emails, the PC's account, domain and computer names) and then the classes ADR-028 adds: bearer tokens, `name=value` secrets (passwords, tokens, API and licence keys, serial numbers), well-known key prefixes, JSON web tokens, product keys, device instance paths, GUIDs, hardware addresses and long hexadecimal or base64 runs. It is idempotent. `SanitizeAnswer` does the same line by line so an answer keeps its paragraphs; links become `[link]`.
- `AdvisoryAiEvidence.cs`
  - `AdvisoryAiSourceKind` (FailedUpdate, DiagnosticTimeline) — decides which clear removes a saved explanation (ADR-027 F5).
  - `AdvisoryAiEvidenceDraft` — redacted items with a closed role, the interval, what was left out, the app-local origin (never sent) and a version. `TryEdit` redacts the owner's edit again and keeps the role; `TryRemove` keeps at least one item. Every change raises the version.
  - `AdvisoryAiInputAllowance` — input text bytes allowed = input token limit − framing − fixed instructions.
  - `AdvisoryAiDiagnosticDraftBuilder.TryBuild(timeline, incident, allowance, …)` — ADR-028 R4 order: the incident and a coverage summary of every source (required), then PC Steward changes within ±15 minutes, then other records within ±15 minutes in frozen timeline order; takes items until the next does not fit and counts the rest by role. Linear in the number of nearby records. No draft when the required items do not fit or the incident has no known time.
  - `AdvisoryAiFailedUpdateDraftBuilder.TryBuild(jobId, facts, …)` — a job summary, then that job's own recorded events in time order.
- `AdvisoryAiRequest.cs`
  - `AdvisoryAiRoute` — `POST https://api.openai.com/v1/responses`, compiled once (since stage 3: `AdvisoryAiProviders.OpenAI`, see the provider and model registry below).
  - `AdvisoryAiInstructions` (version 1) — evidence is quoted untrusted data; facts before possibilities; say what is uncertain or missing; no commands, scripts, registry edits, links or packages to run.
  - `AdvisoryAiInputText` — the one canonical JSON text of a draft that is the request's `input`.
  - `AdvisoryAiRequestBuilder.TryBuild(draft, model, limits)` — the exact body: `model`, `instructions`, `input`, `max_output_tokens`, `store: false`, `truncation: "disabled"`, `service_tier`. No tools, conversation, previous response, background, streaming, metadata or user field. The input token bound is the UTF-8 bytes of instructions and input plus the framing allowance (byte-level BPE: every token covers at least one byte). A draft that no longer fits is blocked, never shortened.
  - `AdvisoryAiHeaderProfile` — the closed header list the preview shows; the authorization value is a fixed placeholder.
  - `AdvisoryAiApproval` — memory only; bound to the exact body, model profile, limits, draft version and origin, and to the settings, consent, credential and tariff versions. `IsStillValid` fails on any change; `MatchesWireBody` compares the bytes a transport is about to send.
- `AdvisoryAiResponse.cs` — `AdvisoryAiResponseReader.Read(body, approval)`: at most the admitted response bytes and 16 levels deep; must be a `response` object for the approved model with a valid ID, not stored and without tools; only `message` (output text or refusal) and `reasoning` items, anything else makes the body untrusted. Status, incomplete reason, text and usage are read independently: usage is reliable only when every count is present, whole, non-negative and consistent, and usage above the approved bounds is flagged for the ledger.

Application (`src/PCSteward.Application/AdvisoryAi.cs`):

- `AdvisoryAiApiKey` — non-empty, no whitespace, printable ASCII, at most 2,048 bytes; never in text (`ToString` is a placeholder); cleared on dispose.
- `IAdvisoryAiCredentialStore` — the exact target `PCSteward/OpenAI/ApiKey.v1`: save, presence check (deliberate connect flow only), read for dispatch, remove and verify absence (Removed, AlreadyAbsent, Incomplete).
- `IAdvisoryAiTransport.SendOnceAsync(approval, key, …)` → `AdvisoryAiTransportResult` (closed outcome, HTTP status, complete body only; no headers, exception text or key).
- `AdvisoryAiFailedUpdateEvidence` — which jobs can be explained (failed, timed out, inconclusive) and their facts from that job's own recorded events.

Tests: `AdvisoryAiPolicyTests`, `AdvisoryAiRedactionTests`, `AdvisoryAiRequestTests`, `AdvisoryAiResponseTests` (Domain), `AdvisoryAiApplicationTests` (Application), `AdvisoryAiBoundaryTests` (architecture).

## Stage 2 interfaces (implemented)

Domain (`src/PCSteward.Domain/AdvisoryAiBudget.cs`, pure):

- `AdvisoryAiTariff` — one owner-confirmed rate card (input, cached input, output per million tokens) for one exact model and tier; current only in `[confirmed, confirmed + 24 h)`; `Validate` refuses a zero rate, another model or tier, or a version below 1.
- `AdvisoryAiCost` — `Charge` = ceil(tokens × rate ÷ 1,000,000) in 128-bit checked arithmetic; `Reservation` = every input token at the higher of the two input rates plus every allowed output token; `Actual` from reliable usage (reasoning is part of output, never added twice).
- `AdvisoryAiPeriod` — the UTC calendar month, with a checked stored key.
- `AdvisoryAiRequestState` and `AdvisoryAiRequestStates` — Reserved → NotSent or DispatchClaimed; DispatchClaimed → Settled or Uncertain; nothing else.
- `AdvisoryAiLedgerSnapshot` and `AdvisoryAiReservationRule.Check` — in order: build policy, zero limits, a request already in flight, a review block, a full ledger, continuity (not established, prior use unknown, and unknown first-live-test spending after a fresh month), the clock (moved backwards, or a new month not yet confirmed), the tariff, then `R ≤ request cap`, `settled + outstanding + R ≤ monthly budget` and, in first-live-test mode, `test settled + test outstanding + R ≤ $1`. Exactly at a cap passes; one micro-dollar over does not.
- `AdvisoryAiClock` — backwards, same month, or a new month needing confirmation; and the 24-hour forward-jump test against monotonic time (wired in stage 4, where the app has a monotonic clock).
- `AdvisoryAiSettlementRule.Decide` — only a well-formed response with reliable usage settles, at the frozen rates; everything else keeps the whole reservation with a typed reason. A charge above the reservation, usage above the approved bounds, or unreliable usage that would cost more is recorded in full and stops paid requests for review.

Application:

- `AdvisoryAiAccounting.cs` — `IAdvisoryAiSettingsStore` (settings with separate settings, consent and credential versions; immutable tariff versions), `IAdvisoryAiLedger` (reserve, claim, release, settle, recovery, continuity answer, new-month confirmation, clock acceptance, review acknowledgement, spending summary) and `IAdvisoryAiExplanationStore`.
- `AdvisoryAiRequestWorkflow.SendAsync` — the only order: connected → reviewed model → confirmed prices → approval still matches a fresh preview and the current versions → reserve → read the key → claim → send once → read strictly → settle → redact the answer → save it. A refusal before the claim costs nothing; no failure before the claim reaches the transport; after the claim, ledger writes ignore cancellation and a transport exception is recorded as uncertain. The caller runs it inside the exclusive data-operation lease after recovery.

SQLite (`src/PCSteward.Persistence.Sqlite/SqliteAdvisoryAi.cs`, migration 14): `ai_settings`, `ai_accounting_state`, `ai_tariffs`, `ai_requests`, `ai_explanations`, each with CHECK constraints. Every ledger change is one immediate (write-locked) transaction, so the commitment check and the insert cannot interleave across processes. Rows read back are validated; a bad row blocks paid requests rather than granting them. Clear scan data removes diagnostic-timeline explanations, Clear update history removes failed-update explanations, neither touches the ledger, and factory reset deletes the database. Explanations: 30 days (expired ones are removed at start-up, before each listing and before each save), 4 MiB each, 8 MiB in total (oldest go first), redacted again when read.

Tests: `AdvisoryAiBudgetTests` (Domain); `AdvisoryAiLedgerTests` (the SQLite store, including eight concurrent reservations from separate connections, and the workflow end to end over it with a counting fake transport and fake credentials).

Left for later stages, by design: the 12-month compaction of settled rows (the 10,000-row ceiling blocks new requests first, and a full row is well under 1 KiB, so the 16 MiB logical ceiling cannot be reached before it); the monotonic forward-jump check and the owner screens for the continuity question, a new month, a clock review and a breach review (stage 4); deleting the credential on factory reset (stages 3–4).

## Stage 3 interfaces (implemented)

Project `src/PCSteward.AdvisoryAi.Windows` (references Application and the `OpenAI` package only; composed by the desktop app in stage 4; pinned by `AdvisoryAiAdapterBoundaryTests`). The owner approved the dependency on 2026-11-13: OpenAI 2.15.0 with System.ClientModel 1.16.0, both in `Directory.Packages.props`.

- `OpenAiResponsesTransport : IAdvisoryAiTransport` — `ResponsesClient.CreateResponseAsync(BinaryContent, RequestOptions)` (the protocol method) with the approval's exact bytes; `ClientRetryPolicy(maxRetries: 0)`, logging and distributed tracing off, `BufferResponse = false`, errors returned rather than thrown. The network handler is a `SocketsHttpHandler` with redirects off, a 10-second connect timeout, no cookies, credentials or decompression, and the system's certificate validation and proxy. One linked 45-second deadline covers the attempt and the response read.
  - A guard (the last handler before the network) refuses a second attempt, any method or address other than OpenAI's registered route, an approval for another provider's model, a body that is not byte-identical to the approval and an authorization that is not the given key; nothing is sent and the result is `NotSent`. It then sends only `Authorization`, `Accept`, `User-Agent` (`PCSteward-AdvisoryAI/1`), `Content-Type` and `Content-Length`, plus the framing the HTTP stack adds (Host).
  - A 3xx becomes `Redirected` inside the guard, before the SDK sees it: System.ClientModel 1.16's response handling follows some redirects itself (found by the redirect tests), whatever the HTTP handler is set to.
  - Responses are read through a bounded stream at `AdvisoryAiResponseReader.AdmittedBytes`, so neither this class nor the SDK buffers more, error responses included; a longer body is `ResponseTooLarge`.
  - Failures map to closed outcomes without text: nothing handed to the network is `NotSent`; then caller cancellation, deadline, connect/TLS/proxy failure and anything else.
- `WindowsCredentialManagerStore : IAdvisoryAiCredentialStore` — built for one registered provider; CredWriteW, CredReadW, CredDeleteW and CredFree for that provider's one generic target with local-machine persistence and no user name; the key is stored as its ASCII bytes (2,048 at most, inside Credential Manager's 2,560-byte blob limit; UTF-16 would not fit). Save reads back; presence is Absent only on "not found"; a stored value that is empty, oversized or not a valid key is never used; removal is Removed or AlreadyAbsent only when a fresh read returns "not found", otherwise Incomplete. Buffers are cleared after use.

Tests: `AdvisoryAiAdapterTests` (Application.IntegrationTests): the exact bytes and closed headers on the wire, one attempt for 408/429/5xx, redirects not followed even to the same host, the handler settings, the admitted-size bound with and without a declared length (error bodies too), connect/TLS/other failures, cancellation before and during sending, the overall deadline, and the credential save/read/presence/removal paths with the documented Win32 errors. `AdvisoryAiAdapterBoundaryTests` (architecture) pins the references, the four native calls and the transport settings.

Recorded for Tasks 69/70: OpenAI 2.15.0 marks the Responses client "for evaluation" (OPENAI001, suppressed in the one file that uses it). The SDK takes the key as a string, so a managed copy can't be cleared. The real Credential Manager calls are not run in CI; they are first exercised on the owner's PC.

## Stage 4 split (Desktop)

- **4a (implemented).** Application: `AdvisoryAiAvailability` (the first coherent reviewed model and the build policy; one model, no picker), `AdvisoryAiSetupService` (load without any credential access; connect with the prior-use question; disconnect; retry removal; replace key; save limits over the shown settings version; confirm prices; confirm month, accept clock, acknowledge review), the exact limits and prices forms, and `AdvisoryAiSettingsPresenter` with `AdvisoryAiSetupText`. Desktop: the "AI explanations" Settings section, which in this build only says it is not available.
- **4b.** Desktop dialogs for connect (disclosure, prior-use question, masked key box), disconnect, replace key, limits, prices and the notices; composing `PCSteward.AdvisoryAi.Windows` (with the release toolchain pins and third-party notices); the exact key's removal and verification in factory reset.
- **4c.** "Explain with AI…" on a Diagnostics incident and a failed update: the draft with edit and remove, the exact body and headers, approval, the send through `AdvisoryAiRequestWorkflow`, the result with its cost status, and saved answers.
- **Recorded for review.** A "credential removal incomplete" result is shown for the rest of the session only; after a restart the section says "Not connected", because ordinary start-up must not probe Credential Manager (ADR-028). Disconnect and factory reset can be run again to verify removal.

## Provider and model registry (ADR-028 addendum A1, implemented in stage 3)

The owner asked on 2026-11-14 for one model now (GPT-6.1 Sol) and a framework that makes choosing a model, or adding Claude's API, seamless later. Stage 3 therefore makes the path provider-neutral without adding a provider, a model or a picker:

- **Providers** (`src/PCSteward.Domain/AdvisoryAiProviders.cs`): `AdvisoryAiProviderId` (stored values; only `OpenAI = 1`), and `AdvisoryAiProvider` with the compiled endpoint, the exact Credential Manager target (`PCSteward/<Provider>/ApiKey.v1`), the authorization header name and its preview placeholder, any further fixed headers, an `IAdvisoryAiWireFormat` (request body writer and strict response reader), and (from stage 4a) the provider's data-controls and pricing pages, which set-up links to. `AdvisoryAiProviders.All` is the registry; the only origin string in `src` is there.
- **Models**: each `AdvisoryAiModelProfile` is one registry entry. It names its `Provider` and its `ReasoningEffort` (sent as `reasoning.effort` when set; null sends nothing) besides its evidenced limits and tokenizer facts, and its prices are its own tariff (model ID and tier). A profile whose provider is not registered, or whose effort is not a short lower-case word, is not coherent and never previews.
- **Ports**: `IAdvisoryAiCredentialStore` and `IAdvisoryAiTransport` each name their provider. `AdvisoryAiProviderAdapters` holds one key store and transport per provider (a pair for two providers, or two pairs for one, is refused). The workflow picks the pair for the approved model's provider and reads the answer with that provider's wire format; a model whose provider has no pair is refused (`ProviderUnavailable`) before anything is reserved or read. The OpenAI transport refuses (`NotSent`) any other provider's approval.
- **This build**: OpenAI only; `AdvisoryAiModelProfiles.Reviewed` stays empty until Task 69/70, then holds only `gpt-6.1-sol`. No picker.

What a later picker or provider still needs (not built now):
- Settings: a stored selected model (a later migration) offered only from `Reviewed`, and per-provider connect, consent version and key generation (today one consent and one key generation, which is correct with one provider). The tariff and ledger key by model ID; a provider column comes with the picker if two providers could share a model ID.
- A new provider (for example Anthropic): its own reviewed ADR amendment (privacy and retention disclosure, route, headers, any SDK), owner approval of its SDK, a wire format and transport proven by the same counting-mock tests, Task 69 evidence per model and the Task 70 review. The registry removes rewriting, not those reviews.

## Decisions taken inside the accepted ADRs (recorded for review)

- **Token bound.** ADR-028 F7 needs a verified tokenizer or a separately evidenced conservative upper bound. Stage 1 uses the byte-level BPE property (tokens ≤ UTF-8 bytes) plus a per-model framing allowance; a profile must assert the tokenizer is byte-level, and Task 69 must evidence that and the framing allowance for the exact model. A plain byte count with a guessed margin is not used.
- **Response bound.** The Responses object can echo the instructions, so `ResponseBytes` counts the input allowance as well as the output (ADR-028 F1 names only the output; this is stricter).
- **Request fields.** `truncation: "disabled"` (the provider must not silently drop input) and an explicit `service_tier` (the tariff states that tier's prices) are added to the minimal body. Task 69 checks both against the current API.
- **A transport "not sent" after the claim is still uncertain.** ADR-029 allows NotSent only with transactional proof that no claim was committed; the workflow checks everything it can before the claim, so a transport that later reports nothing was sent leaves the request Uncertain (reason `NotSentAfterClaim`).
- **Input rounding.** The actual cost prices uncached and cached input together and rounds up once, as the reservation does, so usage within the approved bounds can never exceed the reservation by a rounding micro-dollar (Codex review of #111).
- **The approval is rechecked in the ledger's own transactions.** Reserve and claim each read the stored settings, consent, key generation, limits and newest prices inside their write transaction and refuse (`ApprovalOutdated`, `Outdated`) if anything changed, AI was disconnected or the prices' 24-hour confirmation has run out since the approval, so a limit lowered in another window can't be raced (Codex review of #111).
- **Expired explanations are removed, not only hidden.** The app removes them at every start and before each listing, as well as before each save, so the 30 days hold after the owner stops saving or disconnects (Codex review of #111).
- **A settlement repeat must be identical.** A repeat is harmless only when the state, charge, response ID, reason, above-reservation flag and every token count match; different usage that rounds to the same charge is a conflict and stops paid requests for review (Codex review of #111).
- **Target model (owner's choice, 2026-11-14): `gpt-6.1-sol` only, no model picker.** This names the model Task 69 gathers evidence for; it adds nothing to `AdvisoryAiModelProfiles.Reviewed` and approves no paid call. Task 69 also has to account for the cache-write price listed on the official pricing page and fix the reasoning effort so reasoning cannot use up the output allowance.
- **One provider response, one request.** A response ID already used by another request makes the second settlement uncertain, keeps the larger figure and stops paid requests for review.
- **Malformed but well-formed bodies.** A response with an output item the request did not allow is untrusted: its text is discarded and its usage is never settlement evidence, but the usage is kept so a charge above the reservation is recorded in full (ADR-029).

## Out of scope here

Update matching, eligibility and execution; the diagnostic readers. No model, price, key or paid call in stages 1–5.
