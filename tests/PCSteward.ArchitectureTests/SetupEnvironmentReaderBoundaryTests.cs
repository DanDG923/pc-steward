using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §2 R5: the Native layer's ban on environment and registry access has exactly
/// one pinned, read-only exception, <see cref="ReaderFile"/>. It declares only <c>RegOpenKeyExW</c> and <c>RegQueryValueExW</c>
/// (the key handle closes through <c>SafeRegistryHandle</c>), opens keys with <c>KEY_QUERY_VALUE | KEY_WOW64_64KEY</c> only,
/// names exactly the two environment keys and exactly the three value names pinned setup.psm1 uses to weaken or skip the
/// root ACL, never reads value data, and has no write, create, delete, enumerate or expand capability. No other
/// <c>src</c> file declares or calls a registry API. Its <c>Read</c> callers are pinned in <c>ClientQueriesBoundaryTests</c>.
/// </summary>
[TestClass]
public sealed class SetupEnvironmentReaderBoundaryTests
{
    internal const string ReaderFile = "ChocolateySetupEnvironmentReader.cs";

    private static readonly string[] DeclaredNames = ["RegOpenKeyEx", "RegQueryValueEx"];
    private static readonly string[] EntryPoints = ["RegOpenKeyExW", "RegQueryValueExW"];
    private static readonly string[] Literals =
    [
        "ChocolateyInstallAllowCurrentUser", "ChocolateyAllowInsecureRootDirectory", "ChocolateyInstall", "Environment",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "advapi32.dll", "RegOpenKeyExW", "RegQueryValueExW",
    ];

    [TestMethod]
    public void TheReaderIsReadOnlyAndNamesExactlyThePinnedKeysAndValues()
    {
        string source = File.ReadAllText(Path.Combine(NativeDirectory(), ReaderFile));
        string code = CodeOnly(source);

        string[] declared = [.. Regex.Matches(code, @"static\s+extern\s+\w+\s+(\w+)\s*\(").Select(match => match.Groups[1].Value)];
        CollectionAssert.AreEquivalent(DeclaredNames, declared, "exactly the two read entry points");
        CollectionAssert.AreEquivalent(EntryPoints,
            Regex.Matches(code, @"EntryPoint\s*=\s*""(\w+)""").Select(match => match.Groups[1].Value).ToArray());
        Assert.AreEqual(2, Regex.Count(code, @"\[DllImport\(""advapi32\.dll"""), "advapi32 only");

        StringAssert.Contains(code, "private const uint KeyQueryValue = 0x0001;");
        StringAssert.Contains(code, "private const uint KeyWow64View64 = 0x0100;");
        StringAssert.Contains(code, "private const uint ReadOnlyKeyAccess = KeyQueryValue | KeyWow64View64;");
        Assert.AreEqual(1, Regex.Count(code, @"RegOpenKeyEx\(hive, path, 0, ReadOnlyKeyAccess, out SafeRegistryHandle key\)"), "one read-only open");
        // Value data is never read: the only query passes no buffer.
        Assert.AreEqual(1, Regex.Count(code, @"RegQueryValueEx\(key, name, IntPtr\.Zero, out _, IntPtr\.Zero, ref size\)"), "presence-only query");

        foreach (string token in new[]
                 {
                     "RegSetValue", "RegCreateKey", "RegDeleteKey", "RegDeleteValue", "RegDeleteTree", "RegEnumValue", "RegEnumKey",
                     "RegSaveKey", "RegRestoreKey", "RegLoadKey", "RegSetKeySecurity", "RegGetValue", "RegConnectRegistry",
                     "Microsoft.Win32.Registry", "Registry.", "RegistryKey", "SetValue", "CreateSubKey", "DeleteValue", "DeleteSubKey",
                     "OpenSubKey", "GetValueNames", "ExpandEnvironmentVariables", "GetEnvironmentVariable", "SetEnvironmentVariable",
                     "EnvironmentVariableTarget", "KEY_SET_VALUE", "KEY_WRITE", "KEY_ALL_ACCESS", "0x0002", "0x0004", "0x00020006", "0xF003F",
                 })
            Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"the reader uses {token}");

        CollectionAssert.AreEquivalent(Literals,
            StringLiterals(code).Distinct(StringComparer.Ordinal).ToArray(),
            "the reader's only string literals are the three pinned names, the two keys and the two entry points");
    }

    [TestMethod]
    public void NoOtherSrcFileDeclaresOrCallsARegistryApi()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string reader = Path.Combine(NativeDirectory(), ReaderFile);
        string[] tokens = ["RegOpenKeyEx", "RegQueryValueEx", "RegGetValue", "RegSetValue", "RegCreateKey", "RegDeleteValue", "SafeRegistryHandle"];
        foreach (string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") && path != reader))
        {
            string code = CodeOnly(File.ReadAllText(path));
            foreach (string token in tokens)
                Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
        }
        // Inside Native/ no other file uses even the managed registry surface (the existing bans cover the rest of the layer).
        foreach (string path in Directory.EnumerateFiles(NativeDirectory(), "*.cs").Where(path => path != reader &&
                     Path.GetFileName(path) != "ChocolateyReleaseManifest2_7_5.cs"))
            Assert.IsFalse(CodeOnly(File.ReadAllText(path)).Contains("Registry", StringComparison.Ordinal), path);
    }

    [TestMethod]
    public void TheEngineTrustHasNoImplicitReleaseAndTheProfileDataNoDefaultAsset()
    {
        // INFO (ADR-031 review): no code path can silently pin 2.7.5 through a two-argument trust check or a default asset digest.
        string native = NativeDirectory();
        string trust = CodeOnly(File.ReadAllText(Path.Combine(native, "ChocolateyEngineTrust.cs")));
        Assert.AreEqual(1, Regex.Count(trust, @"internal\s+static\s+bool\s+IsTrustedEngine\s*\("), "exactly one IsTrustedEngine");
        StringAssert.Contains(trust, "IsTrustedEngine(SafeFileHandle held, string canonicalPath, string expectedFileVersion)");
        string data = CodeOnly(File.ReadAllText(Path.Combine(Path.GetDirectoryName(native)!, "ChocolateyCommonProfileFingerprint.cs")));
        Assert.IsTrue(Regex.IsMatch(data, @"string\s+ReleaseAssetSha256\);"), "the release asset digest is a required parameter");
        Assert.IsFalse(Regex.IsMatch(data, @"ReleaseAssetSha256\s*="), "no default release asset digest");
    }

    private static IEnumerable<string> StringLiterals(string code) =>
        Regex.Matches(code, @"@?""((?:[^""\\]|\\.)*)""").Select(match => match.Groups[1].Value);

    private static string CodeOnly(string source) => string.Join('\n', source.Split('\n')
        .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string NativeDirectory() => Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");

    private static string FindRepositoryRoot() => CompiledIlScanner.FindRepositoryRoot();
}
