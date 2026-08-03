namespace Uberkarl.Editor.Input;

/// <summary>
/// Edge-triggers analog-stick stepping so it matches a D-pad button's "one press, one step" feel instead
/// of firing every frame the stick stays deflected. Godot delivers a fresh <c>InputEventJoypadMotion</c> —
/// and therefore a fresh action-pressed reading — on essentially every frame the stick sits off-centre,
/// unlike a joypad BUTTON (the D-pad), which Godot never echoes; so a raw "is this action pressed" check
/// on a stepper's left/right input fires once per D-pad press but continuously for the stick (Toni's
/// resize-playtest bug, DiVoid #7576). This gate tracks each direction's own deflected/neutral state
/// across successive polls and reports a step only on the neutral→deflected transition, refusing another
/// step in the same direction until it sees neutral again.
///
/// <see cref="Prime"/> is the other half of the fix: it seeds the gate's state from the stick's position
/// at the moment a stepper starts listening (gains focus, or enters an edit mode) WITHOUT firing a step —
/// so a stick that is already deflected right then (e.g. still held over from aiming a radial menu) is
/// recorded as the baseline, not a fresh edge. The very next <see cref="Poll"/> call for that direction
/// therefore reports no step until the stick is released back to neutral and pushed again — this is what
/// stops opening a panel with the stick deflected from causing an instant, arbitrary jump.
///
/// Pure and engine-agnostic: the caller decides what "deflected" means (typically the engine's current
/// <c>IsActionPressed</c> reading for the bound ui_left/ui_right action) and only routes analog-stick
/// motion through this gate — a real D-pad press/keyboard key still adjusts immediately, unaffected,
/// exactly as before.
/// </summary>
public sealed class AnalogStepGate
{
    bool negativeDeflected;
    bool positiveDeflected;

    /// <summary>
    /// Feeds the current deflection of each direction and returns the step to apply: <c>-1</c> or
    /// <c>+1</c> on a fresh neutral→deflected transition for that direction, otherwise <c>0</c> (already
    /// deflected since the last poll, or currently neutral).
    /// </summary>
    public int Poll(bool negativePressed, bool positivePressed)
    {
        int step = 0;

        if (negativePressed)
        {
            if (!negativeDeflected)
                step = -1;
            negativeDeflected = true;
        }
        else
        {
            negativeDeflected = false;
        }

        if (positivePressed)
        {
            if (!positiveDeflected)
                step = +1;
            positiveDeflected = true;
        }
        else
        {
            positiveDeflected = false;
        }

        return step;
    }

    /// <summary>
    /// Seeds the gate's deflected/neutral state from the stick's CURRENT position without firing a step —
    /// call this the moment a stepper starts listening (focus gained, edit mode entered) so an
    /// already-deflected stick becomes the new baseline instead of registering as an edge.
    /// </summary>
    public void Prime(bool negativePressed, bool positivePressed)
    {
        negativeDeflected = negativePressed;
        positiveDeflected = positivePressed;
    }
}
