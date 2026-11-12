// Task 54A throwaway tool: prints every public type in a .winmd with its kind, enum fields and method names, sorted, so two
// WinGet COM versions can be compared with a plain text diff. Member signatures are not printed: WinRT interfaces are
// immutable once shipped, so a change shows up as a new versioned interface (IInstallOptions7 and so on) and a new name here.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: WinmdSurface <file.winmd>");
    return 2;
}

using FileStream stream = File.OpenRead(args[0]);
using PEReader pe = new(stream);
MetadataReader reader = pe.GetMetadataReader();
List<string> lines = [];
foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
{
    TypeDefinition type = reader.GetTypeDefinition(handle);
    string ns = reader.GetString(type.Namespace);
    if (!ns.StartsWith("Microsoft.Management", StringComparison.Ordinal) ||
        (type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
    {
        continue;
    }

    string name = reader.GetString(type.Name);
    string baseName = string.Empty;
    if (!type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeReference)
    {
        baseName = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type.BaseType).Name);
    }
    string kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface"
        : baseName switch { "Enum" => "enum", "ValueType" => "struct", "MulticastDelegate" => "delegate", _ => "class" };
    lines.Add($"{kind} {ns}.{name}");
    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
    {
        string field = reader.GetString(reader.GetFieldDefinition(fieldHandle).Name);
        if (field != "value__")
        {
            lines.Add($"{kind} {ns}.{name} :: field {field}");
        }
    }
    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
    {
        lines.Add($"{kind} {ns}.{name} :: method {reader.GetString(reader.GetMethodDefinition(methodHandle).Name)}");
    }
}

lines.Sort(StringComparer.Ordinal);
foreach (string line in lines.Distinct(StringComparer.Ordinal))
{
    Console.WriteLine(line);
}
return 0;
