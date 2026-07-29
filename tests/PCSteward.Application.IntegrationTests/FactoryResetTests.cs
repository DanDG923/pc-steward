using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class FactoryResetTests
{
    [TestMethod]
    public async Task MarkerLedResetDeletesOnlyTheFixedManifestAndIsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "downloads", "extract"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "downloads", "extract", "payload.bin"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "logs", "events.log"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "database");
            await File.WriteAllTextAsync(Path.Combine(outside, "provider-cache.txt"), "preserved");
            int removedSchedules = 0;

            FactoryReset.ArmForTesting(root);
            Assert.IsTrue(FactoryReset.IsPending(root));
            FactoryResetResult first = await FactoryReset.CompletePendingForTestingAsync(root, _ => { removedSchedules++; return Task.CompletedTask; }, CancellationToken.None);
            FactoryResetResult second = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(second.Succeeded);
            Assert.AreEqual(1, removedSchedules);
            Assert.IsFalse(FactoryReset.IsPending(root));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "downloads")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "logs")));
            Assert.IsTrue(File.Exists(Path.Combine(outside, "provider-cache.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }
}
