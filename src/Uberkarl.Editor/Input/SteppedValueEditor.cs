namespace Uberkarl.Editor.Input;

/// <summary>
/// Engine-agnostic enter/adjust/commit/cancel state machine for a value that is stepped through a
/// discrete ladder inside a summoned panel — the layer panel's Scroll-speed stepper is the first user
/// (Toni's playtest fix, DiVoid #7512); the shape is meant to be reused by any future value-adjusting
/// control in a summoned panel (the package browser, #7470, is the noted next adopter — not wired here,
/// per task scope).
///
/// The whole point is to free left/right for spatial focus navigation while the control is merely
/// focused: a directional press only ever adjusts the value while <see cref="IsEditing"/> is true, and
/// entering/leaving edit mode is an explicit two-press gesture (confirm enters, confirm again commits,
/// cancel reverts) so a spatial-nav pass through the control never mutates anything by accident.
///
/// The state machine never touches the model itself — <see cref="Enter"/> copies the caller's current
/// committed value into a local <see cref="PendingValue"/>, <see cref="Adjust"/> steps only that local
/// copy via the injected step function (e.g. <see cref="ScrollSpeedLadder.Step"/>), and the caller
/// applies the value to the model only after <see cref="TryCommit"/> returns <c>true</c>.
/// <see cref="Cancel"/> simply discards the pending value — because the model was never touched while
/// editing, "revert" needs no model-side undo.
/// </summary>
public sealed class SteppedValueEditor<T>
{
    private readonly Func<T, int, T> step;

    public SteppedValueEditor(Func<T, int, T> step)
    {
        this.step = step ?? throw new ArgumentNullException(nameof(step));
    }

    /// <summary>True while a value is being edited (between a successful <see cref="Enter"/> and its matching <see cref="TryCommit"/>/<see cref="Cancel"/>).</summary>
    public bool IsEditing { get; private set; }

    /// <summary>The value as adjusted so far this edit. Meaningless while <see cref="IsEditing"/> is <c>false</c>.</summary>
    public T PendingValue { get; private set; } = default!;

    /// <summary>
    /// Starts editing from <paramref name="currentValue"/>. No-op (returns <c>false</c>, state
    /// unchanged) when already editing — entry does not restart a half-finished edit.
    /// </summary>
    public bool Enter(T currentValue)
    {
        if (IsEditing)
            return false;

        IsEditing = true;
        PendingValue = currentValue;
        return true;
    }

    /// <summary>Steps <see cref="PendingValue"/> via the injected step function. No-op while not editing.</summary>
    public bool Adjust(int direction)
    {
        if (!IsEditing)
            return false;

        PendingValue = step(PendingValue, direction);
        return true;
    }

    /// <summary>
    /// Ends editing and hands back the final <paramref name="value"/> for the caller to apply to the
    /// model. Returns <c>false</c> (and <paramref name="value"/> must be ignored) when not currently
    /// editing.
    /// </summary>
    public bool TryCommit(out T value)
    {
        value = PendingValue;
        if (!IsEditing)
            return false;

        IsEditing = false;
        return true;
    }

    /// <summary>
    /// Ends editing and discards the pending value — the model, never touched during the edit, needs no
    /// revert of its own. No-op while not editing.
    /// </summary>
    public bool Cancel()
    {
        if (!IsEditing)
            return false;

        IsEditing = false;
        return true;
    }
}
