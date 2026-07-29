using PCSteward.Application;

namespace PCSteward.Diagnostics;

/// <summary>Bounded, allowlisted update events. Provider output must already be sanitized by Execution.</summary>
public sealed class BoundedUpdateExecutionEventSink(int capacity = 256) : IUpdateExecutionEventSink
{
    private readonly int capacity = Math.Clamp(capacity, 1, 4096);
    private readonly Queue<UpdateExecutionEvent> events = new();
    private readonly object sync = new();

    public void Write(UpdateExecutionEvent updateEvent)
    {
        ArgumentNullException.ThrowIfNull(updateEvent);
        UpdateExecutionEvent bounded = updateEvent with
        {
            EventId = Text(updateEvent.EventId, 64),
            PackageId = Text(updateEvent.PackageId, 256),
            TargetVersion = Text(updateEvent.TargetVersion, 128),
            Message = Text(updateEvent.Message, 4096)
        };
        lock (sync)
        {
            if (events.Count == capacity) events.Dequeue();
            events.Enqueue(bounded);
        }
    }

    public IReadOnlyCollection<UpdateExecutionEvent> Snapshot()
    {
        lock (sync) return events.ToArray();
    }

    private static string Text(string value, int maximum)
    {
        string safe = new(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        safe = safe.Trim();
        return safe[..Math.Min(safe.Length, maximum)];
    }
}
