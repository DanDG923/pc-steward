[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string]$MsiVersion,
    [Parameter(Mandatory)][string]$OutputPath,
    # Optional: the dotnet.exe of a pinned SDK folder (New-PCStewardMsiReference.ps1 -PinnedSdk). Without it, dotnet on PATH.
    [string]$Dotnet
)

# ADR-034 §1 and §5 (FR-UPD-04/15, TM-04/13): build the PC Steward MSI from a complete payload folder (signed executables and
# PCSteward.ReleaseManifest.json already written), with the WiX SDK pinned in packaging\msi\PCSteward.Installer.wixproj, then
# check the built package: no services and no custom action that runs code, the protected install-folder DACL, the fixed
# UpgradeCode and the version asked for. Local only: no signing, install, elevation or certificate store. The only network
# effect is one download of the WiX SDK package from the nuget.org address pinned in scripts\release-toolchain-pins.json
# (msiSdk). Mythos whole-app review MY-01: its size and SHA-512 must equal the pin before any of its code runs, and the build
# restores only from a local feed that holds that one checked file, into a new empty package folder.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$script:UpgradeCode = '{2AD17330-BAD5-46BE-A409-20814B26673C}'
$script:FolderSddl = 'D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'

if ($MsiVersion -notmatch '\A([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,5})\z' -or [int]$Matches[1] -gt 255 -or [int]$Matches[2] -gt 255 -or [int]$Matches[3] -gt 65535) {
    throw "MsiVersion must be major.minor.build within Windows Installer limits (255.255.65535): $MsiVersion"
}
$payload = [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\')
foreach ($required in @('PCSteward.ReleaseManifest.json', 'PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'Assets\pc-steward.ico')) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "The payload is missing $required." }
}
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "OutputPath already exists: $output" }

$build = Join-Path ([IO.Path]::GetTempPath()) ('pcsteward-msi-' + [Guid]::NewGuid().ToString('N'))
try {
    # The build runs on copies of the tracked project files (and the repository's SDK pin) in a new folder, so no other file
    # in the clone, tracked or not, can reach it: MSBuild, NuGet and the .NET host look upwards from the project folder for
    # build hooks, and none may sit above that folder either.
    $source = Join-Path $build 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    foreach ($file in @('packaging\msi\PCSteward.Installer.wixproj', 'packaging\msi\Package.wxs', 'packaging\msi\Directory.Build.props', 'packaging\msi\Directory.Packages.props', 'global.json')) {
        $item = Get-Item -LiteralPath (Join-Path $repositoryRoot $file) -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "$file is not a plain file." }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $source $item.Name)
    }
    # MY-01: the WiX SDK is the one package this build restores. It is checked byte for byte against the committed pin before
    # dotnet is started, then offered to NuGet as the only file of the only package source, with every other source, fallback
    # folder and package folder cleared, so NuGet cannot fetch or reuse anything else.
    $wix = ([IO.File]::ReadAllText((Join-Path $repositoryRoot 'scripts\release-toolchain-pins.json')) | ConvertFrom-Json).msiSdk
    if (!$wix -or [string]$wix.id -cne 'WixToolset.Sdk' -or [string]$wix.version -cnotmatch '\A\d+\.\d+\.\d+\z' -or
        [string]$wix.sha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==\z' -or [long]$wix.bytes -lt 1MB -or [long]$wix.bytes -gt 256MB) {
        throw 'scripts\release-toolchain-pins.json has no valid msiSdk pin.'
    }
    $wixId = ([string]$wix.id).ToLowerInvariant(); $wixVersion = [string]$wix.version
    $wixUrl = "https://api.nuget.org/v3-flatcontainer/$wixId/$wixVersion/$wixId.$wixVersion.nupkg"
    if ([string]$wix.url -cne $wixUrl) { throw "The pinned WiX SDK must be nuget.org's $wixUrl." }
    if (!([IO.File]::ReadAllText((Join-Path $source 'PCSteward.Installer.wixproj'))).StartsWith("<Project Sdk=`"$($wix.id)/$wixVersion`">", [StringComparison]::Ordinal)) {
        throw 'The installer project names another WiX SDK than the pinned one.'
    }
    $feed = Join-Path $build 'feed'
    $packages = Join-Path $build 'packages'
    New-Item -ItemType Directory -Path $feed | Out-Null
    New-Item -ItemType Directory -Path $packages | Out-Null
    $wixPackage = Join-Path $feed "$wixId.$wixVersion.nupkg"
    $previousProgress = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    try { Invoke-WebRequest -Uri $wixUrl -OutFile $wixPackage -UseBasicParsing } finally { $ProgressPreference = $previousProgress }
    function Get-FileSha512Base64([string]$Path) {
        $sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
    }
    if ((Get-Item -LiteralPath $wixPackage).Length -ne [long]$wix.bytes) { throw "The downloaded WiX SDK is not the pinned $($wix.bytes) bytes." }
    if ((Get-FileSha512Base64 $wixPackage) -cne [string]$wix.sha512) { throw 'The downloaded WiX SDK does not match its pinned SHA-512.' }
    $escapedFeed = [Security.SecurityElement]::Escape($feed); $escapedPackages = [Security.SecurityElement]::Escape($packages)
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="pinned-wix-sdk" value="$escapedFeed" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="pinned-wix-sdk">
      <package pattern="$($wix.id)" />
    </packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
  <config>
    <add key="globalPackagesFolder" value="$escapedPackages" />
  </config>
</configuration>
"@
    [IO.File]::WriteAllText((Join-Path $source 'nuget.config'), $nugetConfig, [Text.UTF8Encoding]::new($false))
    $hookNames = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Build.rsp', 'Directory.Packages.props', 'Directory.Solution.props', 'Directory.Solution.targets', 'NuGet.Config', 'global.json')
    for ($folder = Split-Path -Parent $source; $folder; $folder = Split-Path -Parent $folder) {
        foreach ($name in $hookNames) { if (Test-Path -LiteralPath (Join-Path $folder $name)) { throw "A build hook sits above the MSI build folder: $(Join-Path $folder $name)" } }
    }
    $project = Join-Path $source 'PCSteward.Installer.wixproj'
    $buildOutput = Join-Path $build 'out'
    $arguments = @('build', $project, '--configuration', 'Release', '--output', $buildOutput, '--no-incremental', '--disable-build-servers', '-nologo',
        "-p:PayloadDir=$payload", "-p:ProductVersion=$MsiVersion")
    # --no-incremental: the version and payload come in as properties, which an incremental build does not see change.
    # The build log goes to the console, never into this script's output (the last output line is the JSON result).
    # The package folder and HTTP cache are the new empty ones for this build only (an inherited NUGET_PACKAGES, as the release
    # workflow sets, would otherwise win over nuget.config). With -Dotnet, the pinned SDK is the only one dotnet can find.
    $dotnetCommand = 'dotnet'
    $savedEnvironment = @{}
    $buildEnvironment = @{ NUGET_PACKAGES = $packages; NUGET_HTTP_CACHE_PATH = (Join-Path $build 'http-cache'); NUGET_FALLBACK_PACKAGES = $null }
    if ($Dotnet) {
        $dotnetPath = [IO.Path]::GetFullPath($Dotnet)
        if ([IO.Path]::GetFileName($dotnetPath) -cne 'dotnet.exe' -or !(Test-Path -LiteralPath $dotnetPath -PathType Leaf)) { throw "Dotnet must name a dotnet.exe: $Dotnet" }
        $dotnetCommand = $dotnetPath
        $sdkRoot = Split-Path -Parent $dotnetPath
        $buildEnvironment['DOTNET_ROOT'] = $sdkRoot
        $buildEnvironment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $buildEnvironment['PATH'] = $sdkRoot + [IO.Path]::PathSeparator + $env:PATH
    }
    foreach ($name in @($buildEnvironment.Keys)) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name], 'Process')
    }
    try { & $dotnetCommand @arguments | Out-Host; $buildExit = $LASTEXITCODE }
    finally { foreach ($name in @($savedEnvironment.Keys)) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') } }
    if ($buildExit -ne 0) { throw "The MSI build failed with exit code $buildExit." }
    # MY-01: the build restored exactly the checked WiX SDK and nothing else.
    $restored = @(Get-ChildItem -LiteralPath $packages -Force)
    $restoredVersions = @(Get-ChildItem -LiteralPath (Join-Path $packages $wixId) -Force -ErrorAction SilentlyContinue)
    $restoredPackage = Join-Path $packages "$wixId\$wixVersion\$wixId.$wixVersion.nupkg"
    if ($restored.Count -ne 1 -or $restored[0].Name -cne $wixId -or $restoredVersions.Count -ne 1 -or $restoredVersions[0].Name -cne $wixVersion -or
        !(Test-Path -LiteralPath $restoredPackage -PathType Leaf) -or (Get-FileSha512Base64 $restoredPackage) -cne [string]$wix.sha512) {
        throw "The MSI build restored something other than the pinned WiX SDK: $(($restored | ForEach-Object { $_.Name }) -join ', ')"
    }
    $built = @(Get-ChildItem -LiteralPath $buildOutput -Recurse -Filter '*.msi' -File)
    if ($built.Count -ne 1) { throw "Expected one MSI in the build output, found $($built.Count)." }
    # The output copy is taken before the package is opened. The checks below read the build copy, which must hash the same.
    New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
    Copy-Item -LiteralPath $built[0].FullName -Destination $output
    function Get-FileSha256([string]$Path) {
        $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
    }
    $hash = Get-FileSha256 $output
    if ((Get-FileSha256 $built[0].FullName) -cne $hash) { throw 'The MSI copy differs from the build output.' }

    # Package checks read the built database through msi.dll directly (read-only, every handle closed in order), not through
    # the WindowsInstaller.Installer automation objects, whose lifetime PowerShell does not control.
    if (-not ('PCStewardMsiReader' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PCStewardMsiReader
{
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string databasePath, IntPtr persist, out IntPtr database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiRecordGetFieldCount(IntPtr record);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint size);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);

    private const uint Success = 0, NoMoreItems = 259, MoreData = 234;

    public static string[][] Query(string databasePath, string query, int columns)
    {
        IntPtr database;
        uint status = MsiOpenDatabaseW(databasePath, IntPtr.Zero, out database);
        if (status != Success) { throw new InvalidOperationException("MsiOpenDatabase failed with " + status + "."); }
        try
        {
            IntPtr view;
            status = MsiDatabaseOpenViewW(database, query, out view);
            if (status != Success) { throw new InvalidOperationException("MsiDatabaseOpenView failed with " + status + " for: " + query); }
            try
            {
                status = MsiViewExecute(view, IntPtr.Zero);
                if (status != Success) { throw new InvalidOperationException("MsiViewExecute failed with " + status + " for: " + query); }
                List<string[]> rows = new List<string[]>();
                while (true)
                {
                    IntPtr record;
                    status = MsiViewFetch(view, out record);
                    if (status == NoMoreItems) { break; }
                    if (status != Success) { throw new InvalidOperationException("MsiViewFetch failed with " + status + " for: " + query); }
                    try
                    {
                        if (MsiRecordGetFieldCount(record) < (uint)columns) { throw new InvalidOperationException("The record has fewer than " + columns + " fields for: " + query); }
                        string[] values = new string[columns];
                        for (int column = 1; column <= columns; column++) { values[column - 1] = ReadString(record, (uint)column); }
                        rows.Add(values);
                    }
                    finally { MsiCloseHandle(record); }
                }
                return rows.ToArray();
            }
            finally { MsiCloseHandle(view); }
        }
        finally { MsiCloseHandle(database); }
    }

    public static bool HasTable(string databasePath, string table)
    {
        return Query(databasePath, "SELECT `Name` FROM `_Tables` WHERE `Name` = '" + table.Replace("'", "") + "'", 1).Length > 0;
    }

    private static string ReadString(IntPtr record, uint field)
    {
        uint size = 256;
        StringBuilder value = new StringBuilder((int)size);
        uint status = MsiRecordGetStringW(record, field, value, ref size);
        if (status == MoreData)
        {
            size++;
            value = new StringBuilder((int)size);
            status = MsiRecordGetStringW(record, field, value, ref size);
        }
        if (status != Success) { throw new InvalidOperationException("MsiRecordGetString failed with " + status + "."); }
        return value.ToString();
    }
}
'@
    }
    $msi = $built[0].FullName
    # Rows are written to the pipeline one string[] each; every caller collects them with @() so one row stays one row.
    function Get-MsiRows([string]$Query, [int]$Columns) { foreach ($row in [PCStewardMsiReader]::Query($msi, $Query, $Columns)) { , $row } }
    function Test-MsiTable([string]$Table) { [PCStewardMsiReader]::HasTable($msi, $Table) }
    try {
        foreach ($table in @('ServiceInstall', 'ServiceControl', 'MsiServiceConfig', 'MsiServiceConfigFailureActions', 'Binary')) {
            if ((Test-MsiTable $table) -and @(Get-MsiRows "SELECT * FROM ``$table``" 1).Count -gt 0) { throw "The MSI must not contain $table rows (ADR-034 §1)." }
        }
        # No custom action may run code. The only one allowed is type 19 (show an error and stop, no source), which WiX may
        # author for MajorUpgrade's downgrade refusal.
        if (Test-MsiTable 'CustomAction') {
            foreach ($action in @(Get-MsiRows 'SELECT `Action`, `Type`, `Source` FROM `CustomAction`' 3)) {
                if ($action[1] -cne '19' -or $action[2] -ne '') { throw "The MSI must not contain custom actions that run code (ADR-034 §1); found $($action[0]) of type $($action[1])." }
            }
        }
        $properties = @{}
        $propertyRows = @(Get-MsiRows 'SELECT `Property`, `Value` FROM `Property`' 2)
        foreach ($row in $propertyRows) { $properties[$row[0]] = $row[1] }
        if ($propertyRows.Count -eq 0 -or $properties.ContainsKey('')) {
            throw "The MSI Property table did not read back ($($propertyRows.Count) rows: $(($propertyRows | ForEach-Object { $_ -join '=' }) -join '; '))."
        }
        if ($properties['UpgradeCode'] -cne $script:UpgradeCode) { throw "The MSI UpgradeCode is $($properties['UpgradeCode']), not $script:UpgradeCode (properties read: $(($properties.Keys | Sort-Object) -join ', '))." }
        if ($properties['ProductVersion'] -cne $MsiVersion) { throw "The MSI ProductVersion is $($properties['ProductVersion']), not $MsiVersion." }
        if ($properties['ALLUSERS'] -cne '1') { throw 'The MSI must install per machine (ALLUSERS=1).' }
        $locks = @(Get-MsiRows 'SELECT `LockObject`, `Table`, `SDDLText` FROM `MsiLockPermissionsEx`' 3)
        if ($locks.Count -ne 1 -or $locks[0][1] -cne 'CreateFolder' -or $locks[0][0] -cne 'INSTALLFOLDER' -or $locks[0][2] -cne $script:FolderSddl) {
            throw "The MSI must set exactly the protected install-folder DACL; found: $(($locks | ForEach-Object { $_ -join '|' }) -join '; ')"
        }
        if ((Test-MsiTable 'LockPermissions') -and @(Get-MsiRows 'SELECT * FROM `LockPermissions`' 1).Count -gt 0) { throw 'The MSI must not use the LockPermissions table.' }
    }
    catch {
        Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
        throw
    }
    [pscustomobject]@{ msiPath = $output; msiSha256 = $hash; msiVersion = $MsiVersion; upgradeCode = $script:UpgradeCode } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force -ErrorAction SilentlyContinue }
}
