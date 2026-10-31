using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Comparison data only. Neither this record nor its digest asserts native trust, qualification or approval.</summary>
internal sealed record ChocolateyCodeFileData(string RelativePath, string Identity, long Length, string Sha256);
internal sealed record ChocolateyCommonProfileData(string RequestingUserSid, string RootIdentity,
    IReadOnlyList<ChocolateyCodeFileData> CodeFiles, string CacheIdentity, ChocolateyConfigurationObservation Configuration,
    string TargetId, ChocolateyNativeVersion InstalledVersion, string ArchiveIdentity, string ArchiveSha256, string? StoreIdentity);

internal static class ChocolateyCommonProfileFingerprint
{
    internal static string FormatFileIdentity(ulong volumeSerial, ReadOnlySpan<byte> fileId)
    {
        if (fileId.Length != 16) throw new ArgumentException("A full FILE_ID_128 byte sequence is required.", nameof(fileId));
        return volumeSerial.ToString("X16", CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(fileId);
    }

    internal static string Compute(ChocolateyCommonProfileData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(data.Configuration);
        if (!ChocolateyNativePackageId.IsSupported(data.TargetId) || data.InstalledVersion is null ||
            data.Configuration.CommandExecutionTimeoutSeconds < 0 || data.Configuration.WebRequestTimeoutSeconds < 0 ||
            data.Configuration.Features.Count != ChocolateyConfigurationParser.SupportedFeatureDefaults.Count ||
            !ChocolateySourceResolution.TryResolve(data.Configuration, out ChocolateyConfiguredSource? source))
            throw new ArgumentException("A supported complete comparison snapshot is required.", nameof(data));
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Field(hash, "pc-steward-choco-profile:2");
        Field(hash, Sid(data.RequestingUserSid));
        Field(hash, FileIdentity(data.RootIdentity));
        Field(hash, ChocolateyClientReleasePolicy.AssetSha256);
        Field(hash, ComputeCodeList(data.CodeFiles));
        Field(hash, FileIdentity(data.CacheIdentity));
        foreach (string name in ChocolateyConfigurationParser.SupportedFeatureDefaults.Keys.Order(StringComparer.Ordinal))
        {
            if (!data.Configuration.Features.TryGetValue(name, out bool value)) throw new ArgumentException("Missing known feature.", nameof(data));
            Field(hash, name);
            Field(hash, Boolean(value));
        }
        Field(hash, Number(data.Configuration.CommandExecutionTimeoutSeconds));
        Field(hash, Number(data.Configuration.WebRequestTimeoutSeconds));
        Field(hash, Boolean(data.Configuration.ProxyConfigured));
        Field(hash, Boolean(data.Configuration.ProxyCredentialsConfigured));
        Field(hash, ChocolateyUpdateSourceConnector.CommunitySourceUrl);
        if (source is null)
        {
            for (int index = 0; index < 8; index++) Field(hash, "None");
        }
        else
        {
            if (source.Priority < 0) throw new ArgumentException("Unsupported source priority.", nameof(data));
            Field(hash, "Present");
            Field(hash, Boolean(source.Disabled));
            Field(hash, Boolean(source.BypassProxy));
            Field(hash, Boolean(source.SelfService));
            Field(hash, Boolean(source.AdminOnly));
            Field(hash, Number(source.Priority));
            Field(hash, Boolean(source.HasClientCertificate));
            Field(hash, Boolean(source.HasCredentials));
        }
        Field(hash, "clr4-no-licensed-v2");
        Field(hash, data.TargetId.ToUpperInvariant());
        Field(hash, data.InstalledVersion.Normalized);
        Field(hash, FileIdentity(data.ArchiveIdentity));
        Field(hash, Digest(data.ArchiveSha256));
        Field(hash, data.StoreIdentity is null ? "None" : FileIdentity(data.StoreIdentity));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string ComputeCodeList(IReadOnlyList<ChocolateyCodeFileData> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        int count = files.Count;
        if (count is < 0 or > 32768) throw new ArgumentException("Code-list entry bound exceeded.", nameof(files));
        ChocolateyCodeFileData[] snapshot = new ChocolateyCodeFileData[count];
        for (int index = 0; index < count; index++) snapshot[index] = files[index];
        List<ChocolateyCodeFileData> ordered = new(snapshot.Length);
        HashSet<string> names = new(StringComparer.Ordinal);
        long pathBytes = 0, contentBytes = 0;
        foreach (ChocolateyCodeFileData file in snapshot)
        {
            ArgumentNullException.ThrowIfNull(file);
            string name = CodePath(file.RelativePath);
            if (!names.Add(name) || file.Length is < 0 or > 64 * 1024 * 1024 ||
                (pathBytes += name.Length) > 1048576 || (contentBytes += file.Length) > 512 * 1024 * 1024)
                throw new ArgumentException("Ambiguous or oversized code-list data.", nameof(files));
            ordered.Add(file with { RelativePath = name, Identity = FileIdentity(file.Identity), Sha256 = Digest(file.Sha256) });
        }
        ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Field(hash, "pc-steward-choco-code-list:2");
        Field(hash, Number(ordered.Count));
        foreach (ChocolateyCodeFileData file in ordered)
        {
            Field(hash, file.RelativePath);
            Field(hash, file.Identity);
            Field(hash, Number(file.Length));
            Field(hash, file.Sha256);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Field(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 4096) throw new ArgumentException("Fingerprint field exceeds the encoding bound.", nameof(value));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string Boolean(bool value) => value ? "1" : "0";
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool UpperHex(char value) => value is >= '0' and <= '9' or >= 'A' and <= 'F';
    private static string Digest(string value) => value is { Length: 64 } && value.All(UpperHex)
        ? value : throw new ArgumentException("Canonical SHA256 data is required.", nameof(value));
    private static string FileIdentity(string value) => value is { Length: 49 } && value[16] == ':' &&
        value.AsSpan(0, 16).ToArray().All(UpperHex) && value.AsSpan(17).ToArray().All(UpperHex)
        ? value : throw new ArgumentException("Canonical native file-identity data is required.", nameof(value));

    private static string CodePath(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 4096 || value.Any(character => character is < ' ' or > '~' or ':' or '"' or '*' or '?' or '<' or '>' or '|'))
            throw new ArgumentException("Supported relative ASCII code path required.", nameof(value));
        string canonical = value.Replace('\\', '/').ToUpperInvariant();
        if (canonical.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Ambiguous relative code path.", nameof(value));
        return canonical;
    }

    private static string Sid(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 184) throw new ArgumentException("Requesting SID exceeds its bound.", nameof(value));
        string[] fields = value.Split('-');
        if (fields.Length is < 4 or > 18 || fields[0] != "S" || fields[1] != "1" ||
            !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong authority) ||
            authority > 0xFFFFFFFFFFFF || authority.ToString(CultureInfo.InvariantCulture) != fields[2])
            throw new ArgumentException("Canonical requesting SID data required.", nameof(value));
        foreach (string part in fields.AsSpan(3))
            if (!uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed) || parsed.ToString(CultureInfo.InvariantCulture) != part)
                throw new ArgumentException("Canonical SID subauthority required.", nameof(value));
        return value;
    }
}
