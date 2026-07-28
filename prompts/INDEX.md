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
| 15 | Phase 1 review gate | GPT-5.6 Sol / High | `phase/1-inventory` | [`15_PHASE_1_REVIEW_GATE.md`](15_PHASE_1_REVIEW_GATE.md) |
| 16 | Version comparison engine | GPT-5.6 Terra / Medium | `phase/2-intelligence` | [`16_VERSION_COMPARISON_ENGINE.md`](16_VERSION_COMPARISON_ENGINE.md) |
| 17 | Deterministic matching and confidence engine | GPT-5.6 Terra / High | `phase/2-intelligence` | [`17_DETERMINISTIC_MATCHING_AND_CONFIDENCE_ENGINE.md`](17_DETERMINISTIC_MATCHING_AND_CONFIDENCE_ENGINE.md) |
| 18 | WinGet update intelligence, source cache, and freshness | GPT-5.6 Terra / High | `phase/2-intelligence` | [`18_WINGET_UPDATE_INTELLIGENCE_SOURCE_CACHE_AND_FRESHNESS.md`](18_WINGET_UPDATE_INTELLIGENCE_SOURCE_CACHE_AND_FRESHNESS.md) |
| 19 | Manual decisions and update-intelligence UX | GPT-5.6 Terra / Medium | `phase/2-intelligence` | [`19_MANUAL_DECISIONS_AND_UPDATE_INTELLIGENCE_UX.md`](19_MANUAL_DECISIONS_AND_UPDATE_INTELLIGENCE_UX.md) |
| 20 | Phase 2 review gate | GPT-5.6 Sol / High | `phase/2-intelligence` | [`20_PHASE_2_REVIEW_GATE.md`](20_PHASE_2_REVIEW_GATE.md) |
| 21 | Immutable update jobs and constrained non-elevated runner | GPT-5.6 Terra / High | `phase/3-execution` | [`21_IMMUTABLE_UPDATE_JOBS_AND_CONSTRAINED_NON_ELEVATED_RUNNER.md`](21_IMMUTABLE_UPDATE_JOBS_AND_CONSTRAINED_NON_ELEVATED_RUNNER.md) |
| 22 | Short-lived elevated helper and secure IPC | GPT-5.6 Sol / High | `phase/3-execution` | [`22_SHORT_LIVED_ELEVATED_HELPER_AND_SECURE_IPC.md`](22_SHORT_LIVED_ELEVATED_HELPER_AND_SECURE_IPC.md) |
| 23 | Sequential batches, post-update verification, history, and diagnostics | GPT-5.6 Terra / High | `phase/3-execution` | [`23_SEQUENTIAL_BATCHES_POST_UPDATE_VERIFICATION_HISTORY_AND_DIAGNOSTICS.md`](23_SEQUENTIAL_BATCHES_POST_UPDATE_VERIFICATION_HISTORY_AND_DIAGNOSTICS.md) |
| 24 | Downloaded-artifact cache and safe cleanup | GPT-5.6 Terra / High | `phase/3-execution` | [`24_DOWNLOADED_ARTIFACT_CACHE_AND_SAFE_CLEANUP.md`](24_DOWNLOADED_ARTIFACT_CACHE_AND_SAFE_CLEANUP.md) |
| 25 | Phase 3 security review gate | GPT-5.6 Sol / Extra High | `phase/3-execution` | [`25_PHASE_3_SECURITY_REVIEW_GATE.md`](25_PHASE_3_SECURITY_REVIEW_GATE.md) |
| 26 | Scheduled checks and Windows notifications | GPT-5.6 Terra / Medium | `phase/4-beta` | [`26_SCHEDULED_CHECKS_AND_WINDOWS_NOTIFICATIONS.md`](26_SCHEDULED_CHECKS_AND_WINDOWS_NOTIFICATIONS.md) |
| 27 | Factory-reset design gate | GPT-5.6 Sol / High | `phase/4-beta` | [`27_FACTORY_RESET_DESIGN_GATE.md`](27_FACTORY_RESET_DESIGN_GATE.md) |
| 28 | Factory reset and advanced data controls | GPT-5.6 Terra / High | `phase/4-beta` | [`28_FACTORY_RESET_AND_ADVANCED_DATA_CONTROLS.md`](28_FACTORY_RESET_AND_ADVANCED_DATA_CONTROLS.md) |
| 29 | Accessibility, reliability, and performance hardening | GPT-5.6 Terra / High | `phase/4-beta` | [`29_ACCESSIBILITY_RELIABILITY_AND_PERFORMANCE_HARDENING.md`](29_ACCESSIBILITY_RELIABILITY_AND_PERFORMANCE_HARDENING.md) |
| 30 | Packaging, release evidence, SBOM, and private beta preparation | GPT-5.6 Terra / High | `phase/4-beta` | [`30_PACKAGING_RELEASE_EVIDENCE_SBOM_AND_PRIVATE_BETA_PREPARATION.md`](30_PACKAGING_RELEASE_EVIDENCE_SBOM_AND_PRIVATE_BETA_PREPARATION.md) |
| 31 | Final release audit | GPT-5.6 Sol / Extra High | `phase/4-beta` | [`31_FINAL_RELEASE_AUDIT.md`](31_FINAL_RELEASE_AUDIT.md) |

## Conditional prompts

- Optional GitHub read-only scope: [`18B_OPTIONAL_OFFICIAL_GITHUB_RELEASES_READ_ONLY_CONNECTOR.md`](optional/18B_OPTIONAL_OFFICIAL_GITHUB_RELEASES_READ_ONLY_CONNECTOR.md)
- Failed phase review remediation: [`90_REMEDIATE_A_PHASE_REVIEW.md`](90_REMEDIATE_A_PHASE_REVIEW.md)
- Stuck task escalation: [`91_ESCALATE_A_STUCK_TASK.md`](91_ESCALATE_A_STUCK_TASK.md)
