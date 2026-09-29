using System.Diagnostics;

namespace EasyCon.Tests;

[TestFixture]
public class CustomDelayTests
{
    [Test]
    public void AISleep_DoesNotReturnBeforeRequestedDuration()
    {
        const int durationMs = 50;
        Stopwatch stopwatch = Stopwatch.StartNew();

        global::CustomDelay.AISleep(durationMs);

        Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(durationMs - 1));
    }
}