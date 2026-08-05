# PC Steward build status

**Current phase:** Phase 4 — Task 90 remediation complete; release remains **NOT READY**

**Current task:** Task 90 complete; next is a fresh Task 31 under GPT-5.6 Sol / Extra High

**Reviewed base:** `c8ed26ac2e8ad26c5ba0493c5dd5dee112126512`

**Current branch:** `phase/4-beta`

**Phase gate:** FAIL — `FRA-14–16` remain High external/field blockers. A fresh Task 31 must independently reassess the Task 90 candidate.

## Task 90 result

- `FRA-19` implementation remediation: **complete pending fresh audit**. A failed post-reset acknowledgement is now blocking even when the marker is `None`; a residual marker after a successful acknowledgement also remains blocking. Two deterministic executable scenarios prove zero saved-inventory loads and no Scan navigation for those cases.
- `FRA-20` evidence remediation: **complete pending fresh audit**. Every retained historical launch/update exception is labelled with SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`; the current archive has no inherited lifecycle claim.
- `FRA-18` evidence remediation: **complete pending fresh audit**. Focused factory reset evidence is accurately recorded as 25/25, including pre-migration, control-swap and attempt-cap coverage.

## Current safe verification

Release build: 0 warnings/errors. Serialized component suite: **251/251** (55 Domain, 159 Application integration, 17 Architecture, 20 Security); the App suite and focused reset filter ran in the normal user context because sandbox native temporary-folder handles return `NTSTATUS=C0000022`. Focused reset: **25/25**. Fake-only cleanup contract: Pass. Host-safe hostile boundary: 42 assertions, one local-PC/manual symbolic-link skip. WinGet-focused non-executing checks: 30/30. Fixture performance: 30 local runs, median 1.89 ms / p95 2.07 ms; not final-app acceptance evidence. NuGet advisory query: 17 projects, 0 vulnerable rows. High-confidence tracked-secret scan: 0 matches.

The exact unsigned local archive mechanically validates 94 files / 49,955,522 payload bytes, 15 components, 16 SBOM nodes / 32 edges, licences/notices and SHA-256 `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523`. Validator signing status is `Not requested`; packaged launch smoke is **not performed**.

## Remaining blockers and authority boundary

- `FRA-14` — no exact-current-package final-app/live/idle/process-tree/update-responsiveness/final-timeout performance evidence.
- `FRA-15` — no exact-current-package launch/update/retain-data replacement/task/removal/reset lifecycle evidence.
- `FRA-16` — no approved production signer, signing service, channel, recipient list, support contact or rollback artifact.

No package was launched, no installed application changed, no real scheduled task was created/removed, and no owner data was reset/deleted. Those actions require exact action-time approval. Signer/channel/recipient/support/rollback identities must be supplied by the product owner; they cannot be inferred.

## Next task

Stop after committing the Task 90 remediation. Run a fresh independent Task 31 under GPT-5.6 Sol / Extra High; it must not promote the product to READY without separately authorized `FRA-14–16` evidence.
