using NUnit.Framework;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="TriggerRectTool"/>'s pure two-corner state machine: the first press starts a pending
/// corner, the second completes and clears it, and <see cref="TriggerRectTool.CancelPending"/> is the
/// interruption path a menu opening / modal summoning / playtest starting / undo must all reach so a pending
/// corner never survives past whatever interrupted it.
/// </summary>
[TestFixture]
public sealed class TriggerRectToolTests
{
    [Test]
    public void Press_FirstPress_StartsAPendingCorner_AndReportsCornerStarted()
    {
        var tool = new TriggerRectTool();

        TriggerRectResult result = tool.Press(3, 4, levelWidth: 10, levelHeight: 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(TriggerRectResultKind.CornerStarted));
            Assert.That(tool.HasPendingCorner, Is.True);
        });
    }

    [Test]
    public void Press_SecondPress_CompletesTheRect_AndClearsThePendingCorner()
    {
        var tool = new TriggerRectTool();
        tool.Press(2, 2, levelWidth: 10, levelHeight: 10);

        TriggerRectResult result = tool.Press(5, 6, levelWidth: 10, levelHeight: 10);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(TriggerRectResultKind.RectReady));
            Assert.That(result.Rect, Is.EqualTo(new TriggerRect(2, 2, 4, 5)));
            Assert.That(tool.HasPendingCorner, Is.False, "the second press must clear the pending corner, or a third press would extend rather than start a new rect");
        });
    }

    [Test]
    [Description("After completing one rect, the tool must be ready to start a fresh, unrelated one — the state machine has exactly two phases, not a growing chain.")]
    public void Press_AfterCompletingARect_TheNextPressStartsAFreshCorner_NotAThirdCornerOfThePrevious()
    {
        var tool = new TriggerRectTool();
        tool.Press(0, 0, levelWidth: 10, levelHeight: 10);
        tool.Press(2, 2, levelWidth: 10, levelHeight: 10);

        TriggerRectResult third = tool.Press(7, 7, levelWidth: 10, levelHeight: 10);
        Assert.That(third.Kind, Is.EqualTo(TriggerRectResultKind.CornerStarted));

        TriggerRectResult fourth = tool.Press(8, 9, levelWidth: 10, levelHeight: 10);
        Assert.That(fourth.Rect, Is.EqualTo(new TriggerRect(7, 7, 2, 3)), "the second rect must be built from (7,7)-(8,9), not carry over the first rect's corner");
    }

    [Test]
    public void CancelPending_WithAPendingCorner_ClearsIt_AndReturnsTrue()
    {
        var tool = new TriggerRectTool();
        tool.Press(1, 1, levelWidth: 10, levelHeight: 10);

        bool cancelled = tool.CancelPending();

        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Is.True);
            Assert.That(tool.HasPendingCorner, Is.False);
        });
    }

    [Test]
    [Description("Cancelling with nothing pending must no-op and say so, so a caller can skip redrawing a preview that was never shown.")]
    public void CancelPending_WithNoPendingCorner_IsANoOp_AndReturnsFalse()
    {
        var tool = new TriggerRectTool();

        Assert.That(tool.CancelPending(), Is.False);
    }

    [Test]
    public void CancelPending_ThenPress_StartsAFreshCorner_NotTheCancelledOne()
    {
        var tool = new TriggerRectTool();
        tool.Press(1, 1, levelWidth: 10, levelHeight: 10);
        tool.CancelPending();

        TriggerRectResult result = tool.Press(5, 5, levelWidth: 10, levelHeight: 10);
        Assert.That(result.Kind, Is.EqualTo(TriggerRectResultKind.CornerStarted), "a press right after a cancel is a NEW first corner");

        TriggerRectResult completed = tool.Press(6, 5, levelWidth: 10, levelHeight: 10);
        Assert.That(completed.Rect, Is.EqualTo(new TriggerRect(5, 5, 2, 1)), "the cancelled (1,1) corner must not resurface in the completed rect");
    }

    [Test]
    public void PreviewRect_WithNoPendingCorner_IsNull()
    {
        var tool = new TriggerRectTool();

        Assert.That(tool.PreviewRect(3, 3, levelWidth: 10, levelHeight: 10), Is.Null);
    }

    [Test]
    public void PreviewRect_WithAPendingCorner_TracksTheGivenCell_WithoutMutatingState()
    {
        var tool = new TriggerRectTool();
        tool.Press(2, 2, levelWidth: 10, levelHeight: 10);

        TriggerRect? first = tool.PreviewRect(4, 2, levelWidth: 10, levelHeight: 10);
        TriggerRect? second = tool.PreviewRect(4, 6, levelWidth: 10, levelHeight: 10);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new TriggerRect(2, 2, 3, 1)));
            Assert.That(second, Is.EqualTo(new TriggerRect(2, 2, 3, 5)), "the preview must re-derive from the SAME first corner on each call, not consume it");
            Assert.That(tool.HasPendingCorner, Is.True, "querying the preview must never itself complete or clear the pending corner");
        });
    }
}
