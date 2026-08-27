namespace Uberkarl.Behavior;

/// <summary>The four facade globals a <see cref="BehaviorLoader"/> compile binds.</summary>
public static class BehaviorGlobals
{
    /// <summary>Composes <paramref name="self"/>/<paramref name="level"/>/<paramref name="player"/>/<paramref name="currentEvent"/> into the dictionary <see cref="BehaviorLoader.Compile"/> takes.</summary>
    public static IReadOnlyDictionary<string, object> Compose(BehaviorSubject self, BehaviorLevel level, BehaviorPlayer player, BehaviorEvent currentEvent) => new Dictionary<string, object>
    {
        ["self"] = self,
        ["level"] = level,
        ["player"] = player,
        ["event"] = currentEvent,
    };
}
