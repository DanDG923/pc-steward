using System.Globalization;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-INS-02: live install preflight against the fixed Chocolatey Community feed (spike §3.2) through the
/// trusted client only. It needs exactly one approved, not-broken <c>id|version</c> row for the exact ID and
/// no local registration of that ID. A version still in moderation is therefore not actionable.
/// </summary>
public sealed class ChocolateyInstallPreflightAuthority : IInstallPreflightAuthority
{
    public const string EvidenceVersion = "chocolatey-fixed-community-v1";
    private const int NoResultsExitCode = 2;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);
    private readonly IChocolateyProcess process;

    public ChocolateyInstallPreflightAuthority() : this(new WindowsChocolateyProcess()) { }
    internal ChocolateyInstallPreflightAuthority(IChocolateyProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));

    public static IReadOnlyList<string> BuildSearchArguments(string packageId, string? version)
    {
        if (!ChocolateyLocalInventoryProvider.ValidPackageId(packageId)) throw new ArgumentException("The package ID is outside the allowlisted format.", nameof(packageId));
        if (version is not null && !ChocolateyLocalInventoryProvider.ValidVersion(version)) throw new ArgumentException("The version is outside the allowlisted format.", nameof(version));
        List<string> arguments = ["search", packageId, "--exact"];
        if (version is not null) arguments.AddRange(["--version", version]);
        arguments.AddRange(["--limit-output", "--no-color", "--source", ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--approved-only", "--not-broken", "--execution-timeout", "60"]);
        return arguments;
    }

    public static IReadOnlyList<string> BuildLocalListArguments(string packageId) =>
        ChocolateyLocalInventoryProvider.ValidPackageId(packageId)
            ? ["list", packageId, "--exact", "--limit-output", "--no-color"]
            : throw new ArgumentException("The package ID is outside the allowlisted format.", nameof(packageId));

    public async Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Provider != UpdateProvider.Chocolatey || request.Source != UpdateSource.ChocolateyCommunity) return InstallPreflightResult.NotActionable("provider-source-not-supported");
        if (request.Scope != UpdateScope.Machine) return InstallPreflightResult.NotActionable("chocolatey-requires-machine-scope");
        if (request.Architecture != UpdateArchitecture.Neutral) return InstallPreflightResult.NotActionable("architecture-not-supported");
        if (request.Channel != UpdateChannel.Stable) return InstallPreflightResult.NotActionable("channel-not-stable");
        if (request.ExpectedAgreementHash is not null) return InstallPreflightResult.NotActionable("agreements-changed");
        if (!ChocolateyLocalInventoryProvider.ValidPackageId(request.PackageId)) return InstallPreflightResult.NotActionable("package-id-invalid");
        if (request.ExpectedVersion is not null && !ChocolateyLocalInventoryProvider.ValidVersion(request.ExpectedVersion)) return InstallPreflightResult.NotActionable("version-invalid");

        ChocolateyProcessResult search;
        ChocolateyProcessResult local;
        try
        {
            search = await process.RunAsync(BuildSearchArguments(request.PackageId, request.ExpectedVersion), QueryTimeout, cancellationToken).ConfigureAwait(false);
            local = await process.RunAsync(BuildLocalListArguments(request.PackageId), QueryTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Includes an absent or untrusted client: the trusted-client gate failed closed.
            return InstallPreflightResult.NotActionable("chocolatey-client-unavailable");
        }

        return Assess(request, search, local);
    }

    internal static InstallPreflightResult Assess(InstallPreflightRequest request, ChocolateyProcessResult search, ChocolateyProcessResult local)
    {
        if (!TryReadRows(search, out List<string[]> rows, out string searchFailure)) return InstallPreflightResult.NotActionable("search-" + searchFailure);
        string[][] exact = [.. rows.Where(row => string.Equals(row[0], request.PackageId, StringComparison.OrdinalIgnoreCase))];
        if (rows.Count == 0) return InstallPreflightResult.NotActionable("package-not-found");
        if (exact.Length != 1 || rows.Count != 1) return InstallPreflightResult.NotActionable("package-not-unique");
        string id = exact[0][0];
        string version = exact[0][1];
        if (request.ExpectedVersion is not null && !string.Equals(version, request.ExpectedVersion, StringComparison.Ordinal)) return InstallPreflightResult.NotActionable("version-changed");

        if (!TryReadRows(local, out List<string[]> installed, out string localFailure)) return InstallPreflightResult.NotActionable("local-list-" + localFailure);
        if (installed.Any(row => string.Equals(row[0], request.PackageId, StringComparison.OrdinalIgnoreCase))) return InstallPreflightResult.NotActionable("already-installed");

        try
        {
            UpdateJobSelection selection = UpdateJobSelection.ForInstall(
                request.CatalogEntryKey,
                UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity,
                id,
                version,
                UpdateScope.Machine,
                UpdateArchitecture.Neutral,
                UpdateChannel.Stable,
                EvidenceVersion,
                null,
                installerElevates: true);
            return InstallPreflightResult.Actionable(selection, [], InstallerElevation.ElevationRequired);
        }
        catch (ArgumentException)
        {
            return InstallPreflightResult.NotActionable("evidence-outside-allowlist");
        }
    }

    /// <summary>Strict <c>id|version</c> rows. Any malformed row rejects the whole result rather than being skipped.</summary>
    private static bool TryReadRows(ChocolateyProcessResult result, out List<string[]> rows, out string failure)
    {
        rows = [];
        if (result.OutputLimitExceeded || result.StandardOutput.Length > ChocolateyUpdateSourceConnector.MaximumOutputLength) { failure = "output-too-large"; return false; }
        if (result.TimedOut) { failure = "timed-out"; return false; }
        if (result.ExitCode is not (0 or NoResultsExitCode)) { failure = "exit-" + result.ExitCode.ToString(CultureInfo.InvariantCulture); return false; }
        foreach (string line in ChocolateyLocalInventoryProvider.Lines(result.StandardOutput))
        {
            string[] columns = line.Split('|');
            if (columns.Length != 2 || !ChocolateyLocalInventoryProvider.ValidPackageId(columns[0]) || !ChocolateyLocalInventoryProvider.ValidVersion(columns[1])) { failure = "malformed-row"; return false; }
            rows.Add(columns);
        }

        if (result.ExitCode == NoResultsExitCode && rows.Count != 0) { failure = "inconsistent-exit"; return false; }
        failure = string.Empty;
        return true;
    }
}

/// <summary>FR-INS-01: provider-owned machine-scope argv for an approved Chocolatey Community Install.</summary>
public sealed class ChocolateyInstallExecutor
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);
    private readonly IChocolateyProcess process;

    public ChocolateyInstallExecutor() : this(new WindowsChocolateyProcess()) { }
    internal ChocolateyInstallExecutor(IChocolateyProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));

    public async Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments = BuildInstallArguments(job, freshEvidence);
        ChocolateyProcessResult result = await process.RunAsync(arguments, InstallTimeout, cancellationToken).ConfigureAwait(false);
        return ChocolateyExitCodes.Classify(result);
    }

    public static IReadOnlyList<string> BuildInstallArguments(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(freshEvidence);
        if (!job.HasValidDigest) throw new ArgumentException("The job failed its integrity check.", nameof(job));
        UpdateJobSelection selection = job.Selection;
        if (selection.Operation != PackageOperationKind.Install) throw new ArgumentException("Only install jobs use the install builder.", nameof(job));
        if (selection.Provider != UpdateProvider.Chocolatey || selection.Source != UpdateSource.ChocolateyCommunity || selection.Scope != UpdateScope.Machine) throw new ArgumentException("Only machine-scope Chocolatey Community jobs are supported.", nameof(job));
        if (!job.Matches(freshEvidence)) throw new ArgumentException("Fresh preflight evidence does not match the approved job.", nameof(freshEvidence));
        return ["install", selection.PackageId, "--version", selection.CandidateVersion, "--source", ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--yes", "--limit-output", "--no-progress", "--no-color", "--execution-timeout", "300"];
    }
}

/// <summary>Chocolatey exit codes (spike §3.1). Package-specific codes are not trusted.</summary>
public static class ChocolateyExitCodes
{
    public static ProviderExitAssessment Classify(ChocolateyProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TimedOut) return new(UpdateProviderAttemptStatus.TimedOut, false, "provider-timed-out");
        if (result.OutputLimitExceeded) return new(UpdateProviderAttemptStatus.ProductFailed, false, "provider-output-limit-exceeded");
        return Classify(result.ExitCode);
    }

    public static ProviderExitAssessment Classify(int exitCode) => exitCode switch
    {
        0 => new(UpdateProviderAttemptStatus.AwaitingVerification, false, "provider-completed; verification-required"),
        1641 or 3010 => new(UpdateProviderAttemptStatus.AwaitingVerification, true, "provider-reported-restart-required; verification-required"),
        350 => new(UpdateProviderAttemptStatus.InstallerFailed, false, "pending-reboot-detected; nothing-installed"),
        1604 => new(UpdateProviderAttemptStatus.InstallerFailed, false, "install-suspended"),
        _ => new(UpdateProviderAttemptStatus.InstallerFailed, false, "provider-exit-" + exitCode.ToString(CultureInfo.InvariantCulture))
    };
}
