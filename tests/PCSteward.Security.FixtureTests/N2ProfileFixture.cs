using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 50 design rev 6 §8 N2: a synthetic ProgramData/Windows tree under an N1 GUID temp root. <see cref="Build"/> creates a
/// supported Chocolatey 2.7.5-shaped root (fixture release files, a <c>git</c> 1.2.0 target, the client registration, a
/// store directory, hooks/extensions code, a writable log file), both machine.config files, every GAC root and a protected
/// cacheLocation. A test may change files before <see cref="Lock"/>, which locks the whole tree children-first for the
/// derived token, and change ACLs or add junctions after it. The fixture release table and engine-trust check reach the
/// observer only through the test-only seam.
/// </summary>
internal sealed class N2ProfileFixture : IDisposable
{
    internal const string Target = "git";
    internal const string MachineConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><configSections>" +
        "<section name=\"appSettings\" type=\"System.Configuration.AppSettingsSection, System.Configuration\"/></configSections>" +
        "<runtime><assemblyBinding xmlns=\"urn:schemas-microsoft-com:asm.v1\"><dependentAssembly>" +
        "<assemblyIdentity name=\"System.Web.Extensions\" publicKeyToken=\"31bf3856ad364e35\"/>" +
        "<bindingRedirect oldVersion=\"1.0.0.0-1.1.0.0\" newVersion=\"4.0.0.0\"/></dependentAssembly></assemblyBinding></runtime></configuration>";

    internal static readonly string[] ReleasePaths =
    [
        "choco.exe", "choco.exe.manifest", "helpers/chocolateyInstaller.psm1", "helpers/functions/Get-ChocolateyPath.ps1",
        "tools/7z.exe", "tools/checksum.exe", "tools/shimgen.exe", "tools/7z.exe.ignore",
    ];

    internal static readonly string[] GacRoots =
    [
        @"Microsoft.NET\assembly\GAC_MSIL", @"Microsoft.NET\assembly\GAC_32", @"Microsoft.NET\assembly\GAC_64",
        @"assembly\GAC", @"assembly\GAC_32", @"assembly\GAC_64", @"assembly\GAC_MSIL",
    ];

    internal N2ProfileFixture()
    {
        Native = new N1NativeFixture();
        ProgramData = Native.PathOf("ProgramData");
        Root = ProgramData + @"\chocolatey";
        Windows = Native.PathOf("Windows");
        Cache = Native.PathOf("Cache");
        HttpCache = ProgramData + @"\ChocolateyHttpCache";
    }

    internal N1NativeFixture Native { get; }
    internal string ProgramData { get; }
    internal string Root { get; }
    internal string Windows { get; }
    internal string Cache { get; }
    internal string HttpCache { get; set; }
    internal List<ChocolateyReleaseFile> Release { get; } = [];

    /// <summary>The release contents; a test may replace one before <see cref="Build"/> to change the table.</summary>
    internal Dictionary<string, string> ReleaseContent { get; } = ReleasePaths.ToDictionary(path => path, path => path == "tools/7z.exe.ignore" ? "" : "fixture:" + path);

    /// <summary>
    /// Binary release contents that take precedence over <see cref="ReleaseContent"/> (N3: a runnable stand-in engine). The
    /// fixture release table records their real length and SHA-256.
    /// </summary>
    internal Dictionary<string, byte[]> ReleaseBinary { get; } = [];

    internal string RootPath(string relative) => Root + '\\' + relative.Replace('/', '\\');

    internal N2ProfileFixture Build(string configuration = "")
    {
        System.IO.Directory.CreateDirectory(Root);
        foreach ((string path, string content) in ReleaseContent)
        {
            string full = RootPath(path);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            byte[] bytes = ReleaseBinary.TryGetValue(path, out byte[]? binary) ? binary : Encoding.UTF8.GetBytes(content);
            System.IO.File.WriteAllBytes(full, bytes);
            Release.Add(new ChocolateyReleaseFile(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        foreach (string tree in new[] { "bin", "lib", ".chocolatey", "config", "hooks", "extensions", "license", "lib-bkp", "lib-bad", "logs" })
            System.IO.Directory.CreateDirectory(RootPath(tree));
        System.IO.File.WriteAllText(RootPath(@"bin\choco.exe"), "shim");
        System.IO.File.WriteAllText(RootPath(@"logs\chocolatey.log"), "log");
        System.IO.File.WriteAllText(RootPath(@"hooks\pre-install-all.ps1"), "hook");
        System.IO.Directory.CreateDirectory(RootPath(@"extensions\fixture"));
        System.IO.File.WriteAllText(RootPath(@"extensions\fixture\fixture.psm1"), "extension");
        WritePackage(@"lib\git\git.nupkg", Target, "1.2.0");
        WritePackage(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", "2.7.5");
        System.IO.Directory.CreateDirectory(RootPath(@".chocolatey\git.1.2.0"));
        System.IO.File.WriteAllText(RootPath(@".chocolatey\git.1.2.0\.arguments"), "");
        System.IO.Directory.CreateDirectory(Cache);
        WriteConfiguration(configuration);
        foreach (string framework in new[] { "Framework", "Framework64" })
        {
            string config = Windows + @"\Microsoft.NET\" + framework + @"\v4.0.30319\Config";
            System.IO.Directory.CreateDirectory(config);
            System.IO.File.WriteAllText(config + @"\machine.config", MachineConfig);
        }
        foreach (string gac in GacRoots) System.IO.Directory.CreateDirectory(Windows + '\\' + gac);
        System.IO.Directory.CreateDirectory(Windows + @"\Microsoft.NET\assembly\GAC_MSIL\System.Web");
        return this;
    }

    internal void WriteConfiguration(string body) =>
        System.IO.File.WriteAllText(RootPath(@"config\chocolatey.config"),
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><chocolatey><config><add key=\"cacheLocation\" value=\"{Cache}\"/></config>{body}</chocolatey>");

    /// <summary>Profile V2 revision 2.1: replaces the configuration with the real-shape 2.7.5 file (cacheLocation defaults to <see cref="Cache"/>).</summary>
    internal void WriteRealShapeConfiguration(string? legacyValue = "true", string? cacheLocation = null) =>
        System.IO.File.WriteAllBytes(RootPath(@"config\chocolatey.config"), Chocolatey275ConfigurationFixture.Bytes(cacheLocation ?? Cache, legacyValue));

    internal void WritePackage(string relative, string id, string version)
    {
        string full = RootPath(relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using FileStream stream = new(full, FileMode.Create, FileAccess.Write);
        using ZipArchive zip = new(stream, ZipArchiveMode.Create);
        foreach ((string name, string content) in new[]
        {
            (id + ".nuspec", $"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>"),
            ("[Content_Types].xml", "<Types/>"),
        })
        {
            using Stream entry = zip.CreateEntry(name).Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    /// <summary>Locks every fixture node for the derived token (children first), then makes the log file requester-writable (M3).</summary>
    internal N2ProfileFixture Lock()
    {
        Native.LockTree(Native.Root);
        // Only when present: tests that remove the optional trees (logs among them) have no log file to open up.
        string log = RootPath(@"logs\chocolatey.log");
        if (File.Exists(log)) Native.Protect(log, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Modify);
        return this;
    }

    internal ChocolateyObserverTestSeam Seam(Func<SafeFileHandle, string, bool>? engineTrust = null) =>
        ChocolateyObserverTestSeam.CreateForTests(
            new ChocolateyNativeRoots(ProgramData, Root, HttpCache, Windows, Native.PathOf("ProgramFiles"), Native.PathOf("ProgramFilesX86")),
            Native.Root, Release, engineTrust ?? ((handle, _) => !handle.IsInvalid && !handle.IsClosed));

    /// <summary>ADR-031: the observer seam over a fixture release list (multi-entry lists, leftover rule).</summary>
    internal ChocolateyObserverTestSeam Seam(ChocolateyReleaseList releases, Func<SafeFileHandle, string, bool>? engineTrust = null) =>
        ChocolateyObserverTestSeam.CreateForTests(
            new ChocolateyNativeRoots(ProgramData, Root, HttpCache, Windows, Native.PathOf("ProgramFiles"), Native.PathOf("ProgramFilesX86")),
            Native.Root, releases, engineTrust ?? ((handle, _) => !handle.IsInvalid && !handle.IsClosed));

    /// <summary>ADR-031: this fixture's built release table as a list entry with the given version and asset digest.</summary>
    internal ChocolateyReleaseEntry ReleaseEntry(string version, string assetSha256) =>
        new(version, version + ".0", "chocolatey." + version + ".nupkg", 1000, assetSha256, [.. Release]);

    internal static ChocolateyNativeVersion Version(string text)
    {
        if (!ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? version)) throw new ArgumentException(text);
        return version!;
    }

    public void Dispose() => Native.Dispose();
}
