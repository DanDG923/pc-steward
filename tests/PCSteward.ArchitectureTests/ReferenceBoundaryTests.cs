using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class ReferenceBoundaryTests
{
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
