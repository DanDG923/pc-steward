# PC Steward design system

**System name:** Quiet Authority
**Status:** Implemented production baseline; Task 35 validation boundary is recorded in `../docs/UI_VALIDATION_REPORT.md`
**Target:** Windows 11 x64, WinUI 3 / Windows App SDK
**Source of machine-readable values:** `design-tokens.json`

## 1. Product personality

PC Steward is a careful technical guide, not a mechanic selling an emergency. It should feel:

- **Dependable:** stable layouts, explicit state, predictable consequences.
- **Calm:** muted surfaces, restrained motion, no pulsing warnings or countdowns.
- **Technically credible:** provenance, timestamps, scope and confidence are easy to inspect.
- **Respectful:** no account requirement, no guilt, no fake urgency and no hidden selection.
- **Conservative:** uncertain evidence is shown as uncertainty, never converted into an action.
- **Lightweight:** the UI communicates bounded work and does not appear busy while idle.

The core phrase is **“evidence before action.”**

## 2. Visual principles

1. **Lead with the answer.** Each screen begins with the current state and next safe action.
2. **Show provenance beside conclusions.** Freshness, source and confidence never live only in a detail view when they affect actionability.
3. **Use quiet hierarchy.** Size, spacing and typography carry structure; colour carries meaning only as a supplement.
4. **Separate observation from change.** Scanning, reviewing and applying are visually distinct steps.
5. **Make destructive scope legible.** Deletion controls state both what is removed and what is preserved.
6. **Progressively disclose depth.** Plain-language summaries come first; package IDs, raw provider data and diagnostics remain one level deeper.
7. **Prefer real status over scores.** There is no health percentage, speed score, threat count or “fix all” promise.
8. **Stay recognisably Windows.** System typography, command bars, teaching tips, NavigationView patterns and standard dialogs should feel native without copying Windows branding.

## 3. Identity

### 3.1 Selected logo: Steward Loop

The selected mark is an open rounded loop around three ordered tiles:

- loop: stewardship, continuity and bounded responsibility;
- opening: transparency and user control;
- tiles: inventory, evidence and approved action;
- calm blue: technical trust without a security-product claim.

Use `assets/svg/logo-mark.svg` as the source. The mark remains distinct at 16 px because it has one strong silhouette, two white masses and one pale accent. Do not add a shield, broom, speedometer, rocket, hazard symbol, sparkles or Windows pane motif.

### 3.2 Brand architecture

`PCSteward` remains the technical/package/data identity. “PC Steward” is the replaceable display name. Keep display copy and visual assets centralised so a later approved brand change never renames execution authority or application data roots.

### 3.3 Clear space and minimum size

- Clear space: at least 25% of the mark width on all sides.
- Minimum UI size: 16 × 16 px with the simplified silhouette.
- Minimum marketing size: 24 × 24 px.
- Lockup minimum width: 136 px.
- Never recolour individual internal tiles outside the approved monochrome/high-contrast variants.

## 4. Themes and colour

Use semantic tokens. Palette values exist only to construct semantic tokens and design exports.

### 4.1 Light theme

| Role | Token | Value | Use |
|---|---|---:|---|
| App canvas | `color.canvas` | `#F3F6FA` | Window background |
| Primary surface | `color.surface` | `#FFFFFF` | Cards, tables, dialogs |
| Subtle surface | `color.surfaceSubtle` | `#F7F9FC` | Grouping and inset regions |
| Border | `color.border` | `#D7DEE8` | Decorative cards and dividers; interactive controls use `borderStrong` |
| Primary text | `color.textPrimary` | `#172033` | Headings and body |
| Secondary text | `color.textSecondary` | `#526077` | Supporting text |
| Brand/action | `color.brand` | `#1F62D1` | Primary action, selection, focus |
| Success | `color.success` | `#17663F` | Completed/verified outcome |
| Attention | `color.warning` | `#7A5000` | Review needed, stale evidence |
| Failure/destructive | `color.danger` | `#9C261D` | Actual failure or destructive action only |

### 4.2 Dark theme

| Role | Token | Value | Use |
|---|---|---:|---|
| App canvas | `color.canvas` | `#0E1522` | Window background |
| Primary surface | `color.surface` | `#171F2D` | Cards, tables, dialogs |
| Subtle surface | `color.surfaceSubtle` | `#1D2736` | Grouping and inset regions |
| Border | `color.border` | `#364357` | Decorative cards and dividers; interactive controls use `borderStrong` |
| Primary text | `color.textPrimary` | `#F2F5F9` | Headings and body |
| Secondary text | `color.textSecondary` | `#BBC5D4` | Supporting text |
| Brand/action | `color.brand` | `#8FB7FF` | Primary action, selection, focus |
| Success | `color.success` | `#7AD6A3` | Completed/verified outcome |
| Attention | `color.warning` | `#F0C86A` | Review needed, stale evidence |
| Failure/destructive | `color.danger` | `#FF9A91` | Actual failure or destructive action only |

Dark theme is authored, not inverted. Elevation relies on border plus restrained shadow. Avoid pure black surfaces and pure white text blocks.

### 4.3 Semantic colour rules

- **Blue** means selected, actionable or informational; it never means “healthy.”
- **Green** means a completed, evidenced outcome such as verified update success. Do not colour the whole Overview green.
- **Amber** means attention or review, not danger.
- **Red** is reserved for an actual failed operation, an invalid condition that blocks a requested action, or the final destructive confirmation. Minor findings never use red.
- **Grey/slate** means neutral, unavailable, local-only, unknown or disabled.
- Every semantic colour is paired with an icon and explicit text.
- Selection colour and severity colour must not be the only differentiator in the same row.

### 4.4 Severity model

The product does not use “critical” for ordinary maintenance.

| Level | Label | Meaning | Action pattern |
|---|---|---|---|
| S0 | Information | Useful context; no action expected | Secondary link only |
| S1 | Opportunity | Optional improvement with evidence | Review action |
| S2 | Attention | User decision or stale/incomplete state | Primary review action |
| S3 | Blocked | A requested task failed or cannot safely continue | Safe recovery action |

Security severity requires a future authoritative security source and is not inferred from maintenance data.

### 4.5 Confidence model

Confidence describes the product-to-source match, not risk:

- **Verified match:** exact structured source identity and compatible target.
- **High confidence:** strong deterministic evidence; review remains explicit.
- **Review required:** ambiguity, disagreement or missing stable identity; non-executable.
- **Low confidence — no action:** weak resemblance only; always non-executable.
- **Unmatched:** no acceptable source evidence.

Do not abbreviate confidence to a coloured dot. The full label appears wherever an action may be expected.

## 5. Typography

Use Windows system fonts. Do not bundle a web font in the desktop app.

| Style | Family | Size / line | Weight | Use |
|---|---|---:|---:|---|
| Display | Segoe UI Variable Display | 32 / 38 px | 600 | First-run and major empty-state headline |
| Page title | Segoe UI Variable Display | 24 / 30 px | 600 | One per page |
| Section title | Segoe UI Variable Display | 18 / 24 px | 600 | Card and panel groups |
| Body large | Segoe UI Variable Text | 16 / 24 px | 400 | Introductory summary |
| Body | Segoe UI Variable Text | 14 / 20 px | 400 | Default content |
| Body strong | Segoe UI Variable Text | 14 / 20 px | 600 | Labels and emphasized facts |
| Small | Segoe UI Variable Text | 13 / 18 px | 400 | Table metadata |
| Caption | Segoe UI Variable Text | 12 / 16 px | 400 | Timestamps and compact annotations |
| Code/data | Cascadia Mono | 12 / 18 px | 400 | Package IDs, correlation IDs and sanitized logs |

Rules:

- Sentence case everywhere.
- Avoid all caps except short platform-native abbreviations such as UAC.
- Body text target line length: 45–80 characters.
- Numeric summary counts use tabular figures when available.
- Do not reduce body copy below 12 px at 100% scale.
- Timestamps use the current Windows locale. Precise ISO timestamps belong in diagnostics, not the primary surface.

## 6. Spacing and layout

### 6.1 Grid

Base unit is 4 px; most layout uses an 8 px rhythm.

- Inline gaps: 4 or 8 px.
- Control groups: 12 px.
- Card internal spacing: 16 or 20 px.
- Section gap: 24 or 32 px.
- Page edge: 24 px below 1100 px window width; 32 px at larger widths.

### 6.2 Desktop shell

- Minimum supported window: 900 × 640 px.
- Design reference: 1366 × 820 px.
- Expanded navigation: 232 px.
- Compact navigation: 56 px below 900 effective px; expanded navigation resumes at 1100 effective px.
- Page content max width: 1440 px.
- Main content has a 720 px practical minimum; tables switch to stacked row cards below that point.
- A right detail pane is 360–420 px and overlays rather than compresses content below 1180 px.
- Dialogs: 440 px small, 560 px standard, 720 px complex review.

### 6.3 Information density

Default density is comfortable:

- command/control height: 40 px;
- compact secondary controls: 32 px;
- table row: 52 px, optionally 44 px in user-selected compact mode;
- pointer target: never below 24 × 24 px; primary action target is at least 32 × 32 px;
- no card-in-card nesting beyond one inset evidence group.

## 7. Shape, border and elevation

| Element | Radius | Border | Elevation |
|---|---:|---|---|
| Small chip/icon button | 4 px | 1 px when outlined | none |
| Text field/button | 8 px | 1 px | none |
| Card/table container | 12 px | 1 px | level 1 |
| Flyout/teaching tip | 12 px | 1 px | level 2 |
| Dialog/detail pane | 16 px | 1 px | level 3 |
| Pill/status chip | full | 1 px or subtle fill | none |

Use borders to communicate boundaries before shadow. Shadows must disappear in high contrast and never be necessary to understand hierarchy.

## 8. Focus and selection

- Keyboard focus: 2 px `color.focus` outline with 2 px offset.
- Focus must remain visible over brand, danger and dark surfaces.
- Do not remove the system focus visual unless replacing it with the specified equivalent.
- Selected navigation item uses a 3 px leading indicator, subtle brand surface and semibold label.
- Selected table rows use brand-subtle background plus checked selection control; never background colour alone.
- Focus follows a opened dialog or detail pane and returns to the invoking control on close.

## 9. Iconography

### 9.1 Style

- 20 px default canvas; 1.5–2 px optical stroke.
- Rounded caps and joins.
- One metaphor per action.
- Filled icons only for selected navigation or compact status emphasis.
- Use Windows/Fluent system icons in production where a direct semantic match exists.
- Use original assets only for product identity, empty states and PC Steward-specific concepts.

### 9.2 Required text accompaniment

Status and confidence icons always have visible text. Icon-only buttons require:

- an accessible name;
- a tooltip after a short delay;
- at least 32 × 32 px hit area;
- no ambiguous destructive action.

### 9.3 Prohibited metaphors

No broom, rocket, gauge/speedometer, skull, siren, exploding disk, bug, hazard triangle for routine status, padlock implying encryption, or shield implying antivirus protection.

## 10. Data visualisation

Charts answer a question; they do not decorate.

- Overview uses counts and ordered bars rather than a single score.
- Storage uses a segmented bar/ring with a text legend and exact sizes.
- Scan progress uses named phases plus elapsed time.
- Startup impact uses measured/declared evidence with text labels; never a speculative red “impact score.”
- Use at most five categorical hues; add direct labels and patterns/markers in high contrast.
- Interactive chart regions must have a list/table equivalent.
- Do not animate historical charts on load.

## 11. Motion

Motion is quiet, short and interruptible.

| Interaction | Duration | Behaviour |
|---|---:|---|
| Hover/focus colour | 120 ms | opacity/colour only |
| Pane/flyout open | 180 ms | fade + 8 px translate |
| Page content change | 180 ms | cross-fade, no large slide |
| Dialog open | 180 ms | fade + 0.98 scale |
| Toast | 240 ms | fade + 8 px translate |
| Determinate progress | data-driven | no elastic easing |

Rules:

- No confetti, bouncing warnings or looping decorative animation.
- A scan may use a subtle continuous indicator only while work is real.
- Paused, cancelled and stalled states stop animation and show text.
- Respect “Animation effects” and `prefers-reduced-motion`; reduce transitions to 0–50 ms and remove transforms.
- Never use motion to delay a safe exit or confirmation.

## 12. Windows scaling and responsive behaviour

- Test at 100%, 125%, 150%, 175% and 200% display/text scaling.
- Use effective pixels and flexible Grid definitions; never scale the window as a bitmap.
- Text wrapping is preferred to truncation. Package IDs may use middle ellipsis with a copy action and accessible full value.
- At 200%, command bars wrap into an overflow menu; the primary action remains visible.
- Tables become stacked records when columns cannot preserve readable labels.
- Never position a primary action solely in a fixed bottom area that can move off-screen.
- Minimum window state remains operable at 900 × 640 px and 200% text scale, using scrolling where required.

## 13. High contrast

Use system colour resources when high contrast is active:

- Canvas/CanvasText for surface and text.
- Highlight/HighlightText for selection and primary action.
- 2 px borders for cards, inputs, chips and dialogs.
- Remove shadows, translucent scrims, gradients and colour-only chart segmentation.
- Preserve every status label, icon and state announcement.

Do not attempt to reproduce the authored light/dark palette in high contrast.

## 14. Copy and content design

### 14.1 Voice

Calm, factual, specific and non-judgmental.

Preferred:

- “3 updates are ready to review.”
- “Source data is 2 days old.”
- “No approved source was found.”
- “PC Steward will remove 640 MB from its own download folder.”
- “Nothing has been changed.”

Avoid:

- “Your PC is at risk.”
- “Fix critical issues now!”
- “Boost performance.”
- “Dangerous junk detected.”
- “100% healthy.”
- “One click to clean everything.”

### 14.2 Button labels

Use verb + object:

- `Scan this PC`
- `Review 3 updates`
- `Clear scan data`
- `Clear downloaded files`
- `Factory reset PC Steward`
- `Cancel scan`

Do not use `OK` when a specific verb is possible. Destructive dialogs default focus to `Cancel` or another non-destructive action.

### 14.3 Evidence language

- **Live:** completed against enabled sources in this run.
- **Cached:** last successful source result, with timestamp.
- **Local-only:** no update source contacted.
- **Unavailable:** source did not provide a result; show whether cached data remains.
- **Verified:** deterministic match evidence, not a promise that an installer will succeed.

## 15. Asset and implementation governance

- `design-tokens.json` is the design source. WinUI resources should be generated or transcribed into one central `ResourceDictionary`.
- No production screen may introduce an unreviewed colour, radius or text style.
- All logo/display-name references come from central app identity resources.
- The selected mark requires product-owner and trademark review before external distribution.
- Future startup/storage/general-maintenance modules must use the same system but remain product-gated as described in `UX_SPECIFICATION.md`.
