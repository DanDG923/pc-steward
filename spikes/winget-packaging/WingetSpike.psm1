Set-StrictMode -Version Latest

function ConvertTo-WingetVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text)

    $match = [regex]::Match($Text, '(?im)^v?(?<version>\d+\.\d+(?:\.\d+){0,2})\s*$')
    if (-not $match.Success) {
        throw "WinGet version output was not a supported numeric version."
    }

    return [version]$match.Groups['version'].Value
}

function Get-WingetHelpCapabilities {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$HelpText)

    return [pscustomobject]@{
        SupportsDisableInteractivity = $HelpText -match '(?m)^\s*--disable-interactivity\b'
        SupportsSourceSelection      = $HelpText -match '(?m)^\s*-s,--source\b'
        SupportsExactId              = $HelpText -match '(?m)^\s*-e,--exact\b'
        SupportsArchitecture         = $HelpText -match '(?m)^\s*-a,--architecture\b'
        SupportsScope                = $HelpText -match '(?m)^\s*--scope\b'
        SupportsStructuredOutput     = $HelpText -match '(?m)^\s*(?:-o,)?--output\b'
    }
}

function New-WingetUpdatePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$')][string]$PackageId,
        [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$')][string]$Source,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._+-]{0,127}$')][string]$Version,
        [ValidateSet('x64', 'x86', 'arm64')][string]$Architecture,
        [ValidateSet('user', 'machine')][string]$Scope
    )

    $arguments = [System.Collections.Generic.List[string]]@('upgrade', '--id', $PackageId, '--exact', '--source', $Source, '--version', $Version, '--disable-interactivity')
    if ($Architecture) { $arguments.Add('--architecture'); $arguments.Add($Architecture) }
    if ($Scope) { $arguments.Add('--scope'); $arguments.Add($Scope) }

    return [pscustomobject]@{
        FileName  = 'winget.exe'
        Arguments = $arguments.ToArray()
    }
}

Export-ModuleMember -Function ConvertTo-WingetVersion, Get-WingetHelpCapabilities, New-WingetUpdatePlan
