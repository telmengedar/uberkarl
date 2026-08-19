namespace Uberkarl.Editor.Input;

/// <summary>
/// A tiny press-vs-hold edge detector for one trigger. Fed the trigger's pressed state each frame, it
/// distinguishes a quick <em>tap</em> (pressed then released before the hold threshold) from a sustained
/// <em>hold</em> (still down past the threshold) — the discriminator the pop-in paradigm needs so the
/// same button can carry a fast action on tap and open a radial menu on hold, and so the mouse's
/// right-button can erase on tap yet reveal the context wheel on hold. It holds no engine type; the
/// controller reads the device state and feeds it in, which keeps the timing logic trivial to reason
/// about.
/// </summary>
public sealed class HoldWatch
{
    readonly float holdThreshold;
    bool wasPressed;

    public HoldWatch(float holdThreshold)
    {
        this.holdThreshold = holdThreshold;
    }

    /// <summary>True while the trigger is held down.</summary>
    public bool Pressed { get; private set; }

    /// <summary>Seconds the trigger has been held during the current press.</summary>
    public float HeldTime { get; private set; }

    /// <summary>True on the single frame the trigger was released.</summary>
    public bool JustReleased { get; private set; }

    /// <summary>True on the frame a held trigger first crosses the hold threshold (open-the-menu edge).</summary>
    public bool JustCrossedHold { get; private set; }

    /// <summary>True on release only when the press never reached the hold threshold (it was a tap).</summary>
    public bool ReleasedAsTap { get; private set; }

    /// <summary>Advance the detector with this frame's pressed state and elapsed time.</summary>
    public void Update(bool pressed, float delta)
    {
        JustReleased = false;
        JustCrossedHold = false;
        ReleasedAsTap = false;

        if (pressed && !wasPressed)
        {
            HeldTime = 0f;
        }
        else if (pressed)
        {
            float previous = HeldTime;
            HeldTime += delta;
            if (previous < holdThreshold && HeldTime >= holdThreshold)
                JustCrossedHold = true;
        }
        else if (!pressed && wasPressed)
        {
            JustReleased = true;
            ReleasedAsTap = HeldTime < holdThreshold;
        }

        Pressed = pressed;
        wasPressed = pressed;
    }
}
