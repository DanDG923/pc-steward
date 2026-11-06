using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Passive saved-draft identity. A receipt is neither live confirmation nor execution authority.</summary>
public sealed class ReviewedUpdateDraft
{
    public ReviewedUpdateDraft(ReviewedUpdateOperation operation, Guid generation)
    {
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        if (generation == Guid.Empty) throw new ArgumentException("A recorded draft generation is required.", nameof(generation));
        Generation = generation;
    }

    public ReviewedUpdateOperation Operation { get; }
    public Guid Generation { get; }
    public bool HasSameDraftIdentity(ReviewedUpdateDraft? other) =>
        other is not null && Generation == other.Generation && Operation.HasSameIdentity(other.Operation);
}
