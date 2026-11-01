using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/07/13; Task 50 design rev 5 §4.1 (N0): the launcher's pure formatting runs on every platform. The
/// command line follows the MSVCRT quoting rules, the environment block is built only from the spec, and
/// <see cref="NativeLaunchSpec"/> refuses ambiguous paths and unformattable inputs before any native call.
/// </summary>
[TestClass]
public sealed class NativeCommandLineTests
{
    private const string Image = @"C:\Windows\System32\cmd.exe";

    [TestMethod]
    [DataRow("abc", "abc")]
    [DataRow("", "\"\"")]
    [DataRow("a b", "\"a b\"")]
    [DataRow("a\tb", "\"a\tb\"")]
    [DataRow("a\"b", "\"a\\\"b\"")]
    [DataRow(@"C:\dir\", @"C:\dir\")]
    [DataRow(@"C:\my dir\", "\"C:\\my dir\\\\\"")]
    [DataRow("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
    [DataRow("\\\"", "\"\\\\\\\"\"")]
    [DataRow(@"\\server\share", @"\\server\share")]
    [DataRow(@"a\b c", "\"a\\b c\"")]
    public void QuotingFollowsTheMsvcrtRules(string argument, string expected) =>
        Assert.AreEqual(expected, NativeCommandLine.Quote(argument));

    [TestMethod]
    public void TheImageIsAlwaysQuotedAndArgumentsFollowInOrder() =>
        Assert.AreEqual("\"C:\\Windows\\System32\\cmd.exe\" /d /c \"echo hi\" \"\"",
            NativeCommandLine.Build(Image, ["/d", "/c", "echo hi", ""]));

    [TestMethod]
    public void EveryArgumentListRoundTripsThroughTheMsvcrtParser()
    {
        Random random = new(5017);
        const string alphabet = "ab \t\"\\x";
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            string[] arguments = new string[random.Next(0, 5)];
            for (int index = 0; index < arguments.Length; index++)
            {
                char[] characters = new char[random.Next(0, 10)];
                for (int position = 0; position < characters.Length; position++)
                    characters[position] = alphabet[random.Next(alphabet.Length)];
                arguments[index] = new string(characters);
            }
            string commandLine = NativeCommandLine.Build(Image, arguments);
            List<string> parsed = ParseMsvcrt(commandLine);
            Assert.AreEqual(Image, parsed[0], commandLine);
            CollectionAssert.AreEqual(arguments, parsed.Skip(1).ToArray(), commandLine);
        }
    }

    [TestMethod]
    public void ImagesThatCannotBeQuotedAndOversizedCommandLinesAreRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => NativeCommandLine.Build("C:\\a\"b.exe", []));
        Assert.ThrowsExactly<ArgumentException>(() => NativeCommandLine.Build(Image, ["a\0b"]));
        Assert.ThrowsExactly<ArgumentException>(() => NativeCommandLine.Build(Image, [new string('x', NativeCommandLine.MaximumCommandLineLength)]));
    }

    [TestMethod]
    public void TheEnvironmentBlockIsSortedNulSeparatedAndDoubleTerminated()
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal) { ["b"] = "2", ["A"] = "1", ["Path"] = @"C:\x;C:\y" };
        Assert.AreEqual("A=1\0b=2\0Path=C:\\x;C:\\y\0\0", new string(NativeCommandLine.BuildEnvironmentBlock(environment)));
        Assert.AreEqual("\0\0", new string(NativeCommandLine.BuildEnvironmentBlock(new Dictionary<string, string>())));
    }

    [TestMethod]
    [DataRow("dup")]
    [DataRow("equals")]
    [DataRow("empty")]
    [DataRow("nul-value")]
    [DataRow("control-name")]
    public void InvalidEnvironmentsAreRefused(string scenario)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal) { ["SystemRoot"] = @"C:\Windows" };
        switch (scenario)
        {
            case "dup": environment["SYSTEMROOT"] = @"C:\Other"; break;
            case "equals": environment["A=B"] = "1"; break;
            case "empty": environment[string.Empty] = "1"; break;
            case "nul-value": environment["A"] = "1\0"; break;
            case "control-name": environment["A\nB"] = "1"; break;
        }
        Assert.ThrowsExactly<ArgumentException>(() => NativeCommandLine.BuildEnvironmentBlock(environment));
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.ForTests(Image, [], environment, @"C:\Windows"));
    }

    [TestMethod]
    public void TheSpecCopiesItsInputsAndKeepsTheEnvironmentCaseInsensitive()
    {
        List<string> arguments = ["/c", "exit"];
        Dictionary<string, string> environment = new(StringComparer.Ordinal) { ["SystemRoot"] = @"C:\Windows" };
        NativeLaunchSpec spec = NativeLaunchSpec.ForTests(Image, arguments, environment, @"C:\Windows\");
        arguments.Add("mutated");
        environment["Injected"] = "1";
        Assert.AreEqual("/c|exit", string.Join('|', spec.Arguments));
        Assert.AreEqual(1, spec.Environment.Count);
        Assert.AreEqual(@"C:\Windows", spec.Environment["SYSTEMROOT"]);
        Assert.AreEqual(Image, spec.ImagePath);
        Assert.AreEqual(@"C:\Windows\", spec.WorkingDirectory);
    }

    [TestMethod]
    [DataRow("cmd.exe")]
    [DataRow(@"\\server\share\cmd.exe")]
    [DataRow(@"\\?\C:\Windows\cmd.exe")]
    [DataRow(@"C:\Windows\..\cmd.exe")]
    [DataRow(@"C:\Windows\.\cmd.exe")]
    [DataRow(@"C:\Windows\\cmd.exe")]
    [DataRow(@"C:/Windows/cmd.exe")]
    [DataRow("C:\\Win\"dows\\cmd.exe")]
    [DataRow(@"C:\Windows\cmd.exe:stream")]
    [DataRow(@"C:\Windows\cmd*.exe")]
    [DataRow(@"C:\Windows \cmd.exe")]
    [DataRow("C:")]
    public void AmbiguousImageAndWorkingDirectoryPathsAreRefused(string path)
    {
        Dictionary<string, string> environment = new();
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.ForTests(path, [], environment, @"C:\Windows"));
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.ForTests(Image, [], environment, path));
    }

    [TestMethod]
    [DataRow("a\0b")]
    [DataRow("a\nb")]
    public void ArgumentsWithControlCharactersAreRefused(string argument) =>
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.ForTests(Image, [argument], new Dictionary<string, string>(), @"C:\Windows"));

    [TestMethod]
    public async Task InvalidRequestsAreRefusedAndAPreCancelledLaunchCreatesNothing()
    {
        NativeLaunchSpec spec = NativeLaunchSpec.ForTests(Image, ["/c", "exit"], new Dictionary<string, string>(), @"C:\Windows");
        Assert.ThrowsExactly<ArgumentNullException>(() => LaunchJob.RunCoreAsync(null!, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LaunchJob.RunCoreAsync(spec, TimeSpan.Zero, CancellationToken.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LaunchJob.RunCoreAsync(spec, Timeout.InfiniteTimeSpan, CancellationToken.None));

        // A cancelled request returns before any native call, so this runs on every platform.
        NativeProcessObservation observation = await LaunchJob.RunCoreAsync(spec, TimeSpan.FromSeconds(1), new CancellationToken(canceled: true));
        Assert.AreEqual(NativeInvocation.NotStarted, observation.Invocation);
        Assert.IsTrue(observation.Cancelled);
        Assert.IsTrue(observation.AggregateSettled, "nothing was launched, so nothing is left to settle");
        Assert.IsNull(observation.ExitCode);
        Assert.IsFalse(observation.DirectExitObserved || observation.TerminationFailed || observation.LingeringTerminated);
    }

    /// <summary>Reference parser: argv[0] per the CRT program-name rule, then the post-2008 MSVCRT argument rules.</summary>
    internal static List<string> ParseMsvcrt(string commandLine)
    {
        List<string> result = [];
        int index = 0;
        if (commandLine.StartsWith('"'))
        {
            int end = commandLine.IndexOf('"', 1);
            result.Add(commandLine[1..end]);
            index = end + 1;
        }
        else
        {
            while (index < commandLine.Length && commandLine[index] is not (' ' or '\t')) index++;
            result.Add(commandLine[..index]);
        }

        while (true)
        {
            while (index < commandLine.Length && commandLine[index] is ' ' or '\t') index++;
            if (index >= commandLine.Length) return result;
            StringBuilder argument = new();
            bool quoted = false;
            while (index < commandLine.Length)
            {
                char character = commandLine[index];
                if (!quoted && character is ' ' or '\t') break;
                if (character == '\\')
                {
                    int backslashes = 0;
                    while (index < commandLine.Length && commandLine[index] == '\\') { backslashes++; index++; }
                    if (index < commandLine.Length && commandLine[index] == '"')
                    {
                        argument.Append('\\', backslashes / 2);
                        if (backslashes % 2 == 1) { argument.Append('"'); index++; }
                    }
                    else
                    {
                        argument.Append('\\', backslashes);
                    }
                    continue;
                }
                if (character == '"')
                {
                    if (quoted && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                    {
                        argument.Append('"');
                        index += 2;
                        continue;
                    }
                    quoted = !quoted;
                    index++;
                    continue;
                }
                argument.Append(character);
                index++;
            }
            result.Add(argument.ToString());
        }
    }
}
