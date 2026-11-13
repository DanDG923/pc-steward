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
            # WCAG 2.5.3 label in name: a control's visible words must appear in its accessible name (speech users say them).
            if ($isInteractive -and $c.ClassName -ne 'ToggleSwitch' -and ![string]::IsNullOrWhiteSpace($c.Name)) {
                $label = $e.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text))
                if ($null -ne $label -and ![string]::IsNullOrWhiteSpace($label.Current.Name) -and $c.Name.IndexOf($label.Current.Name, [StringComparison]::OrdinalIgnoreCase) -lt 0) { $script:labelMismatch++; Write-Line ('  LABEL-NOT-IN-NAME ' + $type + ' visible="' + $label.Current.Name + '" name="' + $name + '"') }
            }
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
$script:labelMismatch = 0
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

# UI review 2026-11-13 (faults 1 and 2), informational: after a read-only scan of this runner, measure the Installed apps page
# through UI Automation. The list must reach down to the bottom of the window (it stopped at its 320-unit minimum), and the
# buttons of one action row must share a line. Results are LAYOUT lines in the log; nothing here adds a failure.
function Find-Named([Windows.Automation.AutomationElement]$Root, [string]$Prefix, [Windows.Automation.ControlType]$Type) {
    $ofType = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $Type)
    foreach ($e in $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $ofType)) {
        try { if (([string]$e.Current.Name).StartsWith($Prefix, [StringComparison]::OrdinalIgnoreCase) -and !$e.Current.IsOffscreen) { return $e } } catch { }
    }
    return $null
}
# ADR-035: how many app icons are showing under an element. The image is decoration, so it is in the raw view only; a row
# that shows its two letters has no image with a size.
function Get-AppIconCount([Windows.Automation.AutomationElement]$Root) {
    $rawView = New-Object Windows.Automation.CacheRequest
    $rawView.TreeFilter = [Windows.Automation.Automation]::RawViewCondition
    $byIcon = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'AppIcon')
    $active = $rawView.Activate()
    try { return @($Root.FindAll([Windows.Automation.TreeScope]::Descendants, $byIcon) | Where-Object { $_.Current.BoundingRectangle.Width -gt 0 }).Count } finally { $active.Dispose() }
}

# Owner report 2026-11-13 (a status badge read lower than the title beside it): a badge's label is measured by its capital
# letters (TextLineBounds Tight), so centring it puts the letters, not the text box, at the pill's middle. At 100% a one-line
# 12-size label is about 9 high when that holds and 16 when the full text box is still in use. The pill is the label's parent:
# offCentre is how far the middle of the label's box is below the middle of the pill.
function Write-BadgeLabel([Windows.Automation.AutomationElement]$Root, [string]$Where) {
    $byLabel = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'StatusBadgeLabel')
    $labels = @($Root.FindAll([Windows.Automation.TreeScope]::Descendants, $byLabel) | Where-Object { !$_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Height -gt 0 })
    if ($labels.Count -eq 0) { Write-Line ('LAYOUT-BADGE-LABEL where=' + $Where + ' skipped (no badge on screen)'); return }
    $heights = @($labels | ForEach-Object { [Math]::Round($_.Current.BoundingRectangle.Height, 1) } | Sort-Object -Unique)
    $l = $labels[0].Current.BoundingRectangle
    $pill = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($labels[0])
    $against = 'pill=(not found)'
    if ($null -ne $pill) {
        $p = $pill.Current.BoundingRectangle
        $against = 'pill=' + ($pill.Current.ControlType.ProgrammaticName -replace '^ControlType\.', '') + ',' + [Math]::Round($p.Top, 1) + ',' + [Math]::Round($p.Height, 1) + ' offCentre=' + [Math]::Round(($l.Top + $l.Height / 2) - ($p.Top + $p.Height / 2), 1)
    }
    Write-Line ('LAYOUT-BADGE-LABEL where=' + $Where + ' count=' + $labels.Count + ' first="' + $labels[0].Current.Name + '" heights=' + ($heights -join ',') + ' capHeightBox=' + ($heights[0] -le 12) + ' label=' + [Math]::Round($l.Top, 1) + ',' + [Math]::Round($l.Height, 1) + ' ' + $against)
}
function Open-Page([Windows.Automation.AutomationElement]$Root, [string]$Page) {
    $item = Find-Named $Root $Page ([Windows.Automation.ControlType]::ListItem)
    if ($null -eq $item) { return $false }
    try { ($item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { }
    try { ($item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select(); return $true } catch { }
    return $false
}
function Write-SameLine([Windows.Automation.AutomationElement]$First, [Windows.Automation.AutomationElement]$Second, [string]$Label) {
    if ($null -eq $First -or $null -eq $Second) { Write-Line ('LAYOUT-ROW ' + $Label + ' skipped (control not found)'); return }
    $a = $First.Current.BoundingRectangle; $b = $Second.Current.BoundingRectangle
    $sameLine = ($b.Top -lt $a.Bottom) -and ($a.Top -lt $b.Bottom)
    Write-Line ('LAYOUT-ROW ' + $Label + ' sameLine=' + $sameLine + ' first=' + [int]$a.Left + ',' + [int]$a.Top + ',' + [int]$a.Width + 'x' + [int]$a.Height + ' second=' + [int]$b.Left + ',' + [int]$b.Top + ',' + [int]$b.Width + 'x' + [int]$b.Height)
}
$process.Refresh()
if ($failures.Count -eq 0 -and !$process.HasExited) {
    try {
        try { Set-DisplayResolution -Width 1920 -Height 1080 -Force | Out-Null; Start-Sleep -Seconds 3 } catch { Write-Line ('LAYOUT resolution unchanged: ' + $_.Exception.Message) }
        [void][SmokeWin32]::ShowWindow($handle, 9); Start-Sleep -Milliseconds 500
        [void][SmokeWin32]::ShowWindow($handle, 3); Start-Sleep -Seconds 2
        $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
        if (!(Open-Page $window 'Installed apps')) { throw 'could not open Installed apps' }
        Start-Sleep -Seconds $PageSettleSeconds
        $scan = Find-Named $window 'Scan again' ([Windows.Automation.ControlType]::Button)
        if ($null -eq $scan) { throw 'Scan again not found' }
        ($scan.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
        $list = $null
        $scanDeadline = (Get-Date).AddSeconds(150)
        while ((Get-Date) -lt $scanDeadline -and $null -eq $list) {
            Start-Sleep -Seconds 5
            $process.Refresh(); if ($process.HasExited) { throw 'the app exited during the scan' }
            [void](Open-Page $window 'Installed apps'); Start-Sleep -Seconds 2
            $byId = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'InstalledAppsList')
            $found = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $byId)
            if ($null -ne $found -and !$found.Current.IsOffscreen -and $found.Current.BoundingRectangle.Height -gt 0) { $list = $found }
        }
        if ($null -eq $list) { throw 'the Installed apps list did not appear within 150 seconds of starting a scan' }
        Start-Sleep -Seconds $PageSettleSeconds
        $r = New-Object SmokeWin32+RECT
        [void][SmokeWin32]::GetWindowRect($handle, [ref]$r)
        $box = $list.Current.BoundingRectangle
        $gap = [int]($r.Bottom - $box.Bottom)
        $rows = $list.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)).Count
        # Below the list are the card's edge, a footnote and the page padding: well under 160 pixels at 100% scaling.
        $verdict = if ($gap -le 160) { 'fills' } else { 'short' }
        Write-Line ('LAYOUT-INSTALLED-LIST window=' + ($r.Right - $r.Left) + 'x' + ($r.Bottom - $r.Top) + ' listTop=' + [int]$box.Top + ' listHeight=' + [int]$box.Height + ' gapToWindowBottom=' + $gap + ' rowsOnScreen=' + $rows + ' verdict=' + $verdict)
        Write-SameLine (Find-Named $window 'Scan again' ([Windows.Automation.ControlType]::Button)) (Find-Named $window 'More options' ([Windows.Automation.ControlType]::Button)) 'installed-header'
        Write-SameLine (Find-Named $window 'Sort by' ([Windows.Automation.ControlType]::Text)) (Find-Named $window 'Sort applications' ([Windows.Automation.ControlType]::ComboBox)) 'installed-sort'
        # UI review 2026-11-13 (item 20; ADR-035): a row shows the app's own icon when one could be read, and its two letters
        # otherwise. An icon is read only for a row that is on screen, so the count is at most the rows the window shows, which
        # is fewer than the rows the list has built. Icons arrive a moment after the rows.
        try {
            $icons = -1
            $iconDeadline = (Get-Date).AddSeconds(18)
            do { Start-Sleep -Seconds 3; $before = $icons; $icons = Get-AppIconCount $list } while (($icons -eq 0 -or $icons -ne $before) -and (Get-Date) -lt $iconDeadline)
            $process.Refresh()
            $onScreen = @($list.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)) | Where-Object { !$_.Current.IsOffscreen })
            Write-Line ('LAYOUT-APP-ICONS shown=' + $icons + ' rowsOnScreen=' + $onScreen.Count + ' rowsBuilt=' + $rows + ' appRunning=' + !$process.HasExited)
            if ($process.HasExited) { $failures.Add('the app exited while showing app icons (code ' + $process.ExitCode + ')') }
            # For each row on screen: whether it shows an icon, against what the runner's uninstall entry of that name gives as
            # its icon. A row with no entry of its name is a Store-type app or came from another source.
            $entryIcons = @{}
            foreach ($root in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall') {
                foreach ($key in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
                    $entryName = [string]$key.GetValue('DisplayName')
                    if ($entryName -and !$entryIcons.ContainsKey($entryName)) { $entryIcons[$entryName] = [string]$key.GetValue('DisplayIcon', '', 'DoNotExpandEnvironmentNames') }
                }
            }
            foreach ($row in $onScreen) {
                $rowName = ($row.Current.Name -split ', ')[0]
                $entryIcon = if ($entryIcons.ContainsKey($rowName)) { '"' + $entryIcons[$rowName] + '"' } else { '(no uninstall entry of this name)' }
                Write-Line ('APP-ICON-ROW icon=' + ((Get-AppIconCount $row) -gt 0) + ' name="' + $rowName + '" entryIcon=' + $entryIcon)
            }
        } catch { Write-Line ('LAYOUT-APP-ICONS skipped: ' + $_.Exception.Message) }
        Save-Screenshot $handle '08-Installed-apps-after-scan'
        try { Write-BadgeLabel $list 'installed-list' } catch { Write-Line ('LAYOUT-BADGE-LABEL skipped: ' + $_.Exception.Message) }
        # UI review 2026-11-13 (item 19): on this wide window an app opened from the list shows its details beside the list.
        try {
            $first = $list.FindFirst([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem))
            if ($null -eq $first) { throw 'no row to open' }
            # ADR-035: open a row that shows an icon when there is one, so the column's tile can be checked against it.
            $withIcon = @($list.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)) | Where-Object { (Get-AppIconCount $_) -gt 0 } | Select-Object -First 1)
            $rowHasIcon = $withIcon.Count -gt 0
            if ($rowHasIcon) { $first = $withIcon[0] }
            ($first.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
            Start-Sleep -Seconds $PageSettleSeconds
            $process.Refresh(); if ($process.HasExited) { throw 'the app exited after opening a row' }
            $byPane = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'AppDetailsPane')
            $pane = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $byPane)
            if ($null -eq $pane -or $pane.Current.IsOffscreen) { Write-Line 'LAYOUT-DETAILS-PANE not shown' }
            else {
                $p = $pane.Current.BoundingRectangle; $l = $list.Current.BoundingRectangle
                Write-Line ('LAYOUT-DETAILS-PANE name="' + $pane.Current.Name + '" pane=' + [int]$p.Left + ',' + [int]$p.Top + ',' + [int]$p.Width + 'x' + [int]$p.Height + ' list=' + [int]$l.Left + ',' + [int]$l.Top + ',' + [int]$l.Width + 'x' + [int]$l.Height + ' beside=' + ($p.Left -ge $l.Right))
                Save-Screenshot $handle '08b-Installed-apps-details-beside'
                $unnamedTotal += Write-Tree $pane 'Details beside the list'
                # ADR-035: the details column's tile is built in code, not from the list's template; 1 when the opened app has an icon.
                Write-Line ('LAYOUT-APP-ICONS details=' + (Get-AppIconCount $pane) + ' openedRowHadIcon=' + $rowHasIcon)
                Write-BadgeLabel $pane 'details'
                $close = Find-Named $pane 'Close details' ([Windows.Automation.ControlType]::Button)
                if ($null -ne $close) {
                    ($close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke(); Start-Sleep -Seconds 2
                    $gone = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $byPane)
                    Write-Line ('LAYOUT-DETAILS-PANE closed=' + ($null -eq $gone -or $gone.Current.IsOffscreen))
                }
            }
        } catch { Write-Line ('LAYOUT-DETAILS-PANE skipped: ' + $_.Exception.Message) }
        if (Open-Page $window 'Home') {
            Start-Sleep -Seconds $PageSettleSeconds
            $homeButtons = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)) |
                Where-Object { $_.Current.ClassName -eq 'Button' -and !$_.Current.IsOffscreen -and $_.Current.Name -match '^(Turn on|See updates|Check for|Scan again|Scan this PC|Look for updates online|Not now)' })
            if ($homeButtons.Count -ge 2) { Write-SameLine $homeButtons[0] $homeButtons[1] 'home-hero' } else { Write-Line ('LAYOUT-ROW home-hero skipped (buttons found: ' + $homeButtons.Count + ')') }
            Save-Screenshot $handle '09-Home-after-scan'
            # UI review 2026-11-13 (item 21): after the first scan Home asks the one first-run question; its words are in this tree.
            $unnamedTotal += Write-Tree $window 'Home after scan'
        }
        # UI review 2026-11-13 (item 19): a page of text and switches is a column of about 1,000 units, not the window's width.
        if (Open-Page $window 'Settings') {
            Start-Sleep -Seconds $PageSettleSeconds
            $switch = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, 'ToggleSwitch'))
            $nav = Find-Named $window 'Settings' ([Windows.Automation.ControlType]::ListItem)
            if ($null -ne $switch -and $null -ne $nav) {
                $sw = $switch.Current.BoundingRectangle; $nv = $nav.Current.BoundingRectangle
                [void][SmokeWin32]::GetWindowRect($handle, [ref]$r)
                Write-Line ('LAYOUT-READING-COLUMN window=' + ($r.Right - $r.Left) + ' navigationRight=' + [int]$nv.Right + ' switchRight=' + [int]$sw.Right + ' columnFromNavigation=' + [int]($sw.Right - $nv.Right))
            } else { Write-Line 'LAYOUT-READING-COLUMN skipped (control not found)' }
        }
        Open-Page $window 'Activity' | Out-Null
        Start-Sleep -Seconds $PageSettleSeconds
        $unnamedTotal += Write-Tree $window 'Activity after scan'
    } catch { Write-Line ('LAYOUT-CHECK skipped: ' + $_.Exception.Message) }
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
Write-Line ('UNNAMED-INTERACTIVE-TOTAL ' + $unnamedTotal + ' LABEL-NOT-IN-NAME-TOTAL ' + $script:labelMismatch)
if ($failures.Count -eq 0) { Write-Line 'DESKTOP-LAUNCH-SMOKE-OK'; exit 0 }
foreach ($f in $failures) { Write-Line ('STOP: ' + $f) }
exit 1
