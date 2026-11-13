using System.Globalization;
using System.Runtime.InteropServices;
using PCSteward.Application;

namespace PCSteward.Diagnostics.Windows;

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): Reliability records through WMI's own COM interfaces, unelevated, in <c>root\cimv2</c> on this PC only.
/// One forward-only, return-immediately WQL query per window from <see cref="ReliabilityDiagnosticCatalog.BuildQuery"/>; results
/// are taken one at a time with a <see cref="NextTimeoutMilliseconds"/> wait, so cancellation is checked at least that often and
/// a stalled provider can never hold the reader. Enumeration stops after the requested number of records plus one. Only the six
/// catalog properties are read; no other class, namespace, method, write or remote connection is used.
/// </summary>
public sealed class WmiReliabilityRecordSource : IReliabilityRecordSource
{
    public const int NextTimeoutMilliseconds = 250;
    private const string Namespace = @"root\cimv2";
    private static readonly Guid WbemLocatorClass = new("4590f811-1d3a-11d0-891f-00aa004b2e24");
    private const int ForwardOnlyReturnImmediately = 0x20 | 0x10;
    private const int Completed = 0, NoMore = 1, TimedOut = 0x40004;

    private static readonly OneCallAtATime Gate = new();

    public ReliabilityQueryAnswer Query(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumRecords, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecords);
        if (!Gate.TryEnter()) return new(DiagnosticReaderStatus.Failed, [], false);
        try
        {
            return QueryOnce(fromUtc, toUtc, maximumRecords, cancellationToken);
        }
        finally
        {
            Gate.Exit();
        }
    }

    private static ReliabilityQueryAnswer QueryOnce(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumRecords, CancellationToken cancellationToken)
    {
        string wql = ReliabilityDiagnosticCatalog.BuildQuery(fromUtc, toUtc);
        Type? locatorType = Type.GetTypeFromCLSID(WbemLocatorClass, throwOnError: false);
        if (locatorType is null) return new(DiagnosticReaderStatus.Unsupported, [], false);

        IWbemLocator? locator = null;
        IWbemServices? services = null;
        IEnumWbemClassObject? results = null;
        try
        {
            locator = (IWbemLocator)Activator.CreateInstance(locatorType)!;
            int status = locator.ConnectServer(Namespace, null, null, null, 0, null, IntPtr.Zero, out services);
            if (status != Completed || services is null) return Fault(status);
            Impersonate(services);
            status = services.ExecQuery("WQL", wql, ForwardOnlyReturnImmediately, IntPtr.Zero, out results);
            if (status != Completed || results is null) return Fault(status);
            Impersonate(results);

            List<ReliabilityRawRecord> records = [];
            while (records.Count <= maximumRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = results.Next(NextTimeoutMilliseconds, 1, out IWbemClassObject? item, out uint returned);
                if (status == TimedOut && returned == 0) continue;
                if (returned == 0 || item is null)
                {
                    if (status is Completed or NoMore) break;
                    return Fault(status);
                }

                try
                {
                    records.Add(Read(item));
                }
                finally
                {
                    ComRelease.Release(item);
                }

                if (status == NoMore) break;
            }

            bool more = records.Count > maximumRecords;
            return new(DiagnosticReaderStatus.Completed, more ? records.GetRange(0, maximumRecords) : records, more);
        }
        catch (COMException exception)
        {
            return Fault(exception.HResult);
        }
        finally
        {
            ComRelease.Release(results);
            ComRelease.Release(services);
            ComRelease.Release(locator);
        }
    }

    private static ReliabilityQueryAnswer Fault(int status) => new(
        ComRelease.IsAccessDenied(status) ? DiagnosticReaderStatus.Denied
            : status is WbemInvalidClass or WbemInvalidNamespace or WbemNotSupported or WbemInvalidQuery ? DiagnosticReaderStatus.Unsupported
            : DiagnosticReaderStatus.Failed, [], false);

    private const int WbemInvalidNamespace = unchecked((int)0x8004100E);
    private const int WbemInvalidClass = unchecked((int)0x80041010);
    private const int WbemNotSupported = unchecked((int)0x8004100C);
    private const int WbemInvalidQuery = unchecked((int)0x80041017);

    /// <summary>WMI needs the impersonation level on each proxy; the scripting API did this itself.</summary>
    private static void Impersonate(object proxy)
    {
        IntPtr unknown = Marshal.GetIUnknownForObject(proxy);
        try
        {
            _ = CoSetProxyBlanket(unknown, AuthenticationWindows, AuthorizationNone, IntPtr.Zero, AuthenticationLevelCall, ImpersonationLevelImpersonate, IntPtr.Zero, CapabilitiesNone);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private const uint AuthenticationWindows = 10, AuthorizationNone = 0, AuthenticationLevelCall = 3, ImpersonationLevelImpersonate = 3, CapabilitiesNone = 0;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoSetProxyBlanket(IntPtr proxy, uint authenticationService, uint authorizationService, IntPtr serverPrincipalName, uint authenticationLevel, uint impersonationLevel, IntPtr authenticationInfo, uint capabilities);

    private static ReliabilityRawRecord Read(IWbemClassObject item) => new(
        ComRelease.Text(Value(item, "LogFile")),
        ComRelease.Integer(Value(item, "RecordNumber")),
        ComRelease.Text(Value(item, "SourceName")),
        ComRelease.Integer(Value(item, "EventIdentifier")),
        ComRelease.Text(Value(item, "TimeGenerated")),
        ComRelease.Text(Value(item, "ProductName")));

    /// <summary>Reads one catalog property; only <see cref="ReliabilityDiagnosticCatalog.SelectedProperties"/> are ever named.</summary>
    private static object? Value(IWbemClassObject item, string name)
    {
        if (!ReliabilityDiagnosticCatalog.SelectedProperties.Contains(name)) throw new ArgumentOutOfRangeException(nameof(name));
        return item.ReadProperty(name, 0, out object? value, IntPtr.Zero, IntPtr.Zero) == Completed ? value : null;
    }

    [ComImport, Guid("dc12a687-737f-11cf-884d-00aa004b2e24"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemLocator
    {
        [PreserveSig]
        int ConnectServer([MarshalAs(UnmanagedType.BStr)] string networkResource, [MarshalAs(UnmanagedType.BStr)] string? user, [MarshalAs(UnmanagedType.BStr)] string? password,
            [MarshalAs(UnmanagedType.BStr)] string? locale, int securityFlags, [MarshalAs(UnmanagedType.BStr)] string? authority, IntPtr context, out IWbemServices? services);
    }

    /// <summary>Only <c>ExecQuery</c> is called. The 17 slots before it are named by position and never called.</summary>
    [ComImport, Guid("9556dc99-828c-11cf-a37e-00aa003240c7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemServices
    {
        [PreserveSig] int Slot01();
        [PreserveSig] int Slot02();
        [PreserveSig] int Slot03();
        [PreserveSig] int Slot04();
        [PreserveSig] int Slot05();
        [PreserveSig] int Slot06();
        [PreserveSig] int Slot07();
        [PreserveSig] int Slot08();
        [PreserveSig] int Slot09();
        [PreserveSig] int Slot10();
        [PreserveSig] int Slot11();
        [PreserveSig] int Slot12();
        [PreserveSig] int Slot13();
        [PreserveSig] int Slot14();
        [PreserveSig] int Slot15();
        [PreserveSig] int Slot16();
        [PreserveSig] int Slot17();

        [PreserveSig]
        int ExecQuery([MarshalAs(UnmanagedType.BStr)] string queryLanguage, [MarshalAs(UnmanagedType.BStr)] string query, int flags, IntPtr context, out IEnumWbemClassObject? results);
    }

    /// <summary>Only <c>Next</c> is called, with a bounded wait.</summary>
    [ComImport, Guid("027947e1-d731-11ce-a357-000000000001"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumWbemClassObject
    {
        [PreserveSig] int Slot01();

        [PreserveSig]
        int Next(int timeoutMilliseconds, uint count, out IWbemClassObject? item, out uint returned);
    }

    /// <summary>Only the property read (<c>IWbemClassObject::Get</c>, the second slot) is called.</summary>
    [ComImport, Guid("dc12a681-737f-11cf-884d-00aa004b2e24"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWbemClassObject
    {
        [PreserveSig] int Slot01();

        [PreserveSig]
        int ReadProperty([MarshalAs(UnmanagedType.LPWStr)] string name, int flags, out object? value, IntPtr type, IntPtr flavor);
    }
}

/// <summary>One Windows Update history entry: the only fields read from <c>IUpdateHistoryEntry</c>.</summary>
public sealed record WindowsUpdateRawEntry(DateTime? DateUtc, int? Operation, int? ResultCode, int? HResult, string? Title, string? UpdateId, int? RevisionNumber);

/// <summary>
/// FR-DIA-01: Windows Update history through the Windows Update Agent API, unelevated. It creates a session and a searcher only to
/// call <c>GetTotalHistoryCount</c> and <c>QueryHistory</c>, which read the local history store. It never searches, downloads,
/// installs, hides or changes an update, sets no searcher option and makes no service call.
/// </summary>
public sealed class WuaHistorySource : IWindowsUpdateHistorySource
{
    private const string SessionProgId = "Microsoft.Update.Session";

    private static readonly OneCallAtATime Gate = new();

    public WindowsUpdateHistoryAnswer Read(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntries);
        if (!Gate.TryEnter()) return new(DiagnosticReaderStatus.Failed, [], false);
        try
        {
            return ReadOnce(fromUtc, toUtc, maximumEntries, cancellationToken);
        }
        finally
        {
            Gate.Exit();
        }
    }

    private static WindowsUpdateHistoryAnswer ReadOnce(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken)
    {
        Type? sessionType = Type.GetTypeFromProgID(SessionProgId, throwOnError: false);
        if (sessionType is null) return new(DiagnosticReaderStatus.Unsupported, [], false);

        object? session = null, searcher = null;
        try
        {
            session = Activator.CreateInstance(sessionType);
            searcher = ((dynamic)session!).CreateUpdateSearcher();
            int total = (int)((dynamic)searcher!).GetTotalHistoryCount();
            object captured = searcher;
            return WindowsUpdateHistoryCatalog.Page(total, (start, count) => Page(captured, start, count), fromUtc, toUtc, maximumEntries, cancellationToken);
        }
        catch (COMException exception) when (ComRelease.IsAccessDenied(exception.HResult))
        {
            return new(DiagnosticReaderStatus.Denied, [], false);
        }
        finally
        {
            ComRelease.Release(searcher);
            ComRelease.Release(session);
        }
    }

    private static List<WindowsUpdateRawEntry> Page(object searcher, int start, int count)
    {
        object? collection = ((dynamic)searcher).QueryHistory(start, count);
        try
        {
            int available = (int)((dynamic)collection!).Count;
            List<WindowsUpdateRawEntry> entries = new(Math.Min(available, count));
            for (int index = 0; index < available && index < count; index++)
            {
                object? entry = ((dynamic)collection).Item(index);
                try
                {
                    entries.Add(Entry(entry!));
                }
                finally
                {
                    ComRelease.Release(entry);
                }
            }

            return entries;
        }
        finally
        {
            ComRelease.Release(collection);
        }
    }

    private static WindowsUpdateRawEntry Entry(object entry)
    {
        dynamic item = entry;
        object? identity = item.UpdateIdentity;
        try
        {
            object? date = item.Date;
            return new(
                date is DateTime value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null,
                (int?)ComRelease.Integer((object?)item.Operation),
                (int?)ComRelease.Integer((object?)item.ResultCode),
                (int?)ComRelease.Integer((object?)item.HResult),
                ComRelease.Text((object?)item.Title),
                identity is null ? null : ComRelease.Text((object?)((dynamic)identity).UpdateID),
                identity is null ? null : (int?)ComRelease.Integer((object?)((dynamic)identity).RevisionNumber));
        }
        finally
        {
            ComRelease.Release(identity);
        }
    }
}

/// <summary>
/// FR-DIA-01 (ADR-027 Q1, Codex review on #105): WMI's <c>ConnectServer</c> and the Windows Update Agent's
/// <c>GetTotalHistoryCount</c> and <c>QueryHistory</c> are synchronous calls with no timeout, so cancellation can only be checked
/// between them. A collection that runs out of time abandons such a call rather than waiting for it. This gate allows one call per
/// source at a time in the whole app: while an abandoned call is still running, a later collection reports that source as not read
/// instead of starting another, so stalled calls can never pile up.
/// </summary>
internal sealed class OneCallAtATime
{
    private int busy;

    public bool TryEnter() => Interlocked.CompareExchange(ref busy, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref busy, 0);
}

internal static class ComRelease
{
    public static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    public static bool IsAccessDenied(int hresult) => hresult is unchecked((int)0x80070005) or unchecked((int)0x80041003);

    public static string? Text(object? value) => value as string;

    public static long? Integer(object? value) => value switch
    {
        int number => number,
        uint number => number,
        long number => number,
        short number => number,
        ushort number => number,
        byte number => number,
        _ => null
    };
}
