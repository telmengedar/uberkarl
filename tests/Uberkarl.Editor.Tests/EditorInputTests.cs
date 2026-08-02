using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the engine-agnostic input core that makes the editor device-neutral: the action↔name mapping,
/// the grid cursor's clamped movement, and the palette/layer cycling arithmetic. None of this touches
/// Godot — it is exactly the logic the abstraction keeps testable outside the engine, so cursor edge
/// behaviour and selection wrapping are pinned down independent of any device.
/// </summary>
[TestFixture]
public sealed class EditorInputTests
{
    // ----- action ↔ name mapping -----

    [Test]
    public void ActionMap_HasAUniqueNameForEveryAction()
    {
        var actions = Enum.GetValues<EditorAction>();
        var names = new HashSet<string>();

        foreach (var action in actions)
        {
            var name = EditorActionMap.NameOf(action);
            Assert.That(name, Is.Not.Null.And.Not.Empty, $"{action} has no input-map name.");
            Assert.That(names.Add(name), Is.True, $"Name '{name}' is bound to more than one action.");
        }

        Assert.That(EditorActionMap.All, Has.Count.EqualTo(actions.Length));
    }

    [Test]
    public void ActionMap_ResolvesNameBackToAction()
    {
        foreach (var action in Enum.GetValues<EditorAction>())
        {
            Assert.That(EditorActionMap.TryResolve(EditorActionMap.NameOf(action), out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(action));
        }
    }

    [Test]
    public void ActionMap_UnknownName_DoesNotResolve()
    {
        Assert.That(EditorActionMap.TryResolve("ui_accept", out _), Is.False);
        Assert.That(EditorActionMap.TryResolve("", out _), Is.False);
    }

    // ----- grid cursor -----

    [Test]
    public void Cursor_StartsAtOrigin_WithinBounds()
    {
        var cursor = new GridCursor(4, 3);
        Assert.Multiple(() =>
        {
            Assert.That(cursor.X, Is.EqualTo(0));
            Assert.That(cursor.Y, Is.EqualTo(0));
            Assert.That(cursor.Width, Is.EqualTo(4));
            Assert.That(cursor.Height, Is.EqualTo(3));
        });
    }

    [Test]
    public void Cursor_Move_ReportsWhetherItChangedCell()
    {
        var cursor = new GridCursor(4, 3);

        Assert.That(cursor.TryMove(1, 0), Is.True);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((1, 0)));

        Assert.That(cursor.TryMove(0, 2), Is.True);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((1, 2)));
    }

    [Test]
    public void Cursor_ClampsAtEdges_AndReportsNoOpMoveAsFalse()
    {
        var cursor = new GridCursor(4, 3);

        // Already at the top-left; moving further up/left is a clamped no-op.
        Assert.That(cursor.TryMove(-1, -1), Is.False);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((0, 0)));

        // Push into the far corner; overshoot is clamped, not wrapped.
        cursor.TryMove(10, 10);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((3, 2)));
        Assert.That(cursor.TryMove(1, 1), Is.False);
    }

    [Test]
    public void Cursor_MoveTo_ClampsAndReportsChange()
    {
        var cursor = new GridCursor(4, 3);

        Assert.That(cursor.MoveTo(2, 1), Is.True);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((2, 1)));

        // Same cell again — no change.
        Assert.That(cursor.MoveTo(2, 1), Is.False);

        // Out of range snaps to the nearest edge cell.
        Assert.That(cursor.MoveTo(99, -5), Is.True);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((3, 0)));
    }

    [Test]
    public void Cursor_Resize_ReclampsIntoTheNewGrid()
    {
        var cursor = new GridCursor(8, 8);
        cursor.MoveTo(7, 6);

        cursor.Resize(4, 3);

        Assert.Multiple(() =>
        {
            Assert.That(cursor.X, Is.EqualTo(3));
            Assert.That(cursor.Y, Is.EqualTo(2));
            Assert.That(cursor.Width, Is.EqualTo(4));
            Assert.That(cursor.Height, Is.EqualTo(3));
        });
    }

    [Test]
    public void Cursor_Resize_RejectsNonPositiveDimensions()
    {
        var cursor = new GridCursor(4, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Resize(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Resize(4, -1));
    }

    // ----- cyclic selection -----

    [Test]
    public void Cycle_NextAndPrev_WrapAtTheEnds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CyclicSelection.Next(0, 3), Is.EqualTo(1));
            Assert.That(CyclicSelection.Next(2, 3), Is.EqualTo(0)); // wrap forward
            Assert.That(CyclicSelection.Prev(0, 3), Is.EqualTo(2)); // wrap back
            Assert.That(CyclicSelection.Prev(2, 3), Is.EqualTo(1));
        });
    }

    [Test]
    public void Cycle_EmptyList_ReturnsNoSelection()
    {
        Assert.That(CyclicSelection.Next(0, 0), Is.EqualTo(-1));
        Assert.That(CyclicSelection.Prev(0, 0), Is.EqualTo(-1));
    }

    [Test]
    public void Cycle_SingleItem_StaysOnIt()
    {
        Assert.That(CyclicSelection.Next(0, 1), Is.EqualTo(0));
        Assert.That(CyclicSelection.Prev(0, 1), Is.EqualTo(0));
    }

    [Test]
    public void Cycle_OutOfRangeCurrent_NormalisesIntoTheList()
    {
        Assert.That(CyclicSelection.Next(-1, 3), Is.EqualTo(0));
        Assert.That(CyclicSelection.Next(5, 3), Is.EqualTo(0)); // 5 → (5+1)%3 = 0
    }
}
