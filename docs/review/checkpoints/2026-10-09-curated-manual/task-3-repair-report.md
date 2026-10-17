# Task 3 repair report — review I1/M1

Status: **Repair complete; scoped re-review pending.** Task 4 has not started.

Requirements: CRD-01/04/11; FR-CAT-04/06; FR-AUT-01; FR-RSC-01–05. Review: [task-3-review.md](task-3-review.md) (GPT-6-Astra), committed as given in `a6e1b25`. It requested changes: 0 Critical, 1 Important (I1) and 1 Minor (M1).

Range: `82ec758` (reviewed head) → `a6e1b25` (review) → `111b448` (regression tests) → `bf99db1` (fix) → this documentation commit.

## I1 — removal could delete another package's automatic-update opt-in

**Cause:** removal took its policy keys from `PersonalCatalogMatching.Match` results where `Detection.IsExact` was true. `IsExact` also covers `NameAndPublisher`, and `CatalogDetector.Detect` falls back to name and publisher when the record's package ID contradicts the entry. As a result, a different WinGet package with the same name and publisher could lose its `AutomaticUpdates=On` row.

**Regression first:** `111b448` added `CuratedMembershipTests.RemovalKeepsSameNameAndPublisherDifferentPackageOptIns`. All three products below carry `Publisher = "Contoso"`. The test expects:
- `Contoso.Widget` (genuinely associated): its On row is removed;
- `Contoso.Widget.Copy` (different package): its On row survives;
- a mixed product with `Contoso.Widget` and `Contoso.Widget.Copy` instances (contradictory identity): its On row survives.

[Windows CI 39080035075](https://github.com/DanDG923/pc-steward/actions/runs/39080035075), job `117534930281`, at `111b448`: clean build. Exactly one failure: this test's assertion that the different package keeps its opt-in (`Assert.IsTrue … CopyKey`). Totals were 703 passed / 1 failed / 0 skipped. Architecture 39, Catalog 97, Domain 87 and Security 72 passed; Application ran 408 of 409.

**Fix (`bf99db1`, `src/PCSteward.Application/CuratedMembership.cs` only):** removal now derives policy keys with a private `UniquelyAssociatedPolicyKeys`, which uses its own `CatalogDetector`. A product qualifies only when all of the following hold:
- its stable key is unique;
- it has at least one instance;
- every instance detects as `CatalogDetectionKind.ProviderIdentity`, naming only the removed entry.

Name/publisher fallback, name-only matches, ambiguous matches and any contradictory provider identity therefore disqualify the product. `CatalogDetector` and `PersonalCatalogMatching` semantics are unchanged, so catalog display and mapping behaviour is unaffected. Caller-supplied keys are still never accepted. The existing `RemovalDerivesOnlyExactCurrentProductKeys` and `RemoveDoesNotUninstallOrDeleteHistory` cases still pass.

## M1 — narrowed download-preservation assertion

In `111b448`, `SqliteDatabaseTests.MappingsAndPoliciesClearIncludesMembershipAndRestrictiveOverrides` now reads the complete persisted `DownloadedArtifact` list just after saving and before the clear. It asserts there is one record with the expected Id. After the clear it compares the complete records with `CollectionAssert.AreEqual`. Record equality covers every field: category, path, size, timestamp, hash, job, outcome and active state. The existing null-hash-to-empty-text normalisation on save is absorbed by the before-read; it is not hidden by dropping fields. This fully addresses M1.

## Validation

[Windows CI 39080312565](https://github.com/DanDG923/pc-steward/actions/runs/39080312565), job `117535741073`, at `bf99db1`: canonical Restore, Build and Test passed with 0 warnings and 0 errors. Architecture 39, Catalog 97, Domain 87, Security 72, Application 409: **704 passed / 0 failed / 0 skipped**. The local SDK is absent, so no local run is claimed. No real provider, installer, UAC, scheduler or owner-data action took place.

## Self-review

- The fix changes only the Application service's private key derivation. No SQL, persistence, path or command construction was touched, and there are no new dependencies.
- An MSIX instance is handled the same way as `PersonalCatalogMatching`: product key used as the package family name. A family-name identity match therefore still qualifies.
- A product with no instances cannot qualify. Its legacy opt-in is preserved, which is the restrictive outcome.
- The four implementer points the review accepted are unchanged. Task 1 Minor M1 stays deferred to Task 15.
