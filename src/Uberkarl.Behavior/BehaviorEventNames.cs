namespace Uberkarl.Behavior;

/// <summary>
/// The canonical handler-variable name a script assigns for each <see cref="BehaviorEventKind"/> (design
/// #7704 §7, D-1 — "a behavior script exposes handlers by assigning named handler lambdas to conventional
/// variables"). E.g. a script implements contact by writing <c>$onContact = $other =&gt; { ... }</c>.
/// </summary>
public static class BehaviorEventNames
{
    private static readonly IReadOnlyDictionary<string, BehaviorEventKind> ByName = new Dictionary<string, BehaviorEventKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["onSpawn"] = BehaviorEventKind.OnSpawn,
        ["onDespawn"] = BehaviorEventKind.OnDespawn,
        ["onContact"] = BehaviorEventKind.OnContact,
        ["onContactLeave"] = BehaviorEventKind.OnContactLeave,
        ["onEnter"] = BehaviorEventKind.OnEnter,
        ["onLeave"] = BehaviorEventKind.OnLeave,
        ["onUpdate"] = BehaviorEventKind.OnUpdate,
        ["onMessage"] = BehaviorEventKind.OnMessage,
        ["onLevelStart"] = BehaviorEventKind.OnLevelStart,
        ["onPlayerDeath"] = BehaviorEventKind.OnPlayerDeath,
        ["onPlayerRespawn"] = BehaviorEventKind.OnPlayerRespawn,
        ["onTimer"] = BehaviorEventKind.OnTimer,
    };

    private static readonly IReadOnlyDictionary<BehaviorEventKind, string> ByKind = new Dictionary<BehaviorEventKind, string>
    {
        [BehaviorEventKind.OnSpawn] = "onSpawn",
        [BehaviorEventKind.OnDespawn] = "onDespawn",
        [BehaviorEventKind.OnContact] = "onContact",
        [BehaviorEventKind.OnContactLeave] = "onContactLeave",
        [BehaviorEventKind.OnEnter] = "onEnter",
        [BehaviorEventKind.OnLeave] = "onLeave",
        [BehaviorEventKind.OnUpdate] = "onUpdate",
        [BehaviorEventKind.OnMessage] = "onMessage",
        [BehaviorEventKind.OnLevelStart] = "onLevelStart",
        [BehaviorEventKind.OnPlayerDeath] = "onPlayerDeath",
        [BehaviorEventKind.OnPlayerRespawn] = "onPlayerRespawn",
        [BehaviorEventKind.OnTimer] = "onTimer",
    };

    public static bool TryParse(string variableName, out BehaviorEventKind kind) => ByName.TryGetValue(variableName, out kind);

    public static string ToVariableName(BehaviorEventKind kind) => ByKind[kind];
}
