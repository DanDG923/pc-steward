# Protected UI behaviours

These behaviours are invariants for the production redesign. Visual or navigation changes must not weaken them.

## Authority and execution

- A local scan is read-only, non-elevated, cancellable, and never implies update approval.
- Enabling a network-backed source requires its existing disclosure and explicit consent. Enabling it starts no scan, check, download, or update.
- Detection, source identification, confidence assessment, user decision, immutable review, explicit approval, execution, and post-update verification remain distinct states.
- Low-confidence, conflicting, stale, unavailable, architecture-conflicting, channel-conflicting, or incomparable evidence is not actionable.
- Selection is not approval. No update action is preselected, silent, unattended, or inferred from navigation.
- One Updates row may stand for several inventory records of the same app. It is selected, reviewed and updated once, and a decision taken on it is recorded for every record it stands for, through the same confirmation.
- The final update dialog shows the exact provider, package ID, installed version, target version, scope, architecture, running-app warning, and administrator requirement derived from the reviewed immutable selection.
- UAC is requested only after final confirmation for a validated machine-scope job. The desktop remains unelevated.
- Provider exit code zero is not success until controlled re-inventory verifies the expected version or a reliable restart-required result is recorded.
- No arbitrary executable, path, argument, URL, database row, remote metadata, or UI field may become execution authority.

## State and persistence

- Existing settings, source consent, schedules, pins, ignores, mappings, exclusions, history, downloads, and scan snapshots remain compatible.
- A failed or cancelled replacement scan does not destroy the prior completed snapshot.
- User-approved mappings remain visibly distinguishable from automatic evidence and remain reversible.
- Closing the app does not leave an unnecessary coordinator, tray process, background service, or elevated process.
- The active WinUI window is retained for its lifetime; startup, reset-recovery, and busy windows must not disappear through garbage collection.

## Data controls

- Clear scan data, clear downloaded files, advanced category clears, and factory reset retain separate scope and confirmation text.
- Routine cleanup is limited to its named PC Steward-owned category and never touches installed applications or provider-owned caches.
- Factory reset retains two confirmations, Cancel as the default, exact case-sensitive `RESET`, no Enter activation, active-work lockout, and next-launch verification before onboarding.
- Exports remain user-initiated and warn that installed-software data may be identifying.

## Product trust

- No health score, fake urgency, fear-based wording, vulnerability claim without a real data source, or red treatment for harmless states.
- Unmanaged (shown as "No update source") means inventoried but not managed; it does not mean unsafe.
- Fresh, cached, partial, offline, unavailable, and local-only evidence remain visibly distinct in text.
- Status never relies on colour alone; label, icon, location, and explanation carry the meaning.
- No drivers, firmware, Windows Update, registry cleaning, broad optimisation, arbitrary web search, telemetry, cloud inventory, or mandatory account is introduced.

## Regression evidence

The redesign must retain focused tests for source consent, state persistence, scan cancellation, review/approval separation, duplicate-operation prevention, update verification, administrator-requirement presentation, reset scope, and protected execution boundaries. Structural UI tests are supplementary and cannot replace executable workflow tests.
