using Uberkarl.Behavior;

namespace Uberkarl.Editor;

/// <summary>Formats a behavior-scriptable subject's kind and optional author-given name into a label — shared by the assignment picker's title and the cursor status line.</summary>
public static class BehaviorSubjectLabel
{
    /// <summary>The longest name <see cref="Format"/> ever shows before ellipsis-truncating it.</summary>
    public const int MaxNameLength = 32;

    /// <summary>The kind alone, or the kind followed by the bounded, quoted name.</summary>
    /// <param name="kind">the subject's kind</param>
    /// <param name="name">the subject's author-given name, or null/empty when it has none; ignored for <see cref="BehaviorSubjectKind.Tile"/>, which is cell-addressed and never named</param>
    /// <returns><c>Kind</c> alone, or <c>Kind 'name'</c> when <paramref name="name"/> is given and applicable</returns>
    public static string Format(BehaviorSubjectKind kind, string? name) =>
        kind != BehaviorSubjectKind.Tile && !string.IsNullOrEmpty(name) ? $"{KindLabel(kind)} '{Bound(name)}'" : KindLabel(kind);

    static string KindLabel(BehaviorSubjectKind kind) => kind switch {
        BehaviorSubjectKind.Tile => "Tile",
        BehaviorSubjectKind.Trigger => "Trigger",
        BehaviorSubjectKind.Object => "Object",
        BehaviorSubjectKind.LevelScript => "Level Script",
        _ => kind.ToString(),
    };

    static string Bound(string name) => name.Length <= MaxNameLength ? name : name[..(MaxNameLength - 1)] + "…";
}
