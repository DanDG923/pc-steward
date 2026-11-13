namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-035 (UI review 2026-11-13, item 20; FR-UI-01, FR-DAT-12, FR-INV-01–04): an app's own icon is read by the desktop lists
/// only, as data, and nothing about it is stored. These contracts keep the icon reader out of the elevated helper, the scheduled
/// check and the scan, keep the scan's registry values as they were, and keep the reader away from the Windows shell, from
/// loading code and from writing anything.
/// </summary>
[TestClass]
public sealed class AppIconBoundaryTests
{
    private static readonly string[] IconFiles =
    [
        "PCSteward.Application/AppIcons.cs", "PCSteward.Desktop/AppIconLoader.cs", "PCSteward.Desktop/MainWindow.About.cs",
        "PCSteward.Desktop/MainWindow.AppIcons.cs", "PCSteward.Desktop/MainWindow.InstalledApps.cs", "PCSteward.Desktop/StewardPresentation.cs"
    ];

    private static readonly string[] DisplayIconFiles = ["PCSteward.Application/AppIcons.cs", "PCSteward.Desktop/AppIconLoader.cs"];
    private static readonly string[] LoaderFiles = ["PCSteward.Desktop/AppIconLoader.cs", "PCSteward.Desktop/MainWindow.AppIcons.cs"];

    [TestMethod]
    [Description("ADR-035 rules 1 and 2: only the desktop app's lists read an icon; the helper, the scheduled check, the scan and every other project never name the icon code or DisplayIcon, and the scan reads the same five values as before.")]
    public void OnlyTheDesktopListsReadAnAppsIcon()
    {
        Dictionary<string, string> sources = SourceFiles();

        CollectionAssert.AreEqual(IconFiles, Naming(sources, "AppIcon"), "Only these files may name the icon code.");
        CollectionAssert.AreEqual(DisplayIconFiles, Naming(sources, "DisplayIcon"),
            "DisplayIcon is read in one place, under the portable rules.");
        CollectionAssert.AreEqual(LoaderFiles, Naming(sources, "AppIconLoader"),
            "The reader is used only by the window's lists.");
        StringAssert.Contains(sources["PCSteward.Desktop/MainWindow.AppIcons.cs"], "private readonly AppIconLoader appIcons = new();");

        // The scan is unchanged: the same five values, and no icon.
        StringAssert.Contains(sources["PCSteward.Inventory.Windows/RegistryInventoryProvider.cs"],
            "internal static readonly string[] InventoryValueNames = [\"DisplayName\", \"DisplayVersion\", \"Publisher\", \"InstallDate\", \"SystemComponent\"];");

        // The portable rules open no file and no registry key themselves: they are given text, attributes and a stream.
        string rules = sources["PCSteward.Application/AppIcons.cs"];
        foreach (string forbidden in new[] { "Microsoft.Win32", "new FileStream", "File.Open", "File.Read", "File.GetAttributes", "DriveInfo", "DllImport", "LibraryImport", "Environment.Expand" })
            Assert.IsFalse(rules.Contains(forbidden, StringComparison.Ordinal), $"the portable icon rules read nothing themselves: {forbidden}");
    }

    [TestMethod]
    [Description("ADR-035 rules 3, 4 and 6: the reader checks the path under the portable rules before it opens the file, opens it for reading only, never runs it, loads it or asks the shell for its icon, and writes nothing.")]
    public void TheIconReaderOpensAFileAsDataAndKeepsNothing()
    {
        string loader = SourceFiles()["PCSteward.Desktop/AppIconLoader.cs"];

        // Read-only, the value unexpanded, and every rule before the open.
        StringAssert.Contains(loader, "OpenSubKey(UninstallEntries + source.Identity, writable: false)");
        StringAssert.Contains(loader, "GetValue(AppIconRules.RegistryValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string");
        string read = Between(loader, "private static byte[]? ReadClassicIconFile(", "private static async Task<ImageSource?> ReadStoreLogoAsync(");
        string[] order =
        [
            "AppIconRules.IsEntryName(source.Identity)", "AppIconRules.TryParseDisplayIcon(displayIcon, out string path, out int index)",
            "AppIconRules.IsAllowedDrive(new DriveInfo(AppIconRules.DriveRoot(path)).DriveType)", "AppIconRules.MayPassThrough(File.GetAttributes(steps[step]), isLastStep: step == steps.Count - 1)",
            "new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.None)", "AppIconFile.Read(file, AppIconRules.IsIconFile(path), index, pixels)"
        ];
        int at = -1;
        foreach (string step in order)
        {
            int next = read.IndexOf(step, StringComparison.Ordinal);
            Assert.IsTrue(next > at, $"missing or out of order in the classic icon read: {step}");
            at = next;
        }
        Assert.AreEqual(1, Count(loader, "new(path,"), "the file is opened once, in one way");

        // Never run, never loaded as code, never the shell, never a write, never the network.
        foreach (string forbidden in new[]
        {
            "DllImport", "LibraryImport", "LoadLibrary", "NativeLibrary", "Assembly.Load", "Process.Start", "ProcessStartInfo", "SHGetFileInfo", "ExtractIcon", "ExtractAssociatedIcon",
            "System.Drawing", "StorageFile", "GetThumbnail", "ExpandEnvironmentVariables", "FileAccess.Write", "FileAccess.ReadWrite", "File.Write", "File.Create", "File.Copy", "Directory.Create",
            "writable: true", "CreateSubKey", "SetValue", "HttpClient", "new Uri(", "UriSource", "Sqlite", "Repository", "ApplicationData"
        })
            Assert.IsFalse(loader.Contains(forbidden, StringComparison.Ordinal), $"the icon reader must not use: {forbidden}");

        // Kept in memory for the session with an upper limit, a few reads at a time, each abandoned if slow.
        StringAssert.Contains(loader, "if (cache.Count < AppIconRules.MaximumCachedIcons) cache[key] = icon;");
        StringAssert.Contains(loader, "if (runningReads < AppIconRules.MaximumConcurrentReads)");
        Assert.AreEqual(3, Count(loader, ".WaitAsync(AppIconRules.ReadTimeout)"));
        StringAssert.Contains(loader, "catch (Exception) { icon = null; }");
    }

    [TestMethod]
    [Description("ADR-035 rule 7 and AC-UX-01: every tile starts as its two letters, the icon is decoration kept out of what a screen reader reads, a reused row never shows another app's icon, and About says what is read.")]
    public void TheTwoLetterTileIsTheFallbackAndTheIconIsDecoration()
    {
        Dictionary<string, string> sources = SourceFiles();
        string root = CompiledIlScanner.FindRepositoryRoot();
        string window = DesktopWindowSource.Read(root);
        string icons = sources["PCSteward.Desktop/MainWindow.AppIcons.cs"];
        string template = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Desktop", "Resources", "DataTemplates.xaml"));

        // One place builds an app's tile, and it starts from the letters.
        Assert.AreEqual(1, Count(window, "StewardVisuals.Avatar("));
        StringAssert.Contains(icons, "Border tile = StewardVisuals.Avatar(StewardPresentation.Initials(name));");
        Assert.AreEqual(5, Count(window, " = AppTile("), "Home, the three Updates groups and app details");

        // Installed apps reuses its rows: each starts again from its letters, and an icon that arrives late is shown only on its own app.
        StringAssert.Contains(sources["PCSteward.Desktop/MainWindow.InstalledApps.cs"], "ShowAppIconsInRows(list);");
        StringAssert.Contains(icons, "image.Source = null;");
        StringAssert.Contains(icons, "letters.Visibility = Visibility.Visible;");
        StringAssert.Contains(icons, "if (args.InRecycleQueue || args.Item is not ApplicationListItem item");
        StringAssert.Contains(icons, "if (!ReferenceEquals(container.Content, item)) return;");
        StringAssert.Contains(icons, "if (icon is not null && !windowClosed) show(icon);");
        StringAssert.Contains(icons, "AppIconRules.PixelSize(Content?.XamlRoot?.RasterizationScale ?? 1)");

        // Decoration: in the raw view only, in both the template and the built tiles; the row's name is unchanged.
        StringAssert.Contains(template, "AutomationProperties.Name=\"{Binding AccessibleSummary}\"");
        StringAssert.Contains(template, "Text=\"{Binding Initials}\"");
        string image = Between(template, "<Image", "/>");
        foreach (string attribute in new[] { "AutomationProperties.AccessibilityView=\"Raw\"", "AutomationProperties.AutomationId=\"AppIcon\"", "Visibility=\"Collapsed\"" })
            StringAssert.Contains(image, attribute);
        string built = Between(sources["PCSteward.Desktop/StewardPresentation.cs"], "public static Image AppIconImage(ImageSource icon)", "return image;");
        StringAssert.Contains(built, "AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);");
        StringAssert.Contains(built, "AutomationProperties.SetAutomationId(image, \"AppIcon\");");

        // About's privacy text says what is read, and the launch test counts what was shown.
        StringAssert.Contains(sources["PCSteward.Desktop/MainWindow.About.cs"], "privacy.Children.Add(PageCopy(AppIconRules.PrivacyNote));");
        string smoke = File.ReadAllText(Path.Combine(root, "scripts", "Test-DesktopLaunchSmokeCI.ps1"));
        StringAssert.Contains(smoke, "'LAYOUT-APP-ICONS shown='");
        StringAssert.Contains(smoke, "'the app exited while showing app icons (code '");
    }

    /// <summary>Every C# source file under src, by its path from src with forward slashes; build output is left out.</summary>
    private static Dictionary<string, string> SourceFiles()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string separator = Path.DirectorySeparatorChar.ToString();
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(separator + "bin" + separator, StringComparison.Ordinal) && !path.Contains(separator + "obj" + separator, StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(src, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
    }

    private static string[] Naming(Dictionary<string, string> sources, string word) =>
        [.. sources.Where(source => source.Value.Contains(word, StringComparison.Ordinal)).Select(source => source.Key).Order(StringComparer.Ordinal)];

    private static string Between(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, $"Could not find section from {startMarker} to {endMarker}.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }
}
