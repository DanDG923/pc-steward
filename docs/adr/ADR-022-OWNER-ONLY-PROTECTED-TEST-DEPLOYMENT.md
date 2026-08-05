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
