namespace Uberkarl.Content;

/// <summary>
/// How a free-moving object's runtime body interacts with the player (DiVoid #7863, design #7704 §9.4):
/// which Godot body type <c>ObjectBodyBuilder</c> constructs for it.
/// </summary>
public enum ObjectCollisionRole
{
    /// <summary>An <c>AnimatableBody2D</c> that blocks and carries the player — a moving platform.</summary>
    Solid,

    /// <summary>An <c>Area2D</c> sensor that detects contact but never blocks — a jump-block/collectible.</summary>
    Passthrough,
}
