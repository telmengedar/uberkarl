namespace Uberkarl.Behavior;

/// <summary>The starter Pooscript source seeded into a newly-created script, one per <see cref="BehaviorSubjectKind"/>.</summary>
public static class BehaviorScriptTemplates
{
    private const string TileTemplate = """
        $onContact = $other => { self.setState("touched", true); }
        { "onContact": onContact }
        """;

    private const string TriggerTemplate = """
        $onEnter = $who => { self.setState("entered", true); }
        { "onEnter": onEnter }
        """;

    private const string ObjectTemplate = """
        $onUpdate = $delta => { self.setState("ticking", true); }
        { "onUpdate": onUpdate }
        """;

    private const string LevelScriptTemplate = """
        $onLevelStart = [] => { self.setState("started", true); }
        { "onLevelStart": onLevelStart }
        """;

    /// <summary>The starter source text for a brand-new script bound to a subject of <paramref name="kind"/>.</summary>
    public static string For(BehaviorSubjectKind kind) => kind switch
    {
        BehaviorSubjectKind.Tile => TileTemplate,
        BehaviorSubjectKind.Trigger => TriggerTemplate,
        BehaviorSubjectKind.Object => ObjectTemplate,
        BehaviorSubjectKind.LevelScript => LevelScriptTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
