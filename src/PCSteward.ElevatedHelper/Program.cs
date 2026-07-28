using PCSteward.Execution;

namespace PCSteward.ElevatedHelper;

internal static class Program
{
    private static Task<int> Main(string[] args) => ElevatedHelperEntryPoint.RunAsync(args);
}
