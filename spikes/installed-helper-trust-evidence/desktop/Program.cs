using System.Text.Json;
using PCSteward.Domain;
using PCSteward.Execution;

const string completed = "Completed";
string expected = completed;
Action<HelperLaunchPaths>? afterInitialTrust = null;

if (args.Length != 0)
{
    if (args.Length == 2 && args[0] == "--expect")
    {
        expected = args[1];
    }
    else if (args.Length == 3 && args[0] == "--replacement")
    {
        expected = "elevated-helper-signature-untrusted";
        string requestPath = Path.GetFullPath(args[1]);
        string completedPath = Path.GetFullPath(args[2]);
        afterInitialTrust = _ =>
        {
            File.WriteAllText(requestPath, "replace");
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!File.Exists(completedPath) && DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            if (!File.Exists(completedPath))
            {
                throw new IOException("The privileged replacement coordinator did not complete.");
            }
        };
    }
    else
    {
        Console.Error.WriteLine("Usage: PCSteward.Desktop.exe [--expect <diagnostic> | --replacement <request> <completed>]");
        return 2;
    }
}

ImmutableUpdateJob job = new UpdateJobReview(
    Guid.NewGuid(),
    Guid.NewGuid(),
    new UpdateJobSelection(
        "winget:contoso.widget",
        UpdateProvider.WinGet,
        UpdateSource.WinGet,
        "Contoso.Widget",
        "1.0.0",
        "1.1.0",
        UpdateScope.Machine,
        UpdateArchitecture.X64,
        UpdateChannel.Stable,
        "installed-helper-evidence-v1"))
    .Confirm(DateTimeOffset.UtcNow);

WindowsElevatedHelperTransport transport = afterInitialTrust is null
    ? new WindowsElevatedHelperTransport()
    : new WindowsElevatedHelperTransport(afterInitialTrust);
ElevatedHelperResult result = await transport.ExecuteAsync(job, CancellationToken.None);

Console.WriteLine(JsonSerializer.Serialize(new
{
    outcome = result.Outcome.ToString(),
    result.ExitCode,
    diagnostic = result.SanitizedOutput
}));

if (expected == completed)
{
    return result is
    {
        Outcome: ElevatedHelperOutcome.Completed,
        ExitCode: 0,
        SanitizedOutput: "approved-no-op-installed-helper-evidence"
    }
        ? 0
        : 3;
}

return result.Outcome == ElevatedHelperOutcome.Failed &&
       string.Equals(result.SanitizedOutput, expected, StringComparison.Ordinal)
    ? 0
    : 4;
