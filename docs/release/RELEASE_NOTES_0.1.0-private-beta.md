# PC Steward 0.1.0-private-beta

**Channel:** private beta — not published  
**Release status:** candidate preparation only; installer lifecycle and signing are unproven.

## Included capability

- Local application inventory, explicit trusted-source checks, review-required decisions, and read-only current-version evidence.
- Explicit, immutable reviewed WinGet update jobs with on-demand elevation only where required and exact post-update verification.
- Scan-only scheduled checks, per-user data controls, factory reset, diagnostics redaction, bounded downloaded-artifact cleanup, and native desktop navigation.
- Private-beta staging script with SHA-256 manifest, dependency inventory and CycloneDX SBOM.

## Upgrade and rollback

This private beta has no automatic updater. A future signed candidate is upgraded manually using the installer only after clean-VM evidence passes. Do not install this unsigned staging output. Rollback requires a separately signed prior installer and must be recorded as a clean-VM lifecycle case.

## Known limitations

See `KNOWN_LIMITATIONS.md`. In particular, the five-minute idle resource requirement is deferred by `PO-EXC-29-PERF-01`, and ADR-002 packaging lifecycle evidence is still incomplete.
