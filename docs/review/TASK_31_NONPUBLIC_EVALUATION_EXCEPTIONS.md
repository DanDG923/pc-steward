# Task 31 non-public evaluation exceptions

**Approved:** 2026-08-05
**Implementation:** `30051670739a412e0436c9c0c750693eaa6f17d8`
**Candidate boundary:** unsigned local archive SHA-256 `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523`
**Permitted use:** product-owner-controlled evaluation from the existing local workspace on the approved physical PC
**Prohibited use:** any external recipient, upload, share, tag, publication, public release, private distribution, automatic update channel or release-readiness claim

## Effect and authority

The product owner approved the following exceptions because this candidate is not intended for public release. They remove `FRA-14–16` as blockers only for the exact owner-only, non-public evaluation boundary above. Missing observations remain `Unverified`; no requirement is relabelled `Proven`, no threshold is changed, and the archive remains unsigned engineering evidence.

These exceptions do not authorize launching the package, changing an installed application, creating or removing a real scheduled task, resetting or deleting owner data, signing, uploading, sharing or sending the archive. Each state-changing host action still requires its own exact action-time approval.

All three exceptions expire immediately if the implementation or archive hash changes, another recipient is proposed, a distribution channel is introduced, or any public, private-distribution or release-ready claim is considered.

## PO-EXC-31-FRA14-OWNER-ONLY-PERFORMANCE

The exact-current-package final-app 30-local/20-live series, full scan process-tree measurement, update responsiveness, five-minute idle I/O/network attribution and scheduled final-timeout observation are omitted for this owner-only non-public evaluation.

- Existing fixture and historical host observations remain evidence only for the cases actually measured.
- `NFR-PERF-01–08` and `AC-NFR-01/02/06/07/11` remain partly `Unverified`; their limits are unchanged.
- Any external evaluation or performance/readiness claim requires the complete ADR-006 protocol on the exact distributed candidate.

Disposition: `FRA-14` is **Accepted exception for the exact owner-only non-public evaluation boundary**, not a performance pass.

## PO-EXC-31-FRA15-OWNER-ONLY-LIFECYCLE

Exact-current-package launch/close, controlled update, retain-data replacement, real scheduled-task creation/removal, two-confirmation owner-state reset/onboarding, reset-then-package removal and scheduled final-timeout lifecycle observations are omitted for this owner-only non-public evaluation.

- Automated reset, scheduler, removal and path-safety evidence remains valid only for the cases executed.
- The missing lifecycle cases remain explicit and `Unverified`; no destructive behavior is inferred from fixtures or an older archive.
- Any future lifecycle replay remains separately approval-gated and must preserve unrelated tasks, outside sentinels and owner rollback state.

Disposition: `FRA-15` is **Accepted exception for the exact owner-only non-public evaluation boundary**, not supported-host lifecycle proof.

## PO-EXC-31-FRA16-LOCAL-UNSIGNED

An approved production signer, signing service, distribution channel, recipient list, support contact and distributable rollback artifact are omitted because the only permitted evaluator is the product owner already in possession of the local workspace.

- The archive must remain local and unsigned and must not be uploaded, shared, tagged, published or sent.
- There is no distribution channel, external recipient or support commitment under this exception.
- Local rollback is limited to closing PC Steward and, only under separately approved action scope, exact-task cleanup and removal of the local extracted payload while retaining per-user data by default.

Disposition: `FRA-16` is **Accepted exception for the exact owner-only non-public evaluation boundary**, not signing or distribution proof.
