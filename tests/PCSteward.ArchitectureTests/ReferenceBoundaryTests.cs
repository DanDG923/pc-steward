using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class ReferenceBoundaryTests
{
    private static readonly string[] RequiredVisualFoundationBrushes = [
        "StewardCanvasBrush", "StewardSurfaceBrush", "StewardTextBrush", "StewardBrandBrush",
        "StewardFocusBrush", "StewardSuccessBrush", "StewardWarningBrush", "StewardDangerBrush"
    ];
    private static readonly string[] SupportedThemeKeys = ["Default", "Light", "Dark", "HighContrast"];

    [TestMethod]
    public void ScheduledHostDoesNotReferenceExecutionOrElevatedHelper()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "PCSteward.ScheduledHost.csproj");
        IEnumerable<string?> references = XDocument.Load(projectPath).Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"));

        CollectionAssert.DoesNotContain(references.Where(reference => reference is not null).ToArray(), "../PCSteward.Execution/PCSteward.Execution.csproj");
        CollectionAssert.DoesNotContain(references.Where(reference => reference is not null).ToArray(), "../PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj");
    }

    [TestMethod]
    public void ElevatedHelperDoesNotReferenceForbiddenCapabilities()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "PCSteward.ElevatedHelper.csproj");
        IEnumerable<string?> references = XDocument.Load(projectPath).Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include"));

        foreach (string forbidden in new[] { "Persistence.Sqlite", "Inventory.Windows", "Scheduling.Windows", "Storage", "Desktop" })
        {
            Assert.IsFalse(references.Any(reference => reference?.Contains(forbidden, StringComparison.Ordinal) == true));
        }
    }

    [TestMethod]
    public void ElevatedHelperIsAOneShotEntryPointWithoutServiceOrPersistenceCode()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "Program.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "ElevatedHelperEntryPoint.RunAsync(args)");
        foreach (string forbidden in new[] { "while (", "ServiceBase", "Sqlite", "Registry", "TaskScheduler", "File." })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void MainWindowUsesTheApplicationWorkflowInsteadOfAdapters()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "MainWindow.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "InventoryWorkflow");
        foreach (string forbidden in new[] { "SqliteDatabase", "new RegistryLocalInventoryProvider", "new MsixLocalInventoryProvider", "new WingetLocalInventoryProvider" }) Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal));
    }

    [TestMethod]
    public void DesktopCompositionWiresTheProductionStructuredWinGetAuthority()
    {
        string sourcePath = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "App.xaml.cs");
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "new WingetStructuredUpdateCandidateAuthority()");
        StringAssert.Contains(source, "new WingetPostUpdatePackageInventory()");
    }

    [TestMethod]
    public void DesktopVisualFoundationProvidesSemanticResourcesForEverySupportedTheme()
    {
        string desktopDirectory = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop");
        string appXaml = File.ReadAllText(Path.Combine(desktopDirectory, "App.xaml"));
        StringAssert.Contains(appXaml, "Resources/ColorTokens.xaml");
        StringAssert.Contains(appXaml, "Resources/ThemeResources.xaml");
        StringAssert.Contains(appXaml, "Resources/Typography.xaml");
        StringAssert.Contains(appXaml, "Resources/ControlStyles.xaml");

        XDocument tokens = XDocument.Load(Path.Combine(desktopDirectory, "Resources", "ColorTokens.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] themeKeys = tokens.Descendants(presentation + "ResourceDictionary")
            .Attributes(xaml + "Key")
            .Select(attribute => attribute.Value)
            .ToArray();
        CollectionAssert.AreEquivalent(SupportedThemeKeys, themeKeys);

        string[] brushKeys = tokens.Descendants(presentation + "SolidColorBrush")
            .Attributes(xaml + "Key")
            .Select(attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string semanticBrush in RequiredVisualFoundationBrushes)
        {
            CollectionAssert.Contains(brushKeys, semanticBrush);
        }

        string project = File.ReadAllText(Path.Combine(desktopDirectory, "PCSteward.Desktop.csproj"));
        StringAssert.Contains(project, "logo-mark-64.png");
        StringAssert.Contains(project, "pc-steward.ico");

        string styles = File.ReadAllText(Path.Combine(desktopDirectory, "Resources", "ControlStyles.xaml"));
        StringAssert.Contains(styles, "StewardInputStyle");
        StringAssert.Contains(styles, "StewardSelectStyle");
        StringAssert.Contains(styles, "StewardStatusChipStyle");
        StringAssert.Contains(styles, "FocusVisualPrimaryBrush");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("PCSteward.slnx was not found above the test output directory.");
    }
}
