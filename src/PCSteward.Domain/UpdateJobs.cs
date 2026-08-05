using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

public enum UpdateProvider { WinGet, Chocolatey }
public enum UpdateSource { WinGet, MicrosoftStore, ChocolateyCommunity }
public enum UpdateScope { User, Machine }
public enum UpdateArchitecture { X64, X86, Arm64, Neutral }
public enum UpdateChannel { Unknown, Stable, Preview }
public enum UpdateJobReviewState { AwaitingConfirmation }

/// <summary>Validated evidence selected for review. It contains data only, never process authority.</summary>
public sealed partial class UpdateJobSelection
{
    private const int MaximumStableKeyLength = 256;
    private const int MaximumVersionLength = 128;
    private const int MaximumEvidenceVersionLength = 128;

    public UpdateJobSelection(
        string stableProductKey,
        UpdateProvider provider,
        UpdateSource source,
        string packageId,
        string installedVersion,
        string candidateVersion,
        UpdateScope scope,
        UpdateArchitecture architecture,
        UpdateChannel channel,
        string mappingEvidenceVersion)
    {
        StableProductKey = Validate(StableKey(), stableProductKey, MaximumStableKeyLength, nameof(stableProductKey));
        Provider = ValidateEnum(provider, nameof(provider));
        Source = ValidateEnum(source, nameof(source));
        PackageId = Validate(PackageIdentifier(), packageId, 256, nameof(packageId));
        InstalledVersion = Validate(Version(), installedVersion, MaximumVersionLength, nameof(installedVersion));
        CandidateVersion = Validate(Version(), candidateVersion, MaximumVersionLength, nameof(candidateVersion));
        Scope = ValidateEnum(scope, nameof(scope));
        Architecture = ValidateEnum(architecture, nameof(architecture));
        Channel = ValidateEnum(channel, nameof(channel));
        MappingEvidenceVersion = Validate(EvidenceVersion(), mappingEvidenceVersion, MaximumEvidenceVersionLength, nameof(mappingEvidenceVersion));
    }

    public string StableProductKey { get; }
    public UpdateProvider Provider { get; }
    public UpdateSource Source { get; }
    public string PackageId { get; }
    public string InstalledVersion { get; }
    public string CandidateVersion { get; }
    public UpdateScope Scope { get; }
    public UpdateArchitecture Architecture { get; }
    public UpdateChannel Channel { get; }
    public string MappingEvidenceVersion { get; }

    internal string CanonicalText =>
        string.Join('\n',
            StableProductKey,
            Provider.ToString(),
            Source.ToString(),
            PackageId,
            InstalledVersion,
            CandidateVersion,
            Scope.ToString(),
            Architecture.ToString(),
            Channel.ToString(),
            MappingEvidenceVersion);

    public bool EqualsEvidence(UpdateJobSelection other) =>
        other is not null &&
        string.Equals(CanonicalText, other.CanonicalText, StringComparison.Ordinal);

    private static string Validate(Regex pattern, string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > maximumLength || !pattern.IsMatch(trimmed))
        {
            throw new ArgumentException("The value is outside the allowlisted format.", parameterName);
        }

        return trimmed;
    }

    private static T ValidateEnum<T>(T value, string parameterName) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9:._-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableKey();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdentifier();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceVersion();
}

/// <summary>A displayed selection that cannot be executed until explicit confirmation creates an approved job.</summary>
public sealed class UpdateJobReview
{
    public UpdateJobReview(Guid jobId, Guid correlationId, UpdateJobSelection selection)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(jobId));
        if (correlationId == Guid.Empty) throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
        JobId = jobId;
        CorrelationId = correlationId;
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        State = UpdateJobReviewState.AwaitingConfirmation;
    }

    public Guid JobId { get; }
    public Guid CorrelationId { get; }
    public UpdateJobSelection Selection { get; }
    public UpdateJobReviewState State { get; }

    public ImmutableUpdateJob Confirm(DateTimeOffset approvedAt)
    {
        if (approvedAt == default) throw new ArgumentException("An approval timestamp is required.", nameof(approvedAt));
        return ImmutableUpdateJob.Create(JobId, CorrelationId, Selection, approvedAt);
    }
}

/// <summary>Approved, immutable execution evidence. The digest detects persistence mutation but grants no authority.</summary>
public sealed class ImmutableUpdateJob
{
    private ImmutableUpdateJob(Guid id, Guid correlationId, UpdateJobSelection selection, DateTimeOffset approvedAt, string? expectedDigest)
    {
        if (id == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(id));
        if (correlationId == Guid.Empty) throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
        Id = id;
        CorrelationId = correlationId;
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        ApprovedAt = approvedAt == default ? throw new ArgumentException("An approval timestamp is required.", nameof(approvedAt)) : approvedAt;
        Digest = ComputeDigest();
        if (expectedDigest is not null && !DigestMatches(expectedDigest))
        {
            throw new InvalidOperationException("The persisted update job failed its integrity check.");
        }
    }

    public Guid Id { get; }
    public Guid CorrelationId { get; }
    public UpdateJobSelection Selection { get; }
    public DateTimeOffset ApprovedAt { get; }
    public string Digest { get; }
    public bool HasValidDigest => DigestMatches(ComputeDigest());

    internal static ImmutableUpdateJob Create(Guid id, Guid correlationId, UpdateJobSelection selection, DateTimeOffset approvedAt) =>
        new(id, correlationId, selection, approvedAt, null);

    public static ImmutableUpdateJob Restore(Guid id, Guid correlationId, UpdateJobSelection selection, DateTimeOffset approvedAt, string digest) =>
        new(id, correlationId, selection, approvedAt, digest);

    public bool Matches(UpdateJobExecutionEvidence evidence) =>
        evidence is not null &&
        evidence.IsActionable &&
        evidence.SourceIsFresh &&
        Selection.EqualsEvidence(evidence.Selection);

    private string ComputeDigest()
    {
        string canonical = string.Join('\n',
            Id.ToString("D", CultureInfo.InvariantCulture),
            CorrelationId.ToString("D", CultureInfo.InvariantCulture),
            Selection.CanonicalText,
            ApprovedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private bool DigestMatches(string candidate)
    {
        if (candidate.Length != Digest.Length) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Digest), Convert.FromHexString(candidate));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Fresh trusted-domain evidence used immediately before process execution.</summary>
public sealed record UpdateJobExecutionEvidence(UpdateJobSelection Selection, bool IsActionable, bool SourceIsFresh);
