# Task 50 — candidate builder (gap G3)

**Status:** implemented and tested on Linux. The Windows CI job is the end-to-end evidence; it has not run yet for this branch. No host action has been taken. This record does not close G3 by itself: it needs an independent review and a green Windows CI run.

**Requirements:** `FR-UPD-15`, `TM-03`, `TM-13`, [ADR-022](../adr/ADR-022-OWNER-ONLY-PROTECTED-TEST-DEPLOYMENT.md) and its protocol-2 alignment note. **Gap:** G3 in the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) §11, which asks for a reviewed builder that writes the `[{bytes,path,sha256}]` manifest with the root capability file, built once and pinned, with no deterministic-build claim.

**Boundary.** The builder runs without elevation and refuses an elevated token. It does not sign, touch a certificate store, install, write under Program Files, request elevation or launch anything. The script makes no network call itself. `dotnet restore` resolves the centrally pinned package graph (`Directory.Packages.props`) from the configured NuGet feeds or the local cache. That is the only network effect, and the same restore already happened in the Task 48 and 49 preparations.

## Why

Task 48's candidate was put together by hand: the ScheduledHost RID prerequisite build, the desktop publish, the XBF/PRI resource merge, the helper publish, and then the manifest. Task 49 repeated the same steps by hand, and the Task 48 run first failed on the RID prerequisite (`TASK_48_OWNER_ONLY_REDEPLOY.md`). No committed script repeated those steps, so B1 in the activation plan had nothing reviewable to run.

## Files

| File | Role |
|---|---|
| `scripts/New-OwnerOnlyCandidate.ps1` | The builder. It runs under Windows PowerShell 5.1 or pwsh. An internal `-ValidateCandidateRoot` mode re-runs the script in Windows PowerShell 5.1 to validate the result. |
| `scripts/Test-OwnerOnlyCandidateBuilderCI.ps1` | CI-only launcher. Like `Test-CI.ps1`, it runs only as an administrator on a GitHub-hosted runner. It creates a disposable standard user and runs the builder plus an independent re-validation as that user. |
| `.github/workflows/ci.yml` | New `owner-only-candidate-builder` job on `windows-latest`. It installs the SDK from `global.json` and uploads only the small JSON evidence files. The existing `build-and-test` job is unchanged. |
| `tests/PCSteward.Security.FixtureTests/OwnerOnlyCandidateBuilderTests.cs` and `Fixtures/OwnerOnlyCandidateBuilder.ps1` | Parse, AST policy, real-function and manifest tests. |

## Design

**Invocation (owner, B1):**

```
powershell.exe -NoProfile -File .\scripts\New-OwnerOnlyCandidate.ps1 -OutputRoot C:\Users\<you>\AppData\Local\Temp\PCSteward-Task50-Candidate-<id>
```

`-Configuration` accepts `Release` only. Pass `-AllowDirty` to build from a dirty tree; the summary then records the build as dirty.

**Refusals before any build step:**
- Not Windows.
- An elevated token.
- An `OutputRoot` that already exists.
- An `OutputRoot` that is not a canonical long-form absolute drive path: a relative path, a UNC path, `..`, an 8.3 `~` name, a trailing dot or space, an alternate stream or a forward slash.
- An `OutputRoot` that is, contains or lies under Program Files (x86 or x64), the Windows directory or the repository.
- An `OutputRoot` that shares the `%LOCALAPPDATA%\PCSteward` data-root prefix (Task 48 renamed a candidate for this reason).
- An `OutputRoot` whose parent does not exist, or which has a reparse-point ancestor such as OneDrive (Task 48 hit this).
- A dirty source tree, unless `-AllowDirty` is passed.
- A resolved SDK that `global.json` does not allow. It must be exactly 10.0.403, or a `latestPatch` roll-forward within the 10.0.4xx band. The summary records `sdkExactPin`.

**Build steps.** These are fixed `[string[]]` argument arrays, run as `& $Dotnet @Arguments` from the repository root, so that `global.json` applies. No command line is built from strings. The profile matches the Task 48/49 candidates and the original installer: Release, `win-x64`, framework-dependent.

| # | Step | Arguments after the project path |
|---|---|---|
| 1–3 | restore ScheduledHost, Desktop, ElevatedHelper | `--runtime win-x64` |
| 4 | build ScheduledHost: the RID output that the desktop's `CopyScheduledHostRuntimeAfterBuild/Publish` targets read | `--configuration Release --runtime win-x64 --self-contained false --no-restore --disable-build-servers` |
| 5 | build Desktop: also produces `App.xbf`, `PCSteward.Desktop.pri` and `Resources\*.xbf` | same |
| 6 | build ElevatedHelper | same |
| 7–8 | publish Desktop and ElevatedHelper into separate staging folders | same, plus `--no-build --output <OutputRoot>\publish-staging\<app>` |

No build step uses `--output`. Task 48's first attempt failed because a build with `--output` moved the ScheduledHost RID output away from the path the desktop target reads.

**Assembly into `payload\`.** Files are copied one by one, in this order:
1. The Desktop publish output.
2. The WinUI resources from the Desktop RID build output (`bin\Release\<tfm>\win-x64`): `App.xbf`, `PCSteward.Desktop.pri`, and exactly one XBF for each source XAML. The XAML files are found by walking the project with `bin` and `obj` skipped. A built `Resources\` file that has no source XAML (a stale output) is refused.
3. The helper publish output, which includes `PCSteward.DeploymentCapability.txt` at the root.

A destination file that already exists must be byte-identical to the new one; otherwise the build fails and names the path. This is stricter than the Task 48/49 hand assembly, where the helper publish overwrote the desktop's files without a check.

**Checks on the assembled payload:**
- The capability file at the root has the checked-in bytes.
- The required leaves are present: both executables, `PCSteward.ScheduledHost.dll`, `App.xbf`, the PRI and the two `Assets` files.
- The payload is framework-dependent: `coreclr.dll`, `hostfxr.dll` and `hostpolicy.dll` are absent.
- Reparse points are refused while the tree is walked.

**Manifest.** `<OutputRoot>\candidate-manifest.json` sits beside `payload\` and is written in the format that `Read-OwnerOnlyCandidateManifest` accepts. It is a JSON array of closed `{"bytes":N,"path":"…","sha256":"…"}` objects:
- sorted by ordinal path order;
- `\` separators, escaped as `\\`;
- upper-case SHA-256;
- LF line endings and UTF-8 without a BOM;
- written with `CreateNew`, so an existing file is never overwritten.

The writer refuses names outside the reader's `[A-Za-z0-9_. -]` charset, reserved device names, traversal, case collisions, more than 512 files, more than 256 MiB in total, and a manifest larger than 1 MiB. Hashing uses .NET directly, not `Get-FileHash`, which is missing in some CI hosts (see `PayloadDeploymentIntegrityTests`).

**Self-validation.** A fresh Windows PowerShell 5.1 child process (the installer's runtime) dot-sources `OwnerOnlyPayloadIntegrity.ps1` and runs:
- `Read-OwnerOnlyCandidateManifest -PayloadRoot`, which must report `pinned-unsigned-v2` with `HelperProtocol` `2`;
- `PayloadIntegrity.AssertTree`;
- `Assert-OwnerOnlyPayloadProfile`;
- for both executables: `NotSigned`, and `PayloadIntegrity.GetUnsignedImage`, the PE32+ profile the installer needs before signing.

A bad build therefore fails before anyone approves it. The child's counts and hash must match the manifest the parent wrote.

**Summary.** The builder writes `<OutputRoot>\candidate-build.json` (UTF-8, no BOM) and prints the same JSON. It records:
- the validation result, origin and helper protocol;
- the file count and total bytes;
- `manifestSha256`;
- `sourceCommit` and `sourceDirty`, rechecked after the build;
- `sdkVersion`, `sdkPinned` and `sdkExactPin`;
- the configuration, RID and `selfContained: false`;
- `signed: false` and `deterministicBuild: false`, with the note "built once and pinned by manifestSha256";
- the two unsigned image identities.

**Layout:**

```
<OutputRoot>\
  payload\                    CandidateRoot for the installer
  candidate-manifest.json     CandidateManifestPath; its SHA-256 is CandidateManifestSha256
  candidate-build.json        summary (not read by the installer)
  publish-staging\desktop|helper   raw publish outputs, kept for inspection (no deletion)
```

## Tests (Linux run)

`OwnerOnlyCandidateBuilderTests`:
- **`BuilderParsesPassesAstPolicyAndWritesAManifestTheInstallerAccepts`.** Rows `pwsh` and `windows-powershell`. The checks are:
  - The builder, the launcher and the fixture parse with zero errors.
  - **AST policy, builder:** none of `Invoke-Expression`, `Start-Process`, `-Verb`, `Set-AuthenticodeSignature`, `New-SelfSignedCertificate` or other certificate cmdlets, web cmdlets or types, `curl`/`wget`/BITS, `Add-Type`, `Remove-Item`, `Set-Acl` or `cmd`. No bare `dotnet`, `git` or `powershell` command.
  - **AST policy, both scripts:** no string mentions `RunAs`, `Cert:`, `EncodedCommand` or `ExecutionPolicy`.
  - **Invocations:** every `&` call targets a resolved variable and passes only a splatted variable or an array of constants and variables. There are exactly 2 dotnet calls (the splatted step and `--version`), 2 git calls and 1 Windows PowerShell call. There is one dot-source, of `OwnerOnlyPayloadIntegrity.ps1`. All 8 build-step argument values are `[string[]]` arrays with no interpolation or concatenation.
  - **Real functions** (taken from the AST):
    - the exact argument arrays, including a project path with a space kept as one argument, and no `--output` on builds;
    - 12 SDK-rule vectors;
    - 21 output-root vectors;
    - the manifest writer on a 14-file synthetic tree, independently checked in C#: no BOM, LF, closed key order, ordinal order, `\` paths, and lengths and hashes re-derived from the files;
    - byte-identical output for reversed input order;
    - 10 writer refusals;
    - the copy rule (new, identical, conflicting);
    - reparse refusal (a symlink, or a junction on Windows);
    - the XBF set (bin and obj skipped).
  - **Windows row only:** `Read-OwnerOnlyCandidateManifest -PayloadRoot` accepts the written manifest (`pinned-unsigned-v2`, protocol `2`). `AssertTree` and `GetUnsignedImage` pass, and a byte appended after pinning is refused. On Linux this row reports Inconclusive with that reason; it is not silently skipped.
- **`BuilderRefusesUnsafeInvocationsBeforeAnyBuildStep`.** On Linux, the builder exits non-zero with "requires Windows" and creates nothing. On Windows, a Program Files path, a path inside the repository, an existing folder and a relative path are each refused before any `== restore` step, and the existing folder stays empty.
- **`CandidateBuilderCiLauncherRefusesAccountCreationOutsideHostedRunner`.** Windows only; Inconclusive on Linux.
- **`WindowsCiRunsTheCandidateBuilderWithThePinnedSdk`.** The job exists, uses `windows-latest` and `global-json-file: global.json`, and the existing job still runs `Test-CI.ps1`.

**Mutation checks**, each reverted afterwards:
- an inserted `Invoke-Expression` was caught;
- a joined `"--runtime $runtime"` argument was caught;
- a culture `Sort-Object` in place of the ordinal sort failed the ordinal-order assertion.

**Results.** The Security project builds in Release with `EnableWindowsTargeting` with 0 warnings and 0 errors. Security results: 1037 passed, 219 failed and 3 Inconclusive of 1259. The baseline without the new tests is 1034 passed, 219 failed and 1 Inconclusive of 1254. The failed sets are identical; all are existing Windows-native tests.

## What the Windows CI job proves

On a clean `windows-latest` checkout, as a non-admin user, with the `global.json` SDK, the job checks that:
- the committed builder restores, builds, publishes and assembles a payload;
- the installer's own reader accepts that payload as `pinned-unsigned-v2` with protocol 2;
- the tree and PE checks pass in Windows PowerShell 5.1;
- the manifest is BOM-free and hashes to the reported value;
- the source is clean and unsigned.

A second Windows PowerShell 5.1 process then re-validates the payload from disk. The job uploads `candidate-manifest.json`, `candidate-build.json` and `candidate-revalidation.json` (14-day retention). The CI candidate is **not** the deployment candidate: it is built from a PR merge commit on a runner and is discarded.

## What remains host evidence

- **B1:** the owner builds once from the chosen build commit (G6) on the host, and records `manifestSha256` and the summary. The CI hash will not match it. There is no deterministic-build claim, and the hash is pinned only once it has been built.
- The protected staging copy, signing, ACLs, the fresh signer and the independent validator still belong to the separately approved B3/B4.
- Whether the host's `%TEMP%` is a long-form, non-reparse path. If it is not, the builder refuses it and the owner chooses another local NTFS folder.

## Open points for review

- The strict identical-or-refuse merge may fail on Windows CI if the desktop and helper publishes emit different copies of a shared file. Task 48/49 silently took the helper's copy. Read-only inspection found a single Windows SDK projection package (`Microsoft.Windows.SDK.NET.Ref` 10.0.22000.57) and no CsWinRT package in the graph, so this is not expected. The first CI run settles it. If the merge does fail, the failing path is printed, and the owner must decide on a merge rule instead of letting the builder choose one.
- The CI launcher passes `safe.directory=*` through the process-scoped `GIT_CONFIG_*` variables, for the disposable user only, because the checkout belongs to the runner account. No Git config file is changed.
- `--disable-build-servers` and `MSBUILDDISABLENODEREUSE=1` avoid leaving build servers running. They do not change the build output.
