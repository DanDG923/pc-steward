# Task 50 — Chocolatey script-refresh guard capability gap

Date2026-10-31. Requirements `FR-UPD-15`, `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03–04/06/07/13`. Native profile remains **unavailable**. This is a source-evidenced contract/capability limitation, not a host finding, deployment approval or final blocked handoff. Independent implementation continues.

## Concrete chain and stronger finding

Pin: choco2.7.5 `194ac6262ac75571c44aa0a431c3ab6f36fa73be`.

1. Native `PowershellService.PreparePowerShellEnvironment` calls UpdateEnvironmentVariables then SetEnvironmentVariables before each script. EnvironmentSettings blob `7eb5d2629828b543ef2f441c5c88c8bb32e16aaf` clears guard variables and restores root/TEMP/TMP from the supported configuration. The initial compiled environment/config/fixed --require-checksums checks are valid at this point.
2. A package script can call refreshenv/Update-SessionEnvironment, including indirectly through ordinary environment/path helpers. EnvironmentHelper blob `9c825f07e31c383b5586b6d58ad83a1937f2201b`: GetVariableNames110–126 obtains **all** registry environment names; SetVariable138–147 writes Process values directly; UpdateSession217–268 applies Process, Machine, then User and preserves only PSModulePath/USERNAME/processor architecture. It does not re-arm checksum variables, installer arguments or TEMP afterward.
3. A separate medium-integrity process under the requesting SID can pre-place `ChocolateyIgnoreChecksums=true` in HKCU/Environment. The refresh imports that value. Protected Get-CheckSumValid.ps1 blob `e5bc737ace79c48f49878d6c9f87e7deb5ab0b9c`,129–131, then returns with a warning **without verification**. Lines134–143 similarly use user-overridable ChocolateyAllowEmptyChecksums/ChocolateyAllowEmptyChecksumsSecure. This is a conditional, deterministic verifier bypass within a script whose refresh precedes verification, not merely a timing race.
4. Install-ChocolateyPackage.ps1 blob `a9b9715d9c53e5a1bc7d36647b7b24787fe2db7e`,373/407, and Install-ChocolateyZipPackage.ps1 blob `f186d22f77c23641b981ee7f5c38e2032746bacf`,210/228, use the refreshed TEMP for download. User-writable staging also creates the separate checksum-to-execution replacement race. Opus traced Install-ChocolateyInstallPackage.ps1 installer-argument/override reads262/284; those user-imported inputs can alter installer arguments after the same refresh.

The script need not be malicious. A different process supplies the user-hive values. The bootstrap re-arms before the **next** package script, so the evidence does not assert a bootstrap bypass or persistence across scripts. No owner's environment, installed client, registry or package was queried, changed or executed; reachability does not assert the owner has these values or that every package refreshes.

## Supported options investigated and rejected

- Fixed initial environment, protected cache and known CLI checksum flags: effective at startup, overwritten by later in-script refresh.
- Prelaunch hive snapshots: observations only; same-user values can change after the check. Not enforcement.
- Static script scans: cannot soundly exclude indirect/dynamic refresh calls; not an approved control.
- Re-arm through provider hooks, function shadowing or patched helpers: provider-tree mutation/script rewriting and unproven command precedence, outside authority.
- Registry redirection, alternate hives, cache/ACL hardening or provider patches: outside scope.
- SYSTEM: forbidden service/task identity change. Different-account elevation would avoid the requester's hive but violates ADR-018's same-SID pipe/peer identity model. No supported fallback.

No implementation commands failed: this is a missing native capability, established through pinned source and iterative joint consultation. Research commands were read-only pinned GitHub fetches; source fields/blobs above are the exact evidence. Existing runtime/test state is f38b78a, clean Release/full837/837 from5418c84; those passes do not close this gap.

## Joint dialogue and smallest decision

Main, Opus5.5, Fable5.1 and Astra6 exchanged the original TEMP race, its accepted-script-risk interpretation and the stronger verifier/argument-switch evidence. Astra/Fable initially distinguished script effects from a tamper-proof staging promise. The stronger chain meets their expressly excluded case of disabling verification itself. Opus independently confirmed native ordering, user-import reachability and the verifier's early return; also excluded alternate-account elevation. Fable withdrew the baseline-equivalence argument after applying the newer shared-contract requirement that new-protocol Update meet the stronger checks. All agree no supported compiled guard closes this path.

The current accepted text requires independently verified operation-specific controls and provider verification; disclosure alone cannot silently replace that with configured/requested checks. The already accepted **pre-UAC provisional-candidate amendment does not change this requirement**. Arbitrary flags/environment remain forbidden in canonical requests under either possible decision.

Smallest owner clarification: retain effective checksum/installer-argument protection throughout scripts (this client/profile remains unsupported, so Chocolatey updates would be unavailable in a protected replacement until a capable route exists), or explicitly permit this route's configured/requested controls with disclosed script-refresh checksum/argument bypass and TEMP replacement risk. The latter is a security-contract exception, not merely improved wording. No action or deployment is approved by that choice. The explicit two-choice owner clarification was presented on2026-10-31 and is pending. Retain effective guards is the recommended fail-closed option; allowing runtime guard limitations is explicitly a new security-contract exception. No elapsed time is treated as an answer. Independent work continues while dependent admission stays closed.

The same source mechanism is a Task90 candidate for the existing protocol1 route; no deployed-host exploit or live validation is claimed. Do not fix/redeploy that route from this record. Do not infer all-unavailable Task50 completion; full native/gate/claim/endpoint/UI/history work is still unfinished.

## Owner decision recorded — pending choice superseded (2026-10-31)

The two-choice clarification above is no longer pending. The owner answered: “I think Chocolatey is fine as long it uses the official community repo”. Main restated it as acceptance of the disclosed runtime limitations (script-triggered refresh checksum/argument overrides, TEMP replacement window, ordinary script command binding) **for the new Chocolatey route only, conditional on the fixed official Community feed**; Opus/Fable/Astra acknowledged that interpretation. ADR-025, V2_PLANNING_DECISIONS and V2_TASK_CONTRACT record the exception. It is implemented as the distinct canonical route `ChocolateyMachineRuntimeLimitsV3` with derived guard contract `CommunityScriptRuntimeLimits` and fixed disclosure `ReviewedUpdateProviderDisclosure`. Older provisional (`RequiredEffectiveControls`) and legacy reviews cannot inherit it. This is a contract exception, not risk prevention; all other profile, gate, claim, helper-qualification and postcheck duties remain, and native admission stays closed.
