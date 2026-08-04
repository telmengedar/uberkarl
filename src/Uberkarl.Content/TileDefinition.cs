using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class TileDefinition
{
    public int Id { get; init; }

    /// <summary>
    /// Optional author-facing display name (DiVoid #7551 tileset authoring — named via the on-screen
    /// keyboard). Purely cosmetic: placement and identity are always by <see cref="Id"/>. Omitted from
    /// JSON when unset so pre-authoring content loads unchanged.
    /// </summary>
    public string? Name { get; init; }

    public ResourceReference Graphic { get; init; }

    /// <summary>
    /// Whether this tile is solid. Collision is a property of the tile, but it is only
    /// enforced when the tile is placed on a layer whose <see cref="LayerDefinition.Collision"/> is true.
    /// </summary>
    public bool Collides { get; init; }
}
