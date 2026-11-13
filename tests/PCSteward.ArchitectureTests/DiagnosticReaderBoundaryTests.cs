using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-DIA-01 (ADR-027 Q1, Task 64 stage 3): the Windows diagnostic readers are read only, unelevated and narrow. The project
/// references only Application; its only native calls are the six wevtapi query/render/close entry points; its only COM
/// classes are the WMI scripting locator and the Windows Update session; it never subscribes to, exports, clears or formats an
/// event log, never searches, downloads, installs or hides an update, never calls a WMI method or writes a WMI object, and never
/// touches files, the registry, processes or the network. The scheduled host never references it.
/// </summary>
[TestClass]
public sealed class DiagnosticReaderBoundaryTests
{
    private const string Project = "PCSteward.Diagnostics.Windows";
    private static readonly string[] AllowedReferences = ["../PCSteward.Application/PCSteward.Application.csproj"];
    private static readonly string[] EntryPoints = ["EvtQuery", "EvtNext", "EvtCreateRenderContext", "EvtRender", "EvtCancel", "EvtClose"];
    private static readonly string[] ProgIds = ["WbemScripting.SWbemLocator", "Microsoft.Update.Session"];

    [TestMethod]
    public void TheReadersProjectReferencesOnlyApplicationAndTheScheduledHostNeverReferencesIt()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root, "src", Project, Project + ".csproj"));
        CollectionAssert.AreEqual(AllowedReferences,
            project.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")).ToArray());
        Assert.IsFalse(project.Descendants("PackageReference").Any());

        foreach (string host in new[] { "PCSteward.ScheduledHost", "PCSteward.ElevatedHelper", "PCSteward.Execution" })
            Assert.IsFalse(File.ReadAllText(Path.Combine(root, "src", host, host + ".csproj")).Contains(Project, StringComparison.Ordinal), host);
    }

    [TestMethod]
    public void NativeAndComAccessIsTheNamedReadOnlyQueryRoutesOnly()
    {
        string code = string.Join(Environment.NewLine, Sources().Select(path => File.ReadAllText(path)));

        CollectionAssert.AreEquivalent(EntryPoints,
            Regex.Matches(code, @"static\s+extern\s+\w+\s+(\w+)\s*\(").Select(match => match.Groups[1].Value).ToArray());
        Assert.AreEqual(6, Regex.Count(code, @"\[DllImport\(""wevtapi\.dll"""), "wevtapi only");
        CollectionAssert.AreEquivalent(ProgIds,
            Regex.Matches(code, @"ProgId\s*=\s*""([^""]+)""").Select(match => match.Groups[1].Value).ToArray());
        Assert.AreEqual(2, Regex.Count(code, @"Type\.GetTypeFromProgID\("));

        foreach (string forbidden in new[]
        {
            "EvtSubscribe", "EvtClearLog", "EvtExportLog", "EvtArchiveExportedLog", "EvtFormatMessage", "EvtOpenSession", "EvtOpenLog", "EvtSetChannelConfigProperty",
            "EvtRenderEventXml", "EvtRenderBookmark", "Win32_Product", ".Search(", "BeginSearch", "CreateUpdateDownloader", "CreateUpdateInstaller", "CreateUpdateServiceManager",
            "IsHidden", "AcceptEula", "Online", "ServerSelection", "ServiceID", "ExecMethod", "Put_", "Delete_", "SpawnInstance", "ExecNotificationQuery",
            "InstancesOf", "Get(", "\"Message\"", "\"User\"", "\"ComputerName\"", "\"InsertionStrings\"",
            "Registry", "Process", "File.", "Directory.", "FileStream", "HttpClient", "Socket", "Environment.", "unsafe",
        })
            Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"the readers use {forbidden}");

        StringAssert.Contains(code, "ConnectServer(\".\", Namespace)");
        StringAssert.Contains(code, "private const string Namespace = @\"root\\cimv2\";");
        Assert.AreEqual(1, Regex.Count(code, @"\.ExecQuery\("), "one WQL query");
        Assert.AreEqual(1, Regex.Count(code, @"\.QueryHistory\("), "history paging only");
        Assert.AreEqual(1, Regex.Count(code, @"\.GetTotalHistoryCount\("));
        Assert.AreEqual(1, Regex.Count(code, @"\.CreateUpdateSearcher\("));
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", Project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}
