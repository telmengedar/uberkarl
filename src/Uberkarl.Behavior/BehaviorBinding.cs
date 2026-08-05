namespace Uberkarl.Behavior;

using Uberkarl.Packages;

/// <summary>
/// The small shared value all four scriptable subjects (tile, object, area trigger, level) use to declare
/// their behavior (design #7704 §5.2/§6): either a reference to a <c>script</c>-kind Pooscript resource, or
/// a <c>{ predefinedId, params }</c> pair addressing the engine's predefined behavior library. Exactly one
/// of <see cref="Script"/> / <see cref="PredefinedId"/> is set — enforced by construction, never both, never
/// neither. Resolving a predefined id to actual Pooscript source is a Phase-1 concern (the predefined
/// library itself, design #7704 Phase 1 seed); this P0 core only carries the binding data.
/// </summary>
public sealed class BehaviorBinding
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyParameters = new Dictionary<string, object?>();

    private BehaviorBinding(ResourceReference? script, string? predefinedId, IReadOnlyDictionary<string, object?> parameters)
    {
        Script = script;
        PredefinedId = predefinedId;
        Parameters = parameters;
    }

    /// <summary>The bound <c>script</c>-kind resource, when this binding points at author-written Pooscript text. Null when <see cref="IsPredefined"/>.</summary>
    public ResourceReference? Script { get; }

    /// <summary>The stable id of a predefined behavior template, when this binding points at the engine's built-in library. Null when <see cref="IsScript"/>.</summary>
    public string? PredefinedId { get; }

    /// <summary>Parameter values for a predefined binding (design #7704 §10.2 — filled via gamepad pickers). Always empty for a script binding.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public bool IsScript => Script.HasValue;

    public bool IsPredefined => PredefinedId is not null;

    public static BehaviorBinding FromScript(ResourceReference script) => new(script, null, EmptyParameters);

    public static BehaviorBinding FromPredefined(string predefinedId, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(predefinedId))
            throw new ArgumentException("Predefined id must not be empty.", nameof(predefinedId));

        return new BehaviorBinding(null, predefinedId, parameters ?? EmptyParameters);
    }
}
