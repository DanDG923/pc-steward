using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, AC-UPD-07; ADR-025, ADR-031 owner decision 7, Task 50 Part C step C-A (gate
/// inventory: <c>docs/review/TASK_50_ACTIVATION_CHANGE.md</c>): the activation is exactly the deliberate change of the three
/// admission pins that the qualification probe spec §7 D4 kept unchanged, and nothing else.
/// <list type="bullet">
/// <item>The review gate (<c>HasSupportedMode</c>) is the unchanged WinGet rule, byte for byte, then one term,
/// <c>ReviewedChocolateyRouteAdmission.Admits(operation)</c>; that type is read only there and reads no external state (it
/// references only Domain types and string comparison in compiled IL).</item>
/// <item>The capability allow-list is defined once with exactly the two Chocolatey routes, and the checked-in capability
/// statement and the deployment scripts' token list match it.</item>
/// <item>The route dispatcher's <c>Supports</c> runs the route-discriminated shape check before the capability seam.</item>
/// <item>Under owner decision D4 = G-0 no code guard and no §5.3a closed result reader exist.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ChocolateyRouteActivationBoundaryTests
{
    private const string Admission = "PCSteward.Application.ReviewedChocolateyRouteAdmission";
    private const string Review = "PCSteward.Application.ReviewedUpdateReview";

    private static readonly string[] UnavailableReasons =
        ["NotAdmittedPendingHostEvidence", "SplitTokenRequired", "DesktopTokenUnsupported", "DesktopTokenUnknown", "ReviewDataMismatch"];

    [TestMethod]
    public void TheReviewGateIsTheUnchangedWinGetRuleThenTheSingleActivationTerm()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string review = Read(root, "src", "PCSteward.Application", "ReviewedUpdateReview.cs");
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        Assert.AreEqual("operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserExactV3 && " +
            "operation.Selection.Source == UpdateSource.WinGet && " +
            "operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed && " +
            "operation.Selection.Architecture.Value is UpdateArchitecture.X86 or UpdateArchitecture.X64 or UpdateArchitecture.Arm64 && " +
            "UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode || " +
            "HasWinGetUserSilentMode(operation) || " +
            "ReviewedChocolateyRouteAdmission.Admits(operation) || " +
            // Phase D stage D4b, stage 3: the whole-PC WinGet route, admitted by its exact shape.
            "WingetMachineReviewedRoute.TryGetShape(operation, out _, out _, out _)", Regex.Replace(gate.Groups["body"].Value, @"\s+", " ").Trim());
        // Stage C4 (Task 54 R1): the per-user WinGet term, as reviewed.
        Match winget = Regex.Match(review, @"private static bool HasWinGetUserSilentMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(winget.Success, "HasWinGetUserSilentMode not found");
        Assert.AreEqual("operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserSilentV1 && " +
            "operation.Selection.Provider == UpdateProvider.WinGet && operation.Selection.Scope == UpdateScope.User && " +
            "operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed && " +
            "operation.Mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) && " +
            "UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode",
            Regex.Replace(winget.Groups["body"].Value, @"\s+", " ").Trim());

        // The two exact shapes, as reviewed (ADR-031 "client target refuses" stays for package jobs).
        string admission = Regex.Replace(Code(Read(root, "src", "PCSteward.Application", "ReviewedChocolateyRouteAdmission.cs")), @"\s+", " ");
        foreach (string conjunct in new[]
                 {
                     "operation is not null && operation.HasCurrentSchema && HasCommonShape(operation) && (IsRuntimeLimitsShape(operation) || IsClientSelfUpdateShape(operation));",
                     "operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 && operation.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits && operation.CandidateProvenance == ReviewedCandidateProvenance.UserCacheIntent && !ReviewedUpdateOperation.IsChocolateyClientIdentity(operation.Selection.PackageId);",
                     "operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 && operation.ProviderGuardContract == ReviewedProviderGuardContract.ChocolateyClientSelfUpdate && operation.CandidateProvenance == ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired && string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal);",
                     "operation.Selection.Provider == UpdateProvider.Chocolatey && operation.Selection.Source == UpdateSource.ChocolateyCommunity && operation.Selection.Scope == UpdateScope.Machine && operation.Selection.Channel == UpdateChannel.Stable && operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.ProviderScriptSelected && operation.Mode == ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) && ReviewedUpdateOperation.TryGetProfileEvidenceDigest(operation.EvidenceId, out _);",
                 })
            StringAssert.Contains(admission, conjunct);
        Assert.AreEqual(2, Regex.Count(admission, @"RouteContract\s*=="), "exactly the two Chocolatey route comparisons");
        Assert.AreEqual(0, Regex.Count(admission, @"AsyncLocal|ThreadStatic|static\s+(?!bool\b|class\b)\w+\s+\w+\s*[=;]"), "no ambient or static state");
    }

    [TestMethod]
    public void TheActivationTypeIsReadOnlyByTheReviewGateAndReadsNoExternalState()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        string application = assemblies.SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Application")
            ?? throw new AssertFailedException("PCSteward.Application.dll was not built");
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(application).Contains((Admission, "Admits")), "the activation term is not compiled");
        int gateReads = 0;
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetType == Admission && reference.Kind == CompiledIlScanner.ReferenceKind.Method &&
                    !(reference.CallerType ?? reference.Caller).StartsWith(Admission, StringComparison.Ordinal))
                {
                    if (reference.LogicalCaller == Review + ".HasSupportedMode" && reference.TargetMember == "Admits") gateReads++;
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {reference.TargetMember}");
                }
                // Inside the activation type: only Domain values and string comparison (no file, registry, environment, clock,
                // network, database or ambient state).
                if ((reference.CallerType ?? reference.Caller).StartsWith(Admission, StringComparison.Ordinal) &&
                    !reference.TargetType.StartsWith("PCSteward.Domain.", StringComparison.Ordinal) &&
                    !reference.TargetType.StartsWith(Admission, StringComparison.Ordinal) &&
                    reference.TargetType is not ("System.String" or "System.StringComparison" or "System.Object"))
                    violations.Add($"activation type references {reference.TargetType}.{reference.TargetMember}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        Assert.AreEqual(1, gateReads, "HasSupportedMode reads the activation term exactly once");
    }

    [TestMethod]
    public void TheCapabilityAllowListStatementAndScriptsAdmitExactlyTheTwoChocolateyRoutesAndTheWholePcWinGetRoute()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string policy = Code(Read(root, "src", "PCSteward.Execution", "DeploymentCapabilityPolicy.cs"));
        StringAssert.Contains(Regex.Replace(policy, @"\s+", " "),
            "internal static readonly IReadOnlyList<UpdateRouteContract> CapabilityOperationAllowlist = " +
            "[UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, UpdateRouteContract.ChocolateyClientSelfUpdateV1, UpdateRouteContract.WinGetMachineSilentV1];");
        Assert.AreEqual(1, Regex.Count(policy, @"CapabilityOperationAllowlist\s*="), "a single allow-list definition");

        // The checked-in statement is the canonical bytes (ordinal token order), LF only.
        byte[] statement = File.ReadAllBytes(Path.Combine(root, "src", "PCSteward.ElevatedHelper", "PCSteward.DeploymentCapability.txt"));
        Assert.AreEqual("pc-steward-deployment-capability:1\nhelper-protocol:2\noperation:Update/ChocolateyClientSelfUpdateV1\n" +
            "operation:Update/ChocolateyMachineRuntimeLimitsV3\noperation:Update/WinGetMachineSilentV1\n", Encoding.ASCII.GetString(statement));
        Assert.AreEqual(188, statement.Length);

        // The deployment scripts' parser accepts exactly the same two tokens.
        string integrity = Read(root, "scripts", "OwnerOnlyPayloadIntegrity.ps1");
        Assert.AreEqual(1, Regex.Count(integrity, Regex.Escape("$allowed = @('Update/ChocolateyClientSelfUpdateV1', 'Update/ChocolateyMachineRuntimeLimitsV3', 'Update/WinGetMachineSilentV1')")));
        Assert.AreEqual(1, Regex.Count(integrity, @"\$allowed\s*="), "one script token list");
    }

    [TestMethod]
    public void SupportsRunsTheRouteDiscriminatedShapeCheckBeforeTheCapabilitySeam()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string dispatcher = Code(Read(root, "src", "PCSteward.Execution", "ChocolateyRuntimeLimitsRouteDispatcher.cs"));
        Match supports = Regex.Match(dispatcher, @"public bool Supports\(ReviewedUpdateOperation operation\)(?<body>[\s\S]*?)\n    }");
        Assert.IsTrue(supports.Success, "Supports not found");
        string body = Regex.Replace(supports.Groups["body"].Value, @"\s+", " ");
        StringAssert.Contains(body, "return operation is not null && ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(operation, out _, out _) && " +
            "capability.Evaluate(operation.RouteContract).IsSupported;");
        StringAssert.Contains(body, "catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }");
    }

    [TestMethod]
    public void UnderG0NoCodeGuardOrClosedResultReaderExists()
    {
        // Owner decision D4 = G-0 (2026-11-06): no §5.3a QualificationResultReader and no probe-pass unavailability reason.
        string root = CompiledIlScanner.FindRepositoryRoot();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            string text = File.ReadAllText(path);
            Assert.IsFalse(text.Contains("QualificationResultReader", StringComparison.Ordinal), $"{path}: G-0 builds no closed result reader");
        }
        string disclosure = Read(root, "src", "PCSteward.Application", "ChocolateyReviewDisclosure.cs");
        Match reasons = Regex.Match(disclosure, @"public enum ReviewedRouteUnavailableReason\s*\{(?<body>[^}]*)\}");
        Assert.IsTrue(reasons.Success);
        CollectionAssert.AreEqual(UnavailableReasons,
            Regex.Matches(Code(reasons.Groups["body"].Value), @"\b([A-Z]\w+)\s*,?").Select(match => match.Groups[1].Value).ToArray(),
            "G-0 adds no review unavailability reason");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Source text without comment lines (<c>//</c> and <c>///</c>).</summary>
    private static string Code(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
