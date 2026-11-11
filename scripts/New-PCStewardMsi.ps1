[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string]$MsiVersion,
    [Parameter(Mandatory)][string]$OutputPath
)

# ADR-034 §1 and §5 (FR-UPD-04/15, TM-04/13): build the PC Steward MSI from a complete payload folder (signed executables and
# PCSteward.ReleaseManifest.json already written), with the WiX SDK pinned in packaging\msi\PCSteward.Installer.wixproj, then
# check the built package: no services and no custom action that runs code, the protected install-folder DACL, the fixed
# UpgradeCode and the version asked for. Local only: no signing, install, elevation or certificate store. `dotnet build`
# restores the pinned WiX SDK from NuGet; that is the only network effect.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'packaging\msi\PCSteward.Installer.wixproj'
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
    $arguments = @('build', $project, '--configuration', 'Release', '--output', $build, '--disable-build-servers', '-nologo',
        "-p:PayloadDir=$payload", "-p:ProductVersion=$MsiVersion")
    # The build log goes to the console, never into this script's output (the last output line is the JSON result).
    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "The MSI build failed with exit code $LASTEXITCODE." }
    $built = @(Get-ChildItem -LiteralPath $build -Recurse -Filter '*.msi' -File)
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
    function Get-MsiRows([string]$Query, [int]$Columns) { , [PCStewardMsiReader]::Query($msi, $Query, $Columns) }
    function Test-MsiTable([string]$Table) { [PCStewardMsiReader]::HasTable($msi, $Table) }
    try {
        foreach ($table in @('ServiceInstall', 'ServiceControl', 'MsiServiceConfig', 'MsiServiceConfigFailureActions', 'Binary')) {
            if ((Test-MsiTable $table) -and (Get-MsiRows "SELECT * FROM ``$table``" 1).Count -gt 0) { throw "The MSI must not contain $table rows (ADR-034 §1)." }
        }
        # No custom action may run code. The only one allowed is type 19 (show an error and stop, no source), which WiX may
        # author for MajorUpgrade's downgrade refusal.
        if (Test-MsiTable 'CustomAction') {
            foreach ($action in Get-MsiRows 'SELECT `Action`, `Type`, `Source` FROM `CustomAction`' 3) {
                if ($action[1] -cne '19' -or $action[2] -ne '') { throw "The MSI must not contain custom actions that run code (ADR-034 §1); found $($action[0]) of type $($action[1])." }
            }
        }
        $properties = @{}
        foreach ($row in Get-MsiRows 'SELECT `Property`, `Value` FROM `Property`' 2) { $properties[$row[0]] = $row[1] }
        if ($properties['UpgradeCode'] -cne $script:UpgradeCode) { throw "The MSI UpgradeCode is $($properties['UpgradeCode']), not $script:UpgradeCode." }
        if ($properties['ProductVersion'] -cne $MsiVersion) { throw "The MSI ProductVersion is $($properties['ProductVersion']), not $MsiVersion." }
        if ($properties['ALLUSERS'] -cne '1') { throw 'The MSI must install per machine (ALLUSERS=1).' }
        $locks = @(Get-MsiRows 'SELECT `LockObject`, `Table`, `SDDLText` FROM `MsiLockPermissionsEx`' 3)
        if ($locks.Count -ne 1 -or $locks[0][1] -cne 'CreateFolder' -or $locks[0][0] -cne 'INSTALLFOLDER' -or $locks[0][2] -cne $script:FolderSddl) {
            throw "The MSI must set exactly the protected install-folder DACL; found: $(($locks | ForEach-Object { $_ -join '|' }) -join '; ')"
        }
        if ((Test-MsiTable 'LockPermissions') -and (Get-MsiRows 'SELECT * FROM `LockPermissions`' 1).Count -gt 0) { throw 'The MSI must not use the LockPermissions table.' }
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
