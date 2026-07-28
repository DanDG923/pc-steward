using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Desktop;

/// <summary>Opt-in final-app staging provider used only for cancellation and interruption evidence.</summary>
internal sealed class EvidenceDelayInventoryProvider(TimeSpan delay) : ILocalInventoryProvider
{
    public string Name => "evidence-delay";
    public TimeSpan Timeout => delay + TimeSpan.FromSeconds(5);

    public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return new ProviderScanResult([], [], [], []);
    }

    public static ILocalInventoryProvider? FromEnvironment()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("PCSTEWARD_EVIDENCE_DELAY_MS"), out int milliseconds) || milliseconds is < 1000 or > 60000) return null;
        return new EvidenceDelayInventoryProvider(TimeSpan.FromMilliseconds(milliseconds));
    }
}
