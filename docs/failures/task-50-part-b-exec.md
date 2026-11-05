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
