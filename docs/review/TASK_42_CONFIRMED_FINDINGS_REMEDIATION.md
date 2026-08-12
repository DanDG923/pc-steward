# Task 42 — Confirmed findings remediation

Date: 2026-08-12

Scan baseline: `032c7dbb0842dff020c5b6746524671a4a1f0a9d` (`phase/4-beta`)

External action: none. This task does not close cloud findings, push Git, deploy PC Steward, launch the application, refresh a live source, or update an application.

## Triage

The current Codex Security scan reported five open findings. Current-code reproduction confirmed two residual defects and rejected three as non-actionable against this checkout.

| Ref | Finding | Disposition | Evidence |
|---|---|---|---|
| F8 | Unified WinGet toggle can hide active source consent state | Confirmed residual; remediated | Atomic paired writes did not normalize older asymmetric rows. The combined UI could show Off while the package-source row still enabled desktop preparation or scheduled refresh. |
| F10 | Malformed WinGet output can replace update cache | Confirmed residual; remediated | The parser emitted `winget:malformed-update-row`, while the fatal-warning gate omitted that exact value. A short row could therefore produce a live snapshot. |
| F12 | WinGet inventory scan may contact sources in local-only mode | Not actionable | The provider returns before process invocation when disabled, and desktop/scheduled composition use persisted consent gates. |
| F18 | Ambiguous fingerprint encoding can merge distinct installs | Not actionable | Current fingerprints use domain-separated, length-prefixed tuple encoding. |
| F21 | Solution references missing SQLite project | Not actionable | The referenced SQLite project exists and the complete solution builds. |

## Remediation

- `TrustedSourceConnectorConsent` derives one effective state from both persisted consent rows. If either row is disabled or unapproved, both owner-facing states are effectively disabled.
- Desktop load, update preparation, scheduled WinGet inventory, scheduled Chocolatey inventory, and scheduled source refresh use the same fail-closed predicate. Fully paired approved consent remains enabled.
- `winget:malformed-update-row` is fatal at the WinGet source connector boundary. The connector throws before a live snapshot can be saved; the existing refresh service then retains last-good cached evidence.

No database migration is needed. Existing asymmetric rows remain preserved but cannot authorize source activity; the next owner toggle writes the pair atomically through the existing transaction.

## Verification

Pre-fix regressions failed as intended:

- `AsymmetricTrustedSourceConsentLoadsFailClosed` exposed the still-enabled package-source state.
- `ShortMalformedUpdateOutputCannotProduceALiveSnapshot` exposed the missing fatal classification.

Post-fix evidence:

- Focused confirmed-finding scenarios: **6/6** (4 Application integration, 2 Architecture).
- Canonical Release build: **0 warnings, 0 errors**.
- Complete normal-host suite: **290/290** — Application **181/181**, Domain **59/59**, Architecture **28/28**, Security **22/22**.
- The managed filesystem run reproduced the documented native cache/reset `NTSTATUS=C0000022` restriction; the unchanged native fixtures passed on the normal Windows host.
- `git diff --check` passes. A final change-aware review found no new command, path, network-source, elevation, persistence, update-execution, or deletion authority.

## Boundary

This closes the two confirmed current-code defects in source and automated evidence only. The protected owner-only installation and private GitHub prerelease still contain an earlier build. Deployment, publication, cloud-finding closure, application launch, source refresh, and any application update require separate exact authority.
