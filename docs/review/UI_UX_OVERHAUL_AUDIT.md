# PC Steward Desktop: UI/UX audit (read-only)

> Planned follow-up to Task 50 (owner request, 2026-11-01). Read-only audit; no code was changed. See [the five concepts](UI_UX_OVERHAUL_CONCEPTS.md).

Audited 2026-11-01 against the working tree at `/home/user/pc-steward` (clean tree, Task 50 head). Abbreviations used below:
- `MW` = `src/PCSteward.Desktop/MainWindow.cs` (1,593 lines)
- `SP` = `src/PCSteward.Desktop/StewardPresentation.cs`
- `RRS` = `src/PCSteward.Desktop/ReviewedRouteSurfaces.cs`
- `APP` = `src/PCSteward.Desktop/App.xaml.cs`
- `IVM` = `src/PCSteward.Application/InventoryExperienceViewModel.cs`
- `RVM` = `src/PCSteward.Application/ReviewedRouteViewModels.cs`
- `SPEC` = `docs/product/PC_STEWARD_PRODUCT_SPEC.md`

Facts are stated plainly with file:line references. Opinions are marked **Assessment**.

Context: `docs/STATUS.md:3` lists the overhaul as a planned, separately scoped task. An earlier redesign, "Steward Ledger" (Task 35, `docs/SELECTED_UI_DIRECTION.md`), produced the current shell. `docs/UI_CURRENT_STATE_AUDIT.md` predates Task 35 and is stale (it describes 11 navigation entries and a 1,075-line MainWindow).

---

## 1. Information architecture

### 1.1 How the UI is built
- Everything is built procedurally in C#. `Render()` (MW:97-115) switches on a string `destination` and rebuilds the whole window `Content`, including a new `NavigationView`, every time (`BuildShell`, MW:806-911). There is no `Frame`/`Page` navigation, no back stack, no MVVM bindings except the inventory row template (`Resources/DataTemplates.xaml`).
- The window opens at a fixed 1280×820 (MW:69). No minimum size is enforced. The title is "PC Steward" (MW:67), with the standard Windows title bar (no `ExtendsContentIntoTitleBar`).
- Separate top-level windows come from `APP`: `DataOperationBusyWindow` (APP:139), `ResetRecoveryWindow` (APP:157) and `StartupFailureWindow` (APP:176). They are unstyled StackPanels with no shell.

### 1.2 Sitemap
```
App launch (APP:22-117)
├─ StartupFailureWindow ......... startup exception (APP:30,115)
├─ DataOperationBusyWindow ...... another PC Steward data operation holds the gate (APP:35)
├─ ResetRecoveryWindow .......... reset incomplete / invalid marker / fresh store unverified (APP:51,59,67,80)
└─ MainWindow
   ├─ [no shell] Startup state-load failure (MW:117-123)
   ├─ [no shell] Post-reset onboarding "Start fresh" (MW:160-204) → Scan
   └─ Shell = header + operation band + NavigationView (MW:806-911)
      ├─ NAV Overview (overview) ............. MW:227-304
      │   ├─ ctx Inventory scan (scan) ....... MW:929-959   [highlighted under Overview]
      │   └─ ctx Inventory results (results) . MW:441-527   [after a successful scan only, MW:1150]
      ├─ NAV Applications (applications) ..... MW:306-367
      │   ├─ ctx Application detail (detail) . MW:1274-1457 [from any list; has Back]
      │   │   ├─ dlg Review approved update .. MW:1542-1551 → UAC → outcome dialog
      │   │   └─ dlg Chocolatey reviewed update (unavailable) RRS:251-279
      │   └─ ctx Repository apps (current) ... MW:369-430   [NO entry point; see P-H2]
      ├─ NAV Updates (updates) ............... MW:961-1017
      │   └─ ctx Review actions (review) ..... MW:529-574   [reached only from Results, MW:525]
      ├─ NAV Activity (activity) ............. MW:740-793 (+ reviewed outcomes RRS:282-297)
      ├─ FOOTER About & support (about) ...... MW:206-225
      └─ FOOTER Settings (settings) .......... MW:1019-1084
          ├─ ctx Storage (storage) ........... MW:725-738   [also from Results when downloads are recoverable]
          └─ ctx Privacy & data (privacy) .... MW:795-802 + AddDataAndResetSection MW:1172-1196
                                               [also from Storage and Activity]
Fallback: RenderPlaceholder (MW:920-927) "This destination preserves the approved navigation structure."
```
- The navigation has 4 main items and 2 footer items (MW:891-896). The built-in Settings item is hidden (`IsSettingsVisible = false`, MW:882). The pane is Auto: compact below 900 px and expanded above 1100 px (MW:880-886).
- Each contextual page is mapped to a parent item for highlighting (MW:897-903). No contextual page except Detail has a Back control, a breadcrumb, or a page-level way up. Navigation reacts only to `SelectionChanged` (MW:905-909). There is no `ItemInvoked` handler.

### 1.3 Dialog inventory (all `ContentDialog`s, created inline)
| Dialog | Where | Primary / Close | Default |
|---|---|---|---|
| Action unavailable (data-gate busy) | MW:94 | Close only | — |
| Factory reset still needs attention / Fresh state could not be loaded | MW:188,195 | Close | — |
| Confirm package identity? | MW:628-636 | Confirm identity / Cancel | Close |
| Pin current version? | MW:654-662 | Pin version / Cancel | Close |
| Ignore this product? (indefinite or date) | MW:684-692 | Ignore product / Cancel | Close |
| Confirm current package identities? (bulk) | MW:707-715 | Confirm mappings / Cancel | Close |
| Enable WinGet and Chocolatey trusted sources? | MW:1095-1103 | I approve / Keep local-only | Close |
| Enable {connector}? (generic) | MW:1132 | I approve / Keep local-only | **not set** |
| Schedule was not changed | MW:1062 | Close | — |
| {Advanced clear}? + result | MW:1203,1209 | label / Cancel | Close |
| Clear scan data? + result | MW:1255,1260 | Clear scan data / Cancel | Close |
| Clear downloaded files? / Delete downloaded files after update completion? + result | MW:1265,1271 | Delete downloaded files / Cancel | Close |
| Review factory reset scope → This cannot be undone (type RESET) | MW:1216,1219 | Continue… / Factory reset PC Steward | Close |
| Review approved update (final WinGet confirmation) | MW:1542-1550 | Install approved update [anyway] / Cancel | Close |
| Update outcome (title from status) | MW:1573-1592 | Close | — |
| Chocolatey reviewed update (unavailable) | RRS:266-275 | Confirm update (disabled) / Close | Close |
| Update not started (after Chocolatey dialog) | MW:1472 | Close | — |

---

## 2. Core flows as implemented

1. **First run (no data).** Overview shows the empty state "See what is installed" with **Start local inventory** and the banner "Private by default" (MW:243-251). The button sets `destination="scan"` and **starts the scan immediately** (MW:247). The scan-scope card and the source toggle (MW:941-950) are not shown before this first scan. Sources stay off by default.
2. **Post-reset first run.** The onboarding page (MW:160-204) verifies the fresh state, then opens Scan *without* starting it (MW:200). This is the only path where the scope card is seen before a scan.
3. **Scan.** The operation band (MW:840-868) shows the phase, elapsed time, an indeterminate bar and **Cancel scan**. The Scan page shows the phase and elapsed time and a second Cancel with `AccessKey="C"` (MW:955). Progress updates only the text (`RefreshOperationVisuals`, MW:153-158, 1144). Elapsed changes only when a provider reports progress (IVM:96). It is not a ticking clock. On success the app goes to Results (MW:1150). On failure or cancel it stays on Scan, with the phase "Scan did not complete"/"Scan cancelled" (IVM:97) and a warnings banner if any (MW:940).
4. **Inventory list.** Applications has search, five ComboBox filters in a 3-column grid, and a virtualized `ListView` capped at `MaxHeight=620` inside the page ScrollViewer (MW:314-366). Rows show initials, name, publisher, version, scope·provider and a status chip (DataTemplates.xaml). Clicking a row opens Detail (MW:352).
5. **App details.** Detail (MW:1274-1457) shows, in order: a heading with a status badge and Back; "Installed" and "Repository evidence" cards side by side; "Why this appears"; "What PC Steward can do"; an admin banner (VerifiedUpdate only, "Administrator requirement unknown", MW:1333-1338); a Decisions card (reason box, ignore-until, candidate picker, Refresh / Confirm selected identity / Pin / Ignore / Confirm package identity / **Review and update**); a Chocolatey card (Chocolatey rows only); and a "Technical details" Expander (instances, timeline, source candidates, manual-mapping form, raw-record count).
6. **Finding updates.** Sources are off until the user agrees in the consent dialog (MW:1090-1124), from the Scan page or Settings. Refresh is on the Updates header (MW:967), on per-row buttons (MW:611-620) and on Repository apps. Updates lists VerifiedUpdate, ReviewRequired and FailedUnavailable cards, each with a "Review … evidence" button that opens Detail (MW:983-1009).
7. **Review and approve (WinGet).** Detail **Review and update** → `BeginReviewAsync` → the "Review approved update" dialog (MW:1517-1551). The dialog is one plain multi-line string: package, installed version, target version, source, scope, architecture, admin label and explanation, a running-app warning or a no-match note, the process-coverage disclosure (IVM-adjacent `RunningApplicationDetection.cs:20-26`), the verification attestation, and the "not successful until verified" note. The primary button becomes "Install approved update anyway" when the app appears to be running (MW:1547). Cancel is the default.
8. **Elevation/UAC moment.** There is no in-app step for it. The admin label comes from `AdministratorRequirementPresenter.ForSelection` (user scope: "not expected"; machine scope: "required … UAC prompt only after you confirm", `AdministratorRequirementPresentation.cs:22-46`). The OS shows UAC after confirmation. The band label reads "Requesting elevation; provider execution not observed" (`UpdateProgressPresentation.cs:46`).
9. **Progress.** The band text is polled every 250 ms from the mailbox (MW:1485-1515). The bar is always indeterminate (MW:849). Phase labels: queued → revalidating → elevation/helper or process started → verifying → final (`UpdateProgressPresentation.cs:42-66`). An update has no cancel. Tests forbid "Cancel update" (`UpdateProgressUiContractTests.cs:44`).
10. **Verification result.** A dialog only (MW:1569). Titles: Success → "Update completed", RestartRequired → "Restart required", not started → "Update not started", no batch → "Outcome unavailable", **every other status → "Update failed"** (MW:1573-1581). The body is `result.Message`, with one special case (MW:1583-1590). There is no completion view and no "Delete downloaded files" there (see P-H4).
11. **History/Activity.** Activity merges the last scan, active decisions and manual mappings into a timeline of at most 50 rows (MW:748-769). It then lazily loads "Reviewed update outcomes" cards (MW:776-793, RRS:282-297): Outcome, Running installers, Verification, Route, Installer mode, Candidate evidence, Helper report, Disclosure, Other updates and Diagnostic (RVM:129-138). The per-product timeline is plain text inside Detail's Technical details (MW:1448).
12. **Settings.** In order (MW:1019-1084): a source-consent banner; Inventory and update sources (required local checkbox, disabled; AppX checkbox; WinGet+Chocolatey toggle); Schedule & notifications (cadence radios, time, weekday, enable toggle, notifications toggle, **Save schedule**, task status); Update decisions (list with Remove); Storage, privacy & reset (counts plus links to Storage and Privacy & data).
13. **Clearing and reset (Privacy & data, MW:1172-1196).** All controls sit on one long stack:
    - an export warning and Export CSV/JSON;
    - scope text and **Clear scan data**;
    - scope text and **Clear downloaded files**;
    - when bytes are recoverable, text and **Delete downloaded files** (same operation, different title, MW:1181-1185);
    - "Advanced data controls": Clear source metadata, logs, update history, mappings and policies (MW:1189-1192);
    - "Factory reset PC Steward" with "Danger zone — …" text and a default-style button (MW:1193-1195).
    Every action has a confirmation dialog with Cancel as the default, then a result dialog. Factory reset uses a scope dialog, then a dialog that requires the exact text `RESET` (MW:1214-1229). It then locks out active work, arms the reset and **closes the window**. Cleanup is verified at the next launch.
14. **Chocolatey reviewed (unavailable) dialog.** Detail shows a "Chocolatey reviewed update" card only when `SourceId == "chocolatey-community"` (MW:1433-1442, RVM:56). The entry button reads "Chocolatey reviewed update (unavailable)…" (RVM:24). The dialog shows Candidate, Candidate checked, Your user cache, Installer mode, Package scripts and downloads, and Running installers. It then shows "Why this update cannot be confirmed" with the reasons (not admitted, plus the split-token reason probed fresh), and a permanently disabled **Confirm update** (RRS:251-279; text in `ChocolateyReviewDisclosure.cs:104-129`). If the dialog ever returned Primary, the app would show "Update not started" (MW:1469-1472).
15. **Errors and empty states.** Shared `EmptyState` component (SP:165-175). It is used for: no scan, no filter match, no repository apps, nothing to review, no activity and no results. Errors are mostly bare dialogs with Close only, for example "Action unavailable" (MW:94). The startup failure text says "Copy the diagnostic below" but the TextBlock is not selectable (APP:187-188). Reset-recovery residuals are shown as raw `Category: Reason` lines (APP:167).

---

## 3. Visual system as implemented
- **Tokens** (`Resources/ColorTokens.xaml`): 19 semantic brushes (Canvas, Surface, Border, three text levels, Brand, Focus, and Success/Warning/Danger with surfaces) for Default, Light, Dark and HighContrast. HighContrast maps everything to `SystemColorWindow`, `WindowText` and `Highlight` colours. The selected NavigationView item is overridden to BrandSubtle.
- **Typography** (`Typography.xaml`): Segoe UI Variable Display for Display 32/38, PageTitle 24 and SectionTitle 18. Segoe UI Variable Text for BodyLarge 16/24, Body 14/20, Caption 12/16 and Eyebrow 12 (SemiBold, tracking 70, upper-cased in code, SP:94). Sizes are hard-coded, not WinUI type ramp resources.
- **Spacing and shape** (`ThemeResources.xaml`): page padding 24, card padding 20, control padding 16,8, control radius 8, card radius 12. Pages are StackPanels with `Spacing=16`, `MaxWidth` 760–1040 (MW:125-133).
- **Controls** (`ControlStyles.xaml`): Card (Border), Button and Primary (based on `AccentButtonStyle`, so primary buttons use the **Windows accent colour**, not the fixed brand blue), Input/Select at MinHeight 40, StatusChip, ListItem at MinHeight 64.
- **Components** (`StewardVisuals`, SP:83-224): PageHeader (eyebrow, title, description, optional action), PrimaryButton, StatusBadge/Badge (glyph plus label, tone-coloured), InformationBanner (5 tones), EmptyState, MetricCard, EvidenceRow (150 px label column), ActivityRow.
- **Status model** (`StewardStatus`, SP:9-23): seven domain statuses plus "Installed · source not checked". Each has a text label, a Segoe Fluent glyph and a tone.
- **Icons**: Segoe Fluent `FontIcon` glyphs only. Applications get initials avatars, not real app icons (SP:74-80). The brand mark is the 32 px `logo-mark-64.png` in the header (MW:824).
- **Backdrop/theming**: there is **no Mica, Acrylic or `SystemBackdrop`** (grep found nothing). The shell uses an opaque `StewardCanvasBrush` (MW:808). There is no in-app theme switch. The app follows the Windows theme.
- **Density** **(Assessment)**: medium-low. 40 px controls, 64 px rows, 20 px card padding and many stacked cards each holding several paragraphs make pages long. Results, Review and Updates render one full card per item with no virtualization (MW:489-520, 538-571, 983-1009).

---

## 4. Copy and wording inventory
**Tone (fact):** long, careful, reassurance-heavy sentences. Almost every surface repeats "No update starts / nothing changed". Examples: MW:535, 645, 669, 700, 957, 1023, 1330.

**Notable strings:**
- Taglines and eyebrows: "Evidence before action" (MW:241, 829), "Quiet authority" (About eyebrow, MW:209), "Contextual view" (MW:377), "Decision checkpoint" (MW:534). **Assessment:** "Quiet authority" and "Contextual view" are internal design vocabulary that has leaked into the UI.
- Jargon that reaches users: "canonical local inventory" (MW:312), "canonical applications" (MW:750), "Ready · no background coordinator is running" (MW:151), "Bounded raw diagnostic records" (MW:1453), "package-derived exact-name heuristic … saturation" (`RunningApplicationDetection.cs:26`), "split token", "lib-bad", "app-container token" (`ChocolateyReviewDisclosure.cs:112-127`), "helper validation and provider execution indeterminate" (`UpdateProgressPresentation.cs:47`).
- Raw enum or identifier leaks:
  - Applications status filter items are `Enum.GetNames` ("VerifiedUpdate", "ReviewRequired", "FailedUnavailable", MW:316).
  - Source and scope filters list raw values ("registry", "machine"), while rows show "Windows registry" and "All users" (MW:318-319 vs SP:56-72).
  - Results filter automation names: "Show NeedsDecision scan results" (MW:461).
  - Activity and Settings show `decision.Kind`/`Value` raw (MW:752, 1239).
  - The mapping source ComboBox offers "winget"/"msstore" (MW:1444).
  - Weekly day uses `DayOfWeek` enum names, which are not localized (MW:1049).
  - Detail Technical details print raw instance tuples (MW:1447).
- Unit and number formatting: scan data size shown as "{N} characters" (MW:732, 1175). Downloads shown as raw "{N:N0} bytes" (MW:523, 733, 1077, 1179). There are no KB/MB/GB conversions.

**Inconsistencies (fact):**
- **Elevation story contradicts itself.** About says "only explicitly approved current-user WinGet updates. Machine-scope updates are not actionable here. PC Steward does not use elevation or an elevated helper in this package" (MW:213). The source consent dialog says "an update is machine-scope only and requires separate review, confirmation, and elevation" (MW:1099). The composition enables `UserAndMachine` with an `ElevatedUpdateRunner` (APP:102-104). The admin presenter says user scope is "not expected" to need admin (`AdministratorRequirementPresentation.cs:27,38-41`). The outcome special case mentions "trusted elevated helper" (MW:1589).
- **"Approved" used before approval.** The Review page heading is "Approved updates" and the copy says "{n} approved action(s)" for VerifiedUpdate rows the user has not approved yet (MW:560-561). The same page's banner says "Review is not approval" (MW:535). The final button is "Install approved update" (MW:1547).
- **Same action, different verbs.** "Verify all current package identities" (Results, MW:472) and "Confirm all current package identities" (Review, MW:555) both call `ConfirmCurrentPackageIdentitiesAsync`. "Clear downloaded files" and "Delete downloaded files" run the same `ClearInactiveAsync` (MW:1180, 1184).
- **Naming drift for statuses.**
  - Badge: "Update ready to review" / "Needs your decision" / "Update evidence unavailable" (SP:14-19).
  - Detail: "Managed · update available" / "Repository match · review required" (MW:1281-1289).
  - Metric cards: "Ready to review" / "Need a decision" / "Inventoried only" (MW:292-295).
  - Results filters: "Actionable / Needs decision / Unavailable / Unmanaged" (MW:453-457).
  - SPEC §7.2 terms such as "Update available — verified" and "Source unavailable" are not used.
- **Toggle label vs effect.** "WinGet and Chocolatey trusted sources" (MW:946, 1035) saves only the two WinGet connectors (MW:1109-1114). The error dialog calls it "the combined WinGet setting" (MW:1121).
- **Wrong pointer.** Updates page: "Open evidence review from Applications before any update can start" (MW:989), although the button on that same card opens it.
- **Release-process text in the product.** About shows "External release is blocked … Do not treat an unsigned archive as a release candidate" (MW:219) and references a "diagnostic export" (MW:222) that the Desktop does not expose (no diagnostics-bundle control in Desktop sources).
- **Tone vs SPEC §17.5.** "Danger zone" (MW:1194) sits close to the "avoid 'dangerous'" guidance. **Assessment:** acceptable for a destructive area, but worth deciding explicitly.

**Mandatory disclosure and risk texts (present):**
- source consent (MW:1099, 948, 1132);
- export warning (MW:1176);
- per-category delete/preserve scopes (MW:1175, 1179, 1189-1192, 1194, 1216, 1255, 1265);
- provider-cache boundary (MW:729, 1179, 1265);
- running-app warning and coverage (MW:1538-1541);
- verification attestation (MW:1535, 1546);
- admin requirement (MW:1337, 1546);
- Chocolatey user-cache, script-runtime and lingering-installer disclosures (`ChocolateyReviewDisclosure.cs:20-24, 104`);
- unmanaged ≠ unsafe (MW:295, 501).

---

## 5. Accessibility as implemented (best effort, ADR-021)
**Present:**
- Explicit `AutomationProperties.Name` on most actions, filters, badges, banners, evidence rows and empty states (SP:110, 142, 161, 173, 202, 222).
- An `AccessibleSummary` on each list row (SP:38, DataTemplates.xaml).
- Help text on refresh and on review-and-update (MW:617, 1425).
- Focus visuals via `FocusVisualPrimaryBrush` (ControlStyles.xaml).
- A HighContrast dictionary.
- Text plus glyph status (AC-UX-03).
- Cancel is the default in every destructive dialog. Factory reset needs `RESET` typed exactly, and Enter goes to Cancel (AC-UX-08).
- Phase, elapsed time and enabled Cancel during a scan (AC-UX-09).
- Focus returns to the invoker when factory reset is cancelled (MW:1217, 1221).

**Gaps (fact):**
- **Focus loss on every `Render()`.** The whole Content, including navigation, is replaced after nearly every action: Results filter buttons (MW:462), decisions, refreshes and clears. Keyboard and screen-reader users are thrown back to the top of the window.
- **No live regions.** No `LiveSetting` exists anywhere (grep). Scan, refresh and update progress text changes silently (MW:153-158). Outcomes are announced only through dialogs.
- **No heading semantics.** `AutomationProperties.HeadingLevel` is never set. Page and section titles are plain TextBlocks.
- **Label vs accessible name mismatch.**
  - Visible "Replace saved inventory"/"Start inventory" is named "Scan this PC" (MW:952-954).
  - Overview primary "Review 3 updates" is named "Review verified update evidence" (MW:255).
  - Primary buttons get a separate automation name in `PrimaryButton` (SP:107-112).
- **Selected state shown as disabled.** The active Results filter is shown with `IsEnabled=false` (MW:460). It is not focusable and is announced as unavailable rather than selected.
- **Unnamed buttons.** No automation names on "Review available actions", the storage link, "View storage", "Privacy & data controls", the export buttons, and Detail's Pin/Ignore (MW:523-525, 736, 1079-1080, 1177, 1399-1400). These fall back to their content, which is acceptable but inconsistent.
- **Final update dialog is one string** with `\n` separators (MW:1546). A screen reader gets one long block. There is no structure to move through.
- **Contrast** (computed WCAG ratios):
  - Tertiary caption `#6B788D` gives 4.13:1 on the canvas `#F3F6FA` and 4.47:1 on white. That is below 4.5:1 for 12 px text. It is used for captions, evidence labels and activity times (Typography.xaml, SP:198, 218).
  - Dark `BorderStrong #68778F` on surface `#171F2D` is 3.64:1.
  - Everything else checked passes (secondary 5.87:1; status foregrounds on their surfaces 6.3–7.7:1).
- **No minimum window size.** Horizontal button rows (MW:254, 450, 579, 1078, 1177, 1374) do not wrap and can clip at narrow widths or at 150–200% text scaling. Filter rows are 5 fixed buttons (MW:450-465).
- **Nested scrolling.** The inventory `ListView` (`MaxHeight=620`) sits inside the page ScrollViewer (MW:340, 870). Mouse wheel and keyboard scrolling can get trapped.
- **Startup failure diagnostic cannot be copied** (APP:188, no `IsTextSelectionEnabled`). The same is true of package IDs and all evidence text across the app.

---

## 6. Constraints any redesign MUST preserve
These come from `docs/PROTECTED_UI_BEHAVIOURS.md` plus the cited SPEC IDs.
1. **Read-only, non-elevated, cancellable scan**, which never implies approval (UJ-01; AC-UX-09; FR-ONB-02; NFR-PERF-08 sets the cancel acknowledgement at ≤2 s).
2. **Source consent stays separate.** It needs the disclosure that package IDs may be shared, and turning it on starts nothing (FR-ONB-01/03/04). Default is local-only.
3. **Distinct stages:** detection → source identification → confidence → user decision → immutable review → explicit confirmation → execution → verification. Selection is not approval and nothing is preselected (FR-UPD-01).
4. **Non-actionable evidence:** low-confidence, conflicting, stale, unavailable, architecture- or channel-conflicting and incomparable candidates must never show an update action (SPEC §7.3; AGENTS.md). FailedUnavailable offers only "refresh source evidence" (MW:1346-1352; pinned by `UiRedesignContractTests.cs:123-128`).
5. **Final confirmation content** (SPEC §17.2, FR-UI-03/04): name, installed and candidate versions, publisher, scope, architecture, **channel**, source, **package ID**, **confidence plus explanation**, running-app state (FR-UPD-05/06: never force-close), likely elevation and restart, and exact consequences. Cancel is the default.
6. **UAC only after final confirmation** of a validated machine-scope job. The desktop stays unelevated (FR-UPD-04; ADR-018).
7. **Truthful outcomes:** Success only after verified re-inventory or a reliable restart-pending result (FR-UPD-08/09). The UI must distinguish success, failure, restart required and **verification inconclusive** (UJ-04 step 8). Progress must stay indeterminate with text unless a defensible percentage exists (§17.4). No "Cancel update" mid-run (contract test).
8. **History fields** (FR-HIS-01/03, FR-UPD-07): record and show start and end times, provider, package ID, target version, exit code and sanitized output, plus the failure class (source, download, installer, elevation, timeout, verification). Reviewed outcomes must keep Route, Verification, Running installers and Helper report (RVM:129-138). Per-product timeline (FR-HIS-02).
9. **Data controls** (FR-DAT-06–15; SPEC §12.5, §17.3; AC-UX-07/08):
    - exact labels **Clear scan data**, **Clear downloaded files** and **Factory reset PC Steward**;
    - separate cards for scan data, downloads, source metadata, logs and history, and factory reset, each showing size, last-cleaned time and delete/preserve scope;
    - factory reset visually separated, with a second confirmation, exact `RESET`, no Enter activation, a lockout during active work, and verification at the next launch;
    - **Delete downloaded files** on the update-completion view;
    - controls disabled during active work;
    - the provider-cache boundary disclosed.
10. **Exports** are user-started and carry the identifying-data warning (FR-DAT-01/03).
11. **Freshness:** live, cached, local-only, offline and unavailable must always differ in text (FR-UI-05; AC-UX-06).
12. **Tone** (§17.5): no health score, fake urgency or "unsafe" label. Unmanaged ≠ unsafe. Status must never rely on colour alone (AC-UX-03).
13. **Decisions** (FR-POL-01–05): pin, ignore (indefinite or until a date), an optional reason (≤512 characters), all reversible, all listed in one view.
14. **Chocolatey route** stays non-admitted. Confirm is bound to `IsConfirmEnabled`, which is false. Disclosures appear before any confirmation. The entry point appears only on Chocolatey Community rows (FR-UPD-15; `ReviewedRouteDesktopUiBoundaryTests.cs`).
15. **Engineering pins the redesign must update deliberately:** `tests/PCSteward.ArchitectureTests/UiRedesignContractTests.cs` asserts literal MainWindow source strings: six `AddNavigationItem` calls, `CompactModeThresholdWidth = 900`, destination string names, `RefreshOperationVisuals` on progress, and specific copy. `UpdateProgressUiContractTests.cs` pins the 250 ms poll and the lack of a "Cancel update". `ReferenceBoundaryTests.cs` has more than 130 assertions over Desktop sources. Any file split or rename breaks these tests by design.
16. **SPEC §17.1** recommends 7 sections (Overview, Applications, Updates, Unmanaged, History, Decisions, Settings with Data & reset). The current five-item navigation is the Task 35 owner choice. Any new IA should be reconciled with §17.1 explicitly.

---

## 7. Pain points and opportunities (ranked)

### High
- **P-H1 Clicking the highlighted parent item does nothing.** Contextual pages highlight their parent (MW:897-904). Navigation listens only to `SelectionChanged` (MW:905-909), which does not fire when the user clicks the item that is already selected. So from Scan, Results or Onboarding, clicking **Overview** does nothing. From Review, **Updates** does nothing. From Repository apps, **Applications** does nothing. Only Detail has Back (MW:1301). *Opportunity:* real Frame navigation with a back stack, `ItemInvoked` handling and breadcrumbs.
- **P-H2 Orphaned and hidden destinations.**
  - Repository apps (`current`, MW:369-430) has no entry point: no code sets `destination = "current"` (grep of MW).
  - Review actions is reachable only from Results (MW:525).
  - Results appears only right after a successful scan (MW:1150).
  - The scan-scope card is skipped on normal first run because Start scans immediately (MW:247, 260, 311).
- **P-H3 Verification outcomes are flattened.** Inconclusive, VerificationRequired, Cancelled, ElevationDenied, TimedOut and similar all get the title "Update failed" (MW:1573-1581). The progress band can say "Installed outcome uncertain; verification required" (`UpdateProgressPresentation.cs:63`) while the dialog says "Update failed". This conflicts with UJ-04 step 8 and the FR-HIS-03 distinction. A UAC refusal reads as a failure.
- **P-H4 Requirements visible in the spec but missing in the UI** (gaps the redesign should close, not new scope):
  - FR-DAT-09 / UJ-04 step 9: no completion view; "Delete downloaded files" lives only on Privacy & data (MW:1181-1185).
  - §17.2 / FR-UI-04: confidence level, channel and review reasons are computed (`UpdateDetail.Confidence/Channel/ReviewReasons`, IVM:16) but never rendered (grep of MW).
  - FR-UI-01: Overview counts only verified, review, current and unmanaged (MW:229-232). Pinned, ignored and failed are missing.
  - §17.3: Data & reset is one stack of TextBlocks, not separate cards, and factory reset uses a default-style button (MW:1172-1196).
  - FR-HIS-05: no "copy diagnostic bundle" in Desktop.
  - FR-UI-06: no compact attention view.
- **P-H5 The elevation and scope story contradicts itself** across About, the consent dialog, the admin presenter and the composition (see §4). For a safety-first product this damages trust most at exactly the point where trust matters (UAC).
- **P-H6 The final update confirmation is a wall of text** (MW:1546). Package facts, admin requirement, running-app warning, the 4,096-entry coverage heuristic paragraph and the attestation all sit in one string. The key comparison (installed → target) has no visual hierarchy, and the risk text competes with telemetry-like counts.
- **P-H7 Every action rebuilds the whole window** (MW:97-115, 806-911). Focus and scroll position are lost, there is visible churn, and Activity reloads history on each visit (MW:784-792). This underlies the a11y focus gap and makes incremental polish expensive.

### Medium
- **P-M1 Inconsistent confirmation semantics for the same decision.**
  - Pin, Ignore and Confirm identity from lists open dialogs (MW:622-701).
  - The same actions on Detail run **immediately**, with no dialog and no success feedback beyond a re-render (MW:1399-1400, 1405-1419).
  - Detail's Ignore reads the ignore-until controls from a separate area of the card.
- **P-M2 Status vocabulary drift and raw enums** (§4). Five different names for the same states, and raw enum names in the status filter, which does not match the chip labels the user sees.
- **P-M3 "Approved" wording before approval** (MW:560-561, 1547), and duplicated verbs (Verify/Confirm all; Clear/Delete downloaded files).
- **P-M4 Overview repeats itself.**
  - The hero ("Updates wait for you"), the PageHeader description, the source banner and four metric cards all restate the same counts (MW:234-302).
  - Metric cards are not clickable, so they do not filter or navigate.
  - Users get from Overview to Updates or Review by different routes depending on counts (MW:257).
- **P-M5 Item lists are not virtualized and are verbose.** Results, Review, Updates and Repository apps build one large card per item, each with 3–6 paragraphs and 2–5 buttons (MW:489-520, 538-571, 983-1009, 408-427). This gets slow and hard to scan at 300+ apps. Applications is the only virtualized list.
- **P-M6 Units and formatting.** Raw bytes, "characters" as a size unit, `DayOfWeek` enums, inconsistent date formats ("dddd, d MMMM yyyy 'at' HH:mm" in IVM:229 vs "d MMM yyyy · HH:mm" in SP:218 vs `ToString("g")` in MW:1066), and the UTC ISO timestamp in the Chocolatey dialog (`ChocolateyReviewDisclosure.cs:149`).
- **P-M7 Settings save model is mixed.** Source and AppX toggles save immediately (MW:1032-1037). The schedule needs **Save schedule** (MW:1054). "Manual only" plus a separate "Enable scheduled checks" toggle is redundant (MW:1044-1052). Time and weekday stay editable when Manual is chosen.
- **P-M8 Scan progress feels frozen.** Elapsed time moves only on provider events (IVM:96). After completion, "elapsed" is the sum of provider timings, not wall-clock time (IVM:97). A failed scan shows no failure view or next action (AC-UX-05).
- **P-M9 Contrast and scaling.** Tertiary text is just under 4.5:1. Horizontal action rows do not wrap. There is no minimum window size. The filter grid is fixed at 3 columns.
- **P-M10 Developer and design jargon** in user copy ("Quiet authority", "canonical", "coordinator", "bounded raw diagnostic records", "split token", "lib-bad"), and release-process warnings on the About page.

### Low
- **P-L1 No Mica or Acrylic, and a stock title bar.** The opaque canvas looks less native on Windows 11. Primary buttons follow the system accent while the brand tokens are a fixed blue, so the two can clash.
- **P-L2 Initials avatars instead of app icons.** Rows look alike and are hard to tell apart.
- **P-L3 The header "Ready / Work in progress" text and the "Stored locally" badge are static per render** (MW:833-834). The text is not refreshed by `RefreshOperationVisuals`.
- **P-L4 The generic connector consent dialog has no `DefaultButton`** (MW:1132), unlike all other consent dialogs.
- **P-L5 Error, busy and recovery windows are unstyled.** They have no next-action button (APP:139-194), and the "copy the diagnostic" text cannot be selected.
- **P-L6 Technical details are a text dump.** Instances, timeline and candidates are newline-joined strings (MW:1447-1449). The per-product timeline (FR-HIS-02) is buried in an Expander.
- **P-L7 Results filter count "All N" excludes Current, Pinned and Ignored rows** (MW:453), which makes "All" misleading.

### Opportunities that any of the five concepts could use **(Assessment)**
1. Real page navigation (Frame, back stack, `ItemInvoked`), so every contextual page is reachable and leavable.
2. A single status vocabulary that maps SPEC §7.2 labels to one chip component, with filters showing the same labels.
3. A structured "Update review" sheet (facts table → risk callouts → attestation → one primary action) and a dedicated completion view: outcome state, verification evidence, restart guidance, and Delete downloaded files with the recoverable size.
4. A Data & reset page built as five cards, with human-readable sizes and a separated destructive zone.
5. View-model-bound, virtualized lists for every collection, with incremental updates instead of full `Render()`.
6. Headings, live regions, label-in-name and text selection for evidence fields, at low cost under ADR-021 best effort.
7. Mica plus a custom title bar, an icon pipeline and density options; all are presentation-only.
