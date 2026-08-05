# Task 37 Task 34 visual and live-source validation

**Date:** 2026-08-05
**Branch:** `phase/4-beta`
**Requirements:** `FR-SRC-01–05/07/10`, `FR-UI-03–04`, `FR-UPD-04`, `AC-UPD-03–04`, `AC-UX-03`, `TM-04`
**Outcome:** PASS for the protected Task 34 visible card and the single authorized source refresh. WinGet and Chocolatey Community returned live results; Microsoft Store failed independently and retained visibly stale cached evidence.

## Exact authority and artifact

The owner authorized launching the current protected owner-only installation for Task 34 visual verification and one live online source refresh. Installation or update of applications, schedule changes, clearing data, and factory reset were expressly excluded. No broader host or release authority was inferred.

The read-only protected-install validator passed before launch and reported that it did not launch the application. The tested desktop was the validly signed `%ProgramFiles%\PC Steward\PCSteward.Desktop.exe` with SHA-256 `8AE4E849D276374AE024A54D5DA43FC21DA05EF3064E2430F71099F7E0DDC8D0`. This installed Task 34-era protected package predates the un-deployed Task 35 working-tree redesign; no reinstall or deployment was authorized or performed.

## Visible Task 34 evidence

The protected application was launched normally and navigated through its real Updates and Vortex detail views. The following controls were simultaneously visible and on-screen:

- `Administrator approval`
- `Administrator requirement unknown`
- `PC Steward will confirm the installation scope during fresh update review. No update or administrator prompt starts from this page.`
- `Review and update this verified package. Administrator requirement unknown`

The real protected-window capture is [task34-administrator-card-protected.png](screenshots/task34-administrator-card-protected.png), SHA-256 `332B10DDC8D7FEF0F4D36269FB081061A12DAB8CEC3B03DB24C83B023E2E41C8`. This closes the pending protected-host visible-card evidence. It does not claim that the owner personally inspected the pixels.

The `Review and update` control was deliberately not invoked. That path performs another fresh source assessment and local scan before showing the definitive provider/scope wording, while the owner authorized one online refresh and prohibited an update. The definitive WinGet/User, WinGet/Machine, Chocolatey/Machine, and rejected Chocolatey/User scenarios therefore remain supported by Task 34's existing automated evidence rather than a second live action.

## Single live refresh

The already-enabled `Refresh connected repositories` control was invoked exactly once at `2026-08-05T18:19:31.6167713Z`. No source-consent or settings control was changed.

| Source | Before | After | Result |
|---|---|---|---|
| Chocolatey Community | Last success `2026-08-05T16:03:50.7487704Z`; 0 packages | Last success `2026-08-05T18:19:34.4439669Z`; 0 packages | Live |
| Microsoft Store | Last success `2026-08-05T16:03:50.9210826Z`; 1 package | Timestamp and cached package retained | Unavailable and visibly stale; failed closed independently |
| WinGet | Last success `2026-08-05T16:03:52.8170091Z`; 60 packages | Last success `2026-08-05T18:19:36.6482819Z`; 60 packages | Live |

The UI reported Chocolatey Community and WinGet as live at 19:19 local time and labelled Microsoft Store `source:unavailable source:stale`. The structured host record is [task-37-protected-refresh.json](raw/task-37-protected-refresh.json).

## Prohibited-action checks

| Check | Before | After | Result |
|---|---:|---:|---|
| Update-history rows | 6 | 6 | Unchanged |
| Approved update jobs | 2 | 2 | Unchanged |
| Update execution-state rows | 2 | 2 | Unchanged |
| Scan runs | 21 | 21 | Unchanged |
| `PCSteward.ScheduledCheck` | Absent | Absent | Unchanged |

The only observed host database change was the successful source-snapshot/cache transaction; its final write time was `2026-08-05T18:19:36Z` and did not advance during screenshot capture or shutdown. No helper, installer, or provider process was observed after refresh. PC Steward was closed normally at `2026-08-05T18:23:37.1337876Z`; the desktop exited, no `PCSteward*`, `winget`, `choco`, or `msiexec` process remained, and the exact scheduled task was still absent.

No application was installed or updated, no update review was confirmed, no scheduled setting was changed, no scan was started, and no clear-data or reset control was opened.
