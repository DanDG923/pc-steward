using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §3.1 and §4 (checked on compiled IL): the reviewed release list is the only
/// source of a release. The test-only list factory has no <c>src</c> caller; a release and a list have only private
/// constructors; a release is created only by its list; and the compiled 2.7.5 table is read only by the production list
/// (and the observer's test-only single-table seam), so no other code can treat a table as a trusted release.
/// </summary>
[TestClass]
public sealed class ReleaseListBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string List = Native + "ChocolateyReleaseList";
    private const string Release = Native + "ChocolateyReviewedRelease";
    private const string Manifest = Native + "ChocolateyReleaseManifest2_7_5";
    private const string Seam = Native + "ChocolateyObserverTestSeam";

    [TestMethod]
    public void NoCompiledSrcCodeCallsTheTestOnlyListFactory()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = Chocolatey(assemblies);
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(chocolatey).Contains((List, "CreateForTests")), "update the ban list");
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == List && reference.TargetMember == "CreateForTests" && reference.Caller != Seam)
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} calls {List}.CreateForTests");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void ReleasesComeOnlyFromAListAndTheCompiledTableOnlyFromTheProductionList()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string chocolatey = Chocolatey(assemblies);
        foreach (string type in new[] { List, Release })
        {
            List<MethodAttributes> constructors = InstanceConstructors(chocolatey, type);
            Assert.IsTrue(constructors.Count > 0, $"{type} has no constructor");
            Assert.IsTrue(constructors.All(access => (access & MethodAttributes.MemberAccessMask) == MethodAttributes.Private), $"{type} has a non-private constructor");
        }
        List<string> violations = [];
        int listCreates = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetType == Release && reference.TargetMember is "Create" or ".ctor")
                {
                    if (reference.Caller == List || reference.Caller == Release) listCreates++;
                    else violations.Add($"{Path.GetFileName(path)}: {reference.Caller} creates a release");
                }
                if (reference.TargetType == List && reference.TargetMember == ".ctor" && reference.Caller != List)
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} constructs a release list");
                if (reference.TargetType == Manifest && reference.Caller is not (List or Seam or Manifest))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} reads the compiled 2.7.5 table");
            }
        Assert.IsTrue(listCreates > 0, "the list's release factory call was not resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    private static string Chocolatey(List<string> assemblies) =>
        assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");

    private static List<MethodAttributes> InstanceConstructors(string path, string fullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<MethodAttributes> result = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            if (CompiledIlScanner.FullName(md, handle) != fullName) continue;
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == ".ctor") result.Add(method.Attributes);
            }
        }
        return result;
    }
}
