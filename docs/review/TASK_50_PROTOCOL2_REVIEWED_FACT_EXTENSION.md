# Task 50 — protocol-2 reviewed helper fact extension (draft for independent review)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03/04/07/13`; retained `FR-UPD-08/09/14`, `TM-06`, `FR-HIS-01`.
Status: **proposed specification only — independent design review pending; not accepted**. It adds no code or endpoint, admits no route, and grants no deployment, host-read, UAC or update authority. Drafted against HEAD `97c7b6d` (read-only).

This is the item the handover requires before endpoint composition: "document and independently review the exact bounded fact extension for the helper's installed/candidate/profile/guard observations" (`TASK_50_CLOUD_HANDOVER_OPUS_5_5.md` §4).

Primary sources: handover §3–§6; ADR-025 (responsibility table and the two 2026-10-31 owner amendments); `TASK_50_PROVIDER_MAPPING_V2.md` ("Bounded response and settlement"); `TASK_50_CHOCOLATEY_PROFILE_V2.md` ("Proposed producer/consumer seams"); `TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md` (L3 fingerprint, M2 postcheck); and the source files `ReviewedUpdateWireCodec.cs`, `ReviewedUpdateOperation.cs`, `ChocolateyCommonProfileFingerprint.cs`, `ChocolateyNativeVersion.cs`, `ChocolateyReviewedUpdateArguments.cs`, `ReviewedUpdateDispatchWorkflow.cs`, `UpdateDispatchSettlement.cs`, `WindowsElevatedHelperTransport.cs` and `ElevatedHelperProtocol.cs`.

## 1. Scope

| In scope | Out of scope |
|---|---|
| The protocol-2 **response** frame, for exactly one route: schema 3, `ChocolateyMachineRuntimeLimitsV3`. That means `ReviewedProviderGuardContract.CommunityScriptRuntimeLimits` and `ReviewedCandidateProvenance.UserCacheIntent`, with Chocolatey / ChocolateyCommunity / Machine / Stable, `NotControllable(ProviderScriptControlled)` and `ProviderScriptSelected`. These are the same predicates as `ChocolateyReviewedUpdateArguments.Build`. | All WinGet routes. They stay unavailable (handover §6), and nothing in this document admits them. |
| The desktop's consistency predicates and its mapping to `ReviewedUpdateDispatchObservation`. | The protocol-2 request. It is unchanged: six fields, exactly as in the codec. |
| Byte bounds, versioning, and the negative tests the implementation must include. | Protocol 1. Its deployments gain nothing, and there is no downgrade, reinterpretation or fallback to the old `UpdatePackage`. `ChocolateyMachineProvisionalV3`, `ChocolateyMachineRegistrationV3` and schema-2 operations are never sent. |
| | Native observers, the endpoint, progress frames, the terminal/history format, deadlines, and Task 51 restart semantics. |

## 2. Response field set (exact order)

The existing 15 response properties keep their names, JSON types and positions 0–13. Six properties are inserted at positions 14–19. `diagnostic` moves to position 20 and stays the last field. That gives exactly 21 properties, with no others.

| # | Name | JSON type | Encoding / allowed values | Max | Absence |
|---|---|---|---|---|---|
| 0–13 | `protocolVersion` … `outputLimitExceeded` | unchanged | unchanged (`UpdateWireFacts`) | unchanged | unchanged |
| 14 | `qualification` | string | `Undetermined` \| `Matched` \| `Refused`. Exact case. Numeric or alias spellings are rejected: enum text must round-trip through `ToString()`, as `invocation` already does. | 12 B | never null |
| 15 | `refusalReason` | string | `None` \| `RequestNotAdmitted` \| `MachineInputsUnsupported` \| `SourceNotCommunity` \| `InstalledMismatch` \| `ProfileMismatch` \| `CandidateMismatch` | 24 B | never null; `None` when not Refused |
| 16 | `observedInstalledVersion` | string \| null | The helper's independently observed installed version, as `ChocolateyNativeVersion.Normalized`. Valid only if `TryParse(s)` succeeds and `Normalized == s`, which implies ASCII `[0-9A-Za-z.-]`, no `+` and no whitespace. The empty string is rejected. | 128 B | JSON `null` |
| 17 | `observedCandidateVersion` | string \| null | The helper's unique candidate from its fixed root-engine `outdated` query, normalized and validated the same way. | 128 B | JSON `null` |
| 18 | `observedProfileFingerprint` | string \| null | `ChocolateyCommonProfileFingerprint.Compute` over the helper's own common profile data, using the authenticated requester SID. The output is exactly 64 uppercase hex digits. The encoding is the pinned L3 form (`pc-steward-choco-profile:2`, golden `C179F864…EE77`). Lowercase is rejected. | 64 B | JSON `null` |
| 19 | `appliedGuardContract` | string \| null | Only `CommunityScriptRuntimeLimits`. In protocol 2, `LegacyUnqualified` and `RequiredEffectiveControls` are rejected by the codec. | 28 B | JSON `null` |
| 20 | `diagnostic` | string | Unchanged: printable ASCII, 2,048 characters/bytes, sanitized, stable on a second pass | 2,048 chars (4,096 B escaped) | `""` |

Every property is always present: absence is the JSON literal `null`, never an omitted property. None of the new values can contain a character that the relaxed encoder escapes. Whole-frame re-encode comparison therefore rejects any escaped form, such as `"M"`, along with whitespace, reordering and numbers sent as strings.

The helper normalizes native version strings before echoing them. A `Normalized` form longer than 128 characters is possible, because padding can add up to 4 characters to a 128-character original. In that case the helper emits `null` with the applicable refusal. Such a version cannot equal a frozen version, which is limited to 128 characters by `UpdateJobSelection`.

### 2.1 Which consumer needs each field, and what was dropped

| Field | Consumer decision it drives |
|---|---|
| `qualification` | Settlement status: Refused, Cancelled/TimedOut/Failed, or provider status. Also the desktop's live candidate-check presentation (§4). A Matched value is **only** a claim that the desktop re-verifies (§3.2). On its own it never qualifies anything. |
| `refusalReason` | Fixed refusal text in the live result and history. The `SourceNotCommunity` text names the owner's official-feed condition. Each reason has a checkable predicate (§3), so a reason cannot contradict the echoed values. |
| `observedInstalledVersion` | Equality with the frozen `Selection.InstalledVersion`, the InstalledMismatch predicate, and the "installed is now X" display. (Profile V2: history retains "independent helper observations/mismatch".) |
| `observedCandidateVersion` | Equality with the frozen `Selection.CandidateVersion`. This is the whole point of the UserCacheIntent amendment: an exact match, or a refusal with no retargeting. It also drives the "source now offers Y" display. |
| `observedProfileFingerprint` | Desktop equality with the review-time common profile digest, frozen in `EvidenceId = "choco-profile-v2:" + <64 hex>` (Profile V2). The digest also transitively binds the requester SID, root identity, compiled release-asset pin, code-list digest (engine, helpers and tools bytes), configured cache identity, effective features, source resolution, target ID, installed version, and archive/store identity. |
| `appliedGuardContract` | Desktop equality with the frozen `operation.ProviderGuardContract`. The digest binds the *input* route. This field exposes a helper that *derived* a different guard profile (build skew), and history records which guard the helper applied. It is cheap at 54 B. |

These fields were considered and dropped, because no consumer decision needs them:

| Dropped | Why it is not needed |
|---|---|
| Package ID echo | The ID is bound by `operationDigest`, and the target's uppercase ID is inside the profile fingerprint. |
| Release / engine pin echo | The compiled `AssetSha256` and the code-list digest are both fields of the fingerprint. |
| SID echo | The SID is a fingerprint field, and the helper must refuse a payload SID that differs from the authenticated peer SID (`RequestNotAdmitted`). |
| Helper-specific facts (elevated `ChocolateyHttpCache`, token, effective environment, loader) | The desktop has no independent elevated view to compare them with. Failure surfaces as `MachineInputsUnsupported`. Success is implied only inside an authenticated Matched, which is the ADR-025 helper responsibility. |
| Post-update file-first client check | ADR-025's Results row says "neither side treats another side's success boolean as postcondition proof". The desktop verifier must run `ChocolateyPostUpdateClientCheck` itself (direct file reads, before any target query). A helper result could only duplicate a negative the desktop also detects. Including it would also extend the helper's owned work past provider exit. See O5. |
| Separate restart report | Profile V2 says "no Task51 restart inference" for this route. The exit code, already on the wire, is the only provider restart signal. `RestartReported` is derived by the desktop (§4, O4). A field whose only legal value is `None` serves no consumer. |
| Per-check booleans / query states | Covered by `refusalReason`, `qualification == Undetermined` and the existing settlement facts. |
| `HelperQualified`, `Success`, `Final`, claim state | Prohibited by the handover §3–§4 and Mapping V2 F3. |

## 3. Consistency predicates

### 3.1 Structural (operation-independent), enforced in the codec

`CreateResponse` must throw, and `TryReadResponse` must return false, unless `UpdateWireFacts.IsConsistent` holds and **all** of the following hold:

| ID | Predicate |
|---|---|
| S1 | `qualification` and `refusalReason` are defined values, spelled exactly. |
| S2 | `refusalReason == None` ⇔ `qualification != Refused`. |
| S3 | `invocation ∈ {Started, Unknown}` ⇒ `qualification == Matched`. No upgrade may be invoked without Matched. |
| S4 | `qualification ∈ {Refused, Undetermined}` ⇒ `invocation == NotStarted`. Refused therefore implies NotStarted, with no exit code and no exit/I/O/EOF facts. |
| S5 | `Matched` ⇒ all four observed fields (16–19) are non-null. |
| S6 | `appliedGuardContract == null` ⇔ `refusalReason == RequestNotAdmitted`. |
| S7 | `RequestNotAdmitted` ⇒ fields 16–18 are null. |
| S8 | `observedCandidateVersion != null` ⇒ `qualification == Matched` or `refusalReason == CandidateMismatch`. In addition, fields 16 and 18 are non-null. This is the stage order: the fixed client `outdated` query runs only after static protected profile/installed reads succeed. |
| S9 | `SourceNotCommunity` ⇒ `observedProfileFingerprint == null`, because `Compute` requires `ChocolateySourceResolution.TryResolve`. |
| S10 | `ProfileMismatch` ⇒ fields 16 and 18 are non-null. `InstalledMismatch` places no constraint on field 18. |
| S11 | `Undetermined` ⇒ `cancelled ∨ timedOut ∨ ¬aggregateSettlementConfirmed`. A settled, uncaused "no decision" is a contradiction. |
| S12 | The value formats in §2 hold, including that the guard value is exactly `CommunityScriptRuntimeLimits`. |

### 3.2 Operation-bound, enforced by the desktop protocol-2 dispatcher adapter

This applies after authentication, correlation (`jobId`/`operationDigest`/`nonce`) and §3.1 pass. Let `F = EvidenceId` with its `choco-profile-v2:` prefix removed. If `EvidenceId` is not exactly that prefix plus 64 uppercase hex digits, the route is unsupported and the adapter must not dispatch (O2).

| ID | Predicate |
|---|---|
| P1 | `appliedGuardContract != null` ⇒ it equals `operation.ProviderGuardContract.ToString()`, which must be `CommunityScriptRuntimeLimits`. |
| P2 | `Matched` ⇒ `installed == Selection.InstalledVersion`, `candidate == Selection.CandidateVersion`, and `fingerprint == F`. Comparisons are ordinal; the digest comparison is fixed-time. |
| P3 | `InstalledMismatch` ⇒ `installed == null ∨ installed != Selection.InstalledVersion`. |
| P4 | `ProfileMismatch` ⇒ `installed == Selection.InstalledVersion ∧ fingerprint != F`. A changed installed version must be reported as InstalledMismatch. |
| P5 | `CandidateMismatch` ⇒ `installed == Selection.InstalledVersion ∧ fingerprint == F ∧ (candidate == null ∨ candidate != Selection.CandidateVersion)`. |
| P6 | `RequestNotAdmitted`, `MachineInputsUnsupported` and `SourceNotCommunity` carry no equality claim. Any values present are "reported" display data only. |

Reason precedence (the helper reports the first that applies):
`RequestNotAdmitted` → `MachineInputsUnsupported` → `SourceNotCommunity` → `InstalledMismatch` → `ProfileMismatch` → `CandidateMismatch`.

**Any failure of §3.1 or §3.2, or any invalid frame, means uncertainty and never qualification.** The adapter discards every wire fact and returns `Invocation = Unknown`, `ExecutionUncertain = true`, `Status = Failed`, the fixed diagnostic `helper-response-rejected`, and no candidate check. The workflow then records Uncertain and keeps the DispatchClaimed barrier, with no retry. This applies even when the frame claims NotStarted.

## 4. Desktop mapping to `ReviewedUpdateDispatchObservation`

`H` means the desktop itself observed helper process exit within the existing `ExitTimeout` (5 s) after a response, and the transport was sealed without a termination failure.

| Observation member | Value from an accepted response |
|---|---|
| `Invocation` | wire `invocation` |
| `ExitCode` | wire `exitCode`, which is null for NotStarted by `IsConsistent` |
| `OwnedRoot` | Immutable snapshot of the desktop's helper-ownership task. It has completed successfully only if `H` holds. |
| `DirectExitObserved` | `wire.directProcessExitObserved ∧ H` |
| `OwnedIoComplete` / `PipeEndOfFile` | wire fact ∧ desktop-observed pipe read completion / EOF |
| `TerminationFailed` | `wire.aggregateTerminationFailed ∨` the desktop failed to terminate the helper |
| `ExecutionUncertain` | `¬wire.aggregateSettlementConfirmed ∨ ¬H` |
| `RestartReported` | `false` for this route (Profile V2). See O4. |
| `Diagnostic` | wire `diagnostic`, already bounded and sanitized |

| Wire state | `Status` | Workflow result |
|---|---|---|
| Refused (any reason) | `Refused` | Settled → NotInvoked → `Refused`. Unsettled → Uncertain. |
| Undetermined, or Matched ∧ NotStarted | `Cancelled` if cancelled, otherwise `TimedOut` if timed out, otherwise `Failed` | Settled → NotInvoked → Cancelled/Failed. Otherwise → Uncertain. |
| Started ∧ outputLimitExceeded / timedOut / cancelled | `Failed` / `TimedOut` / `Cancelled` | Settled → `Unverified`. Otherwise → Uncertain. |
| Started, exit 0 | `Completed` | Settled → the verifier runs the file-first client check, then the residue-aware target query. Success only for an exact verified version. |
| Started, exit ≠ 0 | `ProviderFailed` | Settled → `Failed`. Otherwise → Uncertain. |
| Unknown | `Failed` | Always Uncertain |
| UAC declined (`ERROR_CANCELLED` at launch; no response exists) | `NotStarted(ElevationDenied)`, observed by the desktop | `ElevationDenied` |

**Candidate-check presentation.** This is a new appended optional observation member, for example `ReviewedHelperCandidateCheck { NotObserved, Confirmed, Refused, Unconfirmed }`. The adapter sets it only from an accepted response, and it maps to fixed text. It never changes `Status`, settlement or the outcome.

- The live result may say **"PC Steward's administrator helper confirmed the exact reviewed version with protected machine data"** only when all of these hold: transport peer authentication, exact correlation, §3.1, §3.2, and `Matched`.
- It may appear alongside any outcome, including Cancelled-before-invocation or Failed. It is never worded as, or placed next to, a success statement.
- For a refusal, the "reported by the helper" values (fields 16/17) may be shown with the fixed reason text.
- A serialized label, persisted row, `Invocation == Started` or a helper boolean never produces this text. How history persists it is open (O3). The existing history presentation continues to say nothing about helper qualification.

## 5. Bounds: worst-case byte arithmetic

The calculation assumes compact JSON exactly as written by the codec, maximum-length values, `exitCode = -2147483648`, `invocation = "NotStarted"`, all booleans `false`, and every diagnostic character escaped (`"` or `\` → 2 bytes).

| Component | Bytes |
|---|---|
| Existing 15-field frame without diagnostic content (`"diagnostic":""`) | 513 |
| `"qualification":"Undetermined",` | 31 |
| `"refusalReason":"MachineInputsUnsupported",` | 43 |
| `"observedInstalledVersion":"<128>",` / `"observedCandidateVersion":"<128>",` | 158 + 158 |
| `"observedProfileFingerprint":"<64>",` | 96 |
| `"appliedGuardContract":"CommunityScriptRuntimeLimits",` | 54 |
| **New fixed portion** | **1,053** (513 + 540) |
| Diagnostic: 2,048 ASCII characters, worst-case escaped | 4,096 |
| **Worst-case response frame** | **5,149 ≤ 16,384** (11,235 B headroom; previously 4,609) |

The frame is carried with the existing 4-byte little-endian length prefix (`PipeMessageFraming`), which is outside the 16 KiB frame bound. The diagnostic bound, the request bound (4,096 B payload) and the 16 KiB frame are unchanged. `MaxDepth = 2` still holds, because all new values are scalars.

## 6. Inherited authentication and correlation (no new cryptography)

- **ADR-018/025 transport.** Protected desktop/helper identities, binary ACL and reparse checks, and same-signer verification immediately before launch. A random per-request pipe restricted to user and SYSTEM. Both endpoints verify that the peer PID equals the launched or launching process, and check token, session and SID (`WindowsPeerIdentityVerifier.VerifyPeer`). Bootstrap arguments are only protocol, PID, pipe and nonce. One request per helper.
- **Nonce and freshness.** The nonce is 32 random bytes as 64 uppercase hex, used once. Request freshness is 2 minutes; the review age limit is 30 minutes. The helper rechecks the frozen `ReviewedAt` ceiling.
- **Digest.** The helper hashes the **received canonical payload bytes**, Base64-decoded and not re-serialized. The hash must equal `operationDigest`, and `ReviewedUpdateOperation.TryRestore(bytes, digest)` must succeed. The response echoes that digest. The desktop compares `jobId`, `operationDigest` (equal to `operation.Digest`, the SHA-256 of the bytes it sent) and `nonce` in fixed time.
- **Integrity.** There is no frame MAC or signature. Integrity rests on the pipe ACL and peer identity checks. Helper observations are trusted only as authenticated helper observations, and still require the desktop's own `H` and its own postcondition verification.
- **Uncorrelatable requests.** A request whose frame, digest or `TryRestore` fails, or whose nonce differs from the bootstrap nonce, gets **no** response, only a helper exit code. The desktop treats this as a lost response, which means Uncertain with the barrier retained (Mapping V2). A correlatable but inadmissible request gets `Refused/RequestNotAdmitted`. Examples: stale freshness, review age over 30 minutes, an unsupported route/mode/shape, or a payload SID that differs from the peer SID.
- **Budgets.** The existing budgets are unchanged: connection 30 s, execution 11 min, exit 5 s. The helper 15 min / desktop 11 min inconsistency is recorded but not redesigned here.

## 7. Versioning and compatibility

- Each desktop/helper pair accepts exactly one protocol version, pinned in the ADR-022 manifest. The desktop's `Supports` must check that manifest capability **before the claim**, so a mismatched deployment results in NotStarted with no claim. If a mismatch is still discovered after the claim, the result is Uncertain. It is never a fallback, retry or reinterpretation.
- **Recommendation:** redefine the protocol-2 response in place as this 21-field shape. The passive codec has never been deployed or used by an endpoint. The alternative is to allocate `"3"` (O1).
- Unknown, missing, duplicate or reordered properties, unknown or alias enum values, wrong JSON types, `null` in a non-nullable field, a non-null value where S6–S10 require null, and any non-canonical encoding all reject the **whole frame**. That is a lost response, which means Uncertain.
- A protocol-1 response (six properties) is rejected by the protocol-2 reader, and a protocol-1 helper given protocol-2 bootstrap arguments exits without a response. Old schema-2 and other routes are never sent. There is no downgrade.

## 8. Required negative (and boundary) test matrix

| Group | Cases (each must reject, or produce the stated result) |
|---|---|
| Framing / encoding | Each of the 21 properties missing. An extra property. A duplicate. Any adjacent pair swapped. Whitespace, trailing comma, comment. Unicode-escaped value or name. Lone-surrogate name or value. Number as string; string as number. `null` in fields 0–15 or 20. An empty-string version. Lowercase fingerprint, or 63/65 hex digits. A version of 129 characters. Versions `1.0+meta`, `01.0`, `1.0.0.0` (non-normalized) and `1.0.0-` . A frame of 16,385 bytes. An oversize diagnostic, or one containing a control character or unredacted sensitive value. |
| Enums | `Matched` in the wrong case. `"1"` as a numeric alias. Unknown reason. `LegacyUnqualified` / `RequiredEffectiveControls` as the guard. |
| Correlation | Wrong job, digest or nonce. A request mutated after hashing with the digest **recomputed**: the helper must reject or refuse, and the desktop sees a digest mismatch. A protocol-1 frame. Protocol `"3"` or `"1"`. |
| Structural S1–S12 | `Refused` + `Started`. `Refused` + `None`. `Matched` + any reason. `Undetermined` + `Started`/`Unknown`. `Matched` with any null observed field. A guard null without `RequestNotAdmitted`, and vice versa. `RequestNotAdmitted` with a value present. A candidate present on `ProfileMismatch`/`InstalledMismatch`/`Undetermined`. `SourceNotCommunity` with a fingerprint. A settled, uncancelled, untimed `Undetermined`. |
| Operation-bound P1–P5 | `Matched` with a different installed version, candidate (including `1.2` vs `1.2.0`), fingerprint, or guard. `ProfileMismatch` whose fingerprint equals F. `ProfileMismatch` with a changed installed version. `CandidateMismatch` with an equal candidate. A malformed `EvidenceId`: no dispatch. Each case yields Uncertain plus a barrier and no "confirmed" text, even with `NotStarted`. |
| Mapping / settlement | Each reason, settled → `Refused`. Unsettled `Refused` → Uncertain. No `H` → Uncertain. `Matched` + NotStarted + cancelled → `Cancelled` with Confirmed text. Started exit 0 → the verifier runs, file-first. A file-check ClientChanged → never Success. A verifier miss → Unverified. Exit 3010 → Failed (until O4). Unknown → Uncertain. UAC denial → ElevationDenied. A lost response → Uncertain. In every case the candidate-check value never alters the outcome. |
| Positive boundaries | The largest valid response (5,149 B) round-trips byte-exactly. A minimal `Refused/RequestNotAdmitted`. A full Matched frame. |

## 9. Non-goals and open questions

**Non-goals.** No WinGet; no protocol-1 change; no request change; no progress frames; no wire Success/Final/HelperQualified; no new deadline; no native observer; no terminal/history schema change; no restart inference; no relaxation of verification or claim barriers.

**Open questions for the owner or reviewer** (not resolvable from primary sources):

| # | Question |
|---|---|
| O1 | Redefine the never-deployed protocol-2 response in place (recommended), or allocate `"3"`? |
| O2 | Should `ReviewedUpdateOperation` enforce `EvidenceId = "choco-profile-v2:" + 64 uppercase hex` for `ChocolateyMachineRuntimeLimitsV3`? Today this is only the Profile V2 proposal and a test fixture convention. Without it, P2 has no domain-guaranteed comparator. |
| O3 | ADR-025 says history "preserves … whether the helper independently qualified an exact match". The current history presentation never shows this, because stored rows are untrusted. Should the terminal record gain a desktop-derived check value, displayed as "recorded at the time"? Or should only the live result show it? Either choice needs a format decision outside this specification. |
| O4 | Chocolatey exit codes 3010/1641. Default `RestartReported = false` (Profile V2) records Failed without verification. Setting it from the exit code alone would reach the verifier, giving RestartRequired only on a reliable pending state, and Unverified otherwise. Which policy? |
| O5 | Confirm that the non-elevated desktop can read every `ChocolateyClientStateData` input (engine, code-list and registration digests) for its own file-first check. If it cannot, the route is always Unverifiable, which is safe but unusable. The alternative is a helper negative-only override field, which would need to be decided now because the field set is exact. |
| O6 | May helper-reported refusal values (fields 16/17) be shown to the user and kept in history as "reported by the helper"? |
| O7 | The stage-order predicate S8 and the reason precedence constrain the helper's design (static reads before any client execution). Do they match the intended `ObserveHelperQualificationAsync` order? |
| O8 | An uncorrelatable request leads to no response, then Uncertain, which leaves a reconciliation barrier. Is that acceptable for manifest-mismatch edge cases that slip past the pre-claim capability check? |
| O9 | `ChocolateyNativeVersion` is internal to Provider.Chocolatey. Execution does not have InternalsVisibleTo. The codec needs either visibility or an equivalent strict grammar; this is an implementation choice and must stay byte-identical. |
