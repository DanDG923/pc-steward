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
/// Slice limit: only <see cref="Evaluate"/> is declared. <c>TryHold(route, out HeldHelperBinding?)</c> and the
/// <c>HeldHelperBinding</c> share-read handle set are Windows handle-holding code and arrive with the Windows reader and
/// protocol-2 transport slice. No implementation exists in <c>src</c> yet.
/// </para>
/// </summary>
internal interface IHelperProtocolCapability
{
    HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route);
}
