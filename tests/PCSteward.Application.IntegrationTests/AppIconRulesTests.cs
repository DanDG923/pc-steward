using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// ADR-035 (UI review 2026-11-13, item 20; FR-UI-01, FR-DAT-12): which icon an app's row may show and which files may be opened
/// for it. Every refused form in rule 3 has a case here.
/// </summary>
[TestClass]
public sealed class AppIconRulesTests
{
    private static readonly string[] StepsToAnApp = [@"C:\Program Files", @"C:\Program Files\App", @"C:\Program Files\App\app.exe"];
    private static readonly string[] StepsToARootFile = [@"C:\app.ico"];

    private static InstallationInstance Instance(string provider, string identity, string scope = "Machine", string architecture = "x64") =>
        new(Guid.NewGuid(), Guid.NewGuid(), provider, identity, "1.0", scope, architecture);

    [TestMethod]
    [Description("ADR-035 rule 2: a Store-type app's icon comes from its package family; a classic app's from the uninstall entry the scan recorded, in the hive and view it was read from; WinGet and Chocolatey records name no icon.")]
    public void TheIconSourceIsTheRecordTheScanMade()
    {
        Assert.AreEqual(new AppIconSource(AppIconSourceKind.ClassicEntry, "7-Zip"), AppIconRules.SourceFor([Instance("registry", "7-Zip")]));
        Assert.AreEqual(new AppIconSource(AppIconSourceKind.ClassicEntry, "{GUID}", View32: true), AppIconRules.SourceFor([Instance("registry", "{GUID}", "Machine", "x86")]));
        Assert.AreEqual(new AppIconSource(AppIconSourceKind.ClassicEntry, "Discord", CurrentUser: true), AppIconRules.SourceFor([Instance("registry", "Discord", "User", "x86")]),
            "the current user's hive has one view");
        Assert.AreEqual(new AppIconSource(AppIconSourceKind.StorePackage, "Microsoft.WindowsTerminal_8wekyb3d8bbwe"),
            AppIconRules.SourceFor([Instance("registry", "Terminal"), Instance("msix", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "User")]), "the package's own logo first");
        Assert.AreEqual(new AppIconSource(AppIconSourceKind.ClassicEntry, "App"), AppIconRules.SourceFor([Instance("winget", "Vendor.App"), Instance("registry", "App")]));

        Assert.IsNull(AppIconRules.SourceFor([Instance("winget", "Vendor.App"), Instance("chocolatey", "app")]));
        Assert.IsNull(AppIconRules.SourceFor([Instance("msix", "Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe")]), "a framework keeps its full name and its tile");
        Assert.IsNull(AppIconRules.SourceFor([Instance("registry", @"Vendor\..\Other")]), "an entry name is one key name");
        Assert.IsNull(AppIconRules.SourceFor([]));

        Assert.AreEqual(4, new[]
        {
            new AppIconSource(AppIconSourceKind.ClassicEntry, "App").CacheKey,
            new AppIconSource(AppIconSourceKind.ClassicEntry, "App", View32: true).CacheKey,
            new AppIconSource(AppIconSourceKind.ClassicEntry, "App", CurrentUser: true).CacheKey,
            new AppIconSource(AppIconSourceKind.StorePackage, "App").CacheKey
        }.Distinct(StringComparer.Ordinal).Count(), "each hive, view and kind is its own cached icon");
    }

    [TestMethod]
    [Description("ADR-035 rule 2: an uninstall entry's name can't address another key.")]
    public void AnEntryNameIsOneKeyName()
    {
        Assert.IsTrue(AppIconRules.IsEntryName("{90160000-008C-0000-1000-0000000FF1CE}"));
        Assert.IsTrue(AppIconRules.IsEntryName("7-Zip"));
        Assert.IsFalse(AppIconRules.IsEntryName(null));
        Assert.IsFalse(AppIconRules.IsEntryName("  "));
        Assert.IsFalse(AppIconRules.IsEntryName(@"A\B"));
        Assert.IsFalse(AppIconRules.IsEntryName("A\u0000B"));
        Assert.IsFalse(AppIconRules.IsEntryName(new string('a', 256)));
    }

    [TestMethod]
    [Description("ADR-035 rule 3: DisplayIcon is a path, optionally quoted, optionally followed by a comma and an icon number; a negative number is a resource ID.")]
    public void DisplayIconIsAPathAndAnOptionalIconNumber()
    {
        (string Value, string Path, int Index)[] accepted =
        [
            (@"C:\Program Files\7-Zip\7zFM.exe", @"C:\Program Files\7-Zip\7zFM.exe", 0),
            (@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App\app.exe", 0),
            (@"C:\Program Files\App\app.exe,3", @"C:\Program Files\App\app.exe", 3),
            (@"""C:\Program Files\App\app.exe"", -101", @"C:\Program Files\App\app.exe", -101),
            (@"""C:\Program Files\App\app.exe,2""", @"C:\Program Files\App\app.exe", 2),
            (@"  d:\Apps\Tool\tool.ICO  ", @"d:\Apps\Tool\tool.ICO", 0),
            (@"C:\Program Files\Vendor, Inc\App\app.dll", @"C:\Program Files\Vendor, Inc\App\app.dll", 0),
            (@"C:\Program Files\Vendor, Inc\App\app.dll,1", @"C:\Program Files\Vendor, Inc\App\app.dll", 1),
            (@"C:\Windows\Installer\{A1B2}\icon.ico", @"C:\Windows\Installer\{A1B2}\icon.ico", 0)
        ];
        foreach ((string value, string path, int index) in accepted)
        {
            Assert.IsTrue(AppIconRules.TryParseDisplayIcon(value, out string parsedPath, out int parsedIndex), value);
            Assert.AreEqual(path, parsedPath, value);
            Assert.AreEqual(index, parsedIndex, value);
        }
    }

    [TestMethod]
    [Description("ADR-035 rule 3: network paths, device paths, stream names, relative paths, environment variables and other file types are refused, so the tile stays.")]
    public void PathsThatCouldLeaveThisPcOrAreNotIconFilesAreRefused()
    {
        string[] refused =
        [
            "", "   ", @"\\server\share\icon.ico", @"\\server\share\app.exe,0", @"\\?\C:\Program Files\App\app.exe", @"\\.\C:\app.exe", @"\\?\UNC\server\share\app.exe",
            @"\\.\pipe\name.exe", @"C:\Program Files\App\app.exe:stream", @"C:\Program Files\App\app.exe:icon.ico", @"app.exe", @"App\app.exe", @"\App\app.exe", @"C:app.exe",
            @"C:\Program Files\App\..\Other\app.exe", @"C:\Program Files\.\app.exe", @"%ProgramFiles%\App\app.exe", @"C:\%APPDATA%\app.exe", @"C:/Program Files/App/app.exe",
            @"C:\Program Files\App/app.exe", @"C:\Program Files\App\app.png", @"C:\Program Files\App\app", @"C:\Program Files\App\app.exe.lnk", @"C:\Program Files\App\app.cpl",
            @"C:\Program Files\App\*.exe", @"C:\Program Files\App\a?p.exe", @"C:\Program Files\App\<app>.exe", @"C:\NUL\app.exe", @"C:\Apps\COM1.exe", @"C:\Apps\con.ico",
            @"C:\Apps\aux.backup.dll", @"C:\Apps.\app.exe", @"C:\Apps \app.exe", @"C:\Apps\\app.exe", @"C:\Apps\app.exe\", @"C:\Apps\.exe", @"C:\", @"1:\Apps\app.exe",
            "C:\\Apps\\a\u0001pp.exe", @"""C:\Apps\app.exe", @"C:\Apps\""app"".exe", @"C:\Apps\app.exe,-2147483648", @"file:///C:/Apps/app.exe", @"http://example.invalid/icon.ico",
            @"C:\" + new string('a', 1100) + ".exe"
        ];
        foreach (string value in refused)
        {
            Assert.IsFalse(AppIconRules.TryParseDisplayIcon(value, out string path, out int index), value);
            Assert.AreEqual(string.Empty, path, value);
            Assert.AreEqual(0, index, value);
        }
        Assert.IsFalse(AppIconRules.TryParseDisplayIcon(null, out _, out _));
        Assert.IsFalse(AppIconRules.IsAllowedPath(null));
        Assert.IsTrue(AppIconRules.IsIconFile(@"C:\Apps\app.ICO"));
        Assert.IsFalse(AppIconRules.IsIconFile(@"C:\Apps\app.exe"));
    }

    [TestMethod]
    [Description("ADR-035 rule 3: only a fixed local drive; a mapped network drive, a removable drive, a disc and anything unknown are refused.")]
    public void OnlyAFixedLocalDriveIsRead()
    {
        foreach (DriveType type in Enum.GetValues<DriveType>())
            Assert.AreEqual(type == DriveType.Fixed, AppIconRules.IsAllowedDrive(type), type.ToString());
        Assert.AreEqual(@"D:\", AppIconRules.DriveRoot(@"D:\Apps\app.exe"));
    }

    [TestMethod]
    [Description("ADR-035 rule 3: every folder on the way to the file, then the file, is checked; a link of any kind, a cloud placeholder or a device is refused rather than followed.")]
    public void APathThroughALinkIsRefused()
    {
        CollectionAssert.AreEqual(StepsToAnApp, AppIconRules.PathSteps(@"C:\Program Files\App\app.exe").ToArray());
        CollectionAssert.AreEqual(StepsToARootFile, AppIconRules.PathSteps(@"C:\app.ico").ToArray());

        Assert.IsTrue(AppIconRules.MayPassThrough(FileAttributes.Directory, isLastStep: false));
        Assert.IsTrue(AppIconRules.MayPassThrough(FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.Hidden, isLastStep: false));
        Assert.IsTrue(AppIconRules.MayPassThrough(FileAttributes.Archive, isLastStep: true));
        Assert.IsTrue(AppIconRules.MayPassThrough(FileAttributes.Normal, isLastStep: true));

        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Directory | FileAttributes.ReparsePoint, isLastStep: false), "a junction or folder link");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Archive | FileAttributes.ReparsePoint, isLastStep: true), "a file link");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Archive | FileAttributes.Offline, isLastStep: true), "not on this disk");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Archive | (FileAttributes)0x00400000, isLastStep: true), "a cloud placeholder");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Directory | (FileAttributes)0x00040000, isLastStep: false), "a cloud folder");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Device, isLastStep: true));
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Archive, isLastStep: false), "a file where a folder should be");
        Assert.IsFalse(AppIconRules.MayPassThrough(FileAttributes.Directory, isLastStep: true), "a folder where the file should be");
    }

    [TestMethod]
    [Description("ADR-035 rules 5 and 6: one small icon at the display's scale, a few reads at a time, each abandoned if slow, and an upper limit on what is kept in memory.")]
    public void OneSmallIconAtTheDisplaysScaleWithFixedLimits()
    {
        Assert.AreEqual(32, AppIconRules.PixelSize(1));
        Assert.AreEqual(40, AppIconRules.PixelSize(1.25));
        Assert.AreEqual(48, AppIconRules.PixelSize(1.5));
        Assert.AreEqual(64, AppIconRules.PixelSize(2));
        Assert.AreEqual(128, AppIconRules.PixelSize(10), "never more than four times");
        Assert.AreEqual(32, AppIconRules.PixelSize(0));
        Assert.AreEqual(32, AppIconRules.PixelSize(double.NaN));

        Assert.AreEqual(TimeSpan.FromSeconds(2), AppIconRules.ReadTimeout);
    }
}
