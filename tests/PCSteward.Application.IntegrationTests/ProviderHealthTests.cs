using PCSteward.Catalog;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>CRD-08, FR-PRV-01: failed observation is not absence, consent or launch authority.</summary>
[TestClass]
public sealed class ProviderHealthTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AbsentRequiresPositiveFilesystemEvidence()
    {
        int enumerations = 0;
        ProviderPresence presence = MachineTrustedWinget.ObserveClientPresence(() => { enumerations++; return []; });
        Assert.AreEqual(1, enumerations, "Absence must require accessible canonical directory enumeration.");
        Assert.AreEqual(ProviderPresence.Absent, presence);
        Assert.AreEqual(ProviderPresence.Absent, MachineTrustedChocolatey.ObserveClientPresence(() => []));
        WingetClient fakeClient = new() { Discovery = new(presence, ProviderTrust.Unknown, null) };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(ProviderPresence.Absent, report.Presence);
        Assert.AreEqual(ProviderTrust.Unknown, report.Trust);
        Assert.AreEqual(ProviderProbeState.Current, report.State);
        Assert.AreEqual(0, fakeClient.LaunchCount);
    }

    [TestMethod]
    public async Task ProbeFailureIsUnknownNotAbsent()
    {
        WingetClient fakeClient = new() { DiscoveryFailure = new UnauthorizedAccessException("fixture") };
        ProviderHealthReport failedProbe = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(ProviderPresence.Unknown, failedProbe.Presence);
        Assert.AreEqual(ProviderTrust.Unknown, failedProbe.Trust);
        Assert.AreEqual(ProviderProbeState.Unavailable, failedProbe.State);
        Assert.AreEqual(CheckedAt, failedProbe.CheckedAt);
        Assert.AreEqual(0, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
    }

    [TestMethod]
    public void InaccessibleCanonicalDirectoryIsUnknown()
    {
        Assert.AreEqual(ProviderPresence.Unknown, MachineTrustedWinget.ObserveClientPresence(() => throw new UnauthorizedAccessException()));
        Assert.AreEqual(ProviderPresence.Unknown, MachineTrustedChocolatey.ObserveClientPresence(() => throw new IOException()));
    }

    [TestMethod]
    public void ObservedCanonicalClientIsPresent()
    {
        Assert.AreEqual(ProviderPresence.Present, MachineTrustedWinget.ObserveClientPresence(() => ["winget.exe"]));
        Assert.AreEqual(ProviderPresence.Present, MachineTrustedChocolatey.ObserveClientPresence(() => ["choco.exe"]));
    }

    [TestMethod]
    public async Task UntrustedClientIsNeverRunForVersion()
    {
        WingetClient fakeClient = new() { Discovery = new(ProviderPresence.Present, ProviderTrust.Untrusted, null) };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(0, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
        Assert.AreEqual(ProviderPresence.Present, report.Presence);
        Assert.AreEqual(ProviderTrust.Untrusted, report.Trust);
        Assert.AreEqual(ProviderProbeState.Unavailable, report.State);
        Assert.IsNull(report.Version);
    }

    [TestMethod]
    public async Task ChocolateyRequiresProtectedLayout()
    {
        ChocolateyClient fakeClient = new() { ProtectedLayout = false };
        ProviderHealthReport report = await Chocolatey(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.IsTrue(fakeClient.StrictDiscoveryRequested);
        Assert.AreEqual(0, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
        Assert.AreEqual(ProviderPresence.Present, report.Presence);
        Assert.AreEqual(ProviderTrust.Untrusted, report.Trust);
    }

    [TestMethod]
    [DataRow("acl")]
    [DataRow("signature")]
    [DataRow("reparse")]
    public async Task ChocolateyTrustFailureNeverRuns(string failure)
    {
        ChocolateyClient fakeClient = new() { ProtectedLayout = failure != "acl", SignatureTrusted = failure != "signature", NoReparseComponents = failure != "reparse" };
        ProviderHealthReport report = await Chocolatey(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.IsTrue(fakeClient.StrictDiscoveryRequested);
        Assert.AreEqual(0, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
        Assert.AreEqual(ProviderTrust.Untrusted, report.Trust);
    }

    [TestMethod]
    public async Task ChocolateyRevalidatesStrictUnchangedClientBeforeVersion()
    {
        ChocolateyClient fakeClient = new();
        ProviderHealthReport report = await Chocolatey(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.IsTrue(fakeClient.StrictDiscoveryRequested);
        Assert.IsTrue(fakeClient.StrictRevalidationRequested);
        CollectionAssert.AreEqual(new[] { "discover", "validate", "version", "sources" }, fakeClient.Calls.ToArray());
        Assert.AreEqual(ProviderTrust.Trusted, report.Trust);
        Assert.AreEqual("2.6.0", report.Version);
        Assert.AreEqual(ProviderProbeState.Current, report.State);
    }

    [TestMethod]
    public async Task ChangedChocolateyClientNeverRuns()
    {
        ChocolateyClient fakeClient = new() { Changed = true };
        ProviderHealthReport report = await Chocolatey(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(0, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
        Assert.AreEqual(ProviderTrust.Untrusted, report.Trust);
        Assert.AreEqual(ProviderProbeState.Unavailable, report.State);
    }

    [TestMethod]
    public async Task ConsentDoesNotBecomeHealthOrReachability()
    {
        // Source consent is deliberately not an input to either probe. Even arbitrary configured
        // sources are display evidence only; the health contract has no reachability/consent bit.
        WingetClient fakeClient = new() { Sources = ["winget", "msstore", "offline-private-source"] };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(CatalogProviderKind.WinGet, report.Provider);
        Assert.AreEqual(ProviderProbeState.Current, report.State);
        Assert.AreEqual(ProviderTrust.Trusted, report.Trust);
        CollectionAssert.AreEqual(fakeClient.Sources.ToArray(), report.ConfiguredSources.ToArray());
        Assert.IsFalse(typeof(ProviderHealthReport).GetProperties().Any(property =>
            property.Name.Contains("Consent", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Reachab", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task FailedSourceReadIsPartialAndDoesNotEraseVersion()
    {
        WingetClient fakeClient = new() { SourceFailure = new IOException("fixture") };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(ProviderPresence.Present, report.Presence);
        Assert.AreEqual(ProviderTrust.Trusted, report.Trust);
        Assert.AreEqual(ProviderProbeState.Partial, report.State);
        Assert.AreEqual("v1.12.0", report.Version);
        Assert.HasCount(0, report.ConfiguredSources);
    }

    [TestMethod]
    public async Task OversizedOrFailedVersionIsNeverCurrent()
    {
        WingetClient fakeClient = new() { Version = new(0, "v1.12.0", "", false, true) };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        Assert.IsNull(report.Version);
        Assert.AreEqual(ProviderProbeState.Partial, report.State);
    }

    private static WingetProviderHealthProbe Winget(WingetClient client) => new(client, () => CheckedAt);
    private static ChocolateyProviderHealthProbe Chocolatey(ChocolateyClient client) => new(client, () => CheckedAt);

    private sealed class WingetClient : IWingetHealthClient
    {
        public WingetHealthDiscovery Discovery { get; init; } = new(ProviderPresence.Present, ProviderTrust.Trusted, "fixture-only");
        public Exception? DiscoveryFailure { get; init; }
        public Exception? SourceFailure { get; init; }
        public WingetProcessResult Version { get; init; } = new(0, "v1.12.0", "", false);
        public IReadOnlyList<string> Sources { get; init; } = ["winget", "msstore"];
        public int LaunchCount { get; private set; }
        public int SourceReadCount { get; private set; }
        public WingetHealthDiscovery Discover() => DiscoveryFailure is null ? Discovery : throw DiscoveryFailure;
        public Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken)
        {
            LaunchCount++;
            return Task.FromResult(Version);
        }
        public Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(string executable, CancellationToken cancellationToken)
        {
            SourceReadCount++;
            return SourceFailure is null ? Task.FromResult(Sources) : throw SourceFailure;
        }
    }

    private sealed class ChocolateyClient : IChocolateyHealthClient
    {
        private readonly ChocolateyClientIdentity identity = new("fixture-only", new byte[32]);
        public bool ProtectedLayout { get; init; } = true;
        public bool SignatureTrusted { get; init; } = true;
        public bool NoReparseComponents { get; init; } = true;
        public bool Changed { get; init; }
        public bool StrictDiscoveryRequested { get; private set; }
        public bool StrictRevalidationRequested { get; private set; }
        public int LaunchCount { get; private set; }
        public int SourceReadCount { get; private set; }
        public List<string> Calls { get; } = [];
        public ChocolateyHealthDiscovery Discover(bool requireCallerCannotModifyLayout)
        {
            Calls.Add("discover");
            StrictDiscoveryRequested = requireCallerCannotModifyLayout;
            bool trusted = (!requireCallerCannotModifyLayout || ProtectedLayout) && SignatureTrusted && NoReparseComponents;
            return new(ProviderPresence.Present, trusted ? ProviderTrust.Trusted : ProviderTrust.Untrusted, trusted ? identity : null);
        }
        public void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout)
        {
            Calls.Add("validate");
            StrictRevalidationRequested = requireCallerCannotModifyLayout;
            if (Changed) throw new IOException("fixture changed client");
        }
        public Task<ChocolateyProcessResult> ReadVersionAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken)
        {
            Calls.Add("version");
            LaunchCount++;
            return Task.FromResult(new ChocolateyProcessResult(0, "2.6.0", "", false));
        }
        public Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken)
        {
            Calls.Add("sources");
            SourceReadCount++;
            return Task.FromResult<IReadOnlyList<string>>(["chocolatey-community"]);
        }
    }
}
