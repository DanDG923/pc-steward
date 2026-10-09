# Threat model

**Status:** Accepted Phase 0 baseline; Tasks 02–04 evidence recorded; review again at Tasks 15, 20, 25 and 31; v0.3 additions TM-19–TM-24 accepted 2026-10-08 (D-007), review again at Task 58
**Protected assets:** correct package-to-product identity, user approval, standard/elevated execution boundary, app-owned and non-owned files, inventory/history privacy, last-known-good state, source provenance and release evidence.

## Security objectives

- No wrong or low-confidence package becomes executable.
- Remote metadata and local database content cannot become commands or deletion targets.
- Elevated capability is authenticated, short-lived and operation-specific.
- Cleanup cannot escape fixed PC Steward-owned roots.
- Success is evidence-backed; cancellation, stale data and partial failure remain explicit.
- Work and storage are bounded against malformed or adversarial input.

## Adversaries and assumptions

Consider a compromised source response, malicious/corrupt local records, a standard-user process attempting helper abuse, crafted filesystem reparse structures, malformed provider output and accidental user action. A local administrator or fully compromised OS is outside the confidentiality boundary, but the design must not make exploitation easier. WinGet/provider integrity is relied upon only through its supported constrained operation; display metadata is never trusted.

## Threat register

| ID | Threat / path | Primary controls | Required evidence |
|---|---|---|---|
| TM-01 | Malicious source metadata injects flags, paths, markup or oversized values | Typed schemas, length/enumeration limits, escaping, no command fields, connector isolation | Parser fuzz/negative fixtures; Task 02/18 |
| TM-02 | Incorrect product match installs another product/channel/architecture | Stable IDs, deterministic evidence, compatibility gate, manual mapping audit, low/medium non-actionability, zero-false-positive fixture | Tasks 04, 11, 17, 20 |
| TM-03 | Arbitrary command execution via UI, SQLite, logs or provider output | No shell, fixed executable, structured argument list, provider/verb/flag allowlist, runner revalidation | Tasks 02, 21, 25 |
| TM-04 | UAC/IPC abuse, spoofed caller, replay or request mutation | Restrictive endpoint ACL, caller/session validation, protocol version, nonce, immutable job binding, single request, signed helper | Tasks 03, 22, 25 |
| TM-05 | Reparse-point, junction, symlink, mount or prefix-confusion deletion escape | Fixed roots, canonical containment, component-aware checks, reparse rejection/non-following handles, hostile fixtures | Tasks 03, 24, 28, 25/31 |
| TM-06 | TOCTOU path swap between validation and deletion/execution | Handle-relative/non-following operations where available, immediate revalidation, deny ambiguous states, isolated-environment test | Tasks 03, 24, 25 |
| TM-07 | Database tampering changes package/job/path | Treat rows as untrusted, no commands, rederive allowlists/roots, immutable job integrity and state transition checks | Tasks 07, 21, 24 |
| TM-08 | Logs/exports leak credentials, paths or personal data | Event allowlist, redaction, bounded output, path opt-in, no environment dump, diagnostic tests | Tasks 23, 30 |
| TM-09 | Stale cache is presented as current or replays downgrade | Retrieved/success/expiry timestamps, visible stale state, refresh policy, downgrade rejection | Tasks 16, 18, 20 |
| TM-10 | Cancellation/crash causes corrupt inventory, duplicate update or false terminal state | Staging/atomic promotion, durable job state machine, idempotency, process-tree cancellation, recovery tests | Tasks 07, 12, 21, 23, 29 |
| TM-11 | Resource exhaustion from records, output, processes, concurrency, logs or cache | Size/count/output limits, timeouts, concurrency caps, one-scan lease, retention/quota, process-tree measurement | Tasks 04, 12, 24, 29 |
| TM-12 | Provider exit 0 or restart ambiguity becomes false Success | Preserve exit, targeted rescan, expected-version comparison, separate Inconclusive/Restart required | Tasks 23, 25 |
| TM-13 | Source/package substitution between review and execution | Immutable reviewed IDs/version/source, freshness policy, revalidation at execution, provider identity | Tasks 21, 23 |
| TM-14 | Scheduled task becomes unattended installer or persistence | Scan-only entry point, fixed task definition, standard user, no update verb, overlap guard, exit test. v0.3: automatic updates use separate opt-in Stage A/B tasks and hosts (ADR-020 revision, ADR-025, TM-20). | Tasks 26, 54, 55 |
| TM-15 | Cleanup deletes active artifacts or provider caches | Ownership ledger, active lease, fixed category, provider-cache exclusion, accurate residual report | Task 24 |
| TM-16 | Factory reset partly completes but claims fresh state | Fixed reset manifest, double confirmation, active-work lockout, marker/transaction design, desktop bootstrap before SQLite, scheduled-host sentinel preflight and blocking partial-reset window | Tasks 03, 27, 28 |
| TM-17 | Malformed inventory/source data corrupts UI/database | Bounded DTOs, encoding handling, transactional writes, per-adapter warnings | Tasks 08–12, 18 |
| TM-18 | Dependency/build compromise | Lock files, minimal dependencies, static/dependency/secret scans, SBOM, signed release | Tasks 06, 30, 31 |
| TM-19 | Tampered, malicious or pathological catalog entry (bundled or user-added) becomes execution authority or hangs detection | Signed bundled payload, strict schema/bounds, no command/switch/path/URL fields, non-backtracking patterns with timeout, live exact resolution before approval, fail-closed catalog disable (ADR-023) | Tasks 46, 47, 53, 58 |
| TM-20 | User-writable per-user database or file drives an unattended elevated install | Stage B reads only the admin-ACL'd `%ProgramData%` policy store, the bundled catalog and live provider evidence; policy written only by the helper after UAC; pause marker restrict-only (ADR-025) | Tasks 55, 58 |
| TM-21 | Batch helper abuse: item substitution, reordering, appending, replay, expiry, oversize frame, cancel race | Batch digest, per-item revalidation, item/duration caps, nonce, one helper session per approval, no additions after approval (ADR-017/018 v0.3 revisions) | Tasks 49, 58 |
| TM-22 | Licence/agreement accepted without the user seeing it | Agreements shown in the review sheet and recorded per package and version; automation only where already recorded (`FR-INS-03`) | Tasks 48, 52 |
| TM-23 | Chocolatey setup or adoption runs a raw script or silently switches an app's installer | WinGet `Chocolatey.Chocolatey` install only, trusted-client checks after setup, adoption only as an explicit labelled Install, no provider switch by automation (ADR-024) | Tasks 50, 58 |
| TM-24 | Automation closes or force-terminates a running app, or updates during use | Eligibility requires the app not running; automation never closes apps; manual close needs consent, and force needs a second per-run consent (`FR-BAT-08`) | Tasks 49, 54, 58 |

## Privilege-boundary protocol constraints

The desktop creates a random per-request named pipe restricted to the current user and SYSTEM, then launches the fixed signed helper path through `runas`. Bootstrap data contains only protocol version, desktop PID, pipe locator and nonce. Each side validates the other's PID, fixed signed executable identity and user/session. The helper accepts one versioned immutable-job message containing operation verb, provider enum, source ID, package ID, target version, job ID and freshness/nonce binding. It rejects unknown/duplicate fields, overlong identifiers, replay, free-form flags, paths, URLs, environment, working directory and output destinations. Denial, cancellation, timeout and helper crash are distinct results. No service installation or persistent elevated process is allowed.

## Deletion security constraints

Reset and cleanup operate from a compile-time/configuration-owned manifest of categories and fixed per-user roots. Paths are resolved without following reparse points; root equality/prefix confusion, alternate streams where relevant, UNC/device paths, relative paths and cross-volume/mount traversal are rejected. Partial deletion is not Success. Tests run only under disposable roots until explicitly approved isolated-environment evidence.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` records 42 passing assertions under a uniquely named disposable root. Fixed-category containment, path-confusion rejection, junction rejection, locked residuals, bounded retry, idempotence, and a final-component junction swap failed closed. A strict authenticated no-op helper message rejected mutation, replay, unreviewed package IDs, unknown/duplicate fields and expiry while preserving distinct cancellation and denial states.

Symbolic-link creation required administrator privilege on this host, and a true volume mount was not attempted. Public path-based handle acquisition also leaves an unproven ancestor-component race. The report therefore defines mandatory isolated-environment symbolic-link, volume-mount and concurrent ancestor-swap cases for Tasks 24/25. No production deletion or elevation claim is made.

## Task 24 evidence

The download cache now resolves only `%LocalAppData%\PCSteward\downloads` (or an isolated test root) from code. SQLite path values are treated as relative evidence, not deletion authority. Cleanup validates containment and every existing component before a second validation followed by a `FILE_FLAG_OPEN_REPARSE_POINT` handle-bound deletion. It rejects relative, rooted, UNC, device, alternate-data-stream, prefix-confusion and reparse paths; active leases are excluded. The focused suite proves quota order, active-artifact protection, seven-day/24-hour retention, idempotence, locked partial cleanup, and outside-sentinel preservation. Symbolic-link, volume-mount and concurrent ancestor-swap evidence remains restricted to the approved disposable-VM phase gate.

## Task 22 evidence

The production boundary now creates a random local-only, single-instance named pipe with an explicit protected ACL granting only the current user and SYSTEM. The desktop launches the fixed helper path through `runas` only after a fresh immutable machine-scope job reaches execution. Bootstrap arguments are limited to protocol version, desktop PID, pipe locator and a 256-bit nonce. Both endpoints bind the pipe-reported peer PID to the launched/requesting process and verify the same user SID, session, fixed executable path, trusted Authenticode signature and same signer.

The helper accepts one length-bounded canonical request, validates nonce and two-minute issuance lifetime, restores the immutable job through its digest, rejects unknown/duplicate/malformed fields and reconstructs a fixed machine-scope WinGet argument array in provider code. It monitors desktop disconnect for process-tree cancellation, returns a bounded sanitized result and exits. UAC denial, cancellation, timeout, output overflow, provider failure, helper failure/crash and completed-provider execution are distinct; completed remains awaiting Task 23 verification rather than Success.

Sixteen security fixtures and four architecture tests pass replay, substitution, stale evidence, malformed/overlong IPC, duplicate request, path injection, bootstrap authority injection, per-user privilege confusion, fixed machine arguments, result binding/sanitization and local ACL/PID behavior. Analyzer verification, the Release build and all 120 tests pass. No UAC prompt or real update was invoked. The current artifacts are unsigned and therefore deliberately fail peer-signature validation; signed UAC acceptance/denial, cross-integrity identity checks, disconnect cancellation and immediate teardown remain mandatory disposable-Windows-11-VM evidence for Tasks 25/30.

## Task 90 P3R-15 evidence

The standalone private-beta desktop composes user-scope update execution only and ships no elevated helper. Machine-scope preparation stops before structured execution authority or immutable job creation, and the standalone executor independently rejects a machine-scope job without invoking a transport. The historical installed-mode path still fails before `runas` unless it is executing from the fixed `%ProgramFiles%\PC Steward` root beside the expected helper, neither file nor any root component is a reparse point, the current token lacks write/delete/DACL/owner authority over the root/files and replacement/delete/DACL/owner authority over higher ancestors, both files have trusted Authenticode signatures, and signer certificates match exactly. That path is dormant and not packaged.

The approved host fixture exercised the real production validator/transport. A protected same-signer layout completed the signed no-op UAC/pipe path. Actual tampered, wrong-signer, caller-writable, helper-reparse and privileged between-validation replacement layouts all failed before the helper launch marker. The fixed root, unique temporary root and both exact certificate thumbprints were absent after teardown. This closes the Phase 3 `TM-04/TM-06` production-boundary evidence gap on the approved host; clean installer lifecycle and unconstrained administrator/OS compromise remain outside this proof.

## Task 90 P3R-14 evidence

The actual desktop now composes an official WinGet COM structured-candidate authority. Only predefined trusted `winget`/`msstore` catalogs, one exact ID match, and bounded exact installed/newer-candidate versions can cross into review. Wrong source/ID, fuzzy/duplicate/limit-exceeded/untrusted results, malformed/equal/older versions, cancellation, timeout or COM/catalog error return no authority. Display-table fields remain non-executable. A current-tree composition test uses the production authority around a controlled COM observation and proves the exact evidence crosses review and runner, then becomes Success only after exact export plus concrete local post-update version/scope/architecture evidence.

## Residual risks requiring evidence

- The provisional constrained WinGet CLI route requires approved isolated-environment inventory/source and parser/process evidence: Tasks 10, 18, 21 and 25.
- The accepted standalone package requires extraction/launch, helper-absence, user-scope-only, replacement/removal, exact task cleanup and factory-reset evidence: Tasks 30/30A.
- Full-walk Windows deletion/TOCTOU primitive, symbolic-link and volume-mount behavior without over-privilege: Tasks 24/25, following Task 03's partial evidence.
- Final-application performance on the accepted recorded baseline: Tasks 12 and 29.
- Provider-disconnect cancellation remains residual evidence. Protected-root cross-integrity evidence is retained only for a future installed deployment; it is not required by the standard-user-only standalone package.

## Review rule

Any arbitrary-execution path, executable false positive, privilege-boundary weakness, deletion escape, false Success or missing mandatory hostile isolated-environment evidence is a release blocker, regardless of functional test count.
