using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §7 H1 (slice S3 security review): a pinned transitive
/// closure over the compiled IL of every built <c>src</c> assembly, from the probe entry points
/// (<c>ElevatedHelperEntryPoint.RunProbeAsync</c> and <c>SelectsProbe</c>), proves that no launch, job-object, compiled-command,
/// update-item, update-endpoint or request-read surface is reachable. The direct-reference H1 test sees only the probe types'
/// own calls; this follows every call into shared code.
/// <para>The closure is deliberately conservative (it may over-approximate, never under-approximate, apart from reflection,
/// which <c>src</c> does not use on this path):</para>
/// <list type="bullet">
/// <item>a direct call reaches the exact overload (by parameter types), or every overload of that name if none matches;</item>
/// <item>a call on a type or interface also reaches every same-named method (or explicit implementation) of every
/// instantiated <c>src</c> subtype (rapid type analysis: a type is instantiated once a reachable method constructs it or
/// names it as a type token);</item>
/// <item>every virtual method of an instantiated type that no <c>src</c> interface declares (the overrides and BCL interface
/// implementations the runtime or the BCL may call back, such as <c>Dispose</c>, <c>ToString</c> or <c>ReleaseHandle</c>) is
/// reached;</item>
/// <item>a compiler-generated type (async state machine, iterator, display class, lambda cache) that a reachable method
/// names is reached in full, and every touched <c>src</c> type's static constructor is reached.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class QualificationProbeReachabilityTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Execution = "PCSteward.Execution.";
    private const string EntryPoint = Execution + "ElevatedHelperEntryPoint";

    /// <summary>The pinned probe entry points (their every overload).</summary>
    private static readonly string[] EntryMethods = ["RunProbeAsync", "SelectsProbe"];

    /// <summary>Types the probe closure never references or reaches (nested types included).</summary>
    private static readonly string[] BannedTypes =
    [
        Native + "LaunchJob", Native + "LaunchJobNativeMethods", Native + "NativeLaunchSpec", Native + "NativeCommandLine",
        Native + "ChocolateyCompiledCommand", Native + "NativeChocolateyHelperItem", Execution + "ReviewedHelperEndpoint",
        "System.Diagnostics.ProcessStartInfo",
    ];

    [TestMethod]
    public void H1NoLaunchJobCommandUpdateOrRequestSurfaceIsReachableFromTheProbeEntryPoints()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");

        Closure closure = new(assemblies);
        foreach (string method in EntryMethods)
            Assert.IsTrue(closure.ReachAll(EntryPoint, method) > 0, $"entry point {EntryPoint}.{method} was not found");
        closure.Run();

        Assert.AreEqual(0, closure.Violations.Count, string.Join(Environment.NewLine, closure.Violations.Order(StringComparer.Ordinal)));

        // Not vacuous: the closure follows the probe through interfaces, lambdas and async state machines into shared code.
        foreach (string expected in new[]
        {
            Execution + "ReviewedHelperProbeBootstrap.RunAsync", Execution + "ReviewedHelperProbeEndpoint.RunAsync",
            Execution + "ReviewedHelperProbeEndpoint+Stages.RunAsync", Execution + "ReviewedHelperProbeEndpoint+Stages.Evaluate",
            Execution + "QualificationProbeWireCodec.CreateProbeResponse", Execution + "PipeMessageFraming.WriteAsync",
            Execution + "HelperDeploymentCapabilityCheck.Evaluate", Execution + "WindowsPeerIdentityVerifier.VerifyPeer",
            Native + "NativeChocolateyProbePort.BeginProbe", Native + "NativeChocolateyProbePort.OpenPeer",
            Native + "NativeChocolateyProbeItem.ObserveClient", Native + "NativeChocolateyProbeItem.RequesterCannotModify",
            Native + "NativeChocolateyProbeItem+NativeWork.ObserveClient", Native + "ChocolateyProfileObserver.ObserveClientWithReport",
            Native + "ChocolateyRequesterProtection.CannotModify",
        })
            Assert.IsTrue(closure.ReachedLogical.Contains(expected), $"the probe closure did not reach {expected}");
        // The closure reaches compiler-generated bodies (state machines and lambdas), not only their stubs.
        Assert.IsTrue(closure.Reached.Any(key => key.Type.StartsWith(Execution + "ReviewedHelperProbeEndpoint+<RunAsync>", StringComparison.Ordinal) &&
            key.Name == "MoveNext"), "the RunAsync state machine was not reached");
        Assert.IsTrue(closure.Reached.Any(key => key.Type.StartsWith(Execution + "ReviewedHelperProbeEndpoint+Stages", StringComparison.Ordinal) &&
            key.Name.Contains("Protection", StringComparison.Ordinal)), "the A3 protection local function was not reached");
    }

    private static bool IsBannedType(string type) =>
        BannedTypes.Any(banned => type == banned || type.StartsWith(banned + "+", StringComparison.Ordinal));

    private static bool IsBannedMember(string type, string member) =>
        type == "System.Diagnostics.Process" && member == "Start" ||
        member.StartsWith("CreateProcess", StringComparison.Ordinal) ||
        member.Contains("JobObject", StringComparison.Ordinal) ||
        member == "UpgradeAsync" ||
        member.StartsWith("QueryCandidate", StringComparison.Ordinal) ||
        member == "TryReadRequest" ||
        type == Execution + "PipeMessageFraming" && member == "ReadAsync";

    private static bool IsCompilerGenerated(string type) => type[(type.LastIndexOf('+') + 1)..].StartsWith('<');

    private readonly record struct MethodKey(string Type, string Name, string Signature);

    /// <summary>The worklist closure over the merged call graph of the built <c>src</c> assemblies.</summary>
    private sealed class Closure
    {
        private readonly Dictionary<string, CompiledIlScanner.TypeFacts> types = new(StringComparer.Ordinal);
        private readonly Dictionary<MethodKey, List<CompiledIlScanner.IlReference>> bodies = [];
        private readonly Dictionary<string, HashSet<string>> ancestors = new(StringComparer.Ordinal);
        private readonly HashSet<string> touched = new(StringComparer.Ordinal);
        private readonly HashSet<string> instantiated = new(StringComparer.Ordinal);
        private readonly HashSet<(string Type, string Member)> callSites = [];
        private readonly Queue<MethodKey> work = new();

        internal Closure(IEnumerable<string> assemblies)
        {
            foreach (string path in assemblies)
            {
                foreach (CompiledIlScanner.TypeFacts facts in CompiledIlScanner.Types(path))
                    types[facts.FullName] = types.TryGetValue(facts.FullName, out CompiledIlScanner.TypeFacts? existing)
                        ? existing with { Interfaces = [.. existing.Interfaces.Union(facts.Interfaces)], Methods = [.. existing.Methods.Union(facts.Methods)] }
                        : facts;
                foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                {
                    MethodKey key = new(reference.CallerType ?? reference.Caller, reference.CallerMethod ?? string.Empty, reference.CallerSignature);
                    if (!bodies.TryGetValue(key, out List<CompiledIlScanner.IlReference>? body)) bodies[key] = body = [];
                    body.Add(reference);
                }
            }
        }

        internal HashSet<MethodKey> Reached { get; } = [];

        internal List<string> Violations { get; } = [];

        /// <summary>The source-level "Type.Method" of every reached method (generated types and lambdas mapped back).</summary>
        internal HashSet<string> ReachedLogical => [.. Reached.Select(key =>
            new CompiledIlScanner.IlReference(key.Type, string.Empty, string.Empty, CompiledIlScanner.ReferenceKind.Method)
            { CallerType = key.Type, CallerMethod = key.Name }.LogicalCaller)];

        /// <summary>Reaches every overload of <paramref name="name"/> on <paramref name="type"/>; returns how many there are.</summary>
        internal int ReachAll(string type, string name)
        {
            if (!types.TryGetValue(type, out CompiledIlScanner.TypeFacts? facts)) return 0;
            int count = 0;
            foreach (CompiledIlScanner.MethodFacts method in facts.Methods.Where(method => method.Name == name))
            {
                Reach(new MethodKey(type, method.Name, method.Signature));
                count++;
            }
            Touch(type);
            return count;
        }

        internal void Run()
        {
            while (work.TryDequeue(out MethodKey key))
            {
                if (IsBannedType(key.Type)) Violations.Add($"reached a method of a banned type: {key.Type}.{key.Name}{key.Signature}");
                if (!bodies.TryGetValue(key, out List<CompiledIlScanner.IlReference>? body)) continue;
                foreach (CompiledIlScanner.IlReference reference in body)
                {
                    if (IsBannedType(reference.TargetType) || IsBannedMember(reference.TargetType, reference.TargetMember))
                        Violations.Add($"{key.Type}.{key.Name}{key.Signature} references {reference.TargetType}.{reference.TargetMember}{reference.TargetSignature}");
                    Touch(reference.TargetType);
                    switch (reference.Kind)
                    {
                        case CompiledIlScanner.ReferenceKind.Type:
                            Instantiate(reference.TargetType);
                            break;
                        case CompiledIlScanner.ReferenceKind.Method when reference.TargetMember == ".ctor":
                            Instantiate(reference.TargetType);
                            ReachDirect(reference.TargetType, reference.TargetMember, reference.TargetSignature);
                            break;
                        case CompiledIlScanner.ReferenceKind.Method:
                            ReachDirect(reference.TargetType, reference.TargetMember, reference.TargetSignature);
                            if (callSites.Add((reference.TargetType, reference.TargetMember)))
                                foreach (string type in instantiated.ToArray())
                                    if (Ancestors(type).Contains(reference.TargetType)) Dispatch(type, reference.TargetMember);
                            break;
                    }
                }
            }
        }

        private void Reach(MethodKey key)
        {
            if (Reached.Add(key)) work.Enqueue(key);
        }

        /// <summary>The exact overload, or every overload of that name when no signature matches.</summary>
        private void ReachDirect(string type, string name, string signature)
        {
            if (!types.TryGetValue(type, out CompiledIlScanner.TypeFacts? facts)) return;
            CompiledIlScanner.MethodFacts[] named = [.. facts.Methods.Where(method => method.Name == name)];
            CompiledIlScanner.MethodFacts[] exact = [.. named.Where(method => method.Signature == signature)];
            foreach (CompiledIlScanner.MethodFacts method in exact.Length > 0 ? exact : named) Reach(new MethodKey(type, method.Name, method.Signature));
        }

        /// <summary>A touched <c>src</c> type: its static constructor runs; a compiler-generated type is reached in full.</summary>
        private void Touch(string type)
        {
            if (!types.TryGetValue(type, out CompiledIlScanner.TypeFacts? facts) || !touched.Add(type)) return;
            foreach (CompiledIlScanner.MethodFacts method in facts.Methods.Where(method => method.Name == ".cctor" || IsCompilerGenerated(type)))
                Reach(new MethodKey(type, method.Name, method.Signature));
            if (IsCompilerGenerated(type)) Instantiate(type);
        }

        private void Instantiate(string type)
        {
            if (!types.ContainsKey(type) || !instantiated.Add(type)) return;
            Touch(type);
            HashSet<string> above = Ancestors(type);
            foreach ((string target, string member) in callSites.ToArray())
                if (above.Contains(target)) Dispatch(type, member);
            // Callbacks from the runtime or the BCL: every virtual method that no src interface of the type declares.
            HashSet<string> srcInterfaceMembers = [.. above.Where(name => types.TryGetValue(name, out CompiledIlScanner.TypeFacts? facts) && facts.IsInterface)
                .SelectMany(name => types[name].Methods.Select(method => method.Name))];
            foreach (string inChain in Chain(type))
                foreach (CompiledIlScanner.MethodFacts method in types[inChain].Methods.Where(method => method.IsVirtual))
                    if (!srcInterfaceMembers.Any(name => method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal)))
                        Reach(new MethodKey(inChain, method.Name, method.Signature));
        }

        /// <summary>A call of <paramref name="member"/> dispatched to <paramref name="type"/>: every same-named method in its src chain.</summary>
        private void Dispatch(string type, string member)
        {
            foreach (string inChain in Chain(type))
                foreach (CompiledIlScanner.MethodFacts method in types[inChain].Methods)
                    if (method.Name == member || method.Name.EndsWith("." + member, StringComparison.Ordinal))
                        Reach(new MethodKey(inChain, method.Name, method.Signature));
        }

        /// <summary>The type and its src base classes.</summary>
        private IEnumerable<string> Chain(string type)
        {
            for (string? current = type; current is not null && types.TryGetValue(current, out CompiledIlScanner.TypeFacts? facts); current = facts.BaseType)
                yield return current;
        }

        /// <summary>The type, every base type and every interface it implements, transitively (src and BCL names).</summary>
        private HashSet<string> Ancestors(string type)
        {
            if (ancestors.TryGetValue(type, out HashSet<string>? known)) return known;
            HashSet<string> result = new(StringComparer.Ordinal) { type };
            ancestors[type] = result; // guards a cycle
            if (types.TryGetValue(type, out CompiledIlScanner.TypeFacts? facts))
            {
                if (facts.BaseType is { } baseType) result.UnionWith(Ancestors(baseType));
                foreach (string implemented in facts.Interfaces) result.UnionWith(Ancestors(implemented));
            }
            return result;
        }
    }
}
