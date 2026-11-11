using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PCSteward.SelfUpdate;

/// <summary>
/// The signed release manifest <c>pcsteward-release.json</c> (ADR-032 section 2). It is the only release data the updater
/// trusts, and only after its signature has verified over the exact bytes. It carries no URL, command, path, argument or
/// script. The reader is strict: at most 8 KiB of UTF-8 without a BOM, one JSON object, the closed key set below with exact
/// types and no duplicates. Anything else is refused.
/// </summary>
public sealed record ReleaseManifest(
    int Schema,
    string Product,
    string Channel,
    ReleaseVersion Version,
    long Sequence,
    DateTimeOffset PublishedUtc,
    ReleaseVersion MinimumUpdaterVersion,
    string AssetName,
    long AssetSize,
    string AssetSha256,
    string PayloadManifestSha256,
    string SbomSha256,
    int SchemaVersionAfter,
    string KeyId)
{
    public const int MaximumBytes = 8 * 1024;

    /// <summary>Largest ZIP the updater will ever download (a hard cap independent of the signed size).</summary>
    public const long MaximumAssetBytes = 512L * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 4 };
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "schema", "product", "channel", "version", "sequence", "publishedUtc", "minimumUpdaterVersion", "assetName", "assetSize",
        "assetSha256", "payloadManifestSha256", "sbomSha256", "schemaVersionAfter", "keyId",
    };

    /// <summary>The one asset name a manifest for <paramref name="version"/> may name.</summary>
    public static string ExpectedAssetName(ReleaseVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return "PCSteward-" + version.Text + "-win-x64.zip";
    }

    public static bool TryRead(ReadOnlySpan<byte> bytes, out ReleaseManifest? manifest)
    {
        manifest = null;
        if (bytes.Length is 0 or > MaximumBytes || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return false;
        try
        {
            _ = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), JsonOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !Keys.Contains(property.Name)) return false;
                fields[property.Name] = property.Value;
            }
            if (fields.Count != Keys.Count) return false;

            if (!TryInt(fields["schema"], out int schema) || !TryString(fields["product"], 32, out string product) ||
                !TryString(fields["channel"], 32, out string channel) || !TryString(fields["version"], 64, out string versionText) ||
                !ReleaseVersion.TryParse(versionText, out ReleaseVersion? version) || !TryLong(fields["sequence"], out long sequence) ||
                sequence < 1 || !TryString(fields["publishedUtc"], 20, out string publishedText) ||
                !DateTimeOffset.TryParseExact(publishedText, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset published) ||
                !TryString(fields["minimumUpdaterVersion"], 64, out string minimumText) ||
                !ReleaseVersion.TryParse(minimumText, out ReleaseVersion? minimum) ||
                !TryString(fields["assetName"], 128, out string assetName) || !TryLong(fields["assetSize"], out long assetSize) ||
                assetSize is < 1 or > MaximumAssetBytes || !TryHash(fields["assetSha256"], out string assetHash) ||
                !TryHash(fields["payloadManifestSha256"], out string payloadHash) || !TryHash(fields["sbomSha256"], out string sbomHash) ||
                !TryInt(fields["schemaVersionAfter"], out int schemaAfter) || schemaAfter < 1 ||
                !TryString(fields["keyId"], 16, out string keyId) || !IsLowerHex(keyId, 16))
                return false;
            manifest = new(schema, product, channel, version!, sequence, published, minimum!, assetName, assetSize, assetHash, payloadHash, sbomHash, schemaAfter, keyId);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static bool TryString(JsonElement element, int maximumLength, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String) return false;
        string? text = element.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > maximumLength || text.Any(character => character is < ' ' or > '~')) return false;
        value = text;
        return true;
    }

    private static bool TryHash(JsonElement element, out string value) => TryString(element, 64, out value) && IsLowerHex(value, 64);

    private static bool TryLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.GetRawText().All(char.IsAsciiDigit) && element.TryGetInt64(out value);
    }

    private static bool TryInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.GetRawText().All(char.IsAsciiDigit) && element.TryGetInt32(out value);
    }
}
