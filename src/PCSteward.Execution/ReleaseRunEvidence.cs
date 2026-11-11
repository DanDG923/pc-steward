using System.Text.Json;

namespace PCSteward.Execution;

/// <summary>The outcome of checking GitHub's public answers about the CI run a release check record names.</summary>
internal enum ReleaseRunVerdict
{
    Genuine,
    RunNotGenuine,
    ArtifactGone,
    ArtifactMismatch,
}

/// <summary>
/// FR-UPD-04/15, TM-20, TM-23; ADR-032 revision 2 §3 checks 1 and 2, shared by the desktop's update check and the elevated
/// helper's own confirmation (§6 step 3, owner decision 2) so both apply exactly the same rules. Pure functions over the bytes
/// of two public REST answers: <c>GET /repos/DanDG923/pc-steward/actions/runs/{runId}</c> and
/// <c>GET /repos/DanDG923/pc-steward/actions/runs/{runId}/artifacts?name=pc-steward-release-check</c>. Everything in the
/// answers is untrusted input: only the named fields are read, duplicate properties are refused, and nothing is used as a
/// path, address, command or argument. No network, file, registry or process access happens here.
/// </summary>
internal static class ReleaseRunEvidence
{
    /// <summary>GitHub's numeric id of <see cref="ReleaseCheckPolicy.Repository"/>; a renamed or re-created repository has another.</summary>
    internal const long RepositoryId = 1331300752;
    internal const string WorkflowPath = ".github/workflows/release-payload.yml";
    internal static readonly IReadOnlyList<string> ReleaseBranches = ["phase/4-beta", "main"];

    /// <summary>The largest answer either caller reads before parsing.</summary>
    internal const int MaximumAnswerBytes = 512 * 1024;

    private static readonly JsonDocumentOptions Options = new()
    {
        MaxDepth = 16,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// The run answer: run <paramref name="runId"/> of this repository (also as head repository), workflow file
    /// <see cref="WorkflowPath"/>, event <c>workflow_dispatch</c>, a release branch, completed with success, at
    /// <paramref name="commit"/>.
    /// </summary>
    internal static ReleaseRunVerdict CheckRun(ReadOnlySpan<byte> body, long runId, string commit)
    {
        if (body.Length is 0 or > MaximumAnswerBytes || runId <= 0 || string.IsNullOrEmpty(commit)) return ReleaseRunVerdict.RunNotGenuine;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToArray(), Options);
            JsonElement run = document.RootElement;
            bool genuine = run.ValueKind == JsonValueKind.Object &&
                run.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long idValue) && idValue == runId &&
                Is(run, "path", WorkflowPath) && Is(run, "event", "workflow_dispatch") &&
                TryString(run, "head_branch", out string branch) && ReleaseBranches.Contains(branch, StringComparer.Ordinal) &&
                Is(run, "status", "completed") && Is(run, "conclusion", "success") && Is(run, "head_sha", commit) &&
                IsThisRepository(run, "repository") && IsThisRepository(run, "head_repository");
            return genuine ? ReleaseRunVerdict.Genuine : ReleaseRunVerdict.RunNotGenuine;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return ReleaseRunVerdict.RunNotGenuine;
        }
    }

    /// <summary>
    /// The artefacts answer: exactly one artefact named <see cref="ReleaseCheckPolicy.ArtifactName"/>, not expired, whose
    /// digest is <c>sha256:</c> plus the lowercase hex of <paramref name="zipSha256"/>.
    /// </summary>
    internal static ReleaseRunVerdict CheckArtifact(ReadOnlySpan<byte> body, string zipSha256)
    {
        if (body.Length is 0 or > MaximumAnswerBytes || !IsSha256Hex(zipSha256)) return ReleaseRunVerdict.ArtifactGone;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToArray(), Options);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("artifacts", out JsonElement artifacts) || artifacts.ValueKind != JsonValueKind.Array)
                return ReleaseRunVerdict.ArtifactGone;
            JsonElement[] named = [.. artifacts.EnumerateArray().Where(artifact => artifact.ValueKind == JsonValueKind.Object && Is(artifact, "name", ReleaseCheckPolicy.ArtifactName))];
            if (named.Length == 0) return ReleaseRunVerdict.ArtifactGone;
            if (named.Length != 1) return ReleaseRunVerdict.ArtifactMismatch;
            if (named[0].TryGetProperty("expired", out JsonElement expired) && expired.ValueKind != JsonValueKind.False) return ReleaseRunVerdict.ArtifactGone;
            if (!TryString(named[0], "digest", out string digest)) return ReleaseRunVerdict.ArtifactGone;
            return string.Equals(digest, "sha256:" + zipSha256.ToLowerInvariant(), StringComparison.Ordinal) ? ReleaseRunVerdict.Genuine : ReleaseRunVerdict.ArtifactMismatch;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return ReleaseRunVerdict.ArtifactGone;
        }
    }

    private static bool IsThisRepository(JsonElement run, string property) =>
        run.TryGetProperty(property, out JsonElement repository) && repository.ValueKind == JsonValueKind.Object &&
        repository.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long value) && value == RepositoryId &&
        Is(repository, "full_name", ReleaseCheckPolicy.Repository);

    private static bool Is(JsonElement element, string property, string expected) =>
        TryString(element, property, out string value) && string.Equals(value, expected, StringComparison.Ordinal);

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        if (!element.TryGetProperty(property, out JsonElement found) || found.ValueKind != JsonValueKind.String) return false;
        value = found.GetString() ?? "";
        return value.Length > 0;
    }

    private static bool IsSha256Hex(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
