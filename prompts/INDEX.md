# Prompt index

Open a fresh Codex chat for every numbered task. Keep the same chat only while completing or correcting that single task.

| Task | Outcome | Model / reasoning | Branch | Prompt |
|---|---|---|---|---|
| 00 | Environment and repository audit | GPT-5.6 Luna / Low | `phase/0-foundation` | [`00_ENVIRONMENT_AND_REPOSITORY_AUDIT.md`](00_ENVIRONMENT_AND_REPOSITORY_AUDIT.md) |
| 01 | Architecture, threat model, and execution plan | GPT-5.6 Sol / High | `phase/0-foundation` | [`01_ARCHITECTURE_THREAT_MODEL_AND_EXECUTION_PLAN.md`](01_ARCHITECTURE_THREAT_MODEL_AND_EXECUTION_PLAN.md) |
| 02 | Spike: packaging and WinGet integration | GPT-5.6 Terra / High | `phase/0-foundation` | [`02_SPIKE_PACKAGING_AND_WINGET_INTEGRATION.md`](02_SPIKE_PACKAGING_AND_WINGET_INTEGRATION.md) |
| 03 | Spike: privilege boundary and safe deletion | GPT-5.6 Sol / High | `phase/0-foundation` | [`03_SPIKE_PRIVILEGE_BOUNDARY_AND_SAFE_DELETION.md`](03_SPIKE_PRIVILEGE_BOUNDARY_AND_SAFE_DELETION.md) |
| 04 | Spike: performance baseline and fixture matrix | GPT-5.6 Terra / High | `phase/0-foundation` | [`04_SPIKE_PERFORMANCE_BASELINE_AND_FIXTURE_MATRIX.md`](04_SPIKE_PERFORMANCE_BASELINE_AND_FIXTURE_MATRIX.md) |
| 05 | Lock ADRs and architecture | GPT-5.6 Sol / High | `phase/0-foundation` | [`05_LOCK_ADRS_AND_ARCHITECTURE.md`](05_LOCK_ADRS_AND_ARCHITECTURE.md) |
| 06 | Scaffold solution and engineering guardrails | GPT-5.6 Terra / Medium | `phase/0-foundation` | [`06_SCAFFOLD_SOLUTION_AND_ENGINEERING_GUARDRAILS.md`](06_SCAFFOLD_SOLUTION_AND_ENGINEERING_GUARDRAILS.md) |
| 07 | Core domain model and SQLite persistence | GPT-5.6 Terra / Medium | `phase/1-inventory` | [`07_CORE_DOMAIN_MODEL_AND_SQLITE_PERSISTENCE.md`](07_CORE_DOMAIN_MODEL_AND_SQLITE_PERSISTENCE.md) |
| 08 | Registry inventory providers | GPT-5.6 Terra / Medium | `phase/1-inventory` | [`08_REGISTRY_INVENTORY_PROVIDERS.md`](08_REGISTRY_INVENTORY_PROVIDERS.md) |
| 09 | MSIX/AppX inventory provider | GPT-5.6 Terra / Medium | `phase/1-inventory` | [`09_MSIX_APPX_INVENTORY_PROVIDER.md`](09_MSIX_APPX_INVENTORY_PROVIDER.md) |
| 10 | WinGet installed-inventory provider | GPT-5.6 Terra / High | `phase/1-inventory` | [`10_WINGET_INSTALLED_INVENTORY_PROVIDER.md`](10_WINGET_INSTALLED_INVENTORY_PROVIDER.md) |
| 11 | Normalization, install identity, and conservative deduplication | GPT-5.6 Terra / High | `phase/1-inventory` | [`11_NORMALIZATION_INSTALL_IDENTITY_AND_CONSERVATIVE_DEDUPLICATION.md`](11_NORMALIZATION_INSTALL_IDENTITY_AND_CONSERVATIVE_DEDUPLICATION.md) |
| 12 | Scan orchestration, cancellation, timings, and performance harness | GPT-5.6 Terra / High | `phase/1-inventory` | [`12_SCAN_ORCHESTRATION_CANCELLATION_TIMINGS_AND_PERFORMANCE_HARNESS.md`](12_SCAN_ORCHESTRATION_CANCELLATION_TIMINGS_AND_PERFORMANCE_HARNESS.md) |
| 13 | App shell, onboarding, and inventory experience | GPT-5.6 Terra / Medium | `phase/1-inventory` | [`13_APP_SHELL_ONBOARDING_AND_INVENTORY_EXPERIENCE.md`](13_APP_SHELL_ONBOARDING_AND_INVENTORY_EXPERIENCE.md) |
| 14 | Exports and Clear scan data | GPT-5.6 Terra / High | `phase/1-inventory` | [`14_EXPORTS_AND_CLEAR_SCAN_DATA.md`](14_EXPORTS_AND_CLEAR_SCAN_DATA.md) |
| 14A | Prepare Phase 1 review evidence | GPT-5.6 Terra / High | `phase/1-inventory` | [`14A_PREPARE_PHASE_1_REVIEW_EVIDENCE.md`](14A_PREPARE_PHASE_1_REVIEW_EVIDENCE.md) |
| 15 | Phase 1 review gate | GPT-5.6 Sol / High | `phase/1-inventory` | [`15_PHASE_1_REVIEW_GATE.md`](15_PHASE_1_REVIEW_GATE.md) |
| 16 | Version comparison engine | GPT-5.6 Terra / Medium | `phase/2-intelligence` | [`16_VERSION_COMPARISON_ENGINE.md`](16_VERSION_COMPARISON_ENGINE.md) |
| 17 | Deterministic matching and confidence engine | GPT-5.6 Terra / High | `phase/2-intelligence` | [`17_DETERMINISTIC_MATCHING_AND_CONFIDENCE_ENGINE.md`](17_DETERMINISTIC_MATCHING_AND_CONFIDENCE_ENGINE.md) |
| 18 | WinGet update intelligence, source cache, and freshness | GPT-5.6 Terra / High | `phase/2-intelligence` | [`18_WINGET_UPDATE_INTELLIGENCE_SOURCE_CACHE_AND_FRESHNESS.md`](18_WINGET_UPDATE_INTELLIGENCE_SOURCE_CACHE_AND_FRESHNESS.md) |
| 19 | Manual decisions and update-intelligence UX | GPT-5.6 Terra / Medium | `phase/2-intelligence` | [`19_MANUAL_DECISIONS_AND_UPDATE_INTELLIGENCE_UX.md`](19_MANUAL_DECISIONS_AND_UPDATE_INTELLIGENCE_UX.md) |
| 19A | Prepare Phase 2 review evidence | GPT-5.6 Terra / High | `phase/2-intelligence` | [`19A_PREPARE_PHASE_2_REVIEW_EVIDENCE.md`](19A_PREPARE_PHASE_2_REVIEW_EVIDENCE.md) |
| 20 | Phase 2 review gate | GPT-5.6 Sol / High | `phase/2-intelligence` | [`20_PHASE_2_REVIEW_GATE.md`](20_PHASE_2_REVIEW_GATE.md) |
| 21 | Immutable update jobs and constrained non-elevated runner | GPT-5.6 Terra / High | `phase/3-execution` | [`21_IMMUTABLE_UPDATE_JOBS_AND_CONSTRAINED_NON_ELEVATED_RUNNER.md`](21_IMMUTABLE_UPDATE_JOBS_AND_CONSTRAINED_NON_ELEVATED_RUNNER.md) |
| 22 | Short-lived elevated helper and secure IPC | GPT-5.6 Sol / High | `phase/3-execution` | [`22_SHORT_LIVED_ELEVATED_HELPER_AND_SECURE_IPC.md`](22_SHORT_LIVED_ELEVATED_HELPER_AND_SECURE_IPC.md) |
| 23 | Sequential batches, post-update verification, history, and diagnostics | GPT-5.6 Terra / High | `phase/3-execution` | [`23_SEQUENTIAL_BATCHES_POST_UPDATE_VERIFICATION_HISTORY_AND_DIAGNOSTICS.md`](23_SEQUENTIAL_BATCHES_POST_UPDATE_VERIFICATION_HISTORY_AND_DIAGNOSTICS.md) |
| 24 | Downloaded-artifact cache and safe cleanup | GPT-5.6 Terra / High | `phase/3-execution` | [`24_DOWNLOADED_ARTIFACT_CACHE_AND_SAFE_CLEANUP.md`](24_DOWNLOADED_ARTIFACT_CACHE_AND_SAFE_CLEANUP.md) |
| 24A | Prepare Phase 3 security-review evidence | GPT-5.6 Terra / High | `phase/3-execution` | [`24A_PREPARE_PHASE_3_REVIEW_EVIDENCE.md`](24A_PREPARE_PHASE_3_REVIEW_EVIDENCE.md) |
| 25 | Phase 3 security review gate | GPT-5.6 Sol / Extra High | `phase/3-execution` | [`25_PHASE_3_SECURITY_REVIEW_GATE.md`](25_PHASE_3_SECURITY_REVIEW_GATE.md) |
| 26 | Scheduled checks and Windows notifications | GPT-5.6 Terra / High | `phase/4-beta` | [`26_SCHEDULED_CHECKS_AND_WINDOWS_NOTIFICATIONS.md`](26_SCHEDULED_CHECKS_AND_WINDOWS_NOTIFICATIONS.md) |
| 26B | Native shell, onboarding, and scan experience | GPT-5.6 Terra / High | `phase/4-beta` | [`26B_NATIVE_SHELL_ONBOARDING_AND_SCAN_EXPERIENCE.md`](26B_NATIVE_SHELL_ONBOARDING_AND_SCAN_EXPERIENCE.md) |
| 26C | Applications, findings, and update review experience | GPT-5.6 Terra / High | `phase/4-beta` | [`26C_APPLICATIONS_FINDINGS_AND_UPDATE_REVIEW_EXPERIENCE.md`](26C_APPLICATIONS_FINDINGS_AND_UPDATE_REVIEW_EXPERIENCE.md) |
| 26A | Prepare factory-reset design-gate evidence | GPT-5.6 Terra / High | `phase/4-beta` | [`26A_PREPARE_FACTORY_RESET_DESIGN_EVIDENCE.md`](26A_PREPARE_FACTORY_RESET_DESIGN_EVIDENCE.md) |
| 27 | Factory-reset design gate | GPT-5.6 Sol / High | `phase/4-beta` | [`27_FACTORY_RESET_DESIGN_GATE.md`](27_FACTORY_RESET_DESIGN_GATE.md) |
| 28 | Factory reset and advanced data controls | GPT-5.6 Terra / High | `phase/4-beta` | [`28_FACTORY_RESET_AND_ADVANCED_DATA_CONTROLS.md`](28_FACTORY_RESET_AND_ADVANCED_DATA_CONTROLS.md) |
| 29 | Accessibility, reliability, and performance hardening | GPT-5.6 Terra / High | `phase/4-beta` | [`29_ACCESSIBILITY_RELIABILITY_AND_PERFORMANCE_HARDENING.md`](29_ACCESSIBILITY_RELIABILITY_AND_PERFORMANCE_HARDENING.md) |
| 30 | Packaging, release evidence, SBOM, and private beta preparation | GPT-5.6 Terra / High | `phase/4-beta` | [`30_PACKAGING_RELEASE_EVIDENCE_SBOM_AND_PRIVATE_BETA_PREPARATION.md`](30_PACKAGING_RELEASE_EVIDENCE_SBOM_AND_PRIVATE_BETA_PREPARATION.md) |
| 30A | Prepare final release-audit evidence | GPT-5.6 Terra / High | `phase/4-beta` | [`30A_PREPARE_FINAL_RELEASE_AUDIT_EVIDENCE.md`](30A_PREPARE_FINAL_RELEASE_AUDIT_EVIDENCE.md) |
| 31 | Final release audit | GPT-5.6 Sol / Extra High | `phase/4-beta` | [`31_FINAL_RELEASE_AUDIT.md`](31_FINAL_RELEASE_AUDIT.md) |

## PC Steward 2.0 — Task 47 planning package

**Finalized Task 47 package:** applicable ADR-023–026, numerical catalogue/warning/lifecycle defaults and final review/gate/deadline choices are owner-Accepted after both requested Opus 5.5 reviews and disclosed follow-ups. Start with [Phase A2](../docs/requirements/V2_PHASE_A2.md) and the [shared execution/evidence contract](../docs/requirements/V2_TASK_CONTRACT.md). These prompts do not start future work or authorize host actions. Existing rows above retain historical model settings; 2.0 settings below follow the accepted delta.

| Task | Outcome | Model / reasoning | Phase | Prompt |
|---|---|---|---|---|
| 48 | Owner-only redeploy | GPT-6.1 Sol / High | B | [Task 48](48_OWNER_ONLY_REDEPLOY.md) |
| 49 | Truthful update progress | GPT-6.1 Sol / High | C | [Task 49](49_TRUTHFUL_UPDATE_PROGRESS.md) |
| 50 | Enforceable reviewed installer mode | GPT-6.1 Sol / High | C | [Task 50](50_REVIEWED_INSTALLER_MODE.md) |
| 51 | Reliable restart outcomes | GPT-6.1 Sol / High | C | [Task 51](51_RELIABLE_RESTART_OUTCOMES.md) |
| 52 | Frozen reviewed update batches | GPT-6.1 Sol / High | C | [Task 52](52_FROZEN_UPDATE_BATCHES.md) |
| 53A | Dependency servicing before updater evidence (**Proposed**) | GPT-6.1 Sol / High | C | [Task 53A](53A_DEPENDENCY_SERVICING.md) |
| 53 | Updater evidence readiness | GPT-6.1 Terra / High | C | [Task 53](53_UPDATER_EVIDENCE_READINESS.md) |
| 54 | Updater review gate | GPT-6.1 Sol / Extra High | C | [Task 54](54_UPDATER_REVIEW_GATE.md) |
| 54A | WinGet COM projection refresh spike (**Proposed**) | GPT-6.1 Sol / High | D | [Task 54A](54A_WINGET_COM_PROJECTION_SPIKE.md) |
| 54B | Adopt the refreshed WinGet COM projection (**Proposed**, conditional on 54A) | GPT-6.1 Sol / High | D | [Task 54B](54B_ADOPT_WINGET_COM_PROJECTION.md) |
| 55 | Bounded catalogue and favourites | GPT-6.1 Sol / High | D | [Task 55](55_BOUNDED_CATALOGUE_SEARCH.md) |
| 56 | Distinct reviewed install jobs | GPT-6.1 Sol / High | D | [Task 56](56_DISTINCT_INSTALL_JOBS.md) |
| 57 | Install evidence readiness | GPT-6.1 Terra / High | D | [Task 57](57_INSTALL_EVIDENCE_READINESS.md) |
| 58 | Install security review gate | GPT-6.1 Sol / Extra High | D | [Task 58](58_INSTALL_SECURITY_REVIEW_GATE.md) |
| 59 | One approved live install | GPT-6.1 Sol / High | D | [Task 59](59_APPROVED_LIVE_INSTALL.md) |
| 60 | Instance-bound manual uninstall | GPT-6.1 Sol / High | E | [Task 60](60_INSTANCE_BOUND_UNINSTALL.md) |
| 61 | Uninstall evidence readiness | GPT-6.1 Terra / High | E | [Task 61](61_UNINSTALL_EVIDENCE_READINESS.md) |
| 62 | Uninstall security review gate | GPT-6.1 Sol / Extra High | E | [Task 62](62_UNINSTALL_SECURITY_REVIEW_GATE.md) |
| 63 | One approved live uninstall | GPT-6.1 Sol / High | E | [Task 63](63_APPROVED_LIVE_UNINSTALL.md) |
| 64 | Bounded local diagnostic timeline | GPT-6.1 Sol / High | F | [Task 64](64_BOUNDED_DIAGNOSTIC_TIMELINE.md) |
| 65 | Diagnostic evidence readiness | GPT-6.1 Terra / High | F | [Task 65](65_DIAGNOSTIC_EVIDENCE_READINESS.md) |
| 66 | Diagnostic review gate | GPT-6.1 Sol / Extra High | F | [Task 66](66_DIAGNOSTIC_REVIEW_GATE.md) |
| 67 | Advisory AI privacy path with mocks | GPT-6.1 Sol / High | G | [Task 67](67_ADVISORY_AI_WITH_MOCKS.md) |
| 68 | Transactional budget and reconciliation | GPT-6.1 Sol / High | G | [Task 68](68_TRANSACTIONAL_AI_BUDGET.md) |
| 69 | AI privacy and cost evidence readiness | GPT-6.1 Terra / High | G | [Task 69](69_AI_EVIDENCE_READINESS.md) |
| 70 | AI security, privacy and cost review gate | GPT-6.1 Sol / Extra High | G | [Task 70](70_AI_SECURITY_PRIVACY_COST_GATE.md) |
| 71 | One preview-approved live explanation | GPT-6.1 Sol / High | G | [Task 71](71_APPROVED_LIVE_AI_EXPLANATION.md) |
| 72 | Deferred-item decisions only | GPT-6.1 Sol / High | H | [Task 72](72_DEFERRED_ITEM_DECISIONS.md) |

## Conditional prompts

- Optional GitHub read-only scope: [`18B_OPTIONAL_OFFICIAL_GITHUB_RELEASES_READ_ONLY_CONNECTOR.md`](optional/18B_OPTIONAL_OFFICIAL_GITHUB_RELEASES_READ_ONLY_CONNECTOR.md)
- Failed phase review remediation: [`90_REMEDIATE_A_PHASE_REVIEW.md`](90_REMEDIATE_A_PHASE_REVIEW.md)
- Stuck task escalation: [`91_ESCALATE_A_STUCK_TASK.md`](91_ESCALATE_A_STUCK_TASK.md)
