# Selected UI direction: Steward Ledger

Selected 2026-08-05 with a weighted score of **9.47/10**.

## Direction

PC Steward becomes a calm native Windows ledger of installed applications, source evidence, user decisions, and verified outcomes. The interface answers three questions in order:

1. What did PC Steward observe?
2. Does anything require my decision?
3. What exactly will happen if I approve an action?

## Production decisions

- Keep WinUI 3, the current application/domain services, persisted schemas, and security boundaries.
- Use five stable principal destinations: Overview, Applications, Updates, Activity, and Settings.
- Keep Scan, Scan results, Review actions, Repository apps, Storage, Privacy & data, application detail, and About as contextual destinations so no capability is removed.
- Replace concatenated inventory strings with virtualized, structured rows and professional fallback marks.
- Use a reusable page header, status badge, freshness label, information banner, empty state, evidence row, activity row, and global operation band.
- Follow the Windows theme through semantic light/dark/high-contrast resources; do not invent a non-persisted appearance setting.
- Use indeterminate progress unless the underlying operation can report a defensible percentage.
- Keep technical evidence available through progressive disclosure and keep raw diagnostics secondary.
- Preserve the separate source-consent, review, final approval, UAC, execution, and verification stages.

## Why it wins

Steward Ledger is clearer than the current implementation, more efficient than a guided wizard for repeat use, and less intimidating than an evidence workbench. It can be implemented incrementally against real data without migrating frameworks, changing persisted state, or weakening the approval model. Its visual restraint also differentiates PC Steward from scareware and generic optimizer dashboards.
