using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-04/15, TM-20, TM-23; ADR-032 revision 2 §3 checks 1 and 2 and §6 step 3: the shared rules the desktop and the
/// elevated helper both apply to GitHub's run and artefact answers. A genuine answer passes; a duplicated property, an
/// expired or duplicated artefact, an oversized or empty answer, and a malformed digest input are refused. Pure: these run
/// on every platform.
/// </summary>
[TestClass]
public sealed class ReleaseRunEvidenceTests
{
    private const string Digest = "sha256:" + "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12";
    private const string ZipSha256 = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    private static readonly string Run = "{\"id\":4242,\"path\":\".github/workflows/release-payload.yml\",\"event\":\"workflow_dispatch\"," +
        "\"head_branch\":\"phase/4-beta\",\"head_sha\":\"" + ReleaseCheckFixture.Commit + "\",\"status\":\"completed\",\"conclusion\":\"success\"," +
        "\"repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"},\"head_repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"}}";

    private const string Artifacts = "{\"total_count\":1,\"artifacts\":[{\"name\":\"pc-steward-release-check\",\"digest\":\"" + Digest + "\",\"expired\":false}]}";

    private static ReleaseRunVerdict CheckRun(string json) => ReleaseRunEvidence.CheckRun(Encoding.UTF8.GetBytes(json), 4242, ReleaseCheckFixture.Commit);

    private static ReleaseRunVerdict CheckArtifact(string json) => ReleaseRunEvidence.CheckArtifact(Encoding.UTF8.GetBytes(json), ZipSha256);

    [TestMethod]
    public void AGenuineRunAndArtefactPass()
    {
        Assert.AreEqual(ReleaseRunVerdict.Genuine, CheckRun(Run));
        Assert.AreEqual(ReleaseRunVerdict.Genuine, CheckArtifact(Artifacts));
    }

    [TestMethod]
    public void ADuplicatedPropertyIsRefused()
    {
        // A second "conclusion" must not be able to override (or hide behind) the first.
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, CheckRun(Run.Replace("\"conclusion\":\"success\"", "\"conclusion\":\"failure\",\"conclusion\":\"success\"", StringComparison.Ordinal)));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, CheckRun(Run.Replace("\"conclusion\":\"success\"", "\"conclusion\":\"success\",\"conclusion\":\"failure\"", StringComparison.Ordinal)));
        Assert.AreEqual(ReleaseRunVerdict.ArtifactGone, CheckArtifact(Artifacts.Replace("\"expired\":false", "\"digest\":\"" + Digest + "\",\"expired\":false", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnExpiredOrDuplicatedArtefactIsRefused()
    {
        Assert.AreEqual(ReleaseRunVerdict.ArtifactGone, CheckArtifact(Artifacts.Replace("\"expired\":false", "\"expired\":true", StringComparison.Ordinal)));
        Assert.AreEqual(ReleaseRunVerdict.ArtifactGone, CheckArtifact(Artifacts.Replace("\"expired\":false", "\"expired\":\"no\"", StringComparison.Ordinal)));
        Assert.AreEqual(ReleaseRunVerdict.ArtifactMismatch, CheckArtifact(Artifacts.Replace("[{", "[{\"name\":\"pc-steward-release-check\",\"digest\":\"" + Digest + "\"},{", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void OversizedEmptyOrMalformedInputsAreRefused()
    {
        byte[] oversized = Encoding.UTF8.GetBytes(Run + new string(' ', ReleaseRunEvidence.MaximumAnswerBytes));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, ReleaseRunEvidence.CheckRun(oversized, 4242, ReleaseCheckFixture.Commit));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, ReleaseRunEvidence.CheckRun([], 4242, ReleaseCheckFixture.Commit));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, ReleaseRunEvidence.CheckRun(Encoding.UTF8.GetBytes(Run), 0, ReleaseCheckFixture.Commit));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, CheckRun(Run.Replace("\"id\":4242", "\"id\":\"4242\"", StringComparison.Ordinal)));
        Assert.AreEqual(ReleaseRunVerdict.RunNotGenuine, CheckRun(Run + "//"));
        Assert.AreEqual(ReleaseRunVerdict.ArtifactGone, ReleaseRunEvidence.CheckArtifact(Encoding.UTF8.GetBytes(Artifacts), "not-a-hash"));
        Assert.AreEqual(ReleaseRunVerdict.ArtifactGone, ReleaseRunEvidence.CheckArtifact(Encoding.UTF8.GetBytes(Artifacts), ZipSha256[..63] + "G"));
    }
}
