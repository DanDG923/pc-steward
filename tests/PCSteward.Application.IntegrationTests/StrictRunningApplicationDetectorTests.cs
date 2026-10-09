using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-BAT-08: incomplete process evidence cannot become an elevated-launch all-clear.</summary>
[TestClass]
public sealed class StrictRunningApplicationDetectorTests
{
    [TestMethod]
    public async Task StrictSnapshotRejectsTruncationInsteadOfMissingTheRunningTarget()
    {
        string[] processes = [.. Enumerable.Repeat("unrelated", 4096), "Editor"];
        PackageDerivedRunningApplicationDetector detector = new(() => processes, failClosed: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => detector.DetectAsync(Selection(), CancellationToken.None));
    }

    [TestMethod]
    public async Task StrictSnapshotMatchesTargetFromFreshEvidence()
    {
        IReadOnlyCollection<string> processes = [];
        PackageDerivedRunningApplicationDetector detector = new(() => processes, failClosed: true);
        Assert.IsFalse((await detector.DetectAsync(Selection(), CancellationToken.None)).AppearsToBeRunning);
        processes = ["Editor"];
        Assert.IsTrue((await detector.DetectAsync(Selection(), CancellationToken.None)).AppearsToBeRunning);
    }

    private static UpdateJobSelection Selection() =>
        new("winget:editor", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.Editor", "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");
}
