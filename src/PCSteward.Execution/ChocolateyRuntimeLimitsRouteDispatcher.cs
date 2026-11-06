using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; adapter gates §A.2/§A.4 and reviewed fact extension rev 3 §4.1: the closed route
/// adapter for schema 3 <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>.
/// <para>
/// <see cref="Supports"/> (also the workflow's <c>StillSupported</c>) is the route shape plus O2 <c>EvidenceId</c> check, and
/// only then the protected deployment-capability seam; it never throws and is never eligibility evidence.
/// <see cref="DispatchAsync"/> draws one fresh nonce, runs exactly one protocol-2 exchange and returns the interpreter's
/// workflow observation. A wire label never qualifies anything alone; every rejection is Uncertain in the interpreter.
/// </para>
/// <para>
/// Task 50 activation (ADR-031 owner decision 7, step C-A; <c>docs/review/TASK_50_ACTIVATION_CHANGE.md</c>): <see cref="Supports"/>
/// accepts the exact package shape and the exact client self-update shape (<c>TryGetRouteProfileDigest</c>), each only when the
/// protected deployment capability lists its route. No production composition constructs this adapter yet; the Desktop
/// prepares no Chocolatey review.
/// </para>
/// </summary>
internal sealed class ChocolateyRuntimeLimitsRouteDispatcher : IReviewedUpdateRouteDispatcher
{
    internal const string RouteUnsupportedDiagnostic = "helper-route-unsupported";
    private readonly IHelperProtocolCapability capability;
    private readonly IReviewedHelperTransport transport;
    private readonly Func<string> createNonce;

    internal ChocolateyRuntimeLimitsRouteDispatcher(IHelperProtocolCapability capability, IReviewedHelperTransport transport)
        : this(capability, transport, ElevatedHelperProtocol.CreateNonce)
    {
    }

    internal ChocolateyRuntimeLimitsRouteDispatcher(IHelperProtocolCapability capability, IReviewedHelperTransport transport, Func<string> createNonce)
    {
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.createNonce = createNonce ?? throw new ArgumentNullException(nameof(createNonce));
    }

    public bool Supports(ReviewedUpdateOperation operation)
    {
        try
        {
            // No file I/O before the route and EvidenceId checks pass (A.4 step 1). Activation: the package arm or the exact
            // ADR-031 client arm, then the deployment capability for that route.
            return operation is not null && ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(operation, out _, out _) &&
                capability.Evaluate(operation.RouteContract).IsSupported;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// One exchange. ADR-031 design §7.1: the client self-update is a client mode of this dispatcher (the interpreter reads its
    /// reply shape and run fault). Since the activation, <see cref="Supports"/> admits it like the package arm; the transport's
    /// capability hold still refuses a deployment whose capability statement does not list the route.
    /// </summary>
    public async Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ReviewedUpdateOperation operation = ticket.Operation;
        if (!ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(operation, out _, out _))
            return ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Failed, RouteUnsupportedDiagnostic);
        string nonce = createNonce();
        ReviewedHelperExchange exchange = await transport.ExchangeAsync(operation, nonce, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Transport returned no exchange.");
        return ReviewedUpdateProtocol2Interpreter.Interpret(operation, exchange.Outcome, exchange.ResponseFrame, nonce, exchange.Desktop)
            .WorkflowObservation;
    }
}
