using Avalonia;
using Avalonia.Platform;
using EasyCon2.Avalonia.QQ;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class QQGuideTests
{
    [Test]
    public void RegistrationNavigationReachesConnectionAndCanReturnToEarlierSteps()
    {
        QQGuideViewModel model = new();
        Assert.That(model.PreviousStepCommand.CanExecute(null), Is.False);
        foreach (QQGuideStep step in model.Steps)
        {
            Assert.That(model.CurrentStep, Is.SameAs(step));
            model.NextStepCommand.Execute(null);
        }
        Assert.Multiple(() =>
        {
            Assert.That(model.SelectedSectionIndex, Is.EqualTo(1));
            Assert.That(model.CurrentStep, Is.SameAs(model.Steps[^1]));
        });
        model.SelectedSectionIndex = 0;
        while (model.PreviousStepCommand.CanExecute(null))
            model.PreviousStepCommand.Execute(null);
        Assert.That(model.CurrentStep, Is.SameAs(model.Steps[0]));
    }

    [Test]
    public void DirectStepSelectionAndSectionShortcutsKeepRegistrationPosition()
    {
        QQGuideViewModel model = new();
        List<string?> changed = [];
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        model.SelectedStepIndex = 9;
        Assert.Multiple(() =>
        {
            Assert.That(model.CurrentStep.Number, Is.EqualTo(10));
            Assert.That(model.StepProgress, Does.Contain("10 / 12"));
            Assert.That(changed, Does.Contain(nameof(model.CurrentStep)));
            Assert.That(model.PreviousStepCommand.CanExecute(null), Is.True);
        });
        model.ShowConnectionCommand.Execute(null);
        Assert.That(model.SelectedSectionIndex, Is.EqualTo(1));
        model.ShowScriptCommand.Execute(null);
        Assert.That(model.SelectedSectionIndex, Is.EqualTo(2));
        model.SelectedSectionIndex = 0;
        Assert.That(model.CurrentStep.Number, Is.EqualTo(10));
    }

    [Test]
    public void EveryRegistrationStepHasPackagedImageAndVisibleClickMarkers()
    {
        StandardAssetLoader loader = new(typeof(QQGuideCatalog).Assembly);
        foreach (QQGuideStep step in QQGuideCatalog.Steps)
        {
            Assert.That(loader.Exists(step.ImageUri), Is.True, step.ImageName);
            using Stream image = loader.Open(step.ImageUri);
            Assert.That(image.Length, Is.GreaterThan(0), step.ImageName);
            Rect imageBounds = new(0, 0, step.PixelWidth, step.PixelHeight);
            Assert.That(imageBounds.Contains(step.Crop), Is.True, step.Title);
            Assert.That(step.Focus, Is.Not.Empty, step.Title);
            foreach (Rect marker in step.Focus)
                Assert.That(step.Crop.Contains(marker), Is.True, step.Title);
        }
    }
}