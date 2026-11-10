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
#      applies to any tracked path. Only Git for Windows' default system-scope Git LFS filter entries are tolerated. Git is
#      run only from the signed Git for Windows install under Program Files (never from Path), and its answers are not
#      trusted: the commit object and every tree are re-hashed here and must chain to the pinned commit ID, and untracked
#      files are found by listing the folder, not by asking Git.
#   2. .NET SDK: every file of the official SDK zip (downloaded here, SHA-512 pinned) is present and byte-identical in the
#      owner's dotnet folder, no extra file sits in its versioned folders, and no newer host, runtime or SDK folder would be
#      chosen instead. The one tolerated extra is an older side-by-side workload manifest folder whose files all equal
#      Microsoft's signed manifest package (pinned) while the zip's own version of that manifest is higher; no workload
#      install state or workload set may exist, since either could pin the older version.
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
    foreach ($root in @([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles), [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86))) {
        if ($root -and $Path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    $false
}

# Git for Windows from its admin-protected install (Codex security review of PR #32): a git.exe found on Path could be a
# user-writable shim. Program Files is writable only by administrators; the launcher and the real binary must carry a valid
# Authenticode signature and nothing on the way may be a junction or symlink.
function Get-TrustedGit {
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $gitRoot = Join-Path $programFiles 'Git'; $launcher = Join-Path $gitRoot 'cmd\git.exe'
    $problems = [Collections.Generic.List[string]]::new(); $signers = [Collections.Generic.List[string]]::new(); $binaries = 0
    # The launcher, and every <folder>\bin\git.exe it may start (mingw64\bin, clangarm64\bin, bin, ...), depending on the release.
    $checks = @($gitRoot, (Join-Path $gitRoot 'cmd'), $launcher)
    if (Test-Path -LiteralPath $gitRoot -PathType Container) {
        foreach ($folder in @(Get-ChildItem -LiteralPath $gitRoot -Directory -Force)) {
            $candidate = Join-Path $folder.FullName 'bin\git.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $binaries++; $checks += @($folder.FullName, (Join-Path $folder.FullName 'bin'), $candidate) }
        }
    }
    foreach ($path in $checks) {
        $name = 'ProgramFiles\' + $path.Substring($programFiles.TrimEnd('\').Length + 1)
        if (!(Test-Path -LiteralPath $path)) { $problems.Add('missing ' + $name); continue }
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $problems.Add('reparse point ' + $name) }
        if ($path.EndsWith('\git.exe', [StringComparison]::OrdinalIgnoreCase)) {
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ([string]$signature.Status -cne 'Valid') { $problems.Add('signature ' + $signature.Status + ' on ' + $name) }
            else { $signers.Add($signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)) }
        }
    }
    if ($binaries -lt 1) { $problems.Add('no bin\git.exe below ProgramFiles\Git') }
    [pscustomobject]@{ Path = $launcher; Problems = $problems; Signers = (@($signers | Sort-Object -Unique) -join ';'); Binaries = $binaries }
}
# Runs the trusted Git and returns its standard output as raw bytes (names and commit messages are not decoded or re-encoded).
function Invoke-GitBytes([string[]]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $script:Git; $info.Arguments = (@($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' ')
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($info)
    try {
        $buffer = New-Object IO.MemoryStream; $process.StandardOutput.BaseStream.CopyTo($buffer); $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw ('git ' + ($Arguments -join ' ').Replace($Worktree, '<WORKTREE>') + ' failed') }
        , $buffer.ToArray()
    } finally { $process.Dispose() }
}
function Get-GitObjectId([string]$Type, [byte[]]$Bytes) {
    $header = [Text.Encoding]::ASCII.GetBytes($Type + ' ' + $Bytes.Length + [char]0); $sha = [Security.Cryptography.SHA1]::Create()
    try { [void]$sha.TransformBlock($header, 0, $header.Length, $null, 0); [void]$sha.TransformFinalBlock($Bytes, 0, $Bytes.Length); [BitConverter]::ToString($sha.Hash).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

# NuGet's generated obj\<project>.nuget.g.props/.targets are imported by every build. Restore writes only properties, SourceRoot
# items and package imports there, so anything else (a target, a task, a property function, another property) is a hook. Imports
# must point into a package this restore used and verified.
function Get-NuGetGeneratedFileProblems([string]$Path, [string]$AssetsPath, $UsedPackages, [string[]]$PackageFolders) {
    $problems = [Collections.Generic.List[string]]::new()
    $text = [IO.File]::ReadAllText($Path)
    if ($text.Contains('$([')) { $problems.Add('property function') }
    $xml = New-Object Xml.XmlDocument; $xml.PreserveWhitespace = $false; $xml.LoadXml($text)
    $root = $xml.DocumentElement
    if ($root.LocalName -cne 'Project') { $problems.Add('root ' + $root.LocalName); return $problems }
    foreach ($a in $root.Attributes) { if ($a.Name -cne 'ToolsVersion' -and $a.Name -cne 'xmlns') { $problems.Add('Project attribute ' + $a.Name) } }
    # NuGet writes NuGetPackageRoot as '$(UserProfile)\.nuget\packages\' when the folder is under the profile. Only that exact
    # leading macro is expanded (to this user's profile, which must also be what USERPROFILE says); any other '$(' is a problem.
    $expand = { param([string]$v) if ($v.StartsWith('$(UserProfile)', [StringComparison]::Ordinal) -and $env:USERPROFILE -and $env:USERPROFILE.TrimEnd('\').Equals($script:Profile.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { $script:Profile.TrimEnd('\') + $v.Substring(14) } else { $v } }
    $underFolder = { param([string]$v) foreach ($f in $PackageFolders) { if ($v.StartsWith($f.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and $v.IndexOf('..') -lt 0) { return $true } }; $false }
    $propertyRule = '\A(RestoreSuccess|RestoreTool|ProjectAssetsFile|NuGetPackageRoot|NuGetPackageFolders|NuGetProjectStyle|NuGetToolVersion|Pkg[A-Za-z0-9_]+)\z'
    foreach ($group in $root.ChildNodes) {
        if ($group.NodeType -eq [Xml.XmlNodeType]::Comment) { continue }
        if ($group.NodeType -ne [Xml.XmlNodeType]::Element) { $problems.Add('node ' + $group.NodeType); continue }
        if (@('PropertyGroup', 'ItemGroup', 'ImportGroup') -cnotcontains $group.LocalName) { $problems.Add('element ' + $group.LocalName); continue }
        foreach ($a in $group.Attributes) { if ($a.Name -cne 'Condition') { $problems.Add($group.LocalName + ' attribute ' + $a.Name) } }
        foreach ($child in $group.ChildNodes) {
            if ($child.NodeType -eq [Xml.XmlNodeType]::Comment) { continue }
            if ($child.NodeType -ne [Xml.XmlNodeType]::Element) { $problems.Add($group.LocalName + ' node ' + $child.NodeType); continue }
            $value = & $expand ([string]$child.InnerText)
            if (@($child.ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element }).Count -gt 0) { $problems.Add($child.LocalName + ' has child elements'); continue }
            if ($group.LocalName -ceq 'PropertyGroup') {
                foreach ($a in $child.Attributes) { if ($a.Name -cne 'Condition') { $problems.Add($child.LocalName + ' attribute ' + $a.Name) } }
                if ($child.LocalName -cnotmatch $propertyRule) { $problems.Add('property ' + $child.LocalName); continue }
                switch -CaseSensitive ($child.LocalName) {
                    'NuGetPackageRoot' { if (!$value.TrimEnd('\').Equals($PackageFolders[0].TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { $problems.Add('NuGetPackageRoot value') } }
                    'NuGetPackageFolders' { foreach ($f in $value.Split(';')) { if ($f -and !(@($PackageFolders | Where-Object { $_.TrimEnd('\').Equals($f.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) }).Count)) { $problems.Add('NuGetPackageFolders value') } } }
                    'ProjectAssetsFile' { if ($value -cne '$(MSBuildThisFileDirectory)project.assets.json' -and !$value.Equals($AssetsPath, [StringComparison]::OrdinalIgnoreCase)) { $problems.Add('ProjectAssetsFile value') } }
                    default { if ($child.LocalName.StartsWith('Pkg', [StringComparison]::Ordinal)) { if (!(& $underFolder $value)) { $problems.Add($child.LocalName + ' value') } } elseif ($value -cnotmatch '\A[A-Za-z0-9.+-]+\z') { $problems.Add($child.LocalName + ' value') } }
                }
            } elseif ($group.LocalName -ceq 'ItemGroup') {
                if ($child.LocalName -cne 'SourceRoot') { $problems.Add('item ' + $child.LocalName); continue }
                foreach ($a in $child.Attributes) { if ($a.Name -cne 'Include') { $problems.Add('SourceRoot attribute ' + $a.Name) } }
                $include = & $expand ([string]$child.GetAttribute('Include'))
                if (!(& $underFolder $include) -and !@($PackageFolders | Where-Object { $_.TrimEnd('\').Equals($include.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) }).Count) { $problems.Add('SourceRoot value') }
            } elseif ($group.LocalName -ceq 'ImportGroup') {
                if ($child.LocalName -cne 'Import') { $problems.Add('ImportGroup child ' + $child.LocalName); continue }
                foreach ($a in $child.Attributes) { if ($a.Name -cne 'Project' -and $a.Name -cne 'Condition') { $problems.Add('Import attribute ' + $a.Name) } }
                $target = [string]$child.GetAttribute('Project')
                $prefix = '$(NuGetPackageRoot)'
                if (!$target.StartsWith($prefix, [StringComparison]::Ordinal) -or $target.IndexOf('..') -ge 0) { $problems.Add('import outside the package root'); continue }
                $relativeTarget = $target.Substring($prefix.Length).TrimStart([char[]]@('\', '/')); $parts = $relativeTarget.Split([char[]]@('\', '/'))
                if ($parts.Count -lt 3 -or !$UsedPackages.ContainsKey($parts[0] + '/' + $parts[1])) { $problems.Add('import from a package this restore did not use: ' + $relativeTarget) }
            }
        }
    }
    , $problems
}

if (!$DotnetRoot) { $DotnetRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet' }
if (!$PackagesRoot) { $PackagesRoot = Join-Path $script:Profile '.nuget\packages' }
$DotnetRoot = $DotnetRoot.TrimEnd('\', '/'); $PackagesRoot = $PackagesRoot.TrimEnd('\', '/'); $Worktree = $Worktree.TrimEnd('\', '/')

try {
    $pins = [IO.File]::ReadAllText($PinFile) | ConvertFrom-Json
    if ($pins.schema -cne 'pc-steward-owner-build-toolchain-pins:1') { throw 'Unknown pin file schema.' }
    Write-Safe ('TOOLCHAIN-PINS sha256=' + (Get-FileSha256 $PinFile) + ' sdk=' + $pins.sdkVersion + ' packages=' + @($pins.packages).Count)
    $trustedGit = Get-TrustedGit
    Write-Safe ('GIT path=ProgramFiles\Git\cmd\git.exe binaries=' + $trustedGit.Binaries + ' signers=' + $trustedGit.Signers + ' problems=' + $trustedGit.Problems.Count)
    Write-Examples 'GIT-PROBLEM' $trustedGit.Problems
    if ($trustedGit.Problems.Count -gt 0) { throw 'Git for Windows is not a signed install under Program Files.' }
    $git = $trustedGit.Path; $script:Git = $git

    # 1. Worktree integrity (CB3.7b, amended: Git for Windows' default system Git LFS filter entries are tolerated only when
    #    no attribute source applies a filter to any tracked path).
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $commit = [string]$pins.sourceCommit
    if ($commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'The pin file names no build commit.' }
    $head = [string]($null | & $git -C $Worktree rev-parse --verify HEAD)
    # The commit object must hash to the pinned ID, and the listed trees must hash, folder by folder, to the commit's tree.
    $commitBytes = Invoke-GitBytes @('-C', $Worktree, 'cat-file', 'commit', $commit)
    $commitVerified = (Get-GitObjectId 'commit' $commitBytes) -ceq $commit
    $rootTree = ''; $commitText = $latin1.GetString($commitBytes); if ($commitText -cmatch '\Atree ([0-9a-f]{40})\n') { $rootTree = $Matches[1] }
    $listing = Invoke-GitBytes @('-C', $Worktree, 'ls-tree', '-r', '-t', '-z', '--full-tree', $commit)
    $folders = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal); $folders[''] = [Collections.Generic.List[object]]::new()
    $tree = [Collections.Generic.List[object]]::new(); $start = 0
    while ($start -lt $listing.Length) {
        $end = [Array]::IndexOf($listing, [byte]0, $start); if ($end -lt 0) { throw 'Unexpected git ls-tree output.' }
        $tab = [Array]::IndexOf($listing, [byte]9, $start, $end - $start); if ($tab -lt 0) { throw 'Unexpected git ls-tree output.' }
        $fields = [Text.Encoding]::ASCII.GetString($listing, $start, $tab - $start).Split(' ')
        if ($fields.Count -ne 3 -or $fields[0] -cnotmatch '\A[0-7]{6}\z' -or $fields[2] -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Unexpected git ls-tree output.' }
        $pathBytes = New-Object byte[] ($end - $tab - 1); [Array]::Copy($listing, $tab + 1, $pathBytes, 0, $pathBytes.Length)
        $slash = [Array]::LastIndexOf($pathBytes, [byte]47)
        $nameBytes = New-Object byte[] ($pathBytes.Length - $slash - 1); [Array]::Copy($pathBytes, $slash + 1, $nameBytes, 0, $nameBytes.Length)
        $path = [Text.Encoding]::UTF8.GetString($pathBytes); $parent = ''; if ($slash -ge 0) { $parent = [Text.Encoding]::UTF8.GetString($pathBytes, 0, $slash) }
        if (!$folders.ContainsKey($parent)) { throw 'git ls-tree listed an entry before its folder.' }
        $entry = [pscustomobject]@{ Mode = $fields[0].TrimStart('0'); Type = $fields[1]; Id = $fields[2]; Name = $nameBytes; Path = $path }
        $folders[$parent].Add($entry)
        if ($fields[1] -ceq 'tree') { $folders[$path] = [Collections.Generic.List[object]]::new() } else { $tree.Add($entry) }
        $start = $end + 1
    }
    $folderIds = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal); $treeMismatches = 0
    foreach ($folder in @($folders.Keys | Sort-Object -Descending -Property @{ Expression = { if ($_) { $_.Split('/').Count } else { 0 } } })) {
        $content = New-Object IO.MemoryStream
        foreach ($e in $folders[$folder]) {
            if ($e.Type -ceq 'tree' -and $folderIds[$e.Path] -cne $e.Id) { $treeMismatches++ }
            $modeBytes = [Text.Encoding]::ASCII.GetBytes($e.Mode + ' '); $content.Write($modeBytes, 0, $modeBytes.Length); $content.Write($e.Name, 0, $e.Name.Length); $content.WriteByte(0)
            for ($i = 0; $i -lt 40; $i += 2) { $content.WriteByte([Convert]::ToByte($e.Id.Substring($i, 2), 16)) }
        }
        $folderIds[$folder] = Get-GitObjectId 'tree' $content.ToArray()
    }
    $treesVerified = $treeMismatches -eq 0 -and $rootTree -and $folderIds[''] -ceq $rootTree
    $checked = 0; $mismatched = [Collections.Generic.List[string]]::new(); $crlfOnly = 0; $paths = [Collections.Generic.List[string]]::new()
    foreach ($e in $tree) {
        if ($e.Type -cne 'blob' -or $e.Mode -ceq '120000') { $mismatched.Add($e.Mode + ' ' + $e.Type + ' ' + $e.Path); continue }
        $checked++; $paths.Add($e.Path)
        $path = Join-Path $Worktree $e.Path.Replace('/', '\')
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $mismatched.Add($e.Path); continue }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ((Get-GitObjectId 'blob' $bytes) -ceq $e.Id) { continue }
        if ((Get-GitObjectId 'blob' $latin1.GetBytes($latin1.GetString($bytes).Replace("`r`n", "`n"))) -ceq $e.Id) { $crlfOnly++ } else { $mismatched.Add($e.Path) }
    }
    $filterLines = @($null | & $git -C $Worktree config --show-scope --get-regexp '^filter\.' 2>$null)
    $lfsNames = @('filter.lfs.clean', 'filter.lfs.smudge', 'filter.lfs.process', 'filter.lfs.required')
    $otherFilters = @($filterLines | ForEach-Object { $p = ([string]$_).Split(@(' ', "`t"), 3, [StringSplitOptions]::RemoveEmptyEntries); if ($p.Count -lt 2 -or $p[0] -cne 'system' -or $lfsNames -notcontains $p[1].ToLowerInvariant()) { if ($p.Count -ge 2) { $p[0] + ' ' + $p[1] } else { [string]$_ } } })
    $attributeLines = @($paths | & $git -C $Worktree -c core.quotePath=false check-attr --stdin filter)
    $filtered = @($attributeLines | Where-Object { !([string]$_).EndsWith(': filter: unspecified', [StringComparison]::Ordinal) })
    Write-Safe ('WORKTREE head=' + $head + ' commitVerified=' + $commitVerified + ' treesVerified=' + $treesVerified + ' trees=' + $folders.Count + ' entries=' + $tree.Count + ' checked=' + $checked + ' crlfOnly=' + $crlfOnly + ' mismatched=' + $mismatched.Count + ' filterEntries=' + $filterLines.Count + ' nonDefaultFilterEntries=' + $otherFilters.Count + ' attributeChecks=' + $attributeLines.Count + ' pathsWithFilter=' + $filtered.Count)
    Write-Examples 'WORKTREE-MISMATCH' $mismatched; Write-Examples 'WORKTREE-FILTER' $otherFilters; Write-Examples 'WORKTREE-FILTERED-PATH' $filtered
    if ($head -cne $commit) { Add-Failure 'worktree HEAD is not the pinned build commit' }
    if (!$commitVerified -or !$treesVerified) { Add-Failure 'Git did not return the pinned build commit''s own objects' }
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
            $groups = New-NameMap; $loose = New-NameMap; $entryCount = 0; $zipPaths = New-NameSet
            foreach ($entry in $zip.Entries) {
                if (!$entry.Name) { continue }
                $entryCount++; $relative = $entry.FullName.Replace('/', '\'); $parts = $relative.Split('\'); [void]$zipPaths.Add($relative)
                $at = -1; for ($i = 0; $i -lt $parts.Count - 1; $i++) { if (Test-VersionName $parts[$i]) { $at = $i; break } }
                if ($at -lt 0) { $loose[$relative] = $entry; continue }
                $root = ($parts[0..$at] -join '\')
                if (!$groups.ContainsKey($root)) { $groups[$root] = New-NameMap }
                $groups[$root][($parts[($at + 1)..($parts.Count - 1)] -join '\')] = $entry
            }
            $sideBySide = New-NameMap; $sideBySideSeen = New-NameMap; $sideBySideFiles = 0
            foreach ($m in @($pins.sdkSideBySideManifests)) { if ($m) { $sideBySide['sdk-manifests\' + $m.band + '\' + $m.id + '\' + $m.version] = $m } }
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
                foreach ($x in $comparison.Extra) {
                    $parts = $x.Split('\'); $folderKey = $root + '\' + $parts[0] + '\' + $parts[1]
                    if ($parts.Count -ge 3 -and $sideBySide.ContainsKey($folderKey)) {
                        if (!$sideBySideSeen.ContainsKey($folderKey)) { $sideBySideSeen[$folderKey] = [Collections.Generic.List[string]]::new() }
                        $sideBySideSeen[$folderKey].Add(($parts[2..($parts.Count - 1)] -join '/')); continue
                    }
                    $extra.Add($root + '\' + $x)
                }
                foreach ($x in $comparison.Reparse) { $reparse.Add($root + '\' + $x) }
                $parent = Split-Path -Parent $root; $leaf = Split-Path -Leaf $root
                $ancestor = $DotnetRoot; $walked = ''
                foreach ($segment in $parent.Split('\')) { $ancestor = Join-Path $ancestor $segment; $walked = ($walked + '\' + $segment).TrimStart('\'); if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { $reparse.Add($walked) } }
                foreach ($sibling in @(Get-ChildItem -LiteralPath (Join-Path $DotnetRoot $parent) -Directory -Force -ErrorAction SilentlyContinue)) {
                    if ($sibling.Name -ne $leaf -and (Test-VersionName $sibling.Name) -and (Get-NumericVersion $sibling.Name) -gt (Get-NumericVersion $leaf)) { $newer.Add($parent + '\' + $sibling.Name) }
                }
            }
            # Older side-by-side manifest folders: every file equals the pinned package's, and the zip's own version is higher.
            foreach ($folderKey in $sideBySideSeen.Keys) {
                $m = $sideBySide[$folderKey]; $pinned = @($m.files.PSObject.Properties)
                foreach ($f in $pinned) {
                    $path = Join-Path (Join-Path $DotnetRoot $folderKey) $f.Name.Replace('/', '\')
                    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $missing.Add($folderKey + '\' + $f.Name) }
                    elseif ((Get-FileSha256 $path) -cne [string]$f.Value) { $changed.Add($folderKey + '\' + $f.Name.Replace('/', '\')) }
                    else { $sideBySideFiles++ }
                }
                foreach ($name in $sideBySideSeen[$folderKey]) { if (@($pinned | Where-Object { $_.Name -ceq $name }).Count -eq 0) { $extra.Add($folderKey + '\' + $name.Replace('/', '\')) } }
                $zipVersions = @($groups['sdk-manifests\' + $m.band].Keys | Where-Object { $_.StartsWith($m.id + '\', [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.Split('\')[1] } | Sort-Object -Unique)
                if (@($zipVersions | Where-Object { (Test-VersionName $_) -and (Get-NumericVersion $_) -gt (Get-NumericVersion ([string]$m.version)) }).Count -eq 0) { $newer.Add($folderKey + ' is not older than the zip''s own version') }
            }
            # A workload install state or workload set could make the SDK pick another manifest version (or a user-local root).
            $workloadState = [Collections.Generic.List[string]]::new()
            foreach ($stateFolder in @('metadata\workloads')) {
                $statePath = Join-Path $DotnetRoot $stateFolder
                if (Test-Path -LiteralPath $statePath) { foreach ($file in Get-ChildItem -LiteralPath $statePath -Recurse -Force -File) { $relative = $file.FullName.Substring($DotnetRoot.Length + 1); if (!$zipPaths.Contains($relative)) { $workloadState.Add($relative) } } }
            }
            if ((Get-Item -LiteralPath $DotnetRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $reparse.Add('.') }
            $newer = @($newer | Sort-Object -Unique); $reparse = @($reparse | Sort-Object -Unique)
            Write-Safe ('SDK-FILES zipEntries=' + $entryCount + ' versionedFolders=' + $groups.Count + ' same=' + $same + ' missing=' + $missing.Count + ' changed=' + $changed.Count + ' extra=' + $extra.Count + ' newerFolders=' + $newer.Count + ' reparsePoints=' + $reparse.Count + ' sideBySideManifestFiles=' + $sideBySideFiles + ' workloadStateFiles=' + $workloadState.Count)
            Write-Examples 'SDK-MISSING' $missing; Write-Examples 'SDK-CHANGED' $changed; Write-Examples 'SDK-EXTRA' $extra; Write-Examples 'SDK-NEWER' $newer; Write-Examples 'SDK-REPARSE' $reparse; Write-Examples 'SDK-WORKLOAD-STATE' $workloadState
            if ($missing.Count + $changed.Count + $extra.Count + $newer.Count + $reparse.Count + $workloadState.Count -gt 0 -or $same -lt 1000) { Add-Failure 'the .NET SDK differs from the official zip' }
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
        # Asset paths are resolved inside each package's folder; none may climb out of it.
        foreach ($target in $assets.targets.PSObject.Properties) {
            foreach ($lib in $target.Value.PSObject.Properties) {
                if ($lib.Value.type -cne 'package') { continue }
                foreach ($category in $lib.Value.PSObject.Properties) {
                    if ($category.Value -isnot [Management.Automation.PSCustomObject]) { continue }
                    foreach ($asset in $category.Value.PSObject.Properties) { if ($asset.Name.IndexOf('..') -ge 0 -or $asset.Name.IndexOf(':') -ge 0 -or $asset.Name.StartsWith('/') -or $asset.Name.StartsWith('\')) { $folderProblems.Add($project + ' asset path leaves its package: ' + $lib.Name) } }
                }
            }
        }
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -ceq 'package' -and ([string]$library.Value.path) -cne $library.Name.ToLowerInvariant()) { $folderProblems.Add($project + ' library path differs: ' + $library.Name) }
        }
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
    # Every file in the worktree folder that the build commit does not track (untracked or ignored), found without Git.
    $tracked = New-NameSet; foreach ($e in $tree) { [void]$tracked.Add($e.Path.Replace('/', '\')) }
    # bin and obj folders are not entered (build output; obj imports are checked below), nor is .git.
    $outsideBuild = [Collections.Generic.List[string]]::new(); $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Worktree)
    if ((Get-Item -LiteralPath $Worktree -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { $outsideBuild.Add('. (reparse point)') }
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            $relative = $item.FullName.Substring($Worktree.Length + 1)
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $outsideBuild.Add($relative + ' (reparse point)'); continue }
            if ($relative -eq '.git') { continue }
            if ($item.PSIsContainer) { if (@('bin', 'obj') -notcontains $item.Name) { $pending.Push($item.FullName) }; continue }
            if (!$tracked.Contains($relative)) { $outsideBuild.Add($relative) }
        }
    }
    $outsideBuild = @($outsideBuild | Sort-Object)
    $objProblems = [Collections.Generic.List[string]]::new(); $generatedChecked = 0
    $packageFolderList = @($PackagesRoot) + @($fallbacks)
    foreach ($project in Get-ChildItem -LiteralPath (Join-Path $Worktree 'src') -Recurse -Force -Filter '*.csproj' -File) {
        $obj = Join-Path $project.DirectoryName 'obj'
        if (!(Test-Path -LiteralPath $obj -PathType Container)) { continue }
        $relativeObj = $obj.Substring($Worktree.Length + 1)
        foreach ($file in Get-ChildItem -LiteralPath $obj -Force -File) {
            if ($file.Name -notmatch '\.(props|targets)\z') { continue }
            if ($file.Name -ne ($project.Name + '.nuget.g.props') -and $file.Name -ne ($project.Name + '.nuget.g.targets')) { $objProblems.Add($relativeObj + '\' + $file.Name); continue }
            $generatedChecked++
            foreach ($problem in (Get-NuGetGeneratedFileProblems -Path $file.FullName -AssetsPath (Join-Path $obj 'project.assets.json') -UsedPackages $used -PackageFolders $packageFolderList)) { $objProblems.Add($relativeObj + '\' + $file.Name + ': ' + $problem) }
        }
    }
    $userEnvironment = [Environment]::GetEnvironmentVariables('User')
    $expectedNames = @('Path', 'TEMP', 'TMP', 'OneDrive', 'OneDriveConsumer', 'OneDriveCommercial', 'ChocolateyLastPathUpdate', 'ChocolateyToolsLocation') + $AllowUserEnvironmentName
    $unexpectedNames = @($userEnvironment.Keys | Where-Object { $expectedNames -notcontains $_ } | Sort-Object)
    $processRisky = @(Get-ChildItem Env: | Where-Object { $_.Name -match '\A(MSBuild|NUGET_|DOTNET_|COMPlus_|CORECLR_|Ilc|_?CL_?\z|_?LINK_?\z)' } | ForEach-Object { $_.Name } | Sort-Object)
    Write-Safe ('BUILD-HOOKS msbuildUserCurrent=' + $currentHooks.Count + ' msbuildUserOther=' + $otherMsbuildFiles + ' aboveWorktree=' + $ancestorHooks.Count + ' untrackedOutsideBinObj=' + $outsideBuild.Count + ' nugetGeneratedFiles=' + $generatedChecked + ' objImports=' + $objProblems.Count + ' userEnvironment=' + $userEnvironment.Count + ' unexpectedUserEnvironment=' + $unexpectedNames.Count + ' buildEnvironmentInThisWindow=' + $processRisky.Count)
    Write-Examples 'HOOK-MSBUILD-USER' $currentHooks; Write-Examples 'HOOK-ABOVE-WORKTREE' $ancestorHooks; Write-Examples 'HOOK-UNTRACKED' $outsideBuild; Write-Examples 'HOOK-OBJ' $objProblems
    Write-Examples 'HOOK-USER-ENVIRONMENT-NAME' $unexpectedNames; Write-Examples 'HOOK-WINDOW-ENVIRONMENT-NAME' $processRisky
    if ($currentHooks.Count + $ancestorHooks.Count + $outsideBuild.Count + $objProblems.Count + $unexpectedNames.Count -gt 0) { Add-Failure 'a build hook outside the SDK and packages is present' }
} catch {
    Add-Failure ('the check could not finish: ' + $_.Exception.Message)
}

if ($script:Failures.Count -eq 0) { Write-Output 'CB3.7c-OK'; exit 0 }
foreach ($failure in $script:Failures) { Write-Safe ('STOP: ' + $failure) }
exit 1
