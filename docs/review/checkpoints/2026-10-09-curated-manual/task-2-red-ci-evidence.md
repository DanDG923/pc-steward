# Task 2 compiling regression checkpoint

Commit: `81507fe494e8fc11c933c4fa427a8849baf19c90`. Tree: `51b75e2aca3cdd79b1519cdce58aad5e681de522`.
Windows CI: https://github.com/DanDG923/pc-steward/actions/runs/37998087294 ; job `114049127955`.
Controller fetched decoded complete job logs and checked all eleven failure messages: assertion mismatches against the absent/inert behavior, with no runtime/fixture crash used to manufacture red.

Commands:

- `pwsh -NoProfile -File ./scripts/Restore.ps1`: passed.
- `pwsh -NoProfile -File ./scripts/Build.ps1`: passed, zero warnings/errors.
- `pwsh -NoProfile -File ./scripts/Test.ps1`: failed on the eleven intended new regression assertions.

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Architecture | 38 | 0 | 0 |
| Catalog | 96 | 1 | 0 |
| Domain | 87 | 0 | 0 |
| Security | 72 | 0 | 0 |
| Application | 339 | 10 | 0 |
| Total | 632 | 11 | 0 |

Intended Catalog failure: `BundledCatalogRetainsReservedExactMachineStableNonAutomaticSetup` (reserved entry missing).
Intended Application failures:

- `ChoicesUseLiveApplicableInstallerNotHostArchitecture` (zero choices).
- `NeutralChoicesArePreservedAndIdenticalSelectionsDeduplicated` (zero choices).
- `DiscoveryNeverHasMoreThanTwoQueriesInFlight` (zero fake calls).
- `WorkflowDiscoversOnlyAfterValidatedConsentAndReliableAbsence` (zero choices).
- `PartialLocalInventoryCannotOfferInstall` (existing final selection incorrectly non-null).
- `CompositeChoicesRoutesOnlyTheListedProvider` (zero routed choices).
- `ChocolateyChoicesRequireStrictCurrentTrustAndRemainMachineNeutral` (zero choices).
- `SetupRequiresPositiveAbsenceConsentAndMachineCapability` (positive setup gate still null).
- `SetupUsesExactWingetMachineX86Selection` (setup selection null).
- `SetupVerificationRequiresStrictPostInstallTrust` (zero health calls).

All other baseline/control tests passed. No separate successful local or focused run is claimed: local SDK is absent. No real provider, network, installer, UAC, application close/update, deployment, merge, or scheduler action was run.

Production implementation was released to the original Task 2 implementer after this evidence was checked; candidate review and passing full CI remain required.
