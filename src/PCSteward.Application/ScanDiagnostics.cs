namespace PCSteward.Application;

public enum ScanEventSeverity { Information, Warning, Error }
public sealed record ScanEvent(Guid CorrelationId, DateTimeOffset OccurredAt, ScanEventSeverity Severity, string EventId, string? Provider, string Message);
public interface IScanEventSink { void Write(ScanEvent scanEvent); }
public sealed class NullScanEventSink : IScanEventSink { public static NullScanEventSink Instance { get; } = new(); public void Write(ScanEvent scanEvent) { } }
