using System.Diagnostics;

namespace PCSteward.Execution;

internal sealed class HelperProcessWork
{
    private readonly TaskCompletionSource transportFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HelperProcessWork(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ExitObserved = ObserveExitAsync(process);
        OwnedCompletion = CompleteOwnershipAsync(ExitObserved, transportFinished.Task, process.Dispose);
    }
    internal HelperProcessWork(Task exitObserved, Action dispose)
    {
        ArgumentNullException.ThrowIfNull(exitObserved);
        ArgumentNullException.ThrowIfNull(dispose);
        ExitObserved = exitObserved;
        OwnedCompletion = CompleteOwnershipAsync(exitObserved, transportFinished.Task, dispose);
    }
    internal Task ExitObserved { get; }
    internal Task OwnedCompletion { get; }
    internal void SealTransport() => transportFinished.TrySetResult();
    private static async Task ObserveExitAsync(Process process) => await process.WaitForExitAsync().ConfigureAwait(false);

    private static async Task CompleteOwnershipAsync(Task exit, Task finished, Action dispose)
    {
        try
        {
            try { await Task.WhenAll(exit, finished).ConfigureAwait(false); }
            catch (Exception) when (!exit.IsCompletedSuccessfully)
            {
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.ConfigureAwait(false);
            }
        }
        finally { dispose(); }
    }
}
