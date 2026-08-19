namespace Uberkarl.Editor.Input;

/// <summary>Resolves which trigger opens a pop-in menu this frame, and steps a <see cref="MenuSession"/> to open it.</summary>
public static class MenuTriggerArbitration
{
    public readonly struct Reading
    {
        public Reading(bool justCrossedHold, bool releasedAsTap)
        {
            JustCrossedHold = justCrossedHold;
            ReleasedAsTap = releasedAsTap;
        }

        public bool JustCrossedHold { get; }

        public bool ReleasedAsTap { get; }
    }

    public readonly struct Attempt
    {
        public Attempt(int triggerIndex, bool latchedImmediately)
        {
            TriggerIndex = triggerIndex;
            LatchedImmediately = latchedImmediately;
        }

        public int TriggerIndex { get; }

        public bool LatchedImmediately { get; }

        public bool Opened => TriggerIndex >= 0;
    }

    /// <param name="canOpen">Whether the trigger at a given index has anything to open (e.g. a non-empty palette).</param>
    /// <param name="readings">This frame's press-vs-hold reading for every trigger, index-aligned with <paramref name="canOpen"/> and <paramref name="autoLatch"/>.</param>
    /// <param name="excludeTapIndex">A trigger index whose tap is a different gesture entirely (e.g. the mouse's tap-to-erase) and must never open a menu.</param>
    /// <param name="autoLatch">
    /// Whether the trigger at a given index always opens straight into <see cref="MenuSessionState.Latched"/>,
    /// even on a genuine hold — true for a trigger whose target menu has no aim to track (a list surface),
    /// false for one whose target menu needs continued Transient aim-tracking (a radial).
    /// </param>
    public static Attempt TryOpen(System.Func<int, bool> canOpen, Reading[] readings, int excludeTapIndex, System.Func<int, bool> autoLatch, bool hasSession, MenuSession session)
    {
        (int index, bool wasTap) = Resolve(readings, excludeTapIndex);
        if (index < 0 || !hasSession || !canOpen(index))
            return new Attempt(-1, false);

        MenuSessionTransition opening = session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        if (opening.Effect != MenuSessionEffect.Open)
            return new Attempt(-1, false);

        if (!wasTap && !autoLatch(index))
            return new Attempt(index, false);

        MenuSessionTransition latching = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);
        return new Attempt(index, latching.Effect == MenuSessionEffect.Latch);
    }

    private static (int Index, bool WasTap) Resolve(Reading[] readings, int excludeTapIndex)
    {
        for (int i = 0; i < readings.Length; i++)
            if (readings[i].JustCrossedHold)
                return (i, false);

        for (int i = 0; i < readings.Length; i++)
            if (i != excludeTapIndex && readings[i].ReleasedAsTap)
                return (i, true);

        return (-1, false);
    }
}
