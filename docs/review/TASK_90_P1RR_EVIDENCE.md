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

## Remaining mandatory-evidence limit

One final-app approved scan and restart were recorded. Thirty retained final-app raw samples, five-minute idle/process-tree telemetry, full keyboard cancel timing, scaling exercise and intentional termination staging were not completed in this task. They remain requirements for the fresh Task 15 gate and are not claimed as passed here.
