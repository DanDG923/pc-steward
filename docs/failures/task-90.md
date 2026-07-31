# Task 90 package smoke failures

Date: 2026-07-30

## Failure 1 — duplicate COM publish paths

Command:

```powershell
pwsh -NoProfile -File .\scripts\New-PrivateBetaPackage.ps1
```

Result: `NETSDK1152` reported duplicate relative publish outputs for both
`Microsoft.Management.Deployment.dll` and
`Microsoft.Management.Deployment.winmd`. The desktop project added both files
as `Content`, while the official `Microsoft.WindowsPackageManager.ComInterop`
package already contributed the same `ReferenceCopyLocalPaths` entries.

Minimal correction: remove the redundant explicit `Content` entries (and their
now-unused paths) from `src/PCSteward.Desktop/PCSteward.Desktop.csproj`; retain
its x64 static `MicrosoftManagementDeployment-Platform` setting and the
`GeneratePathProperty` package-reference metadata. The fresh unsigned package
then built and `Test-PrivateBetaPackageEvidence.ps1` passed with all required
WinGet runtime files present.

The focused architecture test initially still asserted those redundant
`Content` elements. It was revised to validate the supported official package
reference/configuration and the packaging script's required-runtime checks;
the mechanical package-evidence validator verifies the actual payload and ZIP
entries.

## Failure 2 — packaged desktop blocked before smoke/review

Command:

```powershell
C:\...\artifacts\private-beta\payload\PCSteward.Desktop.exe
```

Result: the normal standard-user launch showed the blocking dialog **PC Steward
reset needs attention**, with **Factory reset is incomplete** and
`Scheduled check: ScheduleRemovalFailed`. It prevents access to the update
review UI. The current task expressly prohibits factory-reset or scheduled-task
work, so the marker and task state were preserved and the review was not
bypassed.

Smallest suspected cause: a pre-existing reset marker records an unsuccessful
current-user scheduled-check removal. This is a lifecycle-state blocker rather
than evidence that the freshly packaged COM payload is absent; clearing it
requires the separately scoped reset/scheduler remediation and a fresh launch
smoke afterward.

## Failure 3 — exact missing-task COM result surfaced as reset failure

The original unsigned archive
`F5E144978CC7C6890DCEB3BAEA2755CAF3EC24A0342DCD78B38E6D93BD178D0E`
reproduced the blocking `ScheduleRemovalFailed` recovery screen on two serial
launches. The exact task was absent. Directly invoking the packaged
`WindowsExactScheduledTaskQuery` proved the cause: Task Scheduler surfaced the
missing `PCSteward.ScheduledCheck` task as `FileNotFoundException` with HRESULT
`0x80070002`, while the adapter recognised only COM-shaped wrappers. The
exception propagated through `EnsureAbsentAsync` and was recorded as a false
schedule-removal residual.

Correction: `WindowsExactScheduledTaskQuery` now treats the missing-task HRESULT
as absence through its exception chain, including the managed binder shape. The
focused scheduler tests passed 7/7, Release build had 0 warnings/errors, full
suite passed 233/233, and the rebuilt unsigned archive
`58443BA582912CD6621255DA790E4FA580AD3C373F1F1411918F0547DD326C29` passed the
mechanical validator. Its isolated launch reached post-reset onboarding and,
after the approved acknowledgement, the local-only Scan page with `No scan yet`.
The original archive was retained as bounded evidence, not used as a candidate.

## Failure 4 — live WinGet display evidence did not survive structured-authority review

Date: 2026-07-30

Package under review: archive SHA-256
`58443BA582912CD6621255DA790E4FA580AD3C373F1F1411918F0547DD326C29`;
package ID `Microsoft.VisualStudioCode`; approved source `winget`.

Pre-review command and result:

```powershell
winget list --id Microsoft.VisualStudioCode --exact --source winget --scope user
```

```text
Microsoft Visual Studio Code (User)  Microsoft.VisualStudioCode  1.129.0  1.130.0
```

The packaged Applications view showed two correlated rows: the registry
instance was `1.129.0`, `ReviewRequired`, `User`; the WinGet instance was
`1.129.0`, `VerifiedUpdate`, `Unknown`. Its detail view showed package ID
`Microsoft.VisualStudioCode`, repository `winget`, candidate `1.130.0`, and a
live `winget` result at 21:55. It stated that the package ID exactly matched
the installed provider package ID and the candidate was newer.

Selecting **Review and update** did not open the final installation
confirmation. It fail-closed with:

```text
Update not available
The provider did not supply bounded structured candidate-version evidence for
the exact source and package. Display results cannot authorize an update.
```

Activity preserved local inventory completed at 21:54 (Windows-local plus
approved WinGet installed inventory) and live `msstore`/`winget` source
evidence at 21:55. No saved decision or manual mapping was created. Before
review, no `Code`, `PCSteward.ElevatedHelper`, `consent`, or `CredentialUI`
process was present; after rejection, no elevation helper or consent process
was present. No UAC dialog, installation, uninstallation, source change,
Avast change, or schedule change occurred.

Suspected cause: the production structured COM query returned no observation
meeting the exact structured-evidence contract despite the separate WinGet
display/source-refresh path having current matching data. This is a
structured-authority/COM evidence-contract defect, not filtering or product
correlation absence. No application log file was emitted in
`%LOCALAPPDATA%\\PCSteward`.

Smallest next action/design decision: add bounded, test-covered diagnostic
reason capture around the structured COM observation without relaxing the
exact/trusted/one-match/candidate-version gate; reproduce the real
`Microsoft.VisualStudioCode` user-scope observation; then make only the
minimal contract-preserving correction and rerun package review. Do not
promote display-table evidence to update authority.

## Changed files

- `src/PCSteward.Desktop/PCSteward.Desktop.csproj`
- `tests/PCSteward.ArchitectureTests/ReferenceBoundaryTests.cs`
- `docs/failures/task-90.md`
- `src/PCSteward.Scheduling.Windows/PCSteward.Scheduling.Windows.csproj`
- `src/PCSteward.Scheduling.Windows/ScheduledCheckRegistration.cs`
- `tests/PCSteward.Application.IntegrationTests/ScheduledChecksTests.cs`

## Failure 5 — clean packaged idle observations do not establish the full resource gate

Date: 2026-07-30

Package archive SHA-256: `58443BA582912CD6621255DA790E4FA580AD3C373F1F1411918F0547DD326C29`.
Baseline: physical Windows 11 Home build `26200`, AMD Ryzen 7 9800X3D (16 logical processors), 31.15 GiB RAM, NVMe storage, active fixed `3XS-Overclock` power plan. Before each run there were no `PCSteward.Desktop` or `PCSteward.ScheduledHost` processes and no `PCSteward.ScheduledCheck` task. Each desktop was started only by direct PowerShell `Start-Process`; no Computer Use/UI Automation activity occurred during either sample.

The initial detached sampler had a quoting defect because its script/output paths contained workspace spaces. It exited before collecting a sample while the launched desktop remained healthy. A one-second disposable-process probe validated the native `GetProcessIoCounters` helper, and the corrected quoted command was used for both retained results.

| Clean packaged run | Protocol | CPU / memory | Attributable I/O | Result |
| --- | --- | --- | --- | --- |
| Cold Overview | Window observed at 548.891 ms, then five 60-second samples | 0.4521% total-system CPU (7.2344% one-core); 181,592,064 B peak working set | 24,752,388 B read and 21,374,405 B write during minutes 1–3; zero read/write in minutes 4–5 | CPU/memory pass; full NFR-PERF-05 gate not established. |
| Warmed Overview | 180-second untouched warm-up, then five 60-second samples | 0.1491% total-system CPU (2.3854% one-core); 186,441,728 B peak working set | 8,551,036 B read and 6,539,124 B write during captured minutes 1–3; zero read/write in minutes 4–5 | CPU/memory pass; repeat process I/O prevents a no-recurring-I/O claim. |

The previously reported `20.721%`–`26.666%` one-core figure is not reproduced by the two clean runs: the cold run declines from 0.7438% to 0.4521% total-system CPU and the warmed run declines from 0.1937% to 0.1491%. It is therefore not evidence of a sustained desktop CPU defect in this package under the direct, non-interactive protocol. The discrepancy cannot be assigned conclusively, but earlier interactive/page-observation measurement is a plausible interference source and must not be used as a clean CPU gate.

Source inspection found no `DispatcherTimer`, `PeriodicTimer`, composition/render callback, startup refresh loop, or retained ViewModel event subscription in the desktop path. `MainWindow.Render()` is called only for startup/navigation/user-action completion. The remaining disk work is process-attributed by the built-in native counter but cannot be assigned to a source method without another system-wide trace, which prior Task 29 evidence showed to be impractically large and symbol-blocked on this host.

The retained evidence is `artifacts/host-evidence/20260730T222900Z-task90-idle-clean/`. It includes raw JSON, direct-launch records, cleanup records, and the native-counter script. Network is explicitly **unverified** because no supported non-elevated process-attributed network-byte counter is available. The direct runs ended with zero PC Steward desktop/scheduled-host processes and no exact scheduled task. Under the Task 90 stop rule, do not enable WinGet sources or perform the 30-local/20-live scan protocol from this investigation: the full idle gate remains unproven and no safe, evidenced source change is available.

## Failure 6 — package-identity confirmation read a WinUI control on the lease thread

Date: 2026-07-31

The rebuilt package exposed the intended reversible **Confirm package identity** control for the concrete current-user VS Code registry instance. Activating it terminated the desktop. Application Error 1000 identified `Microsoft.UI.Xaml.dll` / `0xc000027b`; WER recorded `combase.dll` / `0x8001010e` (`RPC_E_WRONG_THREAD`).

Root cause: the click handler captured the WinUI `TextBox` itself and evaluated `reason.Text` inside `DataOperationLease.RunAsync`, whose operation deliberately runs on a dedicated worker thread. The same defect affected pin, ignore and manual-map handlers. All affected WinUI values are now copied to plain values on the UI thread before the data-operation lambda begins. An architecture regression forbids the unsafe forms. The rebuilt package replay remained open and changed the concrete registry row to `Managed · update available` with `User` scope and target `1.130.0`.

## Failure 7 — successful update crashed while presenting the outcome

Date: 2026-07-31

The approved current-user WinGet update completed without an observed UAC prompt. Audit history recorded `Queued` → `Running` → `Success`, exit code `0`, and `target-version-verified-by-post-update-inventory`; the current-user uninstall registry reported VS Code `1.130.0`. Immediately afterward, Application Error 1000 and WER recorded a WinUI crash with `combase.dll` / `0x80070057`.

Root cause: the completion path called `LoadSavedInventory(); Render();` and then immediately attempted to attach the outcome `ContentDialog` to the newly replaced `Window.Content` XamlRoot. This is the same ordering previously corrected for bulk identity confirmation. The final path keeps the existing root through the outcome dialog, then renders after the dialog closes; an architecture regression locks this ordering. Retained update evidence is under `artifacts/host-evidence/20260731T225802Z-task90-vscode-update/`. The recorded update is a success; the UI crash is retained separately and is not misclassified as a clean exit.

## Failure 8 — absent first-launch state root was treated as invalid reset state

Date: 2026-07-31

The independent Task 90 re-review found that bootstrap queries the reset marker before creating `%LocalAppData%\PCSteward`. The handle-bound marker reader correctly fails closed for an unsafe or inaccessible existing root, but its missing-root `IOException` was also converted to `Invalid`; a genuinely clean profile would therefore open reset recovery instead of onboarding.

`ReadMarkerState` now distinguishes only `DirectoryNotFoundException` and `FileNotFoundException` as the expected `None` state. All other I/O, access, security, marker-schema and handle-validation failures remain fail-closed. The focused reset suite passes 13/13 and includes a clean-profile regression proving the read reports `None`, does not report pending recovery, and does not create storage.
