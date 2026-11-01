using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

public enum UpdateRouteContract
{
    WinGetUserExactV2, ChocolateyMachineRegistrationV2, WinGetUserExactV3, ChocolateyMachineRegistrationV3,
    ChocolateyMachineProvisionalV3, ChocolateyMachineRuntimeLimitsV3
}

/// <summary>Frozen data semantics only. No member asserts that native candidate qualification succeeded.</summary>
public enum ReviewedCandidateProvenance { UnqualifiedLegacyLabel, TrustedDesktopEvidenceRequired, UserCacheIntent }

/// <summary>
/// Closed provider-guard meaning derived only from the frozen route and schema; never serialized separately.
/// <see cref="RequiredEffectiveControls"/> keeps the earlier provisional route's stronger runtime-control duty.
/// <see cref="CommunityScriptRuntimeLimits"/> is the owner-accepted, disclosed Chocolatey script-runtime exception,
/// conditional on the fixed official Community feed. No member asserts native eligibility, qualification or consent.
/// Append new values; never reinterpret an existing one.
/// </summary>
public enum ReviewedProviderGuardContract { LegacyUnqualified, RequiredEffectiveControls, CommunityScriptRuntimeLimits }

/// <summary>Canonical reviewed data; neither a digest nor a restored identity grants execution consent.</summary>
public sealed partial class ReviewedUpdateOperation
{
    public const int SchemaVersion = 3;
    public const int MaximumCanonicalBytes = 4096;
    /// <summary>O2 (adapter gates §A.6): runtime-limit route evidence is exactly this prefix plus 64 uppercase hex (81 characters).</summary>
    public const string ProfileEvidencePrefix = "choco-profile-v2:";
    private const int ProfileDigestLength = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ReviewedUpdateOperation(ImmutableUpdateJob job, string requestingUserSid, string nativeInstanceId,
        string installationContextId, string evidenceId, DateTimeOffset evidenceObservedAt,
        UpdateRouteContract routeContract, ReviewedInstallerMode mode, ReviewedUpdateArchitecture? architecture = null)
        : this(job, requestingUserSid, nativeInstanceId, installationContextId, evidenceId, evidenceObservedAt,
            routeContract, mode, architecture, SchemaVersion) { }

    private ReviewedUpdateOperation(ImmutableUpdateJob job, string requestingUserSid, string nativeInstanceId,
        string installationContextId, string evidenceId, DateTimeOffset evidenceObservedAt,
        UpdateRouteContract routeContract, ReviewedInstallerMode mode, ReviewedUpdateArchitecture? architecture, int encodingVersion)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mode);
        if (!Enum.IsDefined(routeContract)) throw new ArgumentOutOfRangeException(nameof(routeContract));
        bool userCacheIntentRoute = IsUserCacheIntentRoute(routeContract);
        if (encodingVersion != SchemaVersion && userCacheIntentRoute)
            throw new ArgumentException("Provisional candidate intent requires the current canonical schema.", nameof(routeContract));
        if (userCacheIntentRoute &&
            (job.Selection.Provider != UpdateProvider.Chocolatey || job.Selection.Source != UpdateSource.ChocolateyCommunity ||
                job.Selection.Scope != UpdateScope.Machine))
            throw new ArgumentException("Provisional candidate intent is limited to Chocolatey Community machine Update.", nameof(routeContract));
        if (evidenceObservedAt == default || evidenceObservedAt > job.ApprovedAt)
            throw new ArgumentException("Review evidence needs an observation time no later than review.", nameof(evidenceObservedAt));
        JobId = job.Id;
        CorrelationId = job.CorrelationId;
        ReviewedAt = job.ApprovedAt.ToUniversalTime();
        Selection = new(job.Selection, architecture ?? ReviewedUpdateArchitecture.Observed(job.Selection.Architecture));
        EncodingVersion = encodingVersion;
        LegacyJobDigest = job.Digest;
        RequestingUserSid = ValidateSid(requestingUserSid);
        NativeInstanceId = ValidateIdentity(nativeInstanceId, nameof(nativeInstanceId));
        InstallationContextId = ValidateIdentity(installationContextId, nameof(installationContextId));
        EvidenceId = ValidateIdentity(evidenceId, nameof(evidenceId));
        // Shared by construction and TryRestore, so a non-conforming stored row restores as false (adapter gates §A.6/§A.6.2).
        if (routeContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 && !TryGetProfileEvidenceDigest(EvidenceId, out _))
            throw new ArgumentException("Runtime-limit review evidence must be the exact Chocolatey profile v2 identity.", nameof(evidenceId));
        EvidenceObservedAt = evidenceObservedAt.ToUniversalTime();
        RouteContract = routeContract;
        Mode = mode;
        byte[] canonical = GetCanonicalBytes();
        if (canonical.Length > MaximumCanonicalBytes) throw new ArgumentException("Reviewed operation exceeds its canonical payload bound.");
        Digest = Convert.ToHexString(SHA256.HashData(canonical));
    }

    public Guid JobId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset ReviewedAt { get; }
    /// <summary>
    /// Frozen data, not native proof. For EncodingVersion 2, Architecture contains only the historical,
    /// unqualified label: its Observed binding has no observation meaning. Check HasCurrentSchema before
    /// using the architecture contract; even current-schema data requires independent native evidence.
    /// </summary>
    public ReviewedUpdateSelection Selection { get; }
    public int EncodingVersion { get; }
    public bool HasCurrentSchema => EncodingVersion == SchemaVersion;
    /// <summary>Legacy comparison hash only; it never changes typed architecture semantics or grants approval.</summary>
    public string LegacyJobDigest { get; }
    public string RequestingUserSid { get; }
    public string NativeInstanceId { get; }
    public string InstallationContextId { get; }
    public string EvidenceId { get; }
    public DateTimeOffset EvidenceObservedAt { get; }
    public UpdateRouteContract RouteContract { get; }
    /// <summary>
    /// Describes what the frozen candidate means, not native proof or execution consent. Provisional intent
    /// needs independent helper qualification; neither restoration nor a fresh-evidence Boolean supplies it.
    /// </summary>
    public ReviewedCandidateProvenance CandidateProvenance => !HasCurrentSchema ||
        RouteContract is UpdateRouteContract.WinGetUserExactV2 or UpdateRouteContract.ChocolateyMachineRegistrationV2
        ? ReviewedCandidateProvenance.UnqualifiedLegacyLabel
        : IsUserCacheIntentRoute(RouteContract)
            ? ReviewedCandidateProvenance.UserCacheIntent : ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired;
    /// <summary>
    /// Frozen guard meaning, derived from schema and route only. Legacy, schema-2 and non-provisional routes are
    /// <see cref="ReviewedProviderGuardContract.LegacyUnqualified"/>; a label never grants eligibility or approval.
    /// </summary>
    public ReviewedProviderGuardContract ProviderGuardContract => !HasCurrentSchema
        ? ReviewedProviderGuardContract.LegacyUnqualified
        : RouteContract switch
        {
            UpdateRouteContract.ChocolateyMachineProvisionalV3 => ReviewedProviderGuardContract.RequiredEffectiveControls,
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 => ReviewedProviderGuardContract.CommunityScriptRuntimeLimits,
            _ => ReviewedProviderGuardContract.LegacyUnqualified,
        };
    public ReviewedInstallerMode Mode { get; }
    public string Digest { get; }

    /// <summary>Restores passive data only; never reconstructs a live approval or dispatch permit.</summary>
    public static bool TryRestore(ReadOnlySpan<byte> canonical, string expectedDigest, out ReviewedUpdateOperation? operation)
    {
        operation = null;
        if (canonical.Length is 0 or > MaximumCanonicalBytes || expectedDigest is null || expectedDigest.Length != 64) return false;
        try
        {
            byte[] expectedHash = Convert.FromHexString(expectedDigest);
            if (Convert.ToHexString(expectedHash) != expectedDigest ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(canonical), expectedHash)) return false;
            string[] fields = StrictUtf8.GetString(canonical).Split('\n');
            int version = fields.Length == 24 && fields[0] == "pc-steward-reviewed-update:2" ? 2 :
                fields.Length == 25 && fields[0] == "pc-steward-reviewed-update:3" ? 3 : 0;
            if (version == 0 || fields[1] != "Update") return false;
            int shift = version == 3 ? 1 : 0;
            ReviewedInstallerMode mode;
            InstallerModeBinding binding = ParseEnum<InstallerModeBinding>(fields[21 + shift]);
            if (binding == InstallerModeBinding.Selected && fields[23 + shift] == "None")
                mode = ReviewedInstallerMode.Selected(ParseEnum<InstallerMode>(fields[22 + shift]));
            else if (binding == InstallerModeBinding.NotControllable && fields[22 + shift] == "None")
                mode = ReviewedInstallerMode.NotControllable(ParseEnum<UncontrolledInstallerModeReason>(fields[23 + shift]));
            else return false;

            ReviewedUpdateArchitecture architecture;
            if (version == 2) architecture = ReviewedUpdateArchitecture.Observed(ParseEnum<UpdateArchitecture>(fields[13]));
            else if (fields[13] == nameof(ReviewedArchitectureBinding.Observed))
                architecture = ReviewedUpdateArchitecture.Observed(ParseEnum<UpdateArchitecture>(fields[14]));
            else if (fields[13] == nameof(ReviewedArchitectureBinding.ProviderScriptSelected) && fields[14] == "None")
                architecture = ReviewedUpdateArchitecture.ProviderScriptSelected();
            else return false;
            UpdateJobSelection selection = new(fields[6], ParseEnum<UpdateProvider>(fields[7]), ParseEnum<UpdateSource>(fields[8]),
                fields[9], fields[10], fields[11], ParseEnum<UpdateScope>(fields[12]), architecture.Value ?? UpdateArchitecture.Neutral,
                ParseEnum<UpdateChannel>(fields[14 + shift]), fields[15 + shift]);
            // This temporary legacy-shaped object supplies validated data; it is never returned as approval.
            ImmutableUpdateJob data = new UpdateJobReview(ParseGuid(fields[2]), ParseGuid(fields[3]), selection).Confirm(ParseTime(fields[5]));
            ReviewedUpdateOperation restored = new(data, fields[4], fields[16 + shift], fields[17 + shift], fields[18 + shift], ParseTime(fields[19 + shift]),
                ParseEnum<UpdateRouteContract>(fields[20 + shift]), mode, architecture, version);
            if (!canonical.SequenceEqual(restored.GetCanonicalBytes())) return false;
            operation = restored;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException) { return false; }
    }

    /// <summary>Returns a fresh canonical payload. Caller mutation never changes this operation.</summary>
    public byte[] GetCanonicalBytes() => Encoding.UTF8.GetBytes(string.Join('\n',
        "pc-steward-reviewed-update:" + EncodingVersion.ToString(CultureInfo.InvariantCulture),
        "Update",
        JobId.ToString("D", CultureInfo.InvariantCulture),
        CorrelationId.ToString("D", CultureInfo.InvariantCulture),
        RequestingUserSid,
        ReviewedAt.ToString("O", CultureInfo.InvariantCulture),
        EncodingVersion == 2 ? Selection.LegacyDigestData.CanonicalText : Selection.CanonicalText,
        NativeInstanceId,
        InstallationContextId,
        EvidenceId,
        EvidenceObservedAt.ToString("O", CultureInfo.InvariantCulture),
        RouteContract.ToString(),
        Mode.Binding.ToString(),
        Mode.Mode?.ToString() ?? "None",
        Mode.Reason?.ToString() ?? "None"));

    /// <summary>Complete data comparison only. The caller still needs live consent and a one-use permit.</summary>
    public bool HasSameIdentity(ReviewedUpdateOperation? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(GetCanonicalBytes(), other.GetCanonicalBytes());

    /// <summary>
    /// Format check only (adapter gates §A.6): <c>choco-profile-v2:</c> followed by exactly 64 uppercase hex digits. The digest
    /// is comparison data; it never proves a native profile, qualification or consent.
    /// </summary>
    public static bool TryGetProfileEvidenceDigest(string? evidenceId, out string digest)
    {
        digest = string.Empty;
        if (evidenceId is null || evidenceId.Length != ProfileEvidencePrefix.Length + ProfileDigestLength ||
            !evidenceId.StartsWith(ProfileEvidencePrefix, StringComparison.Ordinal)) return false;
        string candidate = evidenceId[ProfileEvidencePrefix.Length..];
        foreach (char c in candidate)
            if (c is not ((>= '0' and <= '9') or (>= 'A' and <= 'F'))) return false;
        digest = candidate;
        return true;
    }

    private static bool IsUserCacheIntentRoute(UpdateRouteContract route) =>
        route is UpdateRouteContract.ChocolateyMachineProvisionalV3 or UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("Canonical enum is unknown.", nameof(value));

    private static Guid ParseGuid(string value) => Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty
        ? parsed : throw new ArgumentException("Canonical job identity is invalid.", nameof(value));

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed : throw new ArgumentException("Canonical timestamp is invalid.", nameof(value));

    private static string ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || !OpaqueIdentity().IsMatch(value))
            throw new ArgumentException("Native evidence identity is outside the bounded data format.", parameterName);
        return value;
    }

    private static string ValidateSid(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        if (value.Length > 184) throw new ArgumentException("Requesting user SID is too long.", nameof(value));
        string[] fields = value.Split('-');
        if (fields.Length is < 4 or > 18 || fields[0] != "S" || fields[1] != "1" ||
            !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong authority) ||
            authority > 0xFFFFFFFFFFFF || authority.ToString(CultureInfo.InvariantCulture) != fields[2])
            throw new ArgumentException("Requesting user SID is not canonical.", nameof(value));
        foreach (string subAuthority in fields.AsSpan(3))
        {
            if (!uint.TryParse(subAuthority, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed) ||
                parsed.ToString(CultureInfo.InvariantCulture) != subAuthority)
                throw new ArgumentException("Requesting user SID is not canonical.", nameof(value));
        }
        return value;
    }

    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9:._{}-]{0,255}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueIdentity();
}
