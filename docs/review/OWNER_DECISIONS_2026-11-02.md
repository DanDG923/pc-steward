# Owner decisions and evidence waiting (2026-11-02)

Status after the overnight run. Nothing has been merged, released or run on the owner's PC.

## Task 73: Desktop UI and UX overhaul (draft PR DanDG923/pc-steward#13, stacked on #12)

All five phases are implemented, each independently reviewed with its fixes applied. The final integrated review returned PASS with fixes, and those fixes are applied. **Windows CI is green on `a5b8d1c`**, including the Desktop build: architecture 122, integration 1012, Security 1148, domain 167.

Decisions for the owner:
1. **Accept the recorded deviations.** They are listed in the [plan](TASK_73_UI_UX_OVERHAUL_PLAN.md#recorded-deviations) and in the [ADR-030](../adr/ADR-030-DESKTOP-UI-UX-OVERHAUL-DIRECTION.md) notes:
   - a persistent page host instead of Frame/Page;
   - one app per update job;
   - no Skip this version;
   - a read-only activity reader;
   - Home "Check for updates" also refreshes already-enabled sources;
   - an explicit Save schedule;
   - F5 and Ctrl+F shortcuts.
2. **Optional additions that need domain work:**
   - Skip this version;
   - update-all for ready apps;
   - Activity search;
   - confirmation-free pin and ignore on app details (now confirmed everywhere).

   Say which, if any, to scope.
3. **Follow-ups to schedule:**
   - split `MainWindow.cs` (≈2,800 lines), with no behaviour change;
   - extend the diagnostic sanitizer to Windows paths containing spaces outside the profile folder.
4. **Owner-host evidence before release.** It can only be gathered on a real Windows desktop:
   - Narrator: headings, single announcements;
   - keyboard: F6, Ctrl+F, F5 blocked during work, access-key tips;
   - Mica and theme, all high-contrast themes;
   - 100–200% display scaling and 225% text;
   - UAC: confirm gives one prompt; refusing gives Not started and a return to Review;
   - Save schedule refusal and rollback;
   - the clipboard after closing the app.
5. **Merge order:** PR #13 merges into the Task 50 branch, so merge #12 first or together. Merging needs explicit owner approval.

## Task 50: Chocolatey runtime-limits route (draft PR DanDG923/pc-steward#12)

The code work is complete and Windows CI is green (`96cf2db`, which includes the CI test-fixture hardening). The route stays **non-admitted** until the owner provides:
1. **Chocolatey version:** align the host to 2.7.5, or approve a reviewed re-pin;
2. **host evidence on the eligible install:** O5, A-Q1 and A2-L3, a target package, and AQ1UAC/runas;
3. the **ADR-025 exact-artifact test** of the signed helper;
4. an **approved v2 redeploy**;
5. a **decision on the Profile V2 proxy and client-certificate disclosure**;
6. **explicit approval** to change `HasSupportedMode` and the non-admission pins.
