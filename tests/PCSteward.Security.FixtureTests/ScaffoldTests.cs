using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class ScaffoldTests
{
    [TestMethod]
    public void SecurityFixtureProjectExistsForBoundaryTests() => Assert.AreEqual("PCSteward.Security.FixtureTests", typeof(ScaffoldTests).Assembly.GetName().Name);
}
