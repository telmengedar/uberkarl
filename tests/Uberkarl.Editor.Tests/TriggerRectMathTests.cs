using NUnit.Framework;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the two-corner trigger tool's pure rect math (design #8049 M4b): normalizing either corner order
/// into a positive-extent rect, and clamping that rect into a level's grid. Every fixture uses distinct,
/// non-symmetric coordinates so a wrong axis or a swapped corner produces a wrong rect, not a crash.
/// </summary>
[TestFixture]
public sealed class TriggerRectMathTests
{
    [Test]
    public void FromCorners_TopLeftFirst_BottomRightSecond_ProducesTheExpectedRect()
    {
        TriggerRect rect = TriggerRectMath.FromCorners(2, 3, 6, 9, levelWidth: 20, levelHeight: 20);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(2));
            Assert.That(rect.Y, Is.EqualTo(3));
            Assert.That(rect.Width, Is.EqualTo(5));
            Assert.That(rect.Height, Is.EqualTo(7));
        });
    }

    [Test]
    [Description("The two corners are unordered — pressing the bottom-right cell first and the top-left second must produce the identical rect (negative extents corrected).")]
    public void FromCorners_BottomRightFirst_TopLeftSecond_ProducesTheSameRect()
    {
        TriggerRect rect = TriggerRectMath.FromCorners(6, 9, 2, 3, levelWidth: 20, levelHeight: 20);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(2));
            Assert.That(rect.Y, Is.EqualTo(3));
            Assert.That(rect.Width, Is.EqualTo(5));
            Assert.That(rect.Height, Is.EqualTo(7));
        });
    }

    [Test]
    [Description("Mixed corner order (X decreases, Y increases) — pins that each axis is normalized independently, not as a pair.")]
    public void FromCorners_MixedCornerOrder_NormalizesEachAxisIndependently()
    {
        TriggerRect rect = TriggerRectMath.FromCorners(x0: 8, y0: 1, x1: 3, y1: 5, levelWidth: 20, levelHeight: 20);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(3), "min of the two X values");
            Assert.That(rect.Y, Is.EqualTo(1), "min of the two Y values, independent of the X swap");
            Assert.That(rect.Width, Is.EqualTo(6));
            Assert.That(rect.Height, Is.EqualTo(5));
        });
    }

    [Test]
    public void FromCorners_SameCellTwice_ProducesAOneByOneRect()
    {
        TriggerRect rect = TriggerRectMath.FromCorners(4, 4, 4, 4, levelWidth: 20, levelHeight: 20);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(4));
            Assert.That(rect.Y, Is.EqualTo(4));
            Assert.That(rect.Width, Is.EqualTo(1));
            Assert.That(rect.Height, Is.EqualTo(1));
        });
    }

    [Test]
    [Description("A corner exactly on the level's far edge must not be cropped — the rect is already in-bounds and clamping must be a no-op here.")]
    public void FromCorners_SecondCornerOnTheFarEdge_IsNotCropped()
    {
        TriggerRect rect = TriggerRectMath.FromCorners(x0: 7, y0: 7, x1: 9, y1: 9, levelWidth: 10, levelHeight: 10);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(7));
            Assert.That(rect.Y, Is.EqualTo(7));
            Assert.That(rect.Width, Is.EqualTo(3), "7..9 inclusive is 3 cells wide, exactly reaching the last column of a 10-wide level");
            Assert.That(rect.Height, Is.EqualTo(3));
        });
    }

    [Test]
    public void Clamp_OriginBeyondTheGrid_PullsBackIntoBounds()
    {
        TriggerRect rect = TriggerRectMath.Clamp(new TriggerRect(50, 60, 3, 3), levelWidth: 10, levelHeight: 8);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(9), "clamped to the last valid column (width - 1)");
            Assert.That(rect.Y, Is.EqualTo(7), "clamped to the last valid row (height - 1), independently of X");
            Assert.That(rect.Width, Is.EqualTo(1), "no room left past the clamped origin");
            Assert.That(rect.Height, Is.EqualTo(1));
        });
    }

    [Test]
    [Description("Extent clamping is per-axis: a rect whose origin is in bounds but whose width overruns the grid must crop only that axis, leaving the other axis's extent untouched.")]
    public void Clamp_ExtentOverrunsOnOneAxisOnly_CropsOnlyThatAxis()
    {
        TriggerRect rect = TriggerRectMath.Clamp(new TriggerRect(6, 2, 8, 3), levelWidth: 10, levelHeight: 10);

        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(6));
            Assert.That(rect.Y, Is.EqualTo(2));
            Assert.That(rect.Width, Is.EqualTo(4), "6 + 8 = 14 overruns a 10-wide grid; cropped to 10 - 6 = 4");
            Assert.That(rect.Height, Is.EqualTo(3), "height is untouched by the width overrun");
        });
    }

    [Test]
    public void Clamp_AlreadyInBounds_IsUnchanged()
    {
        TriggerRect rect = TriggerRectMath.Clamp(new TriggerRect(1, 2, 3, 4), levelWidth: 20, levelHeight: 20);

        Assert.That(rect, Is.EqualTo(new TriggerRect(1, 2, 3, 4)));
    }
}
