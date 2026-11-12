# Task 54 — Phase C independent review gate

Phase C stage C3 (owner-agreed plan, 2026-11-12). Prompt: [54](../../prompts/54_UPDATER_REVIEW_GATE.md). Requirements: `FR-UPD-01–15`, `AC-UPD-01–11`, `TM-03–04/07/13`. Entry: the committed [Task 53 report](TASK_53_PHASE_C_EVIDENCE_READINESS.md) and its pinned snapshot.

**Status: not cleared. Two P1 findings, both confirmed against the code. Phase C stays open until they are fixed or the owner accepts them.**

## Reviewer and independence

The Phase C plan the owner agreed on 2026-11-12 keeps the project's way of working: Claude threads build, and Codex (chatgpt-codex-connector) reviews independently before merge, with C3 as the formal Phase C review gate. Codex wrote none of Tasks 49–52, 53A, 54B or this PR. The author of the Task 53 report did not review its own work for this gate.

## Scope given to the reviewer

The snapshot in Task 53 §1: Tasks 49–52 as merged, the 53A package delta and its Windows App Runtime prerequisite, the 54B projection and target change, and the third-party notices change, against `FR-UPD-01–15`, `AC-UPD-01–11`, ADR-025 and ADR-026. Focus: progress, mode, restart and batch authority; migration; security of the update, helper and packaging paths; and whether the Task 53 evidence supports each claim.

## Result

Codex code review of `6261fb0` (2026-11-12): two P1 findings, both on the Task 53 report and both confirmed. The Codex security review of the same commit completed with no finding posted. The notices change drew no finding.

| Id | Finding | Verified against | Effect on the report |
|---|---|---|---|
| R1 (P1) | The desktop does not bind or enforce a reviewed installer mode for WinGet updates. `App.xaml.cs` composes `ReviewedUpdateExecutionWorkflow` → `ApprovedUpdateExecutor` → `NonElevatedUpdateRunner(new WingetUpdateExecutor())` on the legacy `ImmutableUpdateJob`, which has no mode field. `WingetUpdateExecutor.BuildUpdateArguments` has no `--silent`. `WingetReviewedUpdateArguments.Build` (the Silent/MSIX mapping) is called only by tests. | `src/PCSteward.Desktop/App.xaml.cs`, `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs`, `src/PCSteward.Provider.WinGet/WingetReviewedUpdateArguments.cs`. TRACEABILITY already called that formatter "passive". | FR-UPD-15 is now met for the Chocolatey route only; WinGet is Gap R1. Task 50's G2 row says the same. |
| R2 (P1) | ADR-025 protocol isolation is not met. `ElevatedHelperEntryPoint.RunAsync` sends every line that is not self-update, probe or `--protocol 2` to `RunProtocol1Async`, which keeps the WinGet and Chocolatey machine executors. The desktop still uses protocol 1 for machine-scope WinGet (`ElevatedUpdateRunner`). The cited tests prove protocol 2 never falls back to protocol 1, not that the new helper refuses protocol 1. | `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs`, ADR-025 "Privilege and protocol boundary". | The protocol-rejection case is now "Not met" (§5); gap R2 in §9. Removing protocol 1 today would stop machine-scope WinGet updates, so this needs an owner choice. |

Both are product gaps that predate this PR; neither is caused by it. The report was corrected to state them, and the owner was asked how to close them.
