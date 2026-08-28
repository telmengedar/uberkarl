namespace Uberkarl.Behavior;

/// <summary>The facade spelling for each <see cref="BehaviorSubjectKind"/> (<see cref="BehaviorSubject.Kind"/>), modelled on <see cref="BehaviorEventNames"/>.</summary>
public static class BehaviorSubjectKinds
{
    private static readonly IReadOnlyDictionary<BehaviorSubjectKind, string> ByKind = new Dictionary<BehaviorSubjectKind, string>
    {
        [BehaviorSubjectKind.Tile] = "tile",
        [BehaviorSubjectKind.Trigger] = "trigger",
        [BehaviorSubjectKind.Object] = "object",
        [BehaviorSubjectKind.LevelScript] = "level",
    };

    private static readonly IReadOnlyDictionary<string, BehaviorSubjectKind> ByName = new Dictionary<string, BehaviorSubjectKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["tile"] = BehaviorSubjectKind.Tile,
        ["trigger"] = BehaviorSubjectKind.Trigger,
        ["object"] = BehaviorSubjectKind.Object,
        ["level"] = BehaviorSubjectKind.LevelScript,
    };

    public static string NameOf(BehaviorSubjectKind kind) => ByKind[kind];

    public static BehaviorSubjectKind Parse(string name) => ByName[name];
}
