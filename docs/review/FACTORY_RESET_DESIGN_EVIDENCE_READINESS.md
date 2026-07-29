# Factory-reset design evidence readiness

**Task:** 26A — evidence only; no reset implementation or design verdict.

| Design input | Evidence | Status | Exact Task 27 action |
|---|---|---|
| Owned state root | `App.xaml.cs` and scheduled host derive `%LocalAppData%\\PCSteward`; ADR-019 assigns database/state, source metadata, logs, settings/consent, reset marker and `downloads`. | Proven | Freeze a compiled category manifest; do not derive targets from SQLite. |
| Retained categories | ADR-019 excludes user-chosen exports and Windows/WinGet/Store/provider caches; ADR-013 preserves binaries. | Proven | Put every delete/preserve item in the first confirmation. |
| Active-work lock | `MainWindow` disables data controls while scan, update or cleanup is active; component architecture requires exclusive data lease. | Proven | Specify the reset lease and refusal/Cancel recovery. |
| Current-user scheduled task | `ScheduledCheckRegistration` owns only `PCSteward.ScheduledCheck`; ADR-013 requires removal before success. | Proven | Include idempotent removal and partial-result reporting in manifest. |
| Restart/handle sequence | ADR-013 selects fixed marker, close UI, then next-launch cleanup before SQLite/log opening; residuals retain reset-pending state. | Proven | Define marker creation, handle closure, next-launch verification and residual UI. |
| Reparse/path resistance | ADR-019 and threat model require canonical fixed roots, non-following deletion, and rejection of UNC/device/ADS/prefix/reparse/mount escapes; storage fixtures cover cleanup roots. | Proven | Apply identical hostile-path matrix to every reset category. |
| Disposable hostile tests | Existing symbolic-link/mount/race evidence is approved-fixture-only, not a reset replay. | Missing | Add reset-manifest fixtures under disposable allowlisted roots before Task 28. |
| UI contract | Design inventory/component/accessibility sources require category cards, separated danger zone, Deletes/Preserves, two dialogs, exact `RESET`, Cancel default, no Enter danger action, high contrast/200%, pending/partial state and focus/announcement recovery. | Proven | Task 27 must grade this contract against the compiled manifest. |

## Gate disposition

Task 27 may begin only as the design gate: the remaining hostile reset-manifest fixture is an explicit Task 28 implementation/evidence action, not permission to implement reset now. No destructive command, schedule registration, or reset action ran for Task 26A.
