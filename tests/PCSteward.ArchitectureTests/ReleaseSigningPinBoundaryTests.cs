using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 §3 and "Follow-up controls and tests" (amends ADR-018), TM-04/TM-13: the release pins are compiled constants that
/// nothing at run time can supply or change; the pinned check tolerates only <c>CERT_E_UNTRUSTEDROOT</c> and only a
/// single-certificate chain; the pre-launch check and both peer checks reach the pinned check through the compiled pins
/// only; and every <c>WinVerifyTrust</c> call in the launch-trust code stays inside <c>AuthenticodeIdentity.VerifyTrust</c>.
/// </summary>
[TestClass]
public sealed class ReleaseSigningPinBoundaryTests
{
    [TestMethod]
    public void PinsAreCompiledConstantsWithNoRuntimeInput()
    {
        string source = File.ReadAllText(Path.Combine(Execution(), "ReleaseSigningPins.cs"));
        Assert.IsTrue(Regex.IsMatch(source, @"internal const string Primary = ""(?:[0-9A-F]{64})?"";"), "the primary pin is a constant");
        Assert.IsTrue(Regex.IsMatch(source, @"internal const string Backup = ""(?:[0-9A-F]{64})?"";"), "the backup pin is a constant");
        StringAssert.Contains(source, "internal static IReadOnlyList<string> Compiled { get; } = [.. new[] { Primary, Backup }.Where(IsWellFormed)];");
        foreach (string forbidden in new[]
        {
            "File.", "Registry", "Environment.", "HttpClient", "AppContext", "Assembly", "GetCustomAttribute", "X509Store", "Process",
            "#if", "static string[]", "set;", "init;",
        })
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"ReleaseSigningPins.cs contains {forbidden}");
    }

    [TestMethod]
    public void ThePinnedCheckToleratesOnlyAnUntrustedRootAndASingleCertificateChain()
    {
        string transport = File.ReadAllText(Path.Combine(Execution(), "WindowsElevatedHelperTransport.cs"));
        StringAssert.Contains(transport, "internal const int CertUntrustedRoot = unchecked((int)0x800B0109);");
        StringAssert.Contains(transport, "if (status is not (0 or CertUntrustedRoot) || signer is null || signer.ChainLength != 1)");
        StringAssert.Contains(transport, "if (pins is null || pins.Count == 0 || string.IsNullOrEmpty(path))");
        StringAssert.Contains(transport, "CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(candidate))");
        StringAssert.Contains(transport, "certificate.PublicKey.ExportSubjectPublicKeyInfo()");
        // The signer comes from WinVerifyTrust's own state data, read before the state is closed, never from a second parse.
        string verify = Between(transport, "private static int VerifyTrust(string path, out VerifiedSigner? signer)", "private static VerifiedSigner? ReadSigner(");
        StringAssert.Contains(verify, "StateAction = NativeMethods.WtdStateActionVerify,");
        StringAssert.Contains(verify, "RevocationChecks = NativeMethods.WtdRevokeNone,");
        StringAssert.Contains(verify, "ProviderFlags = NativeMethods.WtdCacheOnlyUrlRetrieval");
        StringAssert.Contains(verify, "UiChoice = NativeMethods.WtdUiNone,");
        Assert.IsTrue(verify.IndexOf("signer = ReadSigner(data.StateData);", StringComparison.Ordinal) <
            verify.IndexOf("data.StateAction = NativeMethods.WtdStateActionClose;", StringComparison.Ordinal));
        string pinned = Between(transport, "internal static bool TryReadPinnedSigner(", "internal static bool HaveSameSigner(");
        Assert.IsFalse(pinned.Contains("CreateFromSignedFile", StringComparison.Ordinal), "the pinned check never re-parses the file");
        // Exactly two WinVerifyTrust calls in the transport (verify and close), both inside VerifyTrust.
        Assert.AreEqual(2, Regex.Count(transport, @"NativeMethods\.WinVerifyTrust\("));
        Assert.AreEqual(2, Regex.Count(verify, @"NativeMethods\.WinVerifyTrust\("));
    }

    [TestMethod]
    public void LaunchTrustAndBothPeerChecksUseOnlyTheCompiledPins()
    {
        string execution = Execution();
        string transport = File.ReadAllText(Path.Combine(execution, "WindowsElevatedHelperTransport.cs"));
        string validate = Between(transport, "internal static HelperLaunchTrustResult Validate(HelperLaunchPaths paths)", "internal static HelperLaunchTrustResult Evaluate(");
        StringAssert.Contains(validate, "AuthenticodeIdentity.HaveSamePinnedSigner(expectedDesktop, expectedHelper, ReleaseSigningPins.Compiled)");
        string pair = Between(transport, "internal static bool IsTrustedAndSameSigner(string firstPath, string secondPath)", "internal static bool IsTrusted(string path)");
        StringAssert.Contains(pair, "HaveSamePinnedSigner(firstPath, secondPath, ReleaseSigningPins.Compiled)");
        // The desktop's protocol-1 peer check and the helper's protocol-2 peer check both use the pair check.
        StringAssert.Contains(transport, "if (!AuthenticodeIdentity.IsTrustedAndSameSigner(actualPath, ownPath))");
        StringAssert.Contains(File.ReadAllText(Path.Combine(execution, "ReviewedHelperBootstrap.cs")),
            "if (!AuthenticodeIdentity.IsTrustedAndSameSigner(actualPath, ownPath))");

        // Production code passes no pin list other than the compiled one.
        string src = Path.Combine(FindRepositoryRoot(), "src");
        foreach (string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            string source = File.ReadAllText(path);
            foreach (Match call in Regex.Matches(source, @"(?:TryReadPinnedSigner|HaveSamePinnedSigner)\((?<arguments>[^;{]*)\)\s*(?:&&|\|\||;|\))"))
            {
                if (call.Value.Contains("IReadOnlyList<string> pins", StringComparison.Ordinal)) continue;
                string arguments = call.Groups["arguments"].Value;
                Assert.IsTrue(arguments.Contains("ReleaseSigningPins.Compiled", StringComparison.Ordinal) || arguments.Contains(", pins,", StringComparison.Ordinal),
                    $"{Path.GetFileName(path)} passes a pin list other than the compiled pins: {call.Value}");
            }
        }
    }

    private static string Execution() => Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Execution");

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, from, start);
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(from, to, end);
        return source[from..to];
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
