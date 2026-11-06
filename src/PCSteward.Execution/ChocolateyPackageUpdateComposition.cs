using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; Chocolatey package Confirm addendum §5: the single production composition of the
/// reviewed dispatch pipeline for Chocolatey Community package updates (<see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>).
/// It is constructed once, in the Desktop's <c>App.xaml.cs</c>, and exposes only <see cref="CreateSession"/>; the workflow, the
/// dispatcher, the transport, the capability reader and the producers never leave it (architecture-pinned).
/// <para>
/// Each session gets its own pieces, built here and nowhere else: the N6 producer composition and the intent resolver behind their
/// public façade (<see cref="ChocolateyPackageUpdateProducers"/>), the protected deployment-capability reader, the protocol-2
/// transport over that reader, the route dispatcher, and one <see cref="ReviewedUpdateDispatchWorkflow"/> over the common
/// current-user gate and the durable store. The dispatcher is wrapped so that this composition can only ever dispatch the exact
/// package shape (<see cref="PackageRouteOnlyDispatcher"/>): the client self-update is composed only by
/// <see cref="ChocolateyClientUpdateComposition"/>, and WinGet is not touched.
/// </para>
/// Nothing here deletes, repairs or reconfigures anything in Chocolatey's folder or settings, starts background work, or holds an
/// elevated process: the only elevation is the one-off helper the transport launches for one confirmed job.
/// </summary>
public sealed class ChocolateyPackageUpdateComposition
{
    private readonly ReviewedUpdateGateRunner runUnderGate;
    private readonly IUpdateDispatchStore store;
    private readonly Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;

    /// <param name="runUnderGate">The common current-user gate with mandatory reconciliation (the Desktop's data-operation lease).</param>
    /// <param name="store">The durable dispatch store (claim, finalization, barrier).</param>
    /// <param name="saveDraft">Saves the frozen operation under the data-operation lease and returns its draft.</param>
    /// <param name="readHistory">Reads recent reviewed-update history under the lease; null when it cannot be read.</param>
    public ChocolateyPackageUpdateComposition(ReviewedUpdateGateRunner runUnderGate, IUpdateDispatchStore store,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory)
    {
        this.runUnderGate = runUnderGate ?? throw new ArgumentNullException(nameof(runUnderGate));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.saveDraft = saveDraft ?? throw new ArgumentNullException(nameof(saveDraft));
        this.readHistory = readHistory ?? throw new ArgumentNullException(nameof(readHistory));
    }

    /// <summary>
    /// One new review session over its own freshly composed pipeline. Nothing is checked, saved or launched until the session is
    /// prepared and confirmed; the session refuses everything while owner admission is not compiled in.
    /// </summary>
    public ChocolateyPackageUpdateReviewSession CreateSession()
    {
        ChocolateyPackageUpdateProducers producers = new();
        OwnerOnlyDeploymentCapabilityReader capability = new();
        WindowsReviewedHelperTransport transport = new(capability);
        ChocolateyClientUpdatePhaseSignal phases = new();
        PackageRouteOnlyDispatcher dispatcher = new(new ChocolateyRuntimeLimitsRouteDispatcher(capability, transport), phases);
        ReviewedUpdateDispatchWorkflow workflow = new(runUnderGate, producers.EvidenceSource, dispatcher, producers.Verifier, store);
        return new ChocolateyPackageUpdateReviewSession(producers.CheckReviewIntentAsync, saveDraft, readHistory, workflow, phases,
            ChocolateyReviewedRouteProbe.ProbeSplitTokenState);
    }
}

/// <summary>
/// Chocolatey package Confirm addendum §5: the composed dispatcher admits only the exact package shape (the runtime-limit route,
/// its guard contract, never a Chocolatey client identity), then defers to the route dispatcher, whose <c>Supports</c> also checks
/// the protected deployment capability. It reports the progress points around the one exchange. It adds no authority: every
/// refusal stays a refusal.
/// </summary>
internal sealed class PackageRouteOnlyDispatcher(IReviewedUpdateRouteDispatcher inner, ChocolateyClientUpdatePhaseSignal phases) : IReviewedUpdateRouteDispatcher
{
    private readonly IReviewedUpdateRouteDispatcher inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ChocolateyClientUpdatePhaseSignal phases = phases ?? throw new ArgumentNullException(nameof(phases));

    public bool Supports(ReviewedUpdateOperation operation)
    {
        try { return IsPackageRoute(operation) && inner.Supports(operation); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    public async Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!IsPackageRoute(ticket.Operation))
            return ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, ChocolateyRuntimeLimitsRouteDispatcher.RouteUnsupportedDiagnostic);
        phases.Report(ChocolateyClientUpdatePhase.WaitingForPermission);
        try { return await inner.DispatchAsync(ticket, cancellationToken).ConfigureAwait(false); }
        finally { phases.Report(ChocolateyClientUpdatePhase.Verifying); }
    }

    internal static bool IsPackageRoute(ReviewedUpdateOperation? operation) =>
        operation is not null && operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&
        operation.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits &&
        !ReviewedUpdateOperation.IsChocolateyClientIdentity(operation.Selection.PackageId);
}
