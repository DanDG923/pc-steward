# Private-beta known limitations

- The candidate is a standalone, standard-user-only package. Machine-scope updates are visible for awareness but cannot be reviewed or executed; the elevated helper is not shipped.
- The extracted folder is caller-writable and therefore does not provide installed-binary tamper resistance. External distribution still requires approved signing, hashes and a trusted private channel.
- Package removal is explicit: run the exact scheduled-task cleanup script and delete the extracted folder. Per-user data is retained unless the user first completes the in-app two-confirmation factory reset.
- On-machine extraction/launch, replacement, task cleanup, removal and factory-reset observations remain required. VM execution is excluded by product-owner direction.
- `PO-EXC-29-PERF-01` defers `NFR-PERF-05` / `AC-NFR-06`; the five-minute idle budget is not a pass claim.
- Provider-disconnect cancellation, same-user substitution of the name-based temporary WinGet export, imperfect package-ID/process-name correlation and replay of the historical outcome dialog remain bounded residual risks.
- The private beta has no automatic self-update, public-source claim, generic cleaner, registry cleaning, driver/firmware/Windows Update path, arbitrary download path, unattended installation or privileged service.
- Optional `AC-UX-01–09` accessibility evidence remains best effort under ADR-021.

Task 30A must classify every remaining observation as Proven, Missing or explicitly excepted before Task 31.
