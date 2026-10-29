# Task 48 — Final readiness to request the exact host test

Date: 2026-10-29. Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, retained `TM-04/TM-06`.

**Ready to request one exact owner-approved removal/redeployment test. No host execution or launch authority is inferred.** [The exact plan](TASK_48_EXACT_REDEPLOY_PLAN.md) is the approval surface. [Opus's M1 re-check](TASK_48_PAYLOAD_OPUS_REREVIEW.md) found no Blocker or required bootstrap/artifact change and verified all 19 reviewed pins before/after. Its two remaining doc/plan conditions are now addressed by the parent:

- **M1-R1:** the plan and closure now state the trusted owner-account/session assumption. File/compiler substitution routes are addressed; active same-user control of the caller, CLR startup, signer-key use or CurrentUser trust stores is not isolated. Owner action-time approval must knowingly include that limit. A read-only environment-name check detects configured overrides but does not prove a clean account or prevent a race.
- **M1-R2:** the exact caller is prepared and pinned: `artifacts/task-48-redeploy-plan-v2/invoke-caller-v2.ps1`, SHA-256 **`92B8DB08DD9CE23ECD9448B4E624F921C41140E56AA59523AC90F0EE4E55A21A`**. Default CheckOnly verifies literal input hashes, all six scripts, full 102-file payload, SID/old ledger, data/process/root baseline, configured CLR override names and Group Policy; it does not elevate, create a claim record or execute the transaction. Actual execution requires `-ExecuteApprovedPlan` after approval. Raw verified caller bytes must be captured and executed after a literal hash comparison, rather than loading a mutable caller pathname.
- **Low CWD/prompt items:** prose correctly distinguishes PowerShell location from native CWD. Caller requests native System32; no private native CWD claim is made. Plan lists UAC, old Root deletion and new Root installation prompts, expected identities and consequences of declining/missing each. Reviewed bootstrap/program bytes remain unchanged.

Caller follow-ups are parent-authored and checked, not misrepresented as independently reviewed by Opus. No additional delegate was started for these small items. Its argv is exactly `-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <verified string>`; only the child session is affected. It refuses enforced MachinePolicy/UserPolicy rather than overriding them. No CurrentUser/LocalMachine policy, registry environment or trust configuration changed during preparation.

## Verification

| Check | Result and limit |
|---|---|
| Focused production-helper fixture | **85 assertions PASS**; real isolated junction/ADS, synthetic in-memory CMS, memory ACL/OWNER RIGHTS, parser/profile/provenance/order and ordinary child exit checks |
| Focused MSTest / canonical gates | **1/1**, Release **0 warnings/errors**, full **291/291**, zero skipped; code/test source is unchanged since those passing runs |
| Independent fresh review | v4 raw pin manifest `C420A9617BCB4516153741001863B072A05AA2AB195DD554ACD8C8D9CEF227F5`; all 19 hashes matched during review; no bootstrap code blocker |
| Exact caller CheckOnly | **Pass**, 102 candidate files, zero configured CLR override names in process/HKCU Environment, no enforced policy, `executed=false`, no execution root or caller attempt record |
| Exact proposed child argv, verified non-admin token | **Native exit 1**, “Exact installing SID and elevation required”, before host-root creation; no UAC/elevation was requested |
| Execution-policy before/after | CurrentUser `RemoteSigned`; MachinePolicy/UserPolicy/Process/LocalMachine `Undefined`; no persistent setting change |
| Old deployment / data | Unchanged old ledger/signer; explicit legacy validator exits 0, default pinned validator exits 1; no PCSteward process and user-state/database root absent |
| Program identity | Plain `74B6CE6AAF016D5D68025449712A587D5A6133A1D529C8DAE0217AF42A413B49`; encoded `3D3B1DD87FADB221C73E86C6BD447CD3CBD506A3C7D7FB35FFC28CD74B816C3F`; both unchanged from independent review |

Local supplement logs: `final-caller-check-only.log`, `final-caller-exact-argv-negative.log` under ignored `artifacts/task-48-deployment/`. Code/candidate pins, parse, UTF-8, links and whitespace checks accompany the final approval snapshot. The earlier source snapshots/reports remain historical immutable evidence; doc wording and caller added after v4 are recorded in a new v5 snapshot.

Final approval snapshot: `artifacts/task-48-deployment/final-approval-pins-v5.json`, **22 entries**, SHA-256 **`CE1C6EE7062082195E81EEAABEBBF91F4CF7EBDDAE380570E2CC9346FFCB7A2F`**. It preserves the reviewed v4 manifest/report, all source/test/program bytes, and current doc/caller follow-ups. All reviewed source/tests/program bytes were recomputed unchanged; only the disclosed post-review docs/caller are new. `final-readiness-summary.json` records no elevated host execution/launch authority or run. This report itself is outside that snapshot to avoid a self-referential hash.

## Open host gates

Exact approval still needs to cover: additional protected execution/code/compiler/evidence root; one old guarded removal with owned certificate deletion; one pinned installation with a fresh 14-day non-exportable CurrentUser RSA-2048/SHA-256 signer and Root/TrustedPublisher trust; native certificate prompts; mandatory strict new-profile independent validation; declared partial-failure/no-retry/retained-infrastructure behavior; the owner-account trust assumption above. App launch, scan, source refresh, update, other app install/uninstall, reset, public distribution and Task 49 remain excluded.

Live permission-at-creation/compiler inheritance, Windows SHA-256 signing and trust, real RunAs exit capture, native certificate prompts, expiry behavior and the complete new installed profile remain unproven. They are what the separately approved single host test will measure. No synthetic/non-admin result is substituted for them.

No commit: Git user.name/email were rechecked and remain unset; no Git identity/configuration was changed. All pre-existing planning work is retained.
