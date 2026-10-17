# Curated manual release progress

Approved spec: `docs/superpowers/specs/2026-10-09-curated-app-redesign-design.md` (`0761fc1`). Approved implementation plan: `docs/superpowers/plans/2026-10-09-curated-app-redesign.md` (`764df33`). Product base: `e6339762a0e72abcb782338df9ce1cd3295aac7f`.

Execution is authorized for the manual release, task reviews, draft PRs and Windows CI. Merging, deployment, real setup/installers/UAC, automatic-update hosts, dependency/SDK changes and global Git identity changes remain excluded.

Branch: `feat/curated-manual-release`, targeting `ccr-e4805080-7gyz6n`. Both approved documentation commits are preserved. Latest upstream fetch on 2026-10-09 found the same safety base. [Baseline Windows CI](https://github.com/DanDG923/pc-steward/actions/runs/37985975133) succeeded; the approved baseline is 593 passing tests.

Local `dotnet --list-sdks` is empty. No local product build/test pass is claimed. Code commits use the authenticated GitHub connector; prior local one-commit author approvals are not reused.

| Task | Deliverable | Status / evidence |
|---|---|---|
| 1 | Typed provider health and strict probes | Code/review/CI gates passed at `b96a5485`; [CI 37996153500](https://github.com/DanDG923/pc-steward/actions/runs/37996153500): clean build, 621 passed / 0 failed / 0 skipped. Scoped repair review approved. Minor M1 late trust-exception cancellation-reporting behavior recorded for final triage; native timing and later stale UI retention unverified. |
| 2 | Reviewed Chocolatey setup and install choices | Code/review/CI gates passed at `54eab743`; [CI 38002369017](https://github.com/DanDG923/pc-steward/actions/runs/38002369017): clean build, 675 passed / 0 failed / 0 skipped. Independent scoped I1/I2 repair review approved with no new findings. Tasks7/12 integration/native limits remain carried. |
| 3 | Additive membership and removal | **Review I1/M1 repair complete; scoped re-review pending.** Review CHANGES REQUESTED (0C/1I/1M), see [review](checkpoints/2026-10-09-curated-manual/task-3-review.md). Regression `111b448` red [CI 39080035075](https://github.com/DanDG923/pc-steward/actions/runs/39080035075) (1 assertion). Fix `bf99db1` green [CI 39080312565](https://github.com/DanDG923/pc-steward/actions/runs/39080312565): 704 passed / 0 failed / 0 skipped, 0 warnings. [Repair report](checkpoints/2026-10-09-curated-manual/task-3-repair-report.md). Initial delivery: Expected-red `817cb57`, [CI 38004192067](https://github.com/DanDG923/pc-steward/actions/runs/38004192067): 22 intended assertion failures, clean build. Production `5e36d0c` plus one fixture repair `12310e1`, [CI 39079096574](https://github.com/DanDG923/pc-steward/actions/runs/39079096574): 0 warnings/errors, 703 passed / 0 failed / 0 skipped. [Report](checkpoints/2026-10-09-curated-manual/task-3-report.md). |
| 4 | Curated snapshots and decision association | Pending |
| 5 | Temporary queue and updates-only selection | Pending |
| 6 | Live review and atomic confirmation | Pending |
| 7 | Batch session, cancellation and recovery | Pending |
| 8 | Vetted bundled icons | Pending |
| 9 | Safe local icons/cache/reset | Pending |
| 10 | My Apps/Add Apps/tray/inspector | Pending |
| 11 | Deliberate reviewed catalog search | Pending |
| 12 | Desktop composition/review/progress | Pending |
| 13 | Protected inventory/Activity/Settings | Pending |
| 14 | Resource/accessibility evidence | Pending |
| 15 | Whole-change review and owner acceptance | Pending |

## Decisions and limits

- Reuse the task-created isolated checkout: it preserves the approved docs and avoids disturbing existing working copies. If isolation proves insufficient, re-isolate before integration.
- Native PowerShell creates per-plan briefs, review packages and an ignored local ledger; Bash/helper resources are unavailable. Regeneration affects development artifacts only.
- Initial feature-branch publication was rejected by automatic review under the historical validation-only scope. The same push succeeded after remote ownership/private visibility and current explicit publication approval were verified. No shared development/main branch changed.
- Direct progress messages to the parent thread were rejected by automatic review because destination authorization was not recognized. No alternate messaging route is used; this progress record and task output remain available.
- Reserved setup may distinguish only exact optional Chocolatey availability warnings after independent fresh positive absence repeated after the scan; all unknown/partial/other warnings and ordinary paths still refuse. If wrong, remove the exception and rework native inventory/presence composition before making setup actionable.
- Reserved setup must repeat positive absence after every acceptable successful scan, including clean scans, because client presence is independent of package registration. If wrong, this adds one redundant bounded observation and review/test rework; it provides no OS lock.
- Windows interactive UI/accessibility/process-resource measurements remain future evidence; fixture CI cannot establish them. No real installed app has been changed.
