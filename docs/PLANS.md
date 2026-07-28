# Task 90 — Phase 3 review remediation plan

1. **Blocker — P3R-01:** Resolve WinGet only from a machine-trusted, non-reparse, signed Microsoft App Installer package path; reject current-user aliases, unsigned binaries, and unexpected signers.
2. **Blocker — P3R-03:** Replace path revalidation plus final-object deletion with a handle-anchored, no-follow descendant walk; prove hostile components and ancestor swaps cannot escape the fixed cache root.
3. **High — P3R-02:** Use exact ID/source post-update inventory and concrete local scope/architecture evidence, with malformed or incomparable results failing closed.
4. **High — P3R-04:** Reconcile a safe filesystem walk with the artifact ledger so orphans and residuals are measured and prevent a false cleanup success.
5. **Medium — P3R-05:** Refresh exact installed evidence at confirmation and reject changed, deleted, unavailable, or conflicting installed targets before approval/provider construction.
6. **Medium — P3R-06:** Preserve a verification-required interrupted state for started jobs and recover it append-only rather than as queued/cancelled.
7. Add focused regressions, run focused suites then Release build/full suite, and record executed evidence honestly.
8. Update the Phase 3 review, evidence readiness, status, and traceability; commit only after a final diff review finds no Blocker/High finding.
