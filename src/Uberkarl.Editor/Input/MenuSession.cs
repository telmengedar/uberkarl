namespace Uberkarl.Editor.Input;

/// <summary>Where a pop-in menu sits in its lifecycle.</summary>
public enum MenuSessionState
{
    /// <summary>No menu is open.</summary>
    Closed,

    /// <summary>A menu is open and its opening trigger is still held.</summary>
    Transient,

    /// <summary>A menu is open and no longer tied to any trigger.</summary>
    Latched,
}

/// <summary>What a <see cref="MenuSession"/> step asks the caller to do.</summary>
public enum MenuSessionEffect
{
    /// <summary>Nothing changed this step.</summary>
    None,

    /// <summary>Pop the menu in.</summary>
    Open,

    /// <summary>Leave the menu open and stop watching its opening trigger.</summary>
    Latch,

    /// <summary>Resolve the current highlight — commit if a wedge is highlighted, cancel otherwise — and close the menu.</summary>
    Commit,
}

/// <summary>The state and effect returned by one <see cref="MenuSession.Step"/> call.</summary>
public readonly struct MenuSessionTransition
{
    public MenuSessionTransition(MenuSessionState state, MenuSessionEffect effect)
    {
        State = state;
        Effect = effect;
    }

    /// <summary>The session's state after this step.</summary>
    public MenuSessionState State { get; }

    /// <summary>What the caller should do as a result of this step.</summary>
    public MenuSessionEffect Effect { get; }
}

/// <summary>The engine-free pop-in menu lifecycle: Closed, Transient (opening trigger still held), or Latched (trigger released as a tap).</summary>
public sealed class MenuSession
{
    /// <summary>The session's current state.</summary>
    public MenuSessionState State { get; private set; } = MenuSessionState.Closed;

    /// <summary>Advances the session by one frame and returns the resulting transition.</summary>
    public MenuSessionTransition Step(bool openRequested, bool triggerReleased, bool releasedAsTap)
    {
        switch (State)
        {
            case MenuSessionState.Closed:
                return openRequested
                    ? Transition(MenuSessionState.Transient, MenuSessionEffect.Open)
                    : Transition(MenuSessionState.Closed, MenuSessionEffect.None);

            case MenuSessionState.Transient:
                if (!triggerReleased)
                    return Transition(MenuSessionState.Transient, MenuSessionEffect.None);
                return releasedAsTap
                    ? Transition(MenuSessionState.Latched, MenuSessionEffect.Latch)
                    : Transition(MenuSessionState.Closed, MenuSessionEffect.Commit);

            default:
                return Transition(MenuSessionState.Latched, MenuSessionEffect.None);
        }
    }

    /// <summary>Forces the session back to <see cref="MenuSessionState.Closed"/>.</summary>
    public void Reset() => State = MenuSessionState.Closed;

    private MenuSessionTransition Transition(MenuSessionState state, MenuSessionEffect effect)
    {
        State = state;
        return new MenuSessionTransition(state, effect);
    }
}
