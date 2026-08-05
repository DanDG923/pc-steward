# Task 36 native-storage test-host remediation

**Date:** 2026-08-05
**Branch:** `phase/4-beta`
**Requirements:** `FR-DAT-08–10/12–15`, `AC-DAT-08–09/12–16/18`, `AC-NFR-10`, `NFR-REL-02`
**Outcome:** PASS — the previous `NTSTATUS=C0000022` result was isolated to the managed filesystem sandbox; the unchanged storage implementation passes on the normal Windows host.

## Diagnosis

The exact failing cache-quota scenario was rerun first inside the managed filesystem sandbox and failed at the handle-bound native storage boundary. The same compiled test was then rerun outside that sandbox and passed. This A/B result isolates the access denial to the test host boundary rather than the cache logic, factory-reset logic, ACLs of the disposable fixtures, or the current product implementation.

The security-sensitive implementation was intentionally retained. Replacing relative handle-bound opens with path-based deletion or skipping the hostile storage fixtures would reduce reparse/race resistance and would not be a valid remediation.

## Validation evidence

| Validation | Result |
|---|---|
| Exact quota case in managed filesystem sandbox | FAIL — environment denied the native handle operation |
| Exact quota case on normal Windows host | PASS — 1/1 |
| Complete downloaded-cache class on normal Windows host | PASS — 16/16 |
| Complete factory-reset class on normal Windows host | PASS — 25/25 |
| Complete Application integration project on normal Windows host | PASS — 167/167 |
| Official `scripts/Test.ps1` on normal Windows host | PASS — 267/267: Domain 55, Application 167, Architecture 24, Security 21 |
| Post-suite temporary-fixture audit | PASS — 41 stale failed-run fixtures removed; clean 267/267 rerun left zero new matching fixtures |

## Boundary

The tests used disposable `%TEMP%` fixtures, including their deliberate junction and hard-link attack cases. Forty-one stale paths from earlier failed runs were removed only after each immediate child was validated under the Windows temp root and matched a test-only `pcsteward-reset-*` or `pcsteward-outside-*` name; reparse points were removed as links without traversing their targets. Unrelated PC Steward temporary items were excluded. The clean full-suite rerun left zero new matching fixtures.

The validation did not invoke PC Steward against the owner's real state root, start an update, alter an installed application, create a scheduled task, or perform a factory reset of owner data. No production or test source was changed to obtain the pass.

Future automated evidence must run these native handle-bound fixtures on a normal Windows host. A managed sandbox `C0000022` result is evidence that the host is unsuitable for this test class, not permission to bypass the fixture or weaken deletion authority.
