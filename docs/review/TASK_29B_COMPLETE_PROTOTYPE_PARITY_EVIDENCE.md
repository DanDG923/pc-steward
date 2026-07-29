# Task 29B — Complete supported native-prototype parity evidence

**Status:** Complete — all production-supported prototype surfaces are present and use real local product state.

## Scope

The native prototype remains the visual and interaction reference. At the product owner's direction, its non-existing test/concept UI was excluded: the prototype-state selector and `Startup · future` are not in production. No scan, source refresh, update, deletion, export, schedule change, or connector change was invoked during review.

## Delivered parity

| Prototype surface | Production equivalent |
|---|---|
| Overview, hero, metrics | Existing evidence-first Overview with real counts, freshness, source evidence, and explicit no-automatic-change state. |
| Scan | Local read-only scope plus a single WinGet trusted-sources toggle; starting a scan remains explicit. |
| Scan results and Review actions | New navigable, real-data evidence/review surfaces; each item opens its existing individual evidence and confirmation flow. No synthetic queue or batch execution was introduced. |
| Applications and Updates | Prototype-style inventory columns (application/version/status/scope/source), publisher-aware search, and evidence cards with package/version/source details. |
| Storage, Activity, Privacy & data | App-owned storage summary and existing local records/data controls; no provider cache or machine-wide cleanup authority was added. |
| Settings | One `WinGet trusted sources` toggle replaces the two visible WinGet toggles. A single disclosure confirmation synchronizes the existing installed-inventory and update-source connectors. |

## Verification

- Release build: `pwsh -NoProfile -File .\scripts\Build.ps1` — passed, 0 warnings, 0 errors.
- Full suite: `pwsh -NoProfile -File .\scripts\Test.ps1` — passed, 192 tests.
- Release UI Automation review on 2026-07-29 confirmed prototype-aligned navigation, the real-inventory Apps columns, and exactly one named `Enable WinGet trusted sources` control in Settings. The desktop closed normally afterward.

## Boundaries

The combined WinGet toggle still requires explicit source-disclosure approval and does not itself scan, refresh, download, or install. Existing underlying connectors and all update/elevation/deletion/scheduling controls retain their established safety behavior. `PO-EXC-29-PERF-01` and the incomplete ADR-021 accessibility matrices remain unchanged.
