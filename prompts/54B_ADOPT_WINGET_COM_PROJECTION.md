# 54B — Adopt the refreshed WinGet COM projection

**Planning status:** **Proposed 2026-10-30; not owner-accepted.** Added by the [V2 dependency servicing proposal](../docs/review/V2_DEPENDENCY_SERVICING_PROPOSAL.md). It runs only if 54A recommends adoption and the owner accepts the ADR-003 amendment.

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 54A committed; ADR-003 amendment owner-Accepted with the adopt outcome; owner approval of the exact package versions and target framework recorded under `AGENTS.md`. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `NFR-MNT-01; NFR-MNT-03; NFR-MNT-05; FR-SRC-01–05; FR-UPD-02–04`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 54B — Adopt the refreshed WinGet COM projection.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract, the accepted ADR-003 amendment, the 54A spike report and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 54A committed; ADR-003 amendment accepted with the adopt outcome; exact versions and target approved.

**Files to inspect / expected change boundary:** `Directory.Packages.props; Directory.Build.props; the Windows-targeted *.csproj files; src/PCSteward.Provider.WinGet/; scripts/New-PrivateBetaPackage.ps1; scripts/Test-PrivateBetaPackageEvidence.ps1; docs/adr/ADR-003-WINGET-INTEGRATION-ROUTE.md`. Change only what the accepted amendment names. Do not start WS3 search/install behaviour here.

**Deliverable:** Move `Microsoft.WindowsPackageManager.ComInterop` and `InProcCom` to the accepted version and the Windows-targeted projects to the accepted target framework/minimum OS. Keep existing ADR-003 behaviour unchanged: the structured candidate authority still selects only the predefined `winget`/`msstore` catalogs, queries an exact ID with the two-result bound, applies the 20-second bound and fails closed. Update the packaging evidence script and licence/NOTICE map for the new file layout.

**Verification and failure cases:** All existing ADR-003 production-adapter tests pass unchanged (trusted/predefined/exact/single/newer acceptance and untrusted, wrong-source, wrong-ID, duplicate, limit, malformed version, stale/equal/older and stalled-query rejection). Canonical Release build has zero warnings and the full suite passes. The private-beta package builds and its evidence check passes. Any live catalogue query or desktop/helper activation on the host needs separate approval under the host-test plan; otherwise record runtime activation as unproven.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Committed projection/target change with unchanged ADR-003 behaviour, green canonical gates and updated packaging evidence. Task 55 builds on it; Task 58's security gate reviews it with the rest of Phase D.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
