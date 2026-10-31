# Task 50 — concrete provider and protocol mapping, revision 1

Status: **proposal for fresh preimplementation mapping review**. Requirements `FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03–04/07/13`. This is not an implementation/host/deployment pass. Current helper source remains protocol1. The owner selected one protected deployed build; legacy standalone coexistence is unsupported. Replacement/quiescence/scheduled-entry evidence stays mandatory.

## Closed mode and route table

The operation is Update only. Existing `ReviewedInstallerMode` has Selected(Silent/Interactive) or NotControllable(ProviderScriptControlled). Unknown/inapplicable values reject. NotControllable is derived from the compiled provider profile and explicitly displayed; a requested Silent never becomes NotControllable automatically.

| Provider/source/scope | Required reviewed binding | Execution eligibility |
|---|---|---|
| WinGet/winget/User | Selected(Silent), WinGetUserExactV2 | Conditional on complete independently fresh native target/source/version/scope/architecture/channel evidence below |
| Chocolatey/Community/Machine | NotControllable(ProviderScriptControlled), ChocolateyMachineRegistrationV2 | Conditional on the pinned native registration/client/input profile below |
| WinGet/winget/Machine | Unavailable | No helper-independent supported resolver in this scope |
| WinGet/msstore/any | Unavailable | Store information-only |
| Any Interactive | Unavailable | No proof that installer UI cannot widen effects/target/scope |
| All other combinations | Unavailable | Positive allowlist only |

The matrix is a mode mapping, not an assertion that a given host/item qualifies. Review, application admission, runner and helper independently derive the profile and compare the complete canonical data. A changed contract needs a new explicit identifier/schema/protocol as applicable, never reinterpretation of missing data.

## Frozen canonical data and liveness

Retain the existing strict operation schema2 (24 canonical lines) in `ReviewedUpdateOperation`: header, Update discriminator, job/correlation IDs, requester SID, original review time, complete existing selection (stable product/provider/source/package/installed/target/scope/architecture/channel/mapping evidence), native-instance ID, installation-context ID, evidence ID/time, explicit route-contract ID and exact mode union fields. Serialize the unused union member as literal None. Digest covers all bytes. No executable path, URL, flags, environment, output destination, working directory or isSafe assertion is present. The old job digest remains unchanged and non-authorizing.

Native IDs must originate in fresh trusted observations, not a display row or a regenerated display fingerprint asserted as independent proof. The current live inventory handoff copies observations before persistence and preserves coverage warnings. The target preparation must demonstrate each required native fact and its applicability; absent/conflicting evidence refuses. A route ID is comparison data, not authority.

Save-before-display returns the exact draft generation. Confirm is process-local and original-deadline bound. Claim CAS matches full bytes/digest/generation and reserves terminal capacity under reconciled ownership; no permit before certain commit. Runner consumes its one-use live permit and independently re-prepares evidence. Legacy/restored rows never recreate consent. Final requires claim/ownership-bound positive native settlement; pending/faulted/unresolved work retains DispatchClaimed/Uncertain exclusion, irrespective of visible history. No further wait on potentially unbounded ownership roots and no automatic late clearing.

## WinGet user mapping

The fixed CLI arguments are the current exact user golden plus **only `--silent` appended**:

`upgrade --id <validated ID> --version <validated target> --source winget --exact --scope user --architecture <closed architecture mapping> --disable-interactivity --silent`

No force, unknown-version acceptance, installer arguments, alternate source, implicit agreement acceptance, elevation call or retry exists. Mode validation and all liveness/identity checks precede process creation. Passing the manifest-declared silent mode is the proved mapping; actual installer compliance remains host-only/unproven.

Reuse the current structured candidate authority, with an explicit user-scope observation where needed. The locally pinned ComInterop1.10.340 metadata includes installed scope, ProductCodes/PackageFamilyNames, Channel, and applicable-installer scope/architecture/elevation metadata. Primary IDL: [v1.10.340](https://github.com/microsoft/winget-cli/blob/v1.10.340/src/Microsoft.Management.Deployment/PackageManager.idl), blob `29f2e9166afe38a4add5f7b07acfd344f3d73a43`. No COM activation/OS inventory was performed during this inspection.

Required: predefined/trusted winget catalog; exactly one exact ID match, no truncation; exact installed and requested candidate versions with strict Newer; user-installed association; exact native product/registration association (ProductCodes or PackageFamilyNames compared with direct registry/MSIX observation); compatible architecture/channel; applicable user installer for the requested architecture/mode. Do not equate a registry-view label with independently observed payload architecture. Positive cases may use current-user MSIX's native architecture; an unproven registry architecture combination stays unavailable until a supported proof is identified. This coverage reduction must be visible, not hidden behind a mode-only matrix. No installed-architecture getter was found in this SDK's simple metadata enum; do not invent one or infer missing values.

The fresh review/runner producer should consume the direct `LiveInventoryObservation`, requester identity from held gate/native token context, and structured provider observation. It must return unavailable where these do not establish the exact target. Read-only display assessments may still use stored snapshots; execution facts may not be replaced by them.

## Chocolatey native profile

Pins: choco2.7.5 `194ac6262ac75571c44aa0a431c3ab6f36fa73be`; NuGet.Client3.5.0 `d08e5cc246b384fd424e99371168d7cf4534723e`. Detailed primary-source ledger and independent blob checks are in [capability evidence](TASK_50_CHOCOLATEY_CAPABILITY.md). Admit only the reviewed client version, protected canonical ProgramData path, trusted signer and unchanged executable/tree identity. No claim about the published/installed binary or cross-session host behavior is made by source review.

| Field/input | Required observation or compiled policy |
|---|---|
| Native instance | Complete resolver-matching V2 root-plus-one-level nupkg/nuspec observation; bounded archives/XML/enumeration; retain duplicates before normalization; reject unknown/V3/unreadable/corrupt/ambiguous layouts |
| Installed version | Exactly one native registration = exact local-list row = outdated installed version = frozen review |
| Candidate | Exact fixed-feed outdated version = frozen target; strict Newer; stable/non-pinned; no silently dropped duplicate rows |
| Persistent package inputs | Refuse .version override, .sxs legacy state and .pin; bounded .chocolatey registration observation. Installed-origin feed remains explicitly unknown/display-only per spec733 |
| Scope/context | Protected machine client/native package store; native machine registration targeting. No user Chocolatey route |
| Architecture/channel | Native package registration has no independent application-payload architecture proof. Compiled route policy is ProviderScriptSelected with no x86/forcex86 override and stable candidate/no pre flag. Do not portray Neutral as observed payload architecture; script selection/side effects are explicitly disclosed. Review must resolve whether this typed route policy satisfies the retained native-package contract before admission |
| Source | Effective repository must remain exactly compiled Community URL. Mirror key-before-name invariant-culture case-insensitive/no-trim alias resolution. Reject any effective redirection, delimiters or ambiguity. TLS client certificates/proxy behavior are disclosed according to actual effect, not described as repository substitution |
| Configuration | Read bounded protected-root XML with DTD prohibited; compatible checksum enforcement, remembered arguments OFF, default supported PowerShell host, no unreviewed safety/targeting override. Unknown required effective settings refuse |
| Licensed mode | Refuse licensed DLL at either pinned discovery location and either root/profile license file; resolve actual elevated token profile, reject unreadable observations. Licensed options can override CLI |
| Native code tree | Protect/revalidate bin, lib, .chocolatey, config, hooks, extensions, license, helpers and tools; no reparse points or caller-writable code/ancestors. Require bundled checksum.exe, shimgen.exe and 7z.exe; absence permits unsafe environment fallback and therefore refuses |
| Environment | Compiled ChocolateyInstall = verified canonical root. No environment payload. Native startup TEMP/TMP policy and effective cache path must be demonstrated safe; do not assert that a launch override survives the client's later environment refresh |
| Timing | Fresh complete observation immediately before invocation; revalidate identity/tree/config/registration after preflight queries; original review<=30min and request<=2min checked again before launch. Existing Update execution/teardown deadlines unchanged |
| Verification | Re-run residue-aware native registration observation plus exact target version and source-specific verification; never Success from exit alone |

Fixed upgrade arguments are the existing constrained golden plus **`--ignore-remembered-arguments --fail-on-not-installed` appended**. These only narrow remembered-option substitution and missing-registration install behavior. No silent/interactive flag is added; no install/uninstall operation, force, params, installer arguments, checksum bypass or alternate source is expressible. Candidate lookup is read-only; source-alias/client/input checks precede even that lookup. All preflight work is bounded within request/review/existing item budgets; no new deadline is assumed here.

The profile's architecture policy is compiled under its explicit route ID, not a caller-selected architecture override. If the review finds that the canonical profile/selection cannot express it without ambiguity, add a typed explicit binding and version/migrate the canonical data before enabling it; do not silently reinterpret Neutral or a missing field.

Community disclosure before confirmation: package, hook and installer scripts execute elevated and may resolve by-name commands/modules through user-influenced Windows PATH/module locations that the client rehydrates. Fixed client/helper/tool paths do not make script dependencies hermetic. Dependencies may be installed/upgraded from the explicit source; Update does not inherit Install-only no-new-dependencies rules. This disclosure cannot replace a native target/source/guard/compatibility check. Native helper/module/tool bootstrap paths must stay protected. The joint council accepted this boundary; no new owner waiver is inferred.

## Proposed protocol2 exact envelope

New endpoint accepts **only protocol2**. Bootstrap remains only protocol/PID/pipe/nonce. Preserve ADR-018 protected pair, signer/ACL/reparse, ancestry, PID/token/SID/session, random user/SYSTEM pipe, nonce and teardown checks. No fallback to protocol1. Historical protocol1 fixtures stay historical; new-endpoint rejection tests cover both deployment mismatches.

Request is one strict UTF-8 JSON object with exactly six required **string** properties in deterministic serialization order:

1. protocolVersion = literal 2;
2. operation = literal UpdatePackage;
3. operationPayload = canonical schema2 bytes encoded in canonical Base64 (re-encoding must match exactly);
4. operationDigest = uppercase SHA256 of canonical bytes;
5. nonce = fresh 64-character uppercase hexadecimal value bound to bootstrap;
6. issuedAtUtc = canonical UTC O-format just-in-time timestamp.

Canonical payload itself binds job/correlation/requester/review and every operation field. No duplicated optional mode/profile fields and no desktop safety assertion. Helper derives the fixed matrix/profile, independently observes the exact Chocolatey native operation and compares it. Non-machine, Store, WinGet-machine, unknown contract/mode, stale review/request, malformed/noncanonical/duplicate/missing/extra/inapplicable fields reject before provider launch. Helper never opens SQLite or authenticates desktop UI consent from persisted data.

Response is a strict object with required fields: protocolVersion/string2, jobId/canonical GUID, nonce/64hex, operationDigest/64hex, outcome/closed existing helper outcome enum, exitCode/integer-or-null, diagnostic/sanitized bounded string, providerStarted/Boolean, directProcessExitObserved/Boolean, ownedIoCompleted/Boolean, pipeEofObserved/Boolean, terminationFailed/Boolean, executionUncertain/Boolean. Validate outcome/fact consistency. These are authenticated helper-observed results, never desktop request assertions; desktop separately observes helper root and performs target verification. No Task crosses the wire. Unknown result facts cannot create settlement.

Keep the existing **16-KiB full frame** limit and Q1 progress bounds. Existing canonical parser's 16-KiB input ceiling is not a promise that a maximum-size input fits the envelope: valid constructed records currently fit below4KiB. Tests must serialize the worst valid canonical record plus full envelope, exercise Base64/UTF8 expansion and reject oversized frames before materialization. Do not increase the accepted frame limit to hide a conflict.

## Producing/consuming seams and mandatory review questions

Application owns live review/native-target preparation and claim/permit orchestration; Storage owns session lifecycle and marker preflight; provider adapters own fixed argument/native API mappings; Execution owns strict protocol/runner admission and bounded owned-process execution; SQLite owns passive canonical/draft/claim/history data. No arbitrary paths/commands/options flow from metadata/database into these seams.

Before production callers change, record concrete signatures for native target/profile evidence and reuse or extract the existing bounded owned-process seam for Chocolatey without introducing a provider-to-Execution dependency cycle. Current Chocolatey process wrapper does not yet prove the stronger owned-work contract. That is an implementation prerequisite, not solved by the wire schema.

Fresh reviewer must separate: (1) ready strict mode/argument/serialization mapping; (2) exact native profile facts still missing; (3) owner decisions versus ordinary fail-closed engineering choices. In particular assess registry architecture qualification, explicit ProviderScriptSelected representation, effective cache/TEMP, full native observation completeness, helper response settlement consistency and worst-case framing. An unproven profile remains unavailable. No mapping review can authorize live actions, relax accepted constraints, or close Task50 without actual positive/negative integration tests.
