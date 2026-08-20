using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

[TestFixture]
public sealed class ContactDirectionTests
{
    static readonly BehaviorRect2 Self = new(0, 0, 16, 16);

    [Test]
    public void Classify_OtherBelow_ReturnsBelow()
    {
        BehaviorRect2 other = new(-6, 15, 16, 16);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("below"));
    }

    [Test]
    public void Classify_OtherAbove_ReturnsAbove()
    {
        BehaviorRect2 other = new(10, -15, 16, 16);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("above"));
    }

    [Test]
    public void Classify_OtherLeft_ReturnsLeft()
    {
        BehaviorRect2 other = new(-15, 4, 16, 16);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("left"));
    }

    [Test]
    public void Classify_OtherRight_ReturnsRight()
    {
        BehaviorRect2 other = new(15, -6, 16, 16);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    [Description("DiVoid #8047")]
    public void Classify_TallRectGrazingSide_ReturnsRight_NotBelow()
    {
        BehaviorRect2 other = new(15, -12, 12, 28);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    public void Classify_EqualPenetrationOnBothAxes_ResolvesToHorizontalAxis()
    {
        BehaviorRect2 other = new(12, 12, 16, 16);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    [Description("DiVoid #8741 CF-1")]
    public void Classify_CornerContactAtRealBlockPlayerProportions_ReturnsRight_NotBelow()
    {
        BehaviorRect2 other = new(11, 9, 14, 26);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    [Description("DiVoid #8741 CF-1")]
    public void Classify_NonSquareOffsetSelf_CornerContact_ReturnsRight_NotBelow()
    {
        BehaviorRect2 playerSelf = new(100, 200, 14, 26);
        BehaviorRect2 other = new(109, 219, 16, 16);

        string direction = ContactDirection.Classify(playerSelf, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    [Description("DiVoid #8741 CF-1")]
    public void Classify_OtherFullyContainsSelf_EqualPenetrationOnBothAxes_ResolvesToHorizontalAxis()
    {
        BehaviorRect2 other = new(-3, -2, 30, 20);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }

    [Test]
    [Description("DiVoid #8741 CF-1")]
    public void Classify_OtherCenterExactlyOnSelfCenterX_ResolvesToRight()
    {
        BehaviorRect2 other = new(7, -5, 2, 20);

        string direction = ContactDirection.Classify(Self, other);

        Assert.That(direction, Is.EqualTo("right"));
    }
}
