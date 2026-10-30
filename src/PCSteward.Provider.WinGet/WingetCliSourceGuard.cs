using System.Text;
using System.Text.Json;

namespace PCSteward.Provider.WinGet;

/// <summary>Passive query observations, never execution authority. Retain the root even when the source check refuses.</summary>
internal sealed record WingetCliSourceObservation(bool QueryRequested, bool IsExpected, Task? OwnedCompletion);

/// <summary>Checks effective CLI source data. Does not prove client identity or freeze externally mutable configuration.</summary>
internal sealed class WingetCliSourceGuard(IWingetProcess process)
{
    private const int MaximumExportBytes = 4096;
    private const string SourceIdentity = "Microsoft.Winget.Source_8wekyb3d8bbwe";
    private static readonly TimeSpan MaximumQueryTime = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IReadOnlyList<string> Arguments = Array.AsReadOnly<string>(["source", "export", "--name", "winget", "--disable-interactivity"]);
    private static readonly string[] Fields = ["Name", "Type", "Arg", "Data", "Identifier", "Explicit", "TrustLevel"];
    private readonly IWingetProcess process = process ?? throw new ArgumentNullException(nameof(process));

    internal async Task<WingetCliSourceObservation> ObserveAsync(TimeSpan remainingBudget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remainingBudget <= TimeSpan.Zero) return new(false, false, null);
        TimeSpan timeout = remainingBudget < MaximumQueryTime ? remainingBudget : MaximumQueryTime;
        WingetProcessResult result = await process.RunObservedQueryAsync(Arguments, timeout, cancellationToken).ConfigureAwait(false);
        // Do not await again or discard a pending/faulted native root. The caller must include it in aggregate settlement.
        bool expected = !cancellationToken.IsCancellationRequested && result.ExitCode == 0 &&
            !result.TimedOut && !result.Cancelled && !result.OutputLimitExceeded && !result.OutputIncomplete && !result.ExecutionUncertain &&
            result.OwnedCompletion?.IsCompletedSuccessfully == true && string.IsNullOrWhiteSpace(result.StandardError) && MatchesExport(result.StandardOutput);
        return new(true, expected, result.OwnedCompletion);
    }

    private static bool MatchesExport(string output)
    {
        if (string.IsNullOrEmpty(output) || output.Length > MaximumExportBytes) return false;
        try
        {
            if (StrictUtf8.GetByteCount(output) > MaximumExportBytes) return false;
            using JsonDocument document = JsonDocument.Parse(StrictUtf8.GetBytes(output), new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 3
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                if (!Fields.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value)) return false;
            if (fields.Count != Fields.Length || !EqualsString(fields["Name"], "winget") ||
                !EqualsString(fields["Type"], "Microsoft.PreIndexed.Package") ||
                !EqualsString(fields["Arg"], "https://cdn.winget.microsoft.com/cache") ||
                !EqualsString(fields["Data"], SourceIdentity) || !EqualsString(fields["Identifier"], SourceIdentity) ||
                fields["Explicit"].ValueKind != JsonValueKind.False || fields["TrustLevel"].ValueKind != JsonValueKind.Array ||
                fields["TrustLevel"].GetArrayLength() != 2) return false;
            HashSet<string> trust = new(StringComparer.Ordinal);
            foreach (JsonElement level in fields["TrustLevel"].EnumerateArray())
                if (level.ValueKind != JsonValueKind.String || !trust.Add(level.GetString()!)) return false;
            return trust.Contains("Trusted") && trust.Contains("StoreOrigin");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) { return false; }
    }

    private static bool EqualsString(JsonElement value, string expected) => value.ValueKind == JsonValueKind.String && value.GetString() == expected;
}
