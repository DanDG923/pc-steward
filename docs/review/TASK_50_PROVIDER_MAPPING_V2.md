# Task 50 — provider mapping corrections, revision 2

Requirements: `FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03–04/07/13`. This supersedes the proposed wire and qualification portions of revision 1. It is not route admission, deployment authority or Task 50 completion.

## Fresh review disposition

The independent review of HEAD `857c29a` and revision-1 SHA256 `EE935BA20F8081C191C6594393EB6F622FF35EA4AE7D214AF0EAE671E6B5E684` was CONDITIONAL. F1–F3 block wire integration; F4/F5/F8/F9 and native evidence block Chocolatey admission. No finding requires a new owner decision. The reviewer did not edit, build or perform host actions.

| Finding | Required correction |
|---|---|
| F1 | Explicit UTF-8 JSON encoder/order; seven-fraction UTC `Z` request time; canonical Base64 and uppercase hashes; whole-frame byte equality on decode/re-encode. Reject alternate escaping, ordering, whitespace, numbers-as-strings and duplicate fields. |
| F2 | Sanitize diagnostics before converting each Unicode scalar to printable ASCII; maximum 2,048 ASCII characters/bytes before JSON escaping. Worst-case quotes/backslashes expand to at most 4,096 encoded bytes. Keep the 16-KiB frame ceiling. |
| F3 | Facts-only correlated response; invocation is `NotStarted`, `Started` or `Unknown`. Separate upgrade exit/I/O/EOF observations from aggregate settlement, including preflight work. No helper claim-state or verified-success verdict. See truth table below. |
| F4 | Introduce explicit `Observed(architecture)` / `ProviderScriptSelected`, schema 3 and new route IDs together before eligibility. Do not reinterpret schema-2 Neutral. Existing schema-2 rows remain passive data. |
| F5 | Observe effective `ChecksumFiles=true` and append fixed `--require-checksums`, alongside ignore-remembered and fail-on-not-installed. Packages lacking required checksums may fail after claim; this is not verified update success. |
| F6 | Positive user WinGet qualification initially requires a current-user MSIX native instance with observed X86/X64/Arm64 equal to the applicable installer architecture. Neutral and registry-view-only EXE/MSI architecture remain unavailable. Explicitly disclose this lost coverage. |
| F7 | Retain CLI execution. Resolve CLI and actual COM implementation to consistent App Installer package identity/version at review and dispatch, or refuse. Projection package version is not implementation identity. COM execution belongs to its owning adoption tasks. |
| F8 | Independently establish effective elevated native cache/TEMP/TMP after refresh. Observe protected compatible locations or refuse. Do not create a new persistent directory or assume a launch override survives native refresh. |
| F9 | Document concrete numeric observer caps before implementation, with fail-closed coverage. Native/archive/XML bounds must precede allocation/decompression. Current native observation is still unimplemented. |

Independent checksum verification: pinned [ChocolateyUpgradeCommand.cs](https://github.com/chocolatey/choco/blob/194ac6262ac75571c44aa0a431c3ab6f36fa73be/src/chocolatey/infrastructure.app/commands/ChocolateyUpgradeCommand.cs), blob `8b1c916c65ef0aeb6e49e565b2279297d018f38d`, lines 140–149, sets both `AllowEmptyChecksums` and `AllowEmptyChecksumsSecure` false for `require-checksums`. This proves the option mapping in that source, not published-binary or host behavior.

## Bounded response and settlement

The council reconciled the reviewer's await-before-response wording: never await an ownership root that may intentionally remain pending forever. After existing bounded teardown, the helper may return unsettled facts. Sending a response is not exiting the helper or settling its children. Existing desktop exit/termination budgets remain unchanged; killing is not proof of settlement.

| Invocation | Aggregate settlement | Consistent upgrade facts | Desktop consequence |
|---|---|---|---|
| NotStarted | true | No upgrade exit code/exit/I/O/EOF observations | Settled refusal/cancellation possible only after independent helper exit observation |
| NotStarted | false | No upgrade observations; preflight may remain unsettled | Keep durable conflict; “not started; cleanup unconfirmed” |
| Started | true | Direct exit, complete owned I/O, EOF; no aggregate termination failure | Settled provider result possible only after independent helper exit and applicable target verification |
| Started | false | Partial or complete direct observations | Keep durable conflict regardless of exit code |
| Unknown | false | No asserted upgrade-specific observations | Keep durable conflict; invocation unknown |
| Unknown | true | — | Reject contradiction |
| Any | true with termination failure or missing required started observations | — | Reject contradiction |

Exit code implies observed direct exit. A settled negative result is possible; settlement does not mean update success. Invalid/lost/uncorrelated responses retain the claim barrier and never permit retry. The desktop claim is not cleared by a response. Helper aggregate evidence is trusted only from the authenticated protected helper and still requires desktop-observed helper settlement. Preflight uncertainty prevents upgrade invocation. A response must not encode `Final`, `Uncertain`, a success Boolean or a restored approval.

## Immediate isolated implementation seam

Add internal `ReviewedUpdateWireCodec` in Execution with passive request/response data and `UpdateWireFacts`. It has no endpoint, provider, approval, database, process-launch or claim dependency. Request creation consumes bounded canonical bytes plus digest/nonce/time; parsing returns **untrusted framed bytes**, not an accepted operation. The consuming future endpoint must require the schema-3 operation codec, lifetime, peer, capability and native-evidence admission independently. No currently active protocol constant or caller changes in this slice.

The frame maximum remains 16,384 bytes; operation payload is capped at 4,096 raw bytes for this new envelope. Request has exactly the six revision-1 properties in that order. Explicit relaxed JSON escaping preserves Base64 `+` and `/`; mandatory JSON quote/backslash escapes remain canonical. Response carries job/digest/nonce, invocation, nullable exit code, direct exit/I/O/EOF facts, aggregate termination failure and settlement, cancellation/timeout/output-limit facts, and bounded diagnostic. No task or claim state crosses the wire. Re-serialization enforces exact property spelling/order, Boolean/integer representation and escaping after bounded JSON parsing. Both largest request and largest escaped response require fixtures.

This slice does not close F4 or native profile evidence, does not qualify a provider and does not switch the active protocol-1 endpoint. Before integration, the full schema-3, profile-observation and lifecycle mapping still needs implementation and validation.
