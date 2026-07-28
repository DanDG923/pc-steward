# Test strategy

**Status:** Accepted Phase 0 baseline

## Principles

- Test deterministic rules before wiring adapters.
- Negative tests are mandatory for every execution, privilege, source-trust and deletion boundary.
- Fixtures are immutable, redacted and safe; no real update, UAC or destructive host test without explicit approval.
- Narrow tests run first, then solution build/full suite. Gates independently review evidence.
- Every mandatory criterion ends as Proven, Failed or Unverified; unexecuted manual evidence is never Pass.

## Test layers

| Layer | Focus |
|---|---|
| Domain unit | Normalization, stable identity, version comparison, matching/confidence, compatibility, policy and state transitions |
| Contract | Provider/connector schema bounds, capability negotiation, result categories and sanitized diagnostics |
| Persistence integration | Migrations, transaction rollback, staging promotion, corruption handling, retention and stable decisions |
| Windows adapter | Registry/AppX fixtures, WinGet parser/process fixtures, scheduler definitions and notification routing |
| Security negative | Command/path/flag rejection, IPC caller/nonce/replay, database tampering, source fuzzing, reparse/TOCTOU fixtures |
| End-to-end fixture | Scan→match→review→typed job→simulated execution→verification; clear/reset/export flows |
| Performance/reliability | 30 warmed local runs, 20 healthy online runs, CPU/memory, cancellation, crash, timeout and bounded concurrency |
| Manual/VM | Packaging, install/upgrade/uninstall, harmless UAC, Store behavior, clean-VM reset and reparse capabilities |

## Fixture matrix

At least 300 deterministic provider records covering normal, malformed, missing, duplicate, user/machine, x86/x64, runtime/system, prerelease/channel conflict, unknown/incomparable version, exact/high/medium/low/unmatched mapping and deliberate false-match cases. Include hostile strings, oversized fields, quote/metacharacter data, altered SQLite rows, locked files, nested roots and reparse attempts. Golden expected outcomes must preserve separate instances and produce zero false-positive executable mappings.

## Safety-critical suites

- `ExecutionBoundary`: arbitrary executable, shell command, URL, flag, source and package-ID rejection.
- `PrivilegeBoundary`: ACL/caller/session, nonce/replay, mutation, timeout, denial, cancellation and one-shot exit.
- `DeletionBoundary`: relative/UNC/device/alternate-root/prefix/reparse/mount/TOCTOU, locked file, idempotence and residual reporting.
- `SuccessSemantics`: non-zero exit, exit-zero/no-version-change, restart-pending and post-scan mismatch.
- `TrustAndMatching`: malformed/stale metadata, architecture/channel/scope conflicts and zero executable false positives.

These suites are mandatory at Phase 3 and final gates.

## Performance protocol

Use a fixed Windows 11 x64 baseline of at least 4 vCPU, 8 GB RAM and SSD-equivalent storage with fixed power plan. Capture OS/app/runtime/build IDs and process-tree metrics. Report raw machine-readable runs, median and p95; never discard failed/cancelled samples without explanation. Measure cold start, five-minute idle, local and online scans, cancellation acknowledgement/recovery, connector isolation, concurrency, scheduled exit and artifact quota. Spike results establish the harness, not final application compliance.

## Reliability and recovery

- Kill during staging write: previous inventory remains readable.
- Cancel each provider/connector phase: operable state within budget.
- Crash/restart each update state: completed evidence persists; unstarted work does not execute.
- Corrupt/missing database fields: reject authority and recover/report safely.
- Connector timeout/failure: other results publish; stale snapshot is labelled.
- Repeat clear/reset: idempotent, accurate and no preserved category is accidentally removed.

## Evidence ownership

| Gate | Mandatory evidence |
|---|---|
| Task 05 | Spike reports support every accepted ADR and unresolved risk is explicit |
| Task 15 | Inventory coverage, no `Win32_Product`, local-only/no elevation, persistence/clear-scan and local performance |
| Task 20 | Version/matching matrix, zero false-positive actionability, cache freshness and source isolation |
| Task 25 | Runner/helper/deletion negative suites, verification semantics and approved VM evidence |
| Task 31 | All acceptance criteria, performance raw data, packaging lifecycle, SBOM/scans and residual-risk acceptance |

## Tooling guardrails

Task 06 selects the test framework and build commands. Tests must run offline where fixtures suffice, use disposable roots/databases, avoid global machine changes, and redact machine identity. Static analysis treats warnings as errors for production projects. Coverage percentage alone never substitutes for boundary and acceptance evidence.
