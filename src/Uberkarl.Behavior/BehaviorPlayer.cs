namespace Uberkarl.Behavior;

/// <summary>
/// Godot-free, intent-recording implementation of <see cref="IPlayerFacade"/> — the reference facade bound
/// as <c>player</c> (see <see cref="BehaviorSubject"/> for the "one glue impl + one test double" rationale).
/// <see cref="Position"/>/<see cref="Velocity"/>/<see cref="IsOnGround"/>/<see cref="State"/> are host-seeded
/// directly each frame; actions only record intents (design #7704 §15 Q-4 — health/death model is a
/// host/glue concern, not owned here).
/// </summary>
public sealed class BehaviorPlayer : IPlayerFacade
{
    private readonly IntentBuffer intents;

    public BehaviorPlayer(IntentBuffer intents) => this.intents = intents;

    public BehaviorVector2 Position { get; set; }

    public BehaviorVector2 Velocity { get; set; }

    public bool IsOnGround { get; set; }

    /// <summary>Host-seeded: player state map.</summary>
    public Dictionary<string, object?> State { get; } = new();

    public object? GetState(string key) => State.TryGetValue(key, out var value) ? value : null;

    public void Hurt(double amount) => intents.Record(new HurtIntent(BehaviorSubjectIds.Player, amount));

    public void Heal(double amount) => intents.Record(new HealIntent(BehaviorSubjectIds.Player, amount));
}
