using System.IO.Pipes;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Empirical runtime evidence, not proof of provider or installer termination.</summary>
[TestClass]
public sealed class Task49PipeEvidenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("FR-UPD-14")]
    public async Task AnonymousPipeReaderOwnershipIsObservedAfterCancellationAndWriterClosure()
    {
        using AnonymousPipeServerStream writer = new(PipeDirection.Out, HandleInheritability.Inheritable);
        using AnonymousPipeClientStream pipe = new(PipeDirection.In, writer.ClientSafePipeHandle);
        using StreamReader reader = new(pipe);
        using CancellationTokenSource cancellation = new();
        char[] buffer = new char[1];
        Task<int> read = reader.ReadAsync(buffer.AsMemory(), cancellation.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        Assert.IsFalse(read.IsCompleted, "The empty pipe must have a pending read before cancellation is probed.");
        cancellation.Cancel();
        await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(500))).ConfigureAwait(false);
        bool completedBeforeWriterClose = read.IsCompleted;

        // Model a surviving inherited writer. The fixture owns it and always closes it:
        // a timeout is not reported as successful task cleanup.
        writer.Dispose();
        try
        {
            int count = await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.AreEqual(0, count, "An empty closed writer must produce EOF, not invented output.");
        }
        catch (OperationCanceledException) when (read.IsCanceled)
        {
            // Cancellation is itself an observed terminal task state.
        }

        Assert.IsTrue(read.IsCompleted, "The reader must be observed terminal before fixture teardown.");
        TestContext.WriteLine($"Anonymous pipe runtime evidence: IsAsync={pipe.IsAsync}; completed before writer close={completedBeforeWriterClose}; final status={read.Status}. This does not prove process exit or release while an external writer stays alive.");
    }
}
