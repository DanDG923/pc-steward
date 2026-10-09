using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PatchStyle.Prototype;

namespace PatchStyle.Prototype.Tests;

[TestClass]
public sealed class HelperProtocolV2Tests
{
    private const string Nonce = "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly int[] OneTwo = [1, 2];

    [TestMethod]
    public void MixedBatchSendsOnlyMachineItemsAndCompletes()
    {
        OperationBatch batch = Batch(Item(0, InstallScope.User), Item(1, InstallScope.Machine), Item(2, InstallScope.Machine, ProviderKind.Chocolatey));
        HelperBatchReceiver receiver = Receiver();
        foreach (byte[] frame in BatchFrameWriter.Write(batch, Nonce, Now)) Assert.IsTrue(receiver.Accept(frame), receiver.Rejection);
        Assert.AreEqual(ReceiverState.Complete, receiver.State);
        CollectionAssert.AreEqual(OneTwo, receiver.AcceptedItems.Select(item => item.Sequence).ToArray());
        Assert.AreEqual(1, batch.UserScopeItems.Count);
    }

    [TestMethod]
    public void WorstCaseFramesStayWithinBound()
    {
        BatchItem[] items = Enumerable.Range(0, OperationBatch.MaximumItems).Select(index => new BatchItem(
            index, OperationKind.Upgrade, ProviderKind.WinGet, SourceKind.WinGet,
            "k" + new string('k', 255 - index.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) + index,
            "P" + new string('p', 250) + "." + index, "1" + new string('1', 127), "2" + new string('2', 127),
            InstallScope.Machine, InstallArchitecture.X64, new string('a', 64))).ToArray();
        IReadOnlyList<byte[]> frames = BatchFrameWriter.Write(new OperationBatch(Guid.NewGuid(), Now, items), Nonce, Now);
        int largest = frames.Max(frame => frame.Length);
        int total = frames.Sum(frame => frame.Length);
        Console.WriteLine($"worst-case frames={frames.Count} largestBytes={largest} totalBytes={total}");
        Assert.IsTrue(largest < BatchFrameWriter.MaximumFrameBytes / 8, "A single item frame should use well under the 16 KiB bound.");
    }

    [TestMethod]
    [DataRow("substitute")]
    [DataRow("reorder")]
    [DataRow("append")]
    [DataRow("truncate")]
    [DataRow("user-scope")]
    [DataRow("unknown-field")]
    [DataRow("duplicate-field")]
    [DataRow("wrong-nonce")]
    [DataRow("wrong-batch")]
    [DataRow("oversize")]
    [DataRow("old-protocol")]
    public void TamperedFramesAreRejected(string tamper)
    {
        OperationBatch batch = Batch(Item(0, InstallScope.Machine), Item(1, InstallScope.Machine), Item(2, InstallScope.Machine));
        List<byte[]> frames = [.. BatchFrameWriter.Write(batch, Nonce, Now)];
        switch (tamper)
        {
            case "substitute": frames[2] = Mutate(frames[2], node => node["packageId"] = "Evil.Package"); break;
            case "reorder": (frames[1], frames[2]) = (frames[2], frames[1]); break;
            case "append": frames.Insert(4, BatchFrameWriter.Frame(BatchFrameWriter.ItemFields(batch.BatchId, Item(3, InstallScope.Machine, packageId: "Extra.Package")))); break;
            case "truncate": frames.RemoveAt(3); break;
            case "user-scope": frames[1] = Mutate(frames[1], node => node["scope"] = "User"); break;
            case "unknown-field": frames[1] = Mutate(frames[1], node => node["arguments"] = "/S"); break;
            case "duplicate-field": frames[1] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(frames[1]).TrimEnd('}') + ",\"packageId\":\"Other.Id\"}"); break;
            case "wrong-nonce": frames[0] = Mutate(frames[0], node => node["nonce"] = new string('0', 64)); break;
            case "wrong-batch": frames[1] = Mutate(frames[1], node => node["batchId"] = Guid.NewGuid().ToString("D")); break;
            case "oversize": frames[1] = new byte[BatchFrameWriter.MaximumFrameBytes + 1]; break;
            case "old-protocol": frames[0] = Mutate(frames[0], node => node["protocolVersion"] = "1"); break;
            default: Assert.Fail(tamper); break;
        }

        HelperBatchReceiver receiver = Receiver();
        bool allAccepted = frames.All(frame => receiver.Accept(frame));
        Assert.IsFalse(allAccepted, tamper);
        Assert.AreEqual(ReceiverState.Rejected, receiver.State);
        Assert.AreEqual(0, receiver.AcceptedItems.Count);
        Console.WriteLine($"{tamper} -> {receiver.Rejection}");
    }

    [TestMethod]
    public void ReplayedNonceIsRejected()
    {
        HashSet<string> used = new(StringComparer.Ordinal);
        OperationBatch batch = Batch(Item(0, InstallScope.Machine));
        IReadOnlyList<byte[]> frames = BatchFrameWriter.Write(batch, Nonce, Now);
        HelperBatchReceiver first = new(Nonce, used, () => Now);
        Assert.IsTrue(frames.All(frame => first.Accept(frame)));
        HelperBatchReceiver replay = new(Nonce, used, () => Now);
        Assert.IsFalse(replay.Accept(frames[0]));
        Assert.AreEqual("nonce-replay", replay.Rejection);
    }

    [TestMethod]
    public void ExpiredBatchAndStaleRequestAreRejected()
    {
        OperationBatch batch = Batch(Item(0, InstallScope.Machine));
        HelperBatchReceiver expired = new(Nonce, new HashSet<string>(), () => Now + OperationBatch.Lifetime);
        Assert.IsFalse(expired.Accept(BatchFrameWriter.Write(batch, Nonce, Now + OperationBatch.Lifetime)[0]));
        Assert.AreEqual("batch-expired", expired.Rejection);
        HelperBatchReceiver stale = new(Nonce, new HashSet<string>(), () => Now.AddMinutes(3));
        Assert.IsFalse(stale.Accept(BatchFrameWriter.Write(batch, Nonce, Now)[0]));
        Assert.AreEqual("request-expired", stale.Rejection);
    }

    [TestMethod]
    public void BatchConstructionRejectsDuplicatesGapsAndOversize()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Batch(Item(0, InstallScope.Machine), Item(1, InstallScope.Machine, packageId: "Vendor.App0")));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationBatch(Guid.NewGuid(), Now, [Item(0, InstallScope.Machine), Item(2, InstallScope.Machine)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BatchItem(OperationBatch.MaximumItems, OperationKind.Install, ProviderKind.WinGet, SourceKind.WinGet, "k", "A.B", null, "1.0", InstallScope.User, InstallArchitecture.X64, null));
        Assert.ThrowsExactly<ArgumentException>(() => new BatchItem(0, OperationKind.Install, ProviderKind.Chocolatey, SourceKind.ChocolateyCommunity, "k", "git", null, "1.0", InstallScope.User, InstallArchitecture.X64, null));
        Assert.ThrowsExactly<ArgumentException>(() => new BatchItem(0, OperationKind.Install, ProviderKind.WinGet, SourceKind.WinGet, "k", "A.B", "1.0", "2.0", InstallScope.User, InstallArchitecture.X64, null));
    }

    [TestMethod]
    public void CancelTakesEffectOnlyBetweenItems()
    {
        OperationBatch batch = Batch(Item(0, InstallScope.Machine), Item(1, InstallScope.Machine), Item(2, InstallScope.Machine));
        HelperExecutionSession session = new(batch.MachineScopeItems);
        Assert.IsTrue(session.TryStartNext(out BatchItem? first));
        session.RequestCancelRemaining();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.TryStartNext(out _));
        session.Finish();
        Assert.IsFalse(session.TryStartNext(out _));
        Assert.AreEqual(0, first!.Sequence);
        CollectionAssert.AreEqual(OneTwo, session.NotStarted.Select(item => item.Sequence).ToArray());
    }

    [TestMethod]
    public void DigestChangesWithOrderAndAnyField()
    {
        BatchItem a = Item(0, InstallScope.Machine, packageId: "Vendor.A");
        BatchItem b = Item(1, InstallScope.Machine, packageId: "Vendor.B");
        DateTimeOffset approved = Now;
        Guid id = Guid.NewGuid();
        string original = new OperationBatch(id, approved, [a, b]).Digest;
        BatchItem a2 = Item(0, InstallScope.Machine, packageId: "Vendor.B");
        BatchItem b2 = Item(1, InstallScope.Machine, packageId: "Vendor.A");
        Assert.AreNotEqual(original, new OperationBatch(id, approved, [a2, b2]).Digest);
        Assert.AreNotEqual(original, new OperationBatch(id, approved.AddSeconds(1), [a, b]).Digest);
    }

    private static HelperBatchReceiver Receiver() => new(Nonce, new HashSet<string>(StringComparer.Ordinal), () => Now);

    private static OperationBatch Batch(params BatchItem[] items) => new(Guid.NewGuid(), Now, items);

    private static BatchItem Item(int sequence, InstallScope scope, ProviderKind provider = ProviderKind.WinGet, string? packageId = null) => provider == ProviderKind.Chocolatey
        ? new(sequence, OperationKind.Upgrade, provider, SourceKind.ChocolateyCommunity, "product:" + sequence, packageId ?? "app" + sequence, "1.0", "1.1", InstallScope.Machine, InstallArchitecture.Neutral, null)
        : new(sequence, sequence % 2 == 0 ? OperationKind.Upgrade : OperationKind.Install, provider, SourceKind.WinGet, "product:" + sequence, packageId ?? "Vendor.App" + sequence, sequence % 2 == 0 ? "1.0" : null, "1.1", scope, InstallArchitecture.X64, sequence % 2 == 0 ? null : new string('b', 64));

    private static byte[] Mutate(byte[] frame, Action<JsonObject> change)
    {
        JsonObject node = JsonNode.Parse(frame)!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
