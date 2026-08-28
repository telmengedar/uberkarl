using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Proves <see cref="BehaviorSubjectKinds"/> round-trips all four subject kinds in both directions.</summary>
[TestFixture]
public sealed class BehaviorSubjectKindsTests
{
    [TestCase(BehaviorSubjectKind.Tile, "tile")]
    [TestCase(BehaviorSubjectKind.Trigger, "trigger")]
    [TestCase(BehaviorSubjectKind.Object, "object")]
    [TestCase(BehaviorSubjectKind.LevelScript, "level")]
    public void NameOf_ThenParse_RoundTrips(BehaviorSubjectKind kind, string expectedName)
    {
        string name = BehaviorSubjectKinds.NameOf(kind);

        Assert.That(name, Is.EqualTo(expectedName));
        Assert.That(BehaviorSubjectKinds.Parse(name), Is.EqualTo(kind));
    }

    [Test]
    public void Parse_UnrecognisedName_Throws()
    {
        Assert.That(() => BehaviorSubjectKinds.Parse("not-a-kind"), Throws.InstanceOf<KeyNotFoundException>());
    }
}
