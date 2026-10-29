# Task 90 — PR #4 CI fixture repair

## Failure and cause

PR https://github.com/DanDG923/pc-steward/pull/4 at `e99bc8a2b4821d87dfd2da44277f97c408777063`.
Failed job: https://github.com/DanDG923/pc-steward/actions/runs/40946794070/job/123704170570.
Restore/build passed; tests had 290 passes and one failure.
`PinnedPayloadRejectsDriftEscapesAndUnapprovedSigningChanges` failed loading
`OwnerOnlyPayloadIntegrity.ps1`, before its first assertion, with
`Run only the reviewed protected execution bundle.` GitHub's hosted Windows runner
uses an administrator token. The helper correctly requires protected execution
before administrator-context Add-Type compilation.

## Repair and boundary

Production guards stay unchanged (`TM-04`, `TM-06`, ADR-022).
CI restore/build keep the normal runner account. `Test-CI.ps1` creates a random
temporary standard account only on an administrator GitHub-hosted runner, grants
access to the disposable checkout, runs canonical tests under that credential,
captures output/native exit, and removes the account in finally. The child refuses
an administrator token and uses ignored checkout artifacts for TEMP/TMP/CLI home.
CI NuGet packages also live there so restored adapters are accessible.
Tests have a ten-minute bound. The hosted VM discards checkout ACL grants, logs
and profile state after the job. No fixture is skipped and no production bypass exists.
The new regression checks refusal of account setup outside hosted CI.

## Local verification (2026-10-29)

- Existing SDK 10.0.403, process-local PATH/DOTNET_ROOT only.
- Direct Windows PowerShell payload fixture: 85 assertions passed.
- Focused PayloadDeploymentIntegrityTests: 2 passed, zero failures/skips.
- scripts/Build.ps1 Release: zero warnings/errors.
- scripts/Test.ps1: 292 passed, zero failures/skips (28 architecture, 59 domain,
  24 security, 181 integration).
- Local scripts used Windows PowerShell because pwsh is absent from PATH;
  GitHub supplies PowerShell 7. No software was installed.

Actual hosted standard-account logon and CI rerun remain unverified until an
explicitly approved push. Local success is not a hosted CI pass.
An additional local `Test-CI.ps1 -StandardUser` run passed 291/292: the payload
fixture correctly rejected the OneDrive checkout's reparse-marked artifacts root
used as redirected TEMP. The canonical suite above uses normal local TEMP and
passes. This is not hosted-runner verification. The isolated CLI home also caused
.NET first-run output to report development-certificate installation. Subsequent
CurrentUser My inspection found only the pre-existing localhost certificate with
NotBefore 2026-10-29 12:09:47 (thumbprint `2B40230A92DA2EE672354F608F9AE7D87409FD42`),
not a newly issued certificate. The launcher now disables development-certificate
generation and telemetry before dotnet runs; no certificate removal was attempted.
No local account/ACL/installed app/deployment/schedule changed.
No Task 49 or distribution gate starts/closes.
