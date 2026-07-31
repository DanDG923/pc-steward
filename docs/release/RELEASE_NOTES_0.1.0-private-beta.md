# PC Steward 0.1.0-private-beta

**Channel:** private beta — not published  
**Release status:** standalone candidate preparation; lifecycle observations and signing remain incomplete.

## Included capability

- Local application inventory, explicit trusted-source checks, review-required decisions, and read-only current-version evidence.
- Explicit, immutable reviewed current-user WinGet update jobs with exact post-update verification. This standalone package never requests elevation and does not ship an elevated helper.
- Scan-only scheduled checks, per-user data controls, factory reset, diagnostics redaction, bounded downloaded-artifact cleanup, and native desktop navigation.
- Private-beta staging script with SHA-256 manifest, dependency inventory and CycloneDX SBOM.

## Upgrade and rollback

This private beta has no automatic updater and no installer. After a trusted channel and signed artifact are approved, replace the complete extracted package from that channel; do not mix files between versions. Machine-scope updates are non-actionable because the standalone package ships no elevated helper. Rollback means replacing the complete package with the approved prior signed ZIP while retaining the per-user data root.

## Known limitations

See `KNOWN_LIMITATIONS.md`. In particular, the five-minute idle resource requirement is deferred by `PO-EXC-29-PERF-01`, and ADR-002 packaging lifecycle evidence is still incomplete.
