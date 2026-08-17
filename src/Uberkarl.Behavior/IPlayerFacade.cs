namespace Uberkarl.Behavior;

/// <summary>
/// The facade bound as <c>player</c> (design #7704 §8.1, "player" row). Player health/death model is not
/// owned by this core (design #7704 §15 Q-4, open) — <see cref="Hurt"/>/<see cref="Heal"/> only record
/// intents; interpreting them into a health value and death/respawn transitions is a host/glue concern.
/// </summary>
public interface IPlayerFacade
{
    /// <summary>Current continuous world position.</summary>
    BehaviorVector2 Position { get; }

    /// <summary>Current velocity.</summary>
    BehaviorVector2 Velocity { get; }

    /// <summary>Whether the player is currently standing on solid ground.</summary>
    bool IsOnGround { get; }

    /// <summary>Reads a value from the player's state map, or null if unset.</summary>
    object? GetState(string key);

    /// <summary>Records an intent damaging the player.</summary>
    void Hurt(double amount);

    /// <summary>Records an intent healing the player.</summary>
    void Heal(double amount);
}
