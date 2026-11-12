namespace PCSteward.Execution;

/// <summary>
/// Phase D stage D4b, stage 2c (design §5.4): the single production composition of the whole-PC WinGet route's protocol-2 runner.
/// It builds one protected Desktop-role deployment-capability reader, one protocol-2 transport over it and the reviewed exchange,
/// and gives them only to the <see cref="ElevatedUpdateRunner"/> it returns, beside the protocol-1 transport that machine-scope jobs
/// without a reviewed operation still use until stage 3. Nothing here admits the route: the review gate and the capability statement
/// still refuse it, and the helper repeats every check. No production code calls it before stage 3.
/// </summary>
public static class WingetMachineUpdateComposition
{
    /// <summary>A runner whose reviewed whole-PC path runs on the protocol-2 helper.</summary>
    public static ElevatedUpdateRunner CreateElevatedRunner()
    {
        OwnerOnlyDeploymentCapabilityReader capability = new();
        WindowsReviewedHelperTransport transport = new(capability);
        return new ElevatedUpdateRunner(new WindowsElevatedHelperTransport(), reviewedExchange: new WingetMachineReviewedExchange(capability, transport));
    }
}
