[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Worktree,
    [Parameter(Mandatory)][string]$PinFile,
    [Parameter(Mandatory)][string]$WorkFolder,
    [string]$DotnetRoot,
    [string]$PackagesRoot,
    [string]$SdkZipPath,
    [string[]]$AllowUserEnvironmentName = @()
)

# Task 50 CB3.7c (ADR-033 redeploy; Codex security review of PR #32, owner decision 2026-11-10: "Accept it, add a check").
# Read-only check, run as the owner after the owner-only candidate build and before the elevated redeploy. It compares the
# toolchain the build used with Microsoft's own published bytes, so a planted or edited SDK file, compiler package or build
# hook that is still in place stops the redeploy:
#   1. Worktree: every tracked file equals the build commit's blob (as is, or with CRLF folded to LF), and no Git filter
#      applies to any tracked path. Only Git for Windows' default system-scope Git LFS filter entries are tolerated.
#   2. .NET SDK: every file of the official SDK zip (downloaded here, SHA-512 pinned) is present and byte-identical in the
#      owner's dotnet folder, no extra file sits in its versioned folders, and no newer host, runtime or SDK folder would be
#      chosen instead.
#   3. NuGet packages: every package the three build projects restored is pinned; its cached .nupkg matches the pinned
#      SHA-512 and every extracted file equals that .nupkg's entry.
#   4. Build hooks: no MSBuild user extension files, no Directory.Build.*/NuGet.Config above the worktree, no untracked or
#      ignored file outside bin/obj, no extra obj import, only package-root imports in the generated NuGet props/targets,
#      and no unexpected user-scope environment variable (MSBuild reads every environment variable as a property).
# It writes only below WorkFolder (the downloaded zip). It cannot see malware that hides from it while it runs; it rules
# out tampering that is still on disk. Output names files relative to their roots and never prints the user's own paths.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:Failures = [Collections.Generic.List[string]]::new()
$script:Profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
function Write-Safe([string]$Text) {
    if ($script:Profile) { $Text = $Text.Replace($script:Profile, '<PROFILE>') }
    Write-Output $Text
}
function Add-Failure([string]$Reason) { $script:Failures.Add($Reason) }
function New-NameSet { , [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
function New-NameMap { , [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase) }
function Get-StreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Stream)).Replace('-', '') } finally { $sha.Dispose(); $Stream.Dispose() }
}
function Get-FileSha256([string]$Path) { Get-StreamSha256 ([IO.File]::OpenRead($Path)) }
function Get-FileSha512Base64([string]$Path) {
    $sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { return [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $sha.Dispose(); $stream.Dispose() }
}
function Get-FileSha512Hex([string]$Path) {
    $sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose(); $stream.Dispose() }
}
function Write-Examples([string]$Label, $Items) {
    $all = @($Items); foreach ($item in ($all | Select-Object -First 10)) { Write-Safe ('  ' + $Label + ' ' + $item) }
    if ($all.Count -gt 10) { Write-Safe ('  ' + $Label + ' ... and ' + ($all.Count - 10) + ' more') }
}
# Files below Root as relative paths, plus any reparse point (junction or symlink) found on the way.
function Get-TreeFiles([string]$Root) {
    $files = New-NameMap; $reparse = [Collections.Generic.List[string]]::new()
    if (!(Test-Path -LiteralPath $Root -PathType Container)) { return [pscustomobject]@{ Exists = $false; Files = $files; Reparse = $reparse } }
    if ((Get-Item -LiteralPath $Root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse.Add('.') }
    $prefix = $Root.TrimEnd('\', '/').Length + 1
    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        $relative = $item.FullName.Substring($prefix)
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse.Add($relative); continue }
        if (!$item.PSIsContainer) { $files[$relative] = $item.FullName }
    }
    [pscustomobject]@{ Exists = $true; Files = $files; Reparse = $reparse }
}
# Compares the expected entries (relative path -> zip entry) with the files on disk below Root.
function Compare-EntriesWithTree($Expected, [string]$Root, [string[]]$AllowedExtra) {
    $tree = Get-TreeFiles -Root $Root
    $result = [pscustomobject]@{ Same = 0; Missing = [Collections.Generic.List[string]]::new(); Changed = [Collections.Generic.List[string]]::new(); Extra = [Collections.Generic.List[string]]::new(); Reparse = $tree.Reparse }
    foreach ($relative in $Expected.Keys) {
        if (!$tree.Files.ContainsKey($relative)) { $result.Missing.Add($relative); continue }
        $entry = $Expected[$relative]; $path = $tree.Files[$relative]
        if ((Get-Item -LiteralPath $path -Force).Length -ne $entry.Length) { $result.Changed.Add($relative); continue }
        if ((Get-StreamSha256 $entry.Open()) -ceq (Get-FileSha256 $path)) { $result.Same++ } else { $result.Changed.Add($relative) }
    }
    foreach ($relative in $tree.Files.Keys) {
        if (!$Expected.ContainsKey($relative) -and $AllowedExtra -notcontains $relative) { $result.Extra.Add($relative) }
    }
    $result
}
function Test-VersionName([string]$Name) { $Name -match '\A\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\z' }
function Get-NumericVersion([string]$Name) { [version]($Name -replace '-.*\z', '') }
function Test-UnderProgramFiles([string]$Path) {
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root -and $Path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    $false
}

if (!$DotnetRoot) { $DotnetRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet' }
if (!$PackagesRoot) { $PackagesRoot = Join-Path $script:Profile '.nuget\packages' }
$DotnetRoot = $DotnetRoot.TrimEnd('\', '/'); $PackagesRoot = $PackagesRoot.TrimEnd('\', '/'); $Worktree = $Worktree.TrimEnd('\', '/')

try {
    $pins = [IO.File]::ReadAllText($PinFile) | ConvertFrom-Json
    if ($pins.schema -cne 'pc-steward-owner-build-toolchain-pins:1') { throw 'Unknown pin file schema.' }
    Write-Safe ('TOOLCHAIN-PINS sha256=' + (Get-FileSha256 $PinFile) + ' sdk=' + $pins.sdkVersion + ' packages=' + @($pins.packages).Count)
    $git = (Get-Command -Name 'git.exe' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

    # 1. Worktree integrity (CB3.7b, amended: Git for Windows' default system Git LFS filter entries are tolerated only when
    #    no attribute source applies a filter to any tracked path).
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $blobSha = { param([byte[]]$b) $h = [Text.Encoding]::ASCII.GetBytes('blob ' + $b.Length + [char]0); $s = [Security.Cryptography.SHA1]::Create(); [void]$s.TransformBlock($h, 0, $h.Length, $null, 0); [void]$s.TransformFinalBlock($b, 0, $b.Length); [BitConverter]::ToString($s.Hash).Replace('-', '').ToLowerInvariant() }
    $head = [string]($null | & $git -C $Worktree rev-parse --verify HEAD)
    $tree = @($null | & $git -C $Worktree -c core.quotePath=false ls-tree -r --full-tree HEAD)
    $checked = 0; $mismatched = [Collections.Generic.List[string]]::new(); $crlfOnly = 0; $paths = [Collections.Generic.List[string]]::new()
    foreach ($line in $tree) {
        $m = [regex]::Match([string]$line, '\A(\d{6}) blob ([0-9a-f]{40})\t(.+)\z')
        if (!$m.Success -or $m.Groups[1].Value -eq '120000') { $mismatched.Add([string]$line); continue }
        $checked++; $paths.Add($m.Groups[3].Value)
        $path = Join-Path $Worktree $m.Groups[3].Value.Replace('/', '\')
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $mismatched.Add($m.Groups[3].Value); continue }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ((& $blobSha $bytes) -ceq $m.Groups[2].Value) { continue }
        if ((& $blobSha $latin1.GetBytes($latin1.GetString($bytes).Replace("`r`n", "`n"))) -ceq $m.Groups[2].Value) { $crlfOnly++ } else { $mismatched.Add($m.Groups[3].Value) }
    }
    $filterLines = @($null | & $git -C $Worktree config --show-scope --get-regexp '^filter\.' 2>$null)
    $lfsNames = @('filter.lfs.clean', 'filter.lfs.smudge', 'filter.lfs.process', 'filter.lfs.required')
    $otherFilters = @($filterLines | ForEach-Object { $p = ([string]$_).Split(@(' ', "`t"), 3, [StringSplitOptions]::RemoveEmptyEntries); if ($p.Count -lt 2 -or $p[0] -cne 'system' -or $lfsNames -notcontains $p[1].ToLowerInvariant()) { if ($p.Count -ge 2) { $p[0] + ' ' + $p[1] } else { [string]$_ } } })
    $attributeLines = @($paths | & $git -C $Worktree -c core.quotePath=false check-attr --stdin filter)
    $filtered = @($attributeLines | Where-Object { !([string]$_).EndsWith(': filter: unspecified', [StringComparison]::Ordinal) })
    Write-Safe ('WORKTREE head=' + $head + ' entries=' + $tree.Count + ' checked=' + $checked + ' crlfOnly=' + $crlfOnly + ' mismatched=' + $mismatched.Count + ' filterEntries=' + $filterLines.Count + ' nonDefaultFilterEntries=' + $otherFilters.Count + ' attributeChecks=' + $attributeLines.Count + ' pathsWithFilter=' + $filtered.Count)
    Write-Examples 'WORKTREE-MISMATCH' $mismatched; Write-Examples 'WORKTREE-FILTER' $otherFilters; Write-Examples 'WORKTREE-FILTERED-PATH' $filtered
    if ($head -cne [string]$pins.sourceCommit) { Add-Failure 'worktree HEAD is not the pinned build commit' }
    if ($mismatched.Count -gt 0 -or $checked -ne $tree.Count -or $checked -lt 1000) { Add-Failure 'worktree files differ from the build commit' }
    if ($otherFilters.Count -gt 0 -or $filtered.Count -gt 0 -or $attributeLines.Count -ne $checked) { Add-Failure 'a Git filter is configured or applies' }

    # 2. .NET SDK against the official zip.
    $zipPin = $pins.sdkZip
    if (!$SdkZipPath) {
        New-Item -ItemType Directory -Path $WorkFolder -Force | Out-Null
        $SdkZipPath = Join-Path $WorkFolder ([string]$zipPin.name)
        if (!(Test-Path -LiteralPath $SdkZipPath -PathType Leaf)) {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $partial = $SdkZipPath + '.partial'
            (New-Object Net.WebClient).DownloadFile([string]$zipPin.url, $partial)
            Move-Item -LiteralPath $partial -Destination $SdkZipPath
        }
    }
    $zipHash = Get-FileSha512Hex $SdkZipPath
    $zipOk = $zipHash -ceq ([string]$zipPin.sha512).ToLowerInvariant()
    Write-Safe ('SDK-ZIP name=' + $zipPin.name + ' bytes=' + (Get-Item -LiteralPath $SdkZipPath).Length + ' sha512Matches=' + $zipOk)
    if (!$zipOk) { Add-Failure 'the downloaded SDK zip does not match the pinned SHA-512' }
    else {
        $zip = [IO.Compression.ZipFile]::OpenRead($SdkZipPath)
        try {
            $groups = New-NameMap; $loose = New-NameMap; $entryCount = 0
            foreach ($entry in $zip.Entries) {
                if (!$entry.Name) { continue }
                $entryCount++; $relative = $entry.FullName.Replace('/', '\'); $parts = $relative.Split('\')
                $at = -1; for ($i = 0; $i -lt $parts.Count - 1; $i++) { if (Test-VersionName $parts[$i]) { $at = $i; break } }
                if ($at -lt 0) { $loose[$relative] = $entry; continue }
                $root = ($parts[0..$at] -join '\')
                if (!$groups.ContainsKey($root)) { $groups[$root] = New-NameMap }
                $groups[$root][($parts[($at + 1)..($parts.Count - 1)] -join '\')] = $entry
            }
            $same = 0; $missing = [Collections.Generic.List[string]]::new(); $changed = [Collections.Generic.List[string]]::new(); $extra = [Collections.Generic.List[string]]::new(); $reparse = [Collections.Generic.List[string]]::new(); $newer = [Collections.Generic.List[string]]::new()
            foreach ($relative in $loose.Keys) {
                $path = Join-Path $DotnetRoot $relative
                if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $missing.Add($relative) }
                elseif ((Get-StreamSha256 $loose[$relative].Open()) -ceq (Get-FileSha256 $path)) { $same++ } else { $changed.Add($relative) }
            }
            foreach ($root in $groups.Keys) {
                $comparison = Compare-EntriesWithTree -Expected $groups[$root] -Root (Join-Path $DotnetRoot $root) -AllowedExtra @()
                $same += $comparison.Same
                foreach ($x in $comparison.Missing) { $missing.Add($root + '\' + $x) }; foreach ($x in $comparison.Changed) { $changed.Add($root + '\' + $x) }
                foreach ($x in $comparison.Extra) { $extra.Add($root + '\' + $x) }; foreach ($x in $comparison.Reparse) { $reparse.Add($root + '\' + $x) }
                $parent = Split-Path -Parent $root; $leaf = Split-Path -Leaf $root
                $ancestor = $DotnetRoot; $walked = ''
                foreach ($segment in $parent.Split('\')) { $ancestor = Join-Path $ancestor $segment; $walked = ($walked + '\' + $segment).TrimStart('\'); if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { $reparse.Add($walked) } }
                foreach ($sibling in @(Get-ChildItem -LiteralPath (Join-Path $DotnetRoot $parent) -Directory -Force -ErrorAction SilentlyContinue)) {
                    if ($sibling.Name -ne $leaf -and (Test-VersionName $sibling.Name) -and (Get-NumericVersion $sibling.Name) -gt (Get-NumericVersion $leaf)) { $newer.Add($parent + '\' + $sibling.Name) }
                }
            }
            if ((Get-Item -LiteralPath $DotnetRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse.Add('.') }
            $newer = @($newer | Sort-Object -Unique); $reparse = @($reparse | Sort-Object -Unique)
            Write-Safe ('SDK-FILES zipEntries=' + $entryCount + ' versionedFolders=' + $groups.Count + ' same=' + $same + ' missing=' + $missing.Count + ' changed=' + $changed.Count + ' extra=' + $extra.Count + ' newerFolders=' + $newer.Count + ' reparsePoints=' + $reparse.Count)
            Write-Examples 'SDK-MISSING' $missing; Write-Examples 'SDK-CHANGED' $changed; Write-Examples 'SDK-EXTRA' $extra; Write-Examples 'SDK-NEWER' $newer; Write-Examples 'SDK-REPARSE' $reparse
            if ($missing.Count + $changed.Count + $extra.Count + $newer.Count + $reparse.Count -gt 0 -or $same -lt 1000) { Add-Failure 'the .NET SDK differs from the official zip' }
        } finally { $zip.Dispose() }
    }

    # 3. NuGet packages restored for the three build projects.
    $pinMap = New-NameMap
    foreach ($p in @($pins.packages)) { $pinMap[([string]$p.id).ToLowerInvariant() + '/' + ([string]$p.version).ToLowerInvariant()] = $p }
    $used = New-NameMap; $folderProblems = [Collections.Generic.List[string]]::new(); $fallbacks = New-NameSet
    foreach ($project in @($pins.projects)) {
        $assetsPath = Join-Path (Join-Path $Worktree (Split-Path -Parent ([string]$project).Replace('/', '\'))) 'obj\project.assets.json'
        if (!(Test-Path -LiteralPath $assetsPath -PathType Leaf)) { $folderProblems.Add('no restore output for ' + $project); continue }
        $assets = [IO.File]::ReadAllText($assetsPath) | ConvertFrom-Json
        $folders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name.TrimEnd('\', '/') })
        if ($folders.Count -lt 1 -or !$folders[0].Equals($PackagesRoot, [StringComparison]::OrdinalIgnoreCase)) { $folderProblems.Add($project + ' restores into another package folder') }
        foreach ($f in ($folders | Select-Object -Skip 1)) { if (Test-UnderProgramFiles $f) { [void]$fallbacks.Add($f) } else { $folderProblems.Add($project + ' has a user-writable fallback package folder') } }
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -cne 'package') { continue }
            $slash = $library.Name.IndexOf('/'); $used[$library.Name.ToLowerInvariant()] = [pscustomobject]@{ Id = $library.Name.Substring(0, $slash); Version = $library.Name.Substring($slash + 1) }
        }
        foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
            foreach ($download in @($framework.Value.downloadDependencies)) {
                if (!$download) { continue }
                $version = ([string]$download.version).Trim('[', ']', ' ').Split(',')[0].Trim()
                $used[([string]$download.name).ToLowerInvariant() + '/' + $version.ToLowerInvariant()] = [pscustomobject]@{ Id = [string]$download.name; Version = $version }
            }
        }
    }
    $unpinned = [Collections.Generic.List[string]]::new(); $nupkgChanged = [Collections.Generic.List[string]]::new(); $pkgMissing = [Collections.Generic.List[string]]::new(); $pkgChanged = [Collections.Generic.List[string]]::new(); $pkgExtra = [Collections.Generic.List[string]]::new(); $pkgReparse = [Collections.Generic.List[string]]::new(); $protected = 0; $verified = 0; $sameFiles = 0
    # NuGet extracts every entry except the OPC parts (it keeps .signature.p7s), and writes the nuspec under the lower-case id.
    $opcSkip = '\A(\[Content_Types\]\.xml|_rels/.*|package/.*)\z'
    foreach ($key in @($used.Keys | Sort-Object)) {
        $u = $used[$key]
        if (!$pinMap.ContainsKey($key)) { $unpinned.Add($key); continue }
        $id = $u.Id.ToLowerInvariant(); $version = $u.Version.ToLowerInvariant()
        $folder = Join-Path (Join-Path $PackagesRoot $id) $version
        if (!(Test-Path -LiteralPath $folder -PathType Container)) {
            $inFallback = @($fallbacks | Where-Object { Test-Path -LiteralPath (Join-Path (Join-Path $_ $id) $version) -PathType Container })
            if ($inFallback.Count -gt 0) { $protected++ } else { $pkgMissing.Add($key) }
            continue
        }
        $nupkgName = $id + '.' + $version + '.nupkg'; $nupkg = Join-Path $folder $nupkgName
        $pinSha = [string]$pinMap[$key].sha512
        if (!(Test-Path -LiteralPath $nupkg -PathType Leaf) -or (Get-FileSha512Base64 $nupkg) -cne $pinSha) { $nupkgChanged.Add($key); continue }
        $shaFile = Join-Path $folder ($nupkgName + '.sha512')
        if (!(Test-Path -LiteralPath $shaFile -PathType Leaf) -or ([IO.File]::ReadAllText($shaFile).Trim()) -cne $pinSha) { $pkgChanged.Add($key + ' ' + $nupkgName + '.sha512') }
        $package = [IO.Compression.ZipFile]::OpenRead($nupkg)
        try {
            $expected = New-NameMap
            foreach ($entry in $package.Entries) {
                if (!$entry.Name -or $entry.FullName -match $opcSkip) { continue }
                if ($entry.FullName.IndexOf('/') -lt 0 -and $entry.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)) { $expected[$id + '.nuspec'] = $entry; continue }
                $expected[[Uri]::UnescapeDataString($entry.FullName).Replace('/', '\')] = $entry
            }
            $comparison = Compare-EntriesWithTree -Expected $expected -Root $folder -AllowedExtra @($nupkgName, ($nupkgName + '.sha512'), '.nupkg.metadata')
        } finally { $package.Dispose() }
        $verified++; $sameFiles += $comparison.Same
        foreach ($x in $comparison.Missing) { $pkgMissing.Add($key + ' ' + $x) }; foreach ($x in $comparison.Changed) { $pkgChanged.Add($key + ' ' + $x) }
        foreach ($x in $comparison.Extra) { $pkgExtra.Add($key + ' ' + $x) }; foreach ($x in $comparison.Reparse) { $pkgReparse.Add($key + ' ' + $x) }
    }
    Write-Safe ('PACKAGES used=' + $used.Count + ' verified=' + $verified + ' adminProtectedFallback=' + $protected + ' filesSame=' + $sameFiles + ' unpinned=' + $unpinned.Count + ' nupkgChanged=' + $nupkgChanged.Count + ' missing=' + $pkgMissing.Count + ' changed=' + $pkgChanged.Count + ' extra=' + $pkgExtra.Count + ' reparsePoints=' + $pkgReparse.Count + ' folderProblems=' + $folderProblems.Count)
    Write-Examples 'PACKAGE-UNPINNED' $unpinned; Write-Examples 'PACKAGE-NUPKG-CHANGED' $nupkgChanged; Write-Examples 'PACKAGE-MISSING' $pkgMissing; Write-Examples 'PACKAGE-CHANGED' $pkgChanged
    Write-Examples 'PACKAGE-EXTRA' $pkgExtra; Write-Examples 'PACKAGE-REPARSE' $pkgReparse; Write-Examples 'PACKAGE-FOLDER' $folderProblems
    if ($used.Count -eq 0 -or $unpinned.Count + $nupkgChanged.Count + $pkgMissing.Count + $pkgChanged.Count + $pkgExtra.Count + $pkgReparse.Count + $folderProblems.Count -gt 0) { Add-Failure 'a restored package differs from its pinned bytes' }

    # 4. Build hooks outside the SDK and the packages.
    $userExtensions = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\MSBuild'
    $currentHooks = @(); $otherMsbuildFiles = 0
    if (Test-Path -LiteralPath $userExtensions -PathType Container) {
        $currentHooks = @(Get-ChildItem -LiteralPath (Join-Path $userExtensions 'Current') -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($userExtensions.Length + 1) })
        $otherMsbuildFiles = @(Get-ChildItem -LiteralPath $userExtensions -Recurse -Force -File | Where-Object { !$_.FullName.StartsWith((Join-Path $userExtensions 'Current') + '\', [StringComparison]::OrdinalIgnoreCase) }).Count
    }
    $hookNames = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Build.rsp', 'Directory.Packages.props', 'Directory.Solution.props', 'Directory.Solution.targets', 'NuGet.Config', 'global.json')
    $ancestorHooks = [Collections.Generic.List[string]]::new()
    for ($dir = Split-Path -Parent $Worktree; $dir; $dir = Split-Path -Parent $dir) {
        foreach ($name in $hookNames) { if (Test-Path -LiteralPath (Join-Path $dir $name) -PathType Leaf) { $ancestorHooks.Add((Join-Path $dir $name)) } }
    }
    $untracked = @($null | & $git -C $Worktree -c core.quotePath=false ls-files --others --exclude-standard) + @($null | & $git -C $Worktree -c core.quotePath=false ls-files --others --ignored --exclude-standard)
    $outsideBuild = @($untracked | Where-Object { $_ -and ('/' + $_) -notmatch '/(bin|obj)/' } | Sort-Object -Unique)
    $objProblems = [Collections.Generic.List[string]]::new()
    foreach ($project in Get-ChildItem -LiteralPath (Join-Path $Worktree 'src') -Recurse -Force -Filter '*.csproj' -File) {
        $obj = Join-Path $project.DirectoryName 'obj'
        if (!(Test-Path -LiteralPath $obj -PathType Container)) { continue }
        $relativeObj = $obj.Substring($Worktree.Length + 1)
        foreach ($file in Get-ChildItem -LiteralPath $obj -Force -File) {
            if ($file.Name -notmatch '\.(props|targets)\z') { continue }
            if ($file.Name -ne ($project.Name + '.nuget.g.props') -and $file.Name -ne ($project.Name + '.nuget.g.targets')) { $objProblems.Add($relativeObj + '\' + $file.Name); continue }
            foreach ($import in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '<Import\s+Project="([^"]*)"')) {
                if (!$import.Groups[1].Value.StartsWith('$(NuGetPackageRoot)', [StringComparison]::Ordinal)) { $objProblems.Add($relativeObj + '\' + $file.Name + ' imports outside the package root') }
            }
        }
    }
    $userEnvironment = [Environment]::GetEnvironmentVariables('User')
    $expectedNames = @('Path', 'TEMP', 'TMP', 'OneDrive', 'OneDriveConsumer', 'OneDriveCommercial', 'ChocolateyLastPathUpdate', 'ChocolateyToolsLocation') + $AllowUserEnvironmentName
    $unexpectedNames = @($userEnvironment.Keys | Where-Object { $expectedNames -notcontains $_ } | Sort-Object)
    $processRisky = @(Get-ChildItem Env: | Where-Object { $_.Name -match '\A(MSBuild|NUGET_|DOTNET_|COMPlus_|CORECLR_|Ilc|_?CL_?\z|_?LINK_?\z)' } | ForEach-Object { $_.Name } | Sort-Object)
    Write-Safe ('BUILD-HOOKS msbuildUserCurrent=' + $currentHooks.Count + ' msbuildUserOther=' + $otherMsbuildFiles + ' aboveWorktree=' + $ancestorHooks.Count + ' untrackedOutsideBinObj=' + $outsideBuild.Count + ' objImports=' + $objProblems.Count + ' userEnvironment=' + $userEnvironment.Count + ' unexpectedUserEnvironment=' + $unexpectedNames.Count + ' buildEnvironmentInThisWindow=' + $processRisky.Count)
    Write-Examples 'HOOK-MSBUILD-USER' $currentHooks; Write-Examples 'HOOK-ABOVE-WORKTREE' $ancestorHooks; Write-Examples 'HOOK-UNTRACKED' $outsideBuild; Write-Examples 'HOOK-OBJ' $objProblems
    Write-Examples 'HOOK-USER-ENVIRONMENT-NAME' $unexpectedNames; Write-Examples 'HOOK-WINDOW-ENVIRONMENT-NAME' $processRisky
    if ($currentHooks.Count + $ancestorHooks.Count + $outsideBuild.Count + $objProblems.Count + $unexpectedNames.Count -gt 0) { Add-Failure 'a build hook outside the SDK and packages is present' }
} catch {
    Add-Failure ('the check could not finish: ' + $_.Exception.Message)
}

if ($script:Failures.Count -eq 0) { Write-Output 'CB3.7c-OK'; exit 0 }
foreach ($failure in $script:Failures) { Write-Safe ('STOP: ' + $failure) }
exit 1
