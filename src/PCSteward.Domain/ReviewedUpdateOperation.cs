using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

public enum UpdateRouteContract
{
    WinGetUserExactV2, ChocolateyMachineRegistrationV2, WinGetUserExactV3, ChocolateyMachineRegistrationV3,
    ChocolateyMachineProvisionalV3, ChocolateyMachineRuntimeLimitsV3,
    /// <summary>
    /// ADR-031 (accepted, Option 2): update the Chocolatey client itself (package ID exactly <c>chocolatey</c>) to a release on
    /// PC Steward's compiled reviewed list, with its own guard contract. Admitted at the review gate since the Task 50
    /// activation (ADR-031 decision 7), and the frozen versions are data: only the provider's compiled list and native checks
    /// decide whether a job could run.
    /// </summary>
    ChocolateyClientSelfUpdateV1,
    /// <summary>
    /// Stage C4 (Task 54 R1, FR-UPD-15; owner decision 2026-11-12 "Silent for all per-user apps"): a desktop WinGet update of an app
    /// installed for the current user, from the <c>winget</c> or <c>msstore</c> source, with <see cref="InstallerMode.Silent"/> bound
    /// at review. It replaces the mode-less legacy user route; unlike <see cref="WinGetUserExactV3"/> it is not limited to MSIX
    /// installers or a known channel. The frozen data is not native qualification: fresh evidence, the review gate and the
    /// non-elevated runner still decide whether anything runs.
    /// </summary>
    WinGetUserSilentV1,
    /// <summary>
    /// Phase D stage D4b (design note PHASE_D4B_MACHINE_WINGET_UPDATE_DESIGN.md, owner answers 2026-11-12): a WinGet update of an app
    /// installed for every user of the PC, from the <c>winget</c> source only, run by the protocol-2 helper with
    /// <see cref="InstallerMode.Silent"/> bound at review, <c>--scope machine</c>, an observed x86, x64 or arm64 architecture and the
    /// installer type carried in the evidence identity (<see cref="ReviewedUpdateOperation.TryGetWinGetMachineEvidence"/>). Not admitted
    /// at the review gate until the stage that retires protocol 1. The frozen data is not native qualification: the helper checks the
    /// trusted client, source, settings, machine registration and manifest itself before it runs anything.
    /// </summary>
    WinGetMachineSilentV1
}

/// <summary>
/// D4b design §4: the installer types the machine WinGet route accepts. Portable, zip, MSIX and Store installers are never on it.
/// Wire and evidence text is the lowercase name WinGet uses (<c>--installer-type</c>).
/// </summary>
public enum WinGetMachineInstallerType { Msi, Wix, Exe, Inno, Nullsoft, Burn }

/// <summary>The two HKLM uninstall views a machine WinGet registration can live in (D4b design §5 H6).</summary>
public enum WinGetMachineRegistrationView { Machine64, Machine32 }

/// <summary>
/// D4b design §5 H6: the comparison digest of one HKLM uninstall registration, computed the same way by the desktop at review and
/// by the elevated helper before launch: SHA-256 over <c>wgm1-registration</c>, the view, the uninstall key name and its
/// <c>DisplayVersion</c>, one per line, UTF-8, uppercase hex. Data only; it proves nothing about the app by itself.
/// </summary>
public static class WinGetMachineRegistration
{
    /// <summary>Uninstall key names and versions longer than this are refused (bounded data).</summary>
    public const int MaximumFieldLength = 256;

    /// <summary>The digest, or null when a field is empty, too long or holds a line break or NUL.</summary>
    public static string? Digest(WinGetMachineRegistrationView view, string? keyName, string? displayVersion)
    {
        if (!Enum.IsDefined(view) || !IsField(keyName) || !IsField(displayVersion)) return null;
        string text = string.Join('\n', "wgm1-registration", view.ToString(), keyName, displayVersion);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool IsField(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumFieldLength && value.IndexOfAny(['\n', '\r', '\0']) < 0;
}

/// <summary>
/// D4b design §4–§7: the parsed evidence identity of a machine WinGet review. Format check only: every digest is comparison data and
/// never proves a registration, a manifest or consent. <see cref="RegistrationDigest"/> names the exact HKLM uninstall registration
/// observed at review, <see cref="ManifestDigest"/> the exported manifest and <see cref="InstallerSha256"/> its single installer.
/// </summary>
public sealed record WinGetMachineEvidence(WinGetMachineInstallerType InstallerType, string RegistrationDigest, string ManifestDigest,
    string InstallerSha256)
{
    /// <summary>The fixed prefix of the evidence identity.</summary>
    public const string Prefix = "wgm1";

    /// <summary>The lowercase installer-type text WinGet accepts for <c>--installer-type</c>.</summary>
    public static string InstallerTypeText(WinGetMachineInstallerType type) => type switch
    {
        WinGetMachineInstallerType.Msi => "msi",
        WinGetMachineInstallerType.Wix => "wix",
        WinGetMachineInstallerType.Exe => "exe",
        WinGetMachineInstallerType.Inno => "inno",
        WinGetMachineInstallerType.Nullsoft => "nullsoft",
        WinGetMachineInstallerType.Burn => "burn",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Parses the lowercase installer-type text; anything else (including portable, zip, msix, msstore) is refused.</summary>
    public static bool TryParseInstallerType(string? text, out WinGetMachineInstallerType type)
    {
        foreach (WinGetMachineInstallerType candidate in Enum.GetValues<WinGetMachineInstallerType>())
        {
            if (string.Equals(text, InstallerTypeText(candidate), StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }
        type = default;
        return false;
    }

    /// <summary>The canonical identity text: <c>wgm1:&lt;type&gt;:&lt;registration&gt;:&lt;manifest&gt;:&lt;installer sha-256&gt;</c>.</summary>
    public string ToIdentity()
    {
        if (!ReviewedUpdateOperation.IsUpperHex64(RegistrationDigest) || !ReviewedUpdateOperation.IsUpperHex64(ManifestDigest) ||
            !ReviewedUpdateOperation.IsUpperHex64(InstallerSha256))
            throw new ArgumentException("Machine WinGet evidence digests must be 64 uppercase hexadecimal digits.");
        return string.Join(':', Prefix, InstallerTypeText(InstallerType), RegistrationDigest, ManifestDigest, InstallerSha256);
    }
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
public enum ReviewedProviderGuardContract
{
    LegacyUnqualified, RequiredEffectiveControls, CommunityScriptRuntimeLimits,
    /// <summary>
    /// ADR-031: the client self-update guard. The trusted current client runs one compiled command built only from the reviewed
    /// list; success needs an exact manifest match of the target release. Never inherits the package-route meaning.
    /// </summary>
    ChocolateyClientSelfUpdate
}

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
        if (routeContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
            (encodingVersion != SchemaVersion || !HasClientSelfUpdateShape(job.Selection, mode, architecture)))
            throw new ArgumentException("A client self-update is limited to the current schema, the Chocolatey Community machine " +
                "registration of the package ID chocolatey, and provider-script-controlled mode and architecture.", nameof(routeContract));
        if (routeContract == UpdateRouteContract.WinGetMachineSilentV1 &&
            (encodingVersion != SchemaVersion || !HasWinGetMachineSilentShape(job.Selection, mode, architecture)))
            throw new ArgumentException("The machine WinGet route is limited to the current schema, a WinGet machine-scope update from the " +
                "winget source outside a preview channel, the Silent mode and an observed x86, x64 or arm64 architecture.", nameof(routeContract));
        if (routeContract == UpdateRouteContract.WinGetUserSilentV1 &&
            (encodingVersion != SchemaVersion || !HasWinGetUserSilentShape(job.Selection, mode, architecture)))
            throw new ArgumentException("The per-user WinGet route is limited to the current schema, a WinGet user-scope update from the " +
                "winget or msstore source, the Silent mode and an observed architecture.", nameof(routeContract));
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
        if (routeContract is UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 or UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
            !TryGetProfileEvidenceDigest(EvidenceId, out _))
            throw new ArgumentException("Runtime-limit review evidence must be the exact Chocolatey profile v2 identity.", nameof(evidenceId));
        if (routeContract == UpdateRouteContract.WinGetMachineSilentV1 && !TryGetWinGetMachineEvidence(EvidenceId, out _))
            throw new ArgumentException("Machine WinGet review evidence must be the exact wgm1 identity.", nameof(evidenceId));
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
            UpdateRouteContract.ChocolateyClientSelfUpdateV1 => ReviewedProviderGuardContract.ChocolateyClientSelfUpdate,
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
    /// Stage C4: true when this operation was frozen from exactly <paramref name="job"/>: the same job and correlation IDs, its
    /// approval time, its digest (which must still be valid) and the same selection. Data comparison only; it grants nothing.
    /// </summary>
    public bool BindsJob(ImmutableUpdateJob? job) =>
        job is not null && job.HasValidDigest && JobId == job.Id && CorrelationId == job.CorrelationId &&
        ReviewedAt == job.ApprovedAt.ToUniversalTime() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(LegacyJobDigest), Encoding.ASCII.GetBytes(job.Digest)) &&
        Selection.LegacyDigestData.EqualsEvidence(job.Selection);

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

    /// <summary>
    /// D4b design §4: format check only for <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> evidence, exactly
    /// <c>wgm1:&lt;msi|wix|exe|inno|nullsoft|burn&gt;:&lt;64 hex&gt;:&lt;64 hex&gt;:&lt;64 hex&gt;</c> (uppercase hex). Comparison data only.
    /// </summary>
    public static bool TryGetWinGetMachineEvidence(string? evidenceId, out WinGetMachineEvidence? evidence)
    {
        evidence = null;
        if (evidenceId is null || evidenceId.Length > 256) return false;
        string[] fields = evidenceId.Split(':');
        if (fields.Length != 5 || !string.Equals(fields[0], WinGetMachineEvidence.Prefix, StringComparison.Ordinal) ||
            !WinGetMachineEvidence.TryParseInstallerType(fields[1], out WinGetMachineInstallerType type) ||
            !IsUpperHex64(fields[2]) || !IsUpperHex64(fields[3]) || !IsUpperHex64(fields[4])) return false;
        evidence = new(type, fields[2], fields[3], fields[4]);
        return true;
    }

    /// <summary>Exactly 64 characters, each 0–9 or A–F.</summary>
    public static bool IsUpperHex64(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (char c in value)
            if (c is not ((>= '0' and <= '9') or (>= 'A' and <= 'F'))) return false;
        return true;
    }

    /// <summary>The ADR-031 client self-update package ID; compared ordinally (the native registration's own spelling).</summary>
    public const string ChocolateyClientPackageId = "chocolatey";

    /// <summary>
    /// ADR-031 "client target refuses" (observer step 7): the package identities of the Chocolatey client itself,
    /// <c>chocolatey</c> and <c>chocolatey.extension</c>, in any case. Package jobs never target them; the provider's
    /// <c>ChocolateyClientReleasePolicy.IsClientTarget</c> and the review gate's package-route admission share this rule.
    /// </summary>
    public static bool IsChocolateyClientIdentity(string? packageId) => packageId is not null &&
        (packageId.Equals(ChocolateyClientPackageId, StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals("chocolatey.extension", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ADR-031 shape only: Chocolatey Community machine registration of exactly <see cref="ChocolateyClientPackageId"/>,
    /// <c>NotControllable(ProviderScriptControlled)</c> and <c>ProviderScriptSelected</c>. Never list membership or eligibility.
    /// </summary>
    private static bool HasClientSelfUpdateShape(UpdateJobSelection selection, ReviewedInstallerMode mode, ReviewedUpdateArchitecture? architecture) =>
        selection.Provider == UpdateProvider.Chocolatey && selection.Source == UpdateSource.ChocolateyCommunity &&
        selection.Scope == UpdateScope.Machine && string.Equals(selection.PackageId, ChocolateyClientPackageId, StringComparison.Ordinal) &&
        mode == ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) &&
        architecture?.Binding == ReviewedArchitectureBinding.ProviderScriptSelected;

    /// <summary>
    /// Stage C4 shape only: WinGet, <c>winget</c> or <c>msstore</c>, User scope, <c>Selected(Silent)</c> and an observed architecture.
    /// </summary>
    private static bool HasWinGetUserSilentShape(UpdateJobSelection selection, ReviewedInstallerMode mode, ReviewedUpdateArchitecture? architecture) =>
        selection.Provider == UpdateProvider.WinGet && selection.Source is UpdateSource.WinGet or UpdateSource.MicrosoftStore &&
        selection.Scope == UpdateScope.User && mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) &&
        (architecture is null || architecture.Binding == ReviewedArchitectureBinding.Observed);

    /// <summary>
    /// D4b shape only: WinGet, <c>winget</c> source, Machine scope, a channel other than Preview (inventory reports Unknown for the
    /// default channel; the exact version pins the target), <c>Selected(Silent)</c> and an observed x86, x64 or
    /// arm64 architecture. The installer type and digests are checked through the evidence identity.
    /// </summary>
    private static bool HasWinGetMachineSilentShape(UpdateJobSelection selection, ReviewedInstallerMode mode, ReviewedUpdateArchitecture? architecture)
    {
        UpdateArchitecture value = architecture?.Value ?? selection.Architecture;
        return selection.Provider == UpdateProvider.WinGet && selection.Source == UpdateSource.WinGet &&
            selection.Scope == UpdateScope.Machine && selection.Channel != UpdateChannel.Preview &&
            mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) &&
            (architecture is null || architecture.Binding == ReviewedArchitectureBinding.Observed) &&
            value is UpdateArchitecture.X86 or UpdateArchitecture.X64 or UpdateArchitecture.Arm64;
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
