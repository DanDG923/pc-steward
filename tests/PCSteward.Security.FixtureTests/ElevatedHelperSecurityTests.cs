using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class ElevatedHelperSecurityTests
{
    [TestMethod]
    public async Task RestrictedPipeUsesCurrentUserAndSystemAclAndBindsEndpointPids()
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string sddl = RestrictedNamedPipe.BuildSecurityDescriptorSddl(sid);
        StringAssert.Contains(sddl, sid);
        StringAssert.Contains(sddl, ";;;SY");
        string name = WindowsReviewedHelperTransport.CreatePipeName();

        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, sid);
        using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await waiting;

        Assert.IsTrue(RestrictedNamedPipe.TryGetClientProcessId(server, out int clientPid));
        Assert.IsTrue(RestrictedNamedPipe.TryGetServerProcessId(client, out int serverPid));
        Assert.AreEqual(Environment.ProcessId, clientPid);
        Assert.AreEqual(Environment.ProcessId, serverPid);
    }

    [TestMethod]
    [TestCategory("TM-04")]
    public async Task AWellFormedProtocolOneLineIsRefusedBeforeTheHelperConnectsOrReadsAnything()
    {
        // D4b stage 3 (ADR-025 transition amendment, closed): protocol 1 is retired. Its exact former shape ends with exit code 20
        // on every host, elevated or not, and the helper never connects to the pipe the line names.
        string pipe = $"pcsteward-elevated-{Guid.NewGuid():N}";
        string nonce = ElevatedHelperProtocol.CreateNonce();
        using NamedPipeServerStream server = new(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task connected = server.WaitForConnectionAsync();

        int exit = await ElevatedHelperEntryPoint.RunAsync(
            ["--protocol", "1", "--desktop-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--pipe", pipe, "--nonce", nonce])
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(ElevatedHelperEntryPoint.Protocol1RefusedExitCode, exit);
        Assert.AreEqual(20, exit);
        Assert.IsFalse(connected.IsCompleted, "the retired protocol-1 line never connects to its pipe");
        Assert.IsFalse(ElevatedHelperEntryPoint.SelectsProtocol2(["--protocol", "1", "--desktop-pid", "4242", "--pipe", pipe, "--nonce", nonce]));
    }

    [TestMethod]
    [TestCategory("TM-04")]
    public void TheRestrictedPipeRefusesTheRetiredProtocolOneLocator()
    {
        Assert.IsFalse(RestrictedNamedPipe.IsAcceptedLocator($"pcsteward-elevated-{Guid.NewGuid():N}"));
        Assert.IsTrue(RestrictedNamedPipe.IsAcceptedLocator(WindowsReviewedHelperTransport.CreatePipeName()));
    }

    [TestMethod]
    public void PublicElevatedRunnerSurfaceCannotAcceptExecutionAuthorityStrings()
    {
        // D4b stage 3: the only public entry is the reviewed route; the runner is composed internally and has no public constructor.
        Assert.IsNull(typeof(ElevatedUpdateRunner).GetMethod("RunAsync"), "the mode-less protocol-1 entry is retired");
        Type[] parameters = typeof(ElevatedUpdateRunner).GetMethod(nameof(ElevatedUpdateRunner.RunReviewedAsync))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.IsFalse(parameters.Contains(typeof(string)));
        Assert.IsFalse(parameters.Any(type => typeof(IEnumerable<string>).IsAssignableFrom(type)));
        Assert.IsFalse(parameters.Any(type => typeof(IDictionary<string, string>).IsAssignableFrom(type)));
        Assert.IsEmpty(typeof(ElevatedUpdateRunner).GetConstructors());
    }

    [TestMethod]
    public void PreLaunchTrustRejectsUnsignedWrongSignerWritableReparseAndNonFixedLayouts()
    {
        HelperLaunchTrustEvidence trusted = new(true, true, true, true, true, true, true, true);
        Assert.IsTrue(InstalledHelperLaunchTrust.Evaluate(trusted).Trusted);

        Dictionary<string, HelperLaunchTrustEvidence> rejected = new()
        {
            ["elevated-helper-fixed-installed-path-invalid"] = trusted with { FixedInstalledLayout = false },
            ["elevated-helper-installed-binary-missing"] = trusted with { HelperPresent = false },
            ["elevated-helper-reparse-layout-rejected"] = trusted with { NoReparseComponents = false },
            ["elevated-helper-caller-writable-layout-rejected"] = trusted with { CallerCannotModifyLayout = false },
            ["elevated-helper-signature-untrusted"] = trusted with { HelperSignatureTrusted = false },
            ["elevated-helper-signer-mismatch"] = trusted with { SameSigner = false }
        };

        foreach ((string expected, HelperLaunchTrustEvidence evidence) in rejected)
        {
            HelperLaunchTrustResult result = InstalledHelperLaunchTrust.Evaluate(evidence);
            Assert.IsFalse(result.Trusted, expected);
            Assert.AreEqual(expected, result.Rejection);
        }
    }

    [TestMethod]
    public async Task CurrentCallerWritableUnsignedFixtureFailsTheProductionTrustChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), $"pcsteward-helper-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string desktop = Path.Combine(root, InstalledHelperLaunchTrust.DesktopExecutableName);
        string helper = Path.Combine(root, InstalledHelperLaunchTrust.HelperExecutableName);
        await File.WriteAllTextAsync(desktop, "unsigned desktop");
        await File.WriteAllTextAsync(helper, "unsigned helper");
        try
        {
            Assert.IsFalse(AuthenticodeIdentity.IsTrusted(desktop));
            Assert.IsFalse(AuthenticodeIdentity.IsTrusted(helper));
            Assert.IsFalse(ProtectedLaunchLayout.CallerCannotModify(root, desktop, helper));
        }
        finally
        {
            File.Delete(desktop);
            File.Delete(helper);
            Directory.Delete(root);
        }
    }
}
