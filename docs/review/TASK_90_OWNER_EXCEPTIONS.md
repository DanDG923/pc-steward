# Task 90 product-owner exceptions

**Approved:** 2026-07-31
**Candidate boundary:** local-only standalone archive SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`
**Effect:** these exceptions close the named Task 90 evidence-disposition gates only. They do not convert omitted mandatory observations into `Proven`, authorize external distribution, or make the MVP release-ready. A fresh Task 31 review remains mandatory.

## PO-EXC-90-SIGN-LOCAL-ONLY

The product owner has no approved code-signing certificate, signing-service identity, private distribution channel, external recipient list, support contact or rollback package and approved omitting that gate by exception.

- The candidate may be executed only by the product owner from the existing local workspace on the approved physical PC.
- It must remain unsigned and must not be uploaded, shared, tagged, published or sent to another recipient.
- The only recipient is the product owner already in possession of the workspace; there is no distribution channel or support commitment.
- Local rollback is to close PC Steward, delete the extracted candidate payload after exact-task cleanup where applicable, and retain the per-user data by default. No older archive is promoted as a safe distributable rollback package.
- This exception expires immediately if any external recipient or distribution channel is proposed, if the archive hash changes, or before any release/publication claim. At expiry, an approved certificate, signer verification, channel, recipients, support contact and rollback artifact are required.

Disposition: `FRA-04` is **Accepted exception for local owner-only testing**, not signed-distribution proof.

## PO-EXC-90-PERF-BOUNDARY

The product owner accepted the physical 3XS PC and fixed `3XS-Overclock` plan as the ADR-006 baseline and approved a CPU-idle exception. The clean packaged observations measured 0.4521% total-system CPU / 181,592,064 B peak working set from cold start and 0.1491% / 186,441,728 B after warm-up, within the CPU and memory limits. They also recorded early process-attributed reads/writes that reached zero only in minutes four and five; process-attributed network remained unverified. The 30-local/20-live final-application series, full scan process-tree limits, update responsiveness and final-timeout scheduled exit were not completed.

- Task 90 accepts the observed CPU/memory result and omits the recurring-I/O, network-attribution and unexecuted repeatability portions by exception for this local-only candidate.
- `NFR-PERF-05`, `AC-NFR-01/02/06/07/11` remain partly unproven; their numeric thresholds are unchanged.
- This exception is bound to the approved baseline and final candidate. It expires on a package/hash or baseline/power-plan change, before external distribution, or when a future performance task claims these requirements as Proven.

Disposition: `FRA-06` is **Accepted exception for Task 90**, not a complete performance pass claim.

## PO-EXC-90-RESET-LIFECYCLE-OMISSIONS

The retained supported-host evidence proves fresh standard-user launch, exact current-user schedule/action, scheduled-host normal completion, Task Scheduler completion and cancellation, exact-task-only removal with unrelated sentinel retention, extracted binary/folder removal, state retention and zero residual PC Steward process/task. Automated reset/scheduler regressions cover the marker, lease, hostile path and exact-task boundaries.

The product owner approved omitting by exception the remaining action-time destructive or unavailable field cases: a complete two-confirmation factory reset proving zero prior state and onboarding, reset with task present/absent and residual-marker outcomes, retain-data package replacement, reset-then-package-removal, and a real scheduled final-timeout exit. The fixed scan completed too quickly to exercise that timeout without changing the product contract.

- `AC-DAT-12/13/15/18`, `NFR-PERF-07` and `AC-NFR-11` are not promoted to Proven beyond the cases actually observed.
- The exception is local-owner-only and expires on a reset/scheduler/removal implementation change, package/hash change, any external recipient, or before a release-ready claim.
- Any future destructive replay still requires fresh action-time confirmation and must preserve exact-task/unrelated-state isolation.

Disposition: the remaining `FRA-01–03`/`FRA-07` field omissions are **Accepted exceptions for Task 90**; the implemented controls and completed observations remain evidence, while the omitted cases remain explicit.
