# Private-beta known limitations

The current candidate is not eligible for external distribution. It is an unsigned, locally staged payload until an approved certificate, installer toolchain and clean Windows 11 x64 lifecycle evidence are supplied.

- ADR-002 remains Provisional: fresh install, signed protected root, upgrade, rollback, uninstall retain/remove-data, task cleanup and clean-host helper/UAC behavior are unproven.
- The current host is not a recorded clean Windows 11 acceptance image; the environment report also found no installer or signing toolchain.
- `PO-EXC-29-PERF-01` defers `NFR-PERF-05` / `AC-NFR-06`; the five-minute idle budget is not a pass claim.
- Live external COM/repository evidence, a fresh real update, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback protection and replay of the historical outcome dialog remain bounded residual risks.
- The private beta has no automatic self-update, public-source claim, GitHub Releases connector, generic cleaner, registry cleaning, driver/firmware/Windows Update path, arbitrary download path, or unattended installation.
- Optional `AC-UX-01–09` accessibility evidence remains best effort under ADR-021; clean-VM high-contrast and screen-reader observations are still required for release confidence.

Before any controlled install, provide the evidence items in `PRIVATE_BETA_CHECKLIST.md`; before any release decision, Task 30A must prepare and Task 31 must independently assess the complete evidence package.
