# Task 35 production UI validation report

**Date:** 2026-08-05
**Branch:** `phase/4-beta`
**Scope:** production WinUI redesign, real-data rendering, structural accessibility, and large-inventory presentation
**Result:** implementation and automated repository gate pass; the prior native-storage result was a managed-sandbox restriction, while optional manual accessibility acceptance remains unclaimed

## Automated results

| Command / check | Result |
|---|---|
| `dotnet build src/PCSteward.Desktop/PCSteward.Desktop.csproj --configuration Debug --no-restore` | PASS — 0 warnings, 0 errors |
| `dotnet test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj --configuration Debug --no-restore` | PASS — 24/24 |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~InventoryExperienceViewModelTests|FullyQualifiedName~ChocolateyProviderTests"` | PASS — 23/23 |
| `pwsh -NoProfile -File scripts/Build.ps1` | PASS — Release build, 0 warnings, 0 errors |
| `dotnet run --project tests/PCSteward.Performance/PCSteward.Performance.csproj --configuration Release --no-build -- --output docs/review/raw/task-35-ui-performance.json` | PASS — machine-readable result written |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DownloadedArtifactCacheTests"` | PASS — 16/16 on the normal Windows host |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~FactoryResetTests"` | PASS — 25/25 on the normal Windows host |
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore` | PASS — 167/167 on the normal Windows host |
| `pwsh -NoProfile -File scripts/Test.ps1` | PASS — 267/267: Domain 55, Application 167, Architecture 24, Security 21 |

A Task 36 isolation run first reproduced the exact quota failure inside the Codex managed filesystem sandbox. The same test passed immediately outside that sandbox, followed by every downloaded-cache test, every factory-reset test, all Application integration tests, and the official full repository suite. This proves that `NTSTATUS=C0000022` came from the managed test boundary denying direct native handle operations, not from a production storage regression. The hardened native-delete implementation remains unchanged; no path-based fallback or conditional security-test skip was added.

The 300-second script timeout left five idle test-runner child processes from that run. Only those exact processes were stopped; ordinary .NET compiler-server processes were left untouched.

## UI architecture contracts

The Task 35 architecture tests assert:

- five stable task destinations plus contextual route retention;
- an explicit selectable Settings footer item and adaptive `NavigationView` thresholds;
- a reusable structured application row bound through a presentation model;
- a virtualized `ListView` rather than one card/control tree per inventory item;
- scan progress updates without recreating the shell for every event;
- Default, Light, Dark, and High Contrast resource dictionaries;
- the required audit, research, direction, design-system, and validation records.

## Real production app inspection

The rebuilt Debug app opened against the user's existing local PC Steward data. Read-only UI Automation observed the production window and 63 descendants, including the stable navigation, product header, local-storage badge, Overview actions, and real counts. The observed Overview contained:

- 658 installed applications;
- 3 verified updates ready for review;
- 4 items needing a decision;
- 114 up-to-date applications;
- 537 inventoried-only applications.

No scan, source refresh, update, cleanup, schedule mutation, or reset was invoked during visual validation.

Five window-content screenshots were captured from the production XAML tree and visually reviewed after rendering real persisted data:

- `docs/review/screenshots/overview-production.png`
- `docs/review/screenshots/applications-production.png`
- `docs/review/screenshots/updates-production.png`
- `docs/review/screenshots/activity-production.png`
- `docs/review/screenshots/settings-production.png`

The capture-only startup instrumentation was removed, its trace was deleted, the app was closed, and the diagnostic-free desktop source rebuilt successfully. Screenshot review found and drove fixes for selected-navigation contrast, selectable Settings/About states, application-row truncation, and unambiguous activity timestamps.

## Performance result

The Release performance harness reported:

| Workload | Median | p95 | Boundary |
|---|---:|---:|---|
| 300-record deterministic product fixture | 1.78 ms | 2.13 ms | Application fixture only |
| Two-source online-check fixture | 15.56 ms | 16.16 ms | Fixture connectors; no live source contacted |
| 2,500-record load/search/filter/sort | 1.27 ms | 4.68 ms | Application presentation workload only |

The large-list path is structurally virtualized. These figures are not WinUI frame timing, idle-resource, live-source, or final update-responsiveness evidence. An interactive desktop trace is still required for those claims.

## Accessibility evidence

Validated in this task:

- text and glyph accompany semantic status colour;
- production application rows expose a composed accessible summary;
- primary buttons and the operation progress bar have explicit names;
- compact navigation items have explicit automation names;
- Light, Dark, and High Contrast semantic resources are present;
- default control heights, focus brushes, wrapping, and technical-value truncation are encoded in shared styles;
- UI Automation observed the real named production controls.

Not run and therefore not claimed:

- complete keyboard traversal and focus restoration;
- Narrator journey testing;
- Accessibility Insights FastPass;
- 150%/200% text-scale visual matrix;
- live Light/High Contrast visual inspection;
- reduced-motion and Magnifier checks.

Under ADR-021 these criteria are Optional. The product owner has placed the remaining manual matrix out of scope for owner-only use. Accessibility acceptance is therefore not claimed, but it is not an implementation or automated-test blocker.

## Authority and test boundary

Task 35 changed no persisted schema, update authority, elevation route, cleanup scope, reset contract, or source consent. All host interaction used read-only application launch/inspection. The existing local data was displayed but not modified. No package was installed, updated, removed, uploaded, published, or shared.
