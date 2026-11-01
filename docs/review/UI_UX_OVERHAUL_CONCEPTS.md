# PC Steward — UI and UX overhaul: five concepts for owner review

Status: **proposal only.** No code changes. This is a planned follow-up to Task 50, requested by the owner on 2026-11-01. The owner chooses a concept, or a mix, before any implementation. The chosen direction then becomes its own numbered, scoped task with its own branch and PR.

Evidence base:
- [UI audit](UI_UX_OVERHAUL_AUDIT.md): a read-only review of the current Desktop, with file:line evidence.
- [Design research](UI_UX_OVERHAUL_RESEARCH.md): Fluent 2 and Windows 11 guidance, accessibility, comparable software patterns, and trust UX. Claims marked "(S)" there come from search summaries and must be re-checked before they are quoted.
- Visual mockups of every concept, two screens each, on the "PC Steward UI overhaul concepts" design canvas (owner-private link given in chat).

## 1. What the audit found (short)

The current UI is built entirely in code in `MainWindow.cs` (about 1,600 lines) and rebuilds the whole window after nearly every action.

**High-severity problems:**
- **Dead ends:** a highlighted parent navigation item can't be clicked to go back, only the app detail page has Back, and the Repository apps page has no way in at all.
- **Flattened outcomes:** inconclusive, cancelled, UAC-refused and timed-out runs all say "Update failed", but UJ-04 requires "verification inconclusive" as its own state.
- **Spec items not shown:**
  - confidence, channel and review reasons are computed but never displayed (FR-UI-04, §17.2);
  - there is no completion view with Delete downloaded files (FR-DAT-09);
  - Data & reset is not split into cards (§17.3);
  - there is no diagnostic copy (FR-HIS-05).
- **Contradictory elevation wording:** About, the consent dialog and the code tell different stories.
- **The final confirmation is a single wall of text.**
- **Focus and scroll are lost on every rebuild.**

**Medium-severity problems:**
- up to five names for the same state;
- raw enum names in filters;
- "Approved" before anything has been approved;
- raw bytes as sizes;
- internal jargon ("canonical", "split token", "lib-bad");
- a mixed save model in Settings;
- small grey text just under 4.5:1 contrast.

## 2. Foundations in every concept (non-negotiable)

These fix the audit's high findings and keep the protected behaviours (`docs/PROTECTED_UI_BEHAVIOURS.md`, audit §6). Every concept below assumes them.

1. **Real navigation.** `Frame`/`Page` navigation with a back stack, `ItemInvoked` handling and a Back button. Every page is reachable and can be left. View-model binding with incremental updates replaces whole-window rebuilds, so focus and scroll survive.
2. **One status vocabulary.** The SPEC §7.2 labels map to one status chip (icon + text + colour, never colour alone). Filters use the same labels.
3. **Truthful outcomes.** Four final states:
   - **Verified**: the new version was confirmed;
   - **Restart required**: a reliable restart-pending signal;
   - **Installed, not verified**: verification failed or was inconclusive;
   - **Failed**: plain cause, next step, Retry.

   A refused UAC prompt returns quietly to the review with the plan intact; it is not a failure. Progress stays indeterminate with text unless a real percentage exists, and there is no "Cancel update" mid-run.
4. **A structured review sheet** (§17.2, FR-UI-03/04). It is laid out in this order:
   - a facts table: installed → new version, publisher, source, package ID, scope, architecture, channel;
   - confidence in words, for example "Matched by package ID and publisher", with the evidence behind an expander;
   - risk callouts: running app, likely restart, admin needed;
   - one specific primary button ("Update 4 apps"), with the UAC shield only when elevation is really needed. Cancel is the default.
5. **A completion view.** Per-app outcome, the verification evidence, restart guidance, and **Delete downloaded files** with the recoverable size.
6. **Data & reset as separate cards** (§17.3, FR-DAT-06–15):
   - scan data, downloads, source metadata, logs and history each get a card with size, last-cleaned time and what is kept;
   - **Factory reset PC Steward** sits in a separated danger zone: type `RESET`, no Enter activation, locked out during active work.

   The exact labels are kept.
7. **Decisions** (FR-POL-01–05): Pin, Ignore (indefinitely or until a date) and Skip this version, each with an optional reason, all reversible from one view. Wording makes "skip this version" and "ignore this app" clearly different.
8. **Freshness is always in words:** live, cached, local-only, offline or unavailable (FR-UI-05).
9. **Copy.** Sentence case, plain and blame-free, with technical codes inside an expander. One consistent elevation story: the desktop never elevates; UAC appears once per approved job, only when a machine-scope update needs it. Human-readable sizes and dates. No health score, fake urgency or "unsafe" label, and unmanaged does not mean unsafe (§17.5).
10. **Accessibility** (best effort under ADR-021, aimed at AC-UX-01–09):
    - heading semantics;
    - live announcements when scans and jobs finish;
    - accessible names that contain the visible label;
    - F6 to move between panes;
    - access keys;
    - selectable evidence text;
    - high contrast mapped to system colours;
    - text scaling to 225%;
    - targets of at least 24 px;
    - 4.5:1 text contrast.
11. **Native look.** Mica on the base layer and a custom 32 px title bar, the Segoe UI Variable type ramp, a 4 px grid, 4 px control corners and 8 px overlay corners. Standard WinUI controls only: no third-party table without an ADR and dependency approval.
12. **The Chocolatey reviewed route** stays shown-but-unavailable, with Confirm disabled, until owner admission.

## 3. The five concepts

Each concept rests on the foundations above. They differ in **what the app puts first** and in **information density**.

### A · Calm Native — "Windows 11 Settings for your apps"

- **Big idea.** Make PC Steward feel like part of Windows. Familiar layout means instant trust and the lowest learning cost.
- **For.** Everyone; the safe default.
- **Information architecture.** Left NavigationView (Auto mode):
  - **Home**
  - **Updates**
  - **Installed apps**
  - **Activity**
  - **Decisions**
  - footer: **Settings**, with Data & reset as a subpage, and **About**.

  This is SPEC §17.1's recommended set without a separate Unmanaged item. Unmanaged is a filter in Installed apps.
- **Home.**
  - A status hero: one sentence ("4 updates ready · last checked 10 minutes ago · WinGet live"), a **Check for updates** button and a **Review updates** button.
  - SettingsCard-style tiles below it: Restart required, Needs attention (not verified or failed), Needs review (low confidence), Pinned/ignored, Recent activity.
  - Each tile navigates to its filtered page.
- **Update flow.** Updates is a grouped list. The user selects items, then **Review** opens a full-page review sheet. **Update n apps** brings up UAC if needed. A non-modal job page follows, then the completion view.
- **Visual.**
  - Fluent defaults: Mica, accent blue, white cards on the light layer, generous spacing.
  - Feels like Settings and Windows Update.
  - Light, dark and high contrast all come for free.
- **Pros.**
  - Fastest and safest to build.
  - Best accessibility.
  - Easiest to keep consistent.
  - Reviews and tests stay simple.
- **Cons.**
  - Least distinctive.
  - Power users still lack density.
  - Evidence detail sits one click deeper.
- **Effort and risk.** About 2–3 implementation slices. The UI contract tests change deliberately: navigation items, threshold and copy.

### B · Update Queue — "one sentence, one queue"

- **Big idea.**
  - Organise the app around the job people come for.
  - The Updates page *is* the home.
  - Updates are split into three groups, and only one group can be acted on:
    - **Ready** (can be selected);
    - **Needs review** (low confidence, or an architecture or channel conflict; shown with the reason and **no update control**);
    - **Ignored and skipped**.

  This makes "shown but not actionable" structural rather than a greyed-out button.
- **For.** Non-experts who want "keep my apps current" with as few decisions as possible.
- **Information architecture.**
  - Top navigation, with **Updates** as home, then **Installed apps**, **Activity** and **Settings**.
  - Or a slim left rail; to be decided at implementation.
  - Decisions live inside each group's overflow menu and under Settings › Decisions.
- **Updates page.**
  - A large status sentence and **Update all ready (4)** as the primary button, with the shield shown when needed. That button still opens the review sheet first.
  - Ready rows show the app, installed → new version, a patch/minor/major badge (major highlighted), the publisher and a status chip.
  - The Needs review group explains each item in words.
- **Job.** A queue view with an overall progress bar and per-item states (Queued, Downloading, Installing, Verifying, then the result), followed by the four honest outcomes grouped, plus Delete downloaded files. The job keeps running if the user navigates away, and an optional toast reports progress.
- **Visual.** Friendly and roomy:
  - larger type (Subtitle row titles);
  - app icons instead of initials avatars;
  - a warmer neutral ground with blue accent;
  - big, specific buttons.
- **Pros.**
  - The clearest mental model.
  - The fewest clicks to a safe update.
  - Directly expresses the actionable/non-actionable rule.
- **Cons.**
  - The inventory feels secondary.
  - "Update all ready" needs careful copy so it never reads as unattended updating. It always shows the review sheet, which keeps it honest.
- **Effort and risk.** About 3 slices. The status vocabulary and grouping logic sit in Application view models, so they are portable and testable.

### C · Inspector — "see why, at a glance"

- **Big idea.** A dense, keyboard-first workspace for people who want evidence.
  - A three-pane layout:
    - a compact navigation rail;
    - a sortable, filterable list built from column-aligned ListView rows;
    - a **details pane** showing the evidence: confidence explanation, matched identifiers, source, channel, architecture, the per-product timeline and decisions.
  - A **Ctrl+K command palette** for navigation, filters and actions.
- **For.** Power users, and the owner as maintainer.
- **Information architecture.**
  - Rail: **Overview**, **Apps** (all, updates, needs review, unmanaged and pinned/ignored as saved filters), **Activity**, **Decisions**, **Settings**.
  - The list and details panes stack below 641 epx.
- **Interaction.**
  - Shortcuts: F5 rescans, Ctrl+F filters, F6 moves between panes, Space selects, Ctrl+Enter opens the review.
  - Palette actions *open the same review sheet*; there is never a shortcut past approval.
  - Evidence text is selectable and copyable, and **Copy diagnostic** is in the details pane (FR-HIS-05).
- **Visual.**
  - Compact density (32 px rows).
  - Monospace for versions and package IDs.
  - Dark theme shown in the mockup; light and high contrast supported.
  - Subtle dividers instead of cards.
- **Pros.**
  - The most information per screen.
  - Makes the trust model inspectable.
  - Scales to 300+ apps.
  - Fast for experts.
- **Cons.**
  - Intimidating for non-experts unless paired with the Simple/Advanced switch.
  - The most layout work.
  - Narrow and high-zoom layouts need care.
- **Effort and risk.** About 3–4 slices. Needs a virtualized list with column templates (no new dependency) and keyboard-handling tests.

### D · Guided Steward — "a calm, five-step journey"

- **Big idea.**
  - The update job is a linear stepper: **1 Check → 2 Choose → 3 Review → 4 Update → 5 Verify**.
  - Each step has one clear primary action and a plain-language explanation of what happens next, including exactly when and why Windows will ask for permission (UAC appears once, at step 4).
  - This is the most direct expression of PC Steward's safety model.
- **For.** Occasional and cautious users, and first-time use.
- **Information architecture.**
  - Home is a single "Start a check" card with the last result.
  - The stepper takes over the content area during a job.
  - **Installed apps**, **Activity**, **Decisions** and **Settings** stay in a minimal left navigation.
  - Leaving the stepper keeps the job's state, and the user can resume it.
- **Steps.**
  1. **Check:** scope and freshness, then a read-only scan.
  2. **Choose:** Ready items, with nothing preselected; Needs review items are listed with their reason but cannot be chosen.
  3. **Review:** the structured review sheet for the exact immutable plan.
  4. **Update:** the UAC explanation, then progress.
  5. **Verify:** the four honest outcomes, the evidence, restart guidance and Delete downloaded files.
- **Visual.** Spacious and reassuring:
  - a large step indicator;
  - one column, about 760 px wide;
  - an illustration-free calm palette;
  - strong hierarchy.
- **Pros.**
  - Hardest to misuse.
  - Explains the trust model as it goes.
  - Ideal onboarding.
  - Maps one-to-one onto the immutable job.
- **Cons.**
  - Slower for frequent users; mitigate with a "Skip to review" path once trusted.
  - Less useful as a browsing tool.
- **Effort and risk.** About 3 slices. The stepper state machine belongs in Application, where it is portable and testable.

### E · Honest Ledger — "history is the spine"

- **Big idea.** Trust comes from an auditable record.
  - Home is a **timeline** grouped by day. Every scan, decision, job and verified, unverified or failed outcome is one entry, and expands to the structured details: provider, package ID, target version, exit code and sanitized output (FR-HIS-01/03).
  - A right-hand **Now** column shows what needs action: updates ready, restart required, needs attention.
  - Every app has its own **per-product timeline** (FR-HIS-02).
- **For.** Trust-first users who want to see what happened, and anyone diagnosing a problem.
- **Information architecture.**
  - Left navigation: **Ledger** (home), **Updates**, **Installed apps**, **Decisions**, **Settings**.
  - Activity merges into the Ledger.
  - The app detail page leads with its timeline.
- **Interaction.**
  - Filter the timeline by type or outcome, and search.
  - "Copy diagnostic" on any entry.
  - The retention period is shown.
  - Update actions start from the Now column or Updates and go through the same review sheet.
- **Visual.**
  - Editorial and calm.
  - A vertical timeline rail with outcome glyphs: check for Verified, clock for Restart, half-circle for Not verified, cross for Failed. Text labels always accompany them.
  - Monospace for versions.
  - Generous line height.
- **Pros.**
  - Makes truthful outcomes the centrepiece.
  - Strongest support and diagnosis story.
  - Distinctive.
- **Cons.**
  - Home is less action-oriented.
  - Needs good timeline performance and grouping.
  - New users may want a status summary first; the Now column mitigates this.
- **Effort and risk.** About 3–4 slices. Needs a history read model fit for a timeline (paging, grouping) and checks on the storage and retention UX.

### Add-ons that fit any concept

- **Simple / Advanced density.**
  - Simple hides evidence columns and technical fields.
  - Advanced shows them.
  - It is one codebase with progressive disclosure, which pairs naturally with C or B.
- **Keyboard palette (Ctrl+K).**
  - Navigation, filters and "open review" only.
  - It never commits an update.

## 4. Comparison

| | A Calm Native | B Update Queue | C Inspector | D Guided Steward | E Honest Ledger |
|---|---|---|---|---|---|
| First thing you see | Status hero + cards | Ready / Needs review queue | Dense app list + evidence | "Start a check" + stepper | Timeline + Now column |
| Best for | Everyone | Non-experts | Power users | Cautious / first-time | Trust-first, diagnosis |
| Density | Medium | Medium-low | High | Low | Medium |
| Shows the safety model | Good | Very good (structural groups) | Very good (inspectable) | Excellent (linear) | Very good (auditable) |
| Implementation effort | Lowest (≈2–3 slices) | ≈3 slices | ≈3–4 slices | ≈3 slices | ≈3–4 slices |
| Risk | Lowest | Low | Medium | Low | Medium |
| SPEC §17.1 fit | Close | Needs reconciliation | Close | Needs reconciliation | Needs reconciliation |

**Recommendation.** **A as the base**, with **B's Ready / Needs review / Ignored grouping** on the Updates page and **D's stepper used only for the update job** (Review → Update → Verify). Add the **Simple/Advanced switch** later if power users ask for C's density. This mix fixes every high audit finding at the lowest risk, stays closest to SPEC §17.1, and still makes the safety model visible.

## 5. What happens after the owner chooses

1. Record the choice, and any change to SPEC §17.1's navigation, in a short ADR or planning decision.
2. Open a numbered task with phases, implemented on a new branch and PR:
   - **foundations:** navigation and view models;
   - **review and completion:** the review sheet and the completion view;
   - **Data & reset;**
   - **concept-specific pages;**
   - **accessibility pass.**
3. Each phase follows the usual workflow:
   - portable view-model tests;
   - deliberate updates to `UiRedesignContractTests`, `UpdateProgressUiContractTests` and `ReferenceBoundaryTests`;
   - Windows CI;
   - an independent review.
4. Before release, gather runtime screenshots and a Narrator walkthrough on a real desktop. That is owner-host evidence.
