using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Shared compiled-IL reader for the native boundary tests (Task 50 design rev 6 §4.1, rev-5 F4): yields every method,
/// field and type token operand of every method body with its outermost calling type, so aliases, Unicode-escaped
/// identifiers or generated sources cannot hide a reference. Also lists the type-reference table and method definitions.
/// </summary>
internal static class CompiledIlScanner
{
    internal enum ReferenceKind { Method, Field, Type }

    internal readonly record struct IlReference(string Caller, string TargetType, string TargetMember, ReferenceKind Kind);

    internal sealed record StructLayoutFacts(TypeAttributes Layout, int Packing, int Size, IReadOnlyList<(string Name, byte[] Signature)> Fields);

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>(outermost caller type, target declaring type, target member or empty for a type, kind) for each token operand.</summary>
    internal static IEnumerable<IlReference> References(string path)
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
                        case OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineField or OperandType.InlineType:
                            if (TryResolve(md, MetadataTokens.EntityHandle(il.ReadInt32()), out string type, out string member, out ReferenceKind kind))
                                yield return new IlReference(caller, type, member, kind);
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

    /// <summary>Full names of every entry in the assembly's type-reference table.</summary>
    internal static IReadOnlyList<string> TypeReferences(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        return [.. md.TypeReferences.Select(handle => FullName(md, handle))];
    }

    /// <summary>(declaring type, method name) of every method defined in the assembly.</summary>
    internal static IReadOnlyList<(string Type, string Method)> MethodDefinitions(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        return [.. md.MethodDefinitions.Select(handle => md.GetMethodDefinition(handle))
            .Select(method => (FullName(md, method.GetDeclaringType()), md.GetString(method.Name)))];
    }

    /// <summary>Layout facts of a type definition: layout kind, packing, size, and each field's name and signature blob.</summary>
    internal static StructLayoutFacts? StructLayout(string path, string fullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            if (FullName(md, handle) != fullName) continue;
            TypeDefinition definition = md.GetTypeDefinition(handle);
            TypeLayout layout = definition.GetLayout();
            List<(string, byte[])> fields = [];
            foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
            {
                FieldDefinition field = md.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) != 0) continue;
                fields.Add((md.GetString(field.Name), md.GetBlobBytes(field.Signature)));
            }
            return new StructLayoutFacts(definition.Attributes & TypeAttributes.LayoutMask, layout.PackingSize, layout.Size, fields);
        }
        return null;
    }

    internal static List<string> BuiltSrcAssemblies()
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

    internal static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static bool TryResolve(MetadataReader md, EntityHandle handle, out string type, out string member, out ReferenceKind kind)
    {
        type = member = string.Empty;
        kind = ReferenceKind.Method;
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinition definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                type = FullName(md, definition.GetDeclaringType());
                member = md.GetString(definition.Name);
                return true;
            case HandleKind.FieldDefinition:
                FieldDefinition field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                type = FullName(md, field.GetDeclaringType());
                member = md.GetString(field.Name);
                kind = ReferenceKind.Field;
                return true;
            case HandleKind.MemberReference:
                MemberReference reference = md.GetMemberReference((MemberReferenceHandle)handle);
                member = md.GetString(reference.Name);
                kind = reference.GetKind() == MemberReferenceKind.Method ? ReferenceKind.Method : ReferenceKind.Field;
                type = reference.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(md, (TypeReferenceHandle)reference.Parent),
                    HandleKind.TypeDefinition => FullName(md, (TypeDefinitionHandle)reference.Parent),
                    _ => string.Empty,
                };
                return type.Length != 0;
            case HandleKind.MethodSpecification:
                return TryResolve(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method, out type, out member, out kind);
            case HandleKind.TypeDefinition:
                type = FullName(md, (TypeDefinitionHandle)handle);
                kind = ReferenceKind.Type;
                return true;
            case HandleKind.TypeReference:
                type = FullName(md, (TypeReferenceHandle)handle);
                kind = ReferenceKind.Type;
                return true;
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

    internal static string FullName(MetadataReader md, TypeDefinitionHandle handle)
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
}
