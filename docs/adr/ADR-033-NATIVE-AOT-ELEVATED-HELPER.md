# ADR-033 — A Native AOT elevated helper

**Status:** Proposed. The owner decided the direction on 2026-11-06 ("Native AOT helper before ripgrep") and the build machine on 2026-11-06 ("Install C++ Build Tools on my PC", §Decision 5 option a). The named edits to hash-pinned Task 50 files are flagged here and land in their own reviewed change.
**Date:** 2026-11-06
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-UPD-15`, `TM-03/04/13`; ADR-018 (helper IPC and elevation), ADR-022 (owner-only protected test deployment and its protocol-2 alignment note), ADR-025 (typed operation jobs and helper), ADR-031 (Chocolatey client self-update); security review finding H1 ([record](../review/TASK_50_CHOCOLATEY_UPDATE_SECURITY_REVIEW.md))

## Context

Security review H1 found that the elevated helper is a framework-dependent .NET program. A UAC elevation builds the helper's environment from the owner's own registry (`HKCU\Environment` and `HKCU\Volatile Environment`), which any program running as the owner can write without elevation. The .NET host and runtime act on several of those names before `Main` runs. Some load code (a profiler DLL, a standalone GC, a startup hook, a different runtime location) and some write files (EventPipe output), all with administrator rights.

PR #27 narrowed this without changing the deployment layout:

- `StartupHookSupport=false` and `AppHostDotNetSearch=Global` on the helper project close the startup-hook and runtime-location names.
- `ElevatedRuntimeEnvironmentGuard` refuses a `runas` launch when the desktop's environment or either user key holds a .NET runtime control name, and the helper checks its own environment first.

What remains is a race: a name written after the desktop's check and before Windows builds the elevated environment (for example while the UAC prompt is showing) reaches the helper. A name that loads code acts in the runtime before the helper's own check can run. No runtimeconfig switch turns off profiler loading in the CoreCLR runtime. The owner chose to close this before G9-T1 (the ripgrep install test) or any real update on their PC.

## Decision drivers

- Nothing a same-user program writes may decide what code runs elevated.
- The fix must not depend on winning a race against the owner's own session.
- Keep the reviewed helper code, protocol and checks unchanged; change how it is compiled and shipped, not what it does.
- Keep ADR-022's model: an exact, hash-pinned payload, a fixed binding set, and a ledger the desktop and helper only read.
- Smallest change to the hash-pinned Task 50 scripts, each one flagged and approved.

## Options considered

### Option A — keep the framework-dependent helper and the PR #27 guard

No further work. The residual race stays open on every elevated launch. Rejected by the owner.

### Option B — self-contained (not AOT) helper

Ships its own CoreCLR beside the helper. This closes `DOTNET_ROOT` but not the profiler, standalone-GC or EventPipe names, which the bundled CoreCLR still reads. It does not close H1.

### Option C — Native AOT helper (chosen)

The helper compiles to one native image. Native AOT has no JIT, no profiler API, no startup hooks and no runtime to locate. EventPipe and the diagnostic server are compiled out unless `EventSourceSupport` is turned on, and it stays off. The runtime code that reads `DOTNET_` settings in an AOT image only tunes the garbage collector. CI evidence below must show that `DOTNET_GCName` does not load a DLL.

Feasibility, checked so far:

- The helper's code paths read and write JSON only through `JsonDocument` and `Utf8JsonWriter`, which need no reflection. The two reflection-based `JsonSerializer.Serialize` calls in `PCSteward.Application` (`InventoryExportService`, `UpdateBatchWorkflow`) are not reached from the helper entry point.
- Interop is about 20 plain `DllImport` declarations (token, file, pipe, process and `WinVerifyTrust` calls with `SafeHandle`, string and struct marshalling). Native AOT supports these. There is no `ComImport`, `dynamic`, delegate marshalling or custom marshaller.
- No helper code uses `Assembly.Location`, `Activator` or other reflection. `Environment.ProcessPath` and `AppContext.BaseDirectory`, which the helper's peer checks use, work in an AOT image.
- No helper code uses WinRT types. The Windows SDK projection that the `net10.0-windows10.0.22000.0` target pulls in produces trim warnings, but only for code that is never reached.
- A real AOT compile needs Windows. The informational CI job `helper-native-aot-feasibility` (`scripts/Test-ElevatedHelperNativeAotCI.ps1`) compiles the helper with `PublishAot=true` and lists every trim and AOT warning. It fails on any warning in PC Steward code and on any managed runtime or PC Steward assembly file left in the output. It then starts the native image (never connected to anything) with `DOTNET_GCName` and with EventPipe settings, and requires the image to reach its own environment check (exit 20). With a clean environment it must get past that check (exit 21). It runs a framework-dependent build of the same helper as a control.

## Decision

1. **The helper is published with Native AOT.** `PCSteward.ElevatedHelper.csproj` sets `PublishAot=true` for `win-x64`. `EventSourceSupport` stays off and the PR #27 switches stay. The helper's source, protocol, checks and the `ElevatedRuntimeEnvironmentGuard` (kept as a second layer) do not change. The guard's checks before each launch stay as they are.
2. **The payload layout changes in one place.** The payload holds a native `PCSteward.ElevatedHelper.exe` and no `PCSteward.ElevatedHelper.dll`, `.deps.json` or `.runtimeconfig.json`. The desktop stays framework-dependent with its own copies of `PCSteward.Execution.dll`, `PCSteward.Application.dll`, `PCSteward.Domain.dll` and the provider assemblies, unchanged.
3. **The ADR-022 binding set drops the three helper-managed files.** `DeploymentCapabilityPolicy.BindingSet` becomes the desktop apphost and main assembly, the native helper image, the desktop's project-assembly closure and the capability statement. The helper image's hash now covers all helper code. The ledger schema (`pc-steward-owner-only-protected-test-v6`), its key sets, `executableImages` (both executables, PE checksum) and the capability statement bytes do not change. A deployment built before this ADR fails the new binding set, reports protocol-2 **Absent** and gains nothing, as ADR-022 alignment-note item 4 already requires.
4. **The candidate builder publishes the helper natively.** In `scripts/New-OwnerOnlyCandidate.ps1` (hash-pinned), the `restore-helper`, `build-helper` and `publish-helper` steps become one restore and one `publish --runtime win-x64 -p:PublishAot=true` into the helper staging folder. The "must be framework-dependent" check gains a second rule: the helper staging output holds exactly the native image and the capability statement, with no `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll` or `PCSteward.ElevatedHelper.*` managed file. The probe-build mark is unchanged, because it reads the desktop's `PCSteward.Execution.dll`. No other pinned script requires the three removed files: the installer, validator and redeploy caller name only the two executables, and the host-evidence binding list in `scripts/evidence/Task50-HostEvidence.ps1` reports the files that are present, so it needs no edit. Every pinned file that does change gets a new reviewed hash in the next host-session plan.
5. **Build machine: the owner's PC (owner choice 2026-11-06, option a).** Native AOT links with the Microsoft C++ linker and the Windows SDK libraries. The owner's last environment report shows no Visual Studio or Build Tools. The options are:
   - **(a) chosen:** install Visual Studio Build Tools with the "Desktop development with C++" workload on the owner's PC once, an administrator step. The candidate stays built on the owner's machine, as ADR-022 and the G3 builder assume.
   - **(b):** compile the helper image on GitHub's Windows runners and pin its SHA-256 in a reviewed PR, with the local builder checking that hash. This avoids the install, but the local build would then trust an image built elsewhere, which Native AOT cannot reproduce byte for byte locally.
6. **Order.** Merge the feasibility CI and this ADR. Owner approval of this ADR, choice 5 and the named pinned-file edits. Then the code change (csproj, binding set, architecture and fixture tests) and the pinned-script change, reviewed together. The owner installs Visual Studio Build Tools with the C++ workload (one administrator step), and the builder refuses to start without a linker it can find. Then a new candidate build, sign and redeploy under a new exact host-session plan. G9-T1 waits for all of it.

## Consequences

### Positive

- No same-user environment value can load code into, or make the runtime write files from, the elevated helper. H1 closes and does not rely on the race-prone guard.
- One file holds the helper's code, so the binding set is smaller and the helper hash means more.
- Faster helper start, and no dependency on the machine-wide .NET runtime for the elevated process.

### Negative / trade-offs

- A larger helper image (expected 5–15 MB), still well inside the 32 MB binding-file limit.
- Build tooling: Visual Studio Build Tools with the C++ workload on the owner's PC (a few GB, one administrator install).
- Hash-pinned Task 50 scripts change, so each needs a fresh review, new pins and a redeploy.
- Future helper code must stay AOT-compatible: no reflection-based JSON, no COM interop, no runtime code generation.

### Follow-up controls and tests

- The `helper-native-aot-feasibility` CI job becomes gating, with the AOT publish taken from the real builder, once Decision 4 lands.
- An architecture test pins `PublishAot=true` and `EventSourceSupport` off in the helper project, and pins the new binding set.
- A fixture test runs the builder's helper-output rule over a framework-dependent layout and a native layout.
- The host-evidence and validator checks name the native image only.
- The review record's H1 row moves to Fixed only after the redeploy's host evidence shows the native image installed.

## Reconsider when

- Native AOT drops support for something the helper needs (for example a Windows API marshalling form), or CI shows a trim or AOT warning on a helper code path that cannot be fixed.
- The helper is rewritten in a language with no managed runtime.
- A signed release channel replaces the owner-only deployment (ADR-022), which would re-open the build-machine choice.
