using PCSteward.Application;
using PCSteward.Catalog;
using System.Xml;

namespace PCSteward.Provider.Chocolatey;

internal interface IChocolateyHealthClient
{
    ChocolateyHealthDiscovery Discover(bool requireCallerCannotModifyLayout);
    void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout);
    Task<ChocolateyProcessResult> ReadVersionAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken);
}

/// <summary>Strict trusted-client health; evidence grants neither source consent nor execution approval.</summary>
public sealed class ChocolateyProviderHealthProbe : IProviderHealthProbe
{
    private readonly IChocolateyHealthClient client;
    private readonly Func<DateTimeOffset> utcNow;

    public ChocolateyProviderHealthProbe() : this(new WindowsChocolateyHealthClient(), () => DateTimeOffset.UtcNow) { }
    internal ChocolateyProviderHealthProbe(IChocolateyHealthClient client, Func<DateTimeOffset> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public async Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ChocolateyHealthDiscovery discovery;
        try { discovery = client.Discover(requireCallerCannotModifyLayout: true); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(ProviderPresence.Unknown, ProviderTrust.Unknown, ProviderProbeState.Unavailable, null, [], "Provider observation failed.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (discovery.Presence == ProviderPresence.Absent)
            return Report(ProviderPresence.Absent, ProviderTrust.Unknown, ProviderProbeState.Current, null, [], "No client was found in the accessible canonical location.");
        if (discovery.Presence != ProviderPresence.Present || discovery.Trust != ProviderTrust.Trusted || discovery.Client is null)
            return Report(discovery.Presence, discovery.Trust == ProviderTrust.Trusted ? ProviderTrust.Unknown : discovery.Trust,
                ProviderProbeState.Unavailable, null, [], "A strict trusted provider client could not be established.");
        try { client.ValidateUnchanged(discovery.Client, requireCallerCannotModifyLayout: true); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Report(ProviderPresence.Present, ProviderTrust.Untrusted, ProviderProbeState.Unavailable, null, [], "The provider client changed or failed strict trust revalidation.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string? version = null;
        IReadOnlyList<string> sources = [];
        bool complete = true;
        try
        {
            ChocolateyProcessResult result = await client.ReadVersionAsync(discovery.Client, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            string value = result.StandardOutput.Trim();
            if (result is { ExitCode: 0, TimedOut: false, OutputLimitExceeded: false } && value.Length is > 0 and <= 128 &&
                value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' or '_')) version = value;
            else complete = false;
        }
        catch (ChocolateyHealthTrustException) { return Report(ProviderPresence.Present, ProviderTrust.Untrusted, ProviderProbeState.Unavailable, null, [], "The provider client changed or failed strict trust revalidation."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { complete = false; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<string> configured = await client.ReadConfiguredSourcesAsync(discovery.Client, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (configured.Count > 64 || configured.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)))
                throw new IOException("Configured source evidence exceeds its limits.");
            sources = Array.AsReadOnly(configured.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (ChocolateyHealthTrustException) { return Report(ProviderPresence.Present, ProviderTrust.Untrusted, ProviderProbeState.Unavailable, null, [], "The provider client changed or failed strict trust revalidation."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { complete = false; }
        cancellationToken.ThrowIfCancellationRequested();
        return Report(ProviderPresence.Present, ProviderTrust.Trusted, complete ? ProviderProbeState.Current : ProviderProbeState.Partial,
            version, sources, complete ? "Local strict client and configured sources observed; remote reachability was not checked." : "Some local provider evidence could not be read.");
    }

    private ProviderHealthReport Report(ProviderPresence presence, ProviderTrust trust, ProviderProbeState state, string? version, IReadOnlyList<string> sources, string reason) =>
        new(CatalogProviderKind.Chocolatey, presence, trust, state, version, sources, utcNow(), reason);
}

internal sealed class WindowsChocolateyHealthClient : IChocolateyHealthClient
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);
    internal static IReadOnlyList<string> VersionArguments => ["--version", "--no-color"];
    public ChocolateyHealthDiscovery Discover(bool requireCallerCannotModifyLayout)
    {
        if (!requireCallerCannotModifyLayout) throw new ArgumentException("Health requires strict layout trust.", nameof(requireCallerCannotModifyLayout));
        return MachineTrustedChocolatey.DiscoverHealth();
    }
    public void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout) =>
        MachineTrustedChocolatey.ValidateUnchanged(client, requireCallerCannotModifyLayout);
    public Task<ChocolateyProcessResult> ReadVersionAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken) =>
        new WindowsChocolateyProcess(client).RunAsync(VersionArguments, ReadTimeout, cancellationToken);
    public async Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { MachineTrustedChocolatey.ValidateUnchanged(client, requireCallerCannotModifyLayout: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ChocolateyHealthTrustException("Strict provider trust changed.", exception);
        }
        string root = Path.GetDirectoryName(Path.GetDirectoryName(client.Path)) ?? throw new IOException("The canonical provider root is unavailable.");
        string configDirectory = Path.Combine(root, "config");
        string configFile = Path.Combine(configDirectory, "chocolatey.config");
        MachineTrustedChocolatey.ValidateProtectedHealthConfiguration(root, configDirectory, configFile);
        using FileStream file = new(configFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length is <= 0 or > 65_536) throw new IOException("Configured source input exceeds its limits.");
        byte[] bytes = new byte[65_537];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await file.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (count > 65_536) throw new IOException("Configured source input exceeds its limits.");
        using MemoryStream input = new(bytes, 0, count, writable: false);
        return ParseConfiguredSources(input);
    }

    internal static IReadOnlyList<string> ParseConfiguredSources(Stream input)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65_536 };
        using XmlReader reader = XmlReader.Create(input, settings);
        List<string> sources = [];
        bool inSources = false;
        bool sawSources = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0 && reader.Name != "chocolatey") throw new IOException("Configured source input is not a provider configuration.");
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "sources")
            {
                if (sawSources) throw new IOException("Configured source input contains duplicate source sections.");
                inSources = !reader.IsEmptyElement;
                sawSources = true;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 1 && reader.Name == "sources") inSources = false;
            else if (inSources && reader.NodeType == XmlNodeType.Element && reader.Depth == 2 && reader.Name == "source")
            {
                string? id = reader.GetAttribute("id");
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl) || sources.Count >= 64)
                    throw new IOException("Configured source evidence exceeds its limits.");
                sources.Add(id);
            }
        }
        if (!sawSources) throw new IOException("Configured source input is incomplete.");
        return Array.AsReadOnly(sources.ToArray());
    }
}

internal sealed class ChocolateyHealthTrustException(string message, Exception? innerException = null) : IOException(message, innerException) { }
