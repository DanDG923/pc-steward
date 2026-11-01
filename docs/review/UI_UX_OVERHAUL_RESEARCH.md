# PC Steward — external UI/UX research for the overhaul

> Planned follow-up to Task 50 (owner request, 2026-11-01). Claims marked "(S)" come from search-result summaries and must be re-checked before they are quoted. See [the five concepts](UI_UX_OVERHAUL_CONCEPTS.md).

Date: 2026-11-01. Scope: research only, so no repository files changed. AI must not make trust or execution decisions (AGENTS.md).

## 0. Method, source access and confidence

- **Network limits.** This session's egress allowlist blocked `learn.microsoft.com`, `nngroup.com`, `fluent2.microsoft.design`, `w3.org`, `support.*` and vendor sites. Only GitHub hosts could be reached.
- **Primary sources (high confidence).** These were read in full:
  - Microsoft's own Markdown source for the Learn pages: `github.com/MicrosoftDocs/windows-dev-docs` (branch `docs`) and the Win32 UX guide in `github.com/MicrosoftDocs/win32`.
  - W3C's WCAG source (`github.com/w3c/wcag`).
  - GitHub Primer design docs (`github.com/primer/design`).
  - Project READMEs: UniGetUI, GNOME Software, KDE Discover, WinUI Gallery, Chocolatey GUI.
- **Search-derived (medium confidence, marked "(S)").** Claims about products whose sites could not be fetched, such as Windows Update, the Microsoft Store, Patch My PC, SUMo, macOS App Store, Discover offline updates and NN/g articles, come from search-result summaries. Verify them before quoting them as fact.
- **Short source keys.** These are used below.
  - `WDD:` = `https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/`
  - Learn mirror = `https://learn.microsoft.com/windows/apps/` + same path, minus `.md`.
  - `UXG:` = `https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/uxguide/`

---

## 1. Fluent 2 / Windows 11 guidance for WinUI 3

**Design principles.** Windows 11's principles are *Effortless, Calm, Personal, Familiar, Complete + Coherent*. "Calm" means decluttered and fading into the background. That fits a trust utility better than a busy "PC optimiser" look. (WDD:design/design-principles.md)

**NavigationView (left vs top).** (WDD:develop/ui/controls/navigationview.md)
- **Top** suits up to 5 equally important categories, when every option must stay visible, or when content needs the space.
- **Left** suits 5–10 categories, when navigation should be prominent.
- The `Auto` default adapts by window width:
  - `Left` at 1008 epx and wider;
  - `LeftCompact`, which shows icons only, from 641 to 1007 epx;
  - `LeftMinimal` below 641 epx.
- `FooterMenuItems` sit just above the built-in Settings item. Use them for low-frequency destinations such as History/Activity or About. Use `PaneFooter` for non-navigation content.
- The back button exists but does not handle navigation by itself; the app must wire up the back stack.
- Windows 11 Settings is Microsoft's reference "left navigation silhouette". It uses 56 epx content margins around expander cards; tighter utility apps use 12 epx. (WDD:design/basics/app-silhouette.md)

**Title bar.** (WDD:design/basics/titlebar-design.md)
- **Height.** Standard is 32 px. Use 48 px if a search box or person picture lives in the title bar. A global search box goes centred in the title bar.
- **Background.** Mica by default. The title bar should blend with the window.
- **Content.**
  - 16 px icon, 16 px from the edge.
  - Caption-style title.
  - Back button (E830) to the left of the icon.
- **Behaviour.**
  - Every empty area must be draggable.
  - Right-click opens the system menu.
  - Elements go semi-transparent when the window is inactive.
  - The title bar follows high contrast via `SystemColors`, and grows with text scaling.

**Materials.** (WDD:design/style/mica.md, signature-experiences/materials.md, design/style/acrylic.md, signature-experiences/layering.md)
- **Mica** is opaque and wallpaper-tinted. Use it on the base layer of long-lived windows, visible in the title bar.
- **Mica Alt** is for tabbed title bars or a stronger contrast between base and commanding layers.
- **Acrylic** is only for transient, light-dismiss surfaces such as flyouts and menus.
- **Smoke** dims the UI behind modal dialogs.
- Apply a backdrop once per window, never on a single element.
- There are two content-layer patterns, both using `LayerFillColorDefaultBrush`:
  - Standard: one continuous content layer.
  - Card: separate cards.
- Mica falls back to a solid colour when high contrast, transparency off, Battery Saver or low-end hardware is in effect. The UI must not depend on translucency for meaning.
- Elevation scale: Window/Dialog 128, Flyout 32, Tooltip 16, Card 8, Control 2, Layer 1.

**Typography.** (WDD:design/signature-experiences/typography.md; develop/platform/xaml/xaml-theme-resources.md)
- **Font.** Segoe UI Variable, with automatic optical size.
- **Type ramp** (size/line height in epx, with its XAML style):

  | Style | Size / line height |
  |---|---|
  | Caption | 12/16 |
  | Body | 14/20 |
  | Body Strong | 14/20 semibold |
  | Body Large | 18/24 |
  | Subtitle | 20/28 |
  | Title | 28/36 |
  | Title Large | 40/52 |
  | Display | 68/92 |

- **Minimums.** 12 px regular, 14 px semibold.
- **Casing.** Sentence case everywhere.
- **Line length.** 20–60 characters per line.
- **Weights.** Regular for body text, Semibold for titles.

**Geometry and spacing.** (WDD:design/signature-experiences/geometry.md; design/basics/content-basics.md; design/layout/screen-sizes-and-breakpoints-for-responsive-design.md)
- **Corner radius.**
  - 8 px for windows, dialogs, flyouts and TeachingTip.
  - 4 px for in-page controls, list backplates, bars and ToolTip.
  - 0 px where straight edges meet, or when a window is snapped or maximised.
  - Global resources: `ControlCornerRadius` and `OverlayCornerRadius`.
- **Grid.** Every size, margin and position is a multiple of 4 epx.
- **Gutters.**
  - 8 epx between buttons, between a button and its flyout, and between a control and its header.
  - 12 epx between a control and its label, and between content areas.
  - 16 epx from surface edge to text.
- **Lists.** Multi-line lists use Body plus Caption text with 32 epx icons; section headers use Body Strong.
- **Expanders.** Controls sit right-aligned, 16 epx from the expander chevron; nested controls are indented 48 epx.

**Iconography.** (WDD:design/iconography/segoe-fluent-icons-font.md)
- Segoe Fluent Icons replaced Segoe MDL2 Assets on Windows 11. Use it with `FontIcon`/`SymbolIcon`.
- Glyphs are square em, so a 16 epx font size gives a 16×16 epx icon. They are not meant to sit inline with text.
- Glyphs can be layered on top of each other, which lets a status overlay sit on a base glyph.

**Colour, theme and high contrast.** (WDD:design/signature-experiences/color.md; design/accessibility/high-contrast-themes.md)
- Use `{ThemeResource}` brushes with ThemeDictionaries for Light, Dark and HighContrast.
- In HighContrast, map every custom brush to a `SystemColor*` resource:
  - never hard-code colours;
  - don't use `SystemColorGrayTextColor` for secondary text (it is reserved for disabled content);
  - use `SystemColorHotlightColor` only for hyperlinks.
- The theme setting offers "Light / Dark / Use system setting" in a ComboBox, plus a "Windows color settings" link to `ms-settings:colors`. (WDD:design/app-settings/guidelines-for-app-settings.md)

**InfoBar, TeachingTip and ContentDialog.** (WDD:develop/ui/controls/infobar.md, dialogs-and-flyouts/teaching-tip.md, dialogs-and-flyouts/dialogs.md)
- **InfoBar.**
  - Use it for a change in app state that the user should know about, acknowledge or act on. It sits inline, takes layout space and persists until closed or resolved.
  - Examples from the docs: lost connectivity, an automatic save error, "Update applied" with a release-notes link, a background task that finished.
  - Don't use it to respond to a user action that didn't change state, for time-critical alerts, or for non-essential messages.
  - Don't flash it in and out.
  - Updating an open InfoBar is **not announced** to screen readers; close and reopen it, or raise a notification.
- **TeachingTip.**
  - Transient and non-critical. Never use it for errors or status.
  - Stagger tips across sessions.
  - Users read about 3–5 words and take in 2–3 of them, so keep tips very short.
- **ContentDialog.**
  - Use it when the user *must* read something, or to confirm an action. Also use it for severe blocking states.
  - The first line names the issue or goal. The title must relate to the buttons.
  - Always include a safe Close/Cancel button on the right, with "do it" button(s) on the left.
  - Button labels are specific verbs, not OK.
  - Don't use a dialog for inline, contextual errors such as validation.

**Progress controls.** (WDD:develop/ui/controls/progress-controls.md)
- **Determinate ProgressBar** when the duration is known (install, download). It is non-blocking.
- **Indeterminate ProgressBar** when the duration is unknown. It is also non-blocking.
- **Indeterminate ProgressRing** only when interaction is blocked ("Connecting…").
- **Lists.** Put one ProgressBar at the top of a collection that is loading, not a spinner on each item.
- **Background work.** Use text status, or a notice on completion only, rather than constant spinners.

**Collections.** (WDD:develop/ui/controls/itemsview.md, listview-and-gridview.md, list-details.md)
- **ItemsView** (newer) handles uniform items with switchable list, grid or custom layouts.
- **ListView** supports Single, Multiple and Extended selection, each with defined keyboard behaviour.
- **List/details pattern.** Stacked from 320 to 640 epx, side by side from 641 epx.
- **No first-party DataGrid in WinUI 3.** The Community Toolkit DataGrid stayed at UWP 7.1 and was dropped from Toolkit 8. Microsoft's migration page points to the community WinUI.TableView, or ListView with a grid-style template. (S) https://learn.microsoft.com/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/what-is-supported

**Settings page conventions.** (WDD:design/app-settings/guidelines-for-app-settings.md)
- **Entry point.** The built-in NavigationView Settings item, last and pinned to the bottom.
- **Layout.**
  - Full-window and scrollable, with a max width of about 1000–1100 px.
  - Single column, grouped under section headers.
  - Built from Community Toolkit `SettingsCard` / `SettingsExpander`.
- **Content rules.**
  - Smart defaults and few settings.
  - Changes take effect immediately, with no Save button.
  - Toggle for a binary setting; radio buttons for up to 5 options; ComboBox otherwise.
  - Don't put workflow commands in Settings.
  - Disable settings that don't apply, rather than hiding them.
- **About.** An About `SettingsExpander` sits at the bottom, showing name, icon and version, with links inside.

**App notifications (toasts).** (WDD:develop/notifications/app-notifications/app-notifications-ux-guidance.md)
- Toasts must be informative, valuable and not noisy; too many interruptions push users to turn them off.
- You can send a toast suppressed, straight to Notification Center.
- Respect Focus Sessions (`FocusSessionManager`).
- Clicking a toast must open the app in that toast's context.
- Toasts support buttons and progress bars.

**Gallery reference.** WinUI 3 Gallery is the interactive companion for every control. It shows markup and adaptive behaviour, and it has design and accessibility pages. https://github.com/microsoft/WinUI-Gallery

---

## 2. Accessibility checklist for WinUI apps (practical)

Sources: WDD:design/accessibility/accessibility-checklist.md, keyboard-accessibility.md, high-contrast-themes.md, accessible-text-requirements.md; develop/input/access-keys.md, text-scaling.md; WCAG 2.2 Understanding (https://github.com/w3c/wcag/blob/main/understanding/22/target-size-minimum.html; (S) https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/).

1. **Names.** Give every interactive or meaningful element an accessible name, and add a description (Narrator reads it with CapsLock+F) for extra detail such as a confidence explanation. Icon-only buttons need `AutomationProperties.Name`.
2. **Keyboard is a primary input.**
   - Tab order must follow reading order; grids and tables are the common place where they diverge.
   - Arrow keys move within composite controls.
   - **F6 cycles major panes** (nav, list, details, command bar), and each pane has a name.
   - Use `IsTabStop=false` only on elements that stay interactive by other means.
3. **Access keys and accelerators.**
   - Alt shows KeyTips; single-character keys also work directly as Alt+key.
   - Scope keys per page or region to reduce cognitive load.
   - Localise the keys.
   - Add accelerators for frequent actions, for example F5 to rescan and Ctrl+F to filter.
4. **Focus visuals.** Keep the system two-tone focus visual (`FocusVisualWhite`/`FocusVisualBlack`). WCAG 2.2 adds 2.4.11 *Focus Not Obscured (Minimum)*, Level AA: sticky footers or InfoBars must not hide the focused item. 2.4.13 Focus Appearance is AAA.
5. **Contrast.** Text needs 4.5:1, or 3:1 for large text and non-text UI. Never use colour as the only signal: pair status colours with an icon and text.
6. **High contrast.** Test all four contrast themes. Custom status chips and severity colours must map to `SystemColor*` resources.
7. **Text scaling (100–225%).** Use no fixed heights, enable wrapping, and give a ToolTip with the full text wherever text is clipped. The title bar has to grow too.
8. **Target size.** WCAG 2.5.8 sets 24×24 px or enough spacing, at AA. Dense list rows with small checkboxes and overflow buttons need care.
9. **Motion.**
   - Respect "Animation effects" off (`UISettings.AnimationsEnabled`).
   - Avoid flashing, such as InfoBars that toggle rapidly.
   - Offer simplified UI where needed.
10. **Live updates.**
    - Announce status changes through UIA notification events or LiveSetting, for example "3 updates available" or "Update verified".
    - Re-raise InfoBars instead of mutating them in place.
11. **Tooling.** Use Accessibility Insights/Inspect, AccChecker and Narrator passes, plus automated checks in CI (step 6 of the checklist). Note that under ADR-021, AC-UX-01–09 are best effort.

---

## 3. Patterns in comparable software (patterns only, no cloning)

| Concern | Observed patterns |
|---|---|
| **"Updates available"** | Windows Update and the Store: a single status hero ("You're up to date" / "Updates available") with a primary **Get updates / Check for updates** button and a last-checked time. (S) Store support: https://support.microsoft.com/help/4026259. macOS App Store: an Updates pane with a count badge. (S) https://support.apple.com/guide/app-store/fir9b01adda3. Chocolatey GUI checks for outdated packages on open, with a configurable cache duration. (S) https://docs.chocolatey.org/en-us/chocolatey-gui/user-interface/main-window/actions/update-all/ |
| **Batch vs individual** | Almost everyone pairs **Update all** with per-row Update. Patch My PC: outdated items pre-selected; a tri-state header checkbox cycles all → outdated only → none (S). UniGetUI: multi-select bulk operations (https://github.com/marticliment/UniGetUI). Chocolatey for Business can hide Update All entirely for admins (S). |
| **Trust signals** | UniGetUI's README tells users to "review sources and publishers". Its FAQ: the safety of packages depends on the repository's checks, so prefer trusted publishers. GNOME Software's goals: "inspect and appraise apps before they install them", showing which repos are configured and handling "the same app from multiple sources"; "not all repos are equal" (https://github.com/GNOME/gnome-software/blob/main/doc/use-cases.md). WinGet hash mismatches usually mean a stale manifest or an altered download, and **cannot be overridden when elevated** (S). That is a trust state worth showing plainly. |
| **Version semantics** | SUMo lets users filter beta versions, but beta status depends on crowd reports. Mis-flags are common, and users fall back on Skip or Ignore (S). This argues for deterministic, explained classification over hidden heuristics. |
| **Progress and queue** | The Store and Discover show per-item progress inside an overall queue. Toast progress bars let users leave the app (WDD notifications). Silent installs are the default, with an option for a manual or interactive install (Patch My PC (S)). |
| **Failure and retry** | Discover's "Update All" sometimes needs a second click (S); users notice partial completion. Hash-mismatch threads show users need cause, next step and a retry path, without hunting through logs. |
| **Restart pending** | GNOME offline updates: an update needing a reboot installs only on reboot, with a "Restart & Update" prompt. (S) https://wiki.gnome.org/Design/OS/SoftwareUpdates. Discover's offline mode defers system packages to restart; switching it off lists them as separate, deselectable items (S). Windows active hours (max 18 h) control when automatic restarts happen. Patch My PC suppresses restarts during updates (S). |
| **History** | Windows Update History groups entries by category and date, with uninstall links (S). UniGetUI keeps the last 1,000 operations with up to 5,000 log lines each. |
| **Ignore / skip** | UniGetUI: "skip versions or completely ignore updates per package". SUMo: users use *Skip* (this version only) vs *Ignore* (forever) (S). Patch My PC: "skip updates for any application" (S). |
| **Scheduling** | Windows Update: pause for 1–5 weeks (S). macOS: "Update All" has a schedule-later menu (S). UniGetUI: interval or daily/weekly checks and installs. (The PC Steward MVP excludes unattended installation, so scheduling is out of scope beyond *checks/reminders*, if the spec permits even that.) |
| **Dashboards** | Windows 11 Settings Home uses interactive cards such as Recommended settings, Storage and Bluetooth. (S) https://support.microsoft.com/topic/56bdfb5a-b809-4e65-9c90-4bab76745e19 |

---

## 4. Trust and safety UX

**Elevation and UAC moments** (UXG:winenv-uac.md, which is Microsoft's canonical guidance)
- Goals:
  - eliminate unnecessary elevation;
  - be predictable;
  - require a single elevation per task;
  - revert to least privilege afterwards.
- **Shield icon.** Mark commands that elevate *immediately* with the UAC shield. Show it consistently even when UAC is off. Don't change it to reflect elevated state. Don't add an ellipsis just because a command elevates.
- **Elevate as late as possible.** If a confirmation is needed, show elevation **after** the confirmation. Once elevated, stay elevated until the task is done.
- **Elevate per task, not per setting.** Don't mix standard and admin options on one surface. Give everyone read-only information.
- **Cancelled elevation.** If the user cancels the UAC prompt, return to the previous UI with **no error message**: they chose not to proceed. Don't pre-warn that elevation "might" be needed.
- **Wizard pattern.** Put the shield on the commit button, which carries a specific label, then:
  1. On commit, go to the Progress page and show UAC modally ("Waiting for permission…").
  2. If the user cancels, go back to the Commit page so they can retry.

**Confirmations** (UXG:mess-confirm.md; WDD dialogs)
- Confirm only actions with significant or unintended consequences: a valuable asset, system integrity, privacy, or 30 seconds or more of the user's time.
- Unnecessary confirmations train users to click through. Consider alternatives first: prevent the error, offer undo, give clear feedback, or physically separate destructive commands.
- **Labels.**
  - Use specific responses, or Yes/No when thought is needed. Never OK/Cancel for a confirmation.
  - Give everything needed to decide, such as the exact scope and what remains afterwards.
  - Don't use warning icons for routine questions.
- NN/g's article on confirmation dialogs makes the same point: automatic confirmations protect nobody, and dialogs fail when they aren't specific about what will be affected. (S) https://www.nngroup.com/articles/confirmation-dialog/

**Warning fatigue** (UXG:mess-warn.md; (S) research)
- Good warnings involve real risk, have immediate relevance, lead to action, are not obvious and are rare.
- Be specific: say the source, the condition, what to do, and what happens if the user does nothing.
- Show one warning per condition.
- Habituation research:
  - fMRI (CHI 2015): responses decay from the second exposure (S).
  - Vance et al.: habituation to look-alike *non-security* notifications **generalises** to security warnings, depending on visual similarity (S). https://par.nsf.gov/biblio/10176924
  - Implication: risk-level UI must look clearly different from routine informational UI, and appear rarely.

**Destructive actions (Clear scan data / Clear downloaded files / Factory reset)**
- **Primer's tiers** (S, https://primer.style/product/scenario-patterns/delete):
  - no confirmation for reversible actions;
  - a simple dialog for a single resource;
  - **type-to-confirm only for the widest-impact action**, used sparingly so it still means something.
  - The danger button carries an explicit label; avoid "Yes/OK/Confirm".
- **Danger zone.** A "danger zone" section, set apart and bordered with an alert icon, is a recurring proposal (Primer PR #113, draft).
- **Physical separation.** The Win32 guide also says to separate destructive commands physically from safe commands.

**Progressive disclosure** ((S) https://www.nngroup.com/videos/progressive-disclosure/; primer/design ui-patterns/progressive-disclosure.mdx)
- Show primary options by default and put secondary ones a click away.
- Make the toggle obvious, pair icons with text, keep context in place, and use it only when necessary.
- In WinUI, use Expander or SettingsExpander, or a details pane.

**Honest states** (primer/design ui-patterns/degraded-experiences.mdx; HAX)
- "Don't try to conceal or downplay that something is wrong." When data is unavailable, don't show a generic empty state that looks like data loss.
- Use a muted alert icon rather than loud colour when there are many degraded items, to avoid a "broken" feel.
- Microsoft's Human-AI guidelines (*make clear what the system can do / how well it can do it*) apply equally to deterministic matching confidence. (S) https://www.microsoft.com/en-us/research/project/guidelines-for-human-ai-interaction/

**Voice** (WDD:design/style/writing-style.md)
- Casual, empathetic, no jargon, active voice.
- Never blame the user.
- Put the key point first.

---

## 5. Information density modes — when each fits

| Mode | Fits when | PC Steward use | Risk |
|---|---|---|---|
| **Overview / dashboard** (Settings Home cards; Windows Update hero) | Users need status at a glance and one next action. Shneiderman: "overview first, details on demand" (S). | Home: health summary, actionable count, last scan, restart-pending, recent results. | Vanity metrics; scoring that implies "optimisation" (out of scope). |
| **List–details** (WinUI pattern, side by side from 641 epx) | Large collections the user scans, filters and inspects one by one. | Inventory and Updates: rows with version, status and confidence; a details pane showing evidence, source, publisher and history. | Too many columns; poor keyboard support in custom grids. |
| **Wizard / guided flow** (Win32 wizard and UAC pattern) | Infrequent, consequential, linear tasks that need review and commit. | Update job: Review (immutable plan) → Approve (shield) → Progress → Verify → Result. Also Factory reset. | Over-guiding power users; repeat elevations. |
| **Keyboard-first / command palette** (PowerToys Command Palette, Win+Alt+Space, since v0.90, 2025 (S)) | Power users who repeat actions and want to jump without the mouse. | Ctrl+K / Ctrl+F palette for "Scan now", "Go to app X", "Open history". Navigation and filtering only. | Commands must route into the same confirmation and approval flow; the palette must never skip review. |

---

## 6. 2025–2026 trends relevant to utilities

- **Windows App SDK 2.0** (April 2026) is the first major version since 1.0 and uses semantic versioning. Release notes list 2.4.0 on 13 Aug 2026 (S). The WinUI repository is open source; reports say the "WinUI 3" brand is moving to plain "WinUI" (one secondary source). WinUI 3 Gallery 2.9 is built on SDK 2.0 (S).
- **Windows 11 Settings.**
  - Card-based Home with Recommended settings (S).
  - Semantic search (May 2025).
  - An **AI agent in Settings** on Copilot+ PCs that changes settings *with the user's permission* (S). https://pureinfotech.com/windows-11-ai-agent-settings-app/
  - The relevant pattern is the explicit permission step. PC Steward excludes AI from trust and execution decisions entirely. At most, AI-free natural-language *search/filter* over local data could be considered later, and only with explicit scope.
- **Command palettes are mainstream on Windows** (PowerToys CmdPal, 2025) (S). A keyboard-first layer meets power-user expectations.
- **Calm, layered Fluent visuals.** Mica plus card layering is the house style for first-party utilities like Settings. Because the system controls ship it by default, following it signals "native and trustworthy".
- **WCAG 2.2 AA** (focus not obscured, 24 px targets) is now the baseline that procurement and reviews use.

---

## 7. Design implications for PC Steward (sourced recommendations)

1. **Use a left NavigationView in `Auto` mode** with about 5–7 items:
   - main items: Home, Updates, Installed apps, Activity/History;
   - footer item: About/Help;
   - built-in Settings, pinned to the bottom.

   This follows Microsoft's own "left navigation" utility silhouette. [WDD navigationview; app-silhouette; app-settings]
2. **Use Mica on the base layer, extended into a custom 32 px title bar**, with the standard content layer (`LayerFillColorDefaultBrush`) and cards only where content is grouped. Acrylic stays limited to flyouts, and nothing may rely on translucency, because of the HC and Battery Saver fallbacks. [WDD mica; layering; titlebar]
3. **Apply the type ramp strictly:**
   - Title (28) for page headers;
   - Subtitle (20) for section heads;
   - Body/Body Strong for rows;
   - Caption for metadata such as version strings and timestamps;
   - sentence case throughout, nothing smaller than 12 px.

   [WDD typography]
4. **Codify spacing tokens on a 4 epx grid:** 8/12/16 gutters, 4 px corners on in-page elements, 8 px on overlays. Use 24–56 epx page margins, tighter on dense list pages. [WDD geometry; content-basics; app-silhouette]
5. **Make Home a calm status hero plus a few cards.** The hero shows one state sentence (for example "4 updates ready · last scanned 10 min ago"), a primary **Scan** button and a secondary **Review updates** button. Cards cover Restart pending, Needs attention (unverified or failed) and Recent activity. Include no health "scores", to avoid looking like a generic optimiser. [Settings Home (S); design principles: Calm; AGENTS.md safety boundaries]
6. **Updates page: list–details**, side by side from 641 epx and stacked below that.
   - Row: app name, installed → available version, source (WinGet), and a status chip that pairs an icon with text.
   - Details pane: publisher, package identifier, matching evidence, version-jump type, and the last result.
   - Use a single top ProgressBar while loading, not a spinner on each row.

   [WDD list-details; progress-controls; accessibility checklist]
7. **Partition updates into explicit groups:**
   - **Ready** (actionable);
   - **Needs review** (low-confidence, architecture or channel conflict; shown with the reason and *no* update button);
   - **Ignored/Skipped**.

   Only "Ready" items can be selected for a job. This makes "shown but not actionable" structurally obvious instead of relying on a disabled button. [GNOME "not all repos/apps are equal"; Primer degraded-experiences; AGENTS.md]
8. **Explain confidence in words, not just a badge.** For example: "Matched by package ID and publisher" (high), or "Name match only. Can't confirm it's the same app" (low). Put the full evidence behind an Expander, and expose it as the accessible description. [HAX "how well the system can do"; NN/g progressive disclosure (S); WDD basic-accessibility-information]
9. **Batch with a reviewed, immutable plan.** "Update selected (n)" opens a **Review** step listing exact items and versions. The commit button is specific ("Update 4 apps") and carries the **UAC shield** only if elevation is actually needed. Elevate **after** review, once per job, then drop privileges. [UXG winenv-uac; mess-confirm]
10. **If the user cancels UAC, return silently to Review.** Show no error. Leave the plan intact and offer retry. Never pre-warn that "admin may be required". [UXG winenv-uac]
11. **Job progress: an overall determinate ProgressBar plus per-item state**:
    - Queued
    - Downloading
    - Installing
    - Verifying
    - result

    The page stays non-modal so the user can navigate away. An optional toast with progress handles minimised windows; it is suppressed during Focus sessions and opens the job on click. [WDD progress-controls; app-notifications-ux-guidance]
12. **Show final states honestly and distinctly:**
    - **Verified** (version confirmed);
    - **Restart required** (reliable restart-pending signal);
    - **Installed, not verified** (provider reported success but verification failed or was inconclusive);
    - **Failed** (cause in plain language, next step, Retry).

    Each state uses a distinct icon, text and colour. Never collapse the provider result into "Success". [AGENTS.md; Primer degraded-experiences; UXG mess-warn "be specific"]
13. **Show restart-pending as a persistent InfoBar** (closable, re-raised for screen readers) and a Home card listing the affected apps. Don't add a restart button that implies PC Steward restarts the machine unattended. [WDD infobar; GNOME offline updates (S)]
14. **Offer per-row Skip this version and Ignore updates for this app** in an overflow menu, each reversible from an "Ignored" view. Wording should make the difference explicit, because users confuse the two. [UniGetUI README; SUMo forums (S); Patch My PC (S)]
15. **Activity/History as a filterable timeline grouped by date.** Each entry shows the job, items, final verified state and duration; details expand to structured logs. Retention is visible, and export is offered only if the spec allows. [Windows Update history (S); UniGetUI history]
16. **Separate the three destructive actions physically and semantically** in a bordered "Reset & storage" section at the bottom of Settings. Each gets its own card that describes exactly what is deleted and what is kept.
    - **Clear scan data:** a simple specific-verb dialog.
    - **Clear downloaded files:** the same, showing size and path scope.
    - **Factory reset:** type-to-confirm (for example, typing "reset") with a danger-styled, explicitly labelled button.

    [Primer delete tiers (S); UXG mess-confirm; AGENTS.md]
17. **Keep confirmations rare.** Don't confirm scans, filtering, Skip or Ignore (all reversible). Reserve dialogs for update commits and destructive actions, so that warnings stay effective and look different from routine UI. [UXG mess-confirm/mess-warn; Vance et al. habituation (S)]
18. **Choose InfoBar, TeachingTip and ContentDialog strictly by purpose:**
    - InfoBar for state, such as "WinGet unavailable", "Scan stale" or "Restart pending";
    - TeachingTip only for first-run hints, at most a few words and staggered;
    - ContentDialog only for commit and destructive confirmations.

    [WDD infobar; teaching-tip; dialogs]
19. **Settings uses SettingsCard/SettingsExpander**, single column, max width about 1000–1100 px, changes applied immediately. Theme offers Light/Dark/Use system setting, plus the "Windows color settings" link. About sits at the bottom as an expander. [WDD app-settings]
20. **Use a keyboard-first layer:**
    - access keys on primary commands;
    - F5 to rescan, Ctrl+F to filter, F6 between nav, list and details;
    - optionally a Ctrl+K palette for navigation and filters.

    Palette actions must open the same review flow; they are never a shortcut past approval. [WDD keyboard-accessibility; access-keys; PowerToys CmdPal (S)]
21. **Build accessibility in from the start:**
    - every status chip has text plus an icon, and an accessible name such as "Status: needs review, low confidence";
    - UIA notifications for scan or job completion;
    - HC brushes mapped to `SystemColor*`;
    - text scaling to 225% without clipping;
    - targets at least 24 px;
    - sticky bars don't obscure focus.

    [WDD accessibility checklist; high-contrast; text-scaling; WCAG 2.2]
22. **Tables: avoid the deprecated Toolkit DataGrid.** Use ListView or ItemsView with a column-aligned row template, plus sort and filter commands in a CommandBar. Evaluate WinUI.TableView only through an ADR and dependency approval. [MS migration page (S); AGENTS.md "don't install packages"]
23. **Use plain, empathetic, blame-free copy** in sentence case:
    - "Couldn't verify the new version of Firefox. It may still be installing." rather than "Error 0x8…";
    - technical codes go in the details Expander.

    [WDD writing-style; UXG mess-warn]
24. **Trust signals per update:**
    - source (always "WinGet community repository");
    - publisher;
    - version jump labelled patch, minor or major (major highlighted);
    - an installer hash-mismatch status shown as "Blocked: installer doesn't match its listing", never overridable.

    No AI is involved in these signals. [UniGetUI FAQ; WinGet hash threads (S); AGENTS.md]

---

## 8. Overhaul directions suggested by the research

1. **"Calm Native" (Settings-style left nav and cards).** Closest to Windows 11's own utilities, so it gets familiarity and trust for free. Low risk, and maximises use of standard controls and accessibility.
2. **"Status Hero + Review Queue" (Update-centric).** Home is one actionable sentence and a queue, modelled on the Windows Update/Store mental model. It best serves non-experts who just want "keep my apps current".
3. **"Inspector" (list–details power view).** A dense inventory with an evidence-rich details pane, filters and keyboard navigation. It suits power users who want to see *why* a match is trusted.
4. **"Guided Job Wizard" (Review → Approve → Verify).** It makes the immutable, elevated update job a first-class, linear flow with a single UAC moment. It directly embodies the product's safety model.
5. **"Honest Ledger" (Activity-first).** The timeline/history is the spine: every scan and job, with verified, unverified or failed outcomes. It builds long-term trust by making outcomes auditable.
6. **"Dual-density" (Simple / Advanced modes).** One codebase with progressive disclosure: a simple mode hides evidence and technical columns, and an advanced mode shows them. It serves both target personas without forking the UI.
7. **"Keyboard-first Palette layer."** A Ctrl+K command palette and access keys over any of the above. It matches the 2025–26 Windows power-user trend while routing every action through review.

---

### Source index (primary, fetched via GitHub)
- WinUI and Windows design docs: https://github.com/MicrosoftDocs/windows-dev-docs/tree/docs/hub/apps (navigationview, titlebar-design, mica, acrylic, materials, layering, typography, geometry, content-basics, app-silhouette, screen-sizes, segoe-fluent-icons-font, color, high-contrast-themes, infobar, teaching-tip, dialogs, progress-controls, itemsview, listview-and-gridview, list-details, guidelines-for-app-settings, app-notifications-ux-guidance, accessibility-checklist, keyboard-accessibility, access-keys, text-scaling, accessible-text-requirements, writing-style, design-principles)
- Win32 UX guide: https://github.com/MicrosoftDocs/win32/tree/docs/desktop-src/uxguide (winenv-uac.md, mess-confirm.md, mess-warn.md)
- WCAG 2.2 target size: https://github.com/w3c/wcag/blob/main/understanding/22/target-size-minimum.html
- Primer: https://github.com/primer/design/tree/main/content/ui-patterns (progressive-disclosure, degraded-experiences)
- UniGetUI: https://github.com/marticliment/UniGetUI · GNOME Software: https://github.com/GNOME/gnome-software (README, doc/use-cases.md) · KDE Discover: https://github.com/KDE/discover · WinUI Gallery: https://github.com/microsoft/WinUI-Gallery
- Search-derived (S) sources are inline above. Before citing them in product docs, re-verify with network access to learn.microsoft.com, nngroup.com, support.microsoft.com, support.apple.com and primer.style.
