using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// UI polish Phase 1: <c>MainWindow</c> is a partial class split into one file per page (MainWindow.cs holds the shared state and
/// helpers). The source contracts read the window as one text, in this fixed order, so a contract keeps covering a page after it
/// moved files. A MainWindow*.cs file missing from <see cref="PartialFiles"/> fails every contract that reads the window, so a new
/// page file can't silently fall outside the contracts. Each file is followed by <see cref="EndOf"/>, so a section that runs to
/// the last member of a file can end there.
/// </summary>
internal static class DesktopWindowSource
{
    public static readonly string[] PartialFiles =
    [
        "MainWindow.cs", "MainWindow.Onboarding.cs", "MainWindow.About.cs", "MainWindow.Home.cs", "MainWindow.InstalledApps.cs",
        "MainWindow.Review.cs", "MainWindow.Storage.cs", "MainWindow.Activity.cs", "MainWindow.Diagnostics.cs",
        "MainWindow.DataAndReset.cs", "MainWindow.Decisions.cs", "MainWindow.Shell.cs", "MainWindow.Scan.cs", "MainWindow.Updates.cs",
        "MainWindow.UpdateJob.cs", "MainWindow.Settings.cs", "MainWindow.Operations.cs", "MainWindow.Detail.cs", "MainWindow.Chocolatey.cs",
        "MainWindow.SelfUpdate.cs", "MainWindow.AppIcons.cs"
    ];

    /// <summary>The whole window source: every partial file, in <see cref="PartialFiles"/> order.</summary>
    public static string Read(string repositoryRoot)
    {
        string desktop = Path.Combine(repositoryRoot, "src", "PCSteward.Desktop");
        string[] onDisk = [.. Directory.EnumerateFiles(desktop, "MainWindow*.cs", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(desktop, path).Replace('\\', '/')).Order(StringComparer.Ordinal)];
        CollectionAssert.AreEquivalent(PartialFiles, onDisk, $"MainWindow partial files changed; update DesktopWindowSource.PartialFiles. On disk: {string.Join(", ", onDisk)}");
        return string.Concat(PartialFiles.Select(file => File.ReadAllText(Path.Combine(desktop, file)) + "\n" + EndOf(file) + "\n"));
    }

    /// <summary>The marker <see cref="Read"/> puts after each file, so a contract can end a section at the end of a file.</summary>
    public static string EndOf(string file) => $"// <end of {file}>";

    /// <summary>Reads a Desktop source file; "MainWindow.cs" means the whole window (<see cref="Read"/>).</summary>
    public static string ReadDesktopFile(string repositoryRoot, string relativePath) =>
        string.Equals(relativePath, "MainWindow.cs", StringComparison.Ordinal)
            ? Read(repositoryRoot)
            : File.ReadAllText(Path.Combine(repositoryRoot, "src", "PCSteward.Desktop", relativePath));
}
