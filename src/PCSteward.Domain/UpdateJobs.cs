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

/// <summary>ADR-017 v0.3: the immutable job is a package operation of one kind. Uninstall is optional and not built.</summary>
public enum PackageOperationKind { Upgrade, Install }

/// <summary>Validated evidence selected for review. It contains data only, never process authority.</summary>
public sealed partial class UpdateJobSelection
{
    private const int MaximumStableKeyLength = 256;
    private const int MaximumVersionLength = 128;
    private const int MaximumEvidenceVersionLength = 128;
    private const string NoInstalledVersion = "-";

    /// <summary>An Upgrade of an installed product. Machine scope runs elevated; user scope does not.</summary>
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
        string mappingEvidenceVersion,
        string? agreementHash = null)
        : this(PackageOperationKind.Upgrade, stableProductKey, provider, source, packageId, installedVersion, candidateVersion, scope, architecture, channel, mappingEvidenceVersion, agreementHash, scope == UpdateScope.Machine)
    {
    }

    private UpdateJobSelection(
        PackageOperationKind operation,
        string stableProductKey,
        UpdateProvider provider,
        UpdateSource source,
        string packageId,
        string? installedVersion,
        string candidateVersion,
        UpdateScope scope,
        UpdateArchitecture architecture,
        UpdateChannel channel,
        string mappingEvidenceVersion,
        string? agreementHash,
        bool runsElevated)
    {
        Operation = ValidateEnum(operation, nameof(operation));
        StableProductKey = Validate(StableKey(), stableProductKey, MaximumStableKeyLength, nameof(stableProductKey));
        Provider = ValidateEnum(provider, nameof(provider));
        Source = ValidateEnum(source, nameof(source));
        if (!IsAllowedProviderSource(Provider, Source))
        {
            throw new ArgumentException("The provider and source are not an allowlisted pair.", nameof(source));
        }
        PackageId = Validate(PackageIdentifier(), packageId, 256, nameof(packageId));
        if (Provider == UpdateProvider.Chocolatey && PackageId.Contains('+', StringComparison.Ordinal))
        {
            throw new ArgumentException("The value is outside the allowlisted format.", nameof(packageId));
        }
        // FR-INS-01 / ADR-017: an Install has no installed version; an Upgrade always has one.
        InstalledVersion = Operation == PackageOperationKind.Install
            ? installedVersion is null ? null : throw new ArgumentException("An install operation has no installed version.", nameof(installedVersion))
            : Validate(Version(), installedVersion!, MaximumVersionLength, nameof(installedVersion));
        CandidateVersion = Validate(Version(), candidateVersion, MaximumVersionLength, nameof(candidateVersion));
        Scope = ValidateEnum(scope, nameof(scope));
        if (Provider == UpdateProvider.Chocolatey && Scope != UpdateScope.Machine)
        {
            throw new ArgumentException("Chocolatey updates require machine scope.", nameof(scope));
        }
        if (Operation == PackageOperationKind.Install && Source == UpdateSource.MicrosoftStore)
        {
            // Spike §2.5: msstore installs wait for a separate source-agreement review.
            throw new ArgumentException("Microsoft Store installs are not supported.", nameof(source));
        }
        Architecture = ValidateEnum(architecture, nameof(architecture));
        Channel = ValidateEnum(channel, nameof(channel));
        MappingEvidenceVersion = Validate(EvidenceVersion(), mappingEvidenceVersion, MaximumEvidenceVersionLength, nameof(mappingEvidenceVersion));
        if (agreementHash is not null && (Provider != UpdateProvider.WinGet || !AgreementHashPattern().IsMatch(agreementHash)))
        {
            throw new ArgumentException("The agreement hash is invalid for this provider.", nameof(agreementHash));
        }
        AgreementHash = agreementHash;
        if (Scope == UpdateScope.Machine && !runsElevated)
        {
            throw new ArgumentException("A machine-scope operation always runs elevated.", nameof(runsElevated));
        }
        RunsElevated = runsElevated;
    }

    /// <summary>
    /// FR-INS-01/02: an Install of a not-installed catalog entry after live exact resolution.
    /// <paramref name="installerElevates"/> routes a user-scope installer that needs or raises elevation
    /// (ElevationRequired or ElevatesSelf) to the elevated helper, so a batch keeps one UAC prompt (FR-BAT-05).
    /// </summary>
    public static UpdateJobSelection ForInstall(
        string catalogEntryKey,
        UpdateProvider provider,
        UpdateSource source,
        string packageId,
        string targetVersion,
        UpdateScope scope,
        UpdateArchitecture architecture,
        UpdateChannel channel,
        string mappingEvidenceVersion,
        string? agreementHash,
        bool installerElevates) =>
        new(PackageOperationKind.Install, catalogEntryKey, provider, source, packageId, null, targetVersion, scope, architecture, channel, mappingEvidenceVersion, agreementHash, scope == UpdateScope.Machine || installerElevates);

    /// <summary>Rebuilds persisted or transported evidence. Every field is revalidated; the digest check follows.</summary>
    public static UpdateJobSelection Restore(
        PackageOperationKind operation,
        string stableProductKey,
        UpdateProvider provider,
        UpdateSource source,
        string packageId,
        string? installedVersion,
        string candidateVersion,
        UpdateScope scope,
        UpdateArchitecture architecture,
        UpdateChannel channel,
        string mappingEvidenceVersion,
        string? agreementHash,
        bool runsElevated) =>
        new(operation, stableProductKey, provider, source, packageId, installedVersion, candidateVersion, scope, architecture, channel, mappingEvidenceVersion, agreementHash, runsElevated);

    public PackageOperationKind Operation { get; }
    public string StableProductKey { get; }
    public UpdateProvider Provider { get; }
    public UpdateSource Source { get; }
    public string PackageId { get; }
    /// <summary>Null only for <see cref="PackageOperationKind.Install"/>.</summary>
    public string? InstalledVersion { get; }
    public string CandidateVersion { get; }
    public UpdateScope Scope { get; }
    public UpdateArchitecture Architecture { get; }
    public UpdateChannel Channel { get; }
    public string MappingEvidenceVersion { get; }
    /// <summary>SHA-256 of the WinGet agreements the user was shown and accepted (FR-INS-03), or null when none apply.</summary>
    public string? AgreementHash { get; }
    /// <summary>True when the operation must run in the elevated helper session (FR-BAT-05).</summary>
    public bool RunsElevated { get; }

    /// <summary>
    /// A v0.2-shaped Upgrade: no agreement hash and the scope-derived route. Only these may use the v0.2
    /// single-job runners and helper protocol v1; every other operation needs the Task 49 batch path.
    /// </summary>
    public bool IsPlainUpgrade => Operation == PackageOperationKind.Upgrade && AgreementHash is null && RunsElevated == (Scope == UpdateScope.Machine);

    /// <summary>
    /// The first ten lines are the original v0.2 canonical text, so persisted Upgrade jobs keep their digest.
    /// Any v0.3 evidence (kind, agreement hash, a non-default route) appends fixed, labelled lines.
    /// </summary>
    internal string CanonicalText
    {
        get
        {
            string core = string.Join('\n',
                StableProductKey,
                Provider.ToString(),
                Source.ToString(),
                PackageId,
                InstalledVersion ?? NoInstalledVersion,
                CandidateVersion,
                Scope.ToString(),
                Architecture.ToString(),
                Channel.ToString(),
                MappingEvidenceVersion);
            return !IsPlainUpgrade
                ? string.Join('\n', core, "operation=" + Operation, "agreement=" + (AgreementHash ?? NoInstalledVersion), "elevated=" + (RunsElevated ? "true" : "false"))
                : core;
        }
    }

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

    private static bool IsAllowedProviderSource(UpdateProvider provider, UpdateSource source) =>
        (provider, source) is
            (UpdateProvider.WinGet, UpdateSource.WinGet) or
            (UpdateProvider.WinGet, UpdateSource.MicrosoftStore) or
            (UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9:._-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableKey();

    // WinGet IDs may contain + and _ (for example Notepad++.Notepad++); Chocolatey (NuGet) IDs never contain +.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+_-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdentifier();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceVersion();

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgreementHashPattern();
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
