using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Pins <see cref="BehaviorRect2"/>'s derived edges and centers against literals.</summary>
[TestFixture]
public sealed class BehaviorRect2Tests
{
    static readonly BehaviorRect2 Rect = new(10, 20, 30, 40);

    [Test]
    public void Left_IsX()
    {
        Assert.That(Rect.Left, Is.EqualTo(10));
    }

    [Test]
    public void Right_IsXPlusWidth()
    {
        Assert.That(Rect.Right, Is.EqualTo(40));
    }

    [Test]
    public void Top_IsY()
    {
        Assert.That(Rect.Top, Is.EqualTo(20));
    }

    [Test]
    public void Bottom_IsYPlusHeight()
    {
        Assert.That(Rect.Bottom, Is.EqualTo(60));
    }

    [Test]
    public void CenterX_IsXPlusHalfWidth()
    {
        Assert.That(Rect.CenterX, Is.EqualTo(25));
    }

    [Test]
    public void CenterY_IsYPlusHalfHeight()
    {
        Assert.That(Rect.CenterY, Is.EqualTo(40));
    }
}
