# Task 50 — paused at owner's request

Owner instruction: “Can we pause for now as I'm going to bed”. Stop implementation, testing, consultation and commits until the owner resumes. No active child/process run remains from this turn; the three latest consultation acknowledgments completed. No deployment, provider call, UAC or live update occurred.

## Last green checkpoint

Branch phase/5-v2-planning. Committed HEAD44ff0cd45e33e1ba0376bc2275ea591a1071318b. Canonical Release zero warnings/errors; full900/900, zero skipped; focused63/63. Details in TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md. Task50 remains incomplete; native admission and common-gate/claim/helper/runner/UI/history integration remain open.

## Owner decisions to preserve

The provisional candidate split is already accepted/recorded: desktop UserCacheIntent, independent exact-match helper qualification before upgrade. The owner has now also answered the prepared runtime checksum/argument/TEMP limitation question: “I think Chocolatey is fine as long it uses the official community repo”. Main explicitly restated acceptance of the described runtime limitations conditional on the fixed official Community feed, with no sandbox claim; all three consultation participants acknowledge the interpretation. The official remote feed condition includes target/dependency downloads, while existing local registrations may satisfy dependencies with historical origin unknown. Vendor payload downloads by scripts remain disclosed. Keep initial checksum configuration and fixed require-checksums/ignore-remembered-arguments/fail-on-not-installed flags, with remembered-arguments observed off. All other checks and host/deployment gates stay required.

## Uncommitted work at pause

ADR-025, V2_PLANNING_DECISIONS, V2_TASK_CONTRACT and the V2 remediation record have the new exact quote/conditional exception and planned canonical interface. Do not silently drop these when resuming. The older failure record/STATUS/TRACEABILITY still describe the runtime question as pending and need reconciliation **after resumption**.

Tests added/changed BEFORE implementation: new tests/PCSteward.Domain.UnitTests/ReviewedProviderGuardContractTests.cs; modified tests/PCSteward.Application.IntegrationTests/ChocolateyReviewedUpdateArgumentsTests.cs. They currently fail compilation because ChocolateyMachineRuntimeLimitsV3, ReviewedProviderGuardContract/ProviderGuardContract and ReviewedUpdateProviderDisclosure do not exist yet. This is deliberate red-stage work, not the last green committed state. Logs: TEMP/pc-steward-task50-community-contract-red-{domain,app}.log. Do not commit this partial red checkpoint as a passing implementation.

## Intended next step when explicitly resumed

Implement the documented distinct canonical route/guard contract without changing schema3/frame shape: old provisional/legacy operations retain their old meanings and unresolved barriers. Both provisional/new runtime routes carry UserCacheIntent; only exact Chocolatey/Community/Machine current-schema shapes are valid. The passive formatter must accept the new runtime-limit route and reject the earlier provisional one. Application disclosure must state the fixed official remote feed, local dependency/vendor download limits, elevated scripts, refresh-driven checksum/argument overrides and TEMP replacement risk. Confirmation/native dispatch stay closed pending actual native integration.

Before green, strengthen the newly added disclosure test to use a frozen TimeProvider at operation.ReviewedAt; its current Confirm() call with the system clock could fail for age/future time rather than unavailable admission. Add an explicit older-provisional disclosure-negative. Then focused tests, canonical Release/full suite, STATUS/TRACEABILITY/failure record, scoped diff/green commit. No test/build/implementation was performed after the pause request. Preserve historical V1/V2 mapping pins; future corrected mapping belongs in a new revision or the remediation record.