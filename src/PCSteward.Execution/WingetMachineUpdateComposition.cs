namespace PCSteward.Execution;

/// <summary>
/// Phase D stage D4b (design §5.4): the single production composition of the elevated runner. It builds one protected Desktop-role
/// deployment-capability reader, one protocol-2 transport over it and the reviewed exchange, and gives them only to the
/// <see cref="ElevatedUpdateRunner"/> it returns. Since stage 3 this is the only elevated update path (protocol 1 is retired). Nothing
/// here admits a job: the review gate, fresh evidence and the capability statement still decide, and the helper repeats every check.
/// </summary>
public static class WingetMachineUpdateComposition
{
    /// <summary>A runner whose reviewed whole-PC path runs on the protocol-2 helper.</summary>
    public static ElevatedUpdateRunner CreateElevatedRunner()
    {
        OwnerOnlyDeploymentCapabilityReader capability = new();
        WindowsReviewedHelperTransport transport = new(capability);
        return new ElevatedUpdateRunner(new WingetMachineReviewedExchange(capability, transport));
    }
}
