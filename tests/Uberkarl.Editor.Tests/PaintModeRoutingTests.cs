using System;
using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Pins the paint-mode routing matrix (design #8049 addendum; DiVoid #8505/#8525 §12 — the real risk this
/// milestone names: the matrix grows from three modes to four and is otherwise untested Godot glue). Every
/// case is asserted against a literal <see cref="CellRoutingDecision"/> value, never a value re-derived from
/// <see cref="PaintModeRouting"/> itself (DiVoid #8642 — no expected value from a collaborator on the
/// production path).
/// </summary>
[TestFixture]
public sealed class PaintModeRoutingTests
{
    [TestCase(PaintModeKind.Tile, CellRoutingDecision.PaintTile)]
    [TestCase(PaintModeKind.Terrain, CellRoutingDecision.PaintTerrain)]
    [TestCase(PaintModeKind.Object, CellRoutingDecision.PlaceObject)]
    [TestCase(PaintModeKind.TriggerRect, CellRoutingDecision.TriggerRectPress)]
    public void ResolvePress_EveryMode_RoutesToItsOwnDecision(PaintModeKind mode, CellRoutingDecision expected)
    {
        Assert.That(PaintModeRouting.ResolvePress(mode), Is.EqualTo(expected));
    }

    [TestCase(PaintModeKind.Tile, CellRoutingDecision.EraseCell)]
    [TestCase(PaintModeKind.Terrain, CellRoutingDecision.EraseCell)]
    [TestCase(PaintModeKind.Object, CellRoutingDecision.EraseObject)]
    public void ResolveErase_ModesOtherThanTriggerRect_IgnorePendingCorner_AndRouteToTheirOwnDecision(PaintModeKind mode, CellRoutingDecision expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(PaintModeRouting.ResolveErase(mode, triggerCornerPending: false), Is.EqualTo(expected));
            Assert.That(PaintModeRouting.ResolveErase(mode, triggerCornerPending: true), Is.EqualTo(expected), "the pending flag is meaningless outside TriggerRect mode");
        });
    }

    [Test]
    [Description("The boundary this milestone's routing risk actually lives on: with a corner pending, erase must cancel the placement, not remove a committed trigger.")]
    public void ResolveErase_TriggerRect_WithPendingCorner_CancelsRatherThanErases()
    {
        Assert.That(PaintModeRouting.ResolveErase(PaintModeKind.TriggerRect, triggerCornerPending: true),
            Is.EqualTo(CellRoutingDecision.CancelTriggerCorner));
    }

    [Test]
    public void ResolveErase_TriggerRect_WithNoPendingCorner_ErasesTheTriggerAtTheCell()
    {
        Assert.That(PaintModeRouting.ResolveErase(PaintModeKind.TriggerRect, triggerCornerPending: false),
            Is.EqualTo(CellRoutingDecision.EraseTrigger));
    }

    [Test]
    [Description("An out-of-range PaintModeKind must be rejected, not silently routed as Tile -- the resolver's job is exhaustiveness, not a permissive default.")]
    public void ResolvePress_UnrecognizedMode_ThrowsRatherThanDefaultingToTile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintModeRouting.ResolvePress((PaintModeKind)(-1)));
    }

    [Test]
    [Description("Same exhaustiveness stance on the erase side -- an out-of-range mode must not silently inherit EraseCell.")]
    public void ResolveErase_UnrecognizedMode_ThrowsRatherThanDefaultingToEraseCell()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintModeRouting.ResolveErase((PaintModeKind)(-1), triggerCornerPending: false));
    }
}
