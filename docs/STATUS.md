# PC Steward build status

**Current phase:** Phase 4 — fresh Task 31 audit complete; release remains NOT READY

**Current task:** Task 31 complete; next is Task 90 under GPT-5.6 Terra / High

**Reviewed tree:** `0f03127dc9378efa4ace1412b960dc7dd87e97bb`

**Audit commit:** Task 31 review commit (this commit)

**Last green tree:** `0f03127dc9378efa4ace1412b960dc7dd87e97bb` — Release build 0 warnings/errors; full suite 249/249

**Current branch:** `phase/4-beta`

**Phase gate:** FAIL — one mandatory criterion Failed, nine Unverified; four High findings remain

## Fresh Task 31 outcome

- `FRA-19` High — onboarding acknowledgement can return failure with no marker, but the UI can load saved inventory and continue to Scan. Task 90 must make every failed acknowledgement blocking and add the missing UI regression.
- `FRA-14` High — exact-current-package final-app/live/idle/process-tree/update-responsiveness/final-timeout evidence is incomplete.
- `FRA-15` High — exact-current-package launch/update/reset/task/removal/replacement lifecycle observations are incomplete.
- `FRA-16` High — no approved production signer, channel, recipients, support contact or rollback artifact exists.
- `FRA-20` Medium — release evidence contradicts itself about whether current-hash native launch smoke passed.
- `FRA-18` Low — release evidence reports reset 22 while the current focused suite is 25/25.

The prior `FRA-11–13` implementation findings and `FRA-17` limitations wording finding are closed for their original scopes. The fresh audit is authoritative: [final release audit](review/FINAL_RELEASE_AUDIT.md).

## Verification snapshot

Owner-context reset 25/25; fake-only cleanup contract Pass; full suite 249/249 (55 Domain, 157 Application integration, 17 Architecture, 20 Security); hostile boundary 42 assertions with one local-PC/manual symbolic-link skip; WinGet boundary 11/11; Release build 0 warnings/errors; NuGet advisory query 17 projects / 0 vulnerable rows; independent high-confidence secret scan 0 matches.

The exact unsigned local archive mechanically validates 94 files / 49,950,826 payload bytes, 15 components, 16 SBOM nodes / 32 edges, licences/notices and SHA-256 `A8FD4AF124019E44B23C9F067C49F49A3D61FE8025F6ED9D8AF5E51E4CBCA3FC`. Validator signing status is `Not requested`; current-hash packaged launch smoke is not performed.

Fixture performance retry completed at 30 local median 1.81 ms / p95 2.05 ms and 20 simulated-online median 15.53 ms / p95 16.20 ms. It is not final-app or acceptance evidence. Sandbox-account access/concurrency failures were excluded; the single owner-context rerun is the pass evidence.

## Authority boundary

No real scheduled task, owner data, installed application, package recipient, signer, trusted channel or external system was changed. The sandbox .NET first-run path reported installing an untrusted sandbox-only HTTPS development certificate; it was not trusted, used for product signing or counted as release evidence.

Repository-only `FRA-19/20/18` remediation may proceed in Task 90. `FRA-14/15` require exact action-time approval before launching the package, measuring the owner host, changing an installed application, creating/removing a real scheduled task, replacing the package or deleting/resetting owner data. `FRA-16` additionally requires product-owner-provided signer/channel/recipient/support/rollback identities; they cannot be inferred.

## Next task

Stop here because Task 90 requires different model/reasoning settings: GPT-5.6 Terra / High. After switching, fix repository findings first, retain all unauthorized field/external gaps as blockers, rebuild/re-hash, run the complete automated gates and return to Task 31 under GPT-5.6 Sol / Extra High.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit and next task. Historical detail belongs in Git, ADRs, review reports or failure reports.
