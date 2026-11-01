using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 6 §4.1 (rev-5 F4, N0 review M1): the bans on the launcher core, the
/// test-only spec factory and the test-only fault entry are checked on <b>compiled</b> IL of every built <c>src</c>
/// assembly, so aliases, Unicode-escaped identifiers or generated sources cannot hide a call. Calls from inside the
/// declaring type (including its nested and compiler-generated types) are the only permitted call sites.
/// </summary>
[TestClass]
public sealed class NativeLauncherCompiledReferenceTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private static readonly (string Type, string Member)[] Forbidden =
    [
        (Native + "LaunchJob", "RunCoreAsync"),
        (Native + "LaunchJob", "RunCoreWithFaultForTestsAsync"),
        (Native + "NativeLaunchSpec", "ForTests"),
    ];
    private static readonly string[] RequiredAssemblies = ["PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper"];
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [TestMethod]
    public void NoCompiledSrcCodeCallsTheLauncherCoreTheSpecTestFactoryOrTheFaultEntryFromOutsideItsType()
    {
        IReadOnlyList<string> assemblies = BuiltSrcAssemblies();
        foreach (string required in RequiredAssemblies)
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required}.dll was not built");

        List<string> violations = [];
        int resolvedCalls = 0, launchJobInternalCalls = 0;
        foreach (string path in assemblies)
        {
            foreach ((string caller, string targetType, string targetMember) in Calls(path))
            {
                resolvedCalls++;
                if (caller == Native + "LaunchJob" && targetType == Native + "LaunchJob" && targetMember == "StartAsync")
                    launchJobInternalCalls++;
                foreach ((string type, string member) in Forbidden)
                    if (targetType == type && targetMember == member && caller != type)
                        violations.Add($"{Path.GetFileName(path)}: {caller} calls {type}.{member}");
            }
        }

        // The scanner works: it sees the core's own internal calls, and a meaningful volume of calls overall.
        Assert.IsTrue(resolvedCalls > 1000, $"only {resolvedCalls} calls resolved");
        Assert.AreEqual(2, launchJobInternalCalls, "RunCoreAsync and the fault entry each call StartAsync once");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Yields (outermost caller type, target declaring type, target member) for every method-token operand.</summary>
    private static IEnumerable<(string Caller, string TargetType, string TargetMember)> Calls(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        if (!pe.HasMetadata) yield break;
        MetadataReader md = pe.GetMetadataReader();
        foreach (TypeDefinitionHandle typeHandle in md.TypeDefinitions)
        {
            string caller = OutermostName(md, typeHandle);
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(typeHandle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;
                BlobReader il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    short value = il.ReadByte();
                    if (value == 0xFE) value = unchecked((short)(0xFE00 | il.ReadByte()));
                    if (!OpCodesByValue.TryGetValue(value, out OpCode code))
                        throw new InvalidDataException($"{Path.GetFileName(path)} {caller}::{md.GetString(method.Name)} offset {il.Offset - 1}: unknown opcode {value}");
                    switch (code.OperandType)
                    {
                        case OperandType.InlineMethod or OperandType.InlineTok:
                            if (TryResolveMethod(md, MetadataTokens.EntityHandle(il.ReadInt32()), out string type, out string member))
                                yield return (caller, type, member);
                            break;
                        case OperandType.InlineSwitch:
                            // Read the count first: "il.Offset += 4 * il.ReadInt32()" would read Offset before the count.
                            int targets = il.ReadInt32();
                            il.Offset += 4 * targets;
                            break;
                        default:
                            il.Offset += OperandSize(code.OperandType);
                            break;
                    }
                }
            }
        }
    }

    private static bool TryResolveMethod(MetadataReader md, EntityHandle handle, out string type, out string member)
    {
        type = member = string.Empty;
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinition definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                type = FullName(md, definition.GetDeclaringType());
                member = md.GetString(definition.Name);
                return true;
            case HandleKind.MemberReference:
                MemberReference reference = md.GetMemberReference((MemberReferenceHandle)handle);
                if (reference.GetKind() != MemberReferenceKind.Method) return false;
                member = md.GetString(reference.Name);
                type = reference.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(md, (TypeReferenceHandle)reference.Parent),
                    HandleKind.TypeDefinition => FullName(md, (TypeDefinitionHandle)reference.Parent),
                    _ => string.Empty,
                };
                return type.Length != 0;
            case HandleKind.MethodSpecification:
                return TryResolveMethod(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method, out type, out member);
            default:
                return false;
        }
    }

    private static string OutermostName(MetadataReader md, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = md.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = definition.GetDeclaringType();
        return declaring.IsNil ? FullName(md, handle) : OutermostName(md, declaring);
    }

    private static string FullName(MetadataReader md, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = md.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = definition.GetDeclaringType();
        string name = md.GetString(definition.Name);
        return declaring.IsNil ? Qualify(md.GetString(definition.Namespace), name) : FullName(md, declaring) + "+" + name;
    }

    private static string FullName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference reference = md.GetTypeReference(handle);
        string name = md.GetString(reference.Name);
        return reference.ResolutionScope.Kind == HandleKind.TypeReference
            ? FullName(md, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name
            : Qualify(md.GetString(reference.Namespace), name);
    }

    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    private static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };

    private static List<string> BuiltSrcAssemblies()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        List<string> found = [];
        foreach (string project in Directory.EnumerateDirectories(src))
        {
            string name = Path.GetFileName(project);
            string bin = Path.Combine(project, "bin");
            if (!Directory.Exists(bin)) continue;
            string? newest = Directory.EnumerateFiles(bin, name + ".dll", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (newest is not null) found.Add(newest);
        }
        return found;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
