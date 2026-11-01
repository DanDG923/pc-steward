using System.Collections.ObjectModel;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §4 and §4.1 (rev-3 M-B, rev-4 L2): the closed set of argument lists the
/// helper may pass to the held root engine. The base and every nested record have private constructors, so the only way
/// to obtain a command is a validating factory over a pinned builder:
/// <list type="bullet">
/// <item><see cref="TryListExact"/>: <see cref="ChocolateyExactListArguments.Build"/>; existing 30 s budget (the
/// protocol-1 executor recheck).</item>
/// <item><see cref="CreateOutdated"/>: <see cref="ChocolateyUpdateSourceConnector.BuildOutdatedArguments"/>; existing
/// 45 s budget (<see cref="ChocolateyUpdateSourceConnector.Timeout"/>).</item>
/// <item><see cref="TryUpgrade"/>: <see cref="ChocolateyReviewedUpdateArguments.Build"/>; existing 10 min budget
/// (<c>ChocolateyMachineUpdateExecutor.UpdateTimeout</c>).</item>
/// </list>
/// A factory returns null when its builder refuses. <see cref="Arguments"/> is an immutable copy and has no setter, so a
/// <c>with</c> expression cannot change it. No command is ever derived from wire, database or payload strings other than
/// through the reviewed operation and those builders.
/// </summary>
internal abstract record ChocolateyCompiledCommand
{
    internal static readonly TimeSpan ListExactBudget = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan OutdatedBudget = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan UpgradeBudget = TimeSpan.FromMinutes(10);

    private ChocolateyCompiledCommand(IReadOnlyList<string> arguments, TimeSpan budget)
    {
        string[] copied = [.. arguments];
        if (copied.Length == 0 || copied.Any(static argument => string.IsNullOrEmpty(argument) || argument.Any(char.IsControl)))
            throw new ArgumentException("A compiled command needs non-empty, control-free arguments.", nameof(arguments));
        Arguments = new ReadOnlyCollection<string>(copied);
        Budget = budget;
    }

    /// <summary>The fixed argument list (argv[1..]); argv[0] is always the held engine path.</summary>
    internal IReadOnlyList<string> Arguments { get; }

    /// <summary>The command's existing item budget; each launch uses min(budget, remaining) (§4 M5).</summary>
    internal TimeSpan Budget { get; }

    /// <summary>Pinned <c>list &lt;ID&gt; --exact --limit-output --no-color</c>.</summary>
    internal sealed record ListExact : ChocolateyCompiledCommand
    {
        private ListExact(string packageId, IReadOnlyList<string> arguments) : base(arguments, ListExactBudget) => PackageId = packageId;

        internal string PackageId { get; }

        /// <summary>Called only by <see cref="TryListExact"/> (architecture test).</summary>
        internal static ListExact? TryCreate(ReviewedUpdateOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            try { return new ListExact(operation.Selection.PackageId, ChocolateyExactListArguments.Build(operation)); }
            catch (ArgumentException) { return null; }
        }
    }

    /// <summary>Pinned <c>outdated --source &lt;compiled Community URL&gt; --ignore-unfound --limit-output --no-color --execution-timeout 60</c>.</summary>
    internal sealed record Outdated : ChocolateyCompiledCommand
    {
        private Outdated(IReadOnlyList<string> arguments) : base(arguments, OutdatedBudget) { }

        /// <summary>Called only by <see cref="CreateOutdated"/> (architecture test).</summary>
        internal static Outdated Create() => new(ChocolateyUpdateSourceConnector.BuildOutdatedArguments());
    }

    /// <summary>Pinned fixed-feed <c>upgrade</c> of the reviewed ID to the reviewed normalized candidate.</summary>
    internal sealed record Upgrade : ChocolateyCompiledCommand
    {
        private Upgrade(string packageId, string candidateVersion, IReadOnlyList<string> arguments) : base(arguments, UpgradeBudget)
        {
            PackageId = packageId;
            CandidateVersion = candidateVersion;
        }

        internal string PackageId { get; }

        internal string CandidateVersion { get; }

        /// <summary>Called only by <see cref="TryUpgrade"/> (architecture test).</summary>
        internal static Upgrade? TryCreate(ReviewedUpdateOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            try
            {
                return new Upgrade(operation.Selection.PackageId, operation.Selection.CandidateVersion,
                    ChocolateyReviewedUpdateArguments.Build(operation));
            }
            catch (ArgumentException) { return null; }
        }
    }

    /// <summary>The stage-C installed recheck, or null when the operation is not the exact accepted route shape.</summary>
    internal static ListExact? TryListExact(ReviewedUpdateOperation operation) => ListExact.TryCreate(operation);

    /// <summary>The stage-C candidate query (operation-independent).</summary>
    internal static Outdated CreateOutdated() => Outdated.Create();

    /// <summary>The single fixed upgrade, or null when the operation is not the exact accepted route shape.</summary>
    internal static Upgrade? TryUpgrade(ReviewedUpdateOperation operation) => Upgrade.TryCreate(operation);
}
