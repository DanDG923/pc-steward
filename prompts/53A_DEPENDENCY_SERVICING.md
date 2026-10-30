# 53A — Dependency servicing before updater evidence

**Planning status:** **Proposed 2026-10-30; not owner-accepted.** Added by the [V2 dependency servicing proposal](../docs/review/V2_DEPENDENCY_SERVICING_PROPOSAL.md). It does not change any Task 47 decision. Until the owner accepts it, Task 53 entry is unchanged.

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 49–52 committed; proposal accepted; owner approval of the exact package set below recorded under `AGENTS.md`. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `NFR-MNT-01; NFR-MNT-05; NFR-REL-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 53A — Dependency servicing before updater evidence.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract, the dependency servicing proposal and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 49–52 committed; proposal accepted; owner approval of the exact package set recorded.

**Files to inspect / expected change boundary:** `Directory.Packages.props; scripts/New-PrivateBetaPackage.ps1; scripts/Test-PrivateBetaPackageEvidence.ps1; docs/review/; docs/requirements/TRACEABILITY.md`. Change only the approved package versions and the exact licence/NOTICE/evidence entries they require. Do not refactor unrelated modules.

**Deliverable:** Apply the Windows App SDK 1.8 servicing update (`Microsoft.WindowsAppSDK` 1.8.250907003 → the then-current 1.8 servicing release; 1.8.260921001 when proposed) and any other in-major NuGet servicing the owner approves. Regenerate the exact package/version licence and NOTICE map from the restored packages' own nuspec/licence/NOTICE files, not from memory. Record the dependency delta since Task 48 (including the already-merged `eef3784` SQLite/MSTest updates) as an input to Task 53.

**Verification and failure cases:** The canonical Release build has zero warnings, so the new MSTest 4.5 analyzers and Windows App SDK warnings count. The full suite passes. `New-PrivateBetaPackage.ps1` produces a package whose file set exactly matches the regenerated map, and `Test-PrivateBetaPackageEvidence.ps1` passes. A launch of the WinUI desktop on an isolated synthetic profile is a host action and needs separate approval under the host-test plan; without it, record UI behaviour as unproven, not passed. No WindowsAppSDK 2.x, no WinGet projection change, no new package.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Committed servicing change with green canonical gates, a regenerated and validated licence/NOTICE map, and a dependency-delta record that Task 53 cites.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
