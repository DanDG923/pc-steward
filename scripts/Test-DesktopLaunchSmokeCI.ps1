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
# Owner reports 2026-11-13 (releases 0.1.14 and 0.1.15): what is drawn, measured from the screen's pixels. The background is
# the commonest colour in the area; ink is every pixel that differs from it. Returns top, bottom, left, right, the row most
# columns end on (a line of text's baseline), the ink pixel count and the background colour; -1 when nothing differs.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
public static class SmokeInk {
    public static int[] Measure(Bitmap bmp, int x0, int y0, int x1, int y1, bool disc, int threshold) {
        if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0; if (x1 > bmp.Width) x1 = bmp.Width; if (y1 > bmp.Height) y1 = bmp.Height;
        double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0, radius = Math.Min(x1 - x0, y1 - y0) / 2.0;
        Dictionary<int, int> counts = new Dictionary<int, int>();
        for (int y = y0; y < y1; y++) {
            for (int x = x0; x < x1; x++) {
                if (disc && !Inside(x, y, cx, cy, radius)) continue;
                int c = bmp.GetPixel(x, y).ToArgb(); int n; counts.TryGetValue(c, out n); counts[c] = n + 1;
            }
        }
        int bg = 0, best = -1;
        foreach (KeyValuePair<int, int> pair in counts) { if (pair.Value > best) { best = pair.Value; bg = pair.Key; } }
        Color b = Color.FromArgb(bg);
        int top = -1, bottom = -1, left = -1, right = -1, ink = 0;
        int[] ends = new int[bmp.Height + 1];
        for (int x = x0; x < x1; x++) {
            int last = -1;
            for (int y = y0; y < y1; y++) {
                if (disc && !Inside(x, y, cx, cy, radius)) continue;
                Color p = bmp.GetPixel(x, y);
                int d = Math.Max(Math.Abs(p.R - b.R), Math.Max(Math.Abs(p.G - b.G), Math.Abs(p.B - b.B)));
                if (d < threshold) continue;
                ink++; last = y;
                if (top < 0 || y < top) top = y;
                if (y > bottom) bottom = y;
                if (left < 0) left = x;
                right = x;
            }
            if (last >= 0) ends[last]++;
        }
        int baseline = -1, most = 0;
        for (int y = 0; y < ends.Length; y++) { if (ends[y] > most) { most = ends[y]; baseline = y; } }
        return new int[] { top, bottom, left, right, baseline, ink, bg };
    }
    private static bool Inside(int x, int y, double cx, double cy, double radius) {
        double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
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
# The About page also shows its layout samples (MainWindow.LayoutSamples.cs) for the pixel measurements below.
$env:PCSTEWARD_LAYOUT_SAMPLES = '1'
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
# Owner reports 2026-11-13 (releases 0.1.14 and 0.1.15): an icon button was taller than the text button beside it with its
# label higher, the update step numbers sat at the top of their circles, and the app's name sat higher than its mark in the
# title bar. Layout boxes showed none of that, so these read the drawn pixels. At 100% one pixel is one unit. "mid" is how
# far the middle of the ink is below the middle of the control; for a line of text the ink runs from the top of its tallest
# letters to its baseline (the row most columns end on), with the lowest tail in brackets.
$script:ink = @{}
function Get-Capture([double]$Left, [double]$Top, [double]$Width, [double]$Height) {
    $bmp = New-Object Drawing.Bitmap ([Math]::Max(1, [int][Math]::Round($Width))), ([Math]::Max(1, [int][Math]::Round($Height)))
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int][Math]::Round($Left), [int][Math]::Round($Top), 0, 0, $bmp.Size)
    $g.Dispose()
    return ,$bmp
}
function Get-RawTexts([Windows.Automation.AutomationElement]$Root) {
    $rawView = New-Object Windows.Automation.CacheRequest
    $rawView.TreeFilter = [Windows.Automation.Automation]::RawViewCondition
    $active = $rawView.Activate()
    try {
        return ,@($Root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)) |
            Where-Object { $_.Current.BoundingRectangle.Height -gt 0 })
    } finally { $active.Dispose() }
}
function Write-ButtonInk([Windows.Automation.AutomationElement]$Button, [string]$Id) {
    $r = $Button.Current.BoundingRectangle
    if ($Button.Current.IsOffscreen -or $r.Height -le 0) { Write-Line ('LAYOUT-INK button=' + $Id + ' skipped (not on screen)'); return }
    # A button's label is the last text inside it; an icon is drawn by a text of its own before the label.
    $texts = Get-RawTexts $Button
    if ($texts.Count -eq 0) { Write-Line ('LAYOUT-INK button=' + $Id + ' skipped (no text inside)'); return }
    $t = $texts[$texts.Count - 1].Current.BoundingRectangle
    $bmp = Get-Capture $r.Left $r.Top $r.Width $r.Height
    try {
        $from = [Math]::Max(3, [int][Math]::Round($t.Left - $r.Left) - 1)
        $labelInk = [SmokeInk]::Measure($bmp, $from, 3, $bmp.Width - 3, $bmp.Height - 3, $false, 60)
        $faint = [SmokeInk]::Measure($bmp, $from, 3, $bmp.Width - 3, $bmp.Height - 3, $false, 24)
        $result = @{ Height = [Math]::Round($r.Height, 1); Top = $labelInk[0]; Base = $labelInk[4]; IconMid = $null }
        $line = 'LAYOUT-INK button=' + $Id + ' height=' + $result.Height + ' labelBox=' + [Math]::Round($t.Top - $r.Top, 1) + ',' + [Math]::Round($t.Height, 1) +
            ' labelInk=' + $labelInk[0] + '..' + $labelInk[4] + '(' + $labelInk[1] + ') faint=' + $faint[0] + '..' + $faint[4] + ' labelMid=' + (($labelInk[0] + $labelInk[4] + 1) / 2 - $bmp.Height / 2)
        if ($texts.Count -gt 1) {
            $i = $texts[0].Current.BoundingRectangle
            $iconInk = [SmokeInk]::Measure($bmp, [Math]::Max(3, [int][Math]::Round($i.Left - $r.Left) - 1), 3, [Math]::Min($from - 2, [int][Math]::Round($i.Right - $r.Left) + 1), $bmp.Height - 3, $false, 60)
            $result.IconMid = ($iconInk[0] + $iconInk[1] + 1) / 2 - $bmp.Height / 2
            $line += ' iconBox=' + [Math]::Round($i.Top - $r.Top, 1) + ',' + [Math]::Round($i.Height, 1) + ' iconInk=' + $iconInk[0] + '..' + $iconInk[1] + ' iconMid=' + $result.IconMid
        }
        $script:ink[$Id] = $result
        Write-Line $line
    } finally { $bmp.Dispose() }
}
# A step of the update steps: a 24-unit circle holding a number or a tick, then the step's label.
function Write-StepInk([Windows.Automation.AutomationElement]$Entry) {
    $r = $Entry.Current.BoundingRectangle
    $bmp = Get-Capture $r.Left ($r.Top + ($r.Height - 24) / 2) $r.Width 24
    try {
        $dot = [SmokeInk]::Measure($bmp, 3, 3, 21, 21, $true, 60)
        $faint = [SmokeInk]::Measure($bmp, 3, 3, 21, 21, $true, 24)
        $label = [SmokeInk]::Measure($bmp, 30, 0, $bmp.Width, 24, $false, 60)
        $dotMid = ($dot[0] + $dot[1] + 1) / 2 - 12
        $labelMid = ($label[0] + $label[4] + 1) / 2 - 12
        if ([Math]::Abs($dotMid) -gt $script:ink['worstStepDot']) { $script:ink['worstStepDot'] = [Math]::Abs($dotMid) }
        if ([Math]::Abs($labelMid) -gt $script:ink['worstStepLabel']) { $script:ink['worstStepLabel'] = [Math]::Abs($labelMid) }
        Write-Line ('LAYOUT-INK step="' + $Entry.Current.Name + '" entryHeight=' + [Math]::Round($r.Height, 1) + ' markInk=' + $dot[0] + '..' + $dot[1] + ' of 24 faint=' + $faint[0] + '..' + $faint[1] + ' markMid=' + $dotMid +
            ' labelInk=' + $label[0] + '..' + $label[4] + '(' + $label[1] + ') labelMid=' + $labelMid)
    } finally { $bmp.Dispose() }
}
# A title beside a status badge: the badge's label and glyph against the pill, and the title's letters against the pill.
function Write-TitleBadgeInk([Windows.Automation.AutomationElement]$Row) {
    $labels = @($Row.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'StatusBadgeLabel')))
    if ($labels.Count -eq 0) { Write-Line 'LAYOUT-INK title-badge skipped (no badge)'; return }
    $l = $labels[0].Current.BoundingRectangle
    $pill = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($labels[0])
    $p = $pill.Current.BoundingRectangle
    $area = $Row.Current.BoundingRectangle
    $bmp = Get-Capture $p.Left $p.Top $p.Width $p.Height
    try {
        $from = [int][Math]::Round($l.Left - $p.Left) - 1
        $labelInk = [SmokeInk]::Measure($bmp, $from, 2, $bmp.Width - 6, $bmp.Height - 2, $false, 60)
        $glyphInk = [SmokeInk]::Measure($bmp, 6, 2, $from - 2, $bmp.Height - 2, $false, 60)
    } finally { $bmp.Dispose() }
    $labelMid = ($labelInk[0] + $labelInk[4] + 1) / 2 - $p.Height / 2
    $glyphMid = ($glyphInk[0] + $glyphInk[1] + 1) / 2 - $p.Height / 2
    # The title is everything on the row to the left of the pill.
    $strip = Get-Capture $area.Left $area.Top ($p.Left - $area.Left - 4) $area.Height
    try { $nameInk = [SmokeInk]::Measure($strip, 0, 0, $strip.Width, $strip.Height, $false, 60) } finally { $strip.Dispose() }
    $nameMid = ($area.Top + ($nameInk[0] + $nameInk[4] + 1) / 2) - ($p.Top + $p.Height / 2)
    $script:ink['badgeLabelMid'] = $labelMid; $script:ink['titleMid'] = $nameMid
    Write-Line ('LAYOUT-INK title-badge pill=' + [Math]::Round($p.Top - $area.Top, 1) + ',' + [Math]::Round($p.Height, 1) + ' row=' + [Math]::Round($area.Height, 1) + ' labelInk=' + $labelInk[0] + '..' + $labelInk[4] + '(' + $labelInk[1] + ') labelMid=' + $labelMid +
        ' glyphInk=' + $glyphInk[0] + '..' + $glyphInk[1] + ' glyphMid=' + $glyphMid + ' titleInk=' + $nameInk[0] + '..' + $nameInk[4] + '(' + $nameInk[1] + ') titleMidFromPillMiddle=' + $nameMid)
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
        try {
            if (!(Open-Page $window 'About')) { throw 'could not open About' }
            Start-Sleep -Seconds $PageSettleSeconds
            Save-Screenshot $handle '10-About-layout-samples'
            Write-Line ('LAYOUT-INK fonts segoeUiVariable=' + (Test-Path -LiteralPath (Join-Path $env:WINDIR 'Fonts\SegUIVar.ttf')) + ' os="' + [Environment]::OSVersion.VersionString + '"')
            $samples = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'LayoutSamples'))
            if ($null -eq $samples) { Write-Line 'LAYOUT-INK skipped (the layout samples are not on the About page)' }
            else {
                $s = $samples.Current.BoundingRectangle
                $whole = Get-Capture $s.Left $s.Top $s.Width $s.Height
                try { $any = [SmokeInk]::Measure($whole, 0, 0, $whole.Width, $whole.Height, $false, 60) } finally { $whole.Dispose() }
                Write-Line ('LAYOUT-INK samples=' + [int]$s.Left + ',' + [int]$s.Top + ',' + [int]$s.Width + 'x' + [int]$s.Height + ' inkPixels=' + $any[5] + ' background=' + ('{0:X8}' -f $any[6]))
                $script:ink['worstStepDot'] = 0.0; $script:ink['worstStepLabel'] = 0.0
                foreach ($e in $samples.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
                    $c = $e.Current
                    if ($c.AutomationId -like 'SampleButton*') { Write-ButtonInk $e $c.AutomationId }
                    elseif ($c.Name -match '^Step \d of 3') { Write-StepInk $e }
                    elseif ($c.AutomationId -eq 'SampleTitleRow') { Write-TitleBadgeInk $e }
                }
                # What the owner reported, as numbers: an icon button against a text button with the same label, in both styles.
                foreach ($pair in @(@('SampleButtonText', 'SampleButtonIcon'), @('SampleButtonPrimaryText', 'SampleButtonPrimaryIcon'))) {
                    $plain = $script:ink[$pair[0]]; $icon = $script:ink[$pair[1]]
                    if ($null -eq $plain -or $null -eq $icon) { Write-Line ('LAYOUT-INK-VERDICT ' + $pair[1] + ' skipped (not measured)'); continue }
                    Write-Line ('LAYOUT-INK-VERDICT ' + $pair[1] + ' tallerThanTextButton=' + ($icon.Height - $plain.Height) + ' labelHigherThanTextButton=' + ($plain.Base - $icon.Base) + ' iconMid=' + $icon.IconMid +
                        ' level=' + (($icon.Height -eq $plain.Height) -and ($icon.Base -eq $plain.Base) -and ($icon.Top -eq $plain.Top) -and ([Math]::Abs($icon.IconMid) -le 1)))
                }
                Write-Line ('LAYOUT-INK-VERDICT steps worstMarkMid=' + $script:ink['worstStepDot'] + ' worstLabelMid=' + $script:ink['worstStepLabel'] + ' centred=' + (($script:ink['worstStepDot'] -le 1) -and ($script:ink['worstStepLabel'] -le 1)))
                if ($script:ink.ContainsKey('titleMid')) { Write-Line ('LAYOUT-INK-VERDICT title-badge labelMid=' + $script:ink['badgeLabelMid'] + ' titleMid=' + $script:ink['titleMid'] + ' level=' + (([Math]::Abs($script:ink['badgeLabelMid']) -le 1) -and ([Math]::Abs($script:ink['titleMid']) -le 1))) }
            }
            $copy = Find-Named $window 'Copy diagnostic' ([Windows.Automation.ControlType]::Button)
            if ($null -ne $copy) { Write-ButtonInk $copy 'About-Copy-diagnostic' }
            # The title bar: the app's name and mark, Back, and the window's own Close button should share one middle line.
            $brand = Find-Named $window 'PC Steward' ([Windows.Automation.ControlType]::Text)
            if ($null -eq $brand) { Write-Line 'LAYOUT-INK titlebar skipped (name not found)' }
            else {
                $b = $brand.Current.BoundingRectangle
                $top = [Math]::Round($b.Top + $b.Height / 2) - 16
                $strip = Get-Capture ($b.Left - 32) $top ($b.Width + 34) 32
                try {
                    $name = [SmokeInk]::Measure($strip, 31, 0, $strip.Width, 32, $false, 60)
                    $mark = [SmokeInk]::Measure($strip, 4, 0, 27, 32, $false, 60)
                } finally { $strip.Dispose() }
                $line = 'LAYOUT-INK titlebar nameBox=' + [Math]::Round($b.Top, 1) + ',' + [Math]::Round($b.Height, 1) + ' nameInk=' + ($top + $name[0]) + '..' + ($top + $name[1]) + ' nameMidY=' + ($top + ($name[0] + $name[1] + 1) / 2) + ' markInk=' + ($top + $mark[0]) + '..' + ($top + $mark[1]) + ' markMidY=' + ($top + ($mark[0] + $mark[1] + 1) / 2)
                foreach ($caption in 'Back', 'Close', 'Minimize', 'Maximize', 'Restore') {
                    $found = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $caption)) | Where-Object { $_.Current.BoundingRectangle.Height -gt 0 -and $_.Current.BoundingRectangle.Top -lt $b.Bottom } | Select-Object -First 1)
                    if ($found.Count -eq 0) { $line += ' ' + $caption + '=(not found)'; continue }
                    $k = $found[0].Current.BoundingRectangle
                    $shot = Get-Capture $k.Left $k.Top $k.Width $k.Height
                    try { $glyph = [SmokeInk]::Measure($shot, 3, 3, $shot.Width - 3, $shot.Height - 3, $false, 60) } finally { $shot.Dispose() }
                    $line += ' ' + $caption + '=' + [Math]::Round($k.Top, 1) + ',' + [Math]::Round($k.Height, 1) + ' ink=' + ($k.Top + $glyph[0]) + '..' + ($k.Top + $glyph[1]) + ' midY=' + ($k.Top + ($glyph[0] + $glyph[1] + 1) / 2)
                }
                $wr = $window.Current.BoundingRectangle
                $edge = $wr.Right - [Math]::Max(0, -$wr.Left)
                $corner = Get-Capture ($edge - 46) $top 46 32
                try { $cross = [SmokeInk]::Measure($corner, 3, 0, 43, 32, $false, 60) } finally { $corner.Dispose() }
                $line += ' window=' + [Math]::Round($wr.Left, 1) + ',' + [Math]::Round($wr.Top, 1) + ' cornerInk=' + ($top + $cross[0]) + '..' + ($top + $cross[1]) + ' cornerMidY=' + ($top + ($cross[0] + $cross[1] + 1) / 2)
                Write-Line $line
                Write-Line ('LAYOUT-INK-VERDICT titlebar nameBelowMark=' + (($top + ($name[0] + $name[1] + 1) / 2) - ($top + ($mark[0] + $mark[1] + 1) / 2)) + ' nameBelowCorner=' + (($top + ($name[0] + $name[1] + 1) / 2) - ($top + ($cross[0] + $cross[1] + 1) / 2)) + ' level=' + ([Math]::Abs(($name[0] + $name[1]) - ($mark[0] + $mark[1])) -le 2))
            }
        } catch { Write-Line ('LAYOUT-INK skipped: ' + $_.Exception.Message) }
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
