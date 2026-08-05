using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Covers <see cref="IntentBuffer"/> — the single-thread mutation contract's collection point (design #7704 §5.6/§8.5).</summary>
[TestFixture]
public sealed class IntentBufferTests
{
    [Test]
    public void Record_PreservesIssuanceOrder()
    {
        var buffer = new IntentBuffer();

        buffer.Record(new SetStateIntent("a", "k", 1));
        buffer.Record(new SetStateIntent("b", "k", 2));

        Assert.That(buffer.Intents, Is.EqualTo(new BehaviorIntent[]
        {
            new SetStateIntent("a", "k", 1),
            new SetStateIntent("b", "k", 2),
        }));
    }

    [Test]
    public void Drain_ReturnsRecordedIntents_AndClearsTheBuffer()
    {
        var buffer = new IntentBuffer();
        buffer.Record(new DespawnIntent("x"));

        var drained = buffer.Drain();

        Assert.That(drained, Has.Count.EqualTo(1));
        Assert.That(buffer.Intents, Is.Empty);
    }

    [Test]
    public void Drain_WhenEmpty_ReturnsEmpty()
    {
        var buffer = new IntentBuffer();

        Assert.That(buffer.Drain(), Is.Empty);
    }
}
