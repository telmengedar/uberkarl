namespace Uberkarl.Editor;

/// <summary>
/// The editor's authoring view of one logical terrain (DiVoid #7551 Phase 3, design #7580) — the
/// authoring-side counterpart to <see cref="Content.TerrainDefinition"/>. Belongs to exactly one
/// <see cref="EditableTerrainSet"/>.
/// </summary>
public sealed class EditableTerrain
{
    public EditableTerrain(int id, string name, string? color = null, int? defaultTile = null)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Color = color;
        DefaultTile = defaultTile;
    }

    /// <summary>Stable id, unique across the whole tile set (mirrors <see cref="EditableTile.Id"/>'s never-reused stability).</summary>
    public int Id { get; }

    public string Name { get; }

    /// <summary>Author-facing colour (hex string), or <c>null</c> when unset.</summary>
    public string? Color { get; }

    /// <summary>
    /// This terrain's author-designated fallback <see cref="EditableTile.Id"/> (DiVoid #7638), or
    /// <c>null</c> when unset — see <see cref="Content.TerrainDefinition.DefaultTile"/> for the "why".
    /// Always a tile that is currently a member of THIS terrain — <see cref="EditableTileSet"/> keeps this
    /// invariant (clears it if the referenced tile is removed or reassigned elsewhere), the same
    /// self-consistency guarantee it already gives a tile's own <see cref="EditableTile.Terrain"/>.
    /// </summary>
    public int? DefaultTile { get; }
}

/// <summary>
/// The editor's authoring view of one terrain set (DiVoid #7551 Phase 3, design #7580) — the
/// authoring-side counterpart to <see cref="Content.TerrainSetDefinition"/>. Belongs to one
/// <see cref="EditableTileSet"/>; owns its <see cref="Terrains"/>.
/// </summary>
public sealed class EditableTerrainSet
{
    public EditableTerrainSet(int id, string name, Content.TerrainMatchMode matchingMode, IReadOnlyList<EditableTerrain> terrains)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MatchingMode = matchingMode;
        Terrains = terrains ?? throw new ArgumentNullException(nameof(terrains));
    }

    /// <summary>Stable id, unique within the tile set's terrain sets. Never reused.</summary>
    public int Id { get; }

    public string Name { get; }

    public Content.TerrainMatchMode MatchingMode { get; }

    public IReadOnlyList<EditableTerrain> Terrains { get; }
}
