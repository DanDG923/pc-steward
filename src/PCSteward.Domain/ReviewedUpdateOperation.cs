using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

public enum UpdateRouteContract { WinGetUserExactV2, ChocolateyMachineRegistrationV2 }

/// <summary>Canonical reviewed data; neither a digest nor a restored identity grants execution consent.</summary>
public sealed partial class ReviewedUpdateOperation
{
    public const int SchemaVersion = 2;
    public const int MaximumCanonicalBytes = 16 * 1024;
    private const string CanonicalSchema = "pc-steward-reviewed-update:2";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ReviewedUpdateOperation(ImmutableUpdateJob job, string requestingUserSid, string nativeInstanceId,
        string installationContextId, string evidenceId, DateTimeOffset evidenceObservedAt,
        UpdateRouteContract routeContract, ReviewedInstallerMode mode)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mode);
        if (!Enum.IsDefined(routeContract)) throw new ArgumentOutOfRangeException(nameof(routeContract));
        if (evidenceObservedAt == default || evidenceObservedAt > job.ApprovedAt)
            throw new ArgumentException("Review evidence needs an observation time no later than review.", nameof(evidenceObservedAt));
        JobId = job.Id;
        CorrelationId = job.CorrelationId;
        ReviewedAt = job.ApprovedAt.ToUniversalTime();
        Selection = job.Selection;
        RequestingUserSid = ValidateSid(requestingUserSid);
        NativeInstanceId = ValidateIdentity(nativeInstanceId, nameof(nativeInstanceId));
        InstallationContextId = ValidateIdentity(installationContextId, nameof(installationContextId));
        EvidenceId = ValidateIdentity(evidenceId, nameof(evidenceId));
        EvidenceObservedAt = evidenceObservedAt.ToUniversalTime();
        RouteContract = routeContract;
        Mode = mode;
        Digest = Convert.ToHexString(SHA256.HashData(GetCanonicalBytes()));
    }

    public Guid JobId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset ReviewedAt { get; }
    public UpdateJobSelection Selection { get; }
    public string RequestingUserSid { get; }
    public string NativeInstanceId { get; }
    public string InstallationContextId { get; }
    public string EvidenceId { get; }
    public DateTimeOffset EvidenceObservedAt { get; }
    public UpdateRouteContract RouteContract { get; }
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
            if (fields.Length != 24 || fields[0] != CanonicalSchema || fields[1] != "Update") return false;
            ReviewedInstallerMode mode;
            InstallerModeBinding binding = ParseEnum<InstallerModeBinding>(fields[21]);
            if (binding == InstallerModeBinding.Selected && fields[23] == "None")
                mode = ReviewedInstallerMode.Selected(ParseEnum<InstallerMode>(fields[22]));
            else if (binding == InstallerModeBinding.NotControllable && fields[22] == "None")
                mode = ReviewedInstallerMode.NotControllable(ParseEnum<UncontrolledInstallerModeReason>(fields[23]));
            else return false;

            UpdateJobSelection selection = new(fields[6], ParseEnum<UpdateProvider>(fields[7]), ParseEnum<UpdateSource>(fields[8]),
                fields[9], fields[10], fields[11], ParseEnum<UpdateScope>(fields[12]), ParseEnum<UpdateArchitecture>(fields[13]),
                ParseEnum<UpdateChannel>(fields[14]), fields[15]);
            // This temporary legacy-shaped object supplies validated data; it is never returned as approval.
            ImmutableUpdateJob data = new UpdateJobReview(ParseGuid(fields[2]), ParseGuid(fields[3]), selection).Confirm(ParseTime(fields[5]));
            ReviewedUpdateOperation restored = new(data, fields[4], fields[16], fields[17], fields[18], ParseTime(fields[19]),
                ParseEnum<UpdateRouteContract>(fields[20]), mode);
            if (!canonical.SequenceEqual(restored.GetCanonicalBytes())) return false;
            operation = restored;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException) { return false; }
    }

    /// <summary>Returns a fresh canonical payload. Caller mutation never changes this operation.</summary>
    public byte[] GetCanonicalBytes() => Encoding.UTF8.GetBytes(string.Join('\n',
        CanonicalSchema,
        "Update",
        JobId.ToString("D", CultureInfo.InvariantCulture),
        CorrelationId.ToString("D", CultureInfo.InvariantCulture),
        RequestingUserSid,
        ReviewedAt.ToString("O", CultureInfo.InvariantCulture),
        Selection.CanonicalText,
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
