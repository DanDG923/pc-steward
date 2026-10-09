# Curated manual release progress

Approved spec: `docs/superpowers/specs/2026-10-09-curated-app-redesign-design.md` (`0761fc1`). Approved implementation plan: `docs/superpowers/plans/2026-10-09-curated-app-redesign.md` (`764df33`). Product base: `e6339762a0e72abcb782338df9ce1cd3295aac7f`.

Execution is authorized for the manual release, task reviews, draft PRs and Windows CI. Merging, deployment, real setup/installers/UAC, automatic-update hosts, dependency/SDK changes and global Git identity changes remain excluded.

Branch: `feat/curated-manual-release`, targeting `ccr-e4805080-7gyz6n`. Both approved documentation commits are preserved. Latest upstream fetch on 2026-10-09 found the same safety base. [Baseline Windows CI](https://github.com/DanDG923/pc-steward/actions/runs/37985975133) succeeded; the approved baseline is 593 passing tests.

Local `dotnet --list-sdks` is empty. No local product build/test pass is claimed. Code commits use the authenticated GitHub connector; prior local one-commit author approvals are not reused.

| Task | Deliverable | Status / evidence |
|---|---|---|
| 1 | Typed provider health and strict probes | Expected-red checkpoint ready: inert compiling probes plus 14 fake-only cases; Windows build/assertion evidence and implementation pending. No behavior is described as complete. |
| 2 | Reviewed Chocolatey setup and install choices | Pending |
| 3 | Additive membership and removal | Pending |
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
- Windows interactive UI/accessibility/process-resource measurements remain future evidence; fixture CI cannot establish them. No real installed app has been changed.
