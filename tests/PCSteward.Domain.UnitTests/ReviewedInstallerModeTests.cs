using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>FR-UPD-15: a selected mode cannot be confused with lack of provider mode control.</summary>
[TestClass]
public sealed class ReviewedInstallerModeTests
{
    [TestMethod]
    [DataRow(InstallerMode.Silent)]
    [DataRow(InstallerMode.Interactive)]
    public void SelectedModeRetainsExactlyTheReviewedChoice(InstallerMode mode)
    {
        ReviewedInstallerMode binding = ReviewedInstallerMode.Selected(mode);

        Assert.AreEqual(InstallerModeBinding.Selected, binding.Binding);
        Assert.AreEqual(mode, binding.Mode);
        Assert.IsNull(binding.Reason);
    }

    [TestMethod]
    public void LackOfControlHasAReasonAndCannotPretendToSelectSilent()
    {
        ReviewedInstallerMode binding = ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled);

        Assert.AreEqual(InstallerModeBinding.NotControllable, binding.Binding);
        Assert.IsNull(binding.Mode);
        Assert.AreEqual(UncontrolledInstallerModeReason.ProviderScriptControlled, binding.Reason);
        Assert.AreNotEqual(binding, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(2)]
    [DataRow(int.MaxValue)]
    public void UnknownModeCannotBecomeAReviewedChoice(int unknown)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReviewedInstallerMode.Selected((InstallerMode)unknown));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void UnknownReasonCannotCreateAnUncontrolledBinding(int unknown)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReviewedInstallerMode.NotControllable((UncontrolledInstallerModeReason)unknown));
    }

    [TestMethod]
    public void SameChoiceHasValueEqualityButDifferentChoicesRemainDistinct()
    {
        Assert.AreEqual(ReviewedInstallerMode.Selected(InstallerMode.Silent), ReviewedInstallerMode.Selected(InstallerMode.Silent));
        Assert.AreNotEqual(ReviewedInstallerMode.Selected(InstallerMode.Silent), ReviewedInstallerMode.Selected(InstallerMode.Interactive));
        Assert.AreEqual(ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled));
    }

    [TestMethod]
    public void CallersCannotConstructAMissingOrContradictoryBinding()
    {
        Assert.IsEmpty(typeof(ReviewedInstallerMode).GetConstructors());
        Assert.IsTrue(typeof(ReviewedInstallerMode).GetProperties().All(property => property.SetMethod is null));
    }
}
