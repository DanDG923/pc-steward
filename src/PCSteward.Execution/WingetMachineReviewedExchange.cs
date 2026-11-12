using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>D4b stage 2c: one protocol-2 exchange for one frozen whole-PC WinGet operation. Must not throw for expected failures.</summary>
internal interface IWingetMachineReviewedExchange
{
    Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken);
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

    public async Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Supports(operation))
            return ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, RouteUnavailableDiagnostic);
        if (cancellationToken.IsCancellationRequested)
            return ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Cancelled, "operation-cancelled");
        string nonce = createNonce();
        ReviewedHelperExchange exchange = await transport.ExchangeAsync(operation, nonce, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Transport returned no exchange.");
        return ReviewedUpdateProtocol2Interpreter.Interpret(operation, exchange.Outcome, exchange.ResponseFrame, nonce, exchange.Desktop)
            .WorkflowObservation;
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
