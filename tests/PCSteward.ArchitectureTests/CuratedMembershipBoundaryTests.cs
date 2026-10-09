using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>Task 3: FR-RSC-01-05. Guard the disclosure passed to the existing Cancel-default dialog.</summary>
[TestClass]
public sealed class CuratedMembershipBoundaryTests
{
    [TestMethod]
    public void AdvancedMappingsClearDisclosesMembershipAndAutomaticRestrictionsBeforeConfirmation()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) current = current.Parent;
        Assert.IsNotNull(current);
        string source = File.ReadAllText(Path.Combine(current.FullName, "src", "PCSteward.Desktop", "MainWindow.cs"));
        string disclosure = source.Split('\n').Single(line => line.Contains("AddAdvancedClearButton(panel, \"Clear mappings and policies\"", StringComparison.Ordinal));
        StringAssert.Contains(disclosure, "managed app membership");
        StringAssert.Contains(disclosure, "automatic-update restrictions");
        StringAssert.Contains(disclosure, "history and downloads");
        StringAssert.Contains(source, "Content = scope");
        StringAssert.Contains(source, "DefaultButton = ContentDialogButton.Close");
    }
}
