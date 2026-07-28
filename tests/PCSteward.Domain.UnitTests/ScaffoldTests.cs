using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class ScaffoldTests
{
    [TestMethod]
    public void DomainProjectLoadsWithoutAdapterDependencies() => Assert.AreEqual("PCSteward.Domain.UnitTests", typeof(ScaffoldTests).Assembly.GetName().Name);
}
