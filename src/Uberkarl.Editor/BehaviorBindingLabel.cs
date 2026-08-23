using System.Globalization;
using System.Linq;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Formats a <see cref="BehaviorBinding"/> into an author-facing label, either cell-bounded (<see cref="Format(BehaviorBinding)"/>) or full (<see cref="FormatFull(BehaviorBinding)"/>).</summary>
public static class BehaviorBindingLabel
{
    /// <summary>The longest label <see cref="Format(BehaviorBinding)"/> ever returns, ellipsis included.</summary>
    public const int MaxLength = 24;

    private const string NoBehaviorLabel = "no behavior";

    /// <summary>The bounded label for <paramref name="binding"/> — what a cell-width overlay can show.</summary>
    /// <param name="binding">the binding to label</param>
    /// <returns>the predefined's display label, or the script's slug — ellipsis-truncated to <see cref="MaxLength"/></returns>
    public static string Format(BehaviorBinding binding) => Bound(RawLabel(binding));

    /// <summary>The full label for <paramref name="binding"/>, with every parameter value appended and no length bound — what a status line can show.</summary>
    /// <param name="binding">the binding to label</param>
    /// <returns>the predefined's display label or the script's slug, followed by <c>(name value, ...)</c> when <paramref name="binding"/> carries parameters</returns>
    public static string FormatFull(BehaviorBinding binding) => WithParameters(RawLabel(binding), binding.Parameters);

    /// <summary>The bounded label for a tile behavior override entry.</summary>
    /// <param name="entry">the override entry to label</param>
    /// <returns>the removed-marker text when <see cref="TileBehaviorOverride.Removed"/> is set or no binding is present, else the formatted binding</returns>
    public static string Format(TileBehaviorOverride entry) => Bound(RawLabel(entry));

    /// <summary>The full, unbounded, parameter-including label for a tile behavior override entry.</summary>
    /// <param name="entry">the override entry to label</param>
    /// <returns>the removed-marker text when <see cref="TileBehaviorOverride.Removed"/> is set or no binding is present, else the full formatted binding</returns>
    public static string FormatFull(TileBehaviorOverride entry)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));

        return entry.Removed || entry.Binding is not { } binding ? NoBehaviorLabel : FormatFull(binding);
    }

    private static string RawLabel(BehaviorBinding binding)
    {
        if (binding is null)
            throw new ArgumentNullException(nameof(binding));

        return binding.IsPredefined ? PredefinedLabel(binding.PredefinedId) : ScriptLabel(binding.Script);
    }

    private static string RawLabel(TileBehaviorOverride entry)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));

        return entry.Removed || entry.Binding is not { } binding ? NoBehaviorLabel : RawLabel(binding);
    }

    private static string PredefinedLabel(string? predefinedId)
    {
        if (predefinedId is null)
            return NoBehaviorLabel;

        foreach (PredefinedBehaviorDescriptor descriptor in PredefinedBehaviors.Descriptors)
        {
            if (descriptor.Id == predefinedId)
                return descriptor.Label;
        }

        return predefinedId;
    }

    private static string ScriptLabel(ResourceReference? script) =>
        script is { } value ? ScriptResourcePaths.DisplayLabel(value.Path) : NoBehaviorLabel;

    private static string Bound(string label) =>
        label.Length <= MaxLength ? label : label[..(MaxLength - 1)] + "…";

    private static string WithParameters(string label, IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0)
            return label;

        string joined = string.Join(", ", parameters.Select(pair => $"{pair.Key} {FormatParameterValue(pair.Value)}"));
        return $"{label} ({joined})";
    }

    private static string FormatParameterValue(object? value) =>
        value is double number ? number.ToString("0.##", CultureInfo.InvariantCulture) : value?.ToString() ?? "?";
}
