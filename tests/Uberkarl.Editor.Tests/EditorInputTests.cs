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

    // ----- cursor input gate (regression: grid cursor frozen while a menu/panel captures direction) -----

    [Test]
    public void Gate_AllowsMovement_OnlyWhenFocusedAndNothingCapturingDirection()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CursorInputGate.AllowsCursorMovement(surfaceHasFocus: true, directionalCaptured: false),
                Is.True, "focused canvas with no menu/panel up should move.");
            Assert.That(CursorInputGate.AllowsCursorMovement(surfaceHasFocus: false, directionalCaptured: false),
                Is.False, "an unfocused canvas never moves.");
            // The invariant: a menu/panel capturing direction freezes the cursor even if focus bounced back
            // onto the canvas (the real-gamepad bug — ui_* aim bounced focus off the radial onto the canvas).
            Assert.That(CursorInputGate.AllowsCursorMovement(surfaceHasFocus: true, directionalCaptured: true),
                Is.False, "a captured direction (radial/panel open) freezes the cursor despite focus.");
            Assert.That(CursorInputGate.AllowsCursorMovement(surfaceHasFocus: false, directionalCaptured: true),
                Is.False);
        });
    }

    [Test]
    public void GridCursor_DoesNotMove_WhileAMenuCapturesDirectionalInput()
    {
        var cursor = new GridCursor(10, 10);
        cursor.MoveTo(5, 5);

        // A radial (or focused panel) is open: directional input is captured. A move request must be gated
        // out entirely — the cursor underneath stays put no matter how many directions are pushed.
        const bool menuOpen = true;
        for (var i = 0; i < 5; i++)
        {
            if (CursorInputGate.AllowsCursorMovement(surfaceHasFocus: true, directionalCaptured: menuOpen))
                cursor.TryMove(1, 0);
        }
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((5, 5)), "cursor moved while a menu was open.");

        // Menu closed, canvas focused again: the same request now steps the cursor.
        if (CursorInputGate.AllowsCursorMovement(surfaceHasFocus: true, directionalCaptured: false))
            cursor.TryMove(1, 0);
        Assert.That((cursor.X, cursor.Y), Is.EqualTo((6, 5)), "cursor should move once the menu closes.");
    }

    // ----- direction ownership (round 2: a revealed toolbar/panel zone owns direction, not just a radial) -----

    [Test]
    public void DirectionCaptured_WhenARadialIsOpenOrANonCanvasZoneIsActive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CursorInputGate.DirectionCaptured(radialOpen: false, nonCanvasZoneActive: false),
                Is.False, "plain canvas: the grid cursor owns direction.");
            Assert.That(CursorInputGate.DirectionCaptured(radialOpen: true, nonCanvasZoneActive: false),
                Is.True, "an open radial owns direction.");
            Assert.That(CursorInputGate.DirectionCaptured(radialOpen: false, nonCanvasZoneActive: true),
                Is.True, "a revealed toolbar/panel focus-zone owns direction (the round-2 case).");
        });
    }

    [Test]
    public void PanelZoneActive_DirectionalInput_KeepsCursorFrozen_EvenIfFocusBouncesToCanvas()
    {
        // Round-2 regression. On a real gamepad one physical D-pad/stick press fires BOTH the editor cursor
        // action AND Godot's ui_* focus navigation. With a toolbar/panel focus-zone active, the ui_* half can
        // bounce focus back onto the full-rect canvas (surfaceHasFocus becomes true). The gate must still
        // refuse the cursor move, because a non-canvas zone owns direction — the panel stays the input target
        // and the grid cursor underneath does not creep. (Focus containment itself is pinned by the toolbar/
        // panel neighbour wiring and verified in-harness; this locks the cursor-freeze half of the seam.)
        var cursor = new GridCursor(10, 10);
        cursor.MoveTo(5, 5);

        const bool nonCanvasZoneActive = true; // B revealed the toolbar/panel
        var captured = CursorInputGate.DirectionCaptured(radialOpen: false, nonCanvasZoneActive);

        // Feed both the ui_ bounce (focus momentarily on the canvas) and the cursor signal, repeatedly.
        for (var i = 0; i < 5; i++)
        {
            if (CursorInputGate.AllowsCursorMovement(surfaceHasFocus: true, directionalCaptured: captured))
                cursor.TryMove(1, 0);
        }

        Assert.That((cursor.X, cursor.Y), Is.EqualTo((5, 5)),
            "grid cursor moved while a toolbar/panel zone owned directional input.");
    }

    // ----- primary-action gate (part 1: editor_paint inert off-canvas so confirm activates a focused Control) -----

    [Test]
    public void PrimaryAction_AllowedOnlyWhenCanvasFocusedAndNothingCapturingInput()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: true, directionalCaptured: false),
                Is.True, "focused canvas with no radial/zone up: paint acts at the cursor.");
            Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: false, directionalCaptured: false),
                Is.False, "an unfocused canvas never paints.");
            // The activation invariant: while a radial or a non-canvas focus-zone owns input, the confirm
            // button (gamepad A = editor_paint) must NOT paint — it belongs to the focused Control's ui_accept.
            Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: true, directionalCaptured: true),
                Is.False, "a captured direction (radial open / toolbar zone active) makes editor_paint inert.");
            Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: false, directionalCaptured: true),
                Is.False);
        });
    }

    [Test]
    public void PrimaryAction_Suppressed_WhileToolbarZoneActive_SoConfirmReachesFocusedControl()
    {
        // The classic-Control activation seam. A real gamepad's A fires editor_paint; while the toolbar focus-
        // zone is active the canvas must leave that confirm untouched (not AcceptEvent) so Godot's ui_accept
        // reaches the focused Button. This locks the "editor_paint inert off-canvas" half; the ui_accept pad
        // binding (the other half) is an InputMap change verified in-harness.
        const bool toolbarZoneActive = true;
        var captured = CursorInputGate.DirectionCaptured(radialOpen: false, nonCanvasZoneActive: toolbarZoneActive);

        // Even if the confirm event momentarily finds the full-rect canvas focused, the gate refuses the paint.
        Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: true, directionalCaptured: captured),
            Is.False, "editor_paint fired on the canvas while the toolbar zone owned the confirm button.");

        // Back on the plain canvas, the same confirm paints again.
        var free = CursorInputGate.DirectionCaptured(radialOpen: false, nonCanvasZoneActive: false);
        Assert.That(CursorInputGate.AllowsPrimaryAction(surfaceHasFocus: true, directionalCaptured: free),
            Is.True, "editor_paint should act once focus returns to the canvas zone.");
    }

    // ----- stepped value edit-mode state machine (layer panel Scroll stepper, DiVoid #7512) -----
    //
    // Toni's playtest fix: a directional press on a merely-focused control must stay free for spatial
    // nav, so adjusting a value is gated behind an explicit enter/adjust/commit-or-cancel gesture. This is
    // the engine-agnostic half of that fix (the Godot ScrollStepper control drives it from ui_accept/
    // ui_left/ui_right/ui_cancel) — pinned down without Godot so the enter/adjust/commit/cancel semantics
    // are independent of any device or control implementation.

    [Test]
    public void SteppedValueEditor_Enter_StartsEditingFromTheCurrentValue()
    {
        var editor = new SteppedValueEditor<float>((v, d) => v + d);

        var entered = editor.Enter(1.0f);

        Assert.Multiple(() =>
        {
            Assert.That(entered, Is.True);
            Assert.That(editor.IsEditing, Is.True);
            Assert.That(editor.PendingValue, Is.EqualTo(1.0f));
        });
    }

    [Test]
    public void SteppedValueEditor_Enter_WhileAlreadyEditing_IsNoOp()
    {
        var editor = new SteppedValueEditor<float>((v, d) => v + d);
        editor.Enter(1.0f);
        editor.Adjust(+1); // pending now 2.0

        var reentered = editor.Enter(99f);

        Assert.Multiple(() =>
        {
            Assert.That(reentered, Is.False, "a second Enter while editing must not restart the edit.");
            Assert.That(editor.PendingValue, Is.EqualTo(2.0f), "the in-progress pending value must survive.");
        });
    }

    [Test]
    public void SteppedValueEditor_Adjust_WhileEditing_StepsThePendingValueOnly()
    {
        var editor = new SteppedValueEditor<float>(ScrollSpeedLadder.Step);
        editor.Enter(1.0f);

        var adjusted = editor.Adjust(-1);

        Assert.Multiple(() =>
        {
            Assert.That(adjusted, Is.True);
            Assert.That(editor.PendingValue, Is.EqualTo(0.75f), "steps via the injected ladder function.");
            Assert.That(editor.IsEditing, Is.True, "adjusting does not end the edit.");
        });
    }

    [Test]
    public void SteppedValueEditor_Adjust_WhileNotEditing_IsNoOp()
    {
        var editor = new SteppedValueEditor<float>((v, d) => v + d);

        var adjusted = editor.Adjust(+1);

        Assert.Multiple(() =>
        {
            Assert.That(adjusted, Is.False);
            Assert.That(editor.PendingValue, Is.EqualTo(0f), "never entered, so there is nothing to adjust.");
        });
    }

    [Test]
    public void SteppedValueEditor_TryCommit_WhileEditing_ReturnsFinalValueAndEndsTheEdit()
    {
        var editor = new SteppedValueEditor<float>(ScrollSpeedLadder.Step);
        editor.Enter(1.0f);
        editor.Adjust(-1); // 0.75
        editor.Adjust(-1); // 0.5

        var committed = editor.TryCommit(out var value);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.True);
            Assert.That(value, Is.EqualTo(0.5f));
            Assert.That(editor.IsEditing, Is.False);
        });
    }

    [Test]
    public void SteppedValueEditor_TryCommit_WhileNotEditing_ReturnsFalse()
    {
        var editor = new SteppedValueEditor<float>((v, d) => v + d);

        var committed = editor.TryCommit(out _);

        Assert.That(committed, Is.False, "nothing to commit — the caller must not apply the out value.");
    }

    [Test]
    public void SteppedValueEditor_Cancel_WhileEditing_DiscardsThePendingValue_NoModelTouchNeeded()
    {
        var editor = new SteppedValueEditor<float>(ScrollSpeedLadder.Step);
        editor.Enter(1.0f);
        editor.Adjust(-1); // 0.75 — never committed, so the model was never touched.

        var cancelled = editor.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Is.True);
            Assert.That(editor.IsEditing, Is.False);
        });

        // A fresh Enter starts clean from whatever the (untouched) model still holds — proving Cancel left
        // no residue from the discarded 0.75 pending value.
        editor.Enter(1.0f);
        Assert.That(editor.PendingValue, Is.EqualTo(1.0f));
    }

    [Test]
    public void SteppedValueEditor_Cancel_WhileNotEditing_IsNoOp()
    {
        var editor = new SteppedValueEditor<float>((v, d) => v + d);

        Assert.That(editor.Cancel(), Is.False);
    }

    // ----- analog-stick edge-trigger gate (DiVoid #7576: discrete stepping + no jump-on-open) -----
    //
    // Toni's resize-playtest bug: opening the Resize panel with the stick still deflected from aiming the
    // radial instantly stepped the width to an arbitrary value, and holding the stick stepped every frame
    // (fast jump) instead of the D-pad's one-press-one-step feel. This pins the engine-agnostic gate the
    // Godot ScrollStepper/DimensionStepper controls drive from InputEventJoypadMotion.

    [Test]
    public void AnalogStepGate_FreshDeflection_StepsOnce_ThenHoldingProducesNoFurtherSteps()
    {
        var gate = new AnalogStepGate();

        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(-1),
            "first deflection is a fresh edge — one discrete step.");
        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(0),
            "still deflected, no new edge — holding must not repeat every frame.");
        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(0));
    }

    [Test]
    public void AnalogStepGate_ReturnToNeutral_ThenRedeflect_StepsAgain()
    {
        var gate = new AnalogStepGate();
        gate.Poll(negativePressed: true, positivePressed: false); // consume the first edge

        Assert.That(gate.Poll(negativePressed: false, positivePressed: false), Is.EqualTo(0), "neutral: no step.");
        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(-1),
            "released back to neutral and re-deflected: a fresh edge, one more step.");
    }

    [Test]
    public void AnalogStepGate_PositiveDirection_StepsIndependentlyOfNegative()
    {
        var gate = new AnalogStepGate();

        Assert.That(gate.Poll(negativePressed: false, positivePressed: true), Is.EqualTo(+1));
        Assert.That(gate.Poll(negativePressed: false, positivePressed: true), Is.EqualTo(0), "held: no repeat.");
    }

    [Test]
    public void AnalogStepGate_Prime_SeedsBaseline_SoAnAlreadyDeflectedStickDoesNotStepImmediately()
    {
        // The exact bug: the Resize panel opens (or the Scroll stepper enters edit mode) while the stick is
        // still pushed left from aiming the radial. Priming with that already-deflected state must NOT be
        // treated as a fresh edge.
        var gate = new AnalogStepGate();

        gate.Prime(negativePressed: true, positivePressed: false);

        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(0),
            "primed as already-deflected: the next poll at the same deflection must not step.");

        // Only after the stick is released back to neutral and pushed again does a step fire.
        Assert.That(gate.Poll(negativePressed: false, positivePressed: false), Is.EqualTo(0));
        Assert.That(gate.Poll(negativePressed: true, positivePressed: false), Is.EqualTo(-1));
    }

    [Test]
    public void AnalogStepGate_Prime_WithNeutralStick_LeavesTheNextDeflectionAsAFreshEdge()
    {
        var gate = new AnalogStepGate();

        gate.Prime(negativePressed: false, positivePressed: false);

        Assert.That(gate.Poll(negativePressed: false, positivePressed: true), Is.EqualTo(+1),
            "priming from neutral does not suppress a genuinely fresh deflection.");
    }

    // ----- editor viewport clamp (DiVoid #7576: fixed-zoom scrolling instead of fit-to-level) -----
    //
    // Mirrors PlayRuntimeBuilder.AttachCamera's Camera2D Limit* clamp (LimitLeft/Top=0,
    // LimitRight/Bottom=size) for EditorCanvas's own hand-rolled transform, since a Control-hosted render
    // has no Camera2D of its own to attach.

    [Test]
    public void ViewportClamp_LevelSmallerThanPanel_Centers()
    {
        // A 5-tile-wide level (80px) at 3x zoom (240px scaled) inside an 800px panel: too small to scroll,
        // so it centers exactly like Camera2D does when its limits sit inside the viewport.
        float offset = EditorViewportClamp.Offset(targetWorldCenter: 40f, panelExtent: 800f, levelExtentWorld: 80f, scale: 3f);

        Assert.That(offset, Is.EqualTo((800f - 240f) / 2f).Within(0.001f));
    }

    [Test]
    public void ViewportClamp_TargetNearLeftEdge_ClampsToShowTheLevelStart_NotPastIt()
    {
        // A 100-tile-wide level (1600px) at 3x zoom (4800px scaled) in an 800px panel: cursor near the left
        // edge (world x=8) would centre past world 0 — clamp to 0 so world 0 sits at the panel's left edge.
        float offset = EditorViewportClamp.Offset(targetWorldCenter: 8f, panelExtent: 800f, levelExtentWorld: 1600f, scale: 3f);

        Assert.That(offset, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ViewportClamp_TargetNearRightEdge_ClampsToShowTheLevelEnd_NotPastIt()
    {
        float offset = EditorViewportClamp.Offset(targetWorldCenter: 1592f, panelExtent: 800f, levelExtentWorld: 1600f, scale: 3f);

        // panelExtent - levelExtentScaled = 800 - 4800 = -4000: the rightmost clamp (world 1600 lands
        // exactly at the panel's right edge).
        Assert.That(offset, Is.EqualTo(-4000f).Within(0.001f));
    }

    [Test]
    public void ViewportClamp_TargetWellInsideBounds_CentersExactlyOnTarget()
    {
        float offset = EditorViewportClamp.Offset(targetWorldCenter: 800f, panelExtent: 800f, levelExtentWorld: 1600f, scale: 3f);

        // Unclamped centring: panelExtent/2 - target*scale = 400 - 2400 = -2000, well within the clamp range.
        Assert.That(offset, Is.EqualTo(-2000f).Within(0.001f));
    }
}
