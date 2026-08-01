using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class TileDefinition
{
    public int Id { get; init; }

    public ResourceReference Graphic { get; init; }

    /// <summary>
    /// Whether this tile is solid. Collision is a property of the tile, but it is only
    /// enforced when the tile is placed on a <see cref="LayerRole.Main"/> layer.
    /// </summary>
    public bool Collides { get; init; }
}
