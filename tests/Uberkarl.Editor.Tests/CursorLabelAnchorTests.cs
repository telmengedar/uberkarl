using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="CursorLabelAnchor"/>: the above/below placement and the viewport clamp.</summary>
[TestFixture]
public sealed class CursorLabelAnchorTests
{
    [Test]
    public void Resolve_WellInsideTheViewport_PlacesTheLabelAboveTheCell_CentredOnIt()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 100, cellY: 100, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(76));
            Assert.That(y, Is.EqualTo(74));
        });
    }

    [Test]
    [Description("No room above the cell (near the viewport's top edge) falls back to below it.")]
    public void Resolve_NoRoomAbove_FallsBackToBelowTheCell()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 100, cellY: 2, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(76));
            Assert.That(y, Is.EqualTo(40));
        });
    }

    [Test]
    public void Resolve_PastTheLeftEdge_PinsXToTheMargin()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: -100, cellY: 300, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(4));
            Assert.That(y, Is.EqualTo(274));
        });
    }

    [Test]
    public void Resolve_PastTheRightEdge_PinsXToTheFarMargin()
    {
        var (x, _) = CursorLabelAnchor.Resolve(
            cellX: 1000, cellY: 300, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.That(x, Is.EqualTo(716));
    }

    [Test]
    [Description("A cell far down the level pushes the above-placed label past the viewport's bottom edge; the clamp pulls it back in.")]
    public void Resolve_PastTheBottomEdge_PinsYToTheFarMargin()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 400, cellY: 5000, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(376));
            Assert.That(y, Is.EqualTo(576));
        });
    }

    [Test]
    public void Resolve_PastBothEdgesAtACorner_ClampsBothAxesIndependently()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: -100, cellY: 5000, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(4));
            Assert.That(y, Is.EqualTo(576));
        });
    }

    [Test]
    public void Resolve_ExactlyAtTheMargin_IsLeftUnchanged()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 28, cellY: 30, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(4));
            Assert.That(y, Is.EqualTo(4));
        });
    }

    [Test]
    public void Resolve_ViewportNotAtTheOrigin_ClampsRelativeToItsOwnRect()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 0, cellY: 0, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 200, viewportY: 150, viewportWidth: 800, viewportHeight: 600);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(204));
            Assert.That(y, Is.EqualTo(154));
        });
    }

    [Test]
    [Description("The label is wider than the viewport can hold with margin on both sides: the axis collapses to centred rather than clamping to a range that does not exist.")]
    public void Resolve_ViewportNarrowerThanTheLabelPlusMargins_CollapsesXToItsCentre()
    {
        var (x, _) = CursorLabelAnchor.Resolve(
            cellX: 999, cellY: 300, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 20, viewportHeight: 600);

        Assert.That(x, Is.EqualTo(-30));
    }

    [Test]
    public void Resolve_ViewportNarrowerThanTheLabelPlusMargins_AtANonZeroOrigin_CollapsesXRelativeToItsOwnRect()
    {
        var (x, _) = CursorLabelAnchor.Resolve(
            cellX: 999, cellY: 300, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 200, viewportY: 0, viewportWidth: 20, viewportHeight: 600);

        Assert.That(x, Is.EqualTo(170));
    }

    [Test]
    public void Resolve_ViewportShorterThanTheLabelPlusMargins_CollapsesYToItsCentre()
    {
        var (x, y) = CursorLabelAnchor.Resolve(
            cellX: 400, cellY: 999, cellWidth: 32, cellHeight: 32,
            labelWidth: 80, labelHeight: 20,
            viewportX: 0, viewportY: 0, viewportWidth: 800, viewportHeight: 20);

        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(376));
            Assert.That(y, Is.EqualTo(0));
        });
    }
}
