using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using PCSteward.Application;

namespace PCSteward.Diagnostics.Windows;

/// <summary>
/// FR-DIA-01: Reliability records through the WMI scripting API, unelevated, in <c>root\cimv2</c> on this PC only. One
/// forward-only semisynchronous WQL query from <see cref="ReliabilityDiagnosticCatalog.BuildQuery"/>; enumeration stops after
/// the requested number of records plus one. No other class, namespace, method call or remote connection is used.
/// </summary>
public sealed class WmiReliabilityRecordSource : IReliabilityRecordSource
{
    private const string LocatorProgId = "WbemScripting.SWbemLocator";
    private const string Namespace = @"root\cimv2";
    private const int ForwardOnlyReturnImmediately = 0x20 | 0x10;

    public ReliabilityQueryAnswer Query(string wql, int maximumRecords, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(wql);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecords);
        if (!wql.StartsWith("SELECT ", StringComparison.Ordinal) || !wql.Contains(" FROM " + ReliabilityDiagnosticCatalog.WmiClass + " WHERE ", StringComparison.Ordinal))
            throw new ArgumentException("Only the catalog's Reliability query is accepted.", nameof(wql));
        Type? locatorType = Type.GetTypeFromProgID(LocatorProgId, throwOnError: false);
        if (locatorType is null) return new(DiagnosticReaderStatus.Unsupported, [], false);

        object? locator = null, services = null, results = null;
        try
        {
            locator = Activator.CreateInstance(locatorType);
            services = ((dynamic)locator!).ConnectServer(".", Namespace);
            results = ((dynamic)services!).ExecQuery(wql, "WQL", ForwardOnlyReturnImmediately);
            List<ReliabilityRawRecord> records = [];
            foreach (object item in (IEnumerable)results!)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (records.Count > maximumRecords) break;
                    records.Add(Read(item));
                }
                finally
                {
                    ComRelease.Release(item);
                }
            }

            bool more = records.Count > maximumRecords;
            return new(DiagnosticReaderStatus.Completed, more ? records.GetRange(0, maximumRecords) : records, more);
        }
        catch (COMException exception) when (ComRelease.IsAccessDenied(exception.HResult))
        {
            return new(DiagnosticReaderStatus.Denied, [], false);
        }
        catch (COMException exception) when (exception.HResult is WbemInvalidClass or WbemInvalidNamespace or WbemNotSupported)
        {
            return new(DiagnosticReaderStatus.Unsupported, [], false);
        }
        finally
        {
            ComRelease.Release(results);
            ComRelease.Release(services);
            ComRelease.Release(locator);
        }
    }

    private const int WbemInvalidNamespace = unchecked((int)0x8004100E);
    private const int WbemInvalidClass = unchecked((int)0x80041010);
    private const int WbemNotSupported = unchecked((int)0x8004100C);

    private static ReliabilityRawRecord Read(object item)
    {
        object? properties = ((dynamic)item).Properties_;
        try
        {
            return new(
                ComRelease.Text(Value(properties, "LogFile")),
                ComRelease.Integer(Value(properties, "RecordNumber")),
                ComRelease.Text(Value(properties, "SourceName")),
                ComRelease.Integer(Value(properties, "EventIdentifier")),
                ComRelease.Text(Value(properties, "TimeGenerated")),
                ComRelease.Text(Value(properties, "ProductName")));
        }
        finally
        {
            ComRelease.Release(properties);
        }
    }

    private static object? Value(object? properties, string name)
    {
        object? property = ((dynamic)properties!).Item(name);
        try
        {
            return ((dynamic)property!).Value;
        }
        finally
        {
            ComRelease.Release(property);
        }
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

    public WindowsUpdateHistoryAnswer Read(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntries);
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
