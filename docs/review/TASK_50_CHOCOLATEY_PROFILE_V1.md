# Task 50 — proposed compiled Chocolatey Update profile

Requirements: `FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03–04/07/13`. **Proposed mapping for independent review before implementation.** No endpoint/profile is enabled. This defines a potentially supported profile, not the owner's installed configuration. No host/client query, UAC, deployment or real update is authorized.

## Scope and canonical binding

Only `Update`, current canonical schema3, route `ChocolateyMachineRegistrationV3`, provider Chocolatey, source ChocolateyCommunity and scope Machine can match. Mode must be exactly `NotControllable(ProviderScriptControlled)`; architecture must be exactly `ProviderScriptSelected` with no architecture value; channel must be Stable. Selected Silent/Interactive, Observed/Neutral, another operation/route/provider/source/scope/channel, malformed unions and legacy schemas refuse. The registration is the Chocolatey package, not an ARP entry or an assertion about script-selected payload architecture.

Canonical fields/4096-byte payload, original live approval, requesting SID, 30-minute nonrenewing review lifetime, separately fresh evidence, claim generation/CAS, terminal reservation and protocol2 framing remain as [revision2](TASK_50_PROVIDER_MAPPING_V2.md). There is no fallback to protocol1. A digest or parsed profile observation cannot mint approval. Desktop gate SID, reviewed requester and authenticated helper peer SID must agree.

WinGet machine and Store remain unavailable. The proposed narrow User CLI mapping also remains unavailable: the pinned MSIX deployment path uses add-or-update, with weak correlation possible, rather than an existing-only guard. Main adopts the stricter ADR-025 Update-instance interpretation; Fable records a different reading of the concurrent same-user race. That disagreement is not permission to enable the route. It does not block investigation of this Chocolatey profile or require an owner question now.

## Source pins and evidence limits

| Component | Pin |
|---|---|
| Chocolatey open-source client | 2.7.5, commit `194ac6262ac75571c44aa0a431c3ab6f36fa73be` |
| Chocolatey NuGet.Client fork | 3.5.0, commit `d08e5cc246b384fd424e99371168d7cf4534723e` |
| .NET environment dictionary | runtime v10.0.0, Windows OrdinalIgnoreCase keys |

The [capability ledger](TASK_50_CHOCOLATEY_CAPABILITY.md) contains historical findings and corrections. The compiled admitted client set must be explicit; a major-version floor or equal version string alone cannot attest another implementation. Existing protected-path/vendor trust checks are retained. **Open artifact condition:** establish the trusted published-client/artifact relationship for this source pin before positive native admission; record exact evidence rather than inventing a digest from a host that was never read. Source/build-pipeline evidence and actual artifact identity are distinct. A synthetic fixture can demonstrate the predicate without establishing a real artifact's eligibility.

## Native observation and enforcement table

All observations occur independently inside the helper against its native identity. A desktop snapshot, persisted row or caller-supplied Boolean is never the producer. Relevant inputs are checked again immediately before the fixed upgrade. Changed, unreadable, ambiguous or unsupported input refuses; no cleanup/configuration repair is performed.

| Input | Fixed rule and classification |
|---|---|
| Client | Fixed ProgramData Chocolatey executable; exact admitted artifact/version; exact vendor signature/trust; caller cannot modify it or its ancestors; no reparse component. Keep native/file identity bound through invocation. |
| Root | Set the child environment's sole case-insensitive ChocolateyInstall value to the verified root. This compiled value is not a request field. Both reviewed source branches then resolve that root. |
| Code/data tree | Observe root/bin/lib/.chocolatey/config/hooks/extensions/license/helpers/tools and their reachable descendants. Reject caller-writable or reparse-controlled state, access/coverage failure or bound overflow. Administrator replacement remains a trust assumption, not a claim that a snapshot prevents it. Revalidate identities before invocation. |
| Mandatory tools | Require regular protected `tools/checksum.exe`, `tools/shimgen.exe` and `tools/7z.exe`. Their absence would permit helper fallback after environment refresh to user-controlled paths. Missing means unavailable, never use fallback. |
| Licensing | Reject either root `chocolatey.licensed.dll` or `extensions/chocolatey/chocolatey.licensed.dll`, root `license/chocolatey.license.xml`, or the actual elevated identity's profile `chocolatey.license.xml`. Resolve profile from the token, not USERPROFILE. Missing discovery coverage/unreadable state refuses. Licensed options can override CLI options, so this profile does not admit them. |
| Configuration | Read protected-root `config/chocolatey.config` as bounded untrusted XML. Resolve supported effective feature/default precedence; require checksumFiles=true, remembered upgrade arguments=false and usePowerShellHost=true. Reject duplicate/conflicting relevant entries and unsupported effective state. This is an observation, not per-invocation enforcement. Exact XML model/default handling must be pinned before the parser is written. |
| Checksum guard | Always append `--require-checksums`, which disables both empty-checksum features in the pinned command. ChecksumFiles must independently be observed enabled. A package without the required checksum may fail after claim; no verified success follows. |
| Remembered options | Require feature off at admission to preserve the already-selected default behavior; always append `--ignore-remembered-arguments` as an invocation guard. Never import remembered arguments into the request. |
| Effective cache/TEMP | Configured cacheLocation must be nonempty, absolute, contain no environment expansion and resolve to a caller-protected directory with protected/no-reparse ancestors. Observe or refuse; never create a new cache directory. Empty defaults to process TEMP/chocolatey and is unavailable. Pinned native preparation sets TEMP/TMP to CacheLocation after environment refresh. |
| Source | Only the compiled Community URL. Apply the pinned resolver's key-before-name, invariant-culture case-insensitive/no-trim precedence. Require its effective repository string to remain exactly the compiled URL; reject alias redirection and unsupported configuration. Fixed URL contains no semicolon/comma. Client TLS certificates/proxy configuration retain their actual transport implications; do not invent a server-certificate bypass or call the route proxy-free. |
| Per-package store | Reject target .version override, .sxs and .pin. Require complete coverage of the matching store identities, not a filename inferred from untrusted display data. .sourceInstalledFrom remains display-only under spec733; it cannot select execution source. |
| Library | Independently enumerate the resolver's complete V2 discovery set: root lib top-level nupkg files plus each immediate subdirectory's top-level nupkg files. Inspect embedded nuspec identity, not directory naming. Reject unknown/V3 classification, any enumeration/read/archive/XML error, incomplete coverage and limits. Exactly one file/identity for target ID, case-insensitive; do not deduplicate away residue. |
| CLI agreement | Require unique local-list and outdated records, not a silently duplicate-dropped set. Native identity/version = list installed = outdated installed = reviewed installed. Outdated candidate must equal the reviewed stable strictly-newer target, with no pin. A stale/missing/disagreeing candidate refuses. |
| Update-only guard | Append `--fail-on-not-installed`; pinned NugetService checks it and errors/continues before its install branch. No install/uninstall operation or force/unknown-version option is representable. Registration disappearance before that native check refuses. This is not an atomic guarantee against independently changed provider state after its own check; any remaining effect limits must be stated precisely. |
| Post-state | Repeat the same residue-aware native scan for the exact package and compare target version/registration uniqueness. Latest-only list alone cannot verify success. Target check and owned-work settlement are independent. |

## Proposed numeric observer limits

These conservative support limits are compiled policy. Overflow is Unavailable; no partial success, skipping, permissive retry or larger fallback query is allowed. Cancellation is checked during enumeration/read/parsing. Provider queries use their remaining existing item budget; no new Update deadline is added.

| Observation | Maximum |
|---|---|
| Complete protected-tree traversal | 32,768 entries total, depth32, fully resolved path4096 characters |
| Immediate library directories | 1,024 |
| Library discovery nupkg files | 2,048 total |
| One nupkg length | 64 MiB |
| Aggregate nupkg lengths in observation | 512 MiB; reject before archive work exceeds it |
| ZIP directory | 4,096 entries; 1 MiB central-directory bytes; reject multidisk/ZIP64/unsupported structures |
| Embedded nuspec | Exactly one matching archive nuspec; 64 KiB declared **and actual** decompressed bytes; bounded incremental read |
| XML | 64 KiB input/character budget, depth16; DTD prohibited, resolver null; duplicate relevant identity/config fields refuse |
| Feature/source configuration | 64 KiB config bytes, at most256 feature/config entries and64 sources; same XML rules |
| CLI list/outdated | Existing 1,000,000-character capture ceiling plus 1,024 logical records; strict parsing rejects duplicates/malformed rows |
| Each opaque native identity/version string | Existing canonical field bounds; no truncation |

ZIP bounds must be established from a bounded end record/directory range **before** constructing a decoder that could allocate for unbounded entries. Every checked arithmetic overflow, overlap, impossible directory/entry offset, duplicate identity-bearing entry, unsupported compression/encryption, truncated stream or inconsistent declared/actual size refuses. An archive length bound is not by itself a decompression bound. No extraction or package script execution occurs during observation. The native reader and stream parser remain separate and independently testable.

Version equality must follow the admitted NuGet identity semantics, including normalized numeric versions and case rules, or conservatively reject unsupported representations. Never guess a folder suffix or treat two normalized equal files as a unique registration. Normalization and store-location rules need exact pinned source citations before implementation; a matching CLI row does not repair missing native coverage.

## Fixed invocation

Internal `ChocolateyReviewedUpdateArguments.Build(ReviewedUpdateOperation)` will format only the closed schema3 binding above:

`upgrade <validated ID> --version <validated target> --source https://community.chocolatey.org/api/v2/ --yes --limit-output --no-color --execution-timeout 300 --ignore-remembered-arguments --fail-on-not-installed --require-checksums`

No pre/x86/not-silent/force/checksum-bypass/params/install-arguments/dependency-ignore/implicit-extra-source flag exists. The formatter is passive. Only the independently validating helper, reached through authenticated protocol2/live approval/committed claim, may invoke a qualified fixed native binding. Protocol1 argument bytes remain unchanged.

Proposed producing interface: provider-internal `ChocolateyNativeUpdateObserver.ObserveAsync(reviewedIdentity, requesterSid, remainingBudget, ct)` returns typed observations and an immutable item-specific aggregate owned root on every path. It is never constructed from persisted evidence. It includes client/tree/config/store/library/native query coverage and exact registration/candidate facts, or a closed refusal reason. Any unresolved preflight native work prevents upgrade invocation and remains in aggregate settlement. No second wait or lost cancellation/root is permitted.

Proposed consuming interfaces: independent helper validator compares the complete reviewed identity/capability/freshness/SID, then invokes only the same retained native binding with the fixed arguments. Desktop preparation may expose an eligible review only after equivalent native facts are established; helper always observes independently. An internal pure policy evaluates immutable observation values for deterministic fixtures, but its result alone never creates a native binding, approval or dispatch permit.

## Disclosure and settlement

Review must explain: Community package/hook/installer scripts execute elevated; commands/modules they invoke by name can resolve through user-influenced PATH/module locations which the client restores. PC Steward fixes the client/helpers/tools/source/target/version/guards, not third-party name resolution or script-selected payload architecture. Dependencies may be installed/upgraded by the reviewed Update; no dependency-ignore or sandbox promise is added. Installed-origin feed can be unknown under explicit Community mapping. Only target registration is post-verified.

Fixed bootstrap imports/helpers/tools must remain protected; this disclosure cannot waive a native-loader, source, target, checksum or Update-only guard. No profile scripts are run merely because the host sets a profile variable. The pinned CreateDefault host's already loaded built-ins and absolute module imports are distinct from package-triggered module autoload.

Helper protocol2 returns facts within existing bounded teardown: invocation Unknown/NotStarted/Started plus aggregate settlement across **all** preflight/upgrade native work. A response never proves helper exit or target success. Uncertain facts, missing/invalid response or unobserved helper exit retain DispatchClaimed/Uncertain; no Final without positive settlement and applicable independent postconditions. Late observers cannot clear/write shared state. No extra wait on indefinitely pending roots.

## Acceptance work still required

Before implementing an admitting observer: independent review of this concrete mapping/bounds and closure of published artifact, XML model, normalization/store discovery and residual Update-only proof. Then red/green native-fake/archive fixtures, fixed argument golden/mutation negatives, common gate/claim/helper/UI/history composition and fresh integrated review. A positive synthetic admitted profile is not a claim about the owner's host. No all-unavailable completion claim or new owner decision follows from unfinished evidence.
