using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PatchStyle.Prototype;

namespace PatchStyle.Prototype.Tests;

[TestClass]
public sealed class CatalogPrototypeTests
{
    private static byte[] SampleBytes => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-catalog.v1.json"));

    [TestMethod]
    public void SampleCatalogLoadsWithMatchingHash()
    {
        byte[] bytes = SampleBytes;
        CatalogLoadResult result = CatalogLoader.Load(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Errors));
        Assert.AreEqual(20, result.Entries.Count);
        Console.WriteLine($"sample catalog: entries={result.Entries.Count} bytes={bytes.Length} loadMs={result.LoadTime.TotalMilliseconds:F1}");
    }

    [TestMethod]
    [DataRow("wrong-hash")]
    [DataRow("unknown-field")]
    [DataRow("schema-2")]
    [DataRow("duplicate-id")]
    [DataRow("bad-id")]
    [DataRow("unanchored-pattern")]
    [DataRow("long-pattern")]
    [DataRow("backreference-pattern")]
    [DataRow("lookahead-pattern")]
    [DataRow("chocolatey-user-scope")]
    [DataRow("unknown-provider")]
    [DataRow("command-in-id")]
    [DataRow("http-homepage")]
    [DataRow("two-defaults")]
    [DataRow("duplicate-package")]
    [DataRow("bad-process")]
    [DataRow("oversize")]
    [DataRow("install-switch-field")]
    public void HostileCatalogDisablesCatalog(string mutation)
    {
        byte[] bytes = SampleBytes;
        string? hash = null;
        JsonObject root = JsonNode.Parse(bytes)!.AsObject();
        JsonArray entries = root["entries"]!.AsArray();
        JsonObject first = entries[0]!.AsObject();
        JsonObject detection = first["detection"]!.AsObject();
        JsonObject provider = first["providers"]![0]!.AsObject();
        switch (mutation)
        {
            case "wrong-hash": hash = new string('0', 64); break;
            case "unknown-field": first["installerUrl"] = "https://example.invalid/setup.exe"; break;
            case "schema-2": root["schemaVersion"] = 2; break;
            case "duplicate-id": entries[1]!["id"] = first["id"]!.GetValue<string>(); break;
            case "bad-id": first["id"] = "firefox"; break;
            case "unanchored-pattern": detection["arpDisplayName"] = "Mozilla Firefox"; break;
            case "long-pattern": detection["arpDisplayName"] = "^" + new string('a', 130) + "$"; break;
            case "backreference-pattern": detection["arpDisplayName"] = @"^(a)\1$"; break;
            case "lookahead-pattern": detection["arpDisplayName"] = "^(?=Moz).*$"; break;
            case "chocolatey-user-scope": first["providers"]![1]!["scopes"] = new JsonArray("user"); break;
            case "unknown-provider": provider["provider"] = "scoop"; break;
            case "command-in-id": provider["packageId"] = "Mozilla.Firefox & calc"; break;
            case "http-homepage": first["homepage"] = "http://example.invalid"; break;
            case "two-defaults": first["providers"]![1]!["default"] = true; break;
            case "duplicate-package": entries[1]!["providers"]![0]!["packageId"] = provider["packageId"]!.GetValue<string>(); break;
            case "bad-process": first["processNames"] = new JsonArray("..\\evil.exe"); break;
            case "oversize": first["summary"] = new string('x', CatalogLoader.MaximumBytes); break;
            case "install-switch-field": provider["silentArguments"] = "/S"; break;
            default: Assert.Fail(mutation); break;
        }

        byte[] mutated = mutation == "wrong-hash" ? bytes : Encoding.UTF8.GetBytes(root.ToJsonString());
        CatalogLoadResult result = CatalogLoader.Load(mutated, hash);
        Assert.IsFalse(result.IsValid, mutation);
        Assert.AreEqual(0, result.Entries.Count);
        Console.WriteLine($"{mutation} -> {string.Join(", ", result.Errors)}");
    }

    [TestMethod]
    [DataRow("^(a+)+$")]
    [DataRow("^(a|aa)*$")]
    [DataRow("^(ab){2,}$")]
    [DataRow(@"^(a)\1$")]
    [DataRow("^(?=a).*$")]
    [DataRow("^(?i)a$")]
    [DataRow("^(?:a)$")]
    public void DangerousPatternGrammarIsRejected(string pattern) =>
        Assert.IsFalse(CatalogLoader.IsAllowedPatternGrammar(pattern), pattern);

    [TestMethod]
    [DataRow(@"^Mozilla Firefox( \((x64|x86|arm64) [a-z]{2,3}(-[A-Z]{2})?\))?$")]
    [DataRow(@"^7-Zip \d{1,2}\.\d{2}( \((x64|arm64)\))?$")]
    [DataRow(@"^Notepad\+\+( \((32|64)-bit (x86|x64|ARM64)\))?$")]
    public void OrdinaryPatternGrammarIsAllowed(string pattern) =>
        Assert.IsTrue(CatalogLoader.IsAllowedPatternGrammar(pattern), pattern);

    [TestMethod]
    public void TimeoutBoundsAPathologicalPatternThatBypassedValidation()
    {
        System.Text.RegularExpressions.Regex pattern = CatalogLoader.CompileDetectionPattern("^(a+)+$");
        Stopwatch watch = Stopwatch.StartNew();
        bool timedOut = false;
        try { _ = pattern.IsMatch(new string('a', 40) + "!"); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { timedOut = true; }
        watch.Stop();
        Console.WriteLine($"(a+)+ backstop: timedOut={timedOut} ms={watch.Elapsed.TotalMilliseconds:F1}");
        Assert.IsTrue(timedOut);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void ThreeHundredEntryCatalogLoadsAndDetectsWithinBudget()
    {
        JsonObject root = JsonNode.Parse(SampleBytes)!.AsObject();
        JsonArray source = root["entries"]!.AsArray();
        JsonArray scaled = [];
        for (int index = 0; index < 300; index++)
        {
            JsonObject entry = source[index % source.Count]!.DeepClone().AsObject();
            entry["id"] = "pcs:entry-" + index;
            foreach (JsonNode? provider in entry["providers"]!.AsArray())
            {
                string id = provider!["packageId"]!.GetValue<string>();
                provider["packageId"] = provider["provider"]!.GetValue<string>() == "winget" ? id + ".N" + index : id + "-n" + index;
            }

            JsonObject detection = entry["detection"]!.AsObject();
            string name = detection["arpDisplayName"]!.GetValue<string>();
            detection["arpDisplayName"] = "^N" + index + " " + name[1..];
            scaled.Add(entry);
        }

        root["entries"] = scaled;
        byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        _ = CatalogLoader.Load(SampleBytes, null);
        CatalogLoadResult result = CatalogLoader.Load(bytes, null);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Errors));
        Stopwatch build = Stopwatch.StartNew();
        CatalogDetector detector = new(result.Entries);
        build.Stop();
        InstalledRecord[] records = Enumerable.Range(0, 700).Select(index => new InstalledRecord(
            index % 3 == 0 ? "N" + (index % 300) + " Mozilla Firefox (x64 en-US)" : "Unrelated Application " + index,
            index % 3 == 0 ? "Mozilla" : "Somebody", "registry", null)).ToArray();
        Stopwatch detect = Stopwatch.StartNew();
        int detected = records.Count(record => detector.Detect(record).Kind != DetectionKind.None);
        detect.Stop();
        Console.WriteLine($"scaled catalog: entries=300 bytes={bytes.Length} loadMs={result.LoadTime.TotalMilliseconds:F1} detectorBuildMs={build.Elapsed.TotalMilliseconds:F1} detect700Ms={detect.Elapsed.TotalMilliseconds:F1} detected={detected}");
        Assert.IsTrue(result.LoadTime + build.Elapsed + detect.Elapsed < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void IllustrativeDetectionHasNoFalsePositivesAndNoExactWithoutEvidence()
    {
        CatalogLoadResult catalog = CatalogLoader.Load(SampleBytes, null);
        Assert.IsTrue(catalog.IsValid);
        CatalogDetector detector = new(catalog.Entries);
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "illustrative-installed-records.json")));
        int truePositive = 0, falsePositive = 0, falseNegative = 0, trueNegative = 0, exact = 0, nonExactDetected = 0, ambiguous = 0;
        foreach (JsonElement row in fixture.RootElement.GetProperty("records").EnumerateArray())
        {
            InstalledRecord record = new(
                row.GetProperty("displayName").GetString()!,
                row.GetProperty("publisher").ValueKind == JsonValueKind.Null ? null : row.GetProperty("publisher").GetString(),
                row.GetProperty("provider").GetString()!,
                row.GetProperty("providerIdentity").ValueKind == JsonValueKind.Null ? null : row.GetProperty("providerIdentity").GetString());
            string? expected = row.GetProperty("expectedEntry").ValueKind == JsonValueKind.Null ? null : row.GetProperty("expectedEntry").GetString();
            DetectionOutcome outcome = detector.Detect(record);
            string? actual = outcome.Kind is DetectionKind.None or DetectionKind.Ambiguous ? null : outcome.EntryIds[0];
            if (outcome.Kind == DetectionKind.Ambiguous) ambiguous++;
            if (actual is not null && outcome.IsExact) exact++;
            if (actual is not null && !outcome.IsExact) nonExactDetected++;
            if (expected is null && actual is null) trueNegative++;
            else if (expected is null) { falsePositive++; Console.WriteLine($"FALSE POSITIVE {record.DisplayName} -> {actual}"); }
            else if (actual == expected) truePositive++;
            else { falseNegative++; Console.WriteLine($"MISSED {record.DisplayName} expected {expected} got {actual ?? outcome.Kind.ToString()}"); }
        }

        Console.WriteLine($"illustrative detection: TP={truePositive} FP={falsePositive} FN={falseNegative} TN={trueNegative} exact={exact} detectedNotExact={nonExactDetected} ambiguous={ambiguous}");
        Assert.AreEqual(0, falsePositive);
        Assert.AreEqual(0, falseNegative);
        Assert.AreEqual(1, nonExactDetected, "A name match without publisher evidence must not be Exact.");
    }
}
