using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 48: FR-INS-01–04, TM-22, spike §2.2/§2.6/§3.1 — preflight, exact argv snapshots and exit codes.</summary>
[TestClass]
public sealed class InstallOperationTests
{
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly PackageAgreementText Licence = new("License", "You agree to the terms.", "https://example.invalid/license");
    private static readonly string[] ForbiddenFlags = ["--override", "--custom", "--ignore-security-hash", "--force", "--location", "--skip-dependencies", "--accept-source-agreements", "--install-arguments", "--override-arguments", "--params", "--package-parameters", "--skip-powershell", "--ignore-dependencies", "--ignore-checksums", "--allow-empty-checksums", "-y", "--interactive", "-i"];

    [TestMethod]
    public void WingetReadyObservationProducesAnInstallSelectionWithTheAgreementHash()
    {
        InstallPreflightResult result = WingetInstallPreflightAuthority.Assess(WingetRequest(), Observation());

        Assert.IsTrue(result.IsActionable, result.Reason);
        UpdateJobSelection selection = result.Selection!;
        Assert.AreEqual(PackageOperationKind.Install, selection.Operation);
        Assert.AreEqual("pcs:notepadpp", selection.StableProductKey);
        Assert.AreEqual("Notepad++.Notepad++", selection.PackageId, "The provider's canonical casing is used for the case-sensitive --exact match.");
        Assert.AreEqual("8.8.3", selection.CandidateVersion);
        Assert.IsNull(selection.InstalledVersion);
        Assert.IsFalse(selection.RunsElevated);
        Assert.IsTrue(PackageAgreementDigest.TryCompute([Licence], out string? expectedHash, out _));
        Assert.AreEqual(expectedHash, selection.AgreementHash);
        Assert.AreEqual(1, result.Agreements.Count);
    }

    [TestMethod]
    [DataRow("untrusted", "source-not-trusted")]
    [DataRow("not-predefined", "source-not-trusted")]
    [DataRow("other-source", "source-not-trusted")]
    [DataRow("two-matches", "package-not-unique")]
    [DataRow("limit-exceeded", "package-not-unique")]
    [DataRow("not-exact", "package-not-unique")]
    [DataRow("other-package", "package-mismatch")]
    [DataRow("hostile-package", "package-mismatch")]
    [DataRow("installed", "already-installed")]
    [DataRow("no-version", "version-unavailable")]
    [DataRow("hostile-version", "version-unavailable")]
    [DataRow("version-from-msstore", "source-mismatch")]
    [DataRow("preview-channel", "channel-not-stable")]
    [DataRow("no-installer", "no-applicable-installer")]
    [DataRow("machine-installer", "scope-conflict")]
    [DataRow("arm-installer", "architecture-conflict")]
    [DataRow("unknown-architecture", "architecture-conflict")]
    [DataRow("agreements-unavailable", "agreements-missing")]
    [DataRow("agreements-oversize", "agreement-oversize")]
    [DataRow("agreements-too-many", "agreements-too-many")]
    public void HostileOrIncompatibleWingetEvidenceIsNotActionable(string mutation, string reason)
    {
        WinGetInstallObservation observation = Observation();
        WinGetApplicableInstaller installer = observation.Installer!;
        observation = mutation switch
        {
            "untrusted" => observation with { CatalogIsTrusted = false },
            "not-predefined" => observation with { CatalogIsPredefined = false },
            "other-source" => observation with { SourceId = "msstore" },
            "two-matches" => observation with { MatchCount = 2 },
            "limit-exceeded" => observation with { WasLimitExceeded = true },
            "not-exact" => observation with { ExactIdMatch = false },
            "other-package" => observation with { PackageId = "Notepad++.Plugins" },
            "hostile-package" => observation with { PackageId = "Notepad++.Notepad++ --override x" },
            "installed" => observation with { IsInstalled = true },
            "no-version" => observation with { Version = null },
            "hostile-version" => observation with { Version = "8.8.3 --force" },
            "version-from-msstore" => observation with { VersionSourceId = "msstore" },
            "preview-channel" => observation with { Channel = "beta" },
            "no-installer" => observation with { Installer = null },
            "machine-installer" => observation with { Installer = installer with { Scope = UpdateScope.Machine } },
            "arm-installer" => observation with { Installer = installer with { Architecture = UpdateArchitecture.Arm64 } },
            "unknown-architecture" => observation with { Installer = installer with { Architecture = (UpdateArchitecture)(-1) } },
            "agreements-unavailable" => observation with { Agreements = null },
            "agreements-oversize" => observation with { Agreements = [Licence with { Text = new string('x', PackageAgreementDigest.MaximumTextLength + 1) }] },
            "agreements-too-many" => observation with { Agreements = [.. Enumerable.Repeat(Licence, PackageAgreementDigest.MaximumAgreements + 1)] },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        InstallPreflightResult result = WingetInstallPreflightAuthority.Assess(WingetRequest(), observation);

        Assert.IsFalse(result.IsActionable);
        Assert.AreEqual(reason, result.Reason);
        Assert.IsNull(result.Selection);
    }

    [TestMethod]
    public void WingetElevationRouting()
    {
        WinGetInstallObservation observation = Observation();
        InstallPreflightResult selfElevating = WingetInstallPreflightAuthority.Assess(WingetRequest(), observation with { Installer = observation.Installer! with { Elevation = InstallerElevation.ElevatesSelf } });
        InstallPreflightResult required = WingetInstallPreflightAuthority.Assess(WingetRequest(), observation with { Installer = observation.Installer! with { Elevation = InstallerElevation.ElevationRequired } });
        InstallPreflightResult unknownScope = WingetInstallPreflightAuthority.Assess(WingetRequest(), observation with { Installer = observation.Installer! with { Scope = null } });
        InstallPreflightResult neutral = WingetInstallPreflightAuthority.Assess(WingetRequest(), observation with { Installer = observation.Installer! with { Architecture = UpdateArchitecture.Neutral } });
        InstallPreflightResult prohibitedMachine = WingetInstallPreflightAuthority.Assess(WingetRequest(UpdateScope.Machine), observation with { Installer = observation.Installer! with { Scope = UpdateScope.Machine, Elevation = InstallerElevation.ElevationProhibited } });

        Assert.IsTrue(selfElevating.Selection!.RunsElevated, "ElevatesSelf joins the elevated session so a batch keeps one UAC prompt.");
        Assert.IsTrue(required.Selection!.RunsElevated);
        Assert.IsFalse(unknownScope.Selection!.RunsElevated);
        Assert.IsTrue(neutral.IsActionable);
        Assert.AreEqual(UpdateArchitecture.Neutral, neutral.Selection!.Architecture, "Task 2 FR-INS-04: retain live applicable Neutral instead of host architecture.");
        Assert.AreEqual("elevation-prohibited-for-machine-scope", prohibitedMachine.Reason);
    }

    [TestMethod]
    public async Task WingetRequestValidationFailsBeforeAnyQuery()
    {
        WingetInstallPreflightAuthority authority = new(new ThrowingQuery(), TimeSpan.FromSeconds(1));
        Assert.AreEqual("provider-source-not-supported", (await authority.PreflightAsync(WingetRequest() with { Source = UpdateSource.MicrosoftStore }, CancellationToken.None)).Reason);
        Assert.AreEqual("architecture-not-supported", (await authority.PreflightAsync(WingetRequest() with { Architecture = (UpdateArchitecture)(-1) }, CancellationToken.None)).Reason);
        Assert.AreEqual("package-id-invalid", (await authority.PreflightAsync(WingetRequest() with { PackageId = "Vendor.App & calc" }, CancellationToken.None)).Reason);
        Assert.AreEqual("version-invalid", (await authority.PreflightAsync(WingetRequest() with { ExpectedVersion = "1.0 --force" }, CancellationToken.None)).Reason);
    }

    [TestMethod]
    public async Task WingetQueryFailureAndTimeoutFailClosed()
    {
        Assert.AreEqual("provider-unavailable", (await new WingetInstallPreflightAuthority(new ThrowingQuery(), TimeSpan.FromSeconds(1)).PreflightAsync(WingetRequest(), CancellationToken.None)).Reason);
        Assert.AreEqual("provider-timed-out", (await new WingetInstallPreflightAuthority(new StalledQuery(), TimeSpan.FromMilliseconds(50)).PreflightAsync(WingetRequest(), CancellationToken.None)).Reason);
        Assert.AreEqual("provider-unavailable", (await new WingetInstallPreflightAuthority(new FixedQuery(null), TimeSpan.FromSeconds(1)).PreflightAsync(WingetRequest(), CancellationToken.None)).Reason);
    }

    [TestMethod]
    public async Task WingetRevalidationRequiresTheSameVersionAndAgreements()
    {
        ImmutableUpdateJob job = Approve(WingetInstallPreflightAuthority.Assess(WingetRequest(), Observation()).Selection!);
        InstallPreflightRequest revalidation = InstallPreflightRequest.ForApprovedJob(job);
        WingetInstallPreflightAuthority same = new(new FixedQuery(Observation()), TimeSpan.FromSeconds(1));
        WingetInstallPreflightAuthority changedText = new(new FixedQuery(Observation() with { Agreements = [Licence with { Text = "New terms." }] }), TimeSpan.FromSeconds(1));
        WingetInstallPreflightAuthority newerVersion = new(new FixedQuery(Observation() with { Version = "8.8.4" }), TimeSpan.FromSeconds(1));
        WingetInstallPreflightAuthority selfElevatingNow = new(new FixedQuery(Observation() with { Installer = Observation().Installer! with { Elevation = InstallerElevation.ElevatesSelf } }), TimeSpan.FromSeconds(1));

        InstallPreflightResult fresh = await same.PreflightAsync(revalidation, CancellationToken.None);
        Assert.IsTrue(job.Matches(fresh.AsExecutionEvidence()!));
        Assert.AreEqual("agreements-changed", (await changedText.PreflightAsync(revalidation, CancellationToken.None)).Reason);
        Assert.AreEqual("version-changed", (await newerVersion.PreflightAsync(revalidation, CancellationToken.None)).Reason);
        InstallPreflightResult rerouted = await selfElevatingNow.PreflightAsync(revalidation, CancellationToken.None);
        Assert.IsFalse(job.Matches(rerouted.AsExecutionEvidence()!), "A changed elevation route is a changed invariant, never adapted silently.");
    }

    [TestMethod]
    public void WingetInstallArgvSnapshots()
    {
        ImmutableUpdateJob user = Approve(UpdateJobSelection.ForInstall("pcs:a", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor_Name.App", "1.2.3", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false));
        ImmutableUpdateJob machine = Approve(WingetInstallPreflightAuthority.Assess(WingetRequest(UpdateScope.Machine, UpdateArchitecture.Arm64), Observation() with { Installer = new(UpdateScope.Machine, UpdateArchitecture.Arm64, InstallerElevation.ElevationRequired, "Nullsoft") }).Selection!);

        string[] expectedUser = ["install", "--id", "Vendor_Name.App", "--exact", "--version", "1.2.3", "--source", "winget", "--scope", "user", "--architecture", "x64", "--silent", "--disable-interactivity"];
        string[] expectedMachine = ["install", "--id", "Notepad++.Notepad++", "--exact", "--version", "8.8.3", "--source", "winget", "--scope", "machine", "--architecture", "arm64", "--silent", "--disable-interactivity", "--accept-package-agreements"];

        CollectionAssert.AreEqual(expectedUser, WingetInstallExecutor.BuildInstallArguments(user, Evidence(user)).ToArray());
        CollectionAssert.AreEqual(expectedMachine, WingetInstallExecutor.BuildInstallArguments(machine, Evidence(machine)).ToArray());
        foreach (ImmutableUpdateJob job in new[] { user, machine })
        {
            Assert.IsFalse(WingetInstallExecutor.BuildInstallArguments(job, Evidence(job)).Intersect(ForbiddenFlags, StringComparer.OrdinalIgnoreCase).Any());
        }
    }

    [TestMethod]
    public void WingetInstallBuilderRejectsMismatchedEvidenceAndOtherOperations()
    {
        ImmutableUpdateJob job = Approve(WingetInstallPreflightAuthority.Assess(WingetRequest(), Observation()).Selection!);
        UpdateJobSelection withoutAgreements = UpdateJobSelection.ForInstall("pcs:notepadpp", UpdateProvider.WinGet, UpdateSource.WinGet, "Notepad++.Notepad++", "8.8.3", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false);
        ImmutableUpdateJob upgrade = Approve(new UpdateJobSelection("winget:a", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.App", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1"));

        Assert.ThrowsExactly<ArgumentException>(() => WingetInstallExecutor.BuildInstallArguments(job, new UpdateJobExecutionEvidence(withoutAgreements, true, true)), "agreement hash differs");
        Assert.ThrowsExactly<ArgumentException>(() => WingetInstallExecutor.BuildInstallArguments(job, new UpdateJobExecutionEvidence(job.Selection, false, true)), "not actionable");
        Assert.ThrowsExactly<ArgumentException>(() => WingetInstallExecutor.BuildInstallArguments(job, new UpdateJobExecutionEvidence(job.Selection, true, false)), "not fresh");
        Assert.ThrowsExactly<ArgumentException>(() => WingetInstallExecutor.BuildInstallArguments(upgrade, Evidence(upgrade)), "upgrade jobs use the upgrade builder");
        Assert.ThrowsExactly<ArgumentException>(() => WingetUpdateExecutor.BuildUpdateArguments(job), "install jobs never reach the upgrade builder");
    }

    [TestMethod]
    public async Task WingetInstallExecutorRunsTheSnapshotAndClassifiesTheExit()
    {
        ImmutableUpdateJob job = Approve(UpdateJobSelection.ForInstall("pcs:a", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.App", "1.2.3", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false));
        RecordingWingetProcess process = new(new WingetProcessResult(WingetExitCodes.RestartRequiredToFinish, string.Empty, string.Empty, false));

        ProviderExitAssessment outcome = await new WingetInstallExecutor(process).ExecuteAsync(job, Evidence(job), CancellationToken.None);

        CollectionAssert.AreEqual(WingetInstallExecutor.BuildInstallArguments(job, Evidence(job)).ToArray(), process.Arguments.ToArray());
        Assert.IsTrue(outcome.AwaitsVerification, "Restart-required exit still needs verification.");
        Assert.IsTrue(outcome.RestartPending);
    }

    [TestMethod]
    [DataRow(0, UpdateProviderAttemptStatus.AwaitingVerification, false)]
    [DataRow(unchecked((int)0x8A150109), UpdateProviderAttemptStatus.AwaitingVerification, true)]
    [DataRow(unchecked((int)0x8A15010A), UpdateProviderAttemptStatus.AwaitingVerification, true)]
    [DataRow(unchecked((int)0x8A15010B), UpdateProviderAttemptStatus.AwaitingVerification, true)]
    [DataRow(unchecked((int)0x8A15010D), UpdateProviderAttemptStatus.AwaitingVerification, false)]
    [DataRow(unchecked((int)0x8A150061), UpdateProviderAttemptStatus.AwaitingVerification, false)]
    [DataRow(unchecked((int)0x8A150101), UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(unchecked((int)0x8A150103), UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(unchecked((int)0x8A150041), UpdateProviderAttemptStatus.ProductFailed, false)]
    [DataRow(unchecked((int)0x8A150046), UpdateProviderAttemptStatus.SourceFailed, false)]
    [DataRow(unchecked((int)0x8A150010), UpdateProviderAttemptStatus.ProductFailed, false)]
    [DataRow(unchecked((int)0x8A15002B), UpdateProviderAttemptStatus.ProductFailed, false)]
    [DataRow(unchecked((int)0x8A15010E), UpdateProviderAttemptStatus.ProductFailed, false)]
    [DataRow(unchecked((int)0x8A150011), UpdateProviderAttemptStatus.DownloadFailed, false)]
    [DataRow(1, UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(-1, UpdateProviderAttemptStatus.InstallerFailed, false)]
    public void WingetExitCodesNeverMeanSuccess(int exitCode, UpdateProviderAttemptStatus status, bool restart)
    {
        ProviderExitAssessment assessment = WingetExitCodes.Classify(exitCode);
        Assert.AreEqual(status, assessment.Status);
        Assert.AreEqual(restart, assessment.RestartPending);
        Assert.AreEqual(UpdateProviderAttemptStatus.TimedOut, WingetExitCodes.Classify(new WingetProcessResult(0, string.Empty, string.Empty, true)).Status);
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, WingetExitCodes.Classify(new WingetProcessResult(0, string.Empty, string.Empty, false, true)).Status);
    }

    [TestMethod]
    public void ChocolateyQueryArgvSnapshots()
    {
        string[] latest = ["search", "some_package", "--exact", "--limit-output", "--no-color", "--source", "https://community.chocolatey.org/api/v2/", "--approved-only", "--not-broken", "--execution-timeout", "60"];
        string[] pinned = ["search", "firefox", "--exact", "--version", "130.0.1", "--limit-output", "--no-color", "--source", "https://community.chocolatey.org/api/v2/", "--approved-only", "--not-broken", "--execution-timeout", "60"];
        string[] local = ["list", "firefox", "--exact", "--limit-output", "--no-color"];

        CollectionAssert.AreEqual(latest, ChocolateyInstallPreflightAuthority.BuildSearchArguments("some_package", null).ToArray());
        CollectionAssert.AreEqual(pinned, ChocolateyInstallPreflightAuthority.BuildSearchArguments("firefox", "130.0.1").ToArray());
        CollectionAssert.AreEqual(local, ChocolateyInstallPreflightAuthority.BuildLocalListArguments("firefox").ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyInstallPreflightAuthority.BuildSearchArguments("firefox --source evil", null));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyInstallPreflightAuthority.BuildSearchArguments("notepad++", null));
    }

    [TestMethod]
    public void ChocolateyReadySearchProducesAMachineScopeInstall()
    {
        InstallPreflightResult result = ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), Choco(0, "Firefox|130.0.1"), Choco(0, string.Empty));

        Assert.IsTrue(result.IsActionable, result.Reason);
        Assert.AreEqual("Firefox", result.Selection!.PackageId);
        Assert.AreEqual("130.0.1", result.Selection.CandidateVersion);
        Assert.AreEqual(UpdateScope.Machine, result.Selection.Scope);
        Assert.IsTrue(result.Selection.RunsElevated);
        Assert.IsNull(result.Selection.AgreementHash);
        Assert.IsTrue(ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), Choco(0, "firefox|130.0.1"), Choco(2, string.Empty)).IsActionable, "Enhanced exit code 2 means no local results.");
    }

    [TestMethod]
    [DataRow(0, "", 0, "", "package-not-found")]
    [DataRow(2, "", 0, "", "package-not-found")]
    [DataRow(0, "firefox|130.0.1\nfirefox-nightly|131.0", 0, "", "package-not-unique")]
    [DataRow(0, "firefox|130.0.1\nfirefox|129.0", 0, "", "package-not-unique")]
    [DataRow(0, "librewolf|130.0", 0, "", "package-not-unique")]
    [DataRow(0, "firefox|130.0.1|extra", 0, "", "search-malformed-row")]
    [DataRow(0, "firefox & calc|130.0.1", 0, "", "search-malformed-row")]
    [DataRow(0, "firefox|130.0.1 --force", 0, "", "search-malformed-row")]
    [DataRow(1, "firefox|130.0.1", 0, "", "search-exit-1")]
    [DataRow(2, "firefox|130.0.1", 0, "", "search-inconsistent-exit")]
    [DataRow(0, "firefox|130.0.1", 0, "firefox|129.0", "already-installed")]
    [DataRow(0, "firefox|130.0.1", 0, "FireFox|129.0", "already-installed")]
    [DataRow(0, "firefox|130.0.1", 1, "", "local-list-exit-1")]
    [DataRow(0, "firefox|130.0.1", 0, "garbage", "local-list-malformed-row")]
    public void HostileChocolateyOutputIsNotActionable(int searchExit, string searchOutput, int localExit, string localOutput, string reason)
    {
        InstallPreflightResult result = ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), Choco(searchExit, searchOutput), Choco(localExit, localOutput));
        Assert.IsFalse(result.IsActionable);
        Assert.AreEqual(reason, result.Reason);
    }

    [TestMethod]
    public void ChocolateyTimeoutsOversizeAndRevalidationFailClosed()
    {
        Assert.AreEqual("search-timed-out", ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), new ChocolateyProcessResult(-1, "firefox|130.0.1", string.Empty, true), Choco(0, string.Empty)).Reason);
        Assert.AreEqual("search-output-too-large", ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), new ChocolateyProcessResult(0, "firefox|130.0.1", string.Empty, false, true), Choco(0, string.Empty)).Reason);
        Assert.AreEqual("version-changed", ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest() with { ExpectedVersion = "130.0.0" }, Choco(0, "firefox|130.0.1"), Choco(0, string.Empty)).Reason);
    }

    [TestMethod]
    public async Task ChocolateyRequestValidationAndClientFailure()
    {
        ChocolateyInstallPreflightAuthority throwing = new(new ThrowingChocolateyProcess());
        Assert.AreEqual("chocolatey-requires-machine-scope", (await throwing.PreflightAsync(ChocolateyRequest() with { Scope = UpdateScope.User }, CancellationToken.None)).Reason);
        Assert.AreEqual("architecture-not-supported", (await throwing.PreflightAsync(ChocolateyRequest() with { Architecture = UpdateArchitecture.X64 }, CancellationToken.None)).Reason);
        Assert.AreEqual("package-id-invalid", (await throwing.PreflightAsync(ChocolateyRequest() with { PackageId = "notepad++" }, CancellationToken.None)).Reason);
        Assert.AreEqual("provider-source-not-supported", (await throwing.PreflightAsync(ChocolateyRequest() with { Source = UpdateSource.WinGet }, CancellationToken.None)).Reason);
        Assert.AreEqual("chocolatey-client-unavailable", (await throwing.PreflightAsync(ChocolateyRequest(), CancellationToken.None)).Reason);

        RecordingChocolateyProcess recording = new(Choco(0, "firefox|130.0.1"), Choco(0, string.Empty));
        InstallPreflightResult live = await new ChocolateyInstallPreflightAuthority(recording).PreflightAsync(ChocolateyRequest(), CancellationToken.None);
        Assert.IsTrue(live.IsActionable);
        CollectionAssert.AreEqual(ChocolateyInstallPreflightAuthority.BuildSearchArguments("firefox", null).ToArray(), recording.Calls[0].ToArray());
        CollectionAssert.AreEqual(ChocolateyInstallPreflightAuthority.BuildLocalListArguments("firefox").ToArray(), recording.Calls[1].ToArray());
    }

    [TestMethod]
    public async Task ChocolateyInstallArgvSnapshotAndExecution()
    {
        ImmutableUpdateJob job = Approve(ChocolateyInstallPreflightAuthority.Assess(ChocolateyRequest(), Choco(0, "firefox|130.0.1"), Choco(0, string.Empty)).Selection!);
        string[] expected = ["install", "firefox", "--version", "130.0.1", "--source", "https://community.chocolatey.org/api/v2/", "--yes", "--limit-output", "--no-progress", "--no-color", "--execution-timeout", "300"];

        CollectionAssert.AreEqual(expected, ChocolateyInstallExecutor.BuildInstallArguments(job, Evidence(job)).ToArray());
        Assert.IsFalse(expected.Intersect(ForbiddenFlags, StringComparer.OrdinalIgnoreCase).Any());
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyInstallExecutor.BuildInstallArguments(job, new UpdateJobExecutionEvidence(job.Selection, false, true)));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyMachineUpdateExecutor.BuildUpdateArguments(job), "install jobs never reach the upgrade builder");

        RecordingChocolateyProcess process = new(new ChocolateyProcessResult(3010, string.Empty, string.Empty, false));
        ProviderExitAssessment outcome = await new ChocolateyInstallExecutor(process).ExecuteAsync(job, Evidence(job), CancellationToken.None);
        CollectionAssert.AreEqual(expected, process.Calls.Single().ToArray());
        Assert.IsTrue(outcome.AwaitsVerification);
        Assert.IsTrue(outcome.RestartPending);
    }

    [TestMethod]
    [DataRow(0, UpdateProviderAttemptStatus.AwaitingVerification, false)]
    [DataRow(1641, UpdateProviderAttemptStatus.AwaitingVerification, true)]
    [DataRow(3010, UpdateProviderAttemptStatus.AwaitingVerification, true)]
    [DataRow(350, UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(1604, UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(1, UpdateProviderAttemptStatus.InstallerFailed, false)]
    [DataRow(-1, UpdateProviderAttemptStatus.InstallerFailed, false)]
    public void ChocolateyExitCodesNeverMeanSuccess(int exitCode, UpdateProviderAttemptStatus status, bool restart)
    {
        ProviderExitAssessment assessment = ChocolateyExitCodes.Classify(exitCode);
        Assert.AreEqual(status, assessment.Status);
        Assert.AreEqual(restart, assessment.RestartPending);
    }

    [TestMethod]
    public async Task CompositeAuthorityRoutesByProviderAndFailsClosedWhenUnconfigured()
    {
        CompositeInstallPreflightAuthority composite = new(new Dictionary<UpdateProvider, IInstallPreflightAuthority>
        {
            [UpdateProvider.WinGet] = new WingetInstallPreflightAuthority(new FixedQuery(Observation()), TimeSpan.FromSeconds(1))
        });
        Assert.IsTrue((await composite.PreflightAsync(WingetRequest(), CancellationToken.None)).IsActionable);
        Assert.AreEqual("provider-not-configured", (await composite.PreflightAsync(ChocolateyRequest(), CancellationToken.None)).Reason);
    }

    private static InstallPreflightRequest WingetRequest(UpdateScope scope = UpdateScope.User, UpdateArchitecture architecture = UpdateArchitecture.X64) =>
        new("pcs:notepadpp", UpdateProvider.WinGet, UpdateSource.WinGet, "notepad++.notepad++", scope, architecture);

    private static InstallPreflightRequest ChocolateyRequest() =>
        new("pcs:firefox", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "firefox", UpdateScope.Machine, UpdateArchitecture.Neutral);

    private static WinGetInstallObservation Observation() => new(
        "winget", true, true, true, 1, false, "Notepad++.Notepad++", false, "8.8.3", "winget", string.Empty,
        new WinGetApplicableInstaller(UpdateScope.User, UpdateArchitecture.X64, InstallerElevation.Unknown, "Nullsoft"),
        [Licence]);

    private static ChocolateyProcessResult Choco(int exitCode, string output) => new(exitCode, output, string.Empty, false);

    private static ImmutableUpdateJob Approve(UpdateJobSelection selection) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(ApprovedAt);

    private static UpdateJobExecutionEvidence Evidence(ImmutableUpdateJob job) => new(job.Selection, true, true);

    private sealed class FixedQuery(WinGetInstallObservation? observation) : IWinGetInstallCatalogQuery
    {
        public Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken) => Task.FromResult(observation);
    }

    private sealed class ThrowingQuery : IWinGetInstallCatalogQuery
    {
        public Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken) =>
            Task.FromException<WinGetInstallObservation?>(new InvalidOperationException("COM failure"));
    }

    private sealed class StalledQuery : IWinGetInstallCatalogQuery
    {
        public async Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class RecordingWingetProcess(WingetProcessResult result) : IWingetProcess
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Arguments = arguments; return Task.FromResult(result); }
    }

    private sealed class RecordingChocolateyProcess(params ChocolateyProcessResult[] results) : IChocolateyProcess
    {
        private readonly Queue<ChocolateyProcessResult> results = new(results);
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Calls.Add(arguments); return Task.FromResult(results.Dequeue()); }
    }

    private sealed class ThrowingChocolateyProcess : IChocolateyProcess
    {
        public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromException<ChocolateyProcessResult>(new IOException("The Chocolatey client is not installed in a trusted protected layout."));
    }
}
