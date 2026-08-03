using Godot;
using NUnit.Framework;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Pins the parallax scroll-scale mapping used by <see cref="Uberkarl.TileMapLevelBuilder"/> when it wraps
/// a scrolling layer in a <see cref="Parallax2D"/> (DiVoid #7528, bug B): X follows the layer's
/// <c>ScrollSpeed</c>, Y is always world-locked at 1.0 regardless of the speed. Before this fix, Y used the
/// same factor as X, so a `scrollSpeed != 1` layer drifted vertically as the camera followed the player —
/// this test only exercises the pure mapping (no engine/scene tree required), matching how the rest of the
/// game-side render logic in <c>TileMapLevelBuilder</c> is otherwise verified only in-engine via Godot MCP.
/// </summary>
[TestFixture]
public sealed class TileMapLevelBuilderTests
{
    [TestCase(1f)]
    [TestCase(0.5f)]
    [TestCase(1.5f)]
    [TestCase(0f)]
    public void ScrollScaleFor_KeepsYWorldLocked_RegardlessOfScrollSpeed(float scrollSpeed)
    {
        Vector2 scale = TileMapLevelBuilder.ScrollScaleFor(scrollSpeed);

        Assert.That(scale.Y, Is.EqualTo(1f), "Y must stay world-locked — parallax in this side-scroller is X-only.");
    }

    [TestCase(1f)]
    [TestCase(0.5f)]
    [TestCase(1.5f)]
    public void ScrollScaleFor_ScalesXByScrollSpeed(float scrollSpeed)
    {
        Vector2 scale = TileMapLevelBuilder.ScrollScaleFor(scrollSpeed);

        Assert.That(scale.X, Is.EqualTo(scrollSpeed));
    }
}
