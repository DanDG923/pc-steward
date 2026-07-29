# PC Steward accessibility review

**Standard:** WCAG 2.2 Level AA where applicable to a Windows desktop application  
**Platform guidance:** Windows UI Automation, WinUI accessibility, Windows text scaling, High Contrast and reduced motion  
**Interactive reference:** the native WinUI prototype in `design/native-prototype/`; the browser-rendered `design/prototype/` archive is superseded and supplies no native accessibility evidence  
**Design status:** Conforms by specification; implementation conformance must be verified in Task 29 and cannot be claimed from design files alone

ADR-021 classifies the existing `AC-UX-01–09` release criteria as optional best effort. This design nevertheless treats WCAG 2.2 AA as the quality target because the interface handles consequential software updates and data deletion.

## 1. Executive assessment

The design addresses the major accessibility and accidental-action risks:

- full keyboard paths are defined for every primary journey;
- status, confidence, freshness and severity use text and icons as well as colour;
- authored light/dark colours meet AA text contrast in the tested combinations;
- interactive outlines use a 3:1 non-text contrast token;
- layouts reflow through 200% Windows text scaling;
- high contrast uses Windows system colours rather than authored fills;
- motion is restrained and removable;
- destructive actions default to Cancel;
- factory reset requires two separate confirmations and an exact phrase;
- charts have equivalent lists/tables;
- screen-reader announcements are bounded.

The primary unresolved risk inherited from the current implementation is end-to-end keyboard verification in the real WinUI app. Prior evidence confirmed names and text entry but did not reliably move focus or activate all controls. The new implementation must test real focus order rather than infer it from AutomationProperties.

## 2. Colour contrast

Ratios use the WCAG relative-luminance formula. Normal text target is 4.5:1; large text target is 3:1; essential non-text boundaries and states target 3:1.

### 2.1 Verified text combinations

| Theme | Foreground / background | Ratio | Result |
|---|---|---:|---|
| Light | Primary `#172033` / surface `#FFFFFF` | 16.27:1 | Pass AAA |
| Light | Secondary `#526077` / surface `#FFFFFF` | 6.37:1 | Pass AA |
| Light | White / brand `#1F62D1` | 5.64:1 | Pass AA |
| Light | Brand text `#174EA6` / brand subtle `#EDF4FF` | 7.09:1 | Pass AAA |
| Light | Success `#17663F` / success surface `#EAF7F0` | 6.34:1 | Pass AA |
| Light | Warning `#7A5000` / warning surface `#FFF6DD` | 6.55:1 | Pass AA |
| Light | Danger `#9C261D` / danger surface `#FDEDEC` | 6.83:1 | Pass AA |
| Dark | Primary `#F2F5F9` / surface `#171F2D` | 15.12:1 | Pass AAA |
| Dark | Secondary `#BBC5D4` / surface `#171F2D` | 9.49:1 | Pass AAA |
| Dark | Brand `#8FB7FF` / surface `#171F2D` | 8.18:1 | Pass AAA |
| Dark | Dark text `#0E1522` / brand `#8FB7FF` | 9.04:1 | Pass AAA |
| Dark | Success `#7AD6A3` / success surface `#123C29` | 7.03:1 | Pass AAA |
| Dark | Warning `#F0C86A` / warning surface `#443300` | 7.66:1 | Pass AAA |
| Dark | Danger `#FF9A91` / danger surface `#4A1E1B` | 6.89:1 | Pass AA |

### 2.2 Non-text contrast

- Light interactive boundary `#8994A6` against white: 3.07:1 — Pass.
- Dark interactive boundary `#68778F` against `#171F2D`: 3.64:1 — Pass.
- Brand focus indicators exceed 3:1 against authored surfaces.
- `color.border` is decorative only and may be below 3:1; inputs, selection, focus and essential card boundaries use `color.borderStrong`.
- Disabled controls are exempt from contrast requirements but remain visually distinguishable and retain adjacent explanation.

### 2.3 Colour-use rules

- Never use green alone for success, amber alone for review or red alone for failure.
- Chart segments have labels and an adjacent table/list.
- Selected rows use a checkbox/state and border/indicator in addition to fill.
- High contrast replaces semantic fills with system colours while retaining labels/icons.

## 3. Keyboard accessibility

### 3.1 Global focus order

1. Title-bar/system controls.
2. Navigation and collapse control.
3. Global operation/banner controls.
4. Page title and command bar.
5. Search and filters.
6. Main list/content.
7. Detail pane.
8. Page footer/secondary links.

`F6` cycles major regions. Page navigation puts programmatic focus on the new page title without announcing the entire page.

### 3.2 Required keyboard paths

| Journey | Required keys |
|---|---|
| First scan | Tab/Shift+Tab, arrows within radio cards, Space/Enter on safe actions |
| Scan cancel | Focusable Cancel at all active phases; Alt access key optional; Enter/Space |
| Application filtering | Ctrl+F to search, Tab to filters, arrows to values, Escape to close |
| Table navigation | Arrows move row, Space selects, Enter opens detail, Shift+F10 context |
| Finding review | Space selects supported rows; review button receives count in accessible name |
| Update confirmation | Tab through evidence/actions; default remains Cancel |
| Data clearing | Tab to category action; confirmation defaults Cancel |
| Factory reset | Two dialogs; exact phrase input; danger button cannot be activated by Enter from input |
| Detail pane | Heading focus on open; Escape closes; focus returns to invoking row |
| Settings | Tab between setting groups; Space toggles; source consent precedes persisted change |

### 3.3 Focus visibility

- 2 px outline and 2 px offset on every interactive element.
- Focus is never indicated by fill alone.
- Programmatic focus is not moved during background refresh.
- When a selected row disappears after filter/action, focus moves to the next logical row or page heading with an announcement.
- Toast appearance never steals focus.

### 3.4 Keyboard test obligations

Use real keyboard input on an interactive Windows 11 desktop:

- Tab and reverse-Tab across each page.
- Enter/Space semantics on buttons, rows, checkboxes and toggles.
- Escape behaviour on every flyout, pane and dialog.
- F6 region cycle.
- focus restore after dialog/pane.
- no keyboard trap except intentional modal trap.
- no accidental factory reset from Enter, access keys or key repeat.

## 4. Screen-reader clarity

### 4.1 Landmarks and headings

- One navigation region named `PC Steward`.
- One main region.
- One visible page title exposed as UI Automation heading level 1.
- Section headings follow a logical hierarchy.
- Detail pane is a complementary region named after the selected item.

### 4.2 Accessible naming patterns

| Visual | Accessible name example |
|---|---|
| Count card | `3, Updates ready to review` |
| Status chip | `Status: Review required` |
| Freshness | `Source freshness: Cached, last checked 2 days ago` |
| Selection | `Select Contoso Editor update, version 4.2` |
| Confidence | `Match confidence: Verified. Exact WinGet package identity and compatible architecture.` |
| Icon-only copy | `Copy package ID` |
| Progress | `Scan progress, Comparing approved sources, 68 percent` |
| Destructive action | `Factory reset PC Steward, removes all local PC Steward data` |

Avoid names that repeat visible group labels or include decorative icon names.

### 4.3 Live regions

- **Polite:** scan phase change, result count update, setting saved, toast.
- **Assertive:** operation cannot safely continue, partial reset, destructive failure requiring action.
- Progress percentage announces at phase changes or meaningful thresholds, not every update.
- Search result count uses a 300–500 ms debounce.
- Timer/elapsed time is visible but not continuously announced.

### 4.4 Tables and lists

- Headers have programmatic relationships to cells.
- Sort direction is exposed.
- Multi-select state and selected count are exposed.
- Virtualized rows preserve meaningful position/count where the platform supports it.
- Stacked responsive rows keep the same accessible field labels.

### 4.5 Technical content

- Package IDs and paths have readable labels before the value.
- Raw diagnostics use a named scroll region.
- Copy success is announced.
- Redacted values are identified as redacted.
- Long identifiers do not force character-by-character speech by default.

## 5. Windows text and display scaling

### 5.1 Required matrix

| Display scale | Text scale | Minimum validation |
|---:|---:|---|
| 100% | 100% | Reference layout |
| 125% | 100% | Common laptop |
| 150% | 150% | Existing evidence baseline |
| 175% | 150% | Dense desktop stress |
| 200% | 200% | Reflow and primary-action visibility |

### 5.2 Reflow rules

- Navigation collapses below 1040 effective px.
- Headers wrap commands into overflow.
- Summary groups become one column.
- Tables become stacked records below 720 px content width.
- Detail pane overlays or becomes full page.
- Dialog body scrolls but title and actions remain visible.
- Text wraps instead of ellipsis except long technical identifiers.
- No horizontal page scrolling; bounded technical regions may scroll.

### 5.3 Validation focus

At 200% verify:

- Start/Cancel scan remains visible.
- source disclosure text is not clipped;
- finding confidence/actionability remains readable;
- table/list retains labels;
- delete/preserve content is visible in confirmation dialogs;
- final reset action cannot move outside the available dialog/window bounds.

## 6. High contrast

Test Aquatic, Desert and a custom high-contrast theme where available.

Expected behaviour:

- system `Canvas`, `CanvasText`, `Highlight`, `HighlightText` resources;
- 2 px essential boundaries;
- no reliance on shadow, translucency or subtle fill;
- status icon and text remain;
- focus is clearly distinct from selection;
- charts use outlined segments/markers plus legend;
- selected/checked state remains exposed programmatically.

Do not hard-code light/dark palette values in control templates.

## 7. Target size and pointer use

WCAG 2.2 2.5.8 target-size minimum is 24 × 24 WinUI effective pixels (epx), with applicable spacing exceptions.

- Default controls are 40 px high.
- Compact command controls are 32 px.
- Icon targets are at least 32 × 32 px.
- Inline links have sufficient line height and spacing.
- Row checkboxes have a 32 px hit region.
- Resize splitters are at least 8 px visually/operably where used.

No journey requires drag, precision pointing, hover or right-click.

## 8. Motion and vestibular safety

- Authored transitions are 120–240 ms.
- No parallax, auto-scrolling, flashing, bouncing or large-distance motion.
- Scan animation corresponds to active work.
- Pause/cancel stops animation.
- Reduced motion removes transforms and shortens fades to 0–50 ms.
- No content begins moving because it receives focus.

The design has no content near the WCAG flash threshold.

## 9. Timing and interruption

- No user task has a product-imposed timeout.
- Toast dismissal pauses while focused or hovered.
- Source freshness expiry does not erase the visible cached result mid-review; actionability changes explicitly.
- If reviewed evidence expires or changes, execution stops and returns to review with explanation.
- Scan cancellation remains available and acknowledges within the product’s two-second target.

## 10. Error prevention and recovery

### 10.1 Consequential actions

- Updates require review and confirmation.
- No findings are preselected.
- Batch queue lists every item.
- No raw command/path/URL entry exists.
- Running application and UAC expectations appear before execution.
- Provider completion is not called success before verification.

### 10.2 Destructive actions

- Routine categories are separate.
- Each dialog lists Deletes and Preserves.
- Cancel is default.
- Factory reset uses two dialogs plus exact phrase.
- Reset is unavailable during active work.
- Partial deletion/reset never uses a success state.

### 10.3 Undo

Real Undo is provided only for reversible decisions such as pin/ignore. Irreversible operations rely on review, confirmation and accurate recovery.

## 11. Language and cognition

- One concept per sentence where consequences matter.
- Technical terms are paired with plain meaning.
- Button labels use verb + object.
- Status text avoids idiom, shame, humour or fear.
- Date/time uses Windows locale plus relative freshness.
- Advanced evidence is progressively disclosed.
- Empty states identify whether the cause is no data, no findings or no filter match.
- “Unmanaged” and “source not found” replace “unsafe.”

## 12. WCAG 2.2 AA mapping

| Criterion/group | Design response | Implementation verification |
|---|---|---|
| 1.1.1 Non-text Content | Decorative SVG hidden; informative chart has text/table alternative | UIA tree inspection |
| 1.3.1 Info and Relationships | Headings, definition lists, table headers, landmarks | Accessibility Insights |
| 1.3.2 Meaningful Sequence | Visual and automation order match | keyboard + UIA |
| 1.3.3 Sensory Characteristics | No instruction depends only on shape/colour/location | content review |
| 1.4.1 Use of Colour | icon + text semantic status | visual/state audit |
| 1.4.3 Contrast | verified combinations at/above 4.5:1 | automated token test |
| 1.4.10 Reflow | adaptive layouts through 200% | scaling matrix |
| 1.4.11 Non-text Contrast | strong border/focus at/above 3:1 | token + screenshot test |
| 1.4.12 Text Spacing | no clipped fixed-height text regions | text spacing stress |
| 1.4.13 Hover/Focus Content | dismissible/persistent/hoverable tips | interaction test |
| 2.1.1 Keyboard | every primary action keyboard reachable | real keyboard journey |
| 2.1.2 No Keyboard Trap | only modal dialog trap; Escape/Close | journey test |
| 2.2.1 Timing Adjustable | no task timeout | behaviour review |
| 2.2.2 Pause, Stop, Hide | active progress/cancel; no decorative loops | motion review |
| 2.3.1 Flashes | no flashing content | visual review |
| 2.4.3 Focus Order | defined global/page order | keyboard inspection |
| 2.4.6 Headings and Labels | descriptive page/section/control labels | content review |
| 2.4.7 Focus Visible | 2 px + offset | visual inspection |
| 2.4.11 Focus Not Obscured (Minimum) | sticky regions do not cover focused item | scroll/focus test |
| 2.5.1 Pointer Gestures | no multipoint/path-only action | interaction review |
| 2.5.3 Label in Name | accessible name begins with visible label | UIA inspection |
| 2.5.7 Dragging Movements | all reordering/removal has button alternative | journey test |
| 2.5.8 Target Size | 32–40 px controls | layout assertion |
| 3.1.1 Language | root UI language set | automation property |
| 3.2.1 On Focus | focus never triggers consequential change | keyboard test |
| 3.2.2 On Input | source consent precedes privacy-changing toggle | setting test |
| 3.3.1 Error Identification | plain summary + field association | negative tests |
| 3.3.2 Labels or Instructions | visible input labels and required phrase | dialog test |
| 3.3.3 Error Suggestion | safe recovery action where known | state review |
| 3.3.4 Error Prevention | review/confirm/re-validate actions | workflow tests |
| 3.3.7 Redundant Entry | reset phrase is intentional exception for safety | UX review |
| 3.3.8 Accessible Authentication | no product authentication in MVP | not applicable |
| 4.1.2 Name, Role, Value | standard controls + explicit automation properties | UIA test |
| 4.1.3 Status Messages | bounded live-region announcements | screen-reader test |

## 13. Required assistive-technology test set

Before sign-off:

- Narrator with scan, application detail, update review and clear-data journeys.
- Accessibility Insights for Windows FastPass on every primary screen.
- Real keyboard-only traversal at 100% and 150%.
- Windows High Contrast.
- Windows text scaling at 150% and 200%.
- Light/dark theme.
- Reduced animation effects.
- Screen Magnifier at 200–400% for focus visibility.
- Long application name, missing fields and 128-character package ID.

## 14. Known limitations and unresolved checks

1. Design artefacts cannot prove WinUI UI Automation behaviour.
2. Current procedural `MainWindow.cs` needs restructuring before predictable page focus/navigation can be tested.
3. Prior automation could type in Search but did not prove full Tab/list activation; real interactive testing remains mandatory.
4. Windows High Contrast chart patterns and custom status templates require platform-specific implementation QA.
5. App icon legibility at every Windows package size requires raster export inspection.
6. The native WinUI design prototype validates hierarchy, native control composition and interaction intent, but its isolated, unpackaged implementation does not prove production UI Automation or accessibility conformance.

### Native prototype verification checklist

Before the authoritative native design prototype is treated as review-ready, verify and record:

- the native prototype builds successfully in the intended Release configuration;
- the WinUI window launches without unhandled exceptions or relevant debug-output errors;
- light and dark layouts were visually inspected;
- the 900 × 640 minimum window and 150%/200% Windows text-scale checks produce no clipped essential action or uncontrolled horizontal overflow;
- compact `NavigationView` items retain complete accessible names after visible labels collapse;
- destination/page-key changes move focus to the new page heading or first logical content target;
- the navigation pane, main content and active-operation region have clear UI Automation names and predictable F6 region order;
- scanning, pause, cancel, no-findings, partial-provider and offline states remained distinguishable in text;
- update review exposed only one dialog and followed the selected application through evidence and outcome;
- clear-download confirmation explicitly separated Deletes and Preserves;
- factory reset required two dialogs plus the exact `RESET` phrase, with the final action disabled beforehand.

These checks are evidence for the native design prototype only. Narrator, production UI Automation, High Contrast, Windows text scaling and real WinUI focus testing remain release gates for `src/PCSteward.Desktop`.

### Superseded browser-reference record

The earlier browser-rendered artifact received historical layout and interaction QA on 29 July 2026. That record is retained only as visual-design history: browser console, server-render, HTML heading, document-focus and browser-width checks do not satisfy any PC Steward implementation or release criterion. The archive must not be published as the application or used as the accessibility implementation base.

## 15. Accessibility acceptance checklist

- [ ] Every page has one programmatic title.
- [ ] Every icon-only control has name, tooltip and 32 px target.
- [ ] Status/freshness/confidence use text and icon.
- [ ] All normal text combinations pass 4.5:1.
- [ ] Essential non-text boundaries/focus pass 3:1.
- [ ] Primary journeys complete by keyboard.
- [ ] Focus never disappears, jumps on background refresh or remains behind a dialog.
- [ ] Narrator announces page, phase, status and confirmation consequences clearly.
- [ ] 200% text scale preserves primary actions and dialog consequences.
- [ ] High contrast preserves selection, focus and status.
- [ ] Reduced motion removes transforms/loops.
- [ ] Destructive dialogs default to Cancel.
- [ ] Factory reset cannot be activated by one click or accidental Enter.
- [ ] Partial failure is not announced as success.
