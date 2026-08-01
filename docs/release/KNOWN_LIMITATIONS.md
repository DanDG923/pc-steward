# Private-beta known limitations

- The candidate is a standalone, standard-user-only package. Machine-scope updates are visible for awareness but cannot be reviewed or executed; the elevated helper is not shipped.
- The extracted folder is caller-writable and therefore does not provide installed-binary tamper resistance. External distribution still requires approved signing, hashes and a trusted private channel.
- Package removal is explicit: run the exact scheduled-task cleanup script and delete the extracted folder. Per-user data is retained unless the user first completes the in-app two-confirmation factory reset.
- A controlled current-user VS Code production-path update from 1.129.0 to 1.130.0 is evidenced. It does not establish broader provider, performance, or lifecycle requirements.
- Trusted signing/channel approval, complete agreed-baseline ADR-006 performance evidence, and complete supported-host standalone lifecycle observations remain release blockers. `PO-EXC-90-PERF-BOUNDARY` and `PO-EXC-90-RESET-LIFECYCLE-OMISSIONS` were bound to the prior archive and do not cover the remediated `A8FD4AF1...4CBCA3FC` hash; omitted criteria remain unproven. VM execution is excluded by product-owner direction.
- Provider-disconnect cancellation, same-user substitution of the name-based temporary WinGet export, imperfect package-ID/process-name correlation and replay of the historical outcome dialog remain bounded residual risks.
- The private beta has no automatic self-update, public-source claim, generic cleaner, registry cleaning, driver/firmware/Windows Update path, arbitrary download path, unattended installation or privileged service.
- Optional `AC-UX-01–09` accessibility evidence remains best effort under ADR-021.

The in-app About & support page and these notes intentionally identify blockers; no unsigned or unverified archive is a release candidate.
