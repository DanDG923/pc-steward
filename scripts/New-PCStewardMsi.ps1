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
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "The MSI build failed with exit code $LASTEXITCODE." }
    $built = @(Get-ChildItem -LiteralPath $build -Recurse -Filter '*.msi' -File)
    if ($built.Count -ne 1) { throw "Expected one MSI in the build output, found $($built.Count)." }

    # Package checks through the Windows Installer automation interface (read-only database).
    $installer = New-Object -ComObject WindowsInstaller.Installer
    function Get-MsiRows([object]$Database, [string]$Query, [int]$Columns) {
        $view = $Database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $Database, @($Query))
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
        $rows = [Collections.Generic.List[object]]::new()
        while ($true) {
            $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
            if ($null -eq $record) { break }
            $values = foreach ($column in 1..$Columns) { [string]$record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @($column)) }
            $rows.Add([string[]]@($values))
        }
        $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
        , $rows.ToArray()
    }
    function Test-MsiTable([object]$Database, [string]$Table) {
        $rows = Get-MsiRows $Database "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = '$Table'" 1
        $rows.Count -gt 0
    }
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($built[0].FullName, 0))
    try {
        foreach ($table in @('ServiceInstall', 'ServiceControl', 'MsiServiceConfig', 'MsiServiceConfigFailureActions', 'Binary')) {
            if ((Test-MsiTable $database $table) -and (Get-MsiRows $database "SELECT * FROM ``$table``" 1).Count -gt 0) { throw "The MSI must not contain $table rows (ADR-034 §1)." }
        }
        # No custom action may run code. The only one allowed is type 19 (show an error and stop, no source), which WiX may
        # author for MajorUpgrade's downgrade refusal.
        if (Test-MsiTable $database 'CustomAction') {
            foreach ($action in Get-MsiRows $database 'SELECT `Action`, `Type`, `Source` FROM `CustomAction`' 3) {
                if ($action[1] -cne '19' -or $action[2] -ne '') { throw "The MSI must not contain custom actions that run code (ADR-034 §1); found $($action[0]) of type $($action[1])." }
            }
        }
        $properties = @{}
        foreach ($row in Get-MsiRows $database 'SELECT `Property`, `Value` FROM `Property`' 2) { $properties[$row[0]] = $row[1] }
        if ($properties['UpgradeCode'] -cne $script:UpgradeCode) { throw "The MSI UpgradeCode is $($properties['UpgradeCode']), not $script:UpgradeCode." }
        if ($properties['ProductVersion'] -cne $MsiVersion) { throw "The MSI ProductVersion is $($properties['ProductVersion']), not $MsiVersion." }
        if ($properties['ALLUSERS'] -cne '1') { throw 'The MSI must install per machine (ALLUSERS=1).' }
        $locks = @(Get-MsiRows $database 'SELECT `LockObject`, `Table`, `SDDLText` FROM `MsiLockPermissionsEx`' 3)
        if ($locks.Count -ne 1 -or $locks[0][1] -cne 'CreateFolder' -or $locks[0][0] -cne 'INSTALLFOLDER' -or $locks[0][2] -cne $script:FolderSddl) {
            throw "The MSI must set exactly the protected install-folder DACL; found: $(($locks | ForEach-Object { $_ -join '|' }) -join '; ')"
        }
        if ((Test-MsiTable $database 'LockPermissions') -and (Get-MsiRows $database 'SELECT * FROM `LockPermissions`' 1).Count -gt 0) { throw 'The MSI must not use the LockPermissions table.' }
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }

    New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
    Copy-Item -LiteralPath $built[0].FullName -Destination $output
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($output)
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
    [pscustomobject]@{ msiPath = $output; msiSha256 = $hash; msiVersion = $MsiVersion; upgradeCode = $script:UpgradeCode } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
}
