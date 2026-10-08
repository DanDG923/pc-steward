using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchStyle.Prototype;

// Task 45 spike: helper protocol v2 (ADR-017/018 v0.3 revisions). Pure code, no IPC or elevation.
// It answers three questions: can one approved batch be bound by a digest the helper can recompute,
// can each frame stay inside the existing 16 KiB message bound, and can a strict receiver reject
// substitution, reordering, appending, truncation, replay, expiry and oversize input?

public enum OperationKind { Install, Upgrade }
public enum ProviderKind { WinGet, Chocolatey }
public enum SourceKind { WinGet, MicrosoftStore, ChocolateyCommunity }
public enum InstallScope { User, Machine }
public enum InstallArchitecture { X64, X86, Arm64, Neutral }

public sealed partial record BatchItem
{
    public BatchItem(
        int sequence,
        OperationKind kind,
        ProviderKind provider,
        SourceKind source,
        string stableProductKey,
        string packageId,
        string? installedVersion,
        string targetVersion,
        InstallScope scope,
        InstallArchitecture architecture,
        string? agreementHash)
    {
        if (sequence is < 0 or >= OperationBatch.MaximumItems) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Kind = Defined(kind, nameof(kind));
        Provider = Defined(provider, nameof(provider));
        Source = Defined(source, nameof(source));
        StableProductKey = Match(StableKeyPattern(), stableProductKey, nameof(stableProductKey));
        PackageId = Match(PackageIdPattern(), packageId, nameof(packageId));
        TargetVersion = Match(VersionPattern(), targetVersion, nameof(targetVersion));
        Scope = Defined(scope, nameof(scope));
        Architecture = Defined(architecture, nameof(architecture));
        if (kind == OperationKind.Install)
        {
            if (installedVersion is not null) throw new ArgumentException("An install has no installed version.", nameof(installedVersion));
            InstalledVersion = null;
        }
        else
        {
            InstalledVersion = Match(VersionPattern(), installedVersion ?? string.Empty, nameof(installedVersion));
        }

        if (provider == ProviderKind.Chocolatey && (source != SourceKind.ChocolateyCommunity || scope != InstallScope.Machine))
        {
            throw new ArgumentException("Chocolatey operations are Community-source and machine-scope only.", nameof(provider));
        }

        if (provider == ProviderKind.WinGet && source == SourceKind.ChocolateyCommunity)
        {
            throw new ArgumentException("WinGet operations cannot name the Chocolatey source.", nameof(source));
        }

        AgreementHash = agreementHash is null ? null : Match(Sha256HexPattern(), agreementHash, nameof(agreementHash));
    }

    public int Sequence { get; }
    public OperationKind Kind { get; }
    public ProviderKind Provider { get; }
    public SourceKind Source { get; }
    public string StableProductKey { get; }
    public string PackageId { get; }
    public string? InstalledVersion { get; }
    public string TargetVersion { get; }
    public InstallScope Scope { get; }
    public InstallArchitecture Architecture { get; }
    public string? AgreementHash { get; }

    internal string CanonicalText => string.Join('\n',
        Sequence.ToString(CultureInfo.InvariantCulture), Kind, Provider, Source, StableProductKey, PackageId,
        InstalledVersion ?? "-", TargetVersion, Scope, Architecture, AgreementHash ?? "-");

    internal string IdentityKey => string.Join('|', Provider, PackageId.ToUpperInvariant(), Scope, Architecture);

    private static T Defined<T>(T value, string name) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(name);

    private static string Match(Regex pattern, string value, string name) =>
        value is not null && pattern.IsMatch(value) ? value : throw new ArgumentException("Value is outside the allowlisted format.", name);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9:._-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.+-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256HexPattern();
}

/// <summary>An approved, immutable batch. The digest covers every item and the order.</summary>
public sealed class OperationBatch
{
    public const int MaximumItems = 50;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public OperationBatch(Guid batchId, DateTimeOffset approvedAtUtc, IReadOnlyList<BatchItem> items)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch ID is required.", nameof(batchId));
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is 0 or > MaximumItems) throw new ArgumentOutOfRangeException(nameof(items));
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index] is null || items[index].Sequence != index) throw new ArgumentException("Items must be contiguous from sequence 0.", nameof(items));
        }

        if (items.Select(item => item.IdentityKey).Distinct(StringComparer.Ordinal).Count() != items.Count)
        {
            throw new ArgumentException("A batch cannot target the same package identity twice.", nameof(items));
        }

        BatchId = batchId;
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        Items = items.ToArray();
        Digest = ComputeDigest(BatchId, ApprovedAtUtc, Items);
    }

    public Guid BatchId { get; }
    public DateTimeOffset ApprovedAtUtc { get; }
    public IReadOnlyList<BatchItem> Items { get; }
    public string Digest { get; }
    public DateTimeOffset ExpiresAtUtc => ApprovedAtUtc + Lifetime;

    /// <summary>User-scope items run unelevated first; machine-scope items go to one helper session.</summary>
    public IReadOnlyList<BatchItem> UserScopeItems => Items.Where(item => item.Scope == InstallScope.User).ToArray();
    public IReadOnlyList<BatchItem> MachineScopeItems => Items.Where(item => item.Scope == InstallScope.Machine).ToArray();

    internal static string ComputeDigest(Guid batchId, DateTimeOffset approvedAtUtc, IEnumerable<BatchItem> items)
    {
        StringBuilder text = new();
        text.Append("pcsteward-batch-v2\n");
        text.Append(batchId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        text.Append(approvedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        foreach (BatchItem item in items) text.Append(item.CanonicalText).Append("\n--\n");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

/// <summary>Desktop side: serialises the machine-scope part of a batch into bounded frames.</summary>
public static class BatchFrameWriter
{
    public const string ProtocolVersion = "2";
    public const int MaximumFrameBytes = 16 * 1024;

    public static IReadOnlyList<byte[]> Write(OperationBatch batch, string nonce, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        IReadOnlyList<BatchItem> items = batch.MachineScopeItems;
        // The helper recomputes a digest over exactly the frames it receives, so the header carries
        // the machine-scope subset digest, bound to the approved batch digest.
        string subsetDigest = OperationBatch.ComputeDigest(batch.BatchId, batch.ApprovedAtUtc, items);
        List<byte[]> frames = [];
        frames.Add(Frame(new Dictionary<string, object?>
        {
            ["frame"] = "BatchHeader",
            ["protocolVersion"] = ProtocolVersion,
            ["batchId"] = batch.BatchId.ToString("D", CultureInfo.InvariantCulture),
            ["approvedAtUtc"] = batch.ApprovedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["batchDigest"] = batch.Digest,
            ["machineDigest"] = subsetDigest,
            ["itemCount"] = items.Count,
            ["nonce"] = nonce,
            ["issuedAtUtc"] = issuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        }));
        foreach (BatchItem item in items) frames.Add(Frame(ItemFields(batch.BatchId, item)));
        frames.Add(Frame(new Dictionary<string, object?>
        {
            ["frame"] = "BatchEnd",
            ["protocolVersion"] = ProtocolVersion,
            ["batchId"] = batch.BatchId.ToString("D", CultureInfo.InvariantCulture),
            ["itemCount"] = items.Count
        }));
        return frames;
    }

    internal static Dictionary<string, object?> ItemFields(Guid batchId, BatchItem item) => new()
    {
        ["frame"] = "BatchItem",
        ["protocolVersion"] = ProtocolVersion,
        ["batchId"] = batchId.ToString("D", CultureInfo.InvariantCulture),
        ["sequence"] = item.Sequence,
        ["operation"] = item.Kind.ToString(),
        ["provider"] = item.Provider.ToString(),
        ["source"] = item.Source.ToString(),
        ["stableProductKey"] = item.StableProductKey,
        ["packageId"] = item.PackageId,
        ["installedVersion"] = item.InstalledVersion,
        ["targetVersion"] = item.TargetVersion,
        ["scope"] = item.Scope.ToString(),
        ["architecture"] = item.Architecture.ToString(),
        ["agreementHash"] = item.AgreementHash
    };

    internal static byte[] Frame(Dictionary<string, object?> fields)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(fields);
        if (bytes.Length > MaximumFrameBytes) throw new InvalidOperationException("Frame exceeds the protocol bound.");
        return bytes;
    }
}

public enum ReceiverState { AwaitingHeader, ReceivingItems, Complete, Rejected }

/// <summary>Helper side: a strict, one-shot receiver for one approved batch.</summary>
public sealed class HelperBatchReceiver
{
    private static readonly string[] HeaderFields = ["frame", "protocolVersion", "batchId", "approvedAtUtc", "batchDigest", "machineDigest", "itemCount", "nonce", "issuedAtUtc"];
    private static readonly string[] ItemFieldNames = ["frame", "protocolVersion", "batchId", "sequence", "operation", "provider", "source", "stableProductKey", "packageId", "installedVersion", "targetVersion", "scope", "architecture", "agreementHash"];
    private static readonly string[] EndFields = ["frame", "protocolVersion", "batchId", "itemCount"];
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);

    private readonly string expectedNonce;
    private readonly ISet<string> usedNonces;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly List<BatchItem> items = [];
    private Guid batchId;
    private DateTimeOffset approvedAtUtc;
    private string machineDigest = string.Empty;
    private int expectedCount;

    public HelperBatchReceiver(string expectedNonce, ISet<string> usedNonces, Func<DateTimeOffset> utcNow)
    {
        this.expectedNonce = expectedNonce;
        this.usedNonces = usedNonces;
        this.utcNow = utcNow;
    }

    public ReceiverState State { get; private set; } = ReceiverState.AwaitingHeader;
    public string Rejection { get; private set; } = string.Empty;
    public IReadOnlyList<BatchItem> AcceptedItems => State == ReceiverState.Complete ? items : [];

    public bool Accept(ReadOnlySpan<byte> frame)
    {
        if (State is ReceiverState.Complete or ReceiverState.Rejected) return Reject("frame-after-terminal-state");
        if (frame.Length > BatchFrameWriter.MaximumFrameBytes) return Reject("frame-too-large");
        Dictionary<string, JsonElement> fields;
        try
        {
            using JsonDocument document = JsonDocument.Parse(frame.ToArray(), new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Reject("frame-not-object");
            fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!fields.TryAdd(property.Name, property.Value.Clone())) return Reject("duplicate-field");
            }
        }
        catch (JsonException)
        {
            return Reject("malformed-json");
        }

        if (!Text(fields, "protocolVersion", out string? protocol) || protocol != BatchFrameWriter.ProtocolVersion) return Reject("protocol-version");
        if (!Text(fields, "frame", out string? frameKind)) return Reject("frame-kind");
        return (State, frameKind) switch
        {
            (ReceiverState.AwaitingHeader, "BatchHeader") => AcceptHeader(fields),
            (ReceiverState.ReceivingItems, "BatchItem") => AcceptItem(fields),
            (ReceiverState.ReceivingItems, "BatchEnd") => AcceptEnd(fields),
            _ => Reject("unexpected-frame-" + frameKind)
        };
    }

    private bool AcceptHeader(Dictionary<string, JsonElement> fields)
    {
        if (!ExactFields(fields, HeaderFields)) return Reject("header-fields");
        if (!Text(fields, "nonce", out string? nonce) || !FixedEquals(nonce, expectedNonce)) return Reject("nonce-mismatch");
        if (!usedNonces.Add(nonce)) return Reject("nonce-replay");
        if (!Text(fields, "batchId", out string? id) || !Guid.TryParseExact(id, "D", out batchId) || batchId == Guid.Empty) return Reject("batch-id");
        if (!Time(fields, "issuedAtUtc", out DateTimeOffset issued) || !Time(fields, "approvedAtUtc", out approvedAtUtc)) return Reject("timestamps");
        DateTimeOffset now = utcNow();
        if (issued > now.AddSeconds(5) || now - issued > RequestLifetime) return Reject("request-expired");
        if (now >= approvedAtUtc + OperationBatch.Lifetime || approvedAtUtc > now.AddSeconds(5)) return Reject("batch-expired");
        if (!Text(fields, "machineDigest", out string? digest) || digest.Length != 64) return Reject("digest");
        machineDigest = digest;
        if (!Text(fields, "batchDigest", out string? approved) || approved.Length != 64) return Reject("approved-digest");
        if (fields["itemCount"].ValueKind != JsonValueKind.Number || !fields["itemCount"].TryGetInt32(out expectedCount) || expectedCount is < 1 or > OperationBatch.MaximumItems) return Reject("item-count");
        State = ReceiverState.ReceivingItems;
        return true;
    }

    private bool AcceptItem(Dictionary<string, JsonElement> fields)
    {
        if (!ExactFields(fields, ItemFieldNames)) return Reject("item-fields");
        if (!Text(fields, "batchId", out string? id) || !string.Equals(id, batchId.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal)) return Reject("item-batch-mismatch");
        if (items.Count >= expectedCount) return Reject("item-appended");
        try
        {
            BatchItem item = new(
                fields["sequence"].GetInt32(),
                Parse<OperationKind>(fields, "operation"),
                Parse<ProviderKind>(fields, "provider"),
                Parse<SourceKind>(fields, "source"),
                fields["stableProductKey"].GetString()!,
                fields["packageId"].GetString()!,
                fields["installedVersion"].ValueKind == JsonValueKind.Null ? null : fields["installedVersion"].GetString(),
                fields["targetVersion"].GetString()!,
                Parse<InstallScope>(fields, "scope"),
                Parse<InstallArchitecture>(fields, "architecture"),
                fields["agreementHash"].ValueKind == JsonValueKind.Null ? null : fields["agreementHash"].GetString());
            if (item.Scope != InstallScope.Machine) return Reject("user-scope-item-sent-to-helper");
            if (items.Count > 0 && item.Sequence <= items[^1].Sequence) return Reject("item-out-of-order");
            if (items.Any(existing => existing.IdentityKey == item.IdentityKey)) return Reject("duplicate-identity");
            items.Add(item);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            return Reject("item-validation");
        }
    }

    private bool AcceptEnd(Dictionary<string, JsonElement> fields)
    {
        if (!ExactFields(fields, EndFields)) return Reject("end-fields");
        if (!fields["itemCount"].TryGetInt32(out int count) || count != expectedCount || items.Count != expectedCount) return Reject("item-truncated");
        string recomputed = OperationBatch.ComputeDigest(batchId, approvedAtUtc, items);
        if (!FixedEquals(recomputed, machineDigest)) return Reject("digest-mismatch");
        State = ReceiverState.Complete;
        return true;
    }

    private bool Reject(string reason)
    {
        State = ReceiverState.Rejected;
        Rejection = reason;
        items.Clear();
        return false;
    }

    private static bool ExactFields(Dictionary<string, JsonElement> fields, string[] allowed) =>
        fields.Count == allowed.Length && allowed.All(fields.ContainsKey);

    private static bool Text(Dictionary<string, JsonElement> fields, string name, out string value)
    {
        value = string.Empty;
        if (!fields.TryGetValue(name, out JsonElement element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString() ?? string.Empty;
        return value.Length is > 0 and <= 256 && !value.Any(char.IsControl);
    }

    private static bool Time(Dictionary<string, JsonElement> fields, string name, out DateTimeOffset value)
    {
        value = default;
        return Text(fields, name, out string text) &&
            DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    private static T Parse<T>(Dictionary<string, JsonElement> fields, string name) where T : struct, Enum =>
        Text(fields, name, out string text) && Enum.TryParse(text, ignoreCase: false, out T value) && Enum.IsDefined(value)
            ? value
            : throw new FormatException(name);

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

public enum HelperItemOutcome { Completed, Failed, Skipped, CancelledBeforeStart }

/// <summary>Helper side: sequential execution with cancellation honoured only between items.</summary>
public sealed class HelperExecutionSession(IReadOnlyList<BatchItem> items)
{
    private int next;
    private bool cancelRequested;
    private bool running;

    public bool CancelRequested => cancelRequested;

    public void RequestCancelRemaining() => cancelRequested = true;

    public bool TryStartNext(out BatchItem? item)
    {
        item = null;
        if (running) throw new InvalidOperationException("An item is already running.");
        if (cancelRequested || next >= items.Count) return false;
        item = items[next++];
        running = true;
        return true;
    }

    public void Finish() => running = running ? false : throw new InvalidOperationException("No item is running.");

    public IReadOnlyList<BatchItem> NotStarted => items.Skip(next).ToArray();
}
