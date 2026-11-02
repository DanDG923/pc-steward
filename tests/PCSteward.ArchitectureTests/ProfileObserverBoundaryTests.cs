using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3, §3.1, §5.4 and §8 N2 (checked on compiled IL): the observer's test
/// seams have no <c>src</c> caller; the observation, its held-file holder and every result arm have only private
/// constructors, with factory call sites only in the observer; no <c>src</c> code outside <c>Native/</c> names an N2 type
/// yet; the N2 files open nothing write-capable, never read the manifest JSON, and never resolve the <c>bin</c> shim.
/// </summary>
[TestClass]
public sealed class ProfileObserverBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Observer = Native + "ChocolateyProfileObserver";
    private const string Observation = Native + "ChocolateyProfileObservation";
    private const string Result = Native + "ChocolateyProfileResult";
    private const string Held = Native + "ChocolateyHeldProfileFiles";
    private const string Seam = Native + "ChocolateyObserverTestSeam";
    private const string Binding = Native + "ChocolateyHeldClientBinding";

    private const string ManifestFile = "ChocolateyReleaseManifest2_7_5.cs";

    private static readonly string[] N2Types =
    [
        "ChocolateyProfileObserver", "ChocolateyProfileObservation", "ChocolateyProfileResult", "ChocolateyProfileRefusal",
        "ChocolateyObserverRole", "ChocolateyHeldProfileFiles", "ChocolateyHeldFile", "ChocolateyObserverTestSeam",
        "ChocolateyReleaseManifest2_7_5", "ChocolateyReleaseFile", "ChocolateyProfileRules", "ChocolateyRegistrationRecord",
        "ChocolateyEngineTrust",
    ];

    private static readonly string[] N2Files =
    [
        "ChocolateyProfileObserver.cs", "ChocolateyProfileObservation.cs", "ChocolateyProfileRules.cs", "ChocolateyEngineTrust.cs",
        "ChocolateyReleaseManifest2_7_5.cs",
    ];

    private static readonly (string Type, string Member)[] TestOnly =
    [
        (Observer, "ObserveForTests"),
        (Observer, "ObserveClientForTests"),
        (Seam, "CreateForTests"),
    ];

    /// <summary>(type, factory) → the only outermost type allowed to call it.</summary>
    private static readonly (string Type, string Member, string Caller)[] Factories =
    [
        (Observation, "Create", Observer),
        (Held, "Begin", Observer),
        // N3: the retained binding is built only by the observer, from the holder it sealed.
        (Binding, "Create", Observer),
        (Binding, "CreateForClientUpdate", Observer),
        (Result + "+Observed", "Create", Observer),
        (Result + "+Refused", "Create", Observer),
        (Result + "+TimedOut", "Create", Observer),
        (Result + "+Cancelled", "Create", Observer),
    ];

    private static readonly string[] ResultArms = [Result + "+Observed", Result + "+Refused", Result + "+TimedOut", Result + "+Cancelled"];

    private static readonly string[] PrivatelyConstructed =
    [
        Observation, Held, Binding, Seam, Result, Result + "+Observed", Result + "+Refused", Result + "+TimedOut", Result + "+Cancelled",
    ];

    [TestMethod]
    public void NoCompiledSrcCodeCallsAnObserverTestSeam()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = Chocolatey(assemblies);
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member) in TestOnly)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");

        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (TestOnly.Contains((reference.TargetType, reference.TargetMember)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
                // The seam type itself is reachable only from the observer (its optional parameter) and from itself.
                if (reference.TargetType == Seam && reference.Caller is not (Observer or Seam))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references the observer test seam");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void TheObservationHolderAndResultArmsHaveOnlyPrivateConstructorsAndObserverFactories()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = Chocolatey(assemblies);
        Dictionary<string, List<(MethodAttributes Attributes, int Parameters)>> constructors = InstanceConstructors(chocolatey);
        foreach (string type in PrivatelyConstructed)
        {
            Assert.IsTrue(constructors.TryGetValue(type, out List<(MethodAttributes Attributes, int Parameters)>? access) && access.Count > 0,
                $"{type} has no constructor");
            // The abstract record base also has the compiler's protected copy constructor, which needs an existing instance.
            Assert.IsTrue(access.All(ctor => (ctor.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private ||
                type == Result && ctor.Parameters == 1 && (ctor.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family),
                $"{type} has a non-private constructor");
        }
        // The result is closed: exactly the four nested arms derive from it, in every src assembly.
        string[] derived = [.. assemblies.SelectMany(DerivedFromResult)];
        CollectionAssert.AreEquivalent(ResultArms, derived);

        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member, _) in Factories)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the factory list");

        List<string> violations = [];
        int observerCalls = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                foreach ((string type, string member, string caller) in Factories)
                    if (reference.TargetType == type && reference.TargetMember == member)
                    {
                        if (reference.Caller == caller && path == chocolatey) observerCalls++;
                        else violations.Add($"{Path.GetFileName(path)}: {reference.Caller} calls {type}.{member}");
                    }
                // A private constructor is called only from inside its own outermost type.
                if (reference.TargetMember == ".ctor" && PrivatelyConstructed.Contains(reference.TargetType) &&
                    reference.Caller != reference.TargetType.Split('+')[0])
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} constructs {reference.TargetType}");
            }
        Assert.IsTrue(observerCalls >= Factories.Length, $"only {observerCalls} observer factory calls resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void NoCompiledSrcCodeOutsideNativeReferencesAnN2Type()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        List<string> violations = [];
        foreach (string path in assemblies)
        {
            bool chocolatey = Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey";
            if (!chocolatey)
                foreach (string type in CompiledIlScanner.TypeReferences(path).Where(IsN2Type))
                    violations.Add($"{Path.GetFileName(path)} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => IsN2Type(reference.TargetType)))
                if (!chocolatey || !reference.Caller.StartsWith(Native, StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void TheObserverNeverResolvesTheBinShimOrReadsTheManifestJson()
    {
        string chocolatey = Chocolatey(CompiledIlScanner.BuiltSrcAssemblies());
        List<string> violations = [];
        foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(chocolatey))
        {
            if (!reference.Caller.StartsWith(Native, StringComparison.Ordinal)) continue;
            if (reference.TargetType == "PCSteward.Provider.Chocolatey.MachineTrustedChocolatey")
                violations.Add($"{reference.Caller} references MachineTrustedChocolatey.{reference.TargetMember}");
            if (reference.TargetType.StartsWith("System.Text.Json.", StringComparison.Ordinal))
                violations.Add($"{reference.Caller} references {reference.TargetType}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        foreach ((string path, string source) in N2Sources())
        {
            // Only the generated table's documentation names the JSON it was generated from; no code names any JSON file.
            if (Path.GetFileName(path) != "ChocolateyReleaseManifest2_7_5.cs")
                Assert.IsFalse(source.Contains("TASK_50_CHOCOLATEY_RELEASE", StringComparison.Ordinal), $"{path} names the manifest JSON");
            Assert.IsFalse(source.Contains(".json\"", StringComparison.OrdinalIgnoreCase), $"{path} names a JSON file");
        }
    }

    [TestMethod]
    public void TheN2FilesOpenNothingWriteCapable()
    {
        string[] forbidden =
        [
            "FileStream", "File.Open", "File.Create", "File.Write", "File.Read", "File.Delete", "File.Move", "File.Replace",
            "File.Copy", "Directory.Delete", "Directory.Move", "Directory.Create", "Directory.Enumerate", "Directory.GetFiles",
            "CreateFile", "WriteFile", "DeleteFile", "MoveFile", "SetFileInformationByHandle", "SetSecurityInfo", "SetNamedSecurityInfo",
            "SetAccessControl", "ImpersonateLoggedOnUser", "SetThreadToken", "RunImpersonated", "CreateProcess", "Process.Start",
            "RandomAccess.Write", "FileAccess.Write", "FileMode.", "GetEnvironmentVariable", "ExpandEnvironmentVariables",
            "GetFolderPath", "SpecialFolder", "Registry",
        ];
        foreach ((string path, string source) in N2Sources().Where(pair => Path.GetFileName(pair.Key) != ManifestFile))
        {
            foreach (string token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{path} uses {token}");
            Assert.IsFalse(source.Contains("sqlite", StringComparison.OrdinalIgnoreCase), $"{path} uses SQLite");
        }
    }

    /// <summary>
    /// The generated table is exempt from the ambient-token scans only because it is data: every line is documentation, a
    /// fixed declaration, a constant, or one <c>new("path", length, "SHA256")</c> entry.
    /// </summary>
    [TestMethod]
    public void TheGeneratedReleaseTableContainsOnlyData()
    {
        string path = N2Sources().Keys.Single(file => Path.GetFileName(file) == ManifestFile);
        Regex[] allowed =
        [
            new(@"^$"), new(@"^\s*///( .*)?$"), new(@"^namespace PCSteward\.Provider\.Chocolatey\.Native;$"),
            new(@"^internal readonly record struct ChocolateyReleaseFile\(string Path, long Length, string Sha256\);$"),
            new(@"^internal static class ChocolateyReleaseManifest2_7_5$"), new(@"^\{$"), new(@"^\}$"), new(@"^    \{$"), new(@"^    \}\);$"),
            new(@"^    internal const string (Format|Release|Asset|AssetSha256) = ""[A-Za-z0-9.:_-]+"";$"),
            new(@"^    internal const long AssetLength = [0-9]+;$"),
            new(@"^    internal static IReadOnlyList<ChocolateyReleaseFile> Files \{ get; \} = Array\.AsReadOnly\(new ChocolateyReleaseFile\[\]$"),
            new(@"^        new\(""[A-Za-z0-9._/-]+"", [0-9]+, ""[0-9A-F]{64}""\),$"),
        ];
        string[] lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (string line in lines)
            Assert.IsTrue(allowed.Any(pattern => pattern.IsMatch(line)), $"unexpected line in the generated table: {line}");
        Assert.AreEqual(56, lines.Count(line => line.StartsWith("        new(\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TheEngineSignerEqualsTheExistingClientSigner()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey");
        Regex existing = new(@"const string ExpectedSignerSimpleName = ""([^""]+)"";", RegexOptions.CultureInvariant);
        string provider = existing.Match(File.ReadAllText(Path.Combine(src, "ChocolateyProvider.cs"))).Groups[1].Value;
        string native = existing.Match(File.ReadAllText(Path.Combine(src, "Native", "ChocolateyEngineTrust.cs"))).Groups[1].Value;
        Assert.AreEqual("Chocolatey Software, Inc", provider);
        Assert.AreEqual(provider, native);
    }

    private static bool IsN2Type(string fullName) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && N2Types.Contains(fullName[Native.Length..].Split('+')[0]);

    private static string Chocolatey(List<string> assemblies) =>
        assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");

    /// <summary>Access attributes and parameter count of every instance constructor, by full type name.</summary>
    private static Dictionary<string, List<(MethodAttributes Attributes, int Parameters)>> InstanceConstructors(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        Dictionary<string, List<(MethodAttributes, int)>> result = new(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            string name = CompiledIlScanner.FullName(md, handle);
            if (!name.StartsWith(Native, StringComparison.Ordinal)) continue;
            List<(MethodAttributes, int)> access = [];
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == ".ctor") access.Add((method.Attributes, method.GetParameters().Count));
            }
            result[name] = access;
        }
        return result;
    }

    /// <summary>Full names of the types in <paramref name="path"/> whose base type is <c>ChocolateyProfileResult</c>.</summary>
    private static List<string> DerivedFromResult(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> derived = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            EntityHandle baseType = md.GetTypeDefinition(handle).BaseType;
            if (baseType.IsNil) continue;
            string? name = baseType.Kind switch
            {
                HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)baseType),
                HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)baseType).Name),
                _ => null,
            };
            if (name is Result or "ChocolateyProfileResult") derived.Add(CompiledIlScanner.FullName(md, handle));
        }
        return derived;
    }

    private static Dictionary<string, string> N2Sources()
    {
        string native = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");
        Dictionary<string, string> files = N2Files.ToDictionary(name => Path.Combine(native, name), name => File.ReadAllText(Path.Combine(native, name)));
        foreach ((string path, string source) in files)
            StringAssert.Contains(source, "namespace PCSteward.Provider.Chocolatey.Native;", path);
        return files;
    }
}
