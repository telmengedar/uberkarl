namespace Uberkarl.Content;

/// <summary>
/// A named set of <see cref="ObjectDefinition"/>s (DiVoid #7863, design #7704 §5.2/§6) — the <c>objectset</c>
/// resource kind, mirroring <see cref="TileSetDefinition"/>'s role for tiles. Owns reusable object TYPES;
/// placements (where an instance sits) live on the level via <see cref="ObjectPlacement"/>.
/// </summary>
public sealed class ObjectSetDefinition
{
    public IReadOnlyList<ObjectDefinition> Objects { get; init; } = Array.Empty<ObjectDefinition>();
}
