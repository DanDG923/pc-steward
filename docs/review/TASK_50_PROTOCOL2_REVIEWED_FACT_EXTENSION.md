# Task 50 — protocol-2 reviewed helper fact extension, revision 2

Requirements: `FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03/04/07/13`; retained `FR-UPD-08/09/14`, `TM-06`, `FR-HIS-01`.

Status: **proposed specification, revision 2.** It responds to the CONDITIONAL independent review of revision 1 (committed at `c3c870f`, SHA `030df504…de1`). It adds no code or endpoint. It admits no route and authorizes no deployment, host read, UAC prompt or real update. Revision 1 remains as the reviewed artifact.

Sources: the Task 50 handover (§3–§6); ADR-025 (the responsibility table, the 2026-10-31 owner amendments, and the requirement for a new protocol version); `TASK_50_PROVIDER_MAPPING_V2.md` (bounded response and settlement); `TASK_50_CHOCOLATEY_PROFILE_V2.md` (producer/consumer seams, L1 cache precheck, query mapping); the Task 90 profile-V2 remediation (L3 fingerprint, M2 file-first postcheck); and the source files `ReviewedUpdateWireCodec.cs`, `ReviewedUpdateOperation.cs`, `ChocolateyCommonProfileFingerprint.cs`, `ChocolateyNativeVersion.cs`, `ChocolateyReviewedUpdateArguments.cs`, `ReviewedUpdateDispatchWorkflow.cs` (HEAD, including `StillSupported`), `UpdateDispatchSettlement.cs`, `UpdateBatchWorkflow.cs` (`PostUpdateInventoryResult`) and `WindowsElevatedHelperTransport.cs`.

## 0. Changes from revision 1

| Finding | Edit |
|---|---|
| M1 — the helper observes at multiple times | §3 defines four helper stages, including a final pre-launch revalidation (stage D). `Matched` now means every stage passed. Installed version and fingerprint are reported from stage D. Two reasons are new: `PreflightQueryFailed` and `ApprovalExpired`. Every late failure has a stated state and reason (§3.1 table). A candidate may now appear on refusals after the query. A `Matched ∧ NotStarted` result with no cancellation or timeout means only a definitive launch failure. |
| M2 — history persistence | §4.2 makes persistence a **required integration item**: the desktop-derived check value, the refusal reason and the reported versions are written at finalization and never read back as authority. The blanket "no history schema change" non-goal is removed. |
| M3 — `appliedGuardContract` | Dropped, together with S6 and P1. §5 is recomputed: 999 B fixed portion and 5,095 B worst case. |
| M4 — pre-claim vs post-claim vs post-launch | §4.1 gives the three-phase rule. `Supports` rejects malformed `EvidenceId` and missing manifest capability before a claim exists. The P-row in §8 is fixed. |
| L5 — verifier result | §4.3 requires the Chocolatey verifier to return `RestartPending = false` and adds an accurate ClientChanged / Unverifiable detail and message. |
| L6 — several flags at once | §4 gives a single flag precedence: `outputLimitExceeded` > `timedOut` > `cancelled` > exit code. |
| L7 — display wording | All display text now reads "reported by PC Steward's administrator helper". It is never stated as fact. |
| L8 — further negatives | Added to §8. |
| O1–O9 | §9 now records the resolved dispositions. One owner contingency (O5) remains. |

## 1. Scope

This revision covers only the protocol-2 **response** frame, for exactly one route: schema 3, `ChocolateyMachineRuntimeLimitsV3`. On that route the guard contract is `CommunityScriptRuntimeLimits` and the candidate provenance is `UserCacheIntent`. The operation shape is Chocolatey, ChocolateyCommunity, Machine, Stable, `NotControllable(ProviderScriptControlled)`, `ProviderScriptSelected`, using the same predicates as `ChocolateyReviewedUpdateArguments.Build`.

Out of scope:
- WinGet. It remains unavailable (handover §6).
- The request. Its six fields are unchanged.
- Protocol 1. Its deployments gain nothing; there is no downgrade and no fallback to the old `UpdatePackage`.
- Other Chocolatey routes and schema-2 operations. They are never sent.
- Native observers, progress frames, deadlines and Task 51 restart semantics.

## 2. Response field set (exact order; 20 properties)

Positions 0–13 are unchanged (`UpdateWireFacts`). Five properties are inserted at positions 14–18. `diagnostic` moves to position 19 and stays last.

| # | Name | JSON type | Encoding / allowed values | Max | Absence |
|---|---|---|---|---|---|
| 14 | `qualification` | string | `Undetermined` \| `Matched` \| `Refused`. Exact spelling; numeric and alias forms are rejected because the value must round-trip through `ToString()`. | 12 B | never null |
| 15 | `refusalReason` | string | `None` \| `RequestNotAdmitted` \| `MachineInputsUnsupported` \| `SourceNotCommunity` \| `InstalledMismatch` \| `ProfileMismatch` \| `PreflightQueryFailed` \| `CandidateMismatch` \| `ApprovalExpired` | 24 B | never null; `None` unless Refused |
| 16 | `observedInstalledVersion` | string \| null | The installed version from the **latest completed** helper stage (stage D for `Matched`). Format is `ChocolateyNativeVersion.Normalized`: valid only if `TryParse(s)` succeeds and `Normalized == s`. Empty string is rejected. | 128 B | `null` |
| 17 | `observedCandidateVersion` | string \| null | The unique, normalized candidate from the stage-C `outdated` query. Same format as field 16. | 128 B | `null` |
| 18 | `observedProfileFingerprint` | string \| null | `ChocolateyCommonProfileFingerprint.Compute` over the helper's own common data, using the authenticated requester SID. Taken from the latest completed stage. Pinned L3 encoding; exactly 64 uppercase hex. | 64 B | `null` |
| 19 | `diagnostic` | string | Unchanged: printable ASCII, at most 2,048 characters, sanitized, stable when sanitized again | 4,096 B escaped | `""` |

Encoding rules:
- Every property is always present. Absence is the JSON literal `null`.
- None of the new values contains a character the relaxed encoder escapes. The whole-frame re-encode comparison therefore rejects escaped, reordered, whitespace-padded or numbers-as-strings forms.
- If a native `Normalized` string is longer than 128 characters (possible up to 132), the helper emits `null` with the applicable refusal reason. Such a string cannot equal a frozen version, which is limited to 128 characters.

### 2.1 Why each field exists, and what was dropped

| Field | Consumer decision |
|---|---|
| `qualification` | Selects the status in §4. It is a claim the desktop re-verifies (§3.3). It never qualifies anything by itself. |
| `refusalReason` | Selects fixed refusal and history text (§4.2). Each reason has checkable predicates. `SourceNotCommunity` names the owner's official-feed condition. |
| `observedInstalledVersion` / `observedCandidateVersion` | Compared for equality with the frozen `Selection` versions; this is the UserCacheIntent exact-match rule with no retargeting. Also shown as "reported by the helper" for refusals and persisted in history (§4.2). |
| `observedProfileFingerprint` | Compared for equality with `F`, the digest inside `EvidenceId = "choco-profile-v2:" + F` (§9 O2). The digest transitively binds the SID, root, release pin, code-list (engine, helpers and tools bytes), cache identity, effective features, source resolution, target ID, installed version, and archive and store identity. |

Dropped, because no consumer decision needs them:
- `appliedGuardContract`. It is a constant label derived from the route and schema, and both are bound by `operationDigest` and the frozen operation (M3).
- Echoes of the package ID, release/engine pin and SID. All three are inside the fingerprint, and the SID is also checked at stage A.
- Facts specific to the helper side: the elevated HTTP cache, token, environment and loader. The desktop has no elevated view to compare them against. A failure surfaces as `MachineInputsUnsupported`.
- The helper's own post-update client check. The desktop runs its own (§4.3, §9 O5).
- A restart field. This route keeps `RestartReported = false` (§9 O4).
- `HelperQualified`, `Success`, `Final` and claim state. These are prohibited.

## 3. Helper stages, reported state and predicates

### 3.1 Stages (order is normative; see O7)

| Stage | Work | Failure → reported state |
|---|---|---|
| **A** Admission | Read the frame. Hash the received payload bytes, check the digest, `TryRestore`. Check request freshness (2 min) and the 30-minute review ceiling. Check route and shape. Require payload SID = authenticated peer SID. | An uncorrelatable frame (frame, digest or restore failure, or nonce mismatch) gets **no response** (§6). A correlatable failure gets `Refused/RequestNotAdmitted` with fields 16–18 null. |
| **B** Protected static reads | Check root, release tree and code identities, coverage and ACLs against the requester token. Check loader and licensing state. Parse config. Check the configured cache. Check that the **elevated `ChocolateyHttpCache` is protected before any elevated client command** (Profile V2 L1). Check compiled environment and source resolution. Enumerate library, store and the installed registration. Compute the fingerprint. | Reasons in precedence order: `MachineInputsUnsupported`, `SourceNotCommunity`, `InstalledMismatch`, `ProfileMismatch`. Values are reported as far as they were observed. Candidate is null. |
| **C** Fixed client queries | Run the root-engine `list <ID> --exact` recheck, then `outdated --source <compiled URL> …`, under the existing budgets. | A settled query with non-zero exit, malformed, duplicate or incomplete records, or output overflow → `Refused/PreflightQueryFailed`, candidate null; `outputLimitExceeded` may be true. No unique candidate, or a candidate ≠ frozen → `Refused/CandidateMismatch`, candidate null or the differing value. A query timeout, cancellation or unsettled root → `Undetermined` with the matching flag or `¬aggregateSettlementConfirmed`. |
| **D** Final pre-launch revalidation | Immediately before launch, recheck cancellation, the 2-minute and 30-minute limits, and the budget, then **repeat all stage-B checks**. Chocolatey may rewrite config during queries; the fingerprint covers semantic config, not raw XML. Installed version and fingerprint are re-reported from this stage. | A lapsed limit → `Refused/ApprovalExpired`. Drift → the stage-B reason with stage-D values. The stage-C candidate (equal to the frozen value) **may remain**. Cancellation or timeout → `Undetermined`. |
| Launch | Exactly one fixed `upgrade` with the retained, verified root-engine binding. | A definitive failure before process creation → `Matched ∧ NotStarted` with no cancel or timeout flag (this is the only meaning of that combination). Cancellation or timeout after stage D and before launch → `Matched ∧ NotStarted` with the flag set. An ambiguous creation result → `Invocation = Unknown`. |

**`Matched` means stages A–D all passed in one item, with every preflight root settled.** Its values are the stage-D installed version and fingerprint, plus the stage-C candidate. The stage-D drift checks cannot run between the end of stage D and process creation; that window is a residual already covered by the owner's runtime-limit exception and by the desktop's file-first postcheck.

### 3.2 Structural predicates (codec; operation-independent)

`CreateResponse` must throw, and `TryReadResponse` must return false, unless `UpdateWireFacts.IsConsistent` holds and every predicate below holds.

| ID | Predicate |
|---|---|
| S1 | Enum values are defined and spelled exactly. |
| S2 | `refusalReason == None` ⇔ `qualification != Refused`. |
| S3 | `invocation ∈ {Started, Unknown}` ⇒ `Matched`. |
| S4 | `Refused` or `Undetermined` ⇒ `invocation == NotStarted`. |
| S5 | `Matched` ⇒ fields 16, 17 and 18 are all non-null. |
| S6 | `RequestNotAdmitted` ⇒ fields 16–18 are null. |
| S7 | `observedCandidateVersion != null` ⇒ field 16 is non-null, and the reason is not `RequestNotAdmitted` or `PreflightQueryFailed`. |
| S8 | `SourceNotCommunity` ⇒ field 18 is null, because `Compute` requires source resolution. |
| S9 | `ProfileMismatch` ⇒ fields 16 and 18 are non-null. |
| S10 | `Refused` ⇒ `¬cancelled ∧ ¬timedOut`; a decided refusal reports no interrupting cause. Additionally, `outputLimitExceeded` combined with `Refused` ⇒ the reason is `PreflightQueryFailed`. |
| S11 | `Undetermined` ⇒ `cancelled ∨ timedOut ∨ ¬aggregateSettlementConfirmed`. |
| S12 | All value formats in §2 hold. |

### 3.3 Operation-bound predicates (desktop protocol-2 adapter)

These are checked after authentication, correlation and §3.2. `F` is the digest from `EvidenceId`. Its format is guaranteed by the domain and by `Supports` (§4.1).

| ID | Predicate |
|---|---|
| P1 | `Matched` ⇒ installed = frozen installed version, candidate = frozen candidate version, and fingerprint = `F`. Version comparison is ordinal; digest comparison is fixed-time. |
| P2 | `InstalledMismatch` ⇒ installed is null or differs from the frozen installed version. |
| P3 | `ProfileMismatch` ⇒ installed = frozen installed version and fingerprint ≠ `F`. |
| P4 | `CandidateMismatch` ⇒ installed = frozen, fingerprint = `F`, and the candidate is null or differs from the frozen candidate. |
| P5 | A candidate that is present with any reason **other than** `CandidateMismatch` must equal the frozen candidate (stage C passed). |
| P6 | `RequestNotAdmitted`, `MachineInputsUnsupported`, `SourceNotCommunity`, `PreflightQueryFailed` and `ApprovalExpired` make no equality claim beyond P5. |

## 4. Desktop handling

### 4.1 Three phases (M4)

| Phase | Problem | Result |
|---|---|---|
| Before the claim | `Supports` (the closed compiled check): wrong route or shape, a malformed `EvidenceId`, or the **ADR-022 manifest lacks protocol-2 helper capability** | `NotStarted`. No claim is attempted (existing `RunAsync`). |
| After the claim, before the request is written | `TryConsume` or `StillSupported` fails | Finalized `Cancelled` with NotInvoked settlement (existing workflow). |
| | Desktop launch-trust failure before UAC | `NotStarted(Failed)`, NotInvoked. |
| | UAC declined (`ERROR_CANCELLED`) | `NotStarted(ElevationDenied)`. |
| | Helper process exists but no request has been written | NotStarted only with an observed helper exit (H); otherwise Uncertain. |
| After the request is written | Any of: invalid, lost, late (after the budget) or uncorrelated response; trailing bytes or a second frame before pipe EOF; failure of §3.2 or §3.3; missing H | Discard all wire facts. `Invocation = Unknown`, `ExecutionUncertain = true`, `Status = Failed`, fixed diagnostic `helper-response-rejected`, candidate check `NotObserved`. The result is **Uncertain**, the barrier stays, and there is no retry. |

H means the desktop observed the helper's exit within the existing 5-second `ExitTimeout` after the response, and the transport was sealed without a termination failure.

Observation members, built only from an accepted response:
- `Invocation`: the wire value.
- `ExitCode`: the wire value.
- `OwnedRoot`: a snapshot of the helper-ownership task, completed only if H holds.
- `DirectExitObserved`: wire value ∧ H.
- `OwnedIoComplete`, `PipeEndOfFile`: wire value ∧ the desktop's own observation.
- `TerminationFailed`: wire value ∨ desktop termination failure.
- `ExecutionUncertain`: ¬`aggregateSettlementConfirmed` ∨ ¬H.
- `RestartReported`: `false`.
- `Diagnostic`: the wire value.

**Status selection.** Apply the flag precedence first: `outputLimitExceeded` > `timedOut` > `cancelled` > exit code.

| Wire | `Status` | Workflow outcome |
|---|---|---|
| `Refused` (any reason) | `Refused` | Settled → `Refused`. Otherwise Uncertain. |
| `Undetermined` | `TimedOut` if timed out, else `Cancelled` if cancelled, else `Failed` | Settled → NotInvoked → `Failed` or `Cancelled`. A settled timeout is recorded as `Failed`. |
| `Matched ∧ NotStarted` | `TimedOut`, `Cancelled`, or `Failed` (= launch failure) | Settled → NotInvoked → `Failed` or `Cancelled`. |
| `Started` with a flag set | `Failed` / `TimedOut` / `Cancelled`, by precedence. **This includes exit 0 with `cancelled`.** | Settled → `Unverified`. Verification is skipped. |
| `Started`, no flag, exit 0 | `Completed` | Settled → verifier (§4.3). |
| `Started`, no flag, exit ≠ 0 | `ProviderFailed` | Settled → `Failed`. Exit 3010/1641 is also `Failed` (§9 O4). |
| `Unknown` | `Failed` | Uncertain. |

**Candidate-check value.** This is a new, appended, optional observation member, for example `ReviewedHelperCandidateCheck { NotObserved, Confirmed, Refused, Unconfirmed }`, mapping `Matched`, `Refused` and `Undetermined` respectively.
- Only the protocol-2 adapter produces a value other than `NotObserved`; an architecture test enforces this.
- It is never convertible to or from `ReviewedCandidateProvenance`. The operation remains `UserCacheIntent`.
- It never changes `Status`, settlement or the outcome.

Live text, for `Confirmed` only: **"Reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks."** It may accompany any outcome, but is never worded as success or placed next to success.

For refusals, the fixed reason text is shown together with "reported by the helper" values for fields 16 and 17. Values come only from accepted frames. Diagnostic text never produces `Confirmed`.

### 4.2 History persistence (required integration item; M2, ADR-025 amendment, Profile V2)

At finalization, in the same transaction as the terminal record, persist:
- the desktop-derived candidate-check value;
- the refusal reason;
- the reported installed and candidate versions (≤ 128 B each, same grammar).

These fields must be labelled as **desktop-derived and recorded at the time**.

The history reader must:
- validate them strictly;
- apply the existing integrity checks, so any failure makes the row unreadable and blocking;
- display them only as "recorded at the time: reported by PC Steward's administrator helper …";
- **never** feed them into review, approval, admission, dispatch, reconciliation or provenance.

The stored format (terminal-slot layout or columns, plus migration) is an implementation and review matter. The requirement is not optional.

### 4.3 Chocolatey verifier (L5)

The `IReviewedUpdateVerifier` for this route must:
- run `ChocolateyPostUpdateClientCheck` (file-only reads) first;
- return `RestartPending = false` always (no restart inference).

`PostUpdateInventoryResult` gains an appended detail, for example `None` / `ClientChanged` / `ClientUnverifiable`. Any detail other than `None` requires `ExpectedVersionDetected = false`. The workflow maps such a detail to the fixed message: "Chocolatey's own files or registration changed, or could not be checked, so PC Steward did not run it to verify the update; the result is unverified." The outcome is `Unverified`.

As a guard, the workflow must also treat `RestartPending = true` on this route as a contract violation and record `Unverified`, never `RestartRequired`.

## 5. Bounds

The worst case uses compact codec JSON, maximum-length values, `exitCode -2147483648`, `NotStarted`, all values `false`, and every diagnostic character escaped.

| Component | Bytes |
|---|---|
| Existing 15-field frame with `"diagnostic":""` | 513 |
| `qualification` (31) + `refusalReason` with `MachineInputsUnsupported` (43) + two 128-character versions (158 + 158) + fingerprint (96) | +486 |
| **Fixed portion** | **999** |
| Diagnostic: 2,048 characters, all escaped | 4,096 |
| **Worst-case frame** | **5,095 ≤ 16,384** (headroom 11,289 B; was 4,609) |

The 4-byte length prefix lies outside the bound. The request, the diagnostic limit, the 16 KiB frame and `MaxDepth = 2` are all unchanged.

## 6. Inherited authentication and correlation (no new cryptography)

The following carry over unchanged from ADR-018/025:
- protected identities, ACL, reparse and same-signer checks before launch;
- a per-request pipe restricted to the user and SYSTEM;
- verification on both ends of peer PID, token, session and SID (`WindowsPeerIdentityVerifier`);
- bootstrap carrying only protocol, PID, pipe and nonce;
- a one-shot 32-byte nonce (64 uppercase hex characters);
- 2-minute request freshness and the 30-minute review ceiling;
- one request per helper.

Correlation:
- The helper hashes the **received canonical payload bytes**. That hash must equal `operationDigest`, and `TryRestore` must succeed.
- The desktop compares `jobId`, `operationDigest` (= `operation.Digest`) and `nonce` using fixed-time comparison.
- After reading the single response frame, the desktop requires pipe EOF.

There is no frame MAC; integrity rests on the pipe ACL and peer identity. Helper observations are authenticated helper observations only; they also require H and the desktop's own postcondition checks.

An uncorrelatable request gets no response, so the desktop records Uncertain and the barrier stays, with no retry (§9 O8). Budgets are unchanged: connection 30 s, execution 11 min, exit 5 s. The 15-minute (helper) versus 11-minute (desktop) inconsistency is recorded only.

## 7. Versioning and compatibility

- Protocol value `"2"` is **redefined in place**: this 20-field response replaces the passive 15-field shape (§9 O1). The 15-field reader and writer are deleted, not kept alongside. A 15-field `"2"` frame becomes a negative test. The 15-field response text in Mapping V2 is marked as superseded by this record, through the integrating record rather than by rewriting the pinned artifact.
- Each desktop/helper pair accepts exactly one version, pinned in the ADR-022 manifest. `Supports` checks that capability before any claim. A mismatch discovered after the request is written is Uncertain; there is no fallback or retry.
- The following reject the **whole frame**: unknown, missing, duplicate or reordered fields; unknown or alias enum values; wrong JSON types; `null` in a non-nullable field; a value present where S6–S8 require null; any non-canonical encoding.
- Protocol-1 frames and helpers are rejected both ways. There is no downgrade.

## 8. Required negative and boundary tests

| Group | Cases (each rejects, or produces the stated result) |
|---|---|
| Framing / encoding | Each of the 20 fields missing. An extra or duplicate field. Adjacent fields swapped. Whitespace, trailing comma, comment. Unicode-escaped names or values. Lone surrogates. Numbers as strings and strings as numbers. `null` in fields 0–15 or 19. An empty-string version. A lowercase fingerprint, or one with 63 or 65 digits. A 129-character version. Versions `1.0+meta`, `01.0`, `1.0`, `1.0.0.0`, `1.0.0-`. `exitCode` values `2147483648`, `0.0`, `-0`, `1e0`. Booleans sent as `"false"`, `0` or `1`. A 16,385 B frame. Trailing bytes, or a second frame before EOF. A diagnostic that is oversize, has control characters, or contains an unredacted sensitive value. An **explicit 15-field legacy protocol-2 frame**. |
| Enums | Wrong case. The numeric alias `"1"`. An unknown reason. |
| Correlation | Wrong job, digest or nonce. A mutated payload with a **recomputed** digest. Protocol-1 frames. Protocol `"1"` or `"3"`. A response arriving after the execution budget → Uncertain. |
| Structural S1–S12 | `Refused` + `Started`. `Refused` + `None`. `Matched` + any reason. `Undetermined` + `Started` or `Unknown`. `Matched` with a null value. `RequestNotAdmitted` with values. A candidate with `PreflightQueryFailed`. A candidate without an installed version. `SourceNotCommunity` with a fingerprint. `Refused` with `cancelled` or `timedOut`. `outputLimitExceeded` with a non-query refusal. A settled `Undetermined` with no flag. |
| Operation-bound P1–P5 | `Matched` with a differing installed version, candidate or fingerprint. `ProfileMismatch` with fingerprint = `F`, or with a changed installed version. `CandidateMismatch` with an equal candidate. A stage-D `InstalledMismatch` whose candidate ≠ frozen. Each case yields Uncertain, the barrier, and no `Confirmed` value, even if the frame says `NotStarted`. |
| Phases (M4) | A malformed `EvidenceId` rejected by the domain and by `Supports` → NotStarted with **no claim**. Missing manifest capability before the claim → NotStarted with no claim. `StillSupported` failing after the claim → `Cancelled`/NotInvoked. A settled wire response without H → Uncertain. |
| Late stages (M1) | A freshness or 30-minute lapse after stage C → `Refused/ApprovalExpired`, settled `Refused`. Stage-D drift → stage-B reason with a valid stage-C candidate. A preflight query timeout → `Undetermined`, which settled becomes `Failed`. Preflight overflow → `PreflightQueryFailed`. An uncaused `Matched ∧ NotStarted` (launch failure) → `Failed`/NotInvoked. An ambiguous launch → Uncertain. |
| Mapping / outcome | Each reason, settled → `Refused`. `Started` + exit 0 + `cancelled` → `Unverified` with the verifier not called. Flag-precedence combinations. Exit 3010 → `Failed`. A UAC denial → `ElevationDenied`. The candidate-check value never changes the outcome. A diagnostic containing the "matched its protected machine checks" phrase does not produce `Confirmed`. |
| Candidate-check / history (M2) | Only the adapter sets a non-default value (architecture test). No conversion to `ReviewedCandidateProvenance`. The value is persisted at finalization as desktop-derived. A tampered persisted value → row unreadable and blocking. The persisted value is never consumed by review, admission or dispatch. |
| Verifier (L5) | ClientChanged / Unverifiable → the fixed message and `Unverified`, never Success. A Chocolatey verifier returning `RestartPending = true` → rejected (`Unverified`). A detail other than `None` combined with `ExpectedVersionDetected = true` → rejected. |
| Differential (O9) | The codec's version grammar agrees with `ChocolateyNativeVersion.TryParse`/`Normalized` over a shared corpus of valid, invalid and boundary strings. |
| Positive boundaries | The largest valid response (5,095 B) round-trips byte-exactly. A minimal `Refused/RequestNotAdmitted`. A full `Matched`. `Matched ∧ NotStarted ∧ cancelled` → `Cancelled` with `Confirmed` text. |

## 9. Resolved dispositions

| # | Disposition |
|---|---|
| O1 | **Redefine `"2"` in place.** ADR-025 allocates the exact value "in reviewed implementation". The 15-field shape was never deployed and never used by an endpoint. Allocating `"3"` would leave a defined but unused `"2"` that a future reader could accept. The cost is deleting the 15-field reader and writer and marking the Mapping V2 response text as superseded (§7). |
| O2 | Enforce `EvidenceId = "choco-profile-v2:" + 64 uppercase hex` in `ReviewedUpdateOperation` for `ChocolateyMachineRuntimeLimitsV3`, at both construction and restore, and again in `Supports` before the claim. A non-conforming restored row becomes unreadable and blocking; it is never admitted. |
| O3 | History must persist the value (§4.2). The format is an implementation and review matter. |
| O4 | Keep `RestartReported = false` for this route in Task 50. Profile V2 forbids restart inference; any change belongs to Task 51. |
| O5 | The desktop runs its own file-first check. Whether it can read every `ChocolateyClientStateData` input without elevation is native evidence required before admission. |
| O6 | Show values only from accepted frames, labelled "reported by the helper"; persist them per §4.2. |
| O7 | Stage D (pre-launch revalidation) is added. The HTTP-cache protection check runs before any elevated client command (stage B, repeated in stage D). |
| O8 | Keep the barrier; never retry. |
| O9 | The codec may use visibility or its own equivalent grammar, but it **must** pass the differential test against `ChocolateyNativeVersion`. |

**Remaining owner question (contingency only):** if native evidence shows that the non-elevated desktop cannot read the file-first check inputs, every update on this route would end `Unverifiable`. Two options would then need an owner decision:
- accept the route as permanently unusable; or
- approve a helper-side, negative-only client-check field, which requires a new protocol version.

There are no other open owner questions.

**Non-goals:** WinGet; changes to protocol 1 or the request; progress frames; a wire `Success`, `Final` or `HelperQualified`; new deadlines; native observer implementation; restart inference; relaxing verification or claim barriers.
