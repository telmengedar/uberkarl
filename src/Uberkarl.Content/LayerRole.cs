namespace Uberkarl.Content;

/// <summary>
/// The role a level layer plays. Collision is honored on the <see cref="Main"/> layer only;
/// <see cref="Background"/> and <see cref="Foreground"/> layers always ignore tile collision,
/// even when a tile is flagged as colliding.
/// </summary>
public enum LayerRole
{
    /// <summary>Drawn behind the play field. Never collides.</summary>
    Background,

    /// <summary>The play field. The only role that honors tile collision.</summary>
    Main,

    /// <summary>Drawn in front of the play field. Never collides.</summary>
    Foreground,
}
