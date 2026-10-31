# Task 90 — Task 50 Chocolatey mapping remediation

Requirements: `FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03–04/07/13`. This records remediation of the required Task50 mapping review; it does not start another product feature or authorize a host action.

## Reviewed snapshot and findings

Independent Opus review of HEAD `7211dc52d4100bb4d880a9f845a40e511d2392b0`, clean tree and profile SHA256 `C2A1D46772349FD9B847E1E67D9188043FD8C548904865EF2C62FAAE787CC7D0` was **CONDITIONAL**. It ran no tests/builds/edits or host/provider actions. The original profile stays pinned until the findings are reconciled.

| Finding | Disposition |
|---|---|
| B1 native launch environment | Implement an isolated pure compiled environment builder with fixtures; no admitting native caller. Runtime-loader inputs must not be inherited. Complete loader/working-directory/path trust remains an independent native prerequisite. |
| B2 configuration | Need the exact effective defaults, setExplicitly behavior, duplicate refusal, main/backup behavior and a closed relevant-input table. No parser claims completeness yet. |
| B3 licensing | Four fixed paths are insufficient. Need full reachable CLR binding inputs and protected absence or refusal, not a user-profile absence snapshot as enforcement. |
| B4 store identity | Bind normalized/original/four-part/two-part variants and limits; pending/bad/backup package handling requires native source evidence. |
| M1 result codes | Define provider exit facts separately from native target verification/settlement; do not implement Task51 restart inference here. |
| M2 self/client updates | Need explicit target exclusion plus before/after client-tree checks; dependency effects are separately disclosed, not ignored or sand-boxed. |
| M3 archives | Need local/central-header agreement, compression/name/XML encoding/namespace policy and complete bounded reads. |
| L1 wording | Historical unavailability means pending native admission, not proof that the profile can never be supported. |

The reviewer allowed isolated pure parser/policy/environment fixtures before native admission. Some rationale is still reported/general runtime behavior, not a confirmed old-route exploit. Main independently confirmed Microsoft's [.NET Framework binding order](https://learn.microsoft.com/en-us/dotnet/framework/deployment/how-the-runtime-locates-assemblies): app/publisher/machine configuration, GAC, codeBase and probing affect binding. Exact supported native coverage still needs implementation/evidence.

## B1 producing and consuming contract before implementation

Provider-internal `ChocolateyLaunchPaths` contains only native-observed Windows/Program Files/profile/app-data paths, the fixed canonical Chocolatey root and the already verified protected configured cache. It is passive formatting input, not proof that those paths are trusted. A future native observer must obtain paths from OS/token APIs, validate ancestors/ACL/reparse/file identity and bind them through launch; no request/database strings produce them.

Provider-internal `ChocolateyCompiledEnvironment.Create(paths)` returns an immutable case-insensitive dictionary built from scratch. Exact variable set: SystemRoot, WINDIR, SystemDrive, ComSpec, PATH, PATHEXT, ProgramFiles, ProgramW6432, ProgramFiles(x86), USERPROFILE, APPDATA, LOCALAPPDATA, ChocolateyInstall, TEMP, TMP, PSModulePath, PROCESSOR_ARCHITECTURE. System PATH/module entries derive only from the native-observed protected Windows/Program Files roots. TEMP/TMP equal the verified cache. The supported platform is Windows11 x64, so PROCESSOR_ARCHITECTURE is the fixed AMD64 value.

`ApplyTo(ProcessStartInfo, paths)` is the future launcher consumer: require UseShellExecute=false, construct/validate the entire dictionary first, then clear and replace all inherited entries. It neither starts a process nor chooses an executable/arguments. No COR_*, COMPlus_*, DOTNET_*, CORECLR_*, APPDOMAIN_MANAGER_*, DEVPATH, __COMPAT_LAYER, inherited proxy, inherited PATH or arbitrary parent entry survives. A future relevant variable must be explicitly added/reviewed; copying a blacklist-filtered parent dictionary is forbidden. Path-format validation rejects relative/UNC/device/expanded/ambiguous or separator-injecting values; it does not substitute for native trust checks.

The future launcher also uses a compiled verified working directory at the fixed client root, never inherited current directory. Working directory and environment are not operation fields. Package-script environment refresh remains the accepted Community script effect after protected bootstrap; it is not permission for a pre-bootstrap native load. Environment-only proxy preferences will not be inherited by this profile; supported observed proxy configuration must be handled explicitly in B2. This may make a configuration unavailable or fail networking; never silently retry with a permissive environment. The legacy protocol1 launcher remains unchanged.

## Public release artifact evidence

Main read public [release2.7.5 metadata](https://github.com/chocolatey/choco/releases/tag/2.7.5): asset684796542, `chocolatey.2.7.5.nupkg`, length5,719,618 bytes, GitHub digest `F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839`. A bounded HTTPS download followed only github.com/release-assets.githubusercontent.com/objects.githubusercontent.com, enforced exact length/digest, and inspected ZIP streams in an isolated task temporary file. No extraction, installer/package execution, dependency install/restore, GAC change or owner's installed-client read occurred.

| ZIP entry | Uncompressed length | SHA256 |
|---|---:|---|
| tools/chocolateyInstall/choco.exe | 12,022,880 | 4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0 |
| tools/chocolateyInstall/redirects/choco.exe | 147,040 | E8DB87C40BAD54ECFDFDE1F7097090C457A9DB1E533F88AD56E274AD0DA9FA29 |

A temporary self-compiled PEReader utility read the engine's metadata from ZIP memory without loading its assembly: Assembly/FileVersion2.7.5.0, InformationalVersion2.7.5, TargetFramework.NETFramework4.8; no source commit attribute was observed. The released bytes are pinned; there is no reproducible-build/source attestation claim.

Pinned [chocolateysetup.psm1](https://github.com/chocolatey/choco/blob/194ac6262ac75571c44aa0a431c3ab6f36fa73be/nuspec/chocolatey/chocolatey/tools/chocolateysetup.psm1), blob `c66c1282f3d2f50efcdf68cc4b287753e4c444c5`, lines418–423 copies the engine to root/choco.exe; Install-ChocolateyBinFiles473 onward copies the prebuilt redirector to bin/choco.exe. Current protocol1 trusts the latter. Proposed corrected **new** profile invokes the fixed signed root/choco.exe directly and pins its release digest; spec732 requires the canonical ProgramData root, not the bin redirector. The old route is untouched. This avoids treating a shim hash as engine identity; exact helper/tool manifests and reachable loading still need review/implementation.

Research files are in task-specific temporary directories `pcsteward-task50-release-13c8372de24e4457ae01129793fe133f` and `pcsteward-task50-release-metadata-bfbe31ae0465415ca45ea427cb85a2ce`. They contain public artifact bytes and the own metadata reader only, not owner application data. They grant no execution/deployment authority.

## Validation

B1's pure formatting component is implemented with no production caller. Tests preceded it: `%TEMP%\pc-steward-task50-choco-environment-red.log` records CS0246 for the absent input type. The first implementation run found analyzer CA1861 in the test's constant-array argument; making the reviewed key list static resolved it without changing policy. Focused `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ChocolateyCompiledEnvironmentTests` passes14/14. Fixtures cover exact immutable case-insensitive keys/values, poisoned-parent replacement, ambiguous/expanded/relative/device/UNC/path-list injection and shell-execution refusal before touching the parent block. No child is started.

Using the established process-only SDK10.0.403 PATH/DOTNET_ROOT, `& ./scripts/Build.ps1` exits0 with zero warnings/errors; `& ./scripts/Test.ps1` exits0, full701/701, zero skipped (Domain131/Application443/Architecture30/Security97). Logs `%TEMP%\pc-steward-task50-choco-environment-{green,build,test}.log`. Diff/whitespace and source scope review pass. Source call-site search shows only the new tests consume the builder. The legacy process-start code and protocol1 remain untouched.

These checks close the isolated builder implementation, not the native path/loader proof or all of B1. Other findings remain open; no fresh review pass, admitting observer, endpoint integration or Task50 completion is claimed. Source-reported config/store/licensing corrections are retained for the next profile revision, not silently applied to the independently pinned original.
