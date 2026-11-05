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
