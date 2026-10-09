using System.Text.RegularExpressions;
using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// FR-INS-02/03/04: live install preflight through WinGet's structured COM API (ADR-003 v0.3 amendment).
/// It requires one exact match in the trusted predefined <c>winget</c> catalog, no installed instance, an
/// applicable installer for the requested scope and architecture, and bounded agreements, which it hashes.
/// </summary>
public sealed partial class WingetInstallPreflightAuthority : IInstallPreflightAuthority
{
    public const string EvidenceVersion = "winget-com-v1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IWinGetInstallCatalogQuery query;
    private readonly TimeSpan timeout;

    public WingetInstallPreflightAuthority() : this(new WinGetComInstallCatalogQuery(), DefaultTimeout) { }

    internal WingetInstallPreflightAuthority(IWinGetInstallCatalogQuery query, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        this.query = query;
        this.timeout = timeout;
    }

    public async Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Provider != UpdateProvider.WinGet || request.Source != UpdateSource.WinGet) return InstallPreflightResult.NotActionable("provider-source-not-supported");
        if (request.Architecture is not (UpdateArchitecture.X64 or UpdateArchitecture.X86 or UpdateArchitecture.Arm64)) return InstallPreflightResult.NotActionable("architecture-not-supported");
        if (!PackageId().IsMatch(request.PackageId)) return InstallPreflightResult.NotActionable("package-id-invalid");
        if (request.ExpectedVersion is not null && !Version().IsMatch(request.ExpectedVersion)) return InstallPreflightResult.NotActionable("version-invalid");
        cancellationToken.ThrowIfCancellationRequested();

        WinGetInstallObservation? observation;
        try
        {
            using CancellationTokenSource queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queryTimeout.CancelAfter(timeout);
            Task<WinGetInstallObservation?> queryTask = Task.Run(() => query.QueryAsync(request.PackageId, request.ExpectedVersion, request.Scope, request.Architecture, queryTimeout.Token), CancellationToken.None);
            observation = await queryTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return InstallPreflightResult.NotActionable("provider-timed-out");
        }
        catch (Exception)
        {
            return InstallPreflightResult.NotActionable("provider-unavailable");
        }

        return Assess(request, observation);
    }

    internal static InstallPreflightResult Assess(InstallPreflightRequest request, WinGetInstallObservation? observation)
    {
        if (observation is null) return InstallPreflightResult.NotActionable("provider-unavailable");
        if (!observation.CatalogIsPredefined || !observation.CatalogIsTrusted || !string.Equals(observation.SourceId, "winget", StringComparison.Ordinal)) return InstallPreflightResult.NotActionable("source-not-trusted");
        if (observation.WasLimitExceeded || observation.MatchCount != 1 || !observation.ExactIdMatch) return InstallPreflightResult.NotActionable("package-not-unique");
        if (!PackageId().IsMatch(observation.PackageId) || !string.Equals(observation.PackageId, request.PackageId, StringComparison.OrdinalIgnoreCase)) return InstallPreflightResult.NotActionable("package-mismatch");
        if (observation.IsInstalled) return InstallPreflightResult.NotActionable("already-installed");
        if (observation.Version is null || !Version().IsMatch(observation.Version)) return InstallPreflightResult.NotActionable("version-unavailable");
        if (request.ExpectedVersion is not null && !string.Equals(observation.Version, request.ExpectedVersion, StringComparison.Ordinal)) return InstallPreflightResult.NotActionable("version-changed");
        if (!string.Equals(observation.VersionSourceId, "winget", StringComparison.Ordinal)) return InstallPreflightResult.NotActionable("source-mismatch");
        if (!string.IsNullOrEmpty(observation.Channel) || request.Channel != UpdateChannel.Stable) return InstallPreflightResult.NotActionable("channel-not-stable");
        if (observation.Installer is not { } installer) return InstallPreflightResult.NotActionable("no-applicable-installer");

        // WinGet chose the installer under a strict scope filter; a declared scope must still agree.
        bool scopeCompatible = installer.Scope is null || installer.Scope == request.Scope;
        if (!scopeCompatible) return InstallPreflightResult.NotActionable("scope-conflict");
        if (installer.Architecture != request.Architecture && installer.Architecture != UpdateArchitecture.Neutral) return InstallPreflightResult.NotActionable("architecture-conflict");
        if (installer.Elevation == InstallerElevation.ElevationProhibited && request.Scope == UpdateScope.Machine) return InstallPreflightResult.NotActionable("elevation-prohibited-for-machine-scope");

        if (!PackageAgreementDigest.TryCompute(observation.Agreements, out string? agreementHash, out string rejection)) return InstallPreflightResult.NotActionable(rejection);
        if (request.IsRevalidation && !string.Equals(agreementHash, request.ExpectedAgreementHash, StringComparison.Ordinal)) return InstallPreflightResult.NotActionable("agreements-changed");

        bool installerElevates = installer.Elevation is InstallerElevation.ElevationRequired or InstallerElevation.ElevatesSelf;
        try
        {
            UpdateJobSelection selection = UpdateJobSelection.ForInstall(
                request.CatalogEntryKey,
                UpdateProvider.WinGet,
                UpdateSource.WinGet,
                observation.PackageId,
                observation.Version,
                request.Scope,
                request.Architecture,
                UpdateChannel.Stable,
                EvidenceVersion,
                agreementHash,
                installerElevates);
            return InstallPreflightResult.Actionable(selection, observation.Agreements!, installer.Elevation);
        }
        catch (ArgumentException)
        {
            return InstallPreflightResult.NotActionable("evidence-outside-allowlist");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+_-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}

internal interface IWinGetInstallCatalogQuery
{
    Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken);
}

internal sealed record WinGetApplicableInstaller(UpdateScope? Scope, UpdateArchitecture Architecture, InstallerElevation Elevation, string InstallerType);

/// <summary>Structured COM evidence. Every string is provider output and is validated by <see cref="WingetInstallPreflightAuthority.Assess"/>.</summary>
internal sealed record WinGetInstallObservation(
    string SourceId,
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    bool IsInstalled,
    string? Version,
    string? VersionSourceId,
    string? Channel,
    WinGetApplicableInstaller? Installer,
    IReadOnlyList<PackageAgreementText>? Agreements);

/// <summary>
/// Reads install evidence from the in-process WinGet COM engine. It sets only <c>PackageInstallScope</c>,
/// <c>PackageInstallMode</c> and <c>AllowedArchitectures</c> on <see cref="InstallOptions"/>; the members listed
/// in spike §2.2 are forbidden and an architecture test enforces that.
/// </summary>
internal sealed class WinGetComInstallCatalogQuery : IWinGetInstallCatalogQuery
{
    private const int MaximumAgreements = PackageAgreementDigest.MaximumAgreements + 1;

    public async Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken)
    {
        PackageManager manager = new();
        PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
        PackageCatalogInfo referenceInfo = reference.Info;
        bool predefined = referenceInfo.Origin == PackageCatalogOrigin.Predefined && string.Equals(referenceInfo.Name, "winget", StringComparison.OrdinalIgnoreCase);
        bool trusted = referenceInfo.TrustLevel == PackageCatalogTrustLevel.Trusted;

        CreateCompositePackageCatalogOptions compositeOptions = new()
        {
            CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
            InstalledScope = PackageInstallScope.Any
        };
        compositeOptions.Catalogs.Add(reference);
        ConnectResult connection = await manager.CreateCompositePackageCatalog(compositeOptions).ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) return null;

        FindPackagesOptions options = new() { ResultLimit = 2 };
        options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = packageId });
        FindPackagesResult result = await connection.PackageCatalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok) return null;

        MatchResult[] matches = [.. result.Matches];
        MatchResult? only = matches.Length == 1 ? matches[0] : null;
        CatalogPackage? package = only?.CatalogPackage;
        if (only is null || package is null)
        {
            return new("winget", predefined, trusted, false, matches.Length, result.WasLimitExceeded, string.Empty, false, null, null, null, null, null);
        }

        PackageMatchFilter criteria = only.MatchCriteria;
        bool exact = criteria.Field == PackageMatchField.Id && criteria.Option == PackageFieldMatchOption.Equals && string.Equals(criteria.Value, packageId, StringComparison.OrdinalIgnoreCase);
        PackageVersionInfo? version = expectedVersion is null
            ? package.DefaultInstallVersion
            : package.AvailableVersions.Where(available => string.Equals(available.Version, expectedVersion, StringComparison.Ordinal)).Select(package.GetPackageVersionInfo).FirstOrDefault();
        if (version is null)
        {
            return new("winget", predefined, trusted, exact, matches.Length, result.WasLimitExceeded, package.Id, package.InstalledVersion is not null, null, null, null, null, null);
        }

        InstallOptions installOptions = new()
        {
            PackageInstallScope = scope == UpdateScope.Machine ? PackageInstallScope.System : PackageInstallScope.User,
            PackageInstallMode = PackageInstallMode.Silent
        };
        installOptions.AllowedArchitectures.Add(ToProcessorArchitecture(architecture));
        WinGetApplicableInstaller? installer = null;
        if (version.HasApplicableInstaller(installOptions))
        {
            PackageInstallerInfo info = version.GetApplicableInstaller(installOptions);
            installer = new(
                info.Scope switch { PackageInstallerScope.User => UpdateScope.User, PackageInstallerScope.System => UpdateScope.Machine, _ => null },
                FromProcessorArchitecture(info.Architecture),
                info.ElevationRequirement switch
                {
                    ElevationRequirement.ElevationRequired => InstallerElevation.ElevationRequired,
                    ElevationRequirement.ElevationProhibited => InstallerElevation.ElevationProhibited,
                    ElevationRequirement.ElevatesSelf => InstallerElevation.ElevatesSelf,
                    _ => InstallerElevation.Unknown
                },
                info.InstallerType.ToString());
        }

        // One more than the bound is read so that an oversized list is rejected rather than truncated.
        PackageAgreementText[] agreements = [.. version.GetCatalogPackageMetadata().Agreements.Take(MaximumAgreements).Select(agreement => new PackageAgreementText(agreement.Label ?? string.Empty, agreement.Text ?? string.Empty, agreement.Url))];
        return new(
            "winget",
            predefined,
            trusted,
            exact,
            matches.Length,
            result.WasLimitExceeded,
            package.Id,
            package.InstalledVersion is not null,
            version.Version,
            version.PackageCatalog?.Info?.Name,
            version.Channel,
            installer,
            agreements);
    }

    private static Windows.System.ProcessorArchitecture ToProcessorArchitecture(UpdateArchitecture architecture) => architecture switch
    {
        UpdateArchitecture.X64 => Windows.System.ProcessorArchitecture.X64,
        UpdateArchitecture.X86 => Windows.System.ProcessorArchitecture.X86,
        UpdateArchitecture.Arm64 => Windows.System.ProcessorArchitecture.Arm64,
        _ => throw new ArgumentOutOfRangeException(nameof(architecture))
    };

    private static UpdateArchitecture FromProcessorArchitecture(Windows.System.ProcessorArchitecture architecture) => architecture switch
    {
        Windows.System.ProcessorArchitecture.X64 => UpdateArchitecture.X64,
        Windows.System.ProcessorArchitecture.X86 => UpdateArchitecture.X86,
        Windows.System.ProcessorArchitecture.Arm64 => UpdateArchitecture.Arm64,
        Windows.System.ProcessorArchitecture.Neutral => UpdateArchitecture.Neutral,
        // Anything else (Arm, X86OnArm64, Unknown) never matches a request and fails closed.
        _ => (UpdateArchitecture)(-1)
    };
}

/// <summary>
/// FR-INS-01: provider-owned argv for an approved WinGet Install. The CLI is the v0.3 execution route until
/// the elevated-COM evidence in spike §2.4 exists (ADR-003 amendment). Every value comes from the immutable job.
/// </summary>
public sealed class WingetInstallExecutor
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);
    private readonly IWingetProcess process;

    public WingetInstallExecutor() : this(new WindowsWingetProcess()) { }
    internal WingetInstallExecutor(IWingetProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));

    public async Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments = BuildInstallArguments(job, freshEvidence);
        WingetProcessResult result = await process.RunAsync(arguments, InstallTimeout, cancellationToken).ConfigureAwait(false);
        return WingetExitCodes.Classify(result);
    }

    /// <summary>
    /// <c>--accept-package-agreements</c> is added only when the job carries the hash of agreements the user
    /// accepted and fresh preflight evidence carries the same hash (TM-22). Source agreements are never accepted.
    /// </summary>
    public static IReadOnlyList<string> BuildInstallArguments(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(freshEvidence);
        if (!job.HasValidDigest) throw new ArgumentException("The job failed its integrity check.", nameof(job));
        UpdateJobSelection selection = job.Selection;
        if (selection.Operation != PackageOperationKind.Install) throw new ArgumentException("Only install jobs use the install builder.", nameof(job));
        if (selection.Provider != UpdateProvider.WinGet || selection.Source != UpdateSource.WinGet) throw new ArgumentException("Only WinGet-source jobs are supported.", nameof(job));
        if (!job.Matches(freshEvidence)) throw new ArgumentException("Fresh preflight evidence does not match the approved job.", nameof(freshEvidence));
        List<string> arguments =
        [
            "install",
            "--id", selection.PackageId,
            "--exact",
            "--version", selection.CandidateVersion,
            "--source", "winget",
            "--scope", selection.Scope == UpdateScope.Machine ? "machine" : "user",
            "--architecture", Architecture(selection.Architecture),
            "--silent",
            "--disable-interactivity"
        ];
        if (selection.AgreementHash is not null) arguments.Add("--accept-package-agreements");
        return arguments;
    }

    private static string Architecture(UpdateArchitecture architecture) => architecture switch
    {
        UpdateArchitecture.X64 => "x64",
        UpdateArchitecture.X86 => "x86",
        UpdateArchitecture.Arm64 => "arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture))
    };
}

/// <summary>WinGet return codes (winget-cli <c>returnCodes.md</c>, spike §2.6) mapped onto the history taxonomy.</summary>
public static class WingetExitCodes
{
    public const int PackageInUse = unchecked((int)0x8A150101);
    public const int FileInUse = unchecked((int)0x8A150103);
    public const int RestartRequiredToFinish = unchecked((int)0x8A150109);
    public const int RestartRequiredForInstall = unchecked((int)0x8A15010A);
    public const int RestartInitiated = unchecked((int)0x8A15010B);
    public const int PackageAgreementsNotAccepted = unchecked((int)0x8A150041);
    public const int SourceAgreementsNotAccepted = unchecked((int)0x8A150046);
    public const int AlreadyInstalled = unchecked((int)0x8A15010D);
    public const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    public const int Downgrade = unchecked((int)0x8A15010E);
    public const int NoApplicableUpdate = unchecked((int)0x8A15002B);
    public const int NoApplicableInstaller = unchecked((int)0x8A150010);
    public const int InstallerHashMismatch = unchecked((int)0x8A150011);

    public static ProviderExitAssessment Classify(WingetProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TimedOut) return new(UpdateProviderAttemptStatus.TimedOut, false, "provider-timed-out");
        if (result.OutputLimitExceeded) return new(UpdateProviderAttemptStatus.ProductFailed, false, "provider-output-limit-exceeded");
        return Classify(result.ExitCode);
    }

    public static ProviderExitAssessment Classify(int exitCode) => exitCode switch
    {
        0 => new(UpdateProviderAttemptStatus.AwaitingVerification, false, "provider-completed; verification-required"),
        RestartRequiredToFinish or RestartRequiredForInstall or RestartInitiated => new(UpdateProviderAttemptStatus.AwaitingVerification, true, "provider-reported-restart-required; verification-required"),
        // The installed state is what verification proves; "already installed" is never Success by itself.
        AlreadyInstalled or PackageAlreadyInstalled => new(UpdateProviderAttemptStatus.AwaitingVerification, false, "provider-reported-already-installed; verification-required"),
        PackageInUse or FileInUse => new(UpdateProviderAttemptStatus.InstallerFailed, false, "package-or-file-in-use"),
        PackageAgreementsNotAccepted => new(UpdateProviderAttemptStatus.ProductFailed, false, "package-agreements-not-accepted"),
        SourceAgreementsNotAccepted => new(UpdateProviderAttemptStatus.SourceFailed, false, "source-agreements-not-accepted"),
        NoApplicableInstaller or NoApplicableUpdate => new(UpdateProviderAttemptStatus.ProductFailed, false, "no-applicable-installer"),
        Downgrade => new(UpdateProviderAttemptStatus.ProductFailed, false, "downgrade-refused"),
        InstallerHashMismatch => new(UpdateProviderAttemptStatus.DownloadFailed, false, "installer-hash-mismatch"),
        _ => new(UpdateProviderAttemptStatus.InstallerFailed, false, "provider-exit-0x" + exitCode.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))
    };
}
