namespace Uberkarl.Editor;

/// <summary>What a press into <see cref="TriggerRectTool"/> did: recorded the first corner, or completed a rect from a second.</summary>
public enum TriggerRectResultKind
{
    /// <summary>The press recorded the mode's first corner; a second press is now pending.</summary>
    CornerStarted,

    /// <summary>The press was the second corner; <see cref="TriggerRectResult.Rect"/> is the completed, normalized, bounds-clamped rect.</summary>
    RectReady,
}

/// <summary>The outcome of one press into <see cref="TriggerRectTool"/>.</summary>
public readonly struct TriggerRectResult
{
    private TriggerRectResult(TriggerRectResultKind kind, TriggerRect rect)
    {
        Kind = kind;
        Rect = rect;
    }

    /// <summary>Which of the two-corner mode's presses this was.</summary>
    public TriggerRectResultKind Kind { get; }

    /// <summary>The completed rect. Meaningless when <see cref="Kind"/> is <see cref="TriggerRectResultKind.CornerStarted"/>.</summary>
    public TriggerRect Rect { get; }

    /// <summary>The result of a first-corner press.</summary>
    public static TriggerRectResult CornerStarted() => new(TriggerRectResultKind.CornerStarted, default);

    /// <summary>The result of a second-corner press that completed <paramref name="rect"/>.</summary>
    public static TriggerRectResult ForRect(TriggerRect rect) => new(TriggerRectResultKind.RectReady, rect);
}
