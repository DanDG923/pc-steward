[CmdletBinding()]
param(
    [int]$StartTimeoutSeconds = 60,
    [int]$PageSettleSeconds = 4
)

# UI polish phase 5 pre-flight, informational: build the desktop app, start it on the hosted runner, open every page from
# the navigation pane through UI Automation and check that the process is still running after each one. A missing XAML
# resource or a page that throws while it is built ends the process, which this catches before the owner redeploys.
# Each page's UI Automation tree is written to the log (what Narrator reads), interactive controls with no accessible
# name are listed, and a screenshot of each page is saved. No owner-only script, certificate, install or elevation.
# Windows PowerShell 5.1 only (UIAutomationClient and System.Drawing ship with .NET Framework).

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this under Windows PowerShell 5.1 (powershell.exe).' }
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This smoke test starts the app on a disposable hosted runner only.' }

$repository = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repository 'artifacts\desktop-launch-smoke'
$app = Join-Path $out 'app'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$log = Join-Path $out 'smoke.log'
function Write-Line([string]$Text) { Write-Host $Text; Add-Content -LiteralPath $log -Value $Text -Encoding UTF8 }

# The hosted runner has no Windows App Runtime installed, so this build carries it (WindowsAppSDKSelfContained). The owner's
# deployment uses the installed runtime; the XAML, resources and page code are the same.
$scheduledHost = Join-Path $repository 'src\PCSteward.ScheduledHost\PCSteward.ScheduledHost.csproj'
$desktop = Join-Path $repository 'src\PCSteward.Desktop\PCSteward.Desktop.csproj'
$steps = @(
    @('restore', $scheduledHost, '--runtime', 'win-x64'),
    @('restore', $desktop, '--runtime', 'win-x64', '-p:WindowsAppSDKSelfContained=true'),
    @('build', $scheduledHost, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore'),
    @('build', $desktop, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore', '-p:WindowsAppSDKSelfContained=true', '--output', $app)
)
foreach ($step in $steps) {
    & dotnet @step
    if ($LASTEXITCODE -ne 0) { throw ('dotnet ' + $step[0] + ' failed with exit code ' + $LASTEXITCODE) }
}
$exe = Join-Path $app 'PCSteward.Desktop.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'PCSteward.Desktop.exe was not built.' }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWin32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
}
'@

function Save-Screenshot([IntPtr]$Handle, [string]$Name) {
    try {
        $r = New-Object SmokeWin32+RECT
        [void][SmokeWin32]::GetWindowRect($Handle, [ref]$r)
        $w = [Math]::Max(1, $r.Right - $r.Left); $h = [Math]::Max(1, $r.Bottom - $r.Top)
        $bmp = New-Object Drawing.Bitmap $w, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $out ($Name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    } catch { Write-Line ('SCREENSHOT-FAILED ' + $Name + ' ' + $_.Exception.Message) }
}

$interactive = @('Button', 'CheckBox', 'ComboBox', 'Edit', 'Hyperlink', 'ListItem', 'MenuItem', 'RadioButton', 'Slider', 'SplitButton', 'TabItem')
function Write-Tree([Windows.Automation.AutomationElement]$Root, [string]$Page) {
    $all = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $unnamed = 0; $shown = 0
    foreach ($e in $all) {
        try {
            $c = $e.Current
            $type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            if ($c.IsOffscreen) { continue }
            $name = [string]$c.Name; if ($name.Length -gt 90) { $name = $name.Substring(0, 90) + '...' }
            $isInteractive = $interactive -contains $type
            if ($isInteractive -and [string]::IsNullOrWhiteSpace($c.Name)) { $unnamed++; Write-Line ('  UNNAMED ' + $type + ' automationId=' + $c.AutomationId + ' class=' + $c.ClassName) }
            $isLabel = ($type -eq 'Text' -or $type -eq 'Header' -or $type -eq 'Group') -and ![string]::IsNullOrWhiteSpace($c.Name)
            if ($shown -lt 160 -and ($isInteractive -or $isLabel)) {
                $shown++
                $flags = @(); if ($isInteractive -and !$c.IsEnabled) { $flags += 'disabled' }; if ($isInteractive -and !$c.IsKeyboardFocusable) { $flags += 'not-focusable' }
                Write-Line ('  ' + $type + ' "' + $name + '"' + $(if ($flags.Count) { ' [' + ($flags -join ',') + ']' } else { '' }))
            }
        } catch { }
    }
    Write-Line ('PAGE-TREE page=' + $Page + ' elements=' + $all.Count + ' unnamedInteractive=' + $unnamed)
    return $unnamed
}

$started = Get-Date
$process = Start-Process -FilePath $exe -WorkingDirectory $app -PassThru
$handle = [IntPtr]::Zero
$deadline = $started.AddSeconds($StartTimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { break }
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $handle = $process.MainWindowHandle; break }
}
$failures = New-Object Collections.Generic.List[string]
$unnamedTotal = 0
if ($process.HasExited) { $failures.Add('the app exited during start-up with code ' + $process.ExitCode) }
elseif ($handle -eq [IntPtr]::Zero) { $failures.Add('no main window within ' + $StartTimeoutSeconds + ' seconds') }
else {
    [void][SmokeWin32]::ShowWindow($handle, 3)
    [void][SmokeWin32]::SetForegroundWindow($handle)
    Start-Sleep -Seconds $PageSettleSeconds
    $process.Refresh()
    Write-Line ('LAUNCH alive=' + !$process.HasExited + ' title="' + $process.MainWindowTitle + '" secondsToWindow=' + [int]((Get-Date) - $started).TotalSeconds)
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    Save-Screenshot $handle '00-start'
    $unnamedTotal += Write-Tree $window 'start'
    $pages = @('Home', 'Updates', 'Installed apps', 'Activity', 'Decisions', 'Settings', 'About')
    $index = 0
    foreach ($page in $pages) {
        $index++
        $process.Refresh()
        if ($process.HasExited) { $failures.Add('the app exited before opening ' + $page + ' (code ' + $process.ExitCode + ')'); break }
        $byName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $page)
        $isItem = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
        $item = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.AndCondition]::new($byName, $isItem))
        if ($null -eq $item) { $item = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $byName) }
        if ($null -eq $item) { $failures.Add('navigation item not found: ' + $page); continue }
        $opened = $false
        try { ($item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke(); $opened = $true } catch { }
        if (!$opened) { try { ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select(); $opened = $true } catch { } }
        if (!$opened) { $failures.Add('could not open ' + $page + ' through UI Automation'); continue }
        Start-Sleep -Seconds $PageSettleSeconds
        $process.Refresh()
        if ($process.HasExited) { $failures.Add('the app exited after opening ' + $page + ' (code ' + $process.ExitCode + ')'); break }
        Save-Screenshot $handle (('{0:D2}-' -f $index) + ($page -replace ' ', '-'))
        $unnamedTotal += Write-Tree $window $page
        Write-Line ('PAGE-OK ' + $page)
    }
}

# Anything the .NET runtime or Windows Error Reporting recorded for the app while it ran.
try {
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
        Where-Object { ($_.ProviderName -eq '.NET Runtime' -or $_.ProviderName -eq 'Application Error') -and $_.Message -match 'PCSteward' })
    foreach ($ev in $events | Select-Object -First 3) { Write-Line ('EVENT ' + $ev.ProviderName + ' ' + $ev.Id); ($ev.Message -split "`r?`n" | Select-Object -First 40) | ForEach-Object { Write-Line ('  ' + $_) } }
    Write-Line ('APP-EVENTS ' + $events.Count)
} catch { Write-Line ('APP-EVENTS unavailable ' + $_.Exception.Message) }

$process.Refresh()
if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
Write-Line ('UNNAMED-INTERACTIVE-TOTAL ' + $unnamedTotal)
if ($failures.Count -eq 0) { Write-Line 'DESKTOP-LAUNCH-SMOKE-OK'; exit 0 }
foreach ($f in $failures) { Write-Line ('STOP: ' + $f) }
exit 1
