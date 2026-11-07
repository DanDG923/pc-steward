# Task 50 — Security review of real Chocolatey updates (2026-11-06)

Requirements: `FR-UPD-15`, `FR-UPD-01/03–09`, `TM-03/04/07/13`; ADR-018, ADR-022, ADR-025, ADR-031; [Confirm screen build](TASK_50_CHOCOLATEY_CONFIRM_SCREEN_BUILD.md).

The owner approved real Chocolatey updates on condition that a full security review runs once the path is built and enabled, with fixes made before G9-T1 or any real run on the owner's PC. This is that review. Base: `phase/4-beta` at `cdbfe02` (Confirm screen merged, owner admission on).

## 1. Scope and method

The whole real-update path, end to end, for both Chocolatey routes. The client self-update (`ChocolateyClientSelfUpdateV1`) is the one the Confirm screen composes today. The package route (`ChocolateyMachineRuntimeLimitsV3`, the ripgrep test of G9-T1) shares the helper, launcher, observer and verifier, so it is covered too, except for its future review session, which is being built separately and needs its own review when it lands.

Read in full: the Confirm session and its gating (C1–C8), the dispatch workflow's admission and the client attention rule; the desktop transports (protocol 1, protocol 2, probe) and their pre-UAC trust checks; the restricted pipe; the helper entry point, bootstrap and peer verification; the endpoint stage machine (A0–A3, B, C, D, Launch); the compiled commands and argument builders; the compiled child environment; the held client binding; the job-object launcher; engine trust; the profile observer's cache and configuration steps; the client postcheck and the package verifier; the wire codec's diagnostic handling; the owner-only deployment installer's signing step.

Threat model: the product's own. The attacker is any program running as the owner's account without elevation (the "same-user" attacker of Profile V2 and ADR-025). Administrator changes and independently started elevated programs stay outside it. A same-user attacker can write `HKCU`, the owner's profile and anything the owner's account can modify, can start processes (including PC Steward and UAC prompts), and can read and inject into the owner's unelevated processes.

## 2. Findings

| ID | Severity | Finding | Status |
|---|---|---|---|
| H1 | High | The elevated helper's .NET runtime takes control settings from the owner's own environment, which a same-user program can set | Partly fixed (PR #27). Full fix built: Native AOT helper ([ADR-033](../adr/ADR-033-NATIVE-AOT-ELEVATED-HELPER.md)); Fixed once a redeploy's host evidence shows the native image installed |
| M1 | Medium | The same environment settings also reach Chocolatey's own child processes, because Chocolatey reloads the owner's environment while it runs elevated | Narrowed here; owner accepted the named residual in ADR-025 (2026-11-06) |
| M2 | Medium | The owner-only test signing key stays usable by any same-user program for its 14-day life | Not fixed: the fix is in a hash-pinned script (flagged) |
| L1 | Low | Only the `cacheLocation` folder itself and its ancestors are checked, not what is inside it | Recommendation |
| L2 | Low | A refusal by the new guard shows as a generic "could not start" result | Recommendation |
| L3 | Low | The per-request pipe is created without `FILE_FLAG_FIRST_PIPE_INSTANCE` | Recommendation (defence in depth) |
| I1 | Info | The Chocolatey engine signer is matched by its simple name only | No change: the engine's SHA-256 is pinned per release |

### H1 — the elevated helper takes .NET control settings from the owner's environment (High)

**What.** A process elevated through UAC gets an environment built from the owner's own registry: `HKCU\Environment` and `HKCU\Volatile Environment`, which any program running as the owner can write without elevation. This is how the well-known `COR_PROFILER` UAC-bypass technique works against auto-elevated .NET programs. `PCSteward.ElevatedHelper.exe` is a framework-dependent .NET program (`selfContained=False` in the candidate build). Its host and runtime act on many environment names before `Main` runs, for example:

- `DOTNET_ROOT`: the apphost loads `hostfxr.dll` and the whole runtime from the named folder;
- `DOTNET_STARTUP_HOOKS`: the runtime loads and runs a managed assembly;
- `CORECLR_ENABLE_PROFILING` with `CORECLR_PROFILER_PATH`: the runtime loads a native DLL as a profiler;
- other `DOTNET_`/`COMPlus_` settings: a diagnostics port, an event-trace output path (an elevated file write), and more.

**Impact.** A same-user program sets one value once. The next time the owner approves a genuine PC Steward prompt (the expected publisher, the expected program), the program's code runs with administrator rights inside the helper, before any PC Steward check. It affects every helper launch: the client self-update, the package route, the qualification probe and protocol 1. The R5 guard and watchdog do not help, because they check three Chocolatey names only and run inside the already-compromised helper. Nothing in the existing reviews covers it: the child Chocolatey process gets a compiled environment, but the helper itself never did.

**Fixed in this change.**
1. Helper build switches (`src/PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj`): `StartupHookSupport=false` (runtimeconfig `System.StartupHookProvider.IsSupported: false`, so `DOTNET_STARTUP_HOOKS` is ignored) and `AppHostDotNetSearch=Global` (the published apphost looks for .NET only in the machine-registered and default locations, both administrator-only, and ignores `DOTNET_ROOT`). Checked on a local `win-x64` publish, including the builder's `publish --no-build` form: the runtimeconfig carries the switch and the apphost's search byte is `0x08` (global only).
2. `ElevatedRuntimeEnvironmentGuard` (Execution): immediately before each of the three `runas` launches, the desktop refuses (no UAC prompt) when its own environment, `HKCU\Environment` or `HKCU\Volatile Environment` (with its per-session subkeys) holds a name starting `DOTNET_`, `COMPlus_`, `CORECLR_`, `COR_` or `APPDOMAIN_MANAGER_`. Only the two SDK-only names `DOTNET_CLI_TELEMETRY_OPTOUT` and `DOTNET_NOLOGO` are exempt. It reads value names only, read-only, never values.
3. The helper's entry point checks its own environment first and exits with the existing refusal code 20 before it connects or reads a request.

Tests: `ElevatedRuntimeEnvironmentGuardTests` (Security, portable decision tests) and `ElevatedRuntimeEnvironmentBoundaryTests` (Architecture: every `runas` launch guarded before `Process.Start`, the entry check first, the build switches kept, the guard read-only).

**Residual (owner decision).** The switches close the runtime-location and startup-hook names outright. The guard narrows the rest to a race: a value written after the desktop's check and before Windows builds the elevated environment (that is, while the UAC prompt is showing) is not seen by the desktop, and a profiler setting loads its DLL before the helper's own check runs. The .NET runtime has no setting that turns profiler loading off. Closing this fully needs an elevated program that the .NET runtime does not host. Options, recommended first:
- **(a) Publish the helper with Native AOT.** No apphost runtime search, no startup hooks, no profiler interface. It changes the helper's deployment layout (no helper `.dll`, `deps.json` or `runtimeconfig.json`), so the ADR-022 binding set, ledger, capability check and the hash-pinned builder change with it: a new ADR and a redeploy.
- (b) A small native launcher as the UAC target, which starts the .NET helper with a compiled environment. Smaller runtime change, but it adds a process hop to the pipe and peer checks.
- (c) Accept the narrowed residual for the owner-only test (like R5), with the guard in place.

### M1 — the same settings reach Chocolatey's child processes (Medium)

**What.** The helper starts `choco.exe` with a compiled environment, so the .NET Framework runtime inside Chocolatey starts clean. But Chocolatey itself reloads the machine and user environment from the registry while it runs (ADR-031 design §2.1 already records this for `UpdateSession`). Child programs it starts afterwards inherit what it loaded. For .NET Framework children, `COR_ENABLE_PROFILING`/`COR_PROFILER_PATH` or `APPDOMAIN_MANAGER_*` load code into an elevated process. On the ripgrep package route that could be Chocolatey's own shim generator, if the reload happens before it runs (not confirmed against the Chocolatey source in this review).

**Status.** ADR-025 accepts that "a later script-requested environment refresh can import same-user registry values", but it lists checksum, installer arguments and TEMP, not code-loading names. This change narrows it: the guard's prefixes include `COR_` and `APPDOMAIN_MANAGER_`, so a name present when the owner presses Confirm, or when the helper starts, refuses the run. The residual is a name written after the helper started and before Chocolatey's reload. Closing it would need Chocolatey not to read `HKCU\Environment`, which PC Steward cannot control (the same reason R5 stays open). **Recommendation:** extend the owner's ADR-025/R5 acceptance to name code-loading settings explicitly; optionally extend the R5 pre-creation check to these names (a reviewed change to the pinned reader, which today never enumerates).

### M2 — the test signing key stays usable for 14 days (Medium)

**What.** `Install-OwnerOnlyProtectedTestDeployment.ps1` creates the owner-only code-signing certificate in `CurrentUser\My` with a non-exportable key and keeps it there; its public certificate is trusted in the owner's `Root` and `TrustedPublisher`. "Non-exportable" stops export, not use: any program running as the owner can sign with the key through the normal Windows crypto API.

**Impact.** Such a program can sign its own executable as the PC Steward test publisher and show a UAC prompt that looks the same as PC Steward's (same verified publisher, same program name). The product's defence against prompt spoofing is "the owner declines any prompt they did not start" (ADR-018, ADR-025), and that relies on the prompt being distinguishable. It does not let the attacker replace the installed helper, which stays in the protected Program Files layout.

**Status.** Not changed: the installer is one of the hash-pinned Task 50 tooling scripts. **Recommendation:** in the next reviewed redeploy, remove the private key once the two images are signed (keep only the public certificate in `Root` and `TrustedPublisher`); a redeploy already always creates a fresh certificate, so the key is never needed again. Until then, the key lapses with the signer on 2026-11-20.

### L1 — `cacheLocation` contents are not checked (Low)

Step 6 of the observer requires the `cacheLocation` folder and its ancestors to be unmodifiable by the requester, but it does not walk what is inside. With `TEMP`/`TMP` set to that folder, package scripts download installers into its subfolders and run them elevated after Chocolatey's checksum check. A subfolder the owner's account can change or owns would allow a swap between the check and the run, or a junction that redirects an elevated write. Because the folder itself must already be protected, a new weak subfolder cannot be created; only one left over from earlier could. **Recommendation:** walk the `cacheLocation` tree with the HTTP-cache rule (step 4: complete bounded walk, every node unmodifiable and not owned by the requester).

### L2 — a guard refusal looks like a generic failure (Low)

A launch the new guard refuses is recorded as "not started" with the generic launch-refused diagnostic, so the owner is not told which setting to remove. **Recommendation:** a fixed reason on the review screen. Note for host steps: start PC Steward normally (Start menu), not from a terminal that has `DOTNET_` variables set, or the launch is refused.

### L3 — pipe created without `FILE_FLAG_FIRST_PIPE_INSTANCE` (Low)

`RestrictedNamedPipe.Create` does not ask to be the first instance. The name is a fresh random GUID with one allowed instance, and both ends verify each other's process ID and image, so no attack is known. Adding the flag is cheap defence in depth.

### I1 — engine signer by simple name (Info)

`ChocolateyEngineTrust` compares the leaf signer's simple name to `Chocolatey Software, Inc` under any trusted root. The engine's SHA-256 and every release file are pinned per listed release, so this is not a weakness on its own.

## 3. Checked and found sound

- **Confirm gating.** C1 compiled admission; C2 10-minute check age (owner decision); C3 offer only from the compiled list; C4 split token; C6 earlier client update needing attention (fails closed on unreadable history, clears only after a later verified check); C7 gate with reconciliation; C8 30-minute review lifetime and one-use permit. One accepted press gives at most one run; Close is hidden while it runs.
- **UAC launch.** Fixed Program Files paths, no reparse components, layout not modifiable by the caller, both images trusted and same signer, re-checked after the capability hold and just before `runas`; structured arguments only.
- **Pipe and peers.** Per-request pipe with a current-user-and-SYSTEM ACL and remote clients rejected; the helper requires the server PID to be the desktop and verifies it through a held handle (path, session, SID, signer); the desktop requires the client PID to be the launched helper and its image to match the held helper file identity.
- **Request handling.** Exact codec; payload hash equals the digest; fixed-time nonce; request at most 2 minutes old, review at most 30; exact route shape; payload SID equals the peer SID; requester token rules; capability check with the requester as protection principal.
- **What runs.** Arguments come only from compiled builders over the reviewed operation and the compiled release list; no wire, database or remote string becomes an argument. The child gets a complete compiled environment, never merged with the helper's. The launcher creates the process suspended with an explicit handle list, assigns it to a kill-on-close job, then resumes; one upgrade per binding.
- **Pinning and TOCTOU.** Engine and release files held read-share-only from stage B through launch; the engine's SHA-256, file version and signer checked through the held handle; stage D re-observes and compares identities; client holds released only after resume, with the R5 watchdog armed before the last checks. Tree walks reject reparse points.
- **Results.** Success only when the post-update observation proves the expected version (package) or the exact target manifest and asset (client); run faults only lower a result; restart-pending is never Success; a lost launch keeps the barrier.
- **Logging and personal data.** Helper diagnostics are fixed tokens, sanitised and ASCII-bounded on the wire; no process output crosses the pipe; history texts are fixed messages.

## 4. Before anything real runs on the owner's PC

1. Merge this change (H1 switches and guard).
2. Owner decision on the H1 residual (§2 H1 options) and on extending the ADR-025/R5 acceptance to code-loading settings (M1). M1: accepted 2026-11-06 and recorded in ADR-025; H1: the owner chose option (a), the Native AOT helper, which is in progress separately.
3. A new candidate build and redeploy, because the helper's runtimeconfig and apphost bytes change. The currently deployed helper does not have these fixes.
4. M2 at the next redeploy if the owner agrees (pinned installer change).
