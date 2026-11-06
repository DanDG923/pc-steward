namespace PCSteward.Provider.Chocolatey.Native;

/// <summary><c>TOKEN_ELEVATION_TYPE</c>. Any value Windows did not return as 1–3 is <see cref="Unknown"/> and refuses.</summary>
internal enum TokenElevationKind
{
    Unknown = 0,
    Default = 1,
    Full = 2,
    Limited = 3
}

/// <summary>How BUILTIN\Administrators (S-1-5-32-544) appears in a token's groups.</summary>
internal enum AdministratorsMembership
{
    Absent,
    /// <summary>Present with <c>SE_GROUP_USE_FOR_DENY_ONLY</c>, as in a UAC filtered token.</summary>
    DenyOnly,
    /// <summary>Present and usable for allow ACEs (any state other than deny-only).</summary>
    Enabled
}

/// <summary>
/// Facts read from a real token by the native factories (Task 50 design rev 6 §2.1, M5). The policy decides only from
/// these values, so every rule is portable and unit-testable. <see cref="Privileges"/> lists every privilege present,
/// enabled or not, by its canonical name.
/// </summary>
internal sealed record TokenFacts(string UserSid, ulong AuthenticationId, TokenElevationKind ElevationType, uint IntegrityRid,
    bool Restricted, bool AppContainer, AdministratorsMembership Administrators, IReadOnlyList<string> Privileges);

/// <summary>
/// Closed verdict of the requester-token rules (§2.1, M5): <see cref="Accepted"/> or exactly one specific refusal. The
/// pure policy returns the values up to <see cref="EvaluationPrivilegeNotAllowed"/>; the native factories add the values
/// after it for facts they could not obtain. Every value other than <see cref="Accepted"/> refuses the request
/// (<c>RequestNotAdmitted</c>).
/// </summary>
internal enum TokenVerdict
{
    Accepted,
    /// <summary>A fact or the expected SID is missing or malformed.</summary>
    FactsMalformed,
    /// <summary>The peer desktop token is not a UAC filtered token (elevation type Default, Full or unknown).</summary>
    PeerNotLimited,
    /// <summary>The peer token's user SID differs from the expected SID.</summary>
    PeerSidMismatch,
    /// <summary>The evaluation token's user SID differs from the expected SID.</summary>
    EvaluationSidMismatch,
    /// <summary>The peer and evaluation tokens belong to different logon sessions.</summary>
    AuthenticationIdMismatch,
    /// <summary>The evaluation token is not the filtered (Limited) token of the split pair.</summary>
    EvaluationNotLimited,
    /// <summary>The evaluation token's integrity level is not exactly Medium (0x2000).</summary>
    EvaluationNotMediumIntegrity,
    EvaluationRestricted,
    EvaluationAppContainer,
    /// <summary>BUILTIN\Administrators is usable for allow ACEs in the evaluation token.</summary>
    EvaluationAdministratorsEnabled,
    /// <summary>The evaluation token holds a privilege outside the allow-list (L4).</summary>
    EvaluationPrivilegeNotAllowed,
    /// <summary>The held peer handle's process ID differs from the pipe's process ID.</summary>
    PeerProcessIdMismatch,
    /// <summary>The peer process exited before the evaluation token was obtained and checked.</summary>
    PeerExited,
    PeerTokenUnreadable,
    /// <summary>The current process token has no linked token (UAC off, or not a split-token administrator).</summary>
    NoLinkedToken,
    EvaluationTokenUnreadable,
    /// <summary>The evaluation token is not an identification-level impersonation token.</summary>
    EvaluationNotIdentificationLevel
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.1 (H2, L4, M5): the pure requester-token rules. The evaluation
/// token passed to <c>AccessCheck</c> must be the full Medium filtered token of the peer's logon, so a Low, restricted
/// or AppContainer desktop cannot understate the user's rights. The checks run in a fixed order and the first failing
/// rule decides the verdict. Every uncertainty refuses.
/// </summary>
internal static class RequesterTokenPolicy
{
    /// <summary><c>SECURITY_MANDATORY_MEDIUM_RID</c>. Medium Plus (0x2100) and every other level refuse.</summary>
    internal const uint MediumIntegrityRid = 0x2000;

    /// <summary>The standard UAC filtered-token privileges (L4). Any other privilege could exceed what AccessCheck models.</summary>
    internal static readonly IReadOnlySet<string> AllowedPrivileges = new HashSet<string>(StringComparer.Ordinal)
    {
        "SeChangeNotifyPrivilege",
        "SeShutdownPrivilege",
        "SeUndockPrivilege",
        "SeIncreaseWorkingSetPrivilege",
        "SeTimeZonePrivilege"
    };

    /// <summary>
    /// Validates <paramref name="peer"/> (the desktop token read from the held peer handle) and
    /// <paramref name="evaluation"/> (the token that <c>AccessCheck</c> will use) against <paramref name="expectedSid"/>.
    /// The desktop role passes its own facts as both arguments.
    /// </summary>
    internal static TokenVerdict Validate(TokenFacts peer, TokenFacts evaluation, string expectedSid)
    {
        if (!WellFormed(peer) || !WellFormed(evaluation) || !IsSid(expectedSid)) return TokenVerdict.FactsMalformed;
        if (peer.ElevationType != TokenElevationKind.Limited) return TokenVerdict.PeerNotLimited;
        if (!string.Equals(peer.UserSid, expectedSid, StringComparison.Ordinal)) return TokenVerdict.PeerSidMismatch;
        if (!string.Equals(evaluation.UserSid, expectedSid, StringComparison.Ordinal)) return TokenVerdict.EvaluationSidMismatch;
        if (peer.AuthenticationId != evaluation.AuthenticationId) return TokenVerdict.AuthenticationIdMismatch;
        if (evaluation.ElevationType != TokenElevationKind.Limited) return TokenVerdict.EvaluationNotLimited;
        if (evaluation.IntegrityRid != MediumIntegrityRid) return TokenVerdict.EvaluationNotMediumIntegrity;
        if (evaluation.Restricted) return TokenVerdict.EvaluationRestricted;
        if (evaluation.AppContainer) return TokenVerdict.EvaluationAppContainer;
        if (evaluation.Administrators is not (AdministratorsMembership.Absent or AdministratorsMembership.DenyOnly))
            return TokenVerdict.EvaluationAdministratorsEnabled;
        return evaluation.Privileges.All(AllowedPrivileges.Contains) ? TokenVerdict.Accepted : TokenVerdict.EvaluationPrivilegeNotAllowed;
    }

    private static bool WellFormed(TokenFacts? facts) =>
        facts is not null && IsSid(facts.UserSid) && facts.AuthenticationId != 0 && facts.Privileges is not null &&
        facts.Privileges.All(name => !string.IsNullOrEmpty(name)) && Enum.IsDefined(facts.ElevationType) &&
        Enum.IsDefined(facts.Administrators);

    private static bool IsSid(string? value) =>
        value is { Length: > 4 and <= 256 } && value.StartsWith("S-1-", StringComparison.Ordinal) &&
        value.Skip(4).All(character => character is '-' or (>= '0' and <= '9'));
}
