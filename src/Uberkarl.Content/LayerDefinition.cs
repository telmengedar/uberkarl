namespace Uberkarl.Content;

public sealed class LayerDefinition
{
    public const int EmptyCell = -1;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether this layer is a collision layer. When <c>false</c> the layer never collides,
    /// even for a tile flagged <see cref="TileDefinition.Collides"/>. Draw order is the layer
    /// array order (back to front) and is independent of this flag. Defaults to <c>false</c>
    /// so a layer is display-only unless it opts in.
    /// </summary>
    public bool Collision { get; init; }

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
