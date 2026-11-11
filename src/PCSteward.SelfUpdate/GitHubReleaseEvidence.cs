using System.Globalization;
using System.Text.Json;
using PCSteward.Execution;

namespace PCSteward.SelfUpdate;

/// <summary>Why a published release was not trusted. Each value has one owner-facing sentence (<see cref="SelfUpdateText"/>).</summary>
public enum SelfUpdateRefusal
{
    None,
    ReleaseUnreadable,
    UnexpectedAssets,
    CheckFileUnreadable,
    CheckFileForOtherKeys,
    TagMismatch,
    RunNotFound,
    RunNotGenuine,
    CheckArtifactGone,
    CheckArtifactMismatch,
    MsiTooLarge,
    MsiDownloadFailed,
    MsiNotPinned,
    MsiTablesMismatch,
    UnexpectedError,
}

/// <summary>The parts of GitHub's release JSON the check uses. Everything here is untrusted until the checks pass.</summary>
internal sealed record PublishedRelease(string Tag, ReleaseVersion Version, DateTimeOffset? PublishedAt, long MsiBytes, long CheckZipBytes, string Notes);

/// <summary>
/// ADR-032 revision 2 §3–§4: pure checks of what GitHub's public REST API returns. Each method reads only the named fields,
/// refuses any other shape, and returns a refusal rather than throwing. No network access happens here.
/// </summary>
internal static class GitHubReleaseEvidence
{
    internal const long MaximumMsiBytes = 512L * 1024 * 1024;
    internal const int MaximumNotesCharacters = 4000;
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, AllowDuplicateProperties = false };

    /// <summary>The asset name of the MSI for <paramref name="msiVersion"/> (<c>major.minor.patch</c>).</summary>
    internal static string MsiAssetName(string msiVersion) => "PCSteward-" + msiVersion + ".msi";

    /// <summary>
    /// The latest release: <c>tag_name</c> is <c>v</c> plus a <c>major.minor.patch</c> version; not a draft or pre-release;
    /// exactly two assets, the MSI for that version and the check zip, both <c>uploaded</c> with sizes in bounds. Release notes
    /// are kept only as bounded display text with control characters removed.
    /// </summary>
    internal static SelfUpdateRefusal ReadRelease(byte[] body, out PublishedRelease? release)
    {
        release = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body, Options);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "tag_name", out string tag) ||
                !root.TryGetProperty("draft", out JsonElement draft) || draft.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("prerelease", out JsonElement prerelease) || prerelease.ValueKind != JsonValueKind.False ||
                tag.Length is < 6 or > 20 || tag[0] != 'v' || !ReleaseVersion.TryParse(tag[1..], out ReleaseVersion? version) || version is null || !version.IsCoreOnly)
                return SelfUpdateRefusal.ReleaseUnreadable;

            DateTimeOffset? published = null;
            if (TryString(root, "published_at", out string publishedText) &&
                DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                published = parsed;
            string notes = root.TryGetProperty("body", out JsonElement bodyElement) && bodyElement.ValueKind == JsonValueKind.String ? Clean(bodyElement.GetString() ?? "") : "";

            if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() != 2)
                return SelfUpdateRefusal.UnexpectedAssets;
            string msiName = MsiAssetName(version.Text);
            long msiBytes = -1, checkBytes = -1;
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || !TryString(asset, "name", out string name) ||
                    !TryString(asset, "state", out string state) || !string.Equals(state, "uploaded", StringComparison.Ordinal) ||
                    !asset.TryGetProperty("size", out JsonElement sizeElement) || !sizeElement.TryGetInt64(out long size) || size <= 0)
                    return SelfUpdateRefusal.UnexpectedAssets;
                if (string.Equals(name, msiName, StringComparison.Ordinal) && msiBytes < 0) msiBytes = size;
                else if (string.Equals(name, ReleaseCheckPolicy.AssetName, StringComparison.Ordinal) && checkBytes < 0) checkBytes = size;
                else return SelfUpdateRefusal.UnexpectedAssets;
            }
            if (msiBytes < 0 || checkBytes < 0) return SelfUpdateRefusal.UnexpectedAssets;
            if (checkBytes > ReleaseCheckPolicy.MaximumZipBytes) return SelfUpdateRefusal.CheckFileUnreadable;
            if (msiBytes > MaximumMsiBytes) return SelfUpdateRefusal.MsiTooLarge;
            release = new(tag, version, published, msiBytes, checkBytes, notes);
            return SelfUpdateRefusal.None;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return SelfUpdateRefusal.ReleaseUnreadable;
        }
    }

    /// <summary>ADR-032 revision 2 §3 check 1, through the rules the elevated helper also applies (<see cref="ReleaseRunEvidence"/>).</summary>
    internal static SelfUpdateRefusal CheckRun(byte[] body, long runId, string commit) =>
        Map(ReleaseRunEvidence.CheckRun(body, runId, commit));

    /// <summary>ADR-032 revision 2 §3 check 2, through the rules the elevated helper also applies (<see cref="ReleaseRunEvidence"/>).</summary>
    internal static SelfUpdateRefusal CheckArtifact(byte[] body, string zipSha256) =>
        Map(ReleaseRunEvidence.CheckArtifact(body, zipSha256));

    private static SelfUpdateRefusal Map(ReleaseRunVerdict verdict) => verdict switch
    {
        ReleaseRunVerdict.Genuine => SelfUpdateRefusal.None,
        ReleaseRunVerdict.ArtifactGone => SelfUpdateRefusal.CheckArtifactGone,
        ReleaseRunVerdict.ArtifactMismatch => SelfUpdateRefusal.CheckArtifactMismatch,
        _ => SelfUpdateRefusal.RunNotGenuine,
    };

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        if (!element.TryGetProperty(property, out JsonElement found) || found.ValueKind != JsonValueKind.String) return false;
        value = found.GetString() ?? "";
        return value.Length > 0;
    }

    /// <summary>
    /// Release notes as plain display text: bounded, with control and format characters (including bidirectional overrides)
    /// other than line feeds removed.
    /// </summary>
    private static string Clean(string text)
    {
        string bounded = text.Length > MaximumNotesCharacters ? text[..MaximumNotesCharacters] : text;
        return new string([.. bounded.Where(character => character == '\n' ||
            (!char.IsControl(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format))]).Trim();
    }
}
