using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.1 and §8 N1 (M5, L4): <see cref="RequesterTokenPolicy.Validate"/>
/// is pure and portable. Each single deviation from an accepted split-token pair asserts its specific verdict, and an
/// exhaustive sweep over every combination of the §8 factors asserts the first failing rule.
/// </summary>
[TestClass]
public sealed class RequesterTokenPolicyTests
{
    private const string User = "S-1-5-21-1004336348-1177238915-682003330-1001";
    private const string Other = "S-1-5-21-1004336348-1177238915-682003330-1002";
    private const ulong Logon = 0x0000_0000_0123_4567;
    private static readonly string[] Standard =
        ["SeChangeNotifyPrivilege", "SeShutdownPrivilege", "SeUndockPrivilege", "SeIncreaseWorkingSetPrivilege", "SeTimeZonePrivilege"];

    private static TokenFacts Filtered(string sid = User, ulong logon = Logon, TokenElevationKind elevation = TokenElevationKind.Limited,
        uint integrity = 0x2000, bool restricted = false, bool appContainer = false,
        AdministratorsMembership administrators = AdministratorsMembership.DenyOnly, string[]? privileges = null) =>
        new(sid, logon, elevation, integrity, restricted, appContainer, administrators, privileges ?? Standard);

    [TestMethod]
    public void ASplitTokenPairOfTheSameLogonIsAccepted()
    {
        Assert.AreEqual(TokenVerdict.Accepted, RequesterTokenPolicy.Validate(Filtered(), Filtered(), User));
        Assert.AreEqual(TokenVerdict.Accepted, RequesterTokenPolicy.Validate(Filtered(), Filtered(privileges: ["SeChangeNotifyPrivilege"]), User));
        Assert.AreEqual(TokenVerdict.Accepted, RequesterTokenPolicy.Validate(Filtered(), Filtered(privileges: []), User));
        Assert.AreEqual(TokenVerdict.Accepted,
            RequesterTokenPolicy.Validate(Filtered(), Filtered(administrators: AdministratorsMembership.Absent), User));
    }

    [TestMethod]
    public void ALowRestrictedOrAppContainerDesktopIsJudgedByTheEvaluationTokenNotItsOwnShape()
    {
        TokenFacts lowDesktop = Filtered(integrity: 0x1000, restricted: true, appContainer: true, privileges: ["SeChangeNotifyPrivilege"]);
        Assert.AreEqual(TokenVerdict.Accepted, RequesterTokenPolicy.Validate(lowDesktop, Filtered(), User));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(0)]
    public void APeerThatIsNotAFilteredTokenRefuses(int elevation) =>
        Assert.AreEqual(TokenVerdict.PeerNotLimited, RequesterTokenPolicy.Validate(Filtered(elevation: (TokenElevationKind)elevation), Filtered(), User));

    [TestMethod]
    public void SidMismatchesRefuseWithTheirOwnVerdict()
    {
        Assert.AreEqual(TokenVerdict.PeerSidMismatch, RequesterTokenPolicy.Validate(Filtered(sid: Other), Filtered(), User));
        Assert.AreEqual(TokenVerdict.EvaluationSidMismatch, RequesterTokenPolicy.Validate(Filtered(), Filtered(sid: Other), User));
        Assert.AreEqual(TokenVerdict.PeerSidMismatch, RequesterTokenPolicy.Validate(Filtered(), Filtered(), Other));
        // SIDs compare ordinally; a differently cased SID string is not the expected SID.
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), Filtered(), "s-1-5-21-1"));
    }

    [TestMethod]
    public void ADifferentLogonSessionRefuses() =>
        Assert.AreEqual(TokenVerdict.AuthenticationIdMismatch, RequesterTokenPolicy.Validate(Filtered(), Filtered(logon: Logon + 1), User));

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(0)]
    public void AnEvaluationTokenThatIsNotTheFilteredHalfRefuses(int elevation) =>
        Assert.AreEqual(TokenVerdict.EvaluationNotLimited, RequesterTokenPolicy.Validate(Filtered(), Filtered(elevation: (TokenElevationKind)elevation), User));

    [TestMethod]
    [DataRow(0x0000u)]
    [DataRow(0x1000u)]
    [DataRow(0x1FFFu)]
    [DataRow(0x2001u)]
    [DataRow(0x2100u)]
    [DataRow(0x3000u)]
    [DataRow(0x4000u)]
    public void OnlyExactlyMediumIntegrityIsAccepted(uint integrity) =>
        Assert.AreEqual(TokenVerdict.EvaluationNotMediumIntegrity, RequesterTokenPolicy.Validate(Filtered(), Filtered(integrity: integrity), User));

    [TestMethod]
    public void RestrictedAppContainerAndUsableAdministratorsRefuse()
    {
        Assert.AreEqual(TokenVerdict.EvaluationRestricted, RequesterTokenPolicy.Validate(Filtered(), Filtered(restricted: true), User));
        Assert.AreEqual(TokenVerdict.EvaluationAppContainer, RequesterTokenPolicy.Validate(Filtered(), Filtered(appContainer: true), User));
        Assert.AreEqual(TokenVerdict.EvaluationAdministratorsEnabled,
            RequesterTokenPolicy.Validate(Filtered(), Filtered(administrators: AdministratorsMembership.Enabled), User));
    }

    [TestMethod]
    [DataRow("SeBackupPrivilege")]
    [DataRow("SeRestorePrivilege")]
    [DataRow("SeTakeOwnershipPrivilege")]
    [DataRow("SeRelabelPrivilege")]
    [DataRow("SeSecurityPrivilege")]
    [DataRow("SeDebugPrivilege")]
    [DataRow("SeImpersonatePrivilege")]
    [DataRow("SeCreateGlobalPrivilege")]
    [DataRow("SeTcbPrivilege")]
    [DataRow("sechangenotifyprivilege")]
    public void AnyPrivilegeOutsideTheAllowListRefusesEnabledOrNot(string privilege) =>
        Assert.AreEqual(TokenVerdict.EvaluationPrivilegeNotAllowed,
            RequesterTokenPolicy.Validate(Filtered(), Filtered(privileges: [.. Standard, privilege]), User));

    [TestMethod]
    public void MalformedFactsRefuse()
    {
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(null!, Filtered(), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), null!, User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(sid: ""), Filtered(), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), Filtered(sid: "S-1-5-x"), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), Filtered(), ""));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(logon: 0), Filtered(logon: 0), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), Filtered(privileges: [""]), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed, RequesterTokenPolicy.Validate(Filtered(), Filtered() with { Privileges = null! }, User));
        Assert.AreEqual(TokenVerdict.FactsMalformed,
            RequesterTokenPolicy.Validate(Filtered(), Filtered(elevation: (TokenElevationKind)7), User));
        Assert.AreEqual(TokenVerdict.FactsMalformed,
            RequesterTokenPolicy.Validate(Filtered(), Filtered(administrators: (AdministratorsMembership)9), User));
    }

    [TestMethod]
    public void EveryCombinationOfTheFactorsYieldsTheFirstFailingRule()
    {
        TokenElevationKind[] elevations = [TokenElevationKind.Unknown, TokenElevationKind.Default, TokenElevationKind.Full, TokenElevationKind.Limited];
        uint[] integrities = [0x1000, 0x2000, 0x2100, 0x3000];
        AdministratorsMembership[] administrators = [AdministratorsMembership.Absent, AdministratorsMembership.DenyOnly, AdministratorsMembership.Enabled];
        int combinations = 0, accepted = 0;
        HashSet<TokenVerdict> seen = [];
        foreach (bool logonMatch in new[] { true, false })
        foreach (bool peerSidMatch in new[] { true, false })
        foreach (bool evaluationSidMatch in new[] { true, false })
        foreach (TokenElevationKind peerElevation in elevations)
        foreach (TokenElevationKind evaluationElevation in elevations)
        foreach (uint integrity in integrities)
        foreach (bool restricted in new[] { false, true })
        foreach (bool appContainer in new[] { false, true })
        foreach (AdministratorsMembership admin in administrators)
        foreach (bool extraPrivilege in new[] { false, true })
        {
            TokenFacts peer = Filtered(sid: peerSidMatch ? User : Other, elevation: peerElevation);
            TokenFacts evaluation = Filtered(sid: evaluationSidMatch ? User : Other, logon: logonMatch ? Logon : Logon + 7,
                elevation: evaluationElevation, integrity: integrity, restricted: restricted, appContainer: appContainer,
                administrators: admin, privileges: extraPrivilege ? [.. Standard, "SeBackupPrivilege"] : Standard);
            TokenVerdict expected =
                peerElevation != TokenElevationKind.Limited ? TokenVerdict.PeerNotLimited :
                !peerSidMatch ? TokenVerdict.PeerSidMismatch :
                !evaluationSidMatch ? TokenVerdict.EvaluationSidMismatch :
                !logonMatch ? TokenVerdict.AuthenticationIdMismatch :
                evaluationElevation != TokenElevationKind.Limited ? TokenVerdict.EvaluationNotLimited :
                integrity != 0x2000 ? TokenVerdict.EvaluationNotMediumIntegrity :
                restricted ? TokenVerdict.EvaluationRestricted :
                appContainer ? TokenVerdict.EvaluationAppContainer :
                admin == AdministratorsMembership.Enabled ? TokenVerdict.EvaluationAdministratorsEnabled :
                extraPrivilege ? TokenVerdict.EvaluationPrivilegeNotAllowed : TokenVerdict.Accepted;
            TokenVerdict actual = RequesterTokenPolicy.Validate(peer, evaluation, User);
            Assert.AreEqual(expected, actual,
                $"logon={logonMatch} peerSid={peerSidMatch} evalSid={evaluationSidMatch} peer={peerElevation} eval={evaluationElevation} " +
                $"il={integrity:X} restricted={restricted} appContainer={appContainer} admin={admin} extra={extraPrivilege}");
            combinations++;
            seen.Add(actual);
            if (actual == TokenVerdict.Accepted) accepted++;
        }
        Assert.AreEqual(2 * 2 * 2 * 4 * 4 * 4 * 2 * 2 * 3 * 2, combinations);
        Assert.AreEqual(2, accepted, "only the Absent and DenyOnly Administrators variants of the clean pair are accepted");
        Assert.AreEqual(11, seen.Count, "every pure-policy verdict except FactsMalformed is reached");
    }
}
