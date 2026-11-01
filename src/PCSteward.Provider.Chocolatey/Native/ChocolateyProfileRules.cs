using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>One discovered package archive of a client identity, as read through a held handle (registration digest input).</summary>
internal sealed record ChocolateyRegistrationRecord(string RelativePath, string Id, ChocolateyNativeVersion Version, string ArchiveIdentity,
    long Length, string Sha256);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 and Profile V2: the pure, portable rules the static profile observer
/// applies to facts it has already read through held handles. Nothing here reads a file, evaluates an ACL or grants trust;
/// every rule fails closed (false, null or "conflict") on anything it does not positively recognise.
/// </summary>
internal static class ChocolateyProfileRules
{
    /// <summary>Bound for each <c>machine.config</c> (the shipped files are well under 64 KiB).</summary>
    internal const int MachineConfigMaximumBytes = 1024 * 1024;
    internal const int MachineConfigMaximumDepth = 32;
    internal const int MachineConfigMaximumNodes = 65_536;
    internal const string LicensedAssembly = "chocolatey.licensed";
    private const string RegistrationPrefix = "pc-steward-choco-client-registration:1";

    /// <summary>The Profile V2 required effective features (the remaining features are observed and fingerprinted only).</summary>
    internal static readonly IReadOnlyList<(string Name, bool Value)> RequiredFeatures = Array.AsReadOnly(new (string, bool)[]
    {
        ("checksumFiles", true),
        ("useRememberedArgumentsForUpgrades", false),
        ("powershellHost", true),
        ("useEnhancedExitCodes", false),
        ("alwaysIncludeHeaders", false),
        ("exitOnRebootDetected", false),
    });

    private static readonly string[] ForbiddenLoaderElements =
    [
        "appDomainManagerAssembly", "appDomainManagerType", "codeBase", "developmentMode", "linkedConfiguration",
    ];

    private static readonly string[] ForbiddenLoaderAttributes = ["privatePath", "configSource"];

    /// <summary>
    /// A compiled release path: forward-slash separated canonical names, either one root file or a path under
    /// <c>helpers</c>/<c>tools</c>, ASCII only.
    /// </summary>
    internal static bool IsReleasePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Any(character => character is < ' ' or > '~' or '\\')) return false;
        string[] parts = path.Split('/');
        return parts.All(ProtectedNodeReader.IsCanonicalName) && (parts.Length == 1 || parts[0] is "helpers" or "tools");
    }

    /// <summary>Profile V2 L3 file-identity text: 16 hex volume-serial digits, a colon, then the 16 FILE_ID_128 bytes in returned order.</summary>
    internal static string FormatIdentity(NativeFileIdentity identity)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(bytes, identity.FileId);
        return ChocolateyCommonProfileFingerprint.FormatFileIdentity(identity.VolumeSerial, bytes);
    }

    /// <summary>Every Profile V2 required effective feature holds, over a complete known-feature observation.</summary>
    internal static bool HasRequiredFeatures(ChocolateyConfigurationObservation configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Features.Count != ChocolateyConfigurationParser.SupportedFeatureDefaults.Count) return false;
        foreach ((string name, bool value) in RequiredFeatures)
            if (!configuration.Features.TryGetValue(name, out bool effective) || effective != value) return false;
        return true;
    }

    /// <summary>
    /// Configured sources the fingerprint can encode: no negative priority anywhere (a matching source's priority is a
    /// fingerprint field, and the native ordering of a negative value is not modelled).
    /// </summary>
    internal static bool HasSupportedSources(ChocolateyConfigurationObservation configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.Sources.Count <= 64 && configuration.Sources.All(source => source.Priority >= 0);
    }

    /// <summary>
    /// <c>cacheLocation</c> format: nonempty, a canonical absolute local path below a drive root, and no <c>%</c> (no
    /// environment expansion can change which directory it names). Existence and protection are proven natively.
    /// </summary>
    internal static bool IsSupportedCacheLocation(string? value) =>
        value is { Length: > 3 } && !value.Contains('%', StringComparison.Ordinal) && ProtectedNodeReader.IsCanonicalLocalPath(value);

    /// <summary>
    /// Bounded CLR4 <c>machine.config</c> loader policy (§3 step 5): a well-formed <c>configuration</c> document without DTD,
    /// within the byte, depth and node bounds, with no app-domain manager, <c>codeBase</c>, <c>privatePath</c>,
    /// <c>developmentMode</c>, <c>linkedConfiguration</c> or <c>configSource</c> anywhere, and no attribute or text that names
    /// <c>chocolatey.licensed</c> (which covers a <c>dependentAssembly</c>, redirect, <c>qualifyAssembly</c> or publisher
    /// policy naming it). Names are compared case-insensitively, which is stricter than the case-sensitive CLR parser.
    /// </summary>
    internal static bool IsSupportedMachineConfig(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MachineConfigMaximumBytes) return false;
        try
        {
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MachineConfigMaximumBytes,
                IgnoreComments = true, IgnoreWhitespace = true, ConformanceLevel = ConformanceLevel.Document
            };
            using XmlReader reader = XmlReader.Create(stream, settings);
            int nodes = 0;
            bool root = false;
            while (reader.Read())
            {
                if (++nodes > MachineConfigMaximumNodes || reader.Depth > MachineConfigMaximumDepth) return false;
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (reader.Depth == 0)
                        {
                            if (root || !string.Equals(reader.LocalName, "configuration", StringComparison.Ordinal) || reader.NamespaceURI.Length != 0)
                                return false;
                            root = true;
                        }
                        if (ForbiddenLoaderElements.Contains(reader.LocalName, StringComparer.OrdinalIgnoreCase)) return false;
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (ForbiddenLoaderAttributes.Contains(reader.LocalName, StringComparer.OrdinalIgnoreCase) ||
                                    reader.Value.Contains(LicensedAssembly, StringComparison.OrdinalIgnoreCase))
                                    return false;
                            }
                            while (reader.MoveToNextAttribute());
                            reader.MoveToElement();
                        }
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                        if (reader.Value.Contains(LicensedAssembly, StringComparison.OrdinalIgnoreCase)) return false;
                        break;
                    case XmlNodeType.DocumentType or XmlNodeType.EntityReference:
                        return false;
                }
            }
            return root;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>A GAC entry name for the licensed assembly or one of its publisher policies (<c>policy.*.chocolatey.licensed</c>).</summary>
    internal static bool IsLicensedGacEntry(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Equals(LicensedAssembly, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("policy.", StringComparison.OrdinalIgnoreCase) && name.EndsWith("." + LicensedAssembly, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file the CLR could probe for the licensed assembly: <c>chocolatey.licensed.dll</c> or <c>.exe</c>, or a probing
    /// directory named <c>chocolatey.licensed</c>.
    /// </summary>
    internal static bool IsLicensedAssemblyNode(string leaf, bool directory)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        return directory
            ? leaf.Equals(LicensedAssembly, StringComparison.OrdinalIgnoreCase)
            : leaf.Equals(LicensedAssembly + ".dll", StringComparison.OrdinalIgnoreCase) || leaf.Equals(LicensedAssembly + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A native package-pending marker (<c>.chocolateyPending</c> or <c>.chocolateyPendingSkip</c>).</summary>
    internal static bool IsPendingMarker(string leaf) =>
        leaf.Equals(".chocolateyPending", StringComparison.OrdinalIgnoreCase) || leaf.Equals(".chocolateyPendingSkip", StringComparison.OrdinalIgnoreCase);

    /// <summary>A store marker that would override or pin the target (<c>.version</c>, <c>.sxs</c>, <c>.pin</c>).</summary>
    internal static bool IsStoreOverrideMarker(string leaf) =>
        leaf.Equals(".version", StringComparison.OrdinalIgnoreCase) || leaf.Equals(".sxs", StringComparison.OrdinalIgnoreCase) ||
        leaf.Equals(".pin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Store selection over the four pinned alternatives (native order, from <see cref="ChocolateyNativeVersion.GetStoreDirectoryNames"/>):
    /// false when more than one alternative exists among <paramref name="existingNames"/> (case-insensitive, as on NTFS);
    /// otherwise true with the one existing name, or null when none exists. No suffix is ever guessed.
    /// </summary>
    internal static bool TrySelectStore(IReadOnlyList<string> alternatives, IEnumerable<string> existingNames, out string? selected)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        ArgumentNullException.ThrowIfNull(existingNames);
        selected = null;
        string[] matches = [.. existingNames.Where(name => alternatives.Contains(name, StringComparer.OrdinalIgnoreCase))];
        if (matches.Length > 1) return false;
        selected = matches.Length == 1 ? matches[0] : null;
        return true;
    }

    /// <summary>
    /// The unsafe native <c>lib-bkp\&lt;id&gt;*</c> cleanup glob: a <c>lib-bkp</c> entry conflicts when its long name or its
    /// 8.3 short name starts with the target ID under the native case-insensitive comparison (a Win32 wildcard also
    /// matches short names).
    /// </summary>
    internal static bool IsLibBackupConflict(string name, string shortName, string id)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(shortName);
        ArgumentException.ThrowIfNullOrEmpty(id);
        return name.StartsWith(id, StringComparison.OrdinalIgnoreCase) || shortName.StartsWith(id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// File-only digest of one client identity's registration (Task 90 M2): every discovered archive whose nuspec ID is
    /// that identity, ordered by relative path, with its path, ID, original and normalized version, file identity, length
    /// and SHA-256, as unsigned 32-bit little-endian length-prefixed UTF-8 fields. Null means observed absence.
    /// </summary>
    internal static string? ComputeRegistrationDigest(IReadOnlyList<ChocolateyRegistrationRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0) return null;
        ChocolateyRegistrationRecord[] ordered = [.. records.OrderBy(record => record.RelativePath.ToUpperInvariant(), StringComparer.Ordinal)];
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Field(hash, RegistrationPrefix);
        Field(hash, ordered.Length.ToString(CultureInfo.InvariantCulture));
        foreach (ChocolateyRegistrationRecord record in ordered)
        {
            Field(hash, record.RelativePath.Replace('\\', '/').ToUpperInvariant());
            Field(hash, record.Id.ToUpperInvariant());
            Field(hash, record.Version.Original);
            Field(hash, record.Version.Normalized);
            Field(hash, record.ArchiveIdentity);
            Field(hash, record.Length.ToString(CultureInfo.InvariantCulture));
            Field(hash, record.Sha256);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Field(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
