# 54A — WinGet COM projection refresh spike

**Planning status:** **Proposed 2026-10-30; not owner-accepted.** Added by the [V2 dependency servicing proposal](../docs/review/V2_DEPENDENCY_SERVICING_PROPOSAL.md). It does not change any Task 47 decision. Until the owner accepts it, Phase D entry is unchanged.

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Independent 54 passed; proposal accepted. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `NFR-MNT-01; NFR-MNT-03; NFR-MNT-05; FR-CAT-01–06; FR-SRC-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 54A — WinGet COM projection refresh spike.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract, ADR-003, ADR-023, ADR-025, the dependency servicing proposal and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Independent 54 passed; proposal accepted.

**Files to inspect / expected change boundary:** `src/PCSteward.Provider.WinGet/; src/PCSteward.ElevatedHelper/; Directory.Packages.props; Directory.Build.props; scripts/New-PrivateBetaPackage.ps1; scripts/Test-PrivateBetaPackageEvidence.ps1; spikes/`. Work in a new `spikes/winget-com-projection/` folder only. No production source changes.

**Deliverable:** A committed spike report and a drafted ADR-003 amendment (status Proposed) that answer:

1. **Platform target.** `Microsoft.WindowsPackageManager.ComInterop`/`InProcCom` 1.11+ require a `net*-windows10.0.26100` target. No project sets `TargetPlatformMinVersion` or `SupportedOSPlatformVersion` today, so the minimum OS currently follows the `windows10.0.22000.0` target. Can the projects take the 26100 target with those two properties set explicitly to `10.0.22000.0`, keeping ADR-003's Windows 11 22000 baseline, and does the produced package then still pass on that baseline? If not, state exactly which minimum OS changes; that is an owner decision.
2. **Activation routes.** Does the in-process COM route still activate and run from the desktop process and from the one-shot elevated helper (ADR-018)? Compare against the out-of-process route.
3. **API need.** For the WS3/WS4 operations accepted in ADR-023/024 (bounded search, exact version/scope/architecture enforcement, install and uninstall), does 1.10.340 already expose what is needed, or do 1.11+ APIs remove a known gap? Cite the API surface, not release-note claims.
4. **Evidence impact.** `New-PrivateBetaPackage.ps1` (`$inProcComVersion` and the two licence-map keys) and `Test-PrivateBetaPackageEvidence.ps1` (`$expectedInProcComKey`) hard-code `1.10.340`. What else depends on its file layout, and what changes?

**Verification and failure cases:** Spike builds use a throwaway project under `spikes/`. Any WinGet catalogue query, search or COM activation on the owner's host is a host action and needs separate approval under the host-test plan. Without it, answer from build output, metadata and documentation, and mark runtime questions unproven. Record the installed WinGet client version only from evidence collected under an approval.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** A committed spike report and a Proposed ADR-003 amendment that ends in one recommendation: **adopt before Task 55** (then 54B), or **defer to Task 72** because 1.10.340 meets ADR-023/024 needs. The owner accepts either before 54B or 55 starts.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
