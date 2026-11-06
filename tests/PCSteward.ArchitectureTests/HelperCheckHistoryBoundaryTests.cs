using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-HIS-01; FR-UPD-15. Task 50 adapter gates §B.2/§B.7 and §R B2-L1: construction of the recorded helper check is
/// confined to the interpreter's single adapter, restore and the terminal record stay in history files, the value never
/// reaches review/admission/evidence/reconciliation code, and every production composition root injects the grammar.
/// </summary>
[TestClass]
public sealed class HelperCheckHistoryBoundaryTests
{
    private const string InterpreterFile = "ReviewedUpdateProtocol2Interpreter.cs";
    private const string CheckFile = "RecordedHelperCandidateCheck.cs";
    private static readonly string[] RestoreUsers = ["ReviewedUpdateHistory.cs"];
    private static readonly string[] TerminalRecordUsers = ["ReviewedUpdateHistory.cs", "SqliteReviewedUpdateHistory.cs"];
    private static readonly string[] CompositionRoots = ["App.xaml.cs", "Program.cs"];

    [TestMethod]
    public void FromDesktopDerivedHasExactlyOneCallSiteInTheInterpreterFile()
    {
        Dictionary<string, string> sources = Sources();
        string definition = sources.Single(pair => Path.GetFileName(pair.Key) == CheckFile).Value;
        StringAssert.Contains(definition, "private RecordedHelperCandidateCheck(");
        Assert.AreEqual(1, Count(definition, "FromDesktopDerived("), "the definition only");
        string interpreter = sources.Single(pair => Path.GetFileName(pair.Key) == InterpreterFile).Value;
        Assert.AreEqual(1, Count(interpreter, "RecordedHelperCandidateCheck.FromDesktopDerived("));
        StringAssert.Contains(interpreter, "internal RecordedHelperCandidateCheck ToRecorded()");
        foreach ((string path, string source) in sources)
            if (Path.GetFileName(path) is not (CheckFile or InterpreterFile))
                Assert.AreEqual(0, Count(source, "FromDesktopDerived"), path);
        // The adapter itself is called only inside the interpreter file (its WorkflowObservation member).
        foreach ((string path, string source) in sources)
            if (Path.GetFileName(path) != InterpreterFile) Assert.AreEqual(0, Count(source, "ToRecorded("), path);
    }

    [TestMethod]
    public void RestoreAndTheTerminalRecordStayInHistoryFiles()
    {
        Dictionary<string, string> sources = Sources();
        string[] restoreUsers = sources.Where(pair => pair.Value.Contains("RecordedHelperCandidateCheck.TryRestore(", StringComparison.Ordinal))
            .Select(pair => Path.GetFileName(pair.Key)).ToArray();
        CollectionAssert.AreEqual(RestoreUsers, restoreUsers);
        string[] terminalRecordUsers = sources.Where(pair => Regex.IsMatch(pair.Value, @"\bReviewedUpdateTerminalRecord\b"))
            .Select(pair => Path.GetFileName(pair.Key)).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(TerminalRecordUsers, terminalRecordUsers);
    }

    [TestMethod]
    public void HelperCheckNeverReachesReviewAdmissionEvidenceOrReconciliationCode()
    {
        Dictionary<string, string> sources = Sources();
        string[] forbiddenFiles = sources.Keys.Where(path =>
        {
            string name = Path.GetFileName(path);
            return name.StartsWith("ReviewedUpdateReview", StringComparison.Ordinal) ||
                name is "UpdateDispatchAdmission.cs" or "UpdateDispatchReconciliation.cs" or "UpdateDispatchSettlement.cs" or "ReviewedUpdateDraft.cs"
                    or "SqliteReviewedOperations.cs" ||
                path.Split(Path.DirectorySeparatorChar).Contains("PCSteward.Domain") ||
                Regex.IsMatch(sources[path], @":\s*IReviewedUpdateDispatchEvidenceSource\b");
        }).ToArray();
        Assert.IsGreaterThanOrEqualTo(6, forbiddenFiles.Length);
        foreach (string path in forbiddenFiles)
            foreach (string name in new[] { "RecordedHelperCandidateCheck", "RecordedHelperCheckKind", "RecordedHelperRefusalReason", "HelperCheck" })
                Assert.IsFalse(sources[path].Contains(name, StringComparison.Ordinal), $"{path}: {name}");
        // The dispatch-state reader used by barriers and reconciliation never parses the terminal (store rollback, §B.7).
        string dispatches = sources.Single(pair => Path.GetFileName(pair.Key) == "SqliteUpdateDispatches.cs").Value;
        Assert.IsFalse(dispatches.Contains("TerminalRecord", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EveryProductionSqliteDatabaseIsBuiltWithTheChocolateyHelperVersionGrammar()
    {
        Dictionary<string, string> sources = Sources();
        Regex construction = new(@"(new\s+SqliteDatabase\s*\(|SqliteDatabase\s+\w+\s*=\s*new\s*\()(?<args>[^;]*);");
        List<string> roots = [];
        foreach ((string path, string source) in sources)
        {
            if (path.Split(Path.DirectorySeparatorChar).Contains("PCSteward.Persistence.Sqlite")) continue;
            foreach (Match match in construction.Matches(source))
            {
                roots.Add(Path.GetFileName(path));
                StringAssert.Contains(match.Groups["args"].Value, "new ChocolateyHelperVersionGrammar()", path);
            }
        }
        CollectionAssert.AreEquivalent(CompositionRoots, roots);
        string database = sources.Single(pair => Path.GetFileName(pair.Key) == "SqliteDatabase.cs").Value;
        StringAssert.Contains(database, "internal SqliteDatabase(string databasePath)");
        StringAssert.Contains(database, "public SqliteDatabase(string databasePath, IReviewedHelperVersionGrammar helperVersionGrammar)");
        Assert.IsFalse(Regex.IsMatch(database, @"public\s+SqliteDatabase\s*\(\s*string\s+\w+\s*\)"));
        string grammar = sources.Single(pair => Path.GetFileName(pair.Key) == "ChocolateyHelperVersionGrammar.cs").Value;
        StringAssert.Contains(grammar, "ChocolateyVersionGrammar.IsNormalized(value, RecordedHelperCandidateCheck.MaximumVersionLength)");
    }

    private static Dictionary<string, string> Sources()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(path => path, File.ReadAllText);
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = source.IndexOf(value, StringComparison.Ordinal); index >= 0; index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
