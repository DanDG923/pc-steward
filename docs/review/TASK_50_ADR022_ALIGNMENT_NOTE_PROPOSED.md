# Task 50 — ADR-022 alignment note (G2), proposed wording

> **Proposed — requires owner acceptance; not applied to ADR-022.** This file only drafts the note that [adapter gates rev 2](TASK_50_ADAPTER_GATES.md) §A.10 G2 and §R "Authority" (a) require. ADR-022 is unchanged. Under ADR-025, effective ADRs change only through the owner's accepted amendment process. Nothing here grants redeploy, signing, certificate, host-read, UAC or update authority.

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`; ADR-025's ADR-022 amendment row ("exact future payload manifest must identify supported operations/protocol").

## Proposed note text (to append to ADR-022 after owner acceptance)

> ### Alignment note — protocol-2 capability statement (ADR-025 ADR-022 row)
>
> **Status: Proposed for owner acceptance.** This note records how the owner-accepted ADR-025 amendment to ADR-022 is met. It does not change the Task 48 pinned-payload amendment, which still governs installation, and it is not itself deployment approval.
>
> 1. **Manifest fact.** A future payload identifies its helper protocol and supported operations with one pinned file, `PCSteward.DeploymentCapability.txt`, at the payload root. It contains exactly `pc-steward-deployment-capability:1`, `helper-protocol:2`, and one to eight allowlisted `operation:` lines (today only `operation:Update/ChocolateyMachineRuntimeLimitsV3`): printable ASCII with LF line endings and at most 1,024 bytes. A unit test generates the file from the compiled protocol value and route allowlist. Its SHA-256 appears in the owner-approved candidate manifest, so the approved manifest hash pins it.
> 2. **Ledger provenance.** When the pinned manifest contains that file, the installer records `payloadOrigin = "pinned-unsigned-v2"`; otherwise it keeps `pinned-unsigned-v1`. The ledger schema stays `pc-steward-owner-only-protected-test-v6`, and its key sets per writer are unchanged, so removal and recovery are unchanged. The independent validator recognises `pinned-unsigned-v2` only together with a valid capability file, and it reports `helperProtocol`.
> 3. **Runtime reading, not a new runtime capability.** Task 48 says "no runtime updater capability changes". This note keeps that: it adds no update capability, provider, operation or privilege. The desktop and helper only **read** the protected ledger, the pinned candidate manifest, the capability file and the hashes of a fixed binding set. The binding set is the desktop apphost and `PCSteward.Desktop.dll`; the helper apphost, assembly, deps and runtimeconfig; the helper's project-assembly closure; and the capability file. They read these to *refuse* a mismatched or partial deployment before any claim, UAC or provider launch. The result is a consistency gate, never eligibility evidence, never cached and never persisted. ADR-018 launch trust remains the security boundary.
> 4. **Existing deployments.** `legacy-v6` and `pinned-unsigned-v1` deployments, including the owner's current host, report protocol-2 as **Absent** and gain nothing. Each desktop/helper pair accepts exactly one protocol; a protocol-2 helper never accepts protocol 1.
> 5. **No authority follows.** A v2 deployment requires a separately reviewed redeploy-template/plan-schema change (the existing exact plan hard-codes the `legacy-v6` preflight and the `pinned-unsigned-v1` postcondition) and a separate exact owner approval. This note grants no redeploy, signing, certificate, recipient or distribution authority.

## Reconciliation with ADR-022 Task 48

| Task 48 statement | Effect of this note |
|---|---|
| Consume the separately reviewed unsigned payload and the approved manifest hash | Unchanged. The capability file is one more pinned payload file, verified before any signing. |
| Preserve non-signed byte identity; prove signed-executable image identity | Unchanged. The capability file is unsigned, so its manifest hash equals its ledger hash. |
| Retain provenance in the protected ledger and the independent validator | Extended by one origin value (`pinned-unsigned-v2`). The schema version is unchanged. |
| Fixed roots, elevation/SID/mutex, certificate journal, separately approved removal/recovery | Unchanged. |
| "No runtime updater capability changes." | Kept. The runtime only reads protected facts to refuse. Admitting the route stays a separate gate (`HasSupportedMode`, native qualification, A-Q1 Windows evidence). |

## Follow-up script changes (not made in this slice; each needs Windows/pwsh evidence and review)

These come from §A.3, §A.7 "Regression"/"Rollback" and §R A2-L1/A2-L2. They change no host by themselves; running any of them needs separate exact owner approval.

1. `scripts/OwnerOnlyPayloadIntegrity.ps1`, `Read-OwnerOnlyCandidateManifest`: a manifest that contains `PCSteward.DeploymentCapability.txt` is a v2 payload. Validate the capability file's exact bytes, using the same rules as `DeploymentCapabilityPolicy.TryParseCapability`. A malformed v2 capability file must fail the install **before signing**.
2. `scripts/OwnerOnlyPayloadIntegrity.ps1`, `Assert-OwnerOnlyPayloadProvenance` / `Assert-OwnerOnlyPayloadProfile`: recognise `pinned-unsigned-v2` only with a valid pinned capability file. Keep `Unrecognised pinned-payload provenance` for every other origin.
3. `scripts/Install-OwnerOnlyProtectedTestDeployment.ps1`: write `payloadOrigin = 'pinned-unsigned-v2'` if and only if the capability file is in the pinned manifest. Replace the hard-coded `pinned-unsigned-v1` post-install check (currently `if ($origin -cne 'pinned-unsigned-v1')`) with "the origin implied by the pinned manifest". Do not change any other ledger key: the C# policy's closed per-writer key sets mirror the current writers exactly.
4. `scripts/Test-OwnerOnlyProtectedTestDeployment.ps1`: report `helperProtocol` for a v2 deployment.
5. `scripts/Invoke-OwnerOnlyApprovedRedeployment.ps1`: the template hard-codes the `legacy-v6` preflight (`$legacy.payloadOrigin -cne 'legacy-v6'`) and the v1 postcondition (`$proof.payloadOrigin -cne 'pinned-unsigned-v1'`). A v2 redeploy needs a reviewed template or plan-schema change that names the expected old and new origins (A2-L2). The existing plan is left unchanged, so it can still deploy only v1.
6. Payload build: check in `PCSteward.DeploymentCapability.txt`, publish it at the payload root, and add the unit test that its bytes equal `DeploymentCapabilityPolicy.CanonicalCapabilityBytes()`. Add a build test that `DeploymentCapabilityPolicy.BindingSet` equals the published helper `deps.json` project closure. Today a source-level test compares it with the `ProjectReference` closure.
7. Script fixtures (A.7 "Regression", A2-L1): on Windows CI, generate ledgers with the real writers (pending with a null thumbprint, pending with a certificate, rollback with zero, one and several stores, legacy-v6, v1, v2). Feed them to `DeploymentCapabilityPolicy.TryReadLedger` byte-for-byte. Today's fixtures are transcribed key-for-key from the scripts and rendered in the pwsh 7 `ConvertTo-Json` shape, because pwsh is not available in the Linux environment. Also cover: v1 manifest gives `pinned-unsigned-v1`; v2 manifest gives `pinned-unsigned-v2`; the old `Test-` script on a v2 ledger throws.

## Owner decision requested

Accept, amend or reject the note text above for appending to ADR-022. Acceptance does not approve any redeploy. A v2 redeploy still needs the follow-up script review and a separate exact plan approval (A.3.1).
