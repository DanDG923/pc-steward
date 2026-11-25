using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// Four-model review F-03: one exchange's observation and whether the exchange positively proved that no helper launch was attempted
/// (refused before the launcher was invoked, so no helper process and no UAC prompt can exist). False means "not proven", never
/// "launched".
/// </summary>
internal sealed record WingetMachineReviewedExchangeResult(ReviewedUpdateDispatchObservation Observation, bool NoHelperLaunched);

/// <summary>D4b stage 2c: one protocol-2 exchange for one frozen whole-PC WinGet operation. Must not throw for expected failures.</summary>
internal interface IWingetMachineReviewedExchange
{
    Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// F-03: as <see cref="RunAsync"/>, with the exchange's proof that no helper launch was attempted. An implementation that cannot
    /// prove it keeps this default, which never claims the proof.
    /// </summary>
    async Task<WingetMachineReviewedExchangeResult> RunReportingLaunchAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken) =>
        new(await RunAsync(operation, cancellationToken).ConfigureAwait(false), NoHelperLaunched: false);
}

/// <summary>
/// Phase D stage D4b, stage 2c (design §5.4; FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13): runs one frozen
/// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation on the protocol-2 helper. Before anything is launched the
/// operation must be admitted by the review gate (<see cref="WingetMachineReviewedRoute.Admits"/>), have the route's reply shape, and
/// be listed by the protected deployment capability; then it draws one fresh nonce, runs exactly one exchange and returns the
/// interpreter's observation. Never a fallback to protocol 1: a refusal here starts nothing. The helper repeats every check itself.
/// </summary>
internal sealed class WingetMachineReviewedExchange : IWingetMachineReviewedExchange
{
    internal const string RouteUnavailableDiagnostic = "helper-route-unsupported";
    private readonly IHelperProtocolCapability capability;
    private readonly IReviewedHelperTransport transport;
    private readonly Func<string> createNonce;

    internal WingetMachineReviewedExchange(IHelperProtocolCapability capability, IReviewedHelperTransport transport)
        : this(capability, transport, ElevatedHelperProtocol.CreateNonce)
    {
    }

    internal WingetMachineReviewedExchange(IHelperProtocolCapability capability, IReviewedHelperTransport transport, Func<string> createNonce)
    {
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.createNonce = createNonce ?? throw new ArgumentNullException(nameof(createNonce));
    }

    public async Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken) =>
        (await RunReportingLaunchAsync(operation, cancellationToken).ConfigureAwait(false)).Observation;

    /// <summary>
    /// F-03: the proof that no helper launch was attempted holds only for a refusal here, before the transport is called, or for the
    /// transport's own refusal before it invoked the launcher that the interpreter also reads as not started. Anything else, including
    /// a launcher that returned no process, a declined prompt or a helper that started, carries no proof.
    /// </summary>
    public async Task<WingetMachineReviewedExchangeResult> RunReportingLaunchAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Supports(operation))
            return new(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, RouteUnavailableDiagnostic), NoHelperLaunched: true);
        if (cancellationToken.IsCancellationRequested)
            return new(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Cancelled, "operation-cancelled"), NoHelperLaunched: true);
        string nonce = createNonce();
        ReviewedHelperExchange exchange = await transport.ExchangeAsync(operation, nonce, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Transport returned no exchange.");
        ReviewedUpdateDispatchObservation observation = ReviewedUpdateProtocol2Interpreter.Interpret(operation, exchange.Outcome, exchange.ResponseFrame,
            nonce, exchange.Desktop).WorkflowObservation;
        bool noHelperLaunched = exchange.NoHelperLaunchAttempted && exchange.Outcome == ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation &&
            observation.Invocation == ReviewedUpdateInvocation.NotStarted;
        return new(observation, noHelperLaunched);
    }

    /// <summary>The gate, the route's reply shape, then the deployment capability (no file I/O before the shape checks). Never throws.</summary>
    private bool Supports(ReviewedUpdateOperation operation)
    {
        try
        {
            return WingetMachineReviewedRoute.Admits(operation) &&
                ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(operation, out _, out ReviewedHelperReplyShape shape) &&
                shape == ReviewedHelperReplyShape.WinGetMachine && capability.Evaluate(operation.RouteContract).IsSupported;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }
}
