using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PCSteward.Execution;
using Outcome = PCSteward.Execution.QualificationProbeDesktopOutcome;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5.2 step 6, §5.3 pin sources and §7 D9 (slice S4), on real
/// Windows handles over the existing GUID-named deployment fixture tree: the <c>TryHold</c> value extension (the verified
/// ledger SHA-256, kept after a later ledger change; the helper apphost's signer thumbprint and <c>NotAfter</c>, from a file
/// carrying an embedded Authenticode signature) and the probe transport holding every allow-list token through the real
/// Desktop-role reader up to a declined prompt. Windows-native: on other platforms each test reports
/// <see cref="PlatformNotSupportedException"/> (never silently skipped). Every existing <c>TryHold</c> status test is unmodified.
/// </summary>
[TestClass]
public sealed class QualificationProbeDesktopNativeTests
{
    private const string HelperLeaf = "PCSteward.ElevatedHelper.exe";
    private static readonly string[] KindPair = ["--kind", "qualify-chocolatey-helper"];
    private static readonly string[] SignedRuntimeImages = ["coreclr.dll", "hostpolicy.dll", "clrjit.dll", "System.Private.CoreLib.dll"];

    [TestMethod]
    public void D9TheLedgerHashIsOfTheVerifiedBytesAndALaterLedgerChangeKeepsTheEarlierValue()
    {
        RequireWindows("D9 ledger SHA-256");
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture);
        string verified = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tree.LedgerPath)));
        OwnerOnlyDeploymentCapabilityReader reader = new(() => tree.Layout, (_, _) => true);

        HelperProtocolCapabilityResult result = reader.TryHold(DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0], out HeldHelperBinding? binding);
        Assert.AreEqual(Status.Supported, result.Status, "the status is unchanged by the value extension");
        Assert.IsNotNull(binding);
        using (binding)
        {
            Assert.AreEqual(verified, binding.LedgerSha256);
            Assert.IsNull(binding.HelperSigner, "an unsigned fixture helper has no signer values, and the status stays Supported");
            // The ledger is not held: a later change to the file is possible and is not reflected in the recorded value.
            File.AppendAllText(tree.LedgerPath, " ");
            Assert.AreNotEqual(verified, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tree.LedgerPath))));
            Assert.AreEqual(verified, binding.LedgerSha256, "the value records what was verified");
        }
    }

    [TestMethod]
    public void D9TheSignerThumbprintAndNotAfterEqualTheFixtureCertificate()
    {
        RequireWindows("D9 signer thumbprint and NotAfter");
        (byte[] signed, X509Certificate2 certificate) = SignedImage();
        using (certificate)
        {
            using DeploymentCapabilityReaderTests.Fixture fixture = new();
            DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture.Root, null,
                new Dictionary<string, byte[]> { [HelperLeaf] = signed });
            OwnerOnlyDeploymentCapabilityReader desktop = new(() => tree.Layout, (_, _) => true);
            Assert.AreEqual(Status.Supported, desktop.TryHold(DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0], out HeldHelperBinding? binding).Status);
            using (binding)
            {
                Assert.IsNotNull(binding?.HelperSigner);
                Assert.AreEqual(certificate.Thumbprint, binding.HelperSigner.Thumbprint);
                Assert.AreEqual(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero), binding.HelperSigner.NotAfterUtc);
                Assert.AreEqual(TimeSpan.Zero, binding.HelperSigner.NotAfterUtc.Offset);
            }

            // The Helper role records neither value (the helper's own G1 behaviour is unchanged).
            OwnerOnlyDeploymentCapabilityReader helper = new(DeploymentCapabilityRole.Helper, () => tree.Layout with { ProcessPath = tree.Leaf(HelperLeaf) }, (_, _) => true);
            Assert.AreEqual(Status.Supported, helper.TryHold(DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0], out HeldHelperBinding? helperBinding).Status);
            using (helperBinding)
            {
                Assert.IsNull(helperBinding!.LedgerSha256);
                Assert.IsNull(helperBinding.HelperSigner);
            }
        }
    }

    [TestMethod]
    public async Task EveryTokenIsHeldThroughTheRealReaderAndADeclinedPromptReleasesEveryBindingWithThePinsRecorded()
    {
        RequireWindows("§5.2 step 6 TryHold for every token");
        (byte[] signed, X509Certificate2 certificate) = SignedImage();
        using (certificate)
        {
            using DeploymentCapabilityReaderTests.Fixture fixture = new();
            DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture.Root, null,
                new Dictionary<string, byte[]> { [HelperLeaf] = signed });
            string ledger = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tree.LedgerPath)));
            ProbeTransportFakes fakes = new()
            {
                Capability = new OwnerOnlyDeploymentCapabilityReader(() => tree.Layout, (_, _) => true),
                Paths = new(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), tree.Leaf(HelperLeaf)),
                Declined = true,
            };
            QualificationProbeRun run = await new ReviewedHelperProbeTransport(fakes.Dependencies()).ProbeAsync(CancellationToken.None);

            Assert.AreEqual(Outcome.ElevationDeclined, run.Outcome);
            Assert.AreEqual(1, fakes.Launches, "one prompt, only after every pre-UAC check passed");
            Assert.AreEqual(2, fakes.TrustCalls, "initial and final launch trust");
            CollectionAssert.AreEqual(DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported).ToArray(), run.CapabilityStatus.ToArray());
            Assert.IsNotNull(run.Pins);
            Assert.AreEqual(ledger, run.Pins.LedgerSha256);
            Assert.AreEqual(tree.Hashes[DeploymentCapabilityPolicy.CapabilityFileName], run.Pins.CapabilityFileSha256);
            Assert.AreEqual(tree.Hashes[InstalledHelperLaunchTrust.DesktopExecutableName], run.Pins.DesktopImageSha256);
            Assert.AreEqual(tree.Hashes[HelperLeaf], run.Pins.HelperImageSha256);
            Assert.AreEqual(certificate.Thumbprint, run.Pins.SignerThumbprint);
            Assert.IsTrue(run.Transport.InitialLaunchTrust && run.Transport.FinalLaunchTrust);
            Assert.IsFalse(run.Transport.HelperProcessCreated);
            CollectionAssert.AreEqual(KindPair, fakes.LaunchArguments!.Take(2).ToArray());
            // No helper exists, so every binding was released: the held leaves can be replaced again.
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet) File.WriteAllText(tree.Leaf(leaf), "replaced after release");
            Assert.IsNull(QualificationProbeResultDocument.Violation(run, DesktopProbeFixture.Started, DesktopProbeFixture.Started, QualificationProbeContract.Compiled));
        }
    }

    [TestMethod]
    public async Task AnUnsignedHelperHoldsButHasNoPinsSoTheProbeRefusesBeforeAnyPromptAndReleasesTheBindings()
    {
        RequireWindows("§5.3 pins required before the prompt");
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture);
        ProbeTransportFakes fakes = new()
        {
            Capability = new OwnerOnlyDeploymentCapabilityReader(() => tree.Layout, (_, _) => true),
            Paths = new(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), tree.Leaf(HelperLeaf)),
        };
        QualificationProbeRun run = await new ReviewedHelperProbeTransport(fakes.Dependencies()).ProbeAsync(CancellationToken.None);
        Assert.AreEqual(Outcome.CapabilityNotHeld, run.Outcome);
        CollectionAssert.AreEqual(DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported).ToArray(), run.CapabilityStatus.ToArray(),
            "TryHold itself reported Supported; the missing signer refuses only the probe");
        Assert.AreEqual(0, fakes.Launches);
        Assert.IsNull(run.Pins);
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet) File.WriteAllText(tree.Leaf(leaf), "replaced after release");
    }

    [TestMethod]
    public async Task AHelperPathOtherThanTheHeldOneIsCapabilityNotHeld()
    {
        RequireWindows("§5.2 step 6 same helper path");
        (byte[] signed, X509Certificate2 certificate) = SignedImage();
        using (certificate)
        {
            using DeploymentCapabilityReaderTests.Fixture fixture = new();
            DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture.Root, null,
                new Dictionary<string, byte[]> { [HelperLeaf] = signed });
            ProbeTransportFakes fakes = new()
            {
                Capability = new OwnerOnlyDeploymentCapabilityReader(() => tree.Layout, (_, _) => true),
                Paths = new(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), Path.Combine(fixture.Root, HelperLeaf)),
            };
            QualificationProbeRun run = await new ReviewedHelperProbeTransport(fakes.Dependencies()).ProbeAsync(CancellationToken.None);
            Assert.AreEqual(Outcome.CapabilityNotHeld, run.Outcome);
            Assert.AreEqual(0, fakes.Launches);
        }
    }

    /// <summary>
    /// The bytes of a runtime file that carries an embedded Authenticode signature (the .NET runtime's own signed images), and
    /// its signing certificate as an independent read. Inconclusive when this runtime ships no embedded-signed image.
    /// </summary>
    private static (byte[] Bytes, X509Certificate2 Certificate) SignedImage()
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (string candidate in SignedRuntimeImages.Select(name => Path.Combine(runtime, name)))
        {
            if (!File.Exists(candidate) || new FileInfo(candidate).Length > DeploymentCapabilityPolicy.MaximumBindingFileBytes) continue;
            try
            {
#pragma warning disable SYSLIB0057
                using X509Certificate signer = X509Certificate.CreateFromSignedFile(candidate);
                return (File.ReadAllBytes(candidate), new X509Certificate2(signer));
#pragma warning restore SYSLIB0057
            }
            catch (CryptographicException)
            {
                // Not embedded-signed; try the next image.
            }
        }
        Assert.Inconclusive("No embedded-signed runtime image is available on this host.");
        throw new InvalidOperationException("unreachable");
    }

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }
}
