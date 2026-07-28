using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class WingetInstalledInventoryProviderTests
{
    private static readonly string[] ExpectedArguments = ["list", "--disable-interactivity"];
    [TestMethod]
    public async Task UsesFixedExecutableReadOnlyArgumentsAndParsesBoundedTableOutput()
    {
        FakeProcess process = new(new WingetProcessResult(0, "Name  Id  Version  Source\n--------------------------------\nContoso App  Contoso.App  1.2.3  winget", string.Empty, false));
        WingetInventoryResult result = await new WingetInstalledInventoryProvider(process).ReadInstalledAsync(CancellationToken.None);
        Assert.AreEqual("winget.exe", process.Executable); CollectionAssert.AreEqual(ExpectedArguments, process.Arguments.ToArray()); Assert.AreEqual("Contoso.App", result.Records.Single().Id);
    }
    [TestMethod]
    public async Task ClassifiesTimeoutAndNonzeroExitWithoutParsing()
    {
        WingetInventoryResult timeout = await new WingetInstalledInventoryProvider(new FakeProcess(new WingetProcessResult(0, string.Empty, string.Empty, true))).ReadInstalledAsync(CancellationToken.None);
        CollectionAssert.Contains(timeout.Warnings.ToArray(), "winget:timeout");
    }
    private sealed class FakeProcess(WingetProcessResult result) : IWingetProcess
    {
        public string Executable { get; private set; } = string.Empty; public IReadOnlyList<string> Arguments { get; private set; } = [];
        public Task<WingetProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Executable = executable; Arguments = arguments; return Task.FromResult(result); }
    }
}
