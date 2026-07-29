# Task 90 — Phase 3 follow-up remediation evidence (2026-07-29)

## Result

The Task 25 findings `P3R-03`, `P3R-02`, and `P3R-09` are remediated in the current working tree and require a fresh independent review before the Phase 3 gate can change.

| Finding | Remediation | Evidence |
|---|---|---|
| `P3R-03` | Cache-root ancestors are rejected before creation; deletion now opens the volume and every configured-root component handle-relatively with no reparse following. | Parent/grandparent configured-root junction fixture; existing root/ancestor swap and link/mount fixture. |
| `P3R-02` | Final post-update re-inventory requires ID, source, version, scope, and architecture to match the immutable selection. | Production workflow rejects same ID/version with a different scope or architecture. |
| `P3R-09` | Registered inactive artifacts and crash orphans share one oldest-first candidate order; registered timestamps and orphan last-write times use deterministic path ties. | Mixed older orphan/newer registered fixture retains the registered file and recovers exactly 30 bytes. |

## Verification

```text
Focused cache, inventory-workflow, and WinGet tests: 31 passed
Release build: 0 warnings, 0 errors
Full suite: 160 passed (security 16, architecture 4, domain 51, integration 89)
Host-safe hostile script: 42 assertions; 1 approved elevated local-PC skip
```

The approved elevated local-PC fixture remains the evidence route for symbolic-link and scratch-VHD cases. Do not merge Phase 3 or start Task 26; repeat Task 25 under Sol/Extra High.
