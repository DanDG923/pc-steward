using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>Closed capability result (adapter gates §A.2). Every value except <see cref="Supported"/> means unsupported.</summary>
internal enum HelperProtocolCapabilityStatus { Supported, Absent, Incomplete, Unreadable, Mismatch }

/// <summary>
/// One fixed diagnostic token per status. The diagnostic never contains a path, hash or JSON text, so it cannot leak
/// deployment detail or be steered by file content.
/// </summary>
internal sealed record HelperProtocolCapabilityResult
{
    private static readonly HelperProtocolCapabilityResult[] Results =
    [
        new(HelperProtocolCapabilityStatus.Supported, "helper-capability-supported"),
        new(HelperProtocolCapabilityStatus.Absent, "helper-capability-absent"),
        new(HelperProtocolCapabilityStatus.Incomplete, "helper-capability-incomplete"),
        new(HelperProtocolCapabilityStatus.Unreadable, "helper-capability-unreadable"),
        new(HelperProtocolCapabilityStatus.Mismatch, "helper-capability-mismatch"),
    ];

    private HelperProtocolCapabilityResult(HelperProtocolCapabilityStatus status, string diagnostic)
    {
        Status = status;
        Diagnostic = diagnostic;
    }

    public HelperProtocolCapabilityStatus Status { get; }
    public string Diagnostic { get; }
    public bool IsSupported => Status == HelperProtocolCapabilityStatus.Supported;

    internal static HelperProtocolCapabilityResult For(HelperProtocolCapabilityStatus status) =>
        Enum.IsDefined(status) ? Results[(int)status] : Results[(int)HelperProtocolCapabilityStatus.Unreadable];
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; adapter gates §A.2: the protected deployment-capability seam for helper routes. A
/// compatibility and consistency gate only, never a security boundary against administrators, never eligibility evidence
/// and never cached. The protocol version is not a parameter, so a caller cannot choose it.
/// <para>
/// Both members perform bounded synchronous reads (at most 64 MiB) on every call; the Desktop must call
/// <c>ReviewedUpdateDispatchWorkflow.RunAsync</c> off the UI thread (G3). The Windows producer is
/// <see cref="OwnerOnlyDeploymentCapabilityReader"/>. No production caller exists yet: the route dispatcher and the
/// protocol-2 transport that consume it arrive with the protocol-2 transport slice.
/// </para>
/// </summary>
internal interface IHelperProtocolCapability
{
    /// <summary>A.4 steps 2–6 with every share-read handle released before return (<c>Supports</c>/<c>StillSupported</c>).</summary>
    HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route);

    /// <summary>
    /// A.4 step 3.1: the same evaluation, keeping the binding set held through share-read handles. <paramref name="binding"/>
    /// is non-null only when the result is <see cref="HelperProtocolCapabilityStatus.Supported"/>; the caller owns it and
    /// disposes it only after helper exit has been observed and the transport is sealed (A-M2).
    /// </summary>
    HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding);
}
