// Task 54A throwaway probe. Never part of the product. It runs only on disposable GitHub-hosted runners.
//
// Usage: WinGetComProbe.exe <inproc|outofproc> [--query] --out <file>
//   inproc     activate PackageManager from the InProcCom runtime beside the exe (the route PCSteward.Desktop uses)
//   outofproc  activate the out-of-process WinGet COM server that App Installer registers (CoCreateInstance)
//   --query    also connect to the predefined winget catalogue and run one exact-ID query, shaped like
//              WingetStructuredUpdateCandidateAuthority (composite catalogue, Id Equals filter, ResultLimit 2)
// Every outcome is written as key=value lines. Failures are recorded, never thrown, so the CI step always reports.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Management.Deployment;

internal static partial class Program
{
    private static readonly List<string> Lines = [];
    private static readonly Guid OutOfProcessPackageManagerClsid = new("C53A4F16-787E-42A4-B304-29EFFB4BF597");
    private static readonly Guid IUnknownIid = new("00000000-0000-0000-C000-000000000046");
    private const uint ClsctxLocalServer = 0x4;
    private const string QueryPackageId = "Microsoft.PowerShell";

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "inproc";
        bool runQuery = args.Contains("--query");
        int outIndex = Array.IndexOf(args, "--out");
        string? outFile = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null;

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        Log("mode", mode);
        Log("elevated", elevated.ToString());
        Log("osVersion", Environment.OSVersion.Version.ToString());
        Log("projection", typeof(PackageManager).Assembly.GetName().Version?.ToString() ?? "unknown");
        Log("processBits", (IntPtr.Size * 8).ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (mode == "inproc")
        {
            ProbeInProcessFactory();
        }

        try
        {
            Stopwatch timer = Stopwatch.StartNew();
            PackageManager manager = mode == "outofproc" ? CreateOutOfProcess() : new PackageManager();
            Log("activation", "ok");
            Log("activationMs", timer.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // PackageManager.Version exists from 1.12 on; read it by name so the probe still compiles against older projections.
            object? engineVersion = manager.GetType().GetProperty("Version")?.GetValue(manager);
            Log("engineVersion", engineVersion?.ToString() ?? "not exposed by this projection");

            PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
            PackageCatalogInfo info = reference.Info;
            Log("catalogName", info.Name);
            Log("catalogOrigin", info.Origin.ToString());
            Log("catalogTrust", info.TrustLevel.ToString());

            if (runQuery)
            {
                Query(manager, reference);
            }
        }
        catch (Exception exception)
        {
            Log("activation", "failed");
            Log("exception", exception.GetType().FullName ?? "unknown");
            Log("hresult", "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            Log("message", exception.Message.ReplaceLineEndings(" "));
        }

        if (outFile is not null)
        {
            File.WriteAllLines(outFile, Lines);
        }
        return 0;
    }

    private static void Query(PackageManager manager, PackageCatalogReference reference)
    {
        try
        {
            CreateCompositePackageCatalogOptions compositeOptions = new()
            {
                CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
                InstalledScope = PackageInstallScope.Any
            };
            compositeOptions.Catalogs.Add(reference);
            PackageCatalogReference composite = manager.CreateCompositePackageCatalog(compositeOptions);
            Stopwatch timer = Stopwatch.StartNew();
            ConnectResult connection = composite.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
            Log("connectStatus", connection.Status.ToString());
            Log("connectMs", timer.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (connection.Status != ConnectResultStatus.Ok)
            {
                return;
            }

            FindPackagesOptions options = new() { ResultLimit = 2 };
            options.Filters.Add(new PackageMatchFilter
            {
                Field = PackageMatchField.Id,
                Option = PackageFieldMatchOption.Equals,
                Value = QueryPackageId
            });
            timer.Restart();
            FindPackagesResult result = connection.PackageCatalog.FindPackagesAsync(options).AsTask().WaitAsync(TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
            Log("findStatus", result.Status.ToString());
            Log("findMs", timer.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Log("matchCount", result.Matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Log("limitExceeded", result.WasLimitExceeded.ToString());
            if (result.Matches.Count == 1)
            {
                CatalogPackage package = result.Matches[0].CatalogPackage;
                PackageMatchFilter criteria = result.Matches[0].MatchCriteria;
                Log("matchCriteria", $"{criteria.Field}/{criteria.Option}");
                Log("packageId", package.Id);
                Log("defaultInstallVersion", package.DefaultInstallVersion?.Version ?? "none");
                Log("defaultInstallCatalog", package.DefaultInstallVersion?.PackageCatalog?.Info?.Name ?? "none");
                Log("installedVersion", package.InstalledVersion?.Version ?? "none");
                Log("availableVersionCount", package.AvailableVersions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                PackageVersionInfo? candidate = package.DefaultInstallVersion;
                if (candidate is not null)
                {
                    // ADR-023 needs installer scope/architecture before review. Read the applicable installer without installing.
                    InstallOptions installOptions = new()
                    {
                        PackageInstallScope = PackageInstallScope.User,
                        PackageInstallMode = PackageInstallMode.Silent
                    };
                    PackageInstallerInfo? installer = candidate.GetApplicableInstaller(installOptions);
                    Log("applicableUserInstaller", installer is null ? "none" : $"{installer.InstallerType}/{installer.Scope}/{installer.Architecture}/{installer.ElevationRequirement}");
                    CatalogPackageMetadata metadata = candidate.GetCatalogPackageMetadata();
                    Log("agreementCount", metadata.Agreements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Log("tagCount", metadata.Tags.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        catch (Exception exception)
        {
            Log("query", "failed");
            Log("queryException", exception.GetType().FullName ?? "unknown");
            Log("queryHresult", "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            Log("queryMessage", exception.Message.ReplaceLineEndings(" "));
        }
    }

    // Separates "the runtime DLL will not load" from "its factory refuses" before the projection hides both behind one
    // activation error: load the renamed InProcCom runtime beside the exe and ask its DllGetActivationFactory directly.
    private static unsafe void ProbeInProcessFactory()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Microsoft.Management.Deployment.dll");
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
        if (module == IntPtr.Zero)
        {
            Log("inprocDllLoad", "failed win32=" + Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        Log("inprocDllLoad", "ok");
        IntPtr export = NativeLibrary.GetExport(module, "DllGetActivationFactory");
        const string className = "Microsoft.Management.Deployment.PackageManager";
        int hr = WindowsCreateString(className, (uint)className.Length, out IntPtr name);
        if (hr != 0)
        {
            Log("inprocFactory", "WindowsCreateString hr=0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        IntPtr factory = IntPtr.Zero;
        hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)export)(name, &factory);
        Log("inprocFactory", "hr=0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        if (factory != IntPtr.Zero)
        {
            Marshal.Release(factory);
        }
        _ = WindowsDeleteString(name);
    }

    private const uint LoadWithAlteredSearchPath = 0x8;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, uint length, out IntPtr value);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(IntPtr value);

    private static PackageManager CreateOutOfProcess()
    {
        int hr = CoCreateInstance(OutOfProcessPackageManagerClsid, IntPtr.Zero, ClsctxLocalServer, IUnknownIid, out IntPtr unknown);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return PackageManager.FromAbi(unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private static void Log(string key, string value)
    {
        string line = key + "=" + value;
        Lines.Add(line);
        Console.WriteLine(line);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);
}
