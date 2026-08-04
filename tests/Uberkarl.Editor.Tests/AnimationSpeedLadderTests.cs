using NUnit.Framework;
using Uberkarl.Editor;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Pins <see cref="AnimationSpeedLadder"/>'s step/snap arithmetic (DiVoid #7551 Phase 2, design #7580) —
/// the preset ladder <c>TileSetEditor</c>'s "◄ Slower"/"Faster ►" buttons step a tile's animation speed
/// through. Mirrors <see cref="ScrollSpeedLadder"/>'s own test coverage shape (same clamp-at-the-ends /
/// nearest-preset-snap behaviour, just over the animation-speed preset list).
/// </summary>
[TestFixture]
public sealed class AnimationSpeedLadderTests
{
    [Test]
    public void Step_FromAPreset_MovesToTheAdjacentPreset()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AnimationSpeedLadder.Step(5.0, +1), Is.EqualTo(8.0));
            Assert.That(AnimationSpeedLadder.Step(5.0, -1), Is.EqualTo(3.0));
        });
    }

    [Test]
    public void Step_AtTheTopPreset_ClampsAndDoesNotOverflow()
    {
        var top = AnimationSpeedLadder.Presets[^1];
        Assert.That(AnimationSpeedLadder.Step(top, +1), Is.EqualTo(top));
    }

    [Test]
    public void Step_AtTheBottomPreset_ClampsAndDoesNotUnderflow()
    {
        var bottom = AnimationSpeedLadder.Presets[0];
        Assert.That(AnimationSpeedLadder.Step(bottom, -1), Is.EqualTo(bottom));
    }

    [Test]
    public void Step_FromAnOffLadderValue_ProceedsFromTheNearestPreset()
    {
        // 6.0 is nearest to the 5.0 preset; stepping up from there lands on 8.0, not some interpolation.
        Assert.That(AnimationSpeedLadder.Step(6.0, +1), Is.EqualTo(8.0));
    }

    [Test]
    public void Snap_ReturnsTheNearestPreset()
    {
        Assert.That(AnimationSpeedLadder.Snap(6.0), Is.EqualTo(5.0));
    }

    [Test]
    public void Presets_AreStrictlyAscending()
    {
        for (var i = 1; i < AnimationSpeedLadder.Presets.Count; i++)
            Assert.That(AnimationSpeedLadder.Presets[i], Is.GreaterThan(AnimationSpeedLadder.Presets[i - 1]));
    }
}
