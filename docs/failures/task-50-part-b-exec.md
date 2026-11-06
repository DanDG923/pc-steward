# Task 50 Part B — Stage B-exec stopped (2026-11-05)

Requirements: `FR-UPD-15`, `TM-03/04/06/07/13`; ADR-022. Approval 1b (owner, 2026-11-05); [B-exec handover](../review/TASK_50_PART_B_EXEC_HANDOVER.md) revision 1, run by a child session in the owner's Remote Control bridge environment.

## What happened

- B4 ran exactly once (attempt 1, started 2026-11-05T21:57:10Z, completed 21:57:29Z, about 20 s). No retry, no kill; the attempt record is untouched.
- Prompts: UAC (Windows PowerShell, Microsoft Windows) = Yes; Root-store deletion of the old signer `14F34EFE…5623` = Yes; Root-store installation of the new signer = Yes; then **a fourth, unexpected prompt asking to delete a certificate** — the removal was cancelled.
- Attempt record: `status` **StoppedOrUnknown**, `nativeExitCode` 1, `resultExitCode` 1, `resultPhase` **approved-pinned-installation**, `userDatabaseSha256` `CEB917F4…6B99` (equal to the pin), `retry` false, `launched` false.
- `resultFailure`: "Owner-only deployment failed: Could not sign PCSteward.Desktop.exe with the owner-only certificate.. Rollback is incomplete: certificate removal: Exception calling "Remove" with "1" argument(s): "The operation was canceled by the user.""
- B5 was not run (stop rule).

## What this means (from the code, not yet observed on the host)

- The phase `approved-pinned-installation` comes after `approved-removal`, so the **v1 deployment and the old signer were removed** (the owner confirmed both prompts).
- The installer (`Install-OwnerOnlyProtectedTestDeployment.ps1`) created the new signer, added it to CurrentUser Root and TrustedPublisher, then `Set-AuthenticodeSignature` on `PCSteward.Desktop.exe` did not return `Valid` with the new thumbprint (line ~157), so it rolled back.
- Rollback (lines ~185–204): the install root removal ran first (no failure reported for it), then `Remove-ExactCertificate` for the new signer raised the Root-store deletion prompt; it was cancelled, so certificate removal failed, the state-root removal was skipped (it runs only when there are no earlier failures), and a rollback ledger (`rollbackRequired = true`) was written in `%ProgramFiles%\PC Steward OwnerOnlyTest State`.
- Likely host state, to be confirmed read-only: no installed PC Steward; the state root with a rollback ledger; the new signer still in some of My / Root / TrustedPublisher; the protected execution root `…Execution 44f5b22d4271463eb91f9a00cda9ecdc` with its results; the user database unchanged.

## Contributing cause in our handover

The B-exec handover told the owner to answer **No** to any prompt beyond the expected three. The rollback's own Root-store deletion prompt for the new signer was not anticipated, so the rollback could not complete. A future plan must list rollback prompts explicitly.

## Unknown

Why signing failed. Candidates to examine read-only: the signature status returned, the candidate `PCSteward.Desktop.exe` image (built by the new G3 builder; Task 48's hand-built payload signed fine), timestamp or chain conditions, and the protected staging copy.

## Next steps (each needs its own owner approval)

1. A read-only post-failure inventory (stores, state ledger, roots, execution-root results, database).
2. Diagnosis of the signing failure (cloud-side reproduction where possible; host-side read-only checks).
3. A recovery plan: finish the rollback (remove the new signer from the remaining stores, the rollback ledger and the state root) and then a fresh install from a fixed candidate, under a new approval.

## Post-failure inventory (2026-11-05, read-only, via the Remote Control bridge)

- No `PCSteward*` process; **install root absent**; state root present with `deployment.json` (`pc-steward-owner-only-protected-test-v6`, `completed` false, `rollbackRequired` true, signer `ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55`, owned stores My/Root/TrustedPublisher, `originalFailure` = the signing message), the candidate manifest copy and the protected staging folder.
- Execution root `…Execution 44f5b22d4271463eb91f9a00cda9ecdc`: `removal-result.json` (v1 removed, old signer `14F34EFE…` removed, user data preserved), `result.json` (exit 1, phase `approved-pinned-installation`), no `installation-result.json`.
- **The staged `PCSteward.Desktop.exe` carries a Valid Authenticode signature from the new signer** (SHA-256 `07708ABA…7952`, 188,440 B). So signing itself worked; the installer's check of `Set-AuthenticodeSignature`'s returned status failed at that moment.
- Candidate executables are ordinary PE32+ AMD64 apphosts: no certificate table, no overlay, no .NET single-file bundle marker.
- Database unchanged: `CEB917F4…6B99`, 159,744 B, no sidecars.
- Smart App Control off (`VerifiedAndReputablePolicyState` 0); VBS and HVCI on; 8 active CI policies; no Code Integrity events naming PC Steward.
- Certificates: the old signer `14F34EFE…` is gone from every store. The new signer was in CurrentUser Root and TrustedPublisher, and its My entry was gone (rollback removed it before the cancelled Root step).

## Owner actions in the inventory session (outside the approved read-only scope)

The owner replied "Approved to remove" in the inventory session. That session's own permission check refused to change certificate stores, so it changed nothing; it then gave the owner commands, which the owner ran in their own terminal:
- `Remove-Item Cert:\CurrentUser\Root\ED6FBCE8…` (Root prompt answered) — afterwards **Root is clean in both views**.
- `Remove-Item Cert:\CurrentUser\TrustedPublisher\ED6FBCE8…` — the owner's shell reported the path **does not exist**; `certutil -user -delstore TrustedPublisher …` reported success. The inventory session still sees the entry in TrustedPublisher (and its `HKCU\Software\Microsoft\SystemCertificates\TrustedPublisher\Certificates\ED6FBCE8…` key).

## Leading hypothesis: the run's execution context

The owner's own shell and the session started by the Claude desktop app see **different contents of the same per-user certificate store**. That fits per-package registry and AppData virtualization of a packaged desktop app: per-user registry writes (the My and TrustedPublisher entries) and per-user key files made inside the package container stay in a private view, while the Root add goes through the system's protected-root prompt into the real store. B4 was started from such a session, so the elevated installer most likely ran with that view. The returned verification status after signing would then depend on a mixed real and private store view, which can make the installer's `Status -eq 'Valid'` check fail even though the file was signed. Tasks 48 and 49 ran from the owner's own console and signed correctly.

Unconfirmed; the Windows CI signing diagnostic (no packaged-app context) should sign Valid if this is right. **Consequence regardless:** certificate-store and elevated redeploy steps must run from the owner's own Windows PowerShell console, not from a session started by the Claude desktop app. Read-only checks through the bridge remain usable, but a reader inside that container may see stale per-user state.

## Current host state (best evidence)

- Real per-user Root and TrustedPublisher stores: clean of PC Steward test certificates. TrustedPublisher was likely never written to the real store.
- A stale TrustedPublisher entry may remain only in the desktop app's private view; it affects only processes inside that container.
- Needs a separately approved recovery: the state root (rollback ledger, staging, manifest), then a fresh install from the owner's console. The execution root is kept as evidence.

## Inventory session closing report (2026-11-05)

- The owner removed the new signer `ED6FBCE8…4C55` from CurrentUser\Root and CurrentUser\TrustedPublisher by hand (Root dialog confirmed; `certutil -user -delstore` and certmgr.msc for TrustedPublisher). **certmgr.msc shows the Current User Trusted Publishers store empty.**
- CurrentUser\My, LocalMachine\Root and LocalMachine\TrustedPublisher never held it. The old signer `14F34EFE…5623` is in none of these stores.
- The inventory session still reads a stale TrustedPublisher copy (Cert: provider and HKCU key), which supports the private-view hypothesis. certmgr.msc in the owner's own session is treated as authoritative.
- Not changed: the state root's `deployment.json` (`completed` false, `rollbackRequired` true, rollback failure = the cancelled certificate removal), staging and manifest. The new signer's private key file may still exist; the recovery plan must cover it.

## Signing diagnosis (2026-11-05; code, CI and read-only host evidence; no host action)

Host evidence relayed from a read-only inventory: the protected staging copy `…State\staging-*\payload\PCSteward.Desktop.exe` (188,440 B) **carries a Valid Authenticode signature from the new signer** `ED6FBCE8…` (`Get-AuthenticodeSignature`: Valid, "Signature verified."). The candidate apphosts are plain PE32+ (no certificate table, no overlay, no single-file bundle marker); Smart App Control off; no CodeIntegrity events. So `Set-AuthenticodeSignature` embedded a good signature, and the installer's line-157 check of the **returned object** (`Status -eq 'Valid'`, non-null `SignerCertificate`, same thumbprint) failed at that moment. The installer discarded `Status`/`StatusMessage`.

How Windows PowerShell 5.1 builds that object: after `CryptUIWizDigitalSign` succeeds it re-reads the file through `WinVerifyTrust` in the same process; a failed sign returns a Status mapped from the Win32 error instead. With a signature now present on disk, the failure is in the immediate in-process trust verification, not in the image.

Ranked hypotheses:

1. **Store view split in the elevated process (leading).** The installer ran from a session started by the Claude desktop app, likely a packaged (MSIX) app whose children can get a per-package HKCU/AppData view. `CurrentUser\My` (with the CNG key) and `CurrentUser\TrustedPublisher` could then land in the private view, while the protected-root prompt wrote `CurrentUser\Root` to the real store. The immediate verification inside that process then fails the chain (typically `UnknownError`, "terminated in a root certificate which is not trusted"). A later, unpackaged `Get-AuthenticodeSignature` sees the real Root and reports Valid. Supporting evidence: the owner's normal shell and the bridge session show different `CurrentUser\TrustedPublisher` contents. Task 48 and Task 49 signed successfully. Whether their elevated sessions were started the same way is not yet confirmed.
2. **Transient trust/cache timing right after the Root add.** The Root add and the verification happened in the same second, through the protected-root (`ProtectedRoots`) service. Lower likelihood: Task 48/49 used the same sequence.
3. **Host servicing change.** Windows PowerShell moved from 5.1.26100.9711 (Task 49, 2026-11-03 inventory) to 5.1.26100.9818 (B-prep). The new build could behave differently for the returned signer or status. CI on the current image tests this indirectly.
4. **Payload-intrinsic causes: effectively excluded.** The causes considered were a bundle or overlay, a certificate-table or checksum effect of `GetUnsignedImage`, the WinAppSDK apphost shape, the `CopyTreeProtected` bytes and the DACL. The installer's own `GetUnsignedImage` passed, the host inventory found a plain apphost, and the staging file holds a Valid signature.
5. **Excluded by the evidence:** `HashMismatch`, `NotSupportedFileFormat`, a read-only or locked file, Smart App Control, WDAC/HVCI (no CodeIntegrity events), and a missing timestamp (none is needed for Valid).

**CI reproduction.** The `owner-only-signing-diagnostic` job in `.github/workflows/ci.yml` is informational (`continue-on-error: true`). It:

- builds the candidate from `febf1ac` with that commit's own G3 launcher, as a disposable standard user;
- runs `scripts/Test-OwnerOnlySigningDiagnosticCI.ps1` elevated under Windows PowerShell 5.1, which:
  - creates a certificate with the installer's exact parameters;
  - signs copies once before trust, as the baseline for an untrusted root;
  - trusts the certificate in LocalMachine Root and TrustedPublisher and in CurrentUser TrustedPublisher;
  - signs copies of both apphosts, a fresh console apphost and `dotnet.exe`, both in a temp folder and in a Program Files folder with the installer's protected DACL; a `signtool sign` variant is included;
  - prints the **returned** `Set-AuthenticodeSignature` Status, StatusMessage and signer, the installer's line-157 predicate, `Get-AuthenticodeSignature`, `signtool verify /pa /v`, PE facts and whether the process is packaged;
  - uploads `artifacts/signing-diagnostic/signing-diagnostic.json` and removes the certificate and the folders it created.

CI run 1 (2026-11-05, run 42119561344, `a231b06`): the **untrusted baseline reproduced the installer's failure exactly**. Before trust, `Set-AuthenticodeSignature` on the febf1ac `PCSteward.Desktop.exe` and on a `dotnet.exe` control returned `Status` **UnknownError**, "A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider", **with `SignerCertificate` set** (the signature was written). So the installer's line-157 predicate would throw "Could not sign …" while the file is signed, which matches the host's staging file. The run then stopped at the first trust add (`X509Store.Add`: "Access is denied"); cleanup completed. The script now adds a public-only copy, falls back to `certutil`, and records each store's outcome. The trusted variants are pending run 2.

CI run 2 (2026-11-05, run 42120299155, `4780e84`; unpackaged process, `GetCurrentPackageFullName` = 15700; Windows PowerShell 5.1). The three trust adds succeeded: LocalMachine Root, LocalMachine TrustedPublisher and CurrentUser TrustedPublisher, each as a public-only copy through `X509Store`. The chain then builds. **Every trusted variant returned `Valid` with the expected signer, and the installer's line-157 predicate accepted it.** That held for the febf1ac `PCSteward.Desktop.exe`, for `PCSteward.ElevatedHelper.exe`, for a fresh console apphost and for `dotnet.exe`, in both the temp folder and the protected Program Files folder. `signtool verify /pa` exited 0 and `signtool sign` gave Valid. No overlay, no bundle and no Defender detections were found. Cleanup completed.

**Conclusion.** The payload, the builder and the protected copy are cleared. The installer's check fails exactly when the signing process cannot see a trusted root for the new signer, and passes when it can. On the host the root was added through the protected-root prompt while the installer ran in a session started by the Claude desktop app, where the owner's shell and that session demonstrably see different per-user store contents. Hypothesis 1, the execution context, is therefore the confirmed mechanism class. The exact Windows path inside the packaged context is not reproduced. Recovery and any re-run must start from an unpackaged console and should fail closed if packaged; see [the recovery plan](../review/TASK_50_RECOVERY_PLAN.md). If CI, which has no packaged context, returns Valid with the installer accepting, the payload is cleared and hypothesis 1 or 3 remains.

**Hardening (proposed only; not implemented; the installer is pinned by reviewed hashes).** On a non-Valid return, record the returned `Status`, `StatusMessage` and signer, then re-verify with `Get-AuthenticodeSignature` and record that result in the failure text and the rollback ledger. Also refuse to start the installer from a packaged process (`GetCurrentPackageFullName` ≠ 15700), and list the rollback's own Root-deletion prompt in the plan.

## Resolution (2026-11-06)

The failure is resolved through the [recovery plan](../review/TASK_50_RECOVERY_PLAN.md):
- **R1** (2026-11-05): the febf1ac remover, run from the owner's elevated console, recovered the incomplete deployment.
- **R2 tooling** (`385efe0`): fresh-install mode, packaged-process refusal and signing diagnostics.
- **R2.5** (2026-11-06): `Validated` in about 22 s from the owner's console. Only the UAC prompt and the certificate warning appeared.
- **R3:** PASS, with new signer `473DB701…53FD` once in My, Root and TrustedPublisher, the validator `Pass` with `pinned-unsigned-v2`, and the database unchanged.

The B4 approval and plan (`D9CAA3D8…`) remain used and are not reused. The execution root `44f5b22d…` is still kept as evidence.

**Lesson.** Elevated and certificate-store steps run only from a console the owner opens. The tooling now enforces this.
