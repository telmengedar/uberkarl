using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="MenuAnchor"/>: the pointer-vs-cursor placement decision and the viewport
/// clamp that keeps a pop-in menu's disc on screen.</summary>
[TestFixture]
public sealed class MenuAnchorTests
{
    [Test]
    public void Resolve_PointerDrivesCursor_ReturnsThePointerPosition()
    {
        var (x, y) = MenuAnchor.Resolve(pointerDrivesCursor: true, pointerX: 10, pointerY: 20, cursorCenterX: 999, cursorCenterY: 999);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(10));
            Assert.That(y, Is.EqualTo(20));
        });
    }

    [Test]
    public void Resolve_CursorDrivesTheMenu_ReturnsTheCursorCentre()
    {
        var (x, y) = MenuAnchor.Resolve(pointerDrivesCursor: false, pointerX: 999, pointerY: 999, cursorCenterX: 10, cursorCenterY: 20);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(10));
            Assert.That(y, Is.EqualTo(20));
        });
    }

    [Test]
    public void Clamp_WellInsideTheViewport_LeavesTheCentreUntouched()
    {
        var (x, y) = MenuAnchor.Clamp(x: 400, y: 300, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(400));
            Assert.That(y, Is.EqualTo(300));
        });
    }

    [Test]
    public void Clamp_PastTheLeftEdge_PinsXToTheMargin_LeavesYAlone()
    {
        var (x, y) = MenuAnchor.Clamp(x: -50, y: 300, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(100));
            Assert.That(y, Is.EqualTo(300));
        });
    }

    [Test]
    public void Clamp_PastTheRightEdge_PinsXToTheFarMargin()
    {
        var (x, _) = MenuAnchor.Clamp(x: 5000, y: 300, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.That(x, Is.EqualTo(700));
    }

    [Test]
    public void Clamp_PastTheTopEdge_PinsYToTheMargin_LeavesXAlone()
    {
        var (x, y) = MenuAnchor.Clamp(x: 400, y: -50, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(400));
            Assert.That(y, Is.EqualTo(100));
        });
    }

    [Test]
    public void Clamp_PastTheBottomEdge_PinsYToTheFarMargin()
    {
        var (_, y) = MenuAnchor.Clamp(x: 400, y: 5000, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.That(y, Is.EqualTo(500));
    }

    [Test]
    public void Clamp_PastBothEdgesAtACorner_ClampsBothAxesIndependently()
    {
        var (x, y) = MenuAnchor.Clamp(x: -50, y: 5000, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(100));
            Assert.That(y, Is.EqualTo(500));
        });
    }

    [Test]
    public void Clamp_ExactlyAtTheMargin_IsLeftUnchanged()
    {
        var (x, y) = MenuAnchor.Clamp(x: 100, y: 500, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(100));
            Assert.That(y, Is.EqualTo(500));
        });
    }

    [Test]
    public void Clamp_ViewportNotAtTheOrigin_ClampsRelativeToItsOwnRect()
    {
        var (x, y) = MenuAnchor.Clamp(x: 0, y: 0, viewportX: 200, viewportY: 150, viewportWidth: 800, viewportHeight: 600, margin: 100);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(300));
            Assert.That(y, Is.EqualTo(250));
        });
    }

    [Test]
    public void Clamp_ViewportNarrowerThanTwiceTheMargin_CollapsesXToItsCentre()
    {
        var (x, _) = MenuAnchor.Clamp(x: -999, y: 300, viewportX: 0, viewportY: 0, viewportWidth: 150, viewportHeight: 600, margin: 100);

        Assert.That(x, Is.EqualTo(75));
    }

    [Test]
    [Description("QA #8688 W-2")]
    public void Clamp_ViewportNarrowerThanTwiceTheMargin_AtANonZeroOrigin_CollapsesXRelativeToItsOwnRect()
    {
        var (x, _) = MenuAnchor.Clamp(x: -999, y: 300, viewportX: 200, viewportY: 0, viewportWidth: 150, viewportHeight: 600, margin: 100);

        Assert.That(x, Is.EqualTo(275));
    }

    [Test]
    public void Clamp_ViewportShorterThanTwiceTheMargin_CollapsesYToItsCentre()
    {
        var (_, y) = MenuAnchor.Clamp(x: 400, y: 999, viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 150, margin: 100);

        Assert.That(y, Is.EqualTo(75));
    }
}
