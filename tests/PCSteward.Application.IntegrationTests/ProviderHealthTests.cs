using PCSteward.Catalog;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>CRD-08, FR-PRV-01: failed observation is not absence, consent or launch authority.</summary>
[TestClass]
public sealed class ProviderHealthTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedChocolateyProbeCalls = ["discover", "validate", "version", "sources"];
    private static readonly string[] ExpectedWingetSources = ["winget", "private source"];
    private const string ProgramDataRoot = @"C:\ProgramData";
    private const string ProgramFilesRoot = @"C:\Program Files";
    private const string RegisteredWingetDirectory = @"C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_fixture_8wekyb3d8bbwe";
    private const string RegisteredWingetExecutable = RegisteredWingetDirectory + @"\winget.exe";
    private const string ChocolateyExecutable = ProgramDataRoot + @"\chocolatey\bin\choco.exe";

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
        CollectionAssert.AreEqual(ExpectedChocolateyProbeCalls, fakeClient.Calls.ToArray());
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

    [TestMethod]
    public void NativeChocolateyDiscoveryExplicitlyRequestsStrictTrust()
    {
        bool? strict = null;
        ChocolateyHealthDiscovery discovery = MachineTrustedChocolatey.DiscoverHealth(ProgramDataRoot, ChocolateyDirectory,
            _ => FileAttributes.Normal, required => { strict = required; return new(ChocolateyExecutable, new byte[32]); });
        Assert.IsTrue(strict is true);
        Assert.AreEqual(ProviderPresence.Present, discovery.Presence);
        Assert.AreEqual(ProviderTrust.Trusted, discovery.Trust);
    }

    [TestMethod]
    public void NativeChocolateyMissingRootNeedsAccessibleParentEnumeration()
    {
        int trustCalls = 0;
        ChocolateyHealthDiscovery discovery = MachineTrustedChocolatey.DiscoverHealth(ProgramDataRoot, _ => [],
            _ => FileAttributes.Normal, _ => { trustCalls++; throw new IOException(); });
        Assert.AreEqual(ProviderPresence.Absent, discovery.Presence);
        Assert.AreEqual(ProviderTrust.Unknown, discovery.Trust);
        Assert.AreEqual(0, trustCalls);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => MachineTrustedChocolatey.DiscoverHealth(ProgramDataRoot,
            _ => throw new UnauthorizedAccessException(), _ => FileAttributes.Normal, _ => throw new IOException()));
    }

    [TestMethod]
    public void NativeChocolateyReparseAndTrustFailureCannotBeTrusted()
    {
        int trustCalls = 0;
        Assert.ThrowsExactly<IOException>(() => MachineTrustedChocolatey.DiscoverHealth(ProgramDataRoot, ChocolateyDirectory,
            _ => FileAttributes.ReparsePoint, _ => { trustCalls++; return new(ChocolateyExecutable, new byte[32]); }));
        Assert.AreEqual(0, trustCalls);
        ChocolateyHealthDiscovery failure = MachineTrustedChocolatey.DiscoverHealth(ProgramDataRoot, ChocolateyDirectory,
            _ => FileAttributes.Normal, _ => throw new IOException("fixture signature/ACL failure"));
        Assert.AreEqual(ProviderPresence.Present, failure.Presence);
        Assert.AreEqual(ProviderTrust.Untrusted, failure.Trust);
        Assert.IsNull(failure.Client);
    }

    [TestMethod]
    public void RegisteredWinGetDoesNotRequireWindowsAppsRootEnumeration()
    {
        WingetHealthDiscovery discovery = MachineTrustedWinget.DiscoverHealth(ProgramFilesRoot, () => [RegisteredWingetDirectory],
            directory => directory == RegisteredWingetDirectory ? ["winget.exe"] : throw new UnauthorizedAccessException(),
            _ => FileAttributes.Normal, () => RegisteredWingetExecutable);
        Assert.AreEqual(ProviderPresence.Present, discovery.Presence);
        Assert.AreEqual(ProviderTrust.Trusted, discovery.Trust);
        Assert.AreEqual(RegisteredWingetExecutable, discovery.Executable);
    }

    [TestMethod]
    public void MissingRegisteredWinGetRequiresCanonicalFilesystemEvidence()
    {
        int trustCalls = 0;
        WingetHealthDiscovery absent = MachineTrustedWinget.DiscoverHealth(ProgramFilesRoot, () => [], _ => [],
            _ => FileAttributes.Normal, () => { trustCalls++; throw new IOException(); });
        Assert.AreEqual(ProviderPresence.Absent, absent.Presence);
        Assert.AreEqual(0, trustCalls);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => MachineTrustedWinget.DiscoverHealth(ProgramFilesRoot, () => [],
            _ => throw new UnauthorizedAccessException(), _ => FileAttributes.Normal, () => throw new IOException()));
    }

    [TestMethod]
    public void OversizedOrMalformedDirectoryObservationIsUnknown()
    {
        Assert.AreEqual(ProviderPresence.Unknown, MachineTrustedWinget.ObserveClientPresence(() => Enumerable.Repeat("entry", 4097).ToArray()));
        Assert.AreEqual(ProviderPresence.Unknown, MachineTrustedChocolatey.ObserveClientPresence(() => [@"..\choco.exe"]));
    }

    [TestMethod]
    public void SourceReadCommandsContainOnlyFixedReadOnlyArguments()
    {
        Assert.AreEqual("--version", string.Join(' ', WindowsWingetHealthClient.VersionArguments));
        Assert.AreEqual("source list --disable-interactivity", string.Join(' ', WindowsWingetHealthClient.SourceArguments));
        Assert.AreEqual("--version --no-color", string.Join(' ', WindowsChocolateyHealthClient.VersionArguments));
    }

    [TestMethod]
    public void WinGetConfiguredSourcesAreBoundedLocalDisplayNames()
    {
        WingetProcessResult result = new(0, "Name            Argument\n--------------------------------\nwinget          https://example.invalid/\nprivate source  https://private.invalid/\n", "", false);
        IReadOnlyList<string> sources = WindowsWingetHealthClient.ParseConfiguredSources(result);
        CollectionAssert.AreEqual(ExpectedWingetSources, sources.ToArray());
        Assert.ThrowsExactly<IOException>(() => WindowsWingetHealthClient.ParseConfiguredSources(new(0, "partial", "", false)));
        Assert.ThrowsExactly<IOException>(() => WindowsWingetHealthClient.ParseConfiguredSources(new(0, new string('x', 65_537), "", false)));
    }

    [TestMethod]
    public void ChocolateyConfiguredSourcesRejectExternalEntitiesAndMalformedXml()
    {
        using MemoryStream valid = XmlInput("<chocolatey><sources><source id='chocolatey' value='https://example.invalid/' /></sources></chocolatey>");
        Assert.AreEqual("chocolatey", WindowsChocolateyHealthClient.ParseConfiguredSources(valid).Single());
        using MemoryStream hostile = XmlInput("<!DOCTYPE chocolatey [<!ENTITY remote SYSTEM 'https://example.invalid/'>]><chocolatey><sources><source id='&remote;' /></sources></chocolatey>");
        Assert.ThrowsExactly<System.Xml.XmlException>(() => WindowsChocolateyHealthClient.ParseConfiguredSources(hostile));
        using MemoryStream wrongRoot = XmlInput("<untrusted><sources /></untrusted>");
        Assert.ThrowsExactly<IOException>(() => WindowsChocolateyHealthClient.ParseConfiguredSources(wrongRoot));
    }

    [TestMethod]
    public async Task OversizedSourceEvidenceIsPartialAndSourceSnapshotCannotBeMutated()
    {
        WingetClient oversized = new() { Sources = Enumerable.Repeat("entry", 65).ToArray() };
        ProviderHealthReport partial = await Winget(oversized).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(ProviderProbeState.Partial, partial.State);
        Assert.HasCount(0, partial.ConfiguredSources);
        List<string> input = ["winget"];
        WingetClient fakeClient = new() { Sources = input };
        ProviderHealthReport report = await Winget(fakeClient).ProbeAsync(CancellationToken.None);
        input.Add("later-change");
        Assert.HasCount(1, report.ConfiguredSources);
        Assert.IsTrue(((IList<string>)report.ConfiguredSources).IsReadOnly);
    }

    [TestMethod]
    public async Task CancellationAfterAwaitStopsRemainingReads()
    {
        using CancellationTokenSource cancellation = new();
        WingetClient fakeClient = new() { OnVersionRead = cancellation.Cancel };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Winget(fakeClient).ProbeAsync(cancellation.Token));
        Assert.AreEqual(1, fakeClient.LaunchCount);
        Assert.AreEqual(0, fakeClient.SourceReadCount);
    }

    private static MemoryStream XmlInput(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));
    private static IReadOnlyList<string> ChocolateyDirectory(string directory) => directory switch
    {
        ProgramDataRoot => ["chocolatey"],
        ProgramDataRoot + @"\chocolatey" => ["bin"],
        ProgramDataRoot + @"\chocolatey\bin" => ["choco.exe"],
        _ => throw new IOException("Unexpected fixture path.")
    };

    private static WingetProviderHealthProbe Winget(WingetClient client) => new(client, () => CheckedAt);
    private static ChocolateyProviderHealthProbe Chocolatey(ChocolateyClient client) => new(client, () => CheckedAt);

    private sealed class WingetClient : IWingetHealthClient
    {
        public WingetHealthDiscovery Discovery { get; init; } = new(ProviderPresence.Present, ProviderTrust.Trusted, "fixture-only");
        public Exception? DiscoveryFailure { get; init; }
        public Exception? SourceFailure { get; init; }
        public Action? OnVersionRead { get; init; }
        public WingetProcessResult Version { get; init; } = new(0, "v1.12.0", "", false);
        public IReadOnlyList<string> Sources { get; init; } = ["winget", "msstore"];
        public int LaunchCount { get; private set; }
        public int SourceReadCount { get; private set; }
        public WingetHealthDiscovery Discover() => DiscoveryFailure is null ? Discovery : throw DiscoveryFailure;
        public Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken)
        {
            LaunchCount++;
            OnVersionRead?.Invoke();
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
