using System.ComponentModel;

namespace PCSteward.Application;

/// <summary>
/// Every Desktop page. Eight are navigation items (ADR-030, plus Diagnostics from Task 64 stage 4); the rest are contextual pages.
/// UI review 2026-11-13 (item 14): the older Scan results, Review decisions and Repository apps pages are gone; Updates and
/// Installed apps do their work.
/// </summary>
public enum ShellPage
{
    Home,
    Updates,
    InstalledApps,
    Activity,
    Decisions,
    Settings,
    About,
    Scan,
    Detail,
    Storage,
    /// <summary>The Data &amp; reset subpage under Settings (Task 73 Phase 4); the enum name is unchanged.</summary>
    Privacy,
    Onboarding,
    UpdateJob,
    /// <summary>Task 64 stage 4 (FR-DIA-01–05, ADR-027): the local diagnostic timeline, a navigation item after Activity (owner's choice).</summary>
    Diagnostics
}

/// <summary>One navigation pane item. <see cref="IsFooter"/> items sit at the bottom of the pane.</summary>
public sealed record ShellNavigationItem(ShellPage Page, string Label, string Glyph, bool IsFooter);

/// <summary>
/// FR-UI-01/02, AC-UX-01/02 (Task 73 Phase 1, ADR-030): portable navigation state for the persistent Desktop shell. It owns the
/// current page and a bounded back stack, and decides which navigation item is highlighted for a contextual page. It has no
/// UI types, starts no work and grants no authority: navigating never scans, checks, approves or updates anything.
/// </summary>
public sealed class ShellNavigation : INotifyPropertyChanged
{
    /// <summary>The most pages kept for Back; the oldest is dropped first.</summary>
    public const int MaximumBackStackDepth = 32;

    private readonly List<ShellPage> backStack = [];

    public ShellNavigation(ShellPage start = ShellPage.Home) => Current = start;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The ADR-030 navigation set: Home, Updates, Installed apps, Activity, Diagnostics and Decisions, then Settings and About in the
    /// footer. Diagnostics (Task 64 stage 4) sits after Activity: Activity says what PC Steward did, Diagnostics sets that beside
    /// what Windows recorded.
    /// </summary>
    public static IReadOnlyList<ShellNavigationItem> Items { get; } =
    [
        new(ShellPage.Home, "Home", "", false),
        new(ShellPage.Updates, "Updates", "", false),
        new(ShellPage.InstalledApps, "Installed apps", "", false),
        new(ShellPage.Activity, "Activity", "", false),
        new(ShellPage.Diagnostics, "Diagnostics", "\uE9D9", false),
        new(ShellPage.Decisions, "Decisions", "", false),
        new(ShellPage.Settings, "Settings", "", true),
        new(ShellPage.About, "About", "", true)
    ];

    public ShellPage Current { get; private set; }

    public bool CanGoBack => backStack.Count != 0;

    public int BackStackDepth => backStack.Count;

    /// <summary>The navigation item to highlight for <see cref="Current"/>.</summary>
    public ShellPage SelectedItem => Current == ShellPage.Detail && backStack.Count != 0 ? ParentOf(backStack[^1]) : ParentOf(Current);

    /// <summary>
    /// Opens <paramref name="page"/>. Returns false when it is already the current page, so no Back entry is added. Leaving a
    /// transient page (app details) for the page directly beneath it is treated like Back, so that page is not stacked twice.
    /// </summary>
    public bool Navigate(ShellPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        if (page == Current) return false;
        if (Current == ShellPage.Onboarding) backStack.Clear();
        else if (!IsTransient(Current)) Push(Current);
        else if (backStack.Count != 0 && backStack[^1] == page) backStack.RemoveAt(backStack.Count - 1);
        Current = page;
        Changed();
        return true;
    }

    /// <summary>Returns to the previous page. Returns false when there is nowhere to go back to.</summary>
    public bool GoBack()
    {
        if (backStack.Count == 0) return false;
        Current = backStack[^1];
        backStack.RemoveAt(backStack.Count - 1);
        Changed();
        return true;
    }

    /// <summary>Opens <paramref name="page"/> and clears Back, for example after factory-reset onboarding.</summary>
    public void Reset(ShellPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        backStack.Clear();
        Current = page;
        Changed();
    }

    /// <summary>True for the eight navigation-pane pages.</summary>
    public static bool IsNavigationItem(ShellPage page) => Items.Any(item => item.Page == page);

    /// <summary>
    /// Pages that are never returned to with Back once left: onboarding (its acknowledgement must not be repeated), app
    /// details (they depend on the row that opened them and are reopened from a list instead) and the update job (Task 73
    /// Phase 2; it is reopened from Updates while it runs, and leaving its Review step cancels that review).
    /// </summary>
    public static bool IsTransient(ShellPage page) => page is ShellPage.Onboarding or ShellPage.Detail or ShellPage.UpdateJob;

    /// <summary>The navigation item that owns a contextual page.</summary>
    public static ShellPage ParentOf(ShellPage page) => page switch
    {
        ShellPage.Scan or ShellPage.Onboarding => ShellPage.Home,
        ShellPage.UpdateJob => ShellPage.Updates,
        ShellPage.Detail => ShellPage.InstalledApps,
        ShellPage.Storage or ShellPage.Privacy => ShellPage.Settings,
        _ => page
    };

    /// <summary>The page's heading, used for the window and for accessible names.</summary>
    public static string TitleOf(ShellPage page) => page switch
    {
        ShellPage.Home => "Home",
        ShellPage.Updates => "Updates",
        ShellPage.InstalledApps => "Installed apps",
        ShellPage.Activity => "Activity",
        ShellPage.Decisions => "Decisions",
        ShellPage.Settings => "Settings",
        ShellPage.About => "About",
        ShellPage.Scan => "Scan",
        ShellPage.Detail => "App details",
        ShellPage.Storage => "Storage",
        ShellPage.Privacy => "Data & reset",
        ShellPage.Onboarding => "Start fresh",
        ShellPage.UpdateJob => "Update job",
        ShellPage.Diagnostics => "Diagnostics",
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    private void Push(ShellPage page)
    {
        backStack.Add(page);
        if (backStack.Count > MaximumBackStackDepth) backStack.RemoveAt(0);
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItem)));
    }
}
