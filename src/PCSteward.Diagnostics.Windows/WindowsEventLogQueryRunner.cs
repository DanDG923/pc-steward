using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PCSteward.Application;

namespace PCSteward.Diagnostics.Windows;

/// <summary>
/// FR-DIA-01: the Windows Event Log API (wevtapi) route. It opens one structured query in reverse order, takes events in small
/// batches with a short wait so cancellation is seen promptly, renders only the catalog's value paths and closes every handle.
/// It never subscribes, exports, clears or writes a log and never formats message text.
/// </summary>
public sealed class WindowsEventLogQueryRunner : IEventLogQueryRunner
{
    private const int BatchSize = 16;
    private const int NextTimeoutMilliseconds = 500;
    private const int MaximumRenderBytes = 64 * 1024;

    public EventLogQueryAnswer Run(string channel, string structuredQuery, int maximumEvents, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(structuredQuery);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEvents);
        using EventLogHandle query = EventLogNative.EvtQuery(IntPtr.Zero, null, structuredQuery, EventLogNative.EvtQueryChannelPath | EventLogNative.EvtQueryReverseDirection);
        if (query.IsInvalid) return Refused(Marshal.GetLastPInvokeError());
        using CancellationTokenRegistration cancel = cancellationToken.Register(static state => EventLogNative.EvtCancel((EventLogHandle)state!), query);
        string[] paths = [.. EventLogDiagnosticCatalog.RenderedValuePaths];
        using EventLogHandle context = EventLogNative.EvtCreateRenderContext(paths.Length, paths, EventLogNative.EvtRenderContextValues);
        if (context.IsInvalid) return Refused(Marshal.GetLastPInvokeError());

        // ADR-027 Q1 (Codex review on #105): the cap applies to matching events materialized. At most the allowance is rendered;
        // one further handle, never rendered, is enough to prove more matched, and no batch asks for more than that.
        List<EventLogRawEvent> events = [];
        IntPtr[] batch = new IntPtr[BatchSize];
        bool more = false;
        while (!more)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int request = Math.Min(BatchSize, maximumEvents + 1 - events.Count);
            if (!EventLogNative.EvtNext(query, request, batch, NextTimeoutMilliseconds, 0, out int returned))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == EventLogNative.ErrorNoMoreItems) break;
                if (error == EventLogNative.ErrorTimeout) continue;
                if (error == EventLogNative.ErrorCancelled) cancellationToken.ThrowIfCancellationRequested();
                return Refused(error);
            }

            for (int index = 0; index < returned; index++)
            {
                using EventLogHandle item = new(batch[index]);
                batch[index] = IntPtr.Zero;
                if (events.Count < maximumEvents) events.Add(Render(context, item));
                else more = true;
            }
        }

        return new(DiagnosticReaderStatus.Completed, events, more);
    }

    private static EventLogQueryAnswer Refused(int error) => new(error switch
    {
        EventLogNative.ErrorAccessDenied => DiagnosticReaderStatus.Denied,
        EventLogNative.ErrorChannelNotFound => DiagnosticReaderStatus.Unsupported,
        _ => DiagnosticReaderStatus.Failed
    }, [], false);

    /// <summary>Renders the catalog's values. An event that cannot be rendered comes back empty, so admission counts it as rejected.</summary>
    private static EventLogRawEvent Render(EventLogHandle context, EventLogHandle item)
    {
        EventLogRawEvent empty = new(null, null, null, null, null, null);
        if (EventLogNative.EvtRender(context, item, EventLogNative.EvtRenderEventValues, 0, IntPtr.Zero, out int needed, out _)) return empty;
        if (Marshal.GetLastPInvokeError() != EventLogNative.ErrorInsufficientBuffer || needed is <= 0 or > MaximumRenderBytes) return empty;

        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EventLogNative.EvtRender(context, item, EventLogNative.EvtRenderEventValues, needed, buffer, out _, out int count) ||
                count != EventLogDiagnosticCatalog.RenderedValuePaths.Count)
                return empty;
            return new(
                Text(buffer, 0),
                Text(buffer, 1),
                Unsigned(buffer, 2) is { } eventId and <= ushort.MaxValue ? (int)eventId : null,
                Unsigned(buffer, 3),
                FileTime(buffer, 4),
                Unsigned(buffer, 5) is { } code and <= uint.MaxValue ? (long)code : null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // EVT_VARIANT: an 8-byte value, then Count (uint) and Type (uint): 16 bytes each.
    private static int Type(IntPtr buffer, int index) => Marshal.ReadInt32(buffer, (index * 16) + 12) & 0x7F;

    private static bool IsArray(IntPtr buffer, int index) => (Marshal.ReadInt32(buffer, (index * 16) + 12) & 0x80) != 0;

    private static string? Text(IntPtr buffer, int index) =>
        !IsArray(buffer, index) && Type(buffer, index) == EventLogNative.EvtVarTypeString ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, index * 16)) : null;

    private static ulong? Unsigned(IntPtr buffer, int index)
    {
        if (IsArray(buffer, index)) return null;
        long raw = Marshal.ReadInt64(buffer, index * 16);
        return Type(buffer, index) switch
        {
            EventLogNative.EvtVarTypeUInt16 => (ushort)raw,
            EventLogNative.EvtVarTypeUInt32 or EventLogNative.EvtVarTypeHexInt32 => (uint)raw,
            EventLogNative.EvtVarTypeUInt64 or EventLogNative.EvtVarTypeHexInt64 => (ulong)raw,
            _ => null
        };
    }

    private static DateTime? FileTime(IntPtr buffer, int index)
    {
        if (IsArray(buffer, index) || Type(buffer, index) != EventLogNative.EvtVarTypeFileTime) return null;
        long raw = Marshal.ReadInt64(buffer, index * 16);
        return raw is > 0 and < 2650467743999999999 ? DateTime.FromFileTimeUtc(raw) : null;
    }
}

internal sealed class EventLogHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public EventLogHandle()
        : base(true)
    {
    }

    public EventLogHandle(IntPtr existing)
        : base(true) => SetHandle(existing);

    protected override bool ReleaseHandle() => EventLogNative.EvtClose(handle);
}

internal static class EventLogNative
{
    public const int EvtQueryChannelPath = 0x1;
    public const int EvtQueryReverseDirection = 0x200;
    public const int EvtRenderContextValues = 0;
    public const int EvtRenderEventValues = 0;
    public const int EvtVarTypeString = 1;
    public const int EvtVarTypeUInt16 = 6;
    public const int EvtVarTypeUInt32 = 8;
    public const int EvtVarTypeUInt64 = 10;
    public const int EvtVarTypeFileTime = 17;
    public const int EvtVarTypeHexInt32 = 20;
    public const int EvtVarTypeHexInt64 = 21;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorNoMoreItems = 259;
    public const int ErrorCancelled = 1223;
    public const int ErrorTimeout = 1460;
    public const int ErrorChannelNotFound = 15007;

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern EventLogHandle EvtQuery(IntPtr session, string? path, string query, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtNext(EventLogHandle resultSet, int eventArraySize, [Out] IntPtr[] eventArray, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern EventLogHandle EvtCreateRenderContext(int valuePathsCount, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] valuePaths, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtRender(EventLogHandle context, EventLogHandle fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtCancel(EventLogHandle handle);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtClose(IntPtr handle);
}
