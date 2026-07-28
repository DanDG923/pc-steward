# Task 90 — P1RR remediation evidence

**Recorded baseline:** physical Windows 11 Home x64, version/build 10.0.26200.0 / 26200; AMD Ryzen 7 9800X3D (16 logical processors); 31.15 GiB RAM; healthy NVMe SSDs present. Active plan was `3XS-Overclock`. This is a recorded physical Windows baseline; a VM was not used or required.

## Remediation verification

- P1RR-01: SQLite now fails closed for a legacy persisted WinGet `true` without a separate source-disclosure approval flag. Integration coverage pre-seeds the legacy value; only an explicit approved state enables the provider. The desktop UI required explicit consent, rendered the approved mode without local-only labels, and completed an approved read-only WinGet inventory collection.
- P1RR-02: provider cancellation now contains post-timeout faults. If a provider has not quiesced after the bounded grace interval, the scan fails without publishing and the scan lease remains held until that provider finishes; a replacement scan is rejected in the interim. Regression coverage verifies both late fault containment and replacement blocking.
- P1RR-04: publisher and status filters, source/scope/sort controls and stable accessible labels are present. View-model coverage proves combined filtering, sorting and filter-state preservation.
- P1RR-05/06: typed publisher/date/path/channel/classification persistence, bounded raw-diagnostic access, structured allowlisted correlation/severity events, an application workflow boundary and a desktop boundary test are present.

## Live desktop evidence

- High-contrast desktop rendering exposed connector, scan, cancel, search, status, publisher, source, scope and sort controls.
- User-approved WinGet testing: `winget list --disable-interactivity` completed successfully in 2.943 seconds without retaining its output.
- The final desktop build completed an approved WinGet-inclusive scan in 21 seconds. Its status and start action explicitly identified “approved WinGet”; no local-only label was used.
- A restart loaded the persisted result and presented `Cached Windows-local inventory plus approved WinGet installed inventory`, demonstrating successful promotion and restart recovery.
- Initial post-scan UI projection was CPU-bound. The workflow had already committed the scan; `LoadInventory` was changed from per-product whole-instance scans to a single grouped lookup. After rebuild/restart the cached inventory loaded immediately.

## Follow-up idle measurement (2026-07-28)

- Build: `f14f87f879e6b33d14ea7313bc63f4efe1772018`; process id 36508.
- Interval: 2026-07-28T12:50:27.2481478Z–2026-07-28T12:55:55.5411173Z (5m 28s).
- CPU: 89.453125 to 89.984375 seconds (0.53125 CPU-seconds over the interval); working set: 209,887,232 to 202,076,160 bytes (-7,811,072 bytes); threads: 57 to 57; process responsiveness: true at both captures.
- The desktop-control layer timed out during the next interactive scan input and then returned an inconsistent window screenshot. No further UI input was issued, and no result from that input is claimed.

## Final-app repeat-scan and recovery evidence (2026-07-28)

- Remediation commits: `15ebe40` keeps provider/persistence continuations off the WinUI dispatcher; `82f4a79` constrains the inventory list viewport to preserve virtualization.
- Keyboard-driven local scan: 30 final-app scans were started through the focused Start action with WinGet and MSIX disabled. Every run returned to the ready `Cached Local-only Windows inventory` state. Retained observed-ready intervals (ms): `4149, 1870, 1871, 1853, 1858, 1859, 1870, 1867, 1867, 1856, 1871, 1862, 1861, 1890, 1866, 1862, 1870, 1860, 1866, 1859, 1854, 1893, 1877, 1860, 1868, 1884, 1859, 1860, 1868, 1871`. Median 1867 ms; p95 1893 ms; first-run observation 4149 ms.
- The pre-remediation desktop run did reproduce a stuck `Publishing` UI after persistence completed. The `15ebe40` build then completed a keyboard-started scan and returned to ready state; the 30-run sample above was collected from that repaired execution path.
- Restart recovery: after an intentional process termination attempt, a fresh desktop launch loaded the latest local-only cached inventory and presented a ready scan action. The attempted local scan completed too quickly to prove it was terminated mid-scan, so this is restart recovery evidence only, not termination-during-scan evidence.
- Idle/process-tree check on `82f4a79`: the 15-second spot check recorded 0 CPU-seconds, -237,568 working-set bytes, 65 threads and `Responding=true`. The five-minute interval from 2026-07-28T13:22:32.0842525Z to 2026-07-28T13:28:32.4138550Z instead accumulated 141.71875 CPU-seconds (28.90625 to 170.625); working set changed from 145,006,592 to 145,653,760 bytes; threads stayed at 60; response stayed true; process tree had zero child processes. This does not meet an idle-efficiency claim.

## Remaining mandatory-evidence limit

The 30 final-app local scan samples and process-tree snapshot are now recorded. The current evidence still does **not** pass the fresh Task 15 gate: full keyboard-cancel timing was not captured because local scans complete before a reliable cancel interaction; intentional termination during an active scan was not demonstrated; a text-scaling exercise requires a user-controlled Windows setting; and the final five-minute idle CPU result fails. A fresh GPT-5.6 Sol / High Task 15 review is required after any further remediation.
