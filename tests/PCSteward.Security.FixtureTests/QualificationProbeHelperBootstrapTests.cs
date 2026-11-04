using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.ProbeFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §2 and §3 P0, §7 H6 and H8 (slice S3): the strict probe
/// grammar, the dispatch order and the mutual refusal with all three existing parsers; the P0 exit codes. Portable: the
/// grammar, the dispatch, exit 20 before any connection and 21 without host facts. Windows-only (reported as failed elsewhere):
/// the connected P0 against a real restricted pipe (22, 23) and the native probe port's refusals.
/// </summary>
[TestClass]
public sealed class QualificationProbeHelperBootstrapTests
{
    private const string Pipe = "pcsteward-reviewed-0123456789abcdef0123456789abcdef";
    private const string Kind = "qualify-chocolatey-helper";

    // ---- H6: grammar ----

    [TestMethod]
    public void TheExactProbeLineIsAcceptedAndCarriesOnlyPidPipeAndNonce()
    {
        Assert.IsTrue(ReviewedHelperProbeBootstrap.TryParse(Probe(), out ReviewedHelperProbeBootstrap? bootstrap));
        Assert.AreEqual(new ReviewedHelperProbeBootstrap(4242, Pipe, F.Nonce), bootstrap);
        Assert.IsTrue(ReviewedHelperProbeBootstrap.TryParse(Probe(pipe: WindowsReviewedHelperTransport.CreatePipeName()), out _));
        Assert.AreEqual(Kind, Runtime(QualificationProbeWireCodec.Kind), "the parser's kind literal is the codec's");
        Assert.IsTrue(ReviewedHelperProbeBootstrap.SelectsProbe(Probe()));
    }

    [TestMethod]
    [DataRow("revision-1-order")]
    [DataRow("kind-value-other")]
    [DataRow("kind-value-case")]
    [DataRow("kind-key-case")]
    [DataRow("protocol-key-case")]
    [DataRow("kind-leading-space")]
    [DataRow("kind-trailing-space")]
    [DataRow("value-trailing-space")]
    [DataRow("protocol-1")]
    [DataRow("protocol-3")]
    [DataRow("pid-before-protocol")]
    [DataRow("nonce-before-pipe")]
    [DataRow("repeated-key")]
    [DataRow("unknown-key")]
    [DataRow("protocol-1-locator")]
    [DataRow("uppercase-locator")]
    [DataRow("locator-trailing-newline")]
    [DataRow("lowercase-nonce")]
    [DataRow("short-nonce")]
    [DataRow("pid-zero")]
    [DataRow("pid-negative")]
    [DataRow("pid-signed")]
    [DataRow("pid-space")]
    [DataRow("pid-overflow")]
    [DataRow("nine-arguments")]
    [DataRow("eleven-arguments")]
    [DataRow("twelve-arguments")]
    [DataRow("null-value")]
    [DataRow("null-array")]
    public void AnyOtherLineIsRefusedByTheProbeParser(string scenario)
    {
        string[]? arguments = Variant(scenario);
        Assert.IsFalse(ReviewedHelperProbeBootstrap.TryParse(arguments!, out ReviewedHelperProbeBootstrap? bootstrap), scenario);
        Assert.IsNull(bootstrap);
    }

    [TestMethod]
    public void EveryPermutationOfTheProbePairsIsRefused()
    {
        string[][] pairs = [["--kind", Kind], ["--protocol", "2"], ["--desktop-pid", "4242"], ["--pipe", Pipe], ["--nonce", F.Nonce]];
        int permutations = 0;
        foreach (int[] order in Permutations([0, 1, 2, 3, 4]))
        {
            string[] line = [.. order.SelectMany(index => pairs[index])];
            bool identity = order.SequenceEqual([0, 1, 2, 3, 4]);
            Assert.AreEqual(identity, ReviewedHelperProbeBootstrap.TryParse(line, out _), string.Join(' ', line));
            permutations++;
        }
        Assert.AreEqual(120, permutations);
    }

    // ---- H6: mutual refusal ----

    [TestMethod]
    public void AllThreeExistingParsersRefuseEveryProbeLine()
    {
        foreach (string[] line in TenArgumentLines())
        {
            string text = string.Join(' ', line);
            Assert.IsFalse(ReviewedHelperBootstrap.TryParse(line, out _), "protocol-2 TryParse: " + text);
            Assert.IsFalse(ReviewedHelperBootstrap.SelectsProtocol2(line), "SelectsProtocol2: " + text);
            Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProtocol2(line), "entry SelectsProtocol2: " + text);
            Assert.IsFalse(HelperBootstrap.TryParse(line, out _), "protocol-1 TryParse: " + text);
        }
    }

    [TestMethod]
    public void ElementsTwoToNineOfAProbeLineAreAValidUpdateLineThatOnlyTheUpdateParserAccepts()
    {
        string[] update = Probe()[2..];
        Assert.AreEqual(8, update.Length);
        Assert.IsTrue(ReviewedHelperBootstrap.TryParse(update, out ReviewedHelperBootstrap? reviewed), "the tail is a valid protocol-2 update line");
        Assert.AreEqual(new ReviewedHelperBootstrap(4242, Pipe, F.Nonce), reviewed);
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProtocol2(update));
        Assert.IsFalse(ReviewedHelperProbeBootstrap.TryParse(update, out _), "the probe parser refuses the update line");
        Assert.IsFalse(ReviewedHelperProbeBootstrap.SelectsProbe(update));
        Assert.IsFalse(ReviewedHelperBootstrap.TryParse(Probe(), out _), "the update parser refuses the full probe line");
    }

    [TestMethod]
    public void TheProbeParserRefusesEveryEightArgumentUpdateLineAndPermutation()
    {
        string[][] pairs = [["--protocol", "2"], ["--desktop-pid", "4242"], ["--pipe", Pipe], ["--nonce", F.Nonce]];
        foreach (int[] order in Permutations([0, 1, 2, 3]))
        {
            string[] line = [.. order.SelectMany(index => pairs[index])];
            Assert.IsTrue(ReviewedHelperBootstrap.TryParse(line, out _), "a valid update line");
            Assert.IsFalse(ReviewedHelperProbeBootstrap.TryParse(line, out _), string.Join(' ', line));
            Assert.IsFalse(ReviewedHelperProbeBootstrap.SelectsProbe(line));
        }
        string[] protocol1 = ["--protocol", "1", "--desktop-pid", "4242", "--pipe", "pcsteward-elevated-0123456789abcdef0123456789abcdef", "--nonce", F.Nonce];
        Assert.IsTrue(HelperBootstrap.TryParse(protocol1, out _));
        Assert.IsFalse(ReviewedHelperProbeBootstrap.TryParse(protocol1, out _));
        Assert.IsFalse(ReviewedHelperProbeBootstrap.SelectsProbe(protocol1));
    }

    [TestMethod]
    public void TheDispatchSelectsTheProbeOnlyForTenArgumentsWithTheKindKeyFirst()
    {
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProbe(Probe()));
        // A malformed ten-argument line with --kind first is still the probe's, whose full parse refuses it (never an update path).
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProbe(Probe(kind: "other")));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProbe(Variant("revision-1-order")!));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProbe(Variant("kind-key-case")!));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProbe(Variant("eleven-arguments")!));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProbe(["--kind", Kind]));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProbe(null!));
    }

    [TestMethod]
    public async Task TheRevisionOneOrderAndEveryUnselectedTenArgumentLineFallToProtocolOneWhichExitsWith20()
    {
        foreach (string scenario in new[] { "revision-1-order", "kind-key-case", "kind-leading-space", "kind-trailing-space" })
            Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Variant(scenario)!).WaitAsync(F.Wait), scenario);
    }

    // ---- H8: P0 exit codes ----

    [TestMethod]
    public async Task H8AMalformedProbeLineExitsWith20BeforeAnyConnection()
    {
        // The parse refuses before the elevation check, so this holds on every host.
        foreach (string scenario in new[] { "kind-value-other", "lowercase-nonce", "protocol-1-locator", "pid-zero", "nonce-before-pipe" })
            Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Variant(scenario)!).WaitAsync(F.Wait), scenario);
    }

    [TestMethod]
    public async Task H8AWellFormedProbeLineExitsWith20WhenNotElevatedOrNotWindows()
    {
        if (OperatingSystem.IsWindows() && IsElevated())
            Assert.Inconclusive("an elevated test host would attempt the pipe connection");
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Probe(pipe: WindowsReviewedHelperTransport.CreatePipeName())).WaitAsync(F.Wait));
    }

    [TestMethod]
    public async Task H8NoHostFactsExitWith21BeforeAnyConnection()
    {
        CountingProbePort port = new(null);
        int exit = await ReviewedHelperProbeBootstrap.RunAsync(new ReviewedHelperProbeBootstrap(4242, Pipe, F.Nonce), host: null, port, new FakeCapability(),
            TimeProvider.System).WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.LayoutUnavailable, exit);
        Assert.AreEqual(21, exit);
        Assert.AreEqual(0, port.OpenCalls);
    }

    [TestMethod]
    public async Task H8AConnectionTheDesktopPipeRefusesExitsWith22()
    {
        N1NativeFixture.RequireWindows("probe bootstrap connection refusal");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        // A restricted pipe for SYSTEM only: this user's connection is denied at once.
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, "S-1-5-18");
        CountingProbePort port = new(new FakePeer());
        int exit = await ReviewedHelperProbeBootstrap.RunAsync(new ReviewedHelperProbeBootstrap(Environment.ProcessId, name, F.Nonce), Host(), port,
            new FakeCapability(), TimeProvider.System).WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.ConnectionFailed, exit);
        Assert.AreEqual(0, port.OpenCalls);
    }

    [TestMethod]
    public async Task H8AServerPidOtherThanTheDesktopPidIsRefusedWith23BeforeThePeerIsOpened()
    {
        N1NativeFixture.RequireWindows("probe bootstrap server PID check");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
        Task connected = server.WaitForConnectionAsync();
        CountingProbePort port = new(new FakePeer());

        int exit = await ReviewedHelperProbeBootstrap.RunAsync(new ReviewedHelperProbeBootstrap(Environment.ProcessId + 1, name, F.Nonce), Host(), port,
            new FakeCapability(), TimeProvider.System).WaitAsync(F.Wait);

        await connected.WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
        Assert.AreEqual(0, port.OpenCalls);
        Assert.AreEqual(0, port.BeginCalls);
        Assert.AreEqual(0, await ReadToEndAsync(server), "P0 never writes");
    }

    [TestMethod]
    public async Task H8AnUnverifiedOrThrowingPeerIsRefusedWith23DisposedAndNothingIsWritten()
    {
        N1NativeFixture.RequireWindows("probe bootstrap peer verification");
        foreach (bool throwOnOpen in new[] { false, true })
        {
            string name = WindowsReviewedHelperTransport.CreatePipeName();
            using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
            Task connected = server.WaitForConnectionAsync();
            TrackingPeer peer = new();
            CountingProbePort port = new(peer, throwOnOpen);
            FakeCapability capability = new();

            int exit = await ReviewedHelperProbeBootstrap.RunAsync(new ReviewedHelperProbeBootstrap(Environment.ProcessId, name, F.Nonce), Host(), port,
                capability, TimeProvider.System).WaitAsync(F.Wait);

            await connected.WaitAsync(F.Wait);
            Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
            Assert.AreEqual(1, port.OpenCalls);
            Assert.AreEqual(Environment.ProcessId, port.OpenedFor);
            if (!throwOnOpen) Assert.IsTrue(peer.Disposed, "the held peer is closed");
            Assert.AreEqual(0, port.BeginCalls, "the probe entry never ran");
            Assert.AreEqual(0, capability.Calls);
            Assert.AreEqual(0, await ReadToEndAsync(server), "P0 never writes");
        }
    }

    // ---- Windows: the native probe port ----

    [TestMethod]
    public void TheNativeProbePortOpensOnlyLivePositivePidsAndRefusesWithTheSpecificVerdict()
    {
        N1NativeFixture.RequireWindows("native probe port");
        NativeChocolateyProbePort port = new();
        Assert.IsNull(port.OpenPeer(0));
        Assert.IsNull(port.OpenPeer(-4));
        using IChocolateyPeer peer = port.OpenPeer(Environment.ProcessId) ?? throw new AssertFailedException("self open failed");
        IChocolateyProbeItem? item = port.BeginProbe(peer, CurrentSid(), out TokenVerdict verdict);
        Assert.IsNull(item, "the test host is not a split-token desktop peer");
        Assert.AreNotEqual(TokenVerdict.Accepted, verdict);
        // The same verdict as the update port's diagnostic overload for the same peer (the same §2.1 rules).
        _ = NativeChocolateyHelperQualification.BeginItemWithVerdict(peer, CurrentSid(), out _, out TokenVerdict update);
        Assert.AreEqual(update, verdict);

        Assert.IsNull(port.BeginProbe(new FakePeer { UserSid = CurrentSid() }, CurrentSid(), out verdict), "a peer this port did not open");
        Assert.AreEqual(TokenVerdict.EvaluationTokenUnreadable, verdict);
        Assert.IsNull(port.BeginProbe(peer, string.Empty, out verdict));
        Assert.AreEqual(TokenVerdict.EvaluationTokenUnreadable, verdict);
        using Process child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 0")
            { UseShellExecute = false, CreateNoWindow = true }) ?? throw new AssertFailedException("cmd.exe did not start");
        using IChocolateyPeer exited = port.OpenPeer(child.Id) ?? throw new AssertFailedException("child open failed");
        child.WaitForExit();
        Assert.IsNull(port.BeginProbe(exited, CurrentSid(), out verdict));
        Assert.AreEqual(TokenVerdict.PeerExited, verdict);
    }

    // ---- Helpers ----

    private static T Runtime<T>(T value) => value;

    private static string[] Probe(string kind = Kind, string protocol = "2", string pid = "4242", string pipe = Pipe, string? nonce = null) =>
        ["--kind", kind, "--protocol", protocol, "--desktop-pid", pid, "--pipe", pipe, "--nonce", nonce ?? F.Nonce];

    private static string[]? Variant(string scenario) => scenario switch
    {
        "revision-1-order" => ["--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", F.Nonce, "--kind", Kind],
        "kind-value-other" => Probe(kind: "qualify-chocolatey-helpers"),
        "kind-value-case" => Probe(kind: "Qualify-Chocolatey-Helper"),
        "kind-key-case" => ["--Kind", Kind, "--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", F.Nonce],
        "protocol-key-case" => ["--kind", Kind, "--PROTOCOL", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", F.Nonce],
        "kind-leading-space" => [" --kind", Kind, "--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", F.Nonce],
        "kind-trailing-space" => ["--kind ", Kind, "--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", F.Nonce],
        "value-trailing-space" => Probe(kind: Kind + " "),
        "protocol-1" => Probe(protocol: "1"),
        "protocol-3" => Probe(protocol: "3"),
        "pid-before-protocol" => ["--kind", Kind, "--desktop-pid", "4242", "--protocol", "2", "--pipe", Pipe, "--nonce", F.Nonce],
        "nonce-before-pipe" => ["--kind", Kind, "--protocol", "2", "--desktop-pid", "4242", "--nonce", F.Nonce, "--pipe", Pipe],
        "repeated-key" => ["--kind", Kind, "--protocol", "2", "--protocol", "2", "--pipe", Pipe, "--nonce", F.Nonce],
        "unknown-key" => ["--kind", Kind, "--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonc", F.Nonce],
        "protocol-1-locator" => Probe(pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef"),
        "uppercase-locator" => Probe(pipe: "pcsteward-reviewed-0123456789ABCDEF0123456789abcdef"),
        "locator-trailing-newline" => Probe(pipe: Pipe + "\n"),
        "lowercase-nonce" => Probe(nonce: F.Nonce.ToLowerInvariant()),
        "short-nonce" => Probe(nonce: F.Nonce[..63]),
        "pid-zero" => Probe(pid: "0"),
        "pid-negative" => Probe(pid: "-1"),
        "pid-signed" => Probe(pid: "+4242"),
        "pid-space" => Probe(pid: " 4242"),
        "pid-overflow" => Probe(pid: "99999999999"),
        "nine-arguments" => Probe()[..9],
        "eleven-arguments" => [.. Probe(), "--extra"],
        "twelve-arguments" => [.. Probe(), "--extra", "x"],
        "null-value" => ["--kind", Kind, "--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", null!],
        _ => null,
    };

    private static readonly string[] TenArgumentScenarios =
    [
        "revision-1-order", "kind-value-other", "kind-value-case", "kind-key-case", "protocol-key-case", "kind-leading-space",
        "kind-trailing-space", "value-trailing-space", "protocol-1", "protocol-3", "pid-before-protocol", "nonce-before-pipe", "repeated-key",
        "unknown-key", "protocol-1-locator", "uppercase-locator", "lowercase-nonce", "pid-zero",
    ];

    private static IEnumerable<string[]> TenArgumentLines()
    {
        yield return Probe();
        yield return Probe(pipe: WindowsReviewedHelperTransport.CreatePipeName());
        foreach (string scenario in TenArgumentScenarios)
        {
            string[] line = Variant(scenario)!;
            Assert.AreEqual(10, line.Length, scenario);
            yield return line;
        }
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (int index = 0; index < items.Length; index++)
            foreach (int[] rest in Permutations([.. items.Take(index), .. items.Skip(index + 1)]))
                yield return [items[index], .. rest];
    }

    private static ReviewedHelperHost Host() =>
        new(Environment.ProcessPath!, @"C:\Program Files\PC Steward\PCSteward.Desktop.exe", CurrentSid(), Process.GetCurrentProcess().SessionId);

    private static string CurrentSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<int> ReadToEndAsync(NamedPipeServerStream server)
    {
        byte[] buffer = new byte[4096];
        int total = 0;
        using CancellationTokenSource read = new(TimeSpan.FromSeconds(10));
        try
        {
            int count;
            while ((count = await server.ReadAsync(buffer, read.Token)) > 0) total += count;
        }
        catch (IOException)
        {
            // A closed client may surface as an error rather than end of file.
        }
        return total;
    }

    /// <summary>A probe port that records <c>OpenPeer</c> and returns a fixed peer (or null), or throws when told to.</summary>
    private sealed class CountingProbePort(IChocolateyPeer? peer, bool throwOnOpen = false) : IChocolateyProbePort
    {
        internal int OpenCalls { get; private set; }
        internal int OpenedFor { get; private set; }
        internal int BeginCalls { get; private set; }

        public IChocolateyPeer? OpenPeer(int pipeServerProcessId)
        {
            OpenCalls++;
            OpenedFor = pipeServerProcessId;
            if (throwOnOpen) throw new InvalidOperationException("synthetic peer open failure");
            return peer;
        }

        public IChocolateyProbeItem? BeginProbe(IChocolateyPeer peer, string expectedSid, out TokenVerdict verdict)
        {
            BeginCalls++;
            verdict = TokenVerdict.FactsMalformed;
            return null;
        }
    }

    /// <summary>A peer for this process ID whose image is not the fixed desktop path; it records disposal.</summary>
    private sealed class TrackingPeer : IChocolateyPeer
    {
        internal bool Disposed { get; private set; }
        public int ProcessId => Environment.ProcessId;
        public bool HasExited => Disposed;

        public bool TryReadIdentity(out string imagePath, out int session, out string userSid)
        {
            imagePath = @"C:\Windows\System32\cmd.exe";
            session = Process.GetCurrentProcess().SessionId;
            userSid = CurrentSid();
            return true;
        }

        public void Dispose() => Disposed = true;
    }
}
