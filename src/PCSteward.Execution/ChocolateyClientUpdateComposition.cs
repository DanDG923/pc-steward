using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; Chocolatey Confirm design §3 item 1 and §5 items 2 and 6: the single production
/// composition of the reviewed dispatch pipeline for the Chocolatey client self-update
/// (<see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>). It is constructed once, in the Desktop's <c>App.xaml.cs</c>, and
/// exposes only <see cref="CreateSession"/>; the workflow, the dispatcher, the transport, the capability reader and the producers
/// never leave it (architecture-pinned).
/// <para>
/// Each session gets its own pieces, built here and nowhere else: the N6 producer composition behind its public façade
/// (<see cref="ChocolateyClientUpdateProducers"/>: the F′ evidence source and the client verifier over ONE baseline store, and
/// the review observation), the protected deployment-capability reader, the protocol-2 transport over that reader, the route
/// dispatcher, and one <see cref="ReviewedUpdateDispatchWorkflow"/> over the common current-user gate and the durable store.
/// The dispatcher is wrapped so that this composition can only ever dispatch the exact client self-update shape
/// (<see cref="ClientRouteOnlyDispatcher"/>): the package route (<see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>)
/// stays uncomposed, and WinGet is not touched.
/// </para>
/// Nothing here deletes, repairs or reconfigures anything in Chocolatey's folder or settings, starts background work, or holds an
/// elevated process: the only elevation is the one-off helper the transport launches for one confirmed job.
/// </summary>
public sealed class ChocolateyClientUpdateComposition
{
    private readonly ReviewedUpdateGateRunner runUnderGate;
    private readonly IUpdateDispatchStore store;
    private readonly Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;

    /// <param name="runUnderGate">The common current-user gate with mandatory reconciliation (the Desktop's data-operation lease).</param>
    /// <param name="store">The durable dispatch store (claim, finalization, barrier).</param>
    /// <param name="saveDraft">Saves the frozen operation under the data-operation lease and returns its draft.</param>
    /// <param name="readHistory">Reads recent reviewed-update history under the lease; null when it cannot be read.</param>
    public ChocolateyClientUpdateComposition(ReviewedUpdateGateRunner runUnderGate, IUpdateDispatchStore store,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory)
    {
        this.runUnderGate = runUnderGate ?? throw new ArgumentNullException(nameof(runUnderGate));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.saveDraft = saveDraft ?? throw new ArgumentNullException(nameof(saveDraft));
        this.readHistory = readHistory ?? throw new ArgumentNullException(nameof(readHistory));
    }

    /// <summary>
    /// One new review session over its own freshly composed pipeline. Nothing is observed, saved or launched until the session is
    /// prepared and confirmed; the session refuses everything while owner admission is not compiled in.
    /// </summary>
    public ChocolateyClientUpdateReviewSession CreateSession()
    {
        ChocolateyClientUpdateProducers producers = new();
        OwnerOnlyDeploymentCapabilityReader capability = new();
        WindowsReviewedHelperTransport transport = new(capability);
        ChocolateyClientUpdatePhaseSignal phases = new();
        ClientRouteOnlyDispatcher dispatcher = new(new ChocolateyRuntimeLimitsRouteDispatcher(capability, transport), phases);
        ReviewedUpdateDispatchWorkflow workflow = new(runUnderGate, producers.EvidenceSource, dispatcher, producers.Verifier, store);
        return new ChocolateyClientUpdateReviewSession(producers.ObserveClientReviewAsync, saveDraft, readHistory, workflow, phases,
            ChocolateyReviewedRouteProbe.ProbeSplitTokenState, ChocolateyClientReleaseCatalog.Current.ListedVersions);
    }
}

/// <summary>
/// Chocolatey Confirm design §5 item 6: the composed dispatcher admits only the exact client self-update shape (the package route
/// stays uncomposed), then defers to the route dispatcher, whose <c>Supports</c> also checks the protected deployment capability.
/// It reports the progress points of design §4.3 around the one exchange. It adds no authority: every refusal stays a refusal.
/// </summary>
internal sealed class ClientRouteOnlyDispatcher(IReviewedUpdateRouteDispatcher inner, ChocolateyClientUpdatePhaseSignal phases) : IReviewedUpdateRouteDispatcher
{
    private readonly IReviewedUpdateRouteDispatcher inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ChocolateyClientUpdatePhaseSignal phases = phases ?? throw new ArgumentNullException(nameof(phases));

    public bool Supports(ReviewedUpdateOperation operation)
    {
        try { return IsClientRoute(operation) && inner.Supports(operation); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    public async Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!IsClientRoute(ticket.Operation))
            return ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, ChocolateyRuntimeLimitsRouteDispatcher.RouteUnsupportedDiagnostic);
        phases.Report(ChocolateyClientUpdatePhase.WaitingForPermission);
        try { return await inner.DispatchAsync(ticket, cancellationToken).ConfigureAwait(false); }
        finally { phases.Report(ChocolateyClientUpdatePhase.Verifying); }
    }

    internal static bool IsClientRoute(ReviewedUpdateOperation? operation) =>
        operation is not null && operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
        string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal);
}
