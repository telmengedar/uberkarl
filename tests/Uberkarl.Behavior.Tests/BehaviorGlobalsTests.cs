using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Covers the shared four-facade-global composition used by <c>BehaviorRuntime</c>, the test rig, and <see cref="BehaviorSourceValidator"/>.</summary>
[TestFixture]
public sealed class BehaviorGlobalsTests
{
    [Test]
    public void Compose_ReturnsAllFourGlobalsByName()
    {
        var intents = new IntentBuffer();
        var self = new BehaviorSubject("subject-1", "object", "widget", intents);
        var level = new BehaviorLevel(intents);
        var player = new BehaviorPlayer(intents);
        var currentEvent = new BehaviorEvent();

        var globals = BehaviorGlobals.Compose(self, level, player, currentEvent);

        Assert.Multiple(() =>
        {
            Assert.That(globals, Has.Count.EqualTo(4));
            Assert.That(globals["self"], Is.SameAs(self));
            Assert.That(globals["level"], Is.SameAs(level));
            Assert.That(globals["player"], Is.SameAs(player));
            Assert.That(globals["event"], Is.SameAs(currentEvent));
        });
    }
}
