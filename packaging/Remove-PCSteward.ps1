[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'PCSteward.ScheduledCheck'

function Test-TaskNotFound([Exception]$Exception)
{
    $fileNotFoundHResult = [int]0x80070002
    for ($current = $Exception; $null -ne $current; $current = $current.InnerException)
    {
        if ($current.HResult -eq $fileNotFoundHResult) { return $true }
    }

    return $false
}

function Get-TaskSchedulerRootFolder
{
    $serviceType = [Type]::GetTypeFromProgID('Schedule.Service')
    if ($null -eq $serviceType) { throw 'The Windows Task Scheduler service is unavailable.' }

    $service = [Activator]::CreateInstance($serviceType)
    if ($null -eq $service) { throw 'The Windows Task Scheduler service could not be opened.' }

    $flags = [System.Reflection.BindingFlags]::InvokeMethod
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $null = $serviceType.InvokeMember('Connect', $flags, $null, $service, @(), $culture)
    $folder = $serviceType.InvokeMember('GetFolder', $flags, $null, $service, @('\'), $culture)
    if ($null -eq $folder) { throw 'The Windows Task Scheduler root folder is unavailable.' }

    return $folder
}

function Find-ExactScheduledTask([object]$Folder, [string]$Name)
{
    return $Folder.GetType().InvokeMember('GetTask', [System.Reflection.BindingFlags]::InvokeMethod, $null, $Folder, @($Name), [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-ExactScheduledTask([string]$Name)
{
    # Only the exact task lookup may mean absence. Setup, Connect, and root-folder failures must fail closed.
    $folder = Get-TaskSchedulerRootFolder
    try
    {
        return Find-ExactScheduledTask $folder $Name
    }
    catch
    {
        if (Test-TaskNotFound $_.Exception) { return $null }
        throw "Could not query the exact PC Steward scheduled task: $($_.Exception.Message)"
    }
}

function Remove-ExactScheduledTask([string]$Name)
{
    try
    {
        $folder = Get-TaskSchedulerRootFolder
        $null = $folder.GetType().InvokeMember('DeleteTask', [System.Reflection.BindingFlags]::InvokeMethod, $null, $folder, @($Name, 0), [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch
    {
        throw "The PC Steward scheduled task could not be removed: $($_.Exception.Message)"
    }
}

function Invoke-PCStewardTaskCleanup
{
    $running = Get-Process -Name 'PCSteward.Desktop','PCSteward.ScheduledHost' -ErrorAction SilentlyContinue
    if ($running)
    {
        throw 'Close PC Steward and wait for any scheduled scan to finish before removing the standalone package.'
    }

    if ($null -ne (Get-ExactScheduledTask $taskName))
    {
        Remove-ExactScheduledTask $taskName
        if ($null -ne (Get-ExactScheduledTask $taskName))
        {
            throw 'The exact PC Steward scheduled task still exists after removal.'
        }
    }

    Write-Host 'PC Steward scheduled-task cleanup is complete.'
    Write-Host 'Delete this extracted package folder to remove the binaries.'
    Write-Host 'To remove per-user data, run Factory reset inside PC Steward before closing it.'
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-PCStewardTaskCleanup }
