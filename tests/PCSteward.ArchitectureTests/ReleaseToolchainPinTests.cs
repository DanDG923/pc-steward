using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Codex security scan finding 1 (2026-11-12; FR-UPD-15, TM-03/13): a release payload is built only from Microsoft's official
/// SDK zip and NuGet packages pinned in scripts/release-toolchain-pins.json, checked before any release project builds, and
/// the owner's signing script refuses a payload record that does not name this commit's pin file.
/// </summary>
[TestClass]
public sealed class ReleaseToolchainPinTests
{
    private static string Root => CompiledIlScanner.FindRepositoryRoot();
    private static string Script(string name) => File.ReadAllText(Path.Combine(Root, "scripts", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [TestMethod]
    public void ThePinFileNamesTheGlobalJsonSdkFromMicrosoftAndEveryReleasePackage()
    {
        using JsonDocument pins = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "scripts", "release-toolchain-pins.json")));
        using JsonDocument global = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "global.json")));
        JsonElement pin = pins.RootElement;
        Assert.AreEqual("pc-steward-release-toolchain-pins:1", pin.GetProperty("schema").GetString());
        string version = pin.GetProperty("sdkVersion").GetString()!;
        Assert.AreEqual(global.RootElement.GetProperty("sdk").GetProperty("version").GetString(), version);
        JsonElement zip = pin.GetProperty("sdkZip");
        Assert.AreEqual($"https://builds.dotnet.microsoft.com/dotnet/Sdk/{version}/dotnet-sdk-{version}-win-x64.zip", zip.GetProperty("url").GetString());
        StringAssert.Matches(zip.GetProperty("sha512").GetString(), new Regex(@"\A[0-9a-f]{128}\z"));
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (JsonElement package in pin.GetProperty("packages").EnumerateArray())
        {
            StringAssert.Matches(package.GetProperty("sha512").GetString(), new Regex(@"\A[A-Za-z0-9+/]{86}==\z"));
            Assert.IsTrue(keys.Add(package.GetProperty("id").GetString()!.ToLowerInvariant() + "/" + package.GetProperty("version").GetString()!.ToLowerInvariant()));
        }
        // The Native AOT runtime and compiler packs are compiled into the elevated helper; they are pinned like any package.
        Assert.IsTrue(keys.Any(key => key.StartsWith("runtime.win-x64.microsoft.dotnet.ilcompiler/", StringComparison.Ordinal)), "the ILCompiler runtime pack is pinned");
        Assert.IsTrue(keys.Any(key => key.StartsWith("microsoft.windowsappsdk", StringComparison.Ordinal)), "the Windows App SDK is pinned");
    }

    [TestMethod]
    public void TheWixSdkThatBuildsTheMsiIsPinnedAndCheckedBeforeItRuns()
    {
        // Mythos whole-app review MY-01: the WiX SDK is the one package the MSI build restores; its bytes are pinned like every
        // release package, checked before dotnet starts, and offered to NuGet as the only file of the only source.
        using JsonDocument pins = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "scripts", "release-toolchain-pins.json")));
        JsonElement wix = pins.RootElement.GetProperty("msiSdk");
        string id = wix.GetProperty("id").GetString()!, version = wix.GetProperty("version").GetString()!;
        Assert.AreEqual("WixToolset.Sdk", id);
        string lower = id.ToLowerInvariant();
        Assert.AreEqual($"https://api.nuget.org/v3-flatcontainer/{lower}/{version}/{lower}.{version}.nupkg", wix.GetProperty("url").GetString());
        StringAssert.Matches(wix.GetProperty("sha512").GetString(), new Regex(@"\A[A-Za-z0-9+/]{86}==\z"));
        Assert.IsGreaterThan(1_000_000L, wix.GetProperty("bytes").GetInt64());
        string project = File.ReadAllText(Path.Combine(Root, "packaging", "msi", "PCSteward.Installer.wixproj"));
        StringAssert.StartsWith(project, $"<Project Sdk=\"{id}/{version}\">", "the project builds with the pinned WiX SDK");

        string msi = Script("New-PCStewardMsi.ps1");
        int check = msi.IndexOf("if ((Get-FileSha512Base64 $wixPackage) -cne [string]$wix.sha512) { throw", StringComparison.Ordinal);
        int build = msi.IndexOf("& $dotnetCommand @arguments", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, check, "the downloaded WiX SDK is checked against its pin");
        Assert.IsLessThan(build, check, "before dotnet starts");
        Assert.AreEqual(1, Regex.Count(msi, @"& \$dotnetCommand @arguments"), "one dotnet start");
        Assert.IsFalse(Regex.IsMatch(msi, @"&\s+dotnet\s"), "no other dotnet start");
        StringAssert.Contains(msi, "[IO.File]::ReadAllText((Join-Path $source 'PCSteward.Installer.wixproj'))).StartsWith(\"<Project Sdk=`\"$($wix.id)/$wixVersion`\">\"");
        StringAssert.Matches(msi, new Regex(@"<packageSources>\s*<clear />\s*<add key=""pinned-wix-sdk"" value=""\$escapedFeed"" />\s*</packageSources>"));
        StringAssert.Matches(msi, new Regex(@"<fallbackPackageFolders>\s*<clear />\s*</fallbackPackageFolders>"));
        StringAssert.Contains(msi, "NUGET_PACKAGES = $packages;");
        StringAssert.Contains(msi, "throw \"The MSI build restored something other than the pinned WiX SDK:");

        // The CI reference MSI (release workflow and the CI MSI job) is built with the pinned SDK, not the runner's.
        string reference = Script("New-PCStewardMsiReference.ps1");
        StringAssert.Contains(reference, "$sdkRoot = Install-PCStewardPinnedSdk -Pins $toolchainPins -Folder $sdkFolder");
        StringAssert.Contains(reference, "$msiArguments['Dotnet'] = Join-Path $sdkRoot 'dotnet.exe'");
        string workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release-payload.yml"));
        StringAssert.Contains(workflow, "./scripts/New-PCStewardMsiReference.ps1 -ReleasePayloadRoot artifacts/release-payload -PinnedSdk");
        StringAssert.Contains(Script("Test-PCStewardMsiCI.ps1"), "New-PCStewardMsiReference.ps1') -ReleasePayloadRoot $release -PinnedSdk");

        // The pin reader requires it and the pin printer keeps it.
        string toolchain = Script("PCStewardReleaseToolchain.ps1");
        StringAssert.Contains(toolchain, "throw 'release-toolchain-pins.json has no WiX SDK pin.'");
        StringAssert.Contains(toolchain, "msiSdk = [ordered]@{ id = [string]$Pins.MsiSdk.id;");
    }

    [TestMethod]
    public void RestoreUsesOnlyNuGetOrgAndNoFallbackPackageFolder()
    {
        string config = File.ReadAllText(Path.Combine(Root, "nuget.config"));
        StringAssert.Matches(config, new Regex(@"<packageSources>\s*<clear />\s*<add key=""nuget\.org"" value=""https://api\.nuget\.org/v3/index\.json"" protocolVersion=""3"" />\s*</packageSources>"));
        StringAssert.Matches(config, new Regex(@"<fallbackPackageFolders>\s*<clear />\s*</fallbackPackageFolders>"));
    }

    [TestMethod]
    public void ThePinFileBytesAreNeverConverted()
    {
        string attributes = File.ReadAllText(Path.Combine(Root, ".gitattributes"));
        StringAssert.Matches(attributes, new Regex(@"(?m)^scripts/release-toolchain-pins\.json -text\r?$"));
    }

    [TestMethod]
    public void TheLauncherChecksTheToolchainBeforeTheBuildUserStarts()
    {
        string launcher = Script("Test-OwnerOnlyCandidateBuilderCI.ps1");
        StringAssert.Contains(launcher, ". (Join-Path $PSScriptRoot 'PCStewardReleaseToolchain.ps1')");
        int sdk = launcher.IndexOf("$sdkRoot = Install-PCStewardPinnedSdk -Pins $toolchainPins -Folder $toolchain", StringComparison.Ordinal);
        int restore = launcher.IndexOf("Invoke-AsDisposableUser -Mode '-RestoreOnly'", StringComparison.Ordinal);
        int locked = launcher.IndexOf("    Set-AdministratorsOnly $cache", StringComparison.Ordinal);
        int check = launcher.IndexOf("Test-PCStewardRestoredPackages -Used $restored.Used -Pins $toolchainPins -PackagesRoot $cache", StringComparison.Ordinal);
        int refuse = launcher.IndexOf("throw 'The restored toolchain differs from scripts/release-toolchain-pins.json; nothing was built.'", StringComparison.Ordinal);
        int build = launcher.IndexOf("Invoke-AsDisposableUser -Mode ('-StandardUser'", StringComparison.Ordinal);
        Assert.IsTrue(sdk > 0 && restore > sdk && locked > restore && check > locked && refuse > check && build > refuse, "SDK, restore, lock, check, refuse, then build");
        // The build user only reads the SDK and the checked cache.
        StringAssert.Contains(launcher, "-GrantModify @($repository) -GrantRead @($sdkRoot, $cache) -LogName 'build'");
        StringAssert.Contains(launcher, "$env:NUGET_PACKAGES = $cache");
        StringAssert.Contains(launcher, "$env:PATH = $sdkRoot + [IO.Path]::PathSeparator + $env:PATH");
        // The release record names the pin file.
        StringAssert.Contains(launcher, "toolchain = [ordered]@{ pinsSha256 = $toolchainPins.Sha256; sdkVersion = $toolchainPins.SdkVersion; packages = $toolchainPins.Packages.Count }");

        string library = Script("PCStewardReleaseToolchain.ps1");
        StringAssert.Contains(library, "if ($hash -cne [string]$Pins.SdkZip.sha512) { throw 'The downloaded SDK zip does not match its pinned SHA-512.' }");
        StringAssert.Contains(library, "foreach ($key in $Used.Keys) { if (!$Pins.Packages.ContainsKey($key)) { $problems.Add(\"unpinned package $key\") } }");
        StringAssert.Contains(library, "foreach ($download in @($framework.Value.downloadDependencies))");
    }

    [TestMethod]
    public void TheSigningScriptRefusesAPayloadWithoutThisCommitsToolchainPins()
    {
        string signer = Script("New-PCStewardRelease.ps1");
        int record = signer.IndexOf("throw 'release-payload.json does not name this run and commit.'", StringComparison.Ordinal);
        int toolchain = signer.IndexOf("[string]$record.toolchain.pinsSha256 -cne $toolchainPinsSha256", StringComparison.Ordinal);
        int signing = signer.IndexOf("Invoke-PCStewardReleaseSigning", StringComparison.Ordinal);
        Assert.IsTrue(record > 0 && toolchain > record && signing > toolchain, "the toolchain record is checked before anything is signed");
        StringAssert.Contains(signer, "$toolchainPinsSha256 = Get-PCStewardFileSha256 $toolchainPinsPath");
    }
}
