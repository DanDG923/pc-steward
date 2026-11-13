using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, item 18 (FR-UI-01/05, AC-UX-01 best effort): the Installed apps column headings sort the list, including
/// by status, and turn the order round when selected again. Presentation only: sorting never changes what is listed.
/// </summary>
[TestClass]
public sealed class UiReviewSortingPresentationTests
{
    private static readonly string[] Options = ["Name", "Status", "Installed for", "Source", "Publisher"];
    private static readonly string[] Headings = ["Name", "Version", "Installed for", "Status"];

    [TestMethod]
    [Description("FR-UI-01/05 (item 18): Name, Status and Installed for are headings that sort; Version is not; the Sort by list offers the same fields plus Source and Publisher.")]
    public void TheHeadingsThatSortAreNamed()
    {
        CollectionAssert.AreEqual(Options, InstalledAppsFilters.SortOptions.ToArray());
        CollectionAssert.AreEqual(Headings, InstalledAppsFilters.ColumnHeadings.ToArray());
        Assert.IsTrue(InstalledAppsFilters.SortsWhenSelected("Name") && InstalledAppsFilters.SortsWhenSelected("Status") && InstalledAppsFilters.SortsWhenSelected("Installed for"));
        Assert.IsFalse(InstalledAppsFilters.SortsWhenSelected("Version"), "version numbers of different apps are not comparable");
        Assert.AreEqual("Sort by Status", InstalledAppsFilters.SortHeadingName("Status"));
        Assert.AreEqual(string.Empty, InstalledAppsFilters.SortState(false, true));
        Assert.AreEqual("Sorted ascending", InstalledAppsFilters.SortState(true, false));
        Assert.AreEqual("Sorted descending", InstalledAppsFilters.SortState(true, true));
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 18): sorting by status puts what can be acted on first, in the order the status filter lists; ties stay in name order whichever way the sort runs.")]
    public void StatusSortsInTheOrderTheFilterLists()
    {
        InventoryRow[] rows =
        [
            Row("Zeta", "Current", "Machine"), Row("Alpha", "Current", "User"), Row("Mid", "VerifiedUpdate", "User"), Row("Beta", "Unmanaged", "Machine"),
            Row("Gamma", "ReviewRequired", "Unknown"), Row("Delta", StatusVocabulary.InstalledKey, "User"), Row("Odd", "SomethingNew", "User")
        ];
        CollectionAssert.AreEqual(new List<string> { "Mid", "Gamma", "Alpha", "Zeta", "Beta", "Delta", "Odd" }, Names(InstalledAppsFilters.Sorted(rows, "Status", false)));
        CollectionAssert.AreEqual(new List<string> { "Odd", "Delta", "Beta", "Alpha", "Zeta", "Gamma", "Mid" }, Names(InstalledAppsFilters.Sorted(rows, "Status", true)), "ties (Alpha, Zeta) stay in name order");
        Assert.AreEqual(0, StatusVocabulary.SortRank("VerifiedUpdate"));
        Assert.AreEqual(1, StatusVocabulary.SortRank("ReviewRequired"));
        Assert.AreEqual(StatusVocabulary.InventoryStatuses.Count, StatusVocabulary.SortRank("SomethingNew"), "an unknown status sorts last");
        Assert.AreEqual(StatusVocabulary.InventoryStatuses.Count, StatusVocabulary.SortRank(null));

        CollectionAssert.AreEqual(new List<string> { "Alpha", "Beta", "Delta", "Gamma", "Mid", "Odd", "Zeta" }, Names(InstalledAppsFilters.Sorted(rows, "Name", false)));
        CollectionAssert.AreEqual(new List<string> { "Zeta", "Odd", "Mid", "Gamma", "Delta", "Beta", "Alpha" }, Names(InstalledAppsFilters.Sorted(rows, "Name", true)));
        // Installed for sorts by the words the column shows: Everyone on this PC, Just you, Not known.
        CollectionAssert.AreEqual(new List<string> { "Beta", "Zeta", "Alpha", "Delta", "Mid", "Odd", "Gamma" }, Names(InstalledAppsFilters.Sorted(rows, "Installed for", false)));
        CollectionAssert.AreEqual(Names(InstalledAppsFilters.Sorted(rows, "Name", false)), Names(InstalledAppsFilters.Sorted(rows, "No such field", false)), "an unknown field sorts by name");
        Assert.HasCount(rows.Length, InstalledAppsFilters.Sorted(rows, "Status", true), "sorting never drops a row");
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 18): the app list keeps the chosen order and direction, and the older call keeps the direction it had.")]
    public void TheListKeepsTheChosenOrderAndDirection()
    {
        InventoryExperienceViewModel viewModel = new();
        Guid a = Guid.NewGuid(); Guid b = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(a, "product:a", "Alpha"), new CanonicalProduct(b, "product:b", "Beta")],
            [new InstallationInstance(Guid.NewGuid(), a, "registry", "alpha", "1.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), b, "registry", "beta", "1.0", "User", "x64", "Contoso")]);
        Assert.IsFalse(viewModel.SortDescending);
        CollectionAssert.AreEqual(new List<string> { "Alpha", "Beta" }, viewModel.VisibleRows.Select(row => row.Name).ToList());

        viewModel.SetFilters(string.Empty, viewModel.StatusFilter, viewModel.PublisherFilter, viewModel.SourceFilter, viewModel.ScopeFilter, "Name", true);
        Assert.IsTrue(viewModel.SortDescending);
        CollectionAssert.AreEqual(new List<string> { "Beta", "Alpha" }, viewModel.VisibleRows.Select(row => row.Name).ToList());

        viewModel.SetFilters(string.Empty, viewModel.StatusFilter, viewModel.PublisherFilter, viewModel.SourceFilter, viewModel.ScopeFilter, "Installed for");
        Assert.IsTrue(viewModel.SortDescending, "a call that does not name a direction keeps the one in use");
        CollectionAssert.AreEqual(new List<string> { "Beta", "Alpha" }, viewModel.VisibleRows.Select(row => row.Name).ToList(), "Just you, then Everyone on this PC");
    }

    private static List<string> Names(IEnumerable<InventoryRow> rows) => [.. rows.Select(row => row.Name)];

    private static InventoryRow Row(string name, string status, string scope) =>
        new(Guid.NewGuid(), name, "Contoso", "winget", scope, "1.0", status, "Application", [], name);
}
