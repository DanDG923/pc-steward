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
