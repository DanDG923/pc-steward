using System.ComponentModel;

namespace PCSteward.Application;

/// <summary>Every Desktop page. The first seven are navigation items (ADR-030); the rest are contextual pages.</summary>
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
    Results,
    Review,
    RepositoryApps,
    Detail,
    Storage,
    Privacy,
    Onboarding
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

    /// <summary>The ADR-030 navigation set: Home, Updates, Installed apps, Activity and Decisions, then Settings and About in the footer.</summary>
    public static IReadOnlyList<ShellNavigationItem> Items { get; } =
    [
        new(ShellPage.Home, "Home", "", false),
        new(ShellPage.Updates, "Updates", "", false),
        new(ShellPage.InstalledApps, "Installed apps", "", false),
        new(ShellPage.Activity, "Activity", "", false),
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

    /// <summary>True for the seven navigation-pane pages.</summary>
    public static bool IsNavigationItem(ShellPage page) => Items.Any(item => item.Page == page);

    /// <summary>
    /// Pages that are never returned to with Back once left: onboarding (its acknowledgement must not be repeated) and app
    /// details (they depend on the row that opened them and are reopened from a list instead).
    /// </summary>
    public static bool IsTransient(ShellPage page) => page is ShellPage.Onboarding or ShellPage.Detail;

    /// <summary>The navigation item that owns a contextual page.</summary>
    public static ShellPage ParentOf(ShellPage page) => page switch
    {
        ShellPage.Scan or ShellPage.Results or ShellPage.Onboarding => ShellPage.Home,
        ShellPage.Review => ShellPage.Updates,
        ShellPage.RepositoryApps or ShellPage.Detail => ShellPage.InstalledApps,
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
        ShellPage.Scan => "Inventory scan",
        ShellPage.Results => "Inventory results",
        ShellPage.Review => "Review decisions",
        ShellPage.RepositoryApps => "Repository apps",
        ShellPage.Detail => "App details",
        ShellPage.Storage => "Storage",
        ShellPage.Privacy => "Privacy & data",
        ShellPage.Onboarding => "Start fresh",
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
