using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The installer's declared elevation behaviour (WinGet <c>ElevationRequirement</c>, spike §2.3).</summary>
public enum InstallerElevation { Unknown, ElevationRequired, ElevationProhibited, ElevatesSelf }

/// <summary>
/// FR-INS-02: a live, exact install resolution request. The package ID comes from a validated catalog entry
/// and is still only a query: the provider must answer with exactly one trusted match. <see cref="ExpectedVersion"/>
/// is set when an approved job is revalidated immediately before execution; the agreement hash must then
/// match <see cref="ExpectedAgreementHash"/> exactly (TM-22).
/// </summary>
public sealed record InstallPreflightRequest(
    string CatalogEntryKey,
    UpdateProvider Provider,
    UpdateSource Source,
    string PackageId,
    UpdateScope Scope,
    UpdateArchitecture Architecture,
    UpdateChannel Channel = UpdateChannel.Stable,
    string? ExpectedVersion = null,
    string? ExpectedAgreementHash = null)
{
    public bool IsRevalidation => ExpectedVersion is not null;

    /// <summary>The fresh-evidence request for an approved Install job.</summary>
    public static InstallPreflightRequest ForApprovedJob(ImmutableUpdateJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Operation != PackageOperationKind.Install) throw new ArgumentException("Only install jobs are revalidated by install preflight.", nameof(job));
        UpdateJobSelection selection = job.Selection;
        return new(selection.StableProductKey, selection.Provider, selection.Source, selection.PackageId, selection.Scope, selection.Architecture, selection.Channel, selection.CandidateVersion, selection.AgreementHash);
    }
}

/// <summary>
/// Preflight outcome. Only an actionable result carries a selection; the reason is a stable code for the
/// review sheet and diagnostics. Agreements are returned for display (FR-INS-03) and are hashed into the selection.
/// </summary>
public sealed record InstallPreflightResult(
    bool IsActionable,
    string Reason,
    UpdateJobSelection? Selection,
    IReadOnlyList<PackageAgreementText> Agreements,
    InstallerElevation Elevation)
{
    public const string Ready = "ready";

    public static InstallPreflightResult NotActionable(string reason) => new(false, reason, null, [], InstallerElevation.Unknown);

    public static InstallPreflightResult Actionable(UpdateJobSelection selection, IReadOnlyList<PackageAgreementText> agreements, InstallerElevation elevation) =>
        new(true, Ready, selection ?? throw new ArgumentNullException(nameof(selection)), agreements, elevation);

    /// <summary>Fresh evidence for <see cref="ImmutableUpdateJob.Matches"/>; non-actionable evidence never matches.</summary>
    public UpdateJobExecutionEvidence? AsExecutionEvidence() => Selection is null ? null : new(Selection, IsActionable, true);
}

/// <summary>Result of <see cref="InventoryWorkflow.PrepareInstallSelectionAsync"/>. Only an actionable preflight offers a selection.</summary>
public sealed record InstallSelectionPreparation(InstallPreflightResult? Preflight, string Message)
{
    public UpdateJobSelection? Selection => Preflight is { IsActionable: true } ? Preflight.Selection : null;
}

public interface IInstallPreflightAuthority
{
    Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken);
}

/// <summary>CRD-03 / FR-INS-04: provider-owned, live applicable choices, never approval.</summary>
public interface IInstallChoiceAuthority
{
    Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken);
}

/// <summary>Inert Task 2 test-first seam; provider routing is implemented after the assertion-red gate.</summary>
public sealed class CompositeInstallChoiceAuthority : IInstallChoiceAuthority
{
    public CompositeInstallChoiceAuthority(IReadOnlyDictionary<CatalogProviderKind, IInstallChoiceAuthority> authorities) => ArgumentNullException.ThrowIfNull(authorities);

    public Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstallPreflightResult>>([]);
}

/// <summary>Routes a request to the authority for its provider; an unconfigured provider is not actionable.</summary>
public sealed class CompositeInstallPreflightAuthority(IReadOnlyDictionary<UpdateProvider, IInstallPreflightAuthority> authorities) : IInstallPreflightAuthority
{
    private readonly IReadOnlyDictionary<UpdateProvider, IInstallPreflightAuthority> authorities = authorities ?? throw new ArgumentNullException(nameof(authorities));

    public Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return authorities.TryGetValue(request.Provider, out IInstallPreflightAuthority? authority)
            ? authority.PreflightAsync(request, cancellationToken)
            : Task.FromResult(InstallPreflightResult.NotActionable("provider-not-configured"));
    }
}

/// <summary>
/// Provider exit evidence mapped onto the history taxonomy (FR-HIS-03). A completed exit only ever leads to
/// verification; <see cref="RestartPending"/> records a provider-reported reboot requirement for the verifier.
/// </summary>
public sealed record ProviderExitAssessment(UpdateProviderAttemptStatus Status, bool RestartPending, string Diagnostic, int? ExitCode = null)
{
    public bool AwaitsVerification => Status == UpdateProviderAttemptStatus.AwaitingVerification;
}
