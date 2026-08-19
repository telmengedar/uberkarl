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

    public static Attempt TryOpen(System.Func<int, bool> canOpen, Reading[] readings, int excludeTapIndex, bool hasSession, MenuSession session)
    {
        (int index, bool wasTap) = Resolve(readings, excludeTapIndex);
        if (index < 0 || !hasSession || !canOpen(index))
            return new Attempt(-1, false);

        MenuSessionTransition opening = session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        if (opening.Effect != MenuSessionEffect.Open)
            return new Attempt(-1, false);

        if (!wasTap)
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
