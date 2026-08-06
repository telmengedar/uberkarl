namespace Uberkarl.Behavior;

/// <summary>
/// The resolved counterpart of <see cref="BehaviorBinding"/> (design #7704 §5.2/C-2 — the content pipeline's
/// Definition → Resolved → Builder shape applied to behavior bindings). A script binding's
/// <see cref="ResourceReference"/> has been resolved down to its Pooscript source text (payload bytes
/// inlined, exactly like every other resolved content reference); a predefined binding carries its id and
/// parameters through unchanged, since resolving a predefined id into source text is a runtime concern
/// (<see cref="PredefinedBehaviors"/>) — a predefined script can be updated engine-side without touching
/// content. Exactly one of <see cref="Script"/> / <see cref="PredefinedId"/> is set, mirroring
/// <see cref="BehaviorBinding"/>. Produced by <see cref="BehaviorBindingResolver"/>.
/// </summary>
public sealed class ResolvedBehaviorBinding
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyParameters = new Dictionary<string, object?>();

    private ResolvedBehaviorBinding(string? script, string? predefinedId, IReadOnlyDictionary<string, object?> parameters)
    {
        Script = script;
        PredefinedId = predefinedId;
        Parameters = parameters;
    }

    /// <summary>The resolved Pooscript source text, when this binding pointed at a <c>script</c>-kind resource. Null when <see cref="IsPredefined"/>.</summary>
    public string? Script { get; }

    /// <summary>The stable predefined behavior id, when this binding pointed at the engine's built-in library. Null when <see cref="IsScript"/>.</summary>
    public string? PredefinedId { get; }

    /// <summary>Parameter values for a predefined binding. Always empty for a script binding.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public bool IsScript => Script is not null;

    public bool IsPredefined => PredefinedId is not null;

    public static ResolvedBehaviorBinding FromScript(string script)
    {
        if (script is null)
            throw new ArgumentNullException(nameof(script));
        return new ResolvedBehaviorBinding(script, null, EmptyParameters);
    }

    public static ResolvedBehaviorBinding FromPredefined(string predefinedId, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(predefinedId))
            throw new ArgumentException("Predefined id must not be empty.", nameof(predefinedId));
        return new ResolvedBehaviorBinding(null, predefinedId, parameters ?? EmptyParameters);
    }
}
