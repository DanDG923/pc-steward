# Task 50 — Chocolatey capability evidence, pending eligibility

Date: 2026-10-30. Requirements: `FR-UPD-15`, retained `FR-UPD-01`, `AC-UPD-01–11`, `TM-03–04/07/13`. **New route unavailable. No host query, execution or deployed-route vulnerability confirmation.** This is the continuing main/Opus/Fable/Astra capability dialogue, not an independent security-review pass.

## Pinned source and verification

Opus researched Chocolatey 2.7.5 source at commit `194ac6262ac75571c44aa0a431c3ab6f36fa73be`. Main independently fetched these exact files through GitHub's read-only file connector after web-fetch returned DisabledError:

| Source | Blob identity | Verified proposition |
|---|---|---|
| [ApplicationParameters.cs](https://github.com/chocolatey/choco/blob/194ac6262ac75571c44aa0a431c3ab6f36fa73be/src/chocolatey/infrastructure.app/ApplicationParameters.cs) | `8c12a40df39f3233ffa10858994d06a6b9270ee2` | Under the official-key conditional branch, the process ChocolateyInstall environment value takes precedence over the assembly directory. Multiple library/configuration/executable-extension paths derive from that root. This is source behavior, not proof of which branch any installed/published binary uses. |
| [NugetList.cs](https://github.com/chocolatey/choco/blob/194ac6262ac75571c44aa0a431c3ab6f36fa73be/src/chocolatey/infrastructure.app/nuget/NugetList.cs) | `51b2f00c21576db1db791ae493a9534b96c741ef` | Exact-package lookup selects the highest-version result; one exact CLI result cannot by itself establish that no other native registration/residue exists. Complete local repository discovery still needs its pinned dependency contract. |

Current PC Steward `WindowsChocolateyProcess` builds fixed executable/arguments but establishes no explicit child environment policy. This is independently visible in source; it does not prove an exploitable installed-host path.

## Opus findings returned for joint challenge

- Remembered upgrade arguments may reparse broader options after the current fixed invocation. The [upgrade documentation](https://docs.chocolatey.org/en-us/choco/commands/upgrade/) documents the remembered-options feature/override flags. Opus reports stronger source behavior than the documentation's precedence wording; main has not yet independently verified that service code.
- An absent installed registration can turn upgrade into install unless the supported native guard prevents it. The new route must retain update-only semantics at invocation, not rely solely on an earlier observation.
- Version-2 local-only list behavior and removal of side-by-side installs do not prove a clean upgraded library. Observation must follow the actual supported repository resolver, retain duplicates/conflicts and cover legacy state without guessing folder names.
- Proposed fixed root environment, remembered-argument refusal/guard and not-installed guard are deterministic policy candidates, not adopted capabilities or changes to the existing route. Clearing inherited/configured choices can alter script-selected payload/effects; compare observation/refusal against narrowing flags before choosing. No arbitrary environment/argument values may come from metadata/database.
- Candidate package identity is the protected native package registration, not an unrelated ARP record. Installed-origin feed can remain explicitly unknown under specification §733; desktop-owned exact Community mapping/consent is still required.

## Outstanding facts before eligibility

### Subsequent joint-dialogue progress (2026-10-30; delegate-reported source evidence)

Opus pinned Chocolatey's NuGet.Client fork at `d08e5cc246b384fd424e99371168d7cf4534723e` (3.5.0). It reports `FindLocalPackagesResourceV2Provider` accepting FileSystemV2 **or Unknown**, and `LocalPackageListResource` applying latest-version filtering when allVersions is off. Combined with choco's local prerelease-enabled listing and upgrade selection, latest-only agreement cannot prove uniqueness. The future independent observer must follow the actual resolver discovery, retain every matching registration/residue and refuse incomplete, conflicting, unreadable or unsupported layouts. Exact discovery/normalization and guard-option provenance remain in the requested mapping ledger; these reports do not enable the route.

Opus also checked the runtime v10.0.0 `ProcessStartInfo.Environment` implementation: Windows keys use OrdinalIgnoreCase, supporting a fixed verified-root override without a differently cased duplicate. This closes a proposed new-route environment rule only; it does not establish the entire effective input set or an actual elevated-host exploit. The official build scripts/TeamCity configuration select ReleaseOfficial; published-binary provenance remains distinct. M4 affects old-route triage, not whether a compiled root override works under both reviewed source branches.

Joint scope resolved with Astra and Fable: retained Chocolatey Update does not inherit Install's complete no-new-dependencies rule, and no script sandbox is implied. Dependency/package-script effects retain their accepted disclosure. The route still must prove native targeting, Update-only guard, required scope/architecture/channel and relevant executable/configuration/environment input trust. Full protected lib/config/hooks/extensions checks and immediate revalidation are proposed requirements; source/build identity must be admitted for an explicitly reviewed client-version set, not an unrestricted major-version floor. Remembered-options precedence/compatibility, complete resolver observations and checksum admission remain subject to the pinned field-level mapping. Machine Chocolatey stays unavailable. No old-route issue is promoted to confirmed vulnerability by this evidence.

| Fact | Status / next evidence |
|---|---|
| M1 — Published binary build configuration | Official-key source branch and build configuration are not proof of the released/installed binary's build. Need release-build provenance or an exact supported artifact contract. No host read is authorized here. |
| M2 — Complete native registration discovery | Pin the actual NuGet local repository implementation/dependency used by the supported client, including legacy/conflicting/unreadable registration behavior and bounded observer coverage. Names alone are insufficient. |
| M3 — Guard options in the exact supported client | Verify remembered-option and not-installed guard recognition/precedence in the pinned client source; current online docs alone do not prove every client version. |
| M4 — Actual elevated environment origin | Need Microsoft primary documentation for the supported launch/UAC path, including same-user versus alternate-credential behavior. Current source environment inheritance alone does not prove the host exploit. |
| Effective targeting/applicability | Bind or refuse configuration, remembered options and environment affecting root/source/target/architecture/channel/security. NotControllable mode data cannot waive these checks. Package-script-selected effects remain disclosed limitations rather than invented application-architecture proof. |

Potential old-protocol findings (environment redirection, remembered-option widening, missing-registration upgrade behavior) remain **plausible/unconfirmed Task 90 candidates**. No exploit, real client execution, UAC or local configuration probe was attempted. Do not label them confirmed vulnerabilities or silently repair deployed behavior from this record. Main has returned the source-verification results and objections to Opus, with Fable/Astra retained in the dialogue. The compiled new-mode matrix continues to refuse Chocolatey pending evidence; existing installed binaries are unchanged.
