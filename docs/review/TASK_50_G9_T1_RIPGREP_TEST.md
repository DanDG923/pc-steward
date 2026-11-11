# Task 50 G9-T1 to G9-T4: the ripgrep real-update test (revision 1, draft)

Requirements: `FR-UPD-15`, `FR-UPD-01/08/09`, `TM-03/04/07/13`. ADRs: ADR-025, ADR-033. Sources: the [G9 target plan](TASK_50_G9_TARGET_PLAN.md) §7 and §12, the [package Confirm addendum](TASK_50_CHOCOLATEY_PACKAGE_CONFIRM_ADDENDUM.md) §3 and §6, the [native helper redeploy plan](TASK_50_NATIVE_HELPER_REDEPLOY_PLAN.md) §8, and the [security review](TASK_50_CHOCOLATEY_UPDATE_SECURITY_REVIEW.md) (M1).

**Nothing here runs by itself.** The owner runs each host step in their own window. Running a step is the owner's approval of that step, once.

## 1. Where things stand (2026-11-11)

| Gate | State |
|---|---|
| Security review fixes before G9-T1 | H1 **Fixed** (native helper, C3 PASS 2026-11-10). M1 accepted in ADR-025 (PR #30). |
| Probe PASS on the deployed build | C3 PASS on `d9a754b` (2026-11-10T20:17Z). |
| Package Confirm built and admitted | `d9a754b` contains PR #28: `ChocolateyPackageUpdateAdmission.OwnerApproved = true`, `AdmittedPackageId = "ripgrep"`. |
| G9-T3 (deploy the composition build) | **Already met.** CB3 deployed `d9a754b`, which carries the package composition, and C3 passed after it. The binding set has not changed since, so no new redeploy or probe is needed. |
| Test signer | `A935E493…A476`, NotAfter **2026-11-24T20:12:48Z**. G9-T4 has to run before then, or a new redeploy comes first. |
| Second reviewed Chocolatey release (G9 CR2) | Still blocked upstream (newest Chocolatey is 2.7.5). **It does not block this test.** CR2 only feeds the client self-update screen (`ChocolateyClientSelfUpdateV1`). The package route (`ChocolateyMachineRuntimeLimitsV3`) turns Confirm on from addendum §3 C1–C9, none of which needs a second listed release. |

**Feed re-check (cloud, 2026-11-11).** `FindPackagesById('ripgrep')` on the Community feed: 15.2.0 is still the latest approved version and no newer version exists, so the G9-T4 offer is 14.1.0 → 15.2.0, the pair §12 of the G9 plan reviewed. The 14.1.0 nupkg downloaded again today is 3,928,942 bytes, SHA-256 `646C5892…92F6` (the §12 pin), and its SHA-512 equals the feed's `PackageHash`.

## 2. G9-T1: install ripgrep 14.1.0 (owner, one administrator window)

The G9 plan said `choco install ripgrep --version 14.1.0` from Community, pinned to the §12 nupkg hash. To pin the bytes Chocolatey actually runs (not a second download after the check), the paste fetches the nupkg once into a new administrator-only folder, checks its length and SHA-256, and points `choco install` at that folder as its only source. Profile V2 is unaffected: the upgrade passes `--ignore-remembered-arguments` and the compiled Community source, `.sourceInstalledFrom` is display-only, and an unknown installed-origin feed is allowed.

What the paste does, in order, stopping at the first problem:

1. Checks the window is elevated, `ChocolateyInstall` is the native root, and neither `choco` nor PC Steward is running.
2. Refuses when a runtime code-loading name (`DOTNET_`, `COMPlus_`, `CORECLR_`, `COR_`, `APPDOMAIN_MANAGER_`, minus the two SDK-only names the guard allows) is set in the process, `HKCU\Environment`, `HKCU\Volatile Environment` (and its session subkeys) or the machine environment (security review M1: Chocolatey reloads these for its .NET children).
3. Refuses when ripgrep is already present: `lib\ripgrep`, any `ripgrep*` in `lib-bkp`, `lib-bad` or `.chocolatey`, or an `rg` on PATH other than the owner's WinGet copy (G9 criteria 7 and 9; the WinGet exception is the owner's decision in §6).
4. Compares `choco.exe`, `choco.exe.manifest` and every file under `helpers` and `tools` with the 56 pinned 2.7.5 entries (`ChocolateyReleaseManifest2_7_5`): expects `matched=56/56 changed=0 missing=0 extra=0`, no reparse points.
5. Creates `%ProgramData%\PCSteward-G9T1` (SYSTEM and Administrators only), downloads the 14.1.0 nupkg into it and checks length 3,928,942 and the §12 SHA-256.
6. Runs `choco install ripgrep --version 14.1.0 --source <that folder> --yes --no-progress --execution-timeout 300`.
7. Checks `choco list ripgrep --exact` gives exactly `ripgrep|14.1.0`, `lib\ripgrep\ripgrep.nupkg` exists (reports whether it is byte-identical to the reviewed nupkg), reports the `rg.exe` and shim counts, refuses `ripgrep*` residue in `lib-bkp`/`lib-bad`, and repeats check 4.
8. Always deletes the work folder it created, then prints `G9T1 RESULT PASS` or `G9T1 RESULT STOP`.

**Prompts.** One UAC prompt to open the administrator window. None from the paste. It never runs `rg`.

**What changes on the PC.** Chocolatey adds `lib\ripgrep`, its `.chocolatey\ripgrep.14.1.0` store folder and an `rg` shim in `bin`, and appends to its log. Undo: `choco uninstall ripgrep` in an administrator window (G9-T5, optional, after G9-T4).

**Paste-back.** Only the lines that start with `G9T1`. They carry no personal paths.

The paste is in the project files as `task50/g9t1/G9T1-install-ripgrep-v2.ps1.txt` (CRLF). The LF text below (revision 2, after the owner decision in §6) has SHA-256 `1150CADD5F875D529C76DAAF0C6CBEB371CA422758BA3ABA506BF9787F223C82`.

```powershell
# PC Steward G9-T1 (v2, WinGet rg kept): install ripgrep 14.1.0 from hash-checked bytes (run once, in an administrator PowerShell window).
& {
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$result = 'STOP'; $created = $false; $work = $null
$reviewedNupkg = '646C589224874BA7F20DE8C52CD6A6C7BEF5E8A978FAE955DEEA03ACBE9E92F6'
$clean = 'matched=56/56 changed=0 missing=0 extra=0'
$pins = @(
'4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0 12022880 choco.exe',
'D715D6071E5CDD6447D46ED8E903B9B3AD5952ACC7394EE17593D87A546C17FC 2997 choco.exe.manifest',
'B36F60CB783329F341ED0CD0075D9A0BA2863F835FE46A944CCE9F693E9C2480 40544 helpers/Chocolatey.PowerShell.dll',
'A0BCB2523147215E66A126F8B69E578043C14FEE3D72DF085F54C53E0D3CFBD9 59470 helpers/Chocolatey.PowerShell.dll-help.xml',
'4AF7D8DCDBA7335F96D4D7F9B7AB75B29A890380D8C7C35C59F60739DB8A604E 22385 helpers/chocolateyInstaller.psm1',
'ED69CDE7544EFE46ECBC66B10EDC55140E49CD2FA17F5CCF0E214D769E3CAD2B 15622 helpers/chocolateyProfile.psm1',
'859CFA5D9DC747A5BC5651331977BEEF2177CF8335A24A8F0A26D7965FD66944 17777 helpers/chocolateyScriptRunner.ps1',
'1A9137DE5F6B86E6BF4E478E7ACDF95433107E010FED460FAAD88D94EABB50E6 37972 helpers/ChocolateyTabExpansion.ps1',
'D47A44752FD6A983F9AB0E48AA8B12A2B0BC772EA0BB380C64723BB8E0B2CCBB 16054 helpers/functions/Format-FileSize.ps1',
'5F88F0BEAA6CCE518833111F16B532E33F80BA3EA5B878C2690B4FC1218CF3B2 26167 helpers/functions/Get-CheckSumValid.ps1',
'83EB927EFCD495E15FD4FF5D043E1F0CF4B2DCEDED9AEB5A4AF3DB0CDE2BFD8B 16318 helpers/functions/Get-ChocolateyConfigValue.ps1',
'18F1E1DC7EA4C3DAAE3FC51FD1373330C0132270180ED93BCAC7A1D2843353F5 17022 helpers/functions/Get-ChocolateyPath.ps1',
'BB77491D99FA16F09048E81A2CEDC29F3E6397D0D166BA2F72317ACA04347C25 25780 helpers/functions/Get-ChocolateyUnzip.ps1',
'5A763E6F6895FB36C99C942C56B2E5860E316978CE61FFB6D5A4599B357EAE4E 31820 helpers/functions/Get-ChocolateyWebFile.ps1',
'A0D1FCEC293A9D8B1340BBF54194884EF1C7495C3CBE9D4D5673EDF2E5CCFB43 23075 helpers/functions/Get-FtpFile.ps1',
'30F831B5189132D642EDFD7CC9E4F44B11AE357652E1748073D94206544D4B77 17145 helpers/functions/Get-OSArchitectureWidth.ps1',
'D537580623CA8088692AD463E8913A83EDB50963BD4B3B2B7B579E4E2B3B71F9 21718 helpers/functions/Get-PackageParameters.ps1',
'10EF1BD8A810EE08F601A207AC83A4C7D9EBAD1A4777378CF3749E3C56B98C48 17518 helpers/functions/Get-ToolsLocation.ps1',
'3F0C1C8C91696C6AE9C0E41589319D200D2C4BD16CABF4E2F1A11FC947A72F91 16240 helpers/functions/Get-UACEnabled.ps1',
'573FD644BEE61BF85053989C7111BE4A33223CE9BFD0AE5F95E05382FA08A1F5 20286 helpers/functions/Get-UninstallRegistryKey.ps1',
'73D85AA2297033F106A0C8C3138EFB9AD36F97ED108E040F12348FAE94C56F74 16118 helpers/functions/Get-VirusCheckValid.ps1',
'2DC9AE2D1DE175E6B867FF89F84BA25D08DD5F41B84E2818318CA23F3EB5797E 28730 helpers/functions/Get-WebFile.ps1',
'DB062D9D09D7DAE751E626BF97138EAE6E9350112E2738CB3BE9EF78DBDACE1A 24253 helpers/functions/Get-WebFileName.ps1',
'1D15D738C319132C792AC6F8820F50CCB0FC32597E9C886746BCC31FCCE2C683 20634 helpers/functions/Get-WebHeaders.ps1',
'767FD247F1F93CAC6188BA1A0C3398B87CF3178E25DED4A16CED7E9BB3CD27F6 20895 helpers/functions/Install-BinFile.ps1',
'223F17F84D214C9FA9478817EFF65A2681D505DFBFB6B81A2121E446E9614000 18759 helpers/functions/Install-ChocolateyEnvironmentVariable.ps1',
'1E2FB44E0BE817B5E16A03A30502C65F61DDDC551BD3923EA571E3F83980E049 19037 helpers/functions/Install-ChocolateyExplorerMenuItem.ps1',
'9C7C53D9EF8B902F60D5448ADBE96FC1EDA319C1DD6E6E7C8A4FFB3EB46C28DB 17879 helpers/functions/Install-ChocolateyFileAssociation.ps1',
'09E1973A0286C5912C7F233FCE89B2EFD9347EFDD085869437D9FCBE69A5C5D7 29245 helpers/functions/Install-ChocolateyInstallPackage.ps1',
'16B5DF14198360715D06A5F12F2B1976D38E729BBE37748E0CBB17F57C4F367E 31292 helpers/functions/Install-ChocolateyPackage.ps1',
'DBA9B9E9329FA5D918B1E941DBFED9363A616033CDFCAD4A0C60AF9C41C4C4AC 17373 helpers/functions/Install-ChocolateyPinnedTaskBarItem.ps1',
'AEEE93F35724D656A73D1572522FE9B985FA1CAE6978B0405398EF9327A1580D 23698 helpers/functions/Install-ChocolateyPowershellCommand.ps1',
'3410AEB9BC5106B29F2C4CBC74C9FEBDC229C569153DDB1E41188A7396079A3B 22580 helpers/functions/Install-ChocolateyShortcut.ps1',
'4E982CE84C225C6870CC78120E5F85FB622756FEFF4C7E8EB7088473A2538620 23306 helpers/functions/Install-ChocolateyVsixPackage.ps1',
'B0211091D5FE20E5E917855550EEFB2961BE6556A6F4F8A9C98619723A062CB7 24593 helpers/functions/Install-ChocolateyZipPackage.ps1',
'DAD0EF31EB232C6C189E0AD947E62E71C5239BF2DAD8F9D72A06CF3544A427A4 16518 helpers/functions/Install-Vsix.ps1',
'FFFB05319B00EFB87D2705760EF351C11AD2B1913469635B980D386310BF0E1F 16048 helpers/functions/Set-PowerShellExitCode.ps1',
'694C4DAED9ADD47D4ECE4BD07568AA57DBC1F3316426F78CE5FD1EF2F2CE2625 32129 helpers/functions/Start-ChocolateyProcessAsAdmin.ps1',
'F706530F0CDABB2F02C9D5B70D7DE77D1F02FC4F6730C815FF8410DCF208B9FC 18078 helpers/functions/Uninstall-BinFile.ps1',
'C20B736BCE859734C4497C6D5AAEC13BFA3C201461CC02F48A7539FEA54BE59E 17361 helpers/functions/Uninstall-ChocolateyEnvironmentVariable.ps1',
'07CED451A144A7F6E3FD24D19BFCB2E2A5EA49A969A036754CB833DC2D2986CB 20524 helpers/functions/Uninstall-ChocolateyPackage.ps1',
'599EA45533DC1AB68A9646C6A88B71F4FC11A8669FA3EE8F41360435CA8816DC 17275 helpers/functions/UnInstall-ChocolateyZipPackage.ps1',
'117E7BBFD11DA2F5BD00F66AA004837DD774485E96334FB42B8AC537F4FB012B 16311 helpers/functions/Write-FunctionCallLogMessage.ps1',
'1C57100CC237DB626259F68FE91C5A632A5128E0E01BAB1452BAA1104FF84311 1319424 tools/7z.dll',
'6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033 513 tools/7z.dll.manifest',
'C3598543C41918B5C94DAFB9DD724C16C9A217D36B5EE530E3BC10C9F12ACA6C 358912 tools/7z.exe',
'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 0 tools/7z.exe.ignore',
'6B7368CE5E38F6E0EE03CA0A9D1A2322CC0AFC07E8DE9DCC94E156853EAE5033 513 tools/7z.exe.manifest',
'519AC0A4BDED9C18EA02E0AFB71F663D8C47373BD9FACD3AC96A79F51D77765D 6031 tools/7zip.license.txt',
'04DDF72E7D930D3001D8111E1C5EA0A408A56D5BF4FFCF9C6680427375E31E1D 39008 tools/checksum.exe',
'9B250C32CBEC90D2A61CB90055AC825D7A5F9A5923209CFD0625FCA09A908D0A 150 tools/checksum.exe.config',
'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 0 tools/checksum.exe.ignore',
'29472B6BE2F4E7134F09CC2FADF088CB87089853B383CA4AF29C19CC8DFC1A62 95 tools/checksum.license.txt',
'9033F75F25BAE75853BCA02AAB9BBF1EA73D52CD256822F744820052B1CB70AF 568928 tools/shimgen.exe',
'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 0 tools/shimgen.exe.ignore',
'D4D2EF2C520EC3E4ECFF52C867EBD28E357900E0328BB4173CB46996DED353F4 3758 tools/shimgen.license.txt'
)
function Test-Release([string]$root) {
$want = @{}
foreach ($p in $pins) { $f = $p -split ' ', 3; $want[$f[2]] = $f }
$files = @(Get-Item -LiteralPath (Join-Path $root 'choco.exe'), (Join-Path $root 'choco.exe.manifest') -Force)
foreach ($t in 'helpers', 'tools') {
$all = @(Get-ChildItem -LiteralPath (Join-Path $root $t) -Recurse -Force)
if (@($all | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }).Count -gt 0) { throw ('reparse-point-in-' + $t) }
$files += @($all | Where-Object { -not $_.PSIsContainer })
}
$matched = 0; $changed = 0; $extra = 0
foreach ($f in $files) {
$rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
$w = $want[$rel]
if ($null -eq $w) { $extra++; continue }
$h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
if ($h -eq $w[0] -and $f.Length -eq [long]$w[1]) { $matched++ } else { $changed++ }
}
'matched=' + $matched + '/' + $want.Count + ' changed=' + $changed + ' missing=' + ($want.Count - $matched - $changed) + ' extra=' + $extra
}
try {
# 1. Window, client and running programs
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'not-an-administrator-window' }
$pd = [Environment]::GetFolderPath('CommonApplicationData')
$root = Join-Path $pd 'chocolatey'
$choco = Join-Path $root 'choco.exe'
if ($env:ChocolateyInstall -ne $root) { throw 'ChocolateyInstall-differs-from-the-native-root' }
if (Get-Process -Name 'choco' -ErrorAction SilentlyContinue) { throw 'choco-is-already-running' }
if (Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue) { throw 'close-PC-Steward-first' }
Write-Output 'G9T1 window-ok'
# 2. No code-loading settings that Chocolatey's .NET children would pick up (security review M1)
$prefixes = 'DOTNET_', 'COMPlus_', 'CORECLR_', 'COR_', 'APPDOMAIN_MANAGER_'
$allowed = 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO'
$names = @((Get-ChildItem Env:).Name)
foreach ($k in 'HKCU:\Environment', 'HKCU:\Volatile Environment') { if (Test-Path -LiteralPath $k) { $names += (Get-Item -LiteralPath $k).GetValueNames(); foreach ($s in @(Get-ChildItem -LiteralPath $k)) { $names += $s.GetValueNames() } } }
$names += (Get-Item -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').GetValueNames()
$hits = @($names | Where-Object { $n = [string]$_; ($allowed -notcontains $n) -and @($prefixes | Where-Object { $n.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
if ($hits.Count -gt 0) { throw ('runtime-control-names-set=' + $hits.Count) }
Write-Output 'G9T1 environment-ok'
# 3. ripgrep is not on this PC by any route
$present = @()
if (Test-Path -LiteralPath (Join-Path $root 'lib\ripgrep')) { $present += 'lib' }
foreach ($p in 'lib-bkp', 'lib-bad') { $q = Join-Path $root $p; if ((Test-Path -LiteralPath $q) -and @(Get-ChildItem -LiteralPath $q -Force -Filter 'ripgrep*').Count -gt 0) { $present += $p } }
$store = Join-Path $root '.chocolatey'
if ((Test-Path -LiteralPath $store) -and @(Get-ChildItem -LiteralPath $store -Force -Filter 'ripgrep*').Count -gt 0) { $present += 'store' }
$wingetRg = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\rg.exe'
$otherRg = @(Get-Command 'rg' -All -ErrorAction SilentlyContinue | Where-Object { $_.Source -ne $wingetRg })
if ($otherRg.Count -gt 0) { $present += 'rg-on-PATH-other-than-WinGet' }
Write-Output ('G9T1 winget-rg-present ' + (Test-Path -LiteralPath $wingetRg) + ' (kept, owner decision)')
if ($present.Count -gt 0) { throw ('ripgrep-already-present=' + ($present -join ',')) }
Write-Output 'G9T1 not-installed-ok'
# 4. Chocolatey's own files still match the reviewed 2.7.5 release
$before = Test-Release $root
Write-Output ('G9T1 release-before ' + $before)
if ($before -ne $clean) { throw 'release-files-differ-before-install' }
# 5. Fetch ripgrep 14.1.0 into a new administrator-only folder and check its bytes
$work = Join-Path $pd 'PCSteward-G9T1'
if (Test-Path -LiteralPath $work) { throw 'work-folder-already-exists' }
New-Item -ItemType Directory -Path $work | Out-Null; $created = $true
& (Join-Path ([Environment]::SystemDirectory) 'icacls.exe') $work /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'icacls-failed' }
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$nupkg = Join-Path $work 'ripgrep.14.1.0.nupkg'
Invoke-WebRequest -UseBasicParsing -Uri 'https://community.chocolatey.org/api/v2/package/ripgrep/14.1.0' -OutFile $nupkg
$len = (Get-Item -LiteralPath $nupkg).Length; $h = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash
Write-Output ('G9T1 nupkg ' + $len + ' ' + $h)
if ($len -ne 3928942 -or $h -ne $reviewedNupkg) { throw 'nupkg-differs-from-the-reviewed-bytes' }
if (@(Get-ChildItem -LiteralPath $work -Force).Count -ne 1) { throw 'work-folder-has-unexpected-files' }
# 6. Install with Chocolatey's own tooling, from the checked file only
Write-Output 'G9T1 installing (Chocolatey output follows)'
& $choco install ripgrep --version 14.1.0 --source $work --yes --no-progress --execution-timeout 300
$exit = $LASTEXITCODE
Write-Output ('G9T1 choco-exit ' + $exit)
if ($exit -ne 0) { throw 'choco-install-failed' }
# 7. Check the result
$listed = @(& $choco list ripgrep --exact --limit-output --no-color)
Write-Output ('G9T1 choco-list ' + ($listed -join ' ; '))
if ($listed.Count -ne 1 -or $listed[0] -ne 'ripgrep|14.1.0') { throw 'choco-list-does-not-show-ripgrep-14.1.0' }
$lib = Join-Path $root 'lib\ripgrep'
$ln = Join-Path $lib 'ripgrep.nupkg'
if (-not (Test-Path -LiteralPath $ln)) { throw 'lib-nupkg-missing' }
$lh = (Get-FileHash -LiteralPath $ln -Algorithm SHA256).Hash
Write-Output ('G9T1 lib-nupkg-same-as-reviewed ' + ($lh -eq $reviewedNupkg))
$exes = @(Get-ChildItem -LiteralPath (Join-Path $lib 'tools') -Recurse -Force -Filter 'rg.exe')
Write-Output ('G9T1 rg-exe-count ' + $exes.Count + ' shim ' + (Test-Path -LiteralPath (Join-Path $root 'bin\rg.exe')))
foreach ($p in 'lib-bkp', 'lib-bad') { $q = Join-Path $root $p; if ((Test-Path -LiteralPath $q) -and @(Get-ChildItem -LiteralPath $q -Force -Filter 'ripgrep*').Count -gt 0) { throw ('ripgrep-left-in-' + $p) } }
Write-Output ('G9T1 rg-on-PATH-first-is-chocolatey ' + ((@(Get-Command 'rg' -All -ErrorAction SilentlyContinue)[0]).Source -eq (Join-Path $root 'bin\rg.exe')))
$after = Test-Release $root
Write-Output ('G9T1 release-after ' + $after)
if ($after -ne $clean) { throw 'release-files-changed-during-install' }
$result = 'PASS'
} catch { Write-Output ('G9T1 STOP ' + $_.Exception.Message) }
finally { if ($created) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue; Write-Output ('G9T1 work-folder-removed ' + (-not (Test-Path -LiteralPath $work))) } }
Write-Output ('G9T1 RESULT ' + $result)
}
```

## 3. G9-T2: the in-app check (owner, normal window; nothing runs)

The G9 plan's G9-T2 re-ran the host-evidence Default cases to show the tree still matches the release and the profile still admits it. G9-T1 step 4 now covers the release match before and after the install, so G9-T2 becomes PC Steward's own production check, which is stricter than O5:

1. Start PC Steward from `C:\Program Files\PC Steward` (Explorer, not a terminal), scan, and open ripgrep's detail page.
2. Press **Review Chocolatey update…**. Expected: the dialog checks the package, shows "ripgrep 14.1.0 → 15.2.0", the Profile V2 disclosure lines and when the review expires, and Confirm is on with no blocking reasons.
3. Anything else (a refusal, a different version, Confirm off) stops here: close the dialog and report the reason text.

## 4. G9-T4: the one real update (owner; exactly one UAC prompt)

Only after G9-T2 shows Confirm on, and with its own approval:

1. In the same dialog, press **Update ripgrep**. Expect exactly one UAC prompt, for `PCSteward.ElevatedHelper.exe` signed by `A935E493…`. Any other prompt, or a second one: answer **No** and stop.
2. Let it run to the result; do not close PC Steward.
3. Report the result text and the history row. The P1–P4 evidence is written up in §6 from the result, the history row and a read-only post-update check.

## 5. Stop rules

As G9 plan §8. A STOP in G9-T1 is not retried without a new paste. A G9-T4 failure leaves the claim and conflict record; PC Steward does not retry or repair.

## 6. Results

### G9-T1 run 1 (2026-11-11, about 10:31Z): STOP, nothing changed

`G9T1 window-ok`, `G9T1 environment-ok`, then `G9T1 STOP ripgrep-already-present=rg-on-PATH`. The run stopped at check 3, before the release check, the work folder, the download or any install. The owner's follow-up showed one `rg` on PATH: the WinGet links folder under the owner's local application data, `ripgrep 15.2.0`. It was installed through WinGet after criterion 7 was confirmed on 2026-11-06.

### Owner decision (2026-11-11, 10:32Z): keep the WinGet copy and test ripgrep anyway

Options offered: keep it and test ripgrep (recommended), remove the WinGet copy first, or switch to fd (which needs an allowlist code change, review and a redeploy). The owner chose **keep it**. Consequences, as stated on the card: while the test runs, Chocolatey's `bin` shim comes first on PATH (machine PATH before user PATH), so `rg` is 14.1.0 and then 15.2.0, from the same upstream bytes. The WinGet copy is untouched, and `choco uninstall ripgrep` puts it back first on PATH. Paste revision 2 narrows check 3 to allow only that WinGet copy, and adds two information lines.
