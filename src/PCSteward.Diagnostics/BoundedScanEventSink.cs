using PCSteward.Application;

namespace PCSteward.Diagnostics;

/// <summary>Bounded, allowlisted scan events for local diagnostics and future sanitized export.</summary>
public sealed class BoundedScanEventSink(int capacity = 256) : IScanEventSink
{
    private readonly object sync = new();
    private readonly Queue<ScanEvent> events = new();
    private readonly int capacity = Math.Clamp(capacity, 1, 4096);
    public void Write(ScanEvent scanEvent)
    {
        ScanEvent bounded = scanEvent with { EventId = Text(scanEvent.EventId, 64), Provider = string.IsNullOrWhiteSpace(scanEvent.Provider) ? null : Text(scanEvent.Provider, 128), Message = Text(scanEvent.Message, 256) };
        lock (sync) { if (events.Count == capacity) events.Dequeue(); events.Enqueue(bounded); }
    }
    public IReadOnlyCollection<ScanEvent> Snapshot() { lock (sync) return events.ToArray(); }
    private static string Text(string value, int maximum) => value.Replace('\r', ' ').Replace('\n', ' ').Trim()[..Math.Min(value.Trim().Length, maximum)];
}
