using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Execution;

internal static partial class ElevatedHelperProtocol
{
    internal const string Version = "1";
    internal const int MaximumMessageBytes = 16 * 1024;
    internal static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);
    private const string Operation = "UpdatePackage";

    private static readonly string[] RequestProperties =
    [
        "protocolVersion",
        "jobId",
        "correlationId",
        "operation",
        "provider",
        "source",
        "stableProductKey",
        "packageId",
        "installedVersion",
        "targetVersion",
        "scope",
        "architecture",
        "channel",
        "mappingEvidenceVersion",
        "approvedAtUtc",
        "digest",
        "nonce",
        "issuedAtUtc"
    ];

    private static readonly string[] ResponseProperties =
    [
        "protocolVersion",
        "jobId",
        "nonce",
        "outcome",
        "exitCode",
        "sanitizedOutput"
    ];

    internal static string CreateNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static byte[] CreateRequest(ImmutableUpdateJob job, string nonce, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidateNonce(nonce, nameof(nonce));
        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["protocolVersion"] = Version,
            ["jobId"] = job.Id.ToString("D", CultureInfo.InvariantCulture),
            ["correlationId"] = job.CorrelationId.ToString("D", CultureInfo.InvariantCulture),
            ["operation"] = Operation,
            ["provider"] = job.Selection.Provider.ToString(),
            ["source"] = job.Selection.Source.ToString(),
            ["stableProductKey"] = job.Selection.StableProductKey,
            ["packageId"] = job.Selection.PackageId,
            ["installedVersion"] = job.Selection.InstalledVersion,
            ["targetVersion"] = job.Selection.CandidateVersion,
            ["scope"] = job.Selection.Scope.ToString(),
            ["architecture"] = job.Selection.Architecture.ToString(),
            ["channel"] = job.Selection.Channel.ToString(),
            ["mappingEvidenceVersion"] = job.Selection.MappingEvidenceVersion,
            ["approvedAtUtc"] = job.ApprovedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["digest"] = job.Digest,
            ["nonce"] = nonce,
            ["issuedAtUtc"] = issuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        };
        return Serialize(fields, RequestProperties);
    }

    internal static bool TryReadResponse(
        ReadOnlySpan<byte> utf8,
        Guid expectedJobId,
        string expectedNonce,
        out ElevatedHelperResult result,
        out string rejection)
    {
        result = new ElevatedHelperResult(ElevatedHelperOutcome.Failed, null, "invalid-helper-response");
        if (!TryReadStrictObject(utf8, ResponseProperties, out Dictionary<string, JsonElement> fields, out rejection))
        {
            return false;
        }

        if (!TryString(fields, "protocolVersion", out string? protocol) ||
            protocol != Version ||
            !TryString(fields, "jobId", out string? jobText) ||
            !Guid.TryParseExact(jobText, "D", out Guid jobId) ||
            jobId != expectedJobId ||
            !TryString(fields, "nonce", out string? nonce) ||
            !FixedEquals(nonce, expectedNonce) ||
            !TryString(fields, "outcome", out string? outcomeText) ||
            !Enum.TryParse(outcomeText, ignoreCase: false, out ElevatedHelperOutcome outcome) ||
            !Enum.IsDefined(outcome) ||
            !TryString(fields, "sanitizedOutput", out string? output) ||
            output.Length > 4096 ||
            output.Any(char.IsControl))
        {
            rejection = "response-field-validation-failed";
            return false;
        }

        JsonElement exitElement = fields["exitCode"];
        int? exitCode = exitElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when exitElement.TryGetInt32(out int value) => value,
            _ => int.MinValue
        };
        if (exitCode == int.MinValue)
        {
            rejection = "response-exit-code-invalid";
            return false;
        }

        result = new ElevatedHelperResult(outcome, exitCode, NonElevatedUpdateRunner.Sanitize(output));
        rejection = string.Empty;
        return true;
    }

    internal static byte[] CreateResponse(
        Guid jobId,
        string nonce,
        ElevatedHelperOutcome outcome,
        int? exitCode,
        string diagnostic)
    {
        ValidateNonce(nonce, nameof(nonce));
        if (jobId == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(jobId));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["protocolVersion"] = Version,
            ["jobId"] = jobId.ToString("D", CultureInfo.InvariantCulture),
            ["nonce"] = nonce,
            ["outcome"] = outcome.ToString(),
            ["exitCode"] = exitCode,
            ["sanitizedOutput"] = NonElevatedUpdateRunner.Sanitize(diagnostic)
        };
        return Serialize(fields, ResponseProperties);
    }

    internal static bool TryReadRequest(
        ReadOnlySpan<byte> utf8,
        string expectedNonce,
        DateTimeOffset nowUtc,
        out ImmutableUpdateJob? job,
        out string rejection)
    {
        job = null;
        if (!TryReadStrictObject(utf8, RequestProperties, out Dictionary<string, JsonElement> fields, out rejection))
        {
            return false;
        }

        if (!RequestProperties.All(property => TryString(fields, property, out _)))
        {
            rejection = "request-field-type-invalid";
            return false;
        }

        string protocol = fields["protocolVersion"].GetString()!;
        string operation = fields["operation"].GetString()!;
        string providerText = fields["provider"].GetString()!;
        string sourceText = fields["source"].GetString()!;
        string scopeText = fields["scope"].GetString()!;
        string nonce = fields["nonce"].GetString()!;
        if (protocol != Version ||
            operation != Operation ||
            !Enum.TryParse(providerText, ignoreCase: false, out UpdateProvider provider) ||
            !Enum.IsDefined(provider) ||
            scopeText != nameof(UpdateScope.Machine) ||
            !Enum.TryParse(sourceText, ignoreCase: false, out UpdateSource source) ||
            !Enum.IsDefined(source) ||
            !IsAllowedProviderSource(provider, source) ||
            !FixedEquals(nonce, expectedNonce))
        {
            rejection = "request-authority-not-allowlisted";
            return false;
        }

        if (!Guid.TryParseExact(fields["jobId"].GetString(), "D", out Guid jobId) ||
            jobId == Guid.Empty ||
            !Guid.TryParseExact(fields["correlationId"].GetString(), "D", out Guid correlationId) ||
            correlationId == Guid.Empty ||
            !TryTimestamp(fields["approvedAtUtc"].GetString(), out DateTimeOffset approvedAt) ||
            !TryTimestamp(fields["issuedAtUtc"].GetString(), out DateTimeOffset issuedAt))
        {
            rejection = "request-identity-or-time-invalid";
            return false;
        }

        TimeSpan age = nowUtc.ToUniversalTime() - issuedAt.ToUniversalTime();
        if (age < TimeSpan.FromSeconds(-15) || age > RequestLifetime)
        {
            rejection = "request-expired";
            return false;
        }

        try
        {
            UpdateJobSelection selection = new(
                fields["stableProductKey"].GetString()!,
                provider,
                source,
                fields["packageId"].GetString()!,
                fields["installedVersion"].GetString()!,
                fields["targetVersion"].GetString()!,
                UpdateScope.Machine,
                ParseEnum<UpdateArchitecture>(fields["architecture"].GetString()!),
                ParseEnum<UpdateChannel>(fields["channel"].GetString()!),
                fields["mappingEvidenceVersion"].GetString()!);
            job = ImmutableUpdateJob.Restore(
                jobId,
                correlationId,
                selection,
                approvedAt,
                fields["digest"].GetString()!);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            rejection = "request-job-integrity-invalid";
            return false;
        }

        rejection = string.Empty;
        return true;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value));

    private static bool TryReadStrictObject(
        ReadOnlySpan<byte> utf8,
        IReadOnlyCollection<string> expectedProperties,
        out Dictionary<string, JsonElement> fields,
        out string rejection)
    {
        fields = [];
        if (utf8.IsEmpty || utf8.Length > MaximumMessageBytes)
        {
            rejection = "message-size-invalid";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                utf8.ToArray(),
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 3 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                rejection = "message-root-invalid";
                return false;
            }

            HashSet<string> expected = new(expectedProperties, StringComparer.Ordinal);
            Dictionary<string, JsonElement> parsed = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!expected.Contains(property.Name) || !parsed.TryAdd(property.Name, property.Value.Clone()))
                {
                    rejection = "message-property-unknown-or-duplicate";
                    return false;
                }
            }

            if (parsed.Count != expected.Count)
            {
                rejection = "message-property-missing";
                return false;
            }

            fields = parsed;
            rejection = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            rejection = "message-json-invalid";
            return false;
        }
    }

    private static bool IsAllowedProviderSource(UpdateProvider provider, UpdateSource source) =>
        (provider, source) is
            (UpdateProvider.WinGet, UpdateSource.WinGet) or
            (UpdateProvider.WinGet, UpdateSource.MicrosoftStore) or
            (UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity);

    private static bool TryString(
        Dictionary<string, JsonElement> fields,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!fields.TryGetValue(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static byte[] Serialize(
        Dictionary<string, object?> fields,
        IEnumerable<string> propertyOrder)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (string property in propertyOrder)
            {
                writer.WritePropertyName(property);
                object? value = fields[property];
                if (value is null) writer.WriteNullValue();
                else if (value is int integer) writer.WriteNumberValue(integer);
                else writer.WriteStringValue((string)value);
            }
            writer.WriteEndObject();
        }

        if (buffer.Length > MaximumMessageBytes)
        {
            throw new InvalidOperationException("The helper protocol message exceeded its fixed bound.");
        }

        return buffer.ToArray();
    }

    private static bool TryTimestamp(string? value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);

    private static void ValidateNonce(string nonce, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce, parameterName);
        if (!Nonce().IsMatch(nonce)) throw new ArgumentException("The nonce is invalid.", parameterName);
    }

    private static bool FixedEquals(string first, string second)
    {
        byte[] left = Encoding.UTF8.GetBytes(first);
        byte[] right = Encoding.UTF8.GetBytes(second);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    [GeneratedRegex("^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Nonce();
}

internal sealed class OneShotHelperRequestValidator(string expectedNonce, Func<DateTimeOffset>? utcNow = null)
{
    private readonly string expectedNonce = expectedNonce;
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private int consumed;

    internal bool TryAccept(ReadOnlySpan<byte> request, out ImmutableUpdateJob? job, out string rejection)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        {
            job = null;
            rejection = "duplicate-request";
            return false;
        }

        return ElevatedHelperProtocol.TryReadRequest(request, expectedNonce, utcNow(), out job, out rejection);
    }
}
