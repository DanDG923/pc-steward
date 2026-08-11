# Task 41 — Codex Security findings catalogue and remediation

Date: 2026-08-11

Repository evidence: `61ff38c6d39339a07005629aa057b68d4eba13f3` (`phase/4-beta`)
External action: none. This task does not close findings or publish changes.

## Triage result

All 21 supplied findings were assessed individually against the current checkout and PC Steward's safety contracts. Scanner severity is retained as source metadata; the remediation rank below is based on current exploitability and product impact.

- 17 `confirmed` security or safety-boundary defects.
- 2 `not_actionable` against the current code (`F12`, `F21`).
- 2 `not_actionable` as security findings but retained as confirmed product defects (`F13`, `F15`).
- Repository-specific `SECURITY.md`: absent. Governing policy was taken from `AGENTS.md`, product requirements, ADRs, and protected-behaviour documentation.

## Remediation outcome

- All 17 confirmed security/safety defects and both retained product defects are remediated in the current working tree; `F12` and `F21` required no production change.
- Focused regression evidence passes 213/213: Application integration 105/105, Domain 59/59, Architecture 27/27, and Security 22/22.
- Complete normal-host evidence passes 288/288: Application integration 180/180, Domain 59/59, Architecture 27/27, and Security 22/22.
- The canonical Release build passes with zero warnings and zero errors. Native handle-bound reset/cache tests require the normal Windows host; the managed sandbox reproduces the known `NTSTATUS=C0000022` restriction.
- No live application, updater, installer, schedule, data clear/reset, deployment, GitHub upload, or finding-state change was invoked.

## Confirmed remediation queue

| Rank | Ref | Source severity | Finding | Current-code evidence | Remediation and regression proof |
|---:|---|---|---|---|---|
| 1 | F4 | Medium | [Test reset seam can delete data outside app state root](https://chatgpt.com/codex/cloud/security/findings/7acc1b5413c88191b032485907464dbb?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Shipped unsigned friend assembly can call test-root reset methods with an arbitrary root. | Restrict the seam to direct, canonical, PC Steward-prefixed temporary roots and reject reparse traversal; prove arbitrary roots are rejected. |
| 2 | F1 | Medium | [Bulk identity confirmation can override source conflicts](https://chatgpt.com/codex/cloud/security/findings/0d5e0262d6b88191bdf6d38d4e1a9de5?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Bulk confirmation checks equal versions but not conflicting source observations, then persists authority. | Reassess every item at the application boundary and reject source disagreement or unsafe review reasons before persistence. |
| 3 | F2 | Medium | [WinGet fallback uses unscoped global source query](https://chatgpt.com/codex/cloud/security/findings/486086670c0081919c08d8949522c0dc?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | A source-scoped failure falls back to an unscoped `winget list`. | Remove the global fallback; report the source unavailable and preserve last-good cache. |
| 4 | F3 | Medium | [WinGet resolver no longer rejects WindowsApps root reparse](https://chatgpt.com/codex/cloud/security/findings/533a4db9daec8191aef4d0034205b4ab?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Package and executable paths are checked, but the trusted root itself is not. | Reject a reparse-point WindowsApps root before enumeration. |
| 5 | F16 | Informational | [Unknown compatibility metadata can authorize updates](https://chatgpt.com/codex/cloud/security/findings/e742c6ad5fd8819197640b014c421b2a?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Missing scope, architecture, or channel is not treated as a blocking review reason. | Add explicit missing-compatibility review reasons and fail closed before actionability. |
| 6 | F20 | Informational | [SQLite promotion does not enforce foreign keys](https://chatgpt.com/codex/cloud/security/findings/728cf3dd10648191894ac25801e27b12?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Foreign keys are enabled only on the migration connection, not all connections. | Enable foreign keys in the shared connection string and prove failed promotion rolls back. |
| 7 | F7 | Medium | [Mutable SQLite state can crash desktop on startup](https://chatgpt.com/codex/cloud/security/findings/90930952104081918e1e58ca33a6abf7?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Startup snapshot readers use throwing `Guid.Parse`/`Enum.Parse`/timestamp parsing on mutable rows. | Use bounded `TryParse` hydration, skip malformed rows, and surface safe empty/partial state instead of crashing. |
| 8 | F14 | Informational | [Onboarding verification can lock users out after reset](https://chatgpt.com/codex/cloud/security/findings/79fba5fd91d88191973ee7569e4cf54a?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | A resumed onboarding launch rejects the fresh SQLite leaves created by the first launch. | Accept only the reset marker plus verified fresh-store leaves, then revalidate the database before showing onboarding. |
| 9 | F19 | Informational | [Malformed HKCU uninstall values can crash inventory scans](https://chatgpt.com/codex/cloud/security/findings/534d2bc49b7881919483b6f6281dc920?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | I/O and security exceptions from uninstall value enumeration escape the provider. | Isolate each registry entry, skip it with a bounded warning, and continue the view. |
| 10 | F10 | Low | [Malformed WinGet output can replace update cache](https://chatgpt.com/codex/cloud/security/findings/38aa55bed71c8191b613467bbb098b45?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Malformed rows are warnings but the resulting partial/empty snapshot is promoted as live. | Treat malformed structured output as refresh failure and retain last-good cache. |
| 11 | F8 | Low | [Unified WinGet toggle can hide active source consent state](https://chatgpt.com/codex/cloud/security/findings/aae77ec8fc188191a0c07b4f6a5de0b4?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Two connector states are saved sequentially, so partial failure can misrepresent source state. | Persist the paired setting atomically and update the UI only from the committed pair. |
| 12 | F9 | Low | [Scope-only source disagreement remains actionable](https://chatgpt.com/codex/cloud/security/findings/4fab7e4bb7808191ba59f68582415856?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Source disagreement compares version, publisher, architecture, and channel but omits scope. | Include normalized scope in disagreement detection. |
| 13 | F5 | Medium | [Scheduled scans ignore disabled AppX inventory connector](https://chatgpt.com/codex/cloud/security/findings/d3be3681b79481918325c9af30f89865?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Scheduled host always composes the MSIX provider. | Load the AppX connector setting and pass it as the provider's fail-closed enablement gate. |
| 14 | F6 | Medium | [CSV exports allow spreadsheet formula injection](https://chatgpt.com/codex/cloud/security/findings/0de071f5893c8191b4bcca1cd4a29ae5?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | CSV escaping quotes fields but does not neutralize spreadsheet formulas. | Prefix formula-capable cells before RFC-style CSV quoting; prove dangerous leading characters are inert. |
| 15 | F17 | Informational | [Oversized prerelease number can crash version comparison](https://chatgpt.com/codex/cloud/security/findings/ef6bc6083a948191b5ad19e41c7d5b47?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Unbounded prerelease digits flow into `uint.Parse`. | Replace throwing parse with bounded `TryParse` and return incomparable. |
| 16 | F18 | Informational | [Ambiguous fingerprint encoding can merge distinct installs](https://chatgpt.com/codex/cloud/security/findings/094dd23ce8a08191b9a08adcc0a5a52c?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Delimiter-joined fields admit tuple collisions and now feed live deduplication. | Use domain-separated length-prefixed hashing only when a legacy delimiter is present, preserving existing safe identifiers. |
| 17 | F11 | Low | [Environment-gated evidence provider ships in desktop scans](https://chatgpt.com/codex/cloud/security/findings/7ed81215d2748191ba00e7505bc93115?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Production composition reads an environment variable and injects a delay provider. | Remove the test provider from desktop composition and add an architecture guard. |

## Product-defect queue

These two reports reproduce, but their demonstrated impact does not cross a security boundary. They are still remediated in this task because they break promised behavior.

| Rank | Ref | Source severity | Finding | Disposition and remediation |
|---:|---|---|---|---|
| 1 | F13 | Informational | [Chocolatey helper requests are still rejected](https://chatgpt.com/codex/cloud/security/findings/3a79878481208191bf13ac6317742a5a?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | `not_actionable` as a vulnerability: rejection is fail-closed. Fix provider/source pair validation so approved Chocolatey Community machine jobs can execute without broadening authority. |
| 2 | F15 | Informational | [Nested orphan cache directories are deleted in wrong order](https://chatgpt.com/codex/cloud/security/findings/a0de9f2d99bc8191b05dd28bf2f7c47e?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | `not_actionable` as a vulnerability: scope remains app-owned. Delete orphan directories deepest-first after files. |

## Not-actionable current-code queue

| Rank | Ref | Source severity | Finding | Current evidence |
|---:|---|---|---|---|
| 1 | F12 | Low | [WinGet inventory scan may contact sources in local-only mode](https://chatgpt.com/codex/cloud/security/findings/15c39fd2f3ec819194010d60f452d002?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward) | Current provider returns before invoking WinGet when disabled; desktop and scheduled composition both gate it from persisted consent state. Retain regression coverage. |
| 2 | F21 | Informational | [Solution references missing SQLite project](https://chatgpt.com/codex/cloud/security/findings/2b153dffe64c8191a3ad17a78959c0d4?sev=&repo=https%3A%2F%2Fgithub.com%2FDanDG923%2Fpc-steward&page=2) | `src/PCSteward.Persistence.Sqlite/PCSteward.Persistence.Sqlite.csproj` exists and the current solution builds. No code change. |

## Completion gate

Each remediation must have a hostile or failure-path regression test, focused suites must pass, Release must build with zero warnings and errors, the full suite must pass, and a change-aware security review must find no newly introduced authority path. Live application updates, installation, publication, GitHub finding closure, and deployment are outside this task.
