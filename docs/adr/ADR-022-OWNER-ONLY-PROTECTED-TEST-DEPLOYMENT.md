# ADR-022 — Owner-only protected test deployment

**Status:** Accepted for one owner-controlled test machine only
**Date:** 2026-08-05
**Related requirements:** `FR-UPD-01–09`, `AC-UPD-01–08`, `TM-04`

## Context

The standalone private beta is intentionally unsigned and excludes the elevated helper. It cannot execute machine-scope WinGet or Chocolatey updates. The product owner approved an owner-only test route using a temporary local code-signing certificate and the existing protected-helper design; no public, private-distribution or release claim is authorised.

## Decision

`Install-OwnerOnlyProtectedTestDeployment.ps1` is the only supported route for this test. It requires explicit `-Install`, elevation and either an existing code-signing certificate or explicit `-CreateOwnerOnlyCertificate`. The latter creates a fourteen-day non-exportable CurrentUser certificate and adds its public certificate only to the current user's Root and TrustedPublisher stores.

The script creates a collision-failing, non-reparse staging directory beneath an administrator-only `%ProgramFiles%\PC Steward OwnerOnlyTest State` root before publishing, journals a unique intended certificate subject before any certificate-store mutation, and records transaction ownership and the installing Windows SID. It separately builds the WinUI desktop resources and publishes the desktop/helper payloads, requires and merges `App.xbf`, `PCSteward.Desktop.pri` and the compiled resource XBFs, then installs only to `%ProgramFiles%\PC Steward`. It signs both executables with the same certificate, removes inherited write authority on every payload path, grants only SYSTEM/Administrators full control and Users read/execute, then validates all state/payload paths, required WinUI resources, trusted same-signer signatures and a complete file-hash manifest. The desktop application retains its active WinUI window for the window lifetime. One owner-only mutex serializes deployment and recovery. On failure the installer removes only the partial test root, owned certificate entries and state root; if any removal cannot complete, it retains a protected incomplete ledger so the removal script can safely recover the exact test deployment. It never launches PC Steward or updates an application. `Test-OwnerOnlyProtectedTestDeployment.ps1` is read-only validation. `Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove` is retry-safe, requires a separate elevated destructive action by the installing Windows user, validates the protected ledger/root before deletion, and removes only a state-marked test root plus certificate entries the installer itself created; per-user PC Steward data is retained.

## Consequences

- A real update still requires a fresh in-app candidate, visible review and a separate exact action-time approval for one named package/version.
- This route is owner-only test infrastructure, not an installer, updater, distribution channel, signing service or release artefact.
- The temporary certificate and protected root must be removed with the dedicated removal script after test completion.

## Accepted Task 48 amendment — pinned unsigned payload

**Status: Accepted on 2026-10-29 by “I also accept the changes”.** This supersedes installer-time compilation in the historical decision above after implementation/tests/review; it supplies no new host-action authority. The first Task 48 installation passed protection checks but failed the exact candidate desktop DLL/PDB comparison. Opus and parent metadata/PDB checks identified differing CsWinRT-generated code and shared user-writable build intermediates. No launch/retry/cleanup followed.

The [concrete amendment proposal](../review/TASK_48_PINNED_PAYLOAD_FIX_PROPOSAL.md) recommends replacing installer-time compilation with consumption of the separately reviewed unsigned payload and approved manifest hash: copy to protected staging, verify the exact complete payload before any signing, preserve non-signed byte identity, prove signed-executable image identity and retain provenance in the protected ledger/independent validator. Existing fixed roots, elevation/SID/mutex, certificate journal and separately approved removal/recovery remain. No runtime updater capability changes.

Local implementation/fixtures and fresh independent review must precede any revised exact removal/redeploy approval. No host action or launch follows from design acceptance. The historical installed v6 build retains only its originally validated protection evidence.

## Alignment note — protocol-2 capability statement (ADR-025 ADR-022 row)

**Status: Accepted on 2026-11-01 by owner: “Approved”.** This note records how the owner-accepted ADR-025 amendment to ADR-022 is met. It does not change the Task 48 pinned-payload amendment, which still governs installation, and it is not itself deployment approval.

1. **Manifest fact.** A future payload identifies its helper protocol and supported operations with one pinned file, `PCSteward.DeploymentCapability.txt`, at the payload root. It contains exactly `pc-steward-deployment-capability:1`, `helper-protocol:2`, and one to eight allowlisted `operation:` lines (today only `operation:Update/ChocolateyMachineRuntimeLimitsV3`): printable ASCII with LF line endings and at most 1,024 bytes. A unit test generates the file from the compiled protocol value and route allowlist. Its SHA-256 appears in the owner-approved candidate manifest, so the approved manifest hash pins it.
2. **Ledger provenance.** When the pinned manifest contains that file, the installer records `payloadOrigin = "pinned-unsigned-v2"`; otherwise it keeps `pinned-unsigned-v1`. The ledger schema stays `pc-steward-owner-only-protected-test-v6`, and its key sets per writer are unchanged, so removal and recovery are unchanged. The independent validator recognises `pinned-unsigned-v2` only together with a valid capability file, and it reports `helperProtocol`.
3. **Runtime reading, not a new runtime capability.** Task 48 says "no runtime updater capability changes". This note keeps that: it adds no update capability, provider, operation or privilege. The desktop and helper only **read** the protected ledger, the pinned candidate manifest, the capability file and the hashes of a fixed binding set. The binding set is the desktop apphost and `PCSteward.Desktop.dll`; the helper apphost, assembly, deps and runtimeconfig; the helper's project-assembly closure; and the capability file. They read these to *refuse* a mismatched or partial deployment before any claim, UAC or provider launch. The result is a consistency gate, never eligibility evidence, never cached and never persisted. ADR-018 launch trust remains the security boundary.
4. **Existing deployments.** `legacy-v6` and `pinned-unsigned-v1` deployments, including the owner's current host, report protocol-2 as **Absent** and gain nothing. Each desktop/helper pair accepts exactly one protocol; a protocol-2 helper never accepts protocol 1.
5. **No authority follows.** A v2 deployment requires a separately reviewed redeploy-template/plan-schema change (the existing exact plan hard-codes the `legacy-v6` preflight and the `pinned-unsigned-v1` postcondition) and a separate exact owner approval. This note grants no redeploy, signing, certificate, recipient or distribution authority.

Source and reconciliation: [Task 50 proposal record](../review/TASK_50_ADR022_ALIGNMENT_NOTE_PROPOSED.md), under the owner-accepted ADR-025 ADR-022 row. The follow-up script changes in that record remain separately reviewed implementation work; running any of them on a host needs separate exact owner approval.
