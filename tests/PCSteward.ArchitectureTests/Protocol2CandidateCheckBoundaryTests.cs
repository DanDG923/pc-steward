using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15; Task 50 reviewed fact extension rev 3 §4.1/§8: only the protocol-2 interpreter produces a candidate-check
/// value other than NotObserved, and the value never feeds candidate provenance.
/// </summary>
[TestClass]
public sealed class Protocol2CandidateCheckBoundaryTests
{
    private const string InterpreterFile = "ReviewedUpdateProtocol2Interpreter.cs";

    [TestMethod]
    public void OnlyTheProtocol2InterpreterProducesANonDefaultCandidateCheck()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string[] sources = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")).ToArray();
        string interpreter = sources.Single(path => Path.GetFileName(path) == InterpreterFile);
        string interpreterSource = File.ReadAllText(interpreter);
        StringAssert.Contains(interpreterSource, "private ReviewedHelperCandidateCheck(");
        // One definition and exactly one producing call, both in the interpreter file.
        Assert.AreEqual(2, Count(interpreterSource, "FromAcceptedFacts("));
        Assert.AreEqual(1, Count(interpreterSource, "ReviewedHelperCandidateCheck.FromAcceptedFacts("));
        foreach (string path in sources.Where(path => path != interpreter))
        {
            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("FromAcceptedFacts", StringComparison.Ordinal), path);
            Assert.IsFalse(source.Contains("ReviewedHelperCandidateCheck", StringComparison.Ordinal) &&
                source.Contains("ReviewedCandidateProvenance", StringComparison.Ordinal), path);
        }
        Assert.IsFalse(interpreterSource.Contains("CandidateProvenance =", StringComparison.Ordinal));
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
