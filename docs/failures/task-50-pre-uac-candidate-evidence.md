# Task 50 — pre-UAC Chocolatey candidate evidence, active consultation

Date: 2026-10-31. Requirements: `FR-UPD-15`, `FR-UPD-01`, `AC-UPD-01–11`, `TM-03–04/07/13`. **This is an unresolved capability/contract question, not a final blocked handoff or authority to change ADR-025.** Independent parser/native-profile work continues.

## Objective and exact requirement

Preserve constrained Chocolatey Update through schema3/protocol2, with native instance/source/candidate, NotControllable mode, live approval/claim/gate and independent helper validation. [ADR-025:70](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md) explicitly gives the desktop “Fresh trusted exact source/installed-to-candidate association” and the helper independent qualification. [Spec732](../product/PC_STEWARD_PRODUCT_SPEC.md) confines the optional Community source to its signed client. Scans never request elevation.

## Evidence and attempted approaches

Pins: choco `194ac6262ac75571c44aa0a431c3ab6f36fa73be`, NuGet fork `d08e5cc246b384fd424e99371168d7cf4534723e`.

1. **Use a fixed ignore-cache control:** ConfigurationBuilder blob `4e366a52902fbc737c1f25baaf9d895216613307`, lines493–503, sets UseHttpCache=false and CacheExpirationInMinutes=-1 for ignore-http-cache. Opus traced ChocolateySourceCacheContext `bd264d03`, fork SourceCacheContext `2c08dbe6`, HttpSourceCacheContext `d2bb7e1b` and CachingUtility `1f193926`: this does not set NoCache; a small positive maximum age still permits global-cache reads, and future-dated metadata can pass. Thus this flag cannot establish cache-independent candidate provenance. It is not invoked on the host during this investigation.
2. **Resolve a protected cache for the desktop:** ApplicationParameters blob `8c12a40df39f3233ffa10858994d06a6b9270ee2`, lines80–89/133–145, computes the cache once. Non-elevated with a profile uses UserProfile/.chocolatey/http-cache; elevated/no-profile uses ProgramData/ChocolateyHttpCache. No supported signed-client NoCache/alternate-HTTP-cache control was found. No profile/elevation spoofing, deletion, ACL change or new persistent directory is authorized. The helper's protected cache does not attest the desktop cache.
3. **Direct HTTP metadata query:** not implemented; spec732's “only through its signed client” excludes silently adding this provider/network path. It cannot be a fallback.
4. **Helper-first candidate qualification:** proposed, not adopted. Desktop would freeze the candidate as explicitly untrusted review intent plus independently protected installed/profile facts, then commit claim/request UAC. The helper would independently qualify the exact candidate through its protected machine cache/client, refusing every mismatch/no-record without retargeting. It protects the execution boundary but does not literally supply trusted desktop candidate evidence before UAC.

No actual provider/client, UAC, owner cache/config, installed software or live application data was queried. Main inspected public pinned source, exact public release ZIP bytes, and synthetic fixtures only.

## Joint dialogue and unresolved interpretation

Main/Opus/Fable/Astra remain involved. Fable reads the native client/user-cache state as same-principal comparison intent and accepts the helper-only authority split with a closed provenance type and disclosure. Astra cites the explicit desktop responsibility and rejects substituting intent for its required pre-UAC qualification. Main currently follows the stricter literal reading; no Actionable/SourceIsFresh Boolean may mislabel user-cache intent as qualified evidence. Opus has received both arguments for another response. This is not settled by a hash, claim, disclosure or later verification.

If no supported signed-client observation can satisfy the desktop clause, the smallest potential owner clarification is whether to retain that two-sided qualification requirement (route unavailable until it can be met) or explicitly amend the desktop responsibility to allow provisional intent with first authoritative qualification inside the approved helper. Neither choice approves deployment, a provider invocation, cache/ACL mutation or weakening helper validation. No such amendment is inferred yet.

The optional owner question was presented on 2026-10-31 with these two choices and is pending. Main/Opus agree there is no currently evidenced signed-client cache-independent pre-UAC candidate path; Fable/Astra's interpretation difference remains recorded. Dependent qualification changes wait for the answer. Independent bounded parsing/native-profile work continues; no elapsed time is treated as approval.

## Current validated work and next action

Latest committed implementation `bda7d3ca7cd9212a9547185f590fbfbd59b8e846`: environment14/config28 focused; Release0 warnings/errors; full729/729, zero skipped. [Task90 mapping remediation](../review/TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md) records commands/logs, source/artifact identity and open B1–B4/M1–M3. These tests exercise passive prerequisites, not execution eligibility. Changed files for this question: this evidence record only; no production call site changed.

Continue independent bounded library/version/store/parser and native-profile work while resolving the exact desktop qualification contract. Do not start Task54B, import installer URLs, use a new provider, infer an all-unavailable Task50 completion, or request an unprepared host action.
