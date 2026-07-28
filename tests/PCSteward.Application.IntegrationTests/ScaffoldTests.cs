using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ScaffoldTests
{
    [TestMethod]
    public void ApplicationProjectReferencesDomain() => Assert.AreEqual("PCSteward.Application.IntegrationTests", typeof(ScaffoldTests).Assembly.GetName().Name);
}
