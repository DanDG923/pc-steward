# Task 90 — P3R-14/P3R-15 final remediation evidence

**Date:** 2026-07-29
**Branch:** `phase/3-execution`
**Starting gate commit:** `b24d48ee0874c98ce7b9c9d783e2e34dc392a6bc`
**Fixing commit:** this Task 90 commit
**Disposition:** both remaining findings are remediated and evidenced; Phase 3 still requires a fresh independent Task 25 review before merge.

## Finding disposition

| Finding | Current-tree remediation and evidence |
|---|---|
| `P3R-14` High | The actual desktop composes `WingetStructuredUpdateCandidateAuthority` over Microsoft's supported `Microsoft.WindowsPackageManager.ComInterop` projection. It accepts only predefined trusted `winget`/`msstore` catalogs, one exact package-ID match and bounded exact structured installed/newer-candidate versions. Wrong source/ID, fuzzy/duplicate/limit-exceeded/untrusted results, malformed/equal/older versions, cancellation, timeout or API/catalog failure return no authority. Display connectors still do not implement the authority. `ProductionStructuredAuthorityComposesThroughReviewRunnerAndPostUpdateVerification` proves the production authority around a controlled COM observation crosses fresh review and runner construction, then becomes Success only after exact provider export plus concrete local version/scope/architecture re-inventory. |
| `P3R-15` Blocker | The real default `WindowsElevatedHelperTransport` and production `InstalledHelperLaunchTrust.Validate` were run from signed disposable binaries at the exact protected `%ProgramFiles%\PC Steward` path. The clean same-signer/non-reparse/Users-RX layout completed UAC, mutual peer/pipe validation, one immutable job and the approved no-op provider response. Real tampered, second-trusted-signer, caller-writable, helper-symbolic-link and privileged between-validation replacement cases all failed with the expected production diagnostic before the helper-start marker. Exact root, temporary root and both certificate thumbprints were absent after teardown. |

## Structured candidate boundary

The compatible official COM projection is centrally pinned at `1.10.340`; Windows-targeted projects now compile against the documented Windows 11 `22000` minimum instead of the former Windows 10 `19041` platform target. The adapter uses `GetPredefinedPackageCatalog`, requires `Predefined` origin and `Trusted` trust, applies an exact `PackageMatchField.Id`/`Equals` filter with result limit two, and requires exactly one structured result whose candidate catalog source matches the requested source.

The returned application evidence binds source ID, package ID, installed version and candidate version. `InventoryWorkflow` independently requires exact equality with fresh local installed evidence and the fresh display mapping, validates bounded version syntax/newness, and records `winget-com-v1` in the immutable selection. The `winget upgrade` table remains presentation-only. No live external-source listing was run for this remediation because such a query would disclose installed-software metadata; deterministic production-composition evidence and the previously approved real Firefox targeted-verification record are kept distinct.

## Protected-root host evidence

The user explicitly approved temporary certificates in CurrentUser My/Root/TrustedPublisher, disposable binaries only at `%ProgramFiles%\PC Steward`, protected and hostile ACL/layout variants, visible no-op UAC execution, marker checks and exact teardown. Final run `c27f884b52e944e9a569def8d1dae073` recorded:

- root owner `BUILTIN\Administrators`;
- root ACL SYSTEM/Administrators full control and Users read/execute; desktop/helper Users read/execute;
- root and both executable attributes without reparse;
- both Authenticode signatures `Valid` with the same signer thumbprint;
- clean no-op outcome `Completed`, exit `0`, helper-start marker present;
- tamper → `elevated-helper-signature-untrusted`, zero launch;
- second trusted signer → `elevated-helper-signer-mismatch`, zero launch;
- Users-modifiable layout → `elevated-helper-caller-writable-layout-rejected`, zero launch;
- helper symbolic link → `elevated-helper-reparse-layout-rejected`, zero launch;
- privileged replacement between the two real validations → final signature rejection, zero launch;
- installed root and unique temporary root absent; both exact thumbprints had zero entries across My/Root/TrustedPublisher.

The first attempted run exposed non-UI certificate-store cleanup on CurrentUser Root; exact interrupted-run thumbprints were removed and verified absent before retry, and cleanup now uses exact `X509Store.Remove`. The next clean attempt exposed Windows' default volume-root `FILE_ADD_SUBDIRECTORY` grant. That right permits an unrelated sibling but cannot replace an existing protected child. The corrected validator keeps full create/write/delete/DACL/owner probes on the installed root/files and delete-child/delete/DACL/owner probes on higher ancestors. The final positive/replacement cases passed this stronger path-replacement model. Exact SDDL, `icacls`, signatures, case results and teardown counts are retained in [raw host evidence](TASK_90_P3R15_HOST_EVIDENCE.json).

## Reproducible final gates

| Gate | Result |
|---|---|
| Focused production authority/review/batch/WinGet/cache Release matrix | **61 passed** |
| Security fixtures | **19 passed** |
| Architecture fixtures | **5 passed** |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | **0 warnings, 0 errors** |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | **182 passed:** Security 19, Architecture 5, Domain 51, Application integration 107 |
| `SecurityBoundarySpike.Tests.ps1` | **42 assertions passed; 1 approved elevated-only local/manual skip** |
| `WingetSpike.Tests.ps1` | **11 assertions passed** |
| Signed protected-root production transport fixture | **6/6 cases passed; teardown verified** |

## Residual risk and handoff

No Blocker, High or Medium issue remains in the P3R-14/P3R-15 diff after the final local audit. The evidence does not claim a clean-image traditional-installer lifecycle, same-signer rollback, provider-disconnect cancellation, unconstrained administrator/OS-compromise resistance, or a new real software update. Those remain documented later-phase or approved residual risks.

The current Phase 3 status remains `FAIL` procedurally until a fresh GPT-5.6 Sol / Extra High Task 25 review independently evaluates this commit. Do not merge, push or begin Task 26 from this task.
