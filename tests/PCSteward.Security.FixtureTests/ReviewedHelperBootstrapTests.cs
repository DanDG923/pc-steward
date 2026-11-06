using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §2.1 (H3), §5.1, §5.4 and §8 N4b: the protocol-2 bootstrap.
/// Portable: the exact command-line grammar (accept and refuse), the dispatch rule, protocol 1 unchanged, and the entry
/// point's refusal before any connection. Windows-only (reported as failed elsewhere): the connected bootstrap against a
/// real restricted pipe (server PID check before the peer is opened, peer verification before the endpoint), the
/// handle-based <c>VerifyPeer</c> overload through the native port against a child process, and the native port's
/// specific refusals on a runner without split tokens.
/// </summary>
[TestClass]
public sealed class ReviewedHelperBootstrapTests
{
    private const string Pipe = "pcsteward-reviewed-0123456789abcdef0123456789abcdef";

    // ---- Portable: grammar ----

    [TestMethod]
    public void TheExactProtocol2CommandLineIsAcceptedInAnyOrder()
    {
        Assert.IsTrue(ReviewedHelperBootstrap.TryParse(Arguments(), out ReviewedHelperBootstrap? bootstrap));
        Assert.AreEqual(new ReviewedHelperBootstrap(4242, Pipe, F.Nonce), bootstrap);

        string[] permuted = ["--nonce", F.Nonce, "--pipe", Pipe, "--protocol", "2", "--desktop-pid", "4242"];
        Assert.IsTrue(ReviewedHelperBootstrap.TryParse(permuted, out bootstrap));
        Assert.AreEqual(new ReviewedHelperBootstrap(4242, Pipe, F.Nonce), bootstrap);
        Assert.IsTrue(ReviewedHelperBootstrap.TryParse(Arguments(pipe: WindowsReviewedHelperTransport.CreatePipeName()), out _));
    }

    [TestMethod]
    [DataRow("protocol-1")]
    [DataRow("protocol-3")]
    [DataRow("protocol-1-locator")]
    [DataRow("uppercase-locator")]
    [DataRow("short-locator")]
    [DataRow("locator-with-path")]
    [DataRow("locator-trailing-newline")]
    [DataRow("locator-trailing-crlf")]
    [DataRow("lowercase-nonce")]
    [DataRow("short-nonce")]
    [DataRow("non-hex-nonce")]
    [DataRow("pid-zero")]
    [DataRow("pid-negative")]
    [DataRow("pid-signed")]
    [DataRow("pid-space")]
    [DataRow("pid-overflow")]
    [DataRow("pid-text")]
    [DataRow("duplicate-key")]
    [DataRow("unknown-key")]
    [DataRow("six-arguments")]
    [DataRow("ten-arguments")]
    [DataRow("null-value")]
    [DataRow("null-array")]
    public void AnyOtherCommandLineIsRefused(string scenario)
    {
        string[]? arguments = scenario switch
        {
            "protocol-1" => Arguments(protocol: "1"),
            "protocol-3" => Arguments(protocol: "3"),
            "protocol-1-locator" => Arguments(pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef"),
            "uppercase-locator" => Arguments(pipe: "pcsteward-reviewed-0123456789ABCDEF0123456789abcdef"),
            "short-locator" => Arguments(pipe: "pcsteward-reviewed-0123456789abcdef"),
            "locator-with-path" => Arguments(pipe: @"..\pcsteward-reviewed-0123456789abcdef0123456789abcdef"),
            // N4b review L4: "$" would accept a trailing newline; the protocol-2 locator is anchored with \z.
            "locator-trailing-newline" => Arguments(pipe: Pipe + "\n"),
            "locator-trailing-crlf" => Arguments(pipe: Pipe + "\r\n"),
            "lowercase-nonce" => Arguments(nonce: F.Nonce.ToLowerInvariant()),
            "short-nonce" => Arguments(nonce: F.Nonce[..63]),
            "non-hex-nonce" => Arguments(nonce: "G" + F.Nonce[1..]),
            "pid-zero" => Arguments(pid: "0"),
            "pid-negative" => Arguments(pid: "-1"),
            "pid-signed" => Arguments(pid: "+4242"),
            "pid-space" => Arguments(pid: " 4242"),
            "pid-overflow" => Arguments(pid: "99999999999"),
            "pid-text" => Arguments(pid: "abc"),
            "duplicate-key" => ["--protocol", "2", "--protocol", "2", "--pipe", Pipe, "--nonce", F.Nonce],
            "unknown-key" => ["--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonc", F.Nonce],
            "six-arguments" => ["--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe],
            "ten-arguments" => [.. Arguments(), "--extra", "x"],
            "null-value" => ["--protocol", "2", "--desktop-pid", "4242", "--pipe", Pipe, "--nonce", null!],
            _ => null,
        };
        Assert.IsFalse(ReviewedHelperBootstrap.TryParse(arguments!, out ReviewedHelperBootstrap? bootstrap), scenario);
        Assert.IsNull(bootstrap);
    }

    [TestMethod]
    public void ProtocolTwoIsDispatchedAndProtocolOneIsUnchanged()
    {
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProtocol2(Arguments()));
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProtocol2(["--pipe", Pipe, "--protocol", "2", "--nonce", F.Nonce, "--desktop-pid", "1"]));
        // A malformed protocol-2 line is still dispatched to protocol 2, whose full parse refuses it (never to protocol 1).
        Assert.IsTrue(ElevatedHelperEntryPoint.SelectsProtocol2(Arguments(pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef")));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProtocol2(Arguments(protocol: "1")));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProtocol2(["--protocol", "2"]));
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProtocol2(null!));

        // Protocol 1's own bootstrap still refuses protocol 2 and accepts only its own locator.
        Assert.IsFalse(HelperBootstrap.TryParse(Arguments(), out _));
        Assert.IsFalse(HelperBootstrap.TryParse(Arguments(protocol: "1"), out _), "protocol 1 never accepts the protocol-2 locator");
        Assert.IsTrue(HelperBootstrap.TryParse(Arguments(protocol: "1", pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef"), out _));
        Assert.IsFalse(ReviewedHelperBootstrap.TryParse(Arguments(protocol: "1", pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef"), out _));
    }

    [TestMethod]
    public async Task AMalformedProtocol2LineExitsWith20BeforeAnyConnection()
    {
        // The parse refuses before the elevation check, so this holds on every host.
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Arguments(pipe: "pcsteward-elevated-0123456789abcdef0123456789abcdef")).WaitAsync(F.Wait));
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Arguments(nonce: F.Nonce.ToLowerInvariant())).WaitAsync(F.Wait));
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(["--protocol", "3"]).WaitAsync(F.Wait));
    }

    [TestMethod]
    public async Task AWellFormedProtocol2LineExitsWith20WhenNotElevated()
    {
        if (OperatingSystem.IsWindows() && IsElevated())
            // scripts/Test-CI.ps1 runs the tests as a disposable standard user, started with explicit credentials from the
            // runner's elevated administrator session: a plain non-elevated token with no UAC split, so this does not apply there.
            Assert.Inconclusive("an elevated test host would attempt the pipe connection");
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(Arguments(pipe: WindowsReviewedHelperTransport.CreatePipeName())).WaitAsync(F.Wait));
    }

    [TestMethod]
    public void TheHostFactsAreResolvedOnlyBesideAnInstalledDesktop()
    {
        // The test host has no PCSteward.Desktop.exe beside it, so the bootstrap cannot verify a peer and refuses (21).
        bool desktopBeside = File.Exists(Path.Combine(AppContext.BaseDirectory, "PCSteward.Desktop.exe"));
        if (desktopBeside) Assert.Inconclusive("a desktop image is beside the test host");
        Assert.IsNull(ReviewedHelperHost.TryResolve());
    }

    // ---- Windows: the connected bootstrap ----

    [TestMethod]
    public async Task AServerPidOtherThanTheDesktopPidIsRefusedBeforeThePeerIsOpened()
    {
        N1NativeFixture.RequireWindows("protocol-2 bootstrap server PID check");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
        Task connected = server.WaitForConnectionAsync();
        RecordingPort port = new(new FakePeer());

        int exit = await ReviewedHelperBootstrap.RunAsync(new ReviewedHelperBootstrap(Environment.ProcessId + 1, name, F.Nonce), Host(), port,
            new FakeCapability(), TimeProvider.System).WaitAsync(F.Wait);

        await connected.WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
        Assert.AreEqual(0, port.OpenCalls, "the peer is opened only after the server PID matched");
        Assert.AreEqual(0, await ReadToEndAsync(server), "nothing is written");
    }

    [TestMethod]
    public async Task AnUnverifiedPeerIsRefusedAndDisposedBeforeTheEndpointRuns()
    {
        N1NativeFixture.RequireWindows("protocol-2 bootstrap peer verification");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
        Task connected = server.WaitForConnectionAsync();
        TrackingPeer peer = new() { ImagePath = @"C:\Windows\System32\cmd.exe" };
        RecordingPort port = new(peer);
        FakeCapability capability = new();

        int exit = await ReviewedHelperBootstrap.RunAsync(new ReviewedHelperBootstrap(Environment.ProcessId, name, F.Nonce), Host(), port, capability,
            TimeProvider.System).WaitAsync(F.Wait);

        await connected.WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
        Assert.AreEqual(Environment.ProcessId, port.OpenedFor, "the peer is opened for the pipe server PID");
        Assert.IsTrue(peer.Disposed, "the held peer is closed");
        Assert.AreEqual(0, capability.Calls, "the endpoint never ran");
        Assert.AreEqual(0, await ReadToEndAsync(server));
    }

    [TestMethod]
    public async Task ANullPeerIsRefused()
    {
        N1NativeFixture.RequireWindows("protocol-2 bootstrap peer open failure");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
        Task connected = server.WaitForConnectionAsync();

        int exit = await ReviewedHelperBootstrap.RunAsync(new ReviewedHelperBootstrap(Environment.ProcessId, name, F.Nonce), Host(),
            new RecordingPort(null), new FakeCapability(), TimeProvider.System).WaitAsync(F.Wait);

        await connected.WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
    }

    [TestMethod]
    public async Task APeerOpenThatThrowsIsPeerNotVerifiedNotAResponseWriteFailure()
    {
        // N4b review L6: an exception before the endpoint (opening or verifying the peer) is PeerNotVerified (23), not 26.
        N1NativeFixture.RequireWindows("protocol-2 bootstrap peer open exception");
        string name = WindowsReviewedHelperTransport.CreatePipeName();
        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, CurrentSid());
        Task connected = server.WaitForConnectionAsync();
        RecordingPort port = new(null, throwOnOpen: true);
        FakeCapability capability = new();

        int exit = await ReviewedHelperBootstrap.RunAsync(new ReviewedHelperBootstrap(Environment.ProcessId, name, F.Nonce), Host(), port, capability,
            TimeProvider.System).WaitAsync(F.Wait);

        await connected.WaitAsync(F.Wait);
        Assert.AreEqual(ReviewedHelperBootstrap.PeerNotVerified, exit);
        Assert.AreEqual(1, port.OpenCalls);
        Assert.AreEqual(0, capability.Calls, "the endpoint never ran");
        Assert.AreEqual(0, await ReadToEndAsync(server), "nothing is written");
    }

    // ---- Windows: the handle-based VerifyPeer overload through the native port ----

    [TestMethod]
    public void VerifyPeerReadsEveryIdentityFactFromTheHeldHandle()
    {
        N1NativeFixture.RequireWindows("handle-based VerifyPeer against a child process");
        using Process child = StartChild(["/d", "/c", "ping", "-n", "30", "127.0.0.1", ">nul"]);
        try
        {
            NativeChocolateyHelperQualification port = new();
            using IChocolateyPeer peer = port.OpenPeer(child.Id) ?? throw new AssertFailedException("the native port did not open the child");
            string image = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            int session = Process.GetCurrentProcess().SessionId;
            string sid = CurrentSid();
            // An unsigned "own" image: the signer check is the only step that can fail once the handle facts all match.
            string unsigned = typeof(ReviewedHelperBootstrapTests).Assembly.Location;

            Assert.AreEqual(child.Id, peer.ProcessId);
            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id, image, unsigned, session, sid, out string rejection));
            Assert.AreEqual("peer-signature-invalid", rejection, "path, session and SID matched through the held handle");

            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id, Path.Combine(Environment.SystemDirectory, "where.exe"), unsigned, session, sid, out rejection));
            Assert.AreEqual("peer-identity-invalid", rejection, "another fixed path");
            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id + 1, image, unsigned, session, sid, out rejection));
            Assert.AreEqual("peer-identity-invalid", rejection, "the held handle is for another PID");
            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id, image, unsigned, session + 1, sid, out rejection));
            Assert.AreEqual("peer-identity-invalid", rejection, "another session");
            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id, image, unsigned, session, "S-1-5-21-1-2-3-1001", out rejection));
            Assert.AreEqual("peer-identity-invalid", rejection, "another SID");

            child.Kill();
            child.WaitForExit();
            Assert.IsTrue(peer.HasExited);
            Assert.IsFalse(WindowsPeerIdentityVerifier.VerifyPeer(peer, child.Id, image, unsigned, session, sid, out rejection));
            Assert.AreEqual("peer-identity-invalid", rejection, "an exited peer is refused");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            child.WaitForExit();
        }
    }

    [TestMethod]
    public void TheNativePortOpensOnlyLivePositivePidsAndAClosedPeerIsExited()
    {
        N1NativeFixture.RequireWindows("native port OpenPeer");
        NativeChocolateyHelperQualification port = new();
        Assert.IsNull(port.OpenPeer(0));
        Assert.IsNull(port.OpenPeer(-4));
        IChocolateyPeer peer = port.OpenPeer(Environment.ProcessId) ?? throw new AssertFailedException("self open failed");
        Assert.IsFalse(peer.HasExited);
        Assert.IsTrue(peer.TryReadIdentity(out _, out _, out string sid));
        Assert.AreEqual(CurrentSid(), sid);
        peer.Dispose();
        Assert.IsTrue(peer.HasExited, "a closed handle reads as exited (fail closed)");
        Assert.IsFalse(peer.TryReadIdentity(out _, out _, out _));
    }

    [TestMethod]
    public void TheNativePortRefusesTheItemWithTheSpecificTokenVerdict()
    {
        N1NativeFixture.RequireWindows("native port BeginItem refusal");
        NativeChocolateyHelperQualification port = new();
        using IChocolateyPeer peer = port.OpenPeer(Environment.ProcessId) ?? throw new AssertFailedException("self open failed");

        IChocolateyHelperItem? item = NativeChocolateyHelperQualification.BeginItemWithVerdict(peer, CurrentSid(), out ChocolateyItemRefusal refusal,
            out TokenVerdict verdict);

        Assert.IsNull(item);
        Assert.AreEqual(ChocolateyItemRefusal.RequesterTokenRefused, refusal);
        TokenVerdict expected = OwnElevation() switch
        {
            // CI: the disposable standard user that scripts/Test-CI.ps1 starts from the elevated runner administrator has a
            // plain token without a UAC split, so nothing is linked (as for an administrator without split tokens). Linked-token success is
            // owner-host evidence only (design rev 6 §8, §9).
            TokenElevationKind.Default => TokenVerdict.NoLinkedToken,
            TokenElevationKind.Limited => TokenVerdict.EvaluationNotLimited,
            _ => TokenVerdict.PeerNotLimited,
        };
        Assert.AreEqual(expected, verdict);
        Assert.IsNull(port.BeginItem(peer, CurrentSid(), out refusal));
        Assert.AreEqual(ChocolateyItemRefusal.RequesterTokenRefused, refusal);
    }

    [TestMethod]
    public void TheNativePortRefusesAnExitedPeerAForeignPeerAndAnEmptySid()
    {
        N1NativeFixture.RequireWindows("native port BeginItem refusals");
        NativeChocolateyHelperQualification port = new();
        using Process child = StartChild(["/d", "/c", "exit", "0"]);
        using IChocolateyPeer exited = port.OpenPeer(child.Id) ?? throw new AssertFailedException("child open failed");
        child.WaitForExit();

        Assert.IsNull(port.BeginItem(exited, CurrentSid(), out ChocolateyItemRefusal refusal));
        Assert.AreEqual(ChocolateyItemRefusal.PeerExited, refusal);
        Assert.IsNull(port.BeginItem(new FakePeer { UserSid = CurrentSid() }, CurrentSid(), out refusal), "a peer this port did not open");
        Assert.AreEqual(ChocolateyItemRefusal.RequesterTokenRefused, refusal);
        using IChocolateyPeer self = port.OpenPeer(Environment.ProcessId)!;
        Assert.IsNull(port.BeginItem(self, string.Empty, out refusal));
        Assert.AreEqual(ChocolateyItemRefusal.RequesterTokenRefused, refusal);
    }

    // ---- Helpers ----

    private static string[] Arguments(string protocol = "2", string pid = "4242", string pipe = Pipe, string? nonce = null) =>
        ["--protocol", protocol, "--desktop-pid", pid, "--pipe", pipe, "--nonce", nonce ?? F.Nonce];

    private static ReviewedHelperHost Host() =>
        new(Environment.ProcessPath!, @"C:\Program Files\PC Steward\PCSteward.Desktop.exe", CurrentSid(), Process.GetCurrentProcess().SessionId);

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

    private static Process StartChild(string[] arguments)
    {
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new AssertFailedException("cmd.exe did not start");
    }

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

    private static TokenElevationKind OwnElevation()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.IsTrue(NativeTokenReader.TryReadInt32(identity.AccessToken, ProtectedNativeMethods.TokenElevationTypeClass, out int elevation));
        return (TokenElevationKind)elevation;
    }

    /// <summary>A port that records <c>OpenPeer</c> and returns a fixed peer (or null), or throws when told to.</summary>
    private sealed class RecordingPort(IChocolateyPeer? peer, bool throwOnOpen = false) : IChocolateyHelperQualificationPort
    {
        internal int OpenCalls { get; private set; }
        internal int OpenedFor { get; private set; }

        public IChocolateyPeer? OpenPeer(int pipeServerProcessId)
        {
            OpenCalls++;
            OpenedFor = pipeServerProcessId;
            if (throwOnOpen) throw new InvalidOperationException("synthetic peer open failure");
            return peer;
        }

        public IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal) =>
            throw new AssertFailedException("the bootstrap never begins an item before the endpoint");
    }

    /// <summary>A peer for this process ID whose image is not the fixed desktop path; it records disposal.</summary>
    private sealed class TrackingPeer : IChocolateyPeer
    {
        internal string ImagePath { get; init; } = string.Empty;
        internal bool Disposed { get; private set; }
        public int ProcessId => Environment.ProcessId;
        public bool HasExited => Disposed;

        public bool TryReadIdentity(out string imagePath, out int session, out string userSid)
        {
            imagePath = ImagePath;
            session = Process.GetCurrentProcess().SessionId;
            userSid = CurrentSid();
            return true;
        }

        public void Dispose() => Disposed = true;
    }
}
