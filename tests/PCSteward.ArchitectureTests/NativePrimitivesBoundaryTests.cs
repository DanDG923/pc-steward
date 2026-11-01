using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2 (M1), §2.1 (M5), §2.3, §2.4 and §8 N1: the N1 native primitives
/// stay inside <c>Provider.Chocolatey/Native/</c> with no <c>src</c> caller outside it until N2; their test-only entry
/// points have no <c>src</c> caller at all (checked on compiled IL); they read no environment variables, registry or
/// SQLite; the reader, walker and evaluator open nothing write-capable and never impersonate; and the two
/// <c>FILE_ID_INFO</c> P/Invoke layouts are identical.
/// </summary>
[TestClass]
public sealed class NativePrimitivesBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string NativeNamespace = "PCSteward.Provider.Chocolatey.Native";

    /// <summary>Every N1 type, nested types included through the "+" separator.</summary>
    private static readonly string[] N1Types =
    [
        "NativeFileIdentity", "NativeObservedFile", "PeerProcess", "TokenFacts", "TokenVerdict", "TokenElevationKind",
        "AdministratorsMembership", "RequesterTokenPolicy", "RequesterToken", "RequesterModification", "AccessMode",
        "RequesterAccess", "NativeNode", "ProtectedOpenFailure", "ProtectedNodeReader", "ProtectedTreeWalker", "TreeWalkResult",
        "TreeWalkBounds", "TreeWalkRefusal", "ChildPresence", "NativeDirectoryEntry", "NativeTokenGroup", "NativeTokenReader",
        "ProtectedNativeMethods", "ChocolateyNativeRoots", "ChocolateyNativePathResolver",
    ];

    private static readonly (string Type, string Member)[] TestOnly =
    [
        (Native + "RequesterToken", "ForTestsFromDerivedToken"),
        (Native + "RequesterAccess", "EvaluateDescriptorForTests"),
        (Native + "ProtectedTreeWalker", "WalkWithHookForTests"),
    ];

    private static readonly string[] N1Files =
    [
        "NativeFileIdentity.cs", "RequesterTokenPolicy.cs", "ProtectedNativeMethods.cs", "NativeTokenReader.cs", "PeerProcess.cs",
        "RequesterToken.cs", "ProtectedNodeReader.cs", "RequesterAccess.cs", "ProtectedTreeWalker.cs", "ChocolateyNativePathResolver.cs",
    ];

    [TestMethod]
    public void NoCompiledSrcCodeCallsAnN1TestOnlyEntryPoint()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member) in TestOnly)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");

        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                foreach ((string type, string member) in TestOnly)
                    if (reference.TargetType == type && reference.TargetMember == member)
                        violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {type}.{member}");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void NoCompiledSrcCodeOutsideNativeReferencesAnN1Type()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        List<string> violations = [];
        int insideNative = 0;
        foreach (string path in assemblies)
        {
            bool chocolatey = Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey";
            if (!chocolatey)
                foreach (string type in CompiledIlScanner.TypeReferences(path).Where(IsN1Type))
                    violations.Add($"{Path.GetFileName(path)} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => IsN1Type(reference.TargetType)))
            {
                if (reference.Caller.StartsWith(Native, StringComparison.Ordinal) && chocolatey) insideNative++;
                else violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
            }
        }
        // The scanner works: the primitives reference each other inside Native/.
        Assert.IsTrue(insideNative > 50, $"only {insideNative} in-Native references resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void NoSourceFileOutsideNativeNamesAnN1Type()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string native = Path.Combine(src, "PCSteward.Provider.Chocolatey", "Native") + Path.DirectorySeparatorChar;
        Regex names = new(@"\b(" + string.Join('|', N1Types) + @")\b", RegexOptions.CultureInvariant);
        foreach ((string path, string source) in SourceFiles(src).Where(pair => !pair.Key.StartsWith(native, StringComparison.Ordinal)))
            Assert.IsFalse(names.IsMatch(source), $"{path} names an N1 type ({names.Match(source).Value})");
    }

    [TestMethod]
    public void N1CodeReadsNoEnvironmentRegistryOrSqlite()
    {
        Dictionary<string, string> files = N1Sources();
        string[] forbidden =
        [
            "GetEnvironmentVariable", "ExpandEnvironmentVariables", "EnvironmentVariableTarget", "GetEnvironmentStrings",
            "GetFolderPath", "SpecialFolder", "Registry", "RegOpenKey", "RegQueryValue", "RegGetValue",
        ];
        foreach ((string path, string source) in files)
        {
            foreach (string token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
            Assert.IsFalse(source.Contains("sqlite", StringComparison.OrdinalIgnoreCase), $"{path} uses SQLite");
        }
        // N1 review L3: the whole Native/ folder (N0's files too) is free of environment, registry and SQLite access.
        string[] ambient = ["GetEnvironmentVariable", "ExpandEnvironmentVariables", "EnvironmentVariableTarget", "GetEnvironmentStrings",
            "Registry", "RegOpenKey", "RegQueryValue", "RegGetValue"];
        foreach ((string path, string source) in SourceFiles(NativeDirectory()))
        {
            foreach (string token in ambient)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
            Assert.IsFalse(source.Contains("sqlite", StringComparison.OrdinalIgnoreCase), $"{path} uses SQLite");
        }
    }

    [TestMethod]
    public void TheReaderWalkerAndEvaluatorOpenNothingWriteCapableAndNeverImpersonate()
    {
        Dictionary<string, string> files = N1Sources();
        string methods = files.Single(pair => Path.GetFileName(pair.Key) == "ProtectedNativeMethods.cs").Value;
        StringAssert.Contains(methods, "internal const uint ReadOnlyOpenAccess = 0x00120089;");
        StringAssert.Contains(methods, "internal const uint FileShareRead = 0x00000001;");
        Assert.AreEqual(1, Regex.Count(methods, @"\bCreateFile\s*\("), "exactly one CreateFile declaration");
        Assert.IsFalse(Regex.IsMatch(methods, @"const\s+uint\s+(Generic(Write|All)|FileWrite\w*|FileShare(Write|Delete)|CreateAlways|OpenAlways|CreateNew|TruncateExisting)\b"),
            "no write-capable access, share or disposition constant is declared");

        int opens = 0;
        foreach ((string path, string source) in files.Where(pair => Path.GetFileName(pair.Key) != "ProtectedNativeMethods.cs"))
        {
            foreach (Match open in Regex.Matches(source, @"\bCreateFile\s*\(([^;]*);"))
            {
                opens++;
                StringAssert.Contains(open.Groups[1].Value, "Win32.ReadOnlyOpenAccess, Win32.FileShareRead, IntPtr.Zero,", path);
                StringAssert.Contains(open.Groups[1].Value, "Win32.OpenExisting", path);
            }
            string[] forbidden =
            [
                "FileAccess.Write", "FileAccess.ReadWrite", "FileMode.", "FileStream", "File.Open", "File.Create", "File.Write",
                "File.Delete", "File.Move", "File.Replace", "Directory.Delete", "Directory.Move", "Directory.Create", "WriteFile",
                "DeleteFile", "MoveFile", "SetFileInformationByHandle", "SetSecurityInfo", "SetNamedSecurityInfo", "SetAccessControl",
                "ImpersonateLoggedOnUser", "SetThreadToken", "RunImpersonated", "Impersonate(", "CreateProcess", "Process.Start",
                "RandomAccess.Write",
            ];
            foreach (string token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
        }
        Assert.AreEqual(1, opens, "ProtectedNodeReader.TryOpen is the only N1 open");
        Assert.IsTrue(files.Single(pair => Path.GetFileName(pair.Key) == "ProtectedNodeReader.cs").Value
            .Contains("Win32.FileFlagOpenReparsePoint | (directory ? Win32.FileFlagBackupSemantics : 0)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheFileIdInfoLayoutEqualsExecutions()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        CompiledIlScanner.StructLayoutFacts native = CompiledIlScanner.StructLayout(chocolatey, Native + "ProtectedNativeMethods+FileIdInformation")
            ?? throw new AssertFailedException("N1 FileIdInformation not found");
        CompiledIlScanner.StructLayoutFacts reference = CompiledIlScanner.StructLayout(execution, "PCSteward.Execution.DeploymentFileAccess+NativeMethods+FileIdInformation")
            ?? throw new AssertFailedException("Execution FileIdInformation not found");

        Assert.AreEqual(TypeAttributes.SequentialLayout, native.Layout);
        Assert.AreEqual(reference.Layout, native.Layout);
        Assert.AreEqual(reference.Packing, native.Packing);
        Assert.AreEqual(reference.Size, native.Size);
        Assert.AreEqual(3, native.Fields.Count);
        Assert.AreEqual(reference.Fields.Count, native.Fields.Count);
        for (int index = 0; index < reference.Fields.Count; index++)
        {
            Assert.AreEqual(reference.Fields[index].Name, native.Fields[index].Name);
            CollectionAssert.AreEqual(reference.Fields[index].Signature, native.Fields[index].Signature, native.Fields[index].Name);
        }
        // Both read FileIdInfo (class 18) and treat a zero file ID as unreadable.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        StringAssert.Contains(File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "DeploymentFileAccess.cs")), "FileIdInfoClass = 18;");
        StringAssert.Contains(File.ReadAllText(Path.Combine(NativeDirectory(), "ProtectedNativeMethods.cs")), "FileIdInfoClass = 18;");
    }

    [TestMethod]
    public void TheTokenAndWalkResultHaveOnlyPrivateConstruction()
    {
        Dictionary<string, string> files = N1Sources();
        string token = files.Single(pair => Path.GetFileName(pair.Key) == "RequesterToken.cs").Value;
        StringAssert.Contains(token, "private RequesterToken(");
        Assert.IsFalse(Regex.IsMatch(token, @"\b(internal|protected|public)\s+RequesterToken\s*\("));
        string peer = files.Single(pair => Path.GetFileName(pair.Key) == "PeerProcess.cs").Value;
        StringAssert.Contains(peer, "private PeerProcess(");
        string walker = files.Single(pair => Path.GetFileName(pair.Key) == "ProtectedTreeWalker.cs").Value;
        StringAssert.Contains(walker, "private TreeWalkResult() { }");
        StringAssert.Contains(walker, "private TreeWalkBounds(");
        Assert.AreEqual(2, Regex.Count(walker, @"sealed record \w+\([^)]*\) : TreeWalkResult;"), "TreeWalkResult is closed: Complete and Refused");
    }

    private static bool IsN1Type(string fullName)
    {
        if (!fullName.StartsWith(Native, StringComparison.Ordinal)) return false;
        string name = fullName[Native.Length..].Split('+')[0];
        return N1Types.Contains(name);
    }

    private static Dictionary<string, string> N1Sources()
    {
        Dictionary<string, string> native = SourceFiles(NativeDirectory());
        Dictionary<string, string> n1 = native.Where(pair => N1Files.Contains(Path.GetFileName(pair.Key))).ToDictionary();
        CollectionAssert.AreEquivalent(N1Files, n1.Keys.Select(Path.GetFileName).ToArray(), "N1 file list changed");
        foreach ((string path, string source) in n1)
            StringAssert.Contains(source, "namespace " + NativeNamespace + ";", path);
        return n1;
    }

    private static string NativeDirectory() =>
        Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");

    private static Dictionary<string, string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(path => path, File.ReadAllText);
}
