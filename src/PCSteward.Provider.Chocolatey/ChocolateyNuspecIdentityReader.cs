using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Parsed package identity data only; no source, installed state, native trust or approval assertion.</summary>
internal sealed record ChocolateyNuspecIdentity(string Id, ChocolateyNativeVersion Version);

internal static class ChocolateyNuspecIdentityReader
{
    private static readonly HashSet<string> Schemas = new(StringComparer.Ordinal)
    {
        string.Empty,
        "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd"
    };

    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ChocolateyNuspecIdentity? identity)
    {
        identity = null;
        if (bytes.Length is 0 or > 65536) return false;
        try
        {
            Encoding encoding = bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? new UnicodeEncoding(false, false, true) :
                bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ? new UnicodeEncoding(true, false, true) : new UTF8Encoding(false, true);
            if (encoding.GetString(bytes).Contains('\0')) return false;
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536,
                IgnoreComments = true, IgnoreWhitespace = true, ConformanceLevel = ConformanceLevel.Document
            };
            using (XmlReader guard = XmlReader.Create(stream, settings))
            {
                int nodes = 0;
                while (guard.Read())
                {
                    if (++nodes > 4096 || guard.Depth > 16 || guard.NodeType == XmlNodeType.ProcessingInstruction) return false;
                    if (guard.NodeType == XmlNodeType.XmlDeclaration && guard.GetAttribute("encoding") is { } declared &&
                        !declared.Equals("utf-8", StringComparison.OrdinalIgnoreCase) &&
                        !declared.Equals("utf-16", StringComparison.OrdinalIgnoreCase) &&
                        !declared.Equals("utf-16le", StringComparison.OrdinalIgnoreCase) &&
                        !declared.Equals("utf-16be", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            stream.Position = 0;
            using XmlReader reader = XmlReader.Create(stream, settings);
            XElement? root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "package" || !Schemas.Contains(root.Name.NamespaceName)) return false;
            XElement[] metadata = root.Elements().Where(element => element.Name.LocalName == "metadata").ToArray();
            if (metadata.Length != 1 || !Schemas.Contains(metadata[0].Name.NamespaceName)) return false;
            string nativeNamespace = metadata[0].GetDefaultNamespace().NamespaceName;
            if (!Schemas.Contains(nativeNamespace)) return false;
            XElement[] ids = metadata[0].Elements().Where(element => element.Name.LocalName == "id").ToArray();
            XElement[] versions = metadata[0].Elements().Where(element => element.Name.LocalName == "version").ToArray();
            if (ids.Length != 1 || versions.Length != 1 || ids[0].Name.NamespaceName != nativeNamespace ||
                versions[0].Name.NamespaceName != nativeNamespace || ids[0].HasElements || versions[0].HasElements ||
                ids[0].Attributes().Any(attribute => !attribute.IsNamespaceDeclaration) ||
                versions[0].Attributes().Any(attribute => !attribute.IsNamespaceDeclaration)) return false;
            string id = ids[0].Value;
            if (id.Length is 0 or > 256 || !char.IsAsciiLetterOrDigit(id[0]) || id.EndsWith('.') ||
                !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') ||
                !ChocolateyNativeVersion.TryParse(versions[0].Value, out ChocolateyNativeVersion? version)) return false;
            identity = new(id, version!);
            return true;
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException) { return false; }
    }
}
