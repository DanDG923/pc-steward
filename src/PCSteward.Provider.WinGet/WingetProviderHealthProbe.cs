using PCSteward.Application;
using PCSteward.Catalog;
using System.Text.RegularExpressions;

namespace PCSteward.Provider.WinGet;

internal interface IWingetHealthClient
{
    WingetHealthDiscovery Discover();
    Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(string executable, CancellationToken cancellationToken);
}

/// <summary>Read-only provider evidence, independent of saved source consent.</summary>
public sealed class WingetProviderHealthProbe : IProviderHealthProbe
{
    private readonly IWingetHealthClient client;
    private readonly Func<DateTimeOffset> utcNow;

    public WingetProviderHealthProbe() : this(new WindowsWingetHealthClient(), () => DateTimeOffset.UtcNow) { }
    internal WingetProviderHealthProbe(IWingetHealthClient client, Func<DateTimeOffset> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public async Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WingetHealthDiscovery discovery;
        try { discovery = client.Discover(); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(ProviderPresence.Unknown, ProviderTrust.Unknown, ProviderProbeState.Unavailable, null, [], "Provider observation failed.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (discovery.Presence == ProviderPresence.Absent)
            return Report(ProviderPresence.Absent, ProviderTrust.Unknown, ProviderProbeState.Current, null, [], "No client was found in the accessible canonical location.");
        if (discovery.Presence != ProviderPresence.Present || discovery.Trust != ProviderTrust.Trusted || discovery.Executable is null)
            return Report(discovery.Presence, discovery.Trust == ProviderTrust.Trusted ? ProviderTrust.Unknown : discovery.Trust,
                ProviderProbeState.Unavailable, null, [], "A trusted provider client could not be established.");

        string? version = null;
        IReadOnlyList<string> sources = [];
        bool complete = true;
        try
        {
            WingetProcessResult result = await client.ReadVersionAsync(discovery.Executable, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string value = result.StandardOutput.Trim();
            if (result is { ExitCode: 0, TimedOut: false, OutputLimitExceeded: false } && ValidVersion(value)) version = value;
            else complete = false;
        }
        catch (WingetHealthTrustException) { return Report(ProviderPresence.Present, ProviderTrust.Untrusted, ProviderProbeState.Unavailable, null, [], "The provider client changed or failed trust revalidation."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { complete = false; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<string> configured = await client.ReadConfiguredSourcesAsync(discovery.Executable, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            sources = SnapshotSources(configured);
        }
        catch (WingetHealthTrustException) { return Report(ProviderPresence.Present, ProviderTrust.Untrusted, ProviderProbeState.Unavailable, null, [], "The provider client changed or failed trust revalidation."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { complete = false; }
        cancellationToken.ThrowIfCancellationRequested();
        return Report(ProviderPresence.Present, ProviderTrust.Trusted, complete ? ProviderProbeState.Current : ProviderProbeState.Partial,
            version, sources, complete ? "Local client and configured sources observed; remote reachability was not checked." : "Some local provider evidence could not be read.");
    }

    private ProviderHealthReport Report(ProviderPresence presence, ProviderTrust trust, ProviderProbeState state, string? version, IReadOnlyList<string> sources, string reason) =>
        new(CatalogProviderKind.WinGet, presence, trust, state, version, sources, utcNow(), reason);

    private static bool ValidVersion(string value) => value.Length is > 0 and <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' or '_');
    private static System.Collections.ObjectModel.ReadOnlyCollection<string> SnapshotSources(IReadOnlyList<string> sources)
    {
        if (sources.Count > 64 || sources.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)))
            throw new IOException("Configured source evidence exceeds its limits.");
        return Array.AsReadOnly(sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

internal sealed class WindowsWingetHealthClient : IWingetHealthClient
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);
    internal static IReadOnlyList<string> VersionArguments => ["--version"];
    internal static IReadOnlyList<string> SourceArguments => ["source", "list", "--disable-interactivity"];

    public WingetHealthDiscovery Discover() => MachineTrustedWinget.DiscoverHealth();
    public Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken) =>
        new WindowsWingetProcess(executable).RunAsync(VersionArguments, ReadTimeout, cancellationToken);
    public async Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(string executable, CancellationToken cancellationToken)
    {
        WingetProcessResult result = await new WindowsWingetProcess(executable).RunAsync(SourceArguments, ReadTimeout, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ParseConfiguredSources(result);
    }

    internal static IReadOnlyList<string> ParseConfiguredSources(WingetProcessResult result)
    {
        if (result is not { ExitCode: 0, TimedOut: false, OutputLimitExceeded: false } || result.StandardOutput.Length > 65_536)
            throw new IOException("Configured sources could not be read.");
        string[] lines = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        int separator = Array.FindIndex(lines, line => line.Length >= 3 && line.All(character => character == '-'));
        if (separator < 1) throw new IOException("Configured source output is incomplete.");
        MatchCollection columns = Regex.Matches(lines[separator - 1], @"\S+");
        if (columns.Count is < 2 or > 3) throw new IOException("Configured source columns are incomplete.");
        int columnEnd = columns[1].Index;
        List<string> sources = [];
        foreach (string line in lines.Skip(separator + 1))
        {
            if (columnEnd <= 0 || line.Length <= columnEnd || string.IsNullOrWhiteSpace(line[columnEnd..])) throw new IOException("Configured source row is incomplete.");
            string name = line[..columnEnd].Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl) || sources.Count >= 64) throw new IOException("Configured source evidence exceeds its limits.");
            sources.Add(name);
        }
        return Array.AsReadOnly(sources.ToArray());
    }
}

internal sealed class WingetHealthTrustException(string message, Exception? innerException = null) : IOException(message, innerException) { }
