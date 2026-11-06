# Task 50 — Profile V2 revision 2.1: the `containsLegacyPackageInstalls` config key

Requirements: `FR-UPD-15`, `TM-03/04/07/13`. Revision of the [Chocolatey Profile V2 mapping](TASK_50_CHOCOLATEY_PROFILE_V2.md), "Effective configuration, cache and source checks". It fixes cause 1 of the Part C step-6 refusal ([Part C plan §12–§13](TASK_50_PART_C_PLAN.md); owner decision "Go with option 2", 2026-11-06). **Code and tests only. No host action, no redeploy and no new probe are authorized by this record.** The change still needs independent review and Windows CI.

## 1. Finding

On the owner's host, the elevated observer refused at step 6 (`ChocolateyProfileObserver.Configuration`, `MachineInputsUnsupported`). The read-only D1 diagnostic found that the real Chocolatey 2.7.5 `C:\ProgramData\chocolatey\config\chocolatey.config` has these properties:
- 9,464 B, with a UTF-8 BOM;
- 30 known features, 1 source and 0 API keys;
- 12 config keys: the 11 that `ChocolateyConfigurationParser.ConfigNames` admits, plus `containsLegacyPackageInstalls`.

The strict parser refuses any unknown key, so step 6 refused before `cacheLocation` was evaluated. The host's `cacheLocation` is also empty. Profile V2 refuses an empty `cacheLocation` by design, and **this revision does not change that rule**.

## 2. Research (primary source only)

**Source.** github.com/chocolatey/choco, tag `2.7.5`. This is a lightweight tag on commit `194ac6262ac75571c44aa0a431c3ab6f36fa73be`, the reviewed client source pinned in Profile V2. It was fetched read-only at that exact commit. The cited blob IDs were computed from the checked-out bytes. For two of them, the method was checked against Profile V2's existing pins:
- `nuspec/chocolatey/chocolatey/tools/chocolateysetup.psm1` = `c66c1282…`;
- `src/chocolatey.console/Program.cs` = `e7fd54ae…`.

The NuGet fork was not fetched. It never receives the `chocolatey.config` path: the client deserializes that file itself and passes typed values on (R4).

| # | File (blob) | Lines | Finding |
|---|---|---|---|
| R1 | `src/chocolatey/infrastructure.app/configuration/chocolatey.config` (`216f20b4…`) | 5 | The embedded default template contains `<add key="containsLegacyPackageInstalls" value="true" />`, with no description. It is the second of six template keys, after `cacheLocation`. |
| R2 | `src/chocolatey/infrastructure/extractors/AssemblyFileExtractor.cs` (`73c9fe45…`) | 43–58 | `ExtractTextFileFromAssembly` writes the template only when the file does not exist (`overwriteExisting` defaults to false). |
| R3 | `src/chocolatey/infrastructure.app/builders/ConfigurationBuilder.cs` (`4e366a52…`) | 82–100, 102–108, 110–121 | `SetupConfiguration` extracts the template (R2), deserializes `config/chocolatey.config` into `ConfigFileSettings`, applies it, then serializes it back. Entries it does not know are kept as they are. |
| R4 | same file | 185–197, 247–315, 317–339 | `SetFileConfiguration` → `SetAllConfigItems` reads config keys only through `SetConfigItem(name, …)`, which looks up one named key. The names read are `cacheLocation`, `commandExecutionTimeoutSeconds`, `webRequestTimeoutSeconds`, `proxy`, `proxyUser`, `proxyPassword`, `proxyBypassList`, `proxyBypassOnLocal`, `upgradeAllExceptions`, `defaultTemplateName` and `defaultPushSource`. `containsLegacyPackageInstalls` is never read. `SetConfigItem` adds a missing named key with its default and description, so a fresh 2.7.5 file has 6 template keys plus 6 added keys = **12 keys**, which matches D1. |
| R5 | `src/chocolatey/infrastructure.app/ApplicationParameters.cs` (`8c12a40d…`) | 233–246 | `ApplicationParameters.ConfigSettings` defines exactly the 11 names in R4. There is no constant for the legacy key. |
| R6 | `…/configuration/ConfigFileConfigSetting.cs` (`770ca17e…`) 25–36; `…/ConfigFileSettings.cs` (`e394d3e3…`) 30–31 | — | Config entries are a generic `HashSet` of `add` elements with `key`, `value` and `description` attributes. No property is bound to any particular key. |
| R7 | `src/chocolatey/infrastructure.app/services/ChocolateyConfigSettingsService.cs` (`e7dc1b5d…`) | 477–501, 503–523, 525–591 | The generic reads are `choco config list`, `get`, `set` and `unset`. They are not used by the reviewed routes. |
| R8 | `src/chocolatey.resources/helpers/functions/Get-ChocolateyConfigValue.ps1` (`564a7944…`) | 16, 52–58 | A package-script helper (2.1.0+). It returns the `value` of any config key a script names. Nothing in the release calls it for this key. |
| R9 | `tests/pester-tests/commands/choco-config.Tests.ps1` (`3822126b…`) | 18, 86 | The vendor's own test expects `containsLegacyPackageInstalls = true` in a default `config list`. |
| R10 | whole tree at the commit | — | A case-insensitive search finds `containsLegacyPackageInstalls` only at R1 and R9. `chocolateysetup.psm1` (the release's install and self-update setup) and `chocolateyInstall.ps1` never read the config file. The setup's `$env:SystemDrive\Chocolatey` constant (line 4) is the old insecure-root check, unrelated to this key. |

**Conclusion.** In 2.7.5, the key is **written once** by the default template and **read by no engine code**. That covers:
- the reviewed routes: `list`, `outdated`, `upgrade` of a package, and client self-update;
- path resolution: root, lib, cache and temp;
- legacy `C:\Chocolatey` or lib handling;
- source resolution, the timeouts and the proxy.

The client round-trips the key unchanged (R3, R6). Only two things can observe its value:
- the user-invoked `choco config` commands, which are not on any reviewed route (R7);
- a package script that calls `Get-ChocolateyConfigValue` (R8).

A package script can read every admitted config key the same way. That falls under the accepted Community script residual of Profile V2. The key is inert for every route the profile models.

## 3. Decision

1. **Admit the key only with its template value.** `containsLegacyPackageInstalls` joins `ConfigNames`. The entry is admitted only when its `value` attribute is exactly `true` (ordinal, case-sensitive, untrimmed), which is the byte value of R1. Any other value refuses at the parser, so step 6 refuses as before. That includes `false`, `True`, `TRUE`, the empty string from `choco config unset`, padded forms, `1` and `yes`. A missing `value` attribute also refuses.
2. **Absence stays admitted.** The key is optional, as every admitted key already is. The engine never re-adds it (R4), so an absent key stays absent.
3. **Everything else follows the existing rules.**
   - Key-name matching stays invariant-culture case-insensitive with no trimming, consistent with the other keys and with the client's `IsEqualTo` (R4, R7).
   - A duplicate in any case refuses.
   - The attribute set stays `key`/`value`/`description`, and the description is ignored.
4. **No observation or fingerprint field.** `ChocolateyConfigurationObservation` and the `pc-steward-choco-profile:2` normal form do not change. With one admitted value, the key carries no information. Even an admitted absent-to-`true` change between desktop and helper observations could not change any reviewed route's behaviour (§2). The common fingerprint already treats four admitted keys the same way, with *any* value and no field: `proxyBypassList`, `upgradeAllExceptions`, `defaultTemplateName` and `defaultPushSource`. `proxyBypassOnLocal` likewise has no field of its own. The raw-byte audit hash (`ChocolateyConfigurationObservation.Fingerprint`) still changes, as it does for any byte change.
5. **Every other Profile V2 rule is unchanged.**
   - the 30 known features and their attribute set;
   - the six required effective features;
   - the source rules (no negative priority; the exact Community key match);
   - the bounds and encodings;
   - the `cacheLocation` rule: nonempty, canonical, absolute, local, no `%`, existing, and requester-unmodifiable with its ancestors (an empty value still refuses);
   - the helper launch-path checks.

   `HasSupportedMode`, the capability allow-list and the dispatcher are not touched.

**Why no Profile V2 guarantee is weakened.** The parser stays a closed allow-list. One more name is admitted, with exactly one value, and that value is the inert vendor default. A value the profile does not model refuses, which is the same fail-closed result as before. The semantic snapshot and fingerprint keep every behaviour-relevant input they had. Nothing new reaches the CLI, the environment or a request option. The file stays a protected, requester-unmodifiable input read through a held handle (step 6), so the requester cannot add or change the key.

## 4. Change

| File | Change |
|---|---|
| `src/PCSteward.Provider.Chocolatey/ChocolateyConfigurationParser.cs` | `LegacyPackageInstallsKey` and `LegacyPackageInstallsTemplateValue` constants; the key added to `ConfigNames`; a value rule in the `config` branch that refuses any other value. |
| `tests/PCSteward.Security.FixtureTests/Chocolatey275ConfigurationFixture.cs` (new) | A real-shape 2.7.5 file built from R1–R6 and D1: UTF-8 BOM, `xmlns:xsd`/`xmlns:xsi`, the order config, sources, features, apiKeys, 12 keys, the Community source with the serializer's attributes, 30 features with `setExplicitly="false"`, no API keys, and shortened descriptions. |
| `tests/PCSteward.Security.FixtureTests/ChocolateyProfileRulesTests.cs` | Portable tests. The real shape with `true` and a protected-format `cacheLocation` passes the parser and every portable step-6 rule (required features, sources, cache format, Community resolution). Each other value refuses. Empty `cacheLocation` still fails the cache rule. |
| `tests/PCSteward.Security.FixtureTests/ChocolateyProfileObserverTests.cs`, `N2ProfileFixture.cs` | Windows-only tests through the real observer. The real shape with `true` is Observed, with no failed step, and its fingerprint equals the fingerprint computed with the key absent. The value `false` refuses at step 6 (`StepRule`). An empty `cacheLocation` with the key admitted still refuses at step 6 (`StepRule`). |
| `tests/PCSteward.Application.IntegrationTests/ChocolateyConfigurationParserTests.cs` | Admitted forms: `true`, with a description, and a case variant of the key name. Refused: 13 other values and shapes (listed in §3). |
| `tests/PCSteward.Application.IntegrationTests/ChocolateyCommonProfileFingerprintTests.cs` | The key with `true` keeps the existing golden `C179F864…EE77`; the raw audit hash differs. |

**Pins and fingerprints.** Unchanged:
- `pc-steward-choco-profile:2` and its goldens (`C179F864…EE77`, code list `3E8129A2…9823`);
- the registration digest;
- the release manifest;
- the capability statement `50F169DD…654B`;
- the probe script `831B5E17…E77A`.

The build output does change. Any future deployment of this code is a new candidate with new desktop and helper hashes, and it needs its own C-B-style redeploy and a new probe approval (Part C plan §13).

## 5. Validation (Linux, SDK 10.0.403; `dotnet build -c Release -p:EnableWindowsTargeting=true`)

The build has the same two Linux-only errors as base `af34ae1`:
- the WinUI `XamlCompiler.exe` in `PCSteward.Desktop`;
- the missing `PCSteward.ProcessFixture.exe` copy.

The rebuild used `--no-incremental`.

| Suite | Base `af34ae1` | This change |
|---|---|---|
| Architecture | 140/140 | 140/140 |
| Domain | 175/175 | 175/175 |
| Application integration | 886 passed, 112 failed | 903 passed, 112 failed: the same 112, +17 new passing |
| Security fixtures | 1,338 passed, 240 failed, 6 skipped | 1,345 passed, 243 failed, 6 skipped: the same 240, +7 new passing, plus the 3 new Windows-only observer tests (`PlatformNotSupportedException: Requester tokens exist only on Windows`, like every other test in that class) |

Narrow runs:
- `ChocolateyConfigurationParserTests`, `ChocolateyCommonProfileFingerprintTests` and `ChocolateySourceResolutionTests`: 76/76;
- `ChocolateyProfileRulesTests`: 33/33.

The three Windows-only observer tests need Windows CI.

## 6. Limits

- **The host's value was not observed.** D1 printed key names only. A fresh 2.7.5 install writes `true` (R1, R9). If the host's value is anything else, step 6 still refuses. That would be fail-closed, and a read-only follow-up could confirm it (for example `value-equals-template=True/False`, printing no value).
- **Cause 2 still blocks the route.** The empty `cacheLocation` refuses by design. Only the owner can change it, outside PC Steward.
- **The future client is not covered.** For a client self-update, the newer release's own scripts and engine are outside the 2.7.5 source. This revision makes no claim about how a later client treats the key; the existing post-update verification applies.
- **Next steps:** independent review and Windows CI. A redeploy, the owner's `cacheLocation` change and a new probe each need their own approval. Part C stays recorded as FAIL.
