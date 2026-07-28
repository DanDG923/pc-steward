# Task 02 — Packaging and WinGet integration spike

**Status:** Evidence collected on 2026-07-28; neither ADR is accepted.  
**Host boundary:** This host reports Windows 10 Home, not the Windows 11 x64 target. No package was built, installed, upgraded, uninstalled, or removed; no source was reset or agreement accepted; no UAC prompt was invoked.

## Commands and results

| Command | Exit | Result | Limitation |
|---|---:|---|---|
| `winget --version` | 0 | `v1.29.280` | Local capability only. |
| `winget --info` | 0 | Microsoft.DesktopAppInstaller `v1.29.280.0`; x64 host; installer download and per-user paths reported. | Paths are intentionally not copied into this report. |
| `winget list --help` | 0 | Advertises source, exact-ID, scope, non-interactive, `--details`, upgrade-available, pinned and unknown flags. | Does **not** advertise `--output`; table output must be treated as locale/version-sensitive. |
| `winget upgrade --help` | 0 | Advertises exact ID, source, version, architecture, scope and non-interactive flags; also exposes installer-affecting flags. | No update command was run. |
| `winget source --help` | 0 | Source mutation verbs are available. | They are prohibited from the product and this spike. |
| Task 00: `winget source list` / `winget list` | 0 / 0 | Default sources and installed records were available. | Evidence is inherited from the environment report; no repeat was allowed because installed-inventory disclosure to package sources was not authorized. |
| Task 00: Microsoft Store source search | non-zero | Microsoft Store search failed. | Treat Store metadata as independently fallible and retain last-good/cache semantics. |

The attempted read-only `list` and `upgrade` metadata probes were not run: the host safety boundary requires explicit approval before installed-software metadata can be disclosed to external package sources. This is an evidence limitation, not a product failure.

## Packaging evidence — OD-02

| Required interaction | Signed MSIX main app | Signed unpackaged app + traditional installer | Evidence and required Windows 11 VM proof |
|---|---|---|---|
| Current-user storage | Plausible; package-local paths were visible for App Installer. | Plausible via a fixed per-user app-data root. | Neither PC Steward deployment model was built on this non-target host. |
| Task Scheduler | Unproven. | Unproven, but conventional current-user task registration remains compatible with the architecture. | Create, run, disable and uninstall a current-user scan-only task. |
| WinGet CLI | Available to the current user. | Available to the current user. | Repeat inventory and source probes in a clean Windows 11 VM. |
| On-demand UAC and separately signed helper | Unproven package-boundary behavior. | Fits the proposed separate executable boundary, but signing has not been tested. | Prove one helper request, declined UAC, and helper exit. |
| Uninstall retain/remove data choice | Unproven. | Unproven. | Prove both choices and that scheduled tasks are removed. |
| Update-helper lifecycle | Unproven. | Unproven. | Prove signed update/rollback and no resident process. |

**Recommendation:** provisionally favour a signed unpackaged application with a traditional installer, because it best preserves the proposed separately signed helper and explicit uninstall-data choice. This is not an accepted ADR: the required Windows 11 clean-VM lifecycle evidence and signing tooling are absent. Retain an MSIX probe only if it can prove the full helper, task, and uninstall requirements without weakening the security boundary.

## WinGet integration evidence — OD-03

| Route | Evidence | Assessment |
|---|---|---|
| Constrained CLI adapter | Current-user `winget.exe` is present at 1.29.280. Help confirms exact ID, source, version, architecture, scope and non-interactive arguments. | Viable only behind a capability gate, fixed executable, structured argument list, bounded process/output capture, timeout/cancellation, and parser fixtures. |
| Supported programmatic route | No supported programmatic route or compatible build tooling was evidenced on this host. | Keep the provider port so this option can be reassessed; do not claim a typed API is available. |

No structured `--output` option appeared in the observed `list` or `upgrade` help. Therefore the adapter must not make table parsing an execution authority. It may retain bounded raw records for diagnosis and must mark unknown/malformed/locale-sensitive data as non-actionable. Source, scope, architecture, exit code, timeout/cancellation and sanitized diagnostics remain required adapter fields.

**Recommendation:** use a constrained CLI adapter as the provisional route, capability-gated at runtime. A supported programmatic route may replace it only after an evidence-backed compatibility and lifecycle test. Microsoft Store must fail independently and never discard the last successful source data.

## Safety fixtures and tests

- `WingetSpike.psm1` parses numeric versions and observed help capabilities, then produces only `FileName` plus an argument array.
- `fixtures/non-executed-update-plan.json` is deliberately non-executable. It illustrates the allowed immutable update shape and rejected injection-like inputs.
- `tests/WingetSpike.Tests.ps1` verifies capability detection, shell-free fixed-executable planning, deterministic argument order, and unsafe-ID rejection. It never starts WinGet.

Run: `pwsh -NoProfile -File spikes/winget-packaging/tests/WingetSpike.Tests.ps1`.

## Follow-up evidence required before locking either ADR

1. A disposable, clean Windows 11 x64 VM with native packaging, signing and build tooling.
2. Explicit approval for bounded read-only WinGet inventory, upgrade metadata and Microsoft Store source probes, with sanitized retained output.
3. MSIX and unpackaged install/launch/upgrade/uninstall retain-or-remove-data matrix, including current-user Task Scheduler and notifications.
4. Helper/UAC, cancellation/timeout, exit-code, source/scope/architecture and parser-failure fixtures.
