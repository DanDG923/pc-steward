using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PCSteward.Domain;

/// <summary>
/// FR-BAT-03/04: one review of several selections. Nothing here is executable until <see cref="Confirm"/>
/// creates one immutable, digest-protected batch.
/// </summary>
public sealed class OperationBatchReview
{
    public OperationBatchReview(Guid batchId, Guid correlationId, IReadOnlyList<UpdateJobSelection> selections, Func<Guid>? newJobId = null)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch ID is required.", nameof(batchId));
        if (correlationId == Guid.Empty) throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
        ArgumentNullException.ThrowIfNull(selections);
        ApprovedOperationBatch.ValidateSelections(selections);
        Func<Guid> ids = newJobId ?? Guid.NewGuid;
        BatchId = batchId;
        CorrelationId = correlationId;
        Items = [.. ApprovedOperationBatch.ExecutionOrder(selections, selection => selection).Select(selection => new UpdateJobReview(ids(), correlationId, selection))];
        if (Items.Select(item => item.JobId).Distinct().Count() != Items.Count) throw new ArgumentException("Job IDs must be unique.", nameof(newJobId));
    }

    public Guid BatchId { get; }
    public Guid CorrelationId { get; }

    /// <summary>Execution order: unelevated items first, then elevated items, each group in reviewed order.</summary>
    public IReadOnlyList<UpdateJobReview> Items { get; }

    public ApprovedOperationBatch Confirm(DateTimeOffset approvedAt)
    {
        if (approvedAt == default) throw new ArgumentException("An approval timestamp is required.", nameof(approvedAt));
        return ApprovedOperationBatch.Create(BatchId, CorrelationId, approvedAt, [.. Items.Select(item => item.Confirm(approvedAt))]);
    }
}

/// <summary>
/// FR-BAT-04 / ADR-017 v0.3: an approved, ordered batch of at most 50 operations. The digest chains every
/// job digest in order, so substitution, reordering, appending or truncation fails the integrity check.
/// The batch expires 30 minutes after approval and accepts no additions. It is evidence, never authority:
/// each item is still revalidated immediately before it starts.
/// </summary>
public sealed class ApprovedOperationBatch
{
    public const int MaximumItems = 50;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(15);

    private ApprovedOperationBatch(Guid id, Guid correlationId, DateTimeOffset approvedAt, IReadOnlyList<ImmutableUpdateJob> items, string? expectedDigest)
    {
        if (id == Guid.Empty) throw new ArgumentException("A batch ID is required.", nameof(id));
        if (correlationId == Guid.Empty) throw new ArgumentException("A correlation ID is required.", nameof(correlationId));
        if (approvedAt == default) throw new ArgumentException("An approval timestamp is required.", nameof(approvedAt));
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item is null)) throw new ArgumentException("A batch cannot contain an empty item.", nameof(items));
        ValidateSelections([.. items.Select(item => item.Selection)]);
        if (items.Select(item => item.Id).Distinct().Count() != items.Count) throw new ArgumentException("A batch cannot contain the same job twice.", nameof(items));
        if (items.Any(item => !item.HasValidDigest || item.CorrelationId != correlationId || item.ApprovedAt != approvedAt))
        {
            // Every job is confirmed together with its batch; a job approved elsewhere cannot be added.
            throw new InvalidOperationException("A batch item is not bound to this approval.");
        }
        if (!ExecutionOrder(items, item => item.Selection).SequenceEqual(items))
        {
            throw new InvalidOperationException("Batch items are not in execution order.");
        }

        Id = id;
        CorrelationId = correlationId;
        ApprovedAt = approvedAt;
        Items = [.. items];
        Digest = ComputeDigest();
        if (expectedDigest is not null && !DigestMatches(expectedDigest))
        {
            throw new InvalidOperationException("The persisted operation batch failed its integrity check.");
        }
    }

    public Guid Id { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset ApprovedAt { get; }
    public DateTimeOffset ExpiresAt => ApprovedAt + Lifetime;
    public IReadOnlyList<ImmutableUpdateJob> Items { get; }
    public string Digest { get; }
    public bool HasValidDigest => Items.All(item => item.HasValidDigest) && DigestMatches(ComputeDigest());

    /// <summary>Items run in the desktop without elevation (FR-BAT-05).</summary>
    public IReadOnlyList<ImmutableUpdateJob> UnelevatedItems => [.. Items.Where(item => !item.Selection.RunsElevated)];

    /// <summary>Items run in one elevated helper session after every unelevated item (FR-BAT-05).</summary>
    public IReadOnlyList<ImmutableUpdateJob> ElevatedItems => [.. Items.Where(item => item.Selection.RunsElevated)];

    /// <summary>False after expiry, or when the clock reads earlier than the approval (a moved clock fails closed).</summary>
    public bool IsUsableAt(DateTimeOffset now) => now >= ApprovedAt - ClockSkew && now < ExpiresAt;

    internal static ApprovedOperationBatch Create(Guid id, Guid correlationId, DateTimeOffset approvedAt, IReadOnlyList<ImmutableUpdateJob> items) =>
        new(id, correlationId, approvedAt, items, null);

    public static ApprovedOperationBatch Restore(Guid id, Guid correlationId, DateTimeOffset approvedAt, IReadOnlyList<ImmutableUpdateJob> items, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        return new(id, correlationId, approvedAt, items, digest);
    }

    internal static void ValidateSelections(IReadOnlyList<UpdateJobSelection> selections)
    {
        if (selections.Count is 0 or > MaximumItems) throw new ArgumentOutOfRangeException(nameof(selections), "A batch holds 1 to 50 items.");
        if (selections.Any(selection => selection is null)) throw new ArgumentException("A batch cannot contain an empty item.", nameof(selections));
        if (selections.Select(selection => selection.StableProductKey).Distinct(StringComparer.Ordinal).Count() != selections.Count ||
            selections.Select(selection => selection.Provider + "|" + selection.PackageId.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() != selections.Count)
        {
            throw new ArgumentException("A batch cannot target the same product or package twice.", nameof(selections));
        }
    }

    internal static IEnumerable<T> ExecutionOrder<T>(IReadOnlyList<T> items, Func<T, UpdateJobSelection> selection) =>
        items.Where(item => !selection(item).RunsElevated).Concat(items.Where(item => selection(item).RunsElevated));

    private string ComputeDigest()
    {
        StringBuilder text = new();
        text.Append("pcsteward-operation-batch-v1\n")
            .Append(Id.ToString("D", CultureInfo.InvariantCulture)).Append('\n')
            .Append(CorrelationId.ToString("D", CultureInfo.InvariantCulture)).Append('\n')
            .Append(ApprovedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(Items.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        for (int index = 0; index < Items.Count; index++)
        {
            text.Append(index.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(Items[index].Id.ToString("D", CultureInfo.InvariantCulture)).Append(':')
                .Append(Items[index].Digest).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private bool DigestMatches(string candidate)
    {
        if (candidate.Length != Digest.Length) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Digest), Convert.FromHexString(candidate));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
