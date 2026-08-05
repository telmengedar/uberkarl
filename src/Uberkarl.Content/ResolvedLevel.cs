using Uberkarl.Behavior;

namespace Uberkarl.Content;

public sealed class ResolvedLevel
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// The parsed solid background fill rendered behind every layer, or <c>null</c> when the level
    /// declares none (the viewport clear colour shows through). Always covers the viewport regardless
    /// of camera position and does not scroll with the world.
    /// </summary>
    public RgbaColor? BackgroundColor { get; init; }

    public IReadOnlyList<ResolvedLayer> Layers { get; init; } = Array.Empty<ResolvedLayer>();

    public IReadOnlyDictionary<int, byte[]> TileGraphics { get; init; } = new Dictionary<int, byte[]>();

    /// <summary>Tile ids whose <see cref="TileCollisionShapes"/> entry is not <see cref="CollisionShapeKind.None"/>. Only enforced on layers whose <see cref="ResolvedLayer.Collision"/> is true.</summary>
    public IReadOnlySet<int> CollidingTileIds { get; init; } = new HashSet<int>();

    /// <summary>
    /// Every declared tile id's resolved collision footprint (DiVoid #7551 Phase 4, design #7580) — the
    /// authority <c>TileSetBuilder</c> reads to build each tile's physics-layer collision polygon (full
    /// square, rect, explicit polygon, or a named preset's polygon). <see cref="CollidingTileIds"/> is a
    /// derived convenience over this same data (every id whose shape's <see cref="CollisionShapeDefinition.Kind"/>
    /// is not <see cref="CollisionShapeKind.None"/>).
    /// </summary>
    public IReadOnlyDictionary<int, CollisionShapeDefinition> TileCollisionShapes { get; init; } = new Dictionary<int, CollisionShapeDefinition>();

    /// <summary>
    /// Resolved animation data (ordered frame bytes + speed) keyed by tile id, for exactly the tiles
    /// <see cref="TileDefinition.IsAnimated"/> flags (DiVoid #7551 Phase 2, design #7580). A tile id absent
    /// from this dictionary is a simple tile — its one frame is already in <see cref="TileGraphics"/>.
    /// <c>TileSetBuilder</c> (Godot-side) is the only consumer: it maps an entry here to a
    /// <c>TileSetAtlasSource</c> with N animation frames + speed, native Godot playback.
    /// </summary>
    public IReadOnlyDictionary<int, ResolvedAnimation> TileAnimations { get; init; } = new Dictionary<int, ResolvedAnimation>();

    /// <summary>
    /// The bound tile set's declared terrain sets/terrains (DiVoid #7551 Phase 3, design #7580). Empty when
    /// the tile set declares no terrains. <c>TileSetBuilder</c> is the only consumer: it maps this, in
    /// declaration order, onto Godot's index-based Terrain Sets/Terrains.
    /// </summary>
    public IReadOnlyList<ResolvedTerrainSet> TerrainSets { get; init; } = Array.Empty<ResolvedTerrainSet>();

    /// <summary>
    /// Which tile ids are terrain variants and their peering bits, keyed by tile id (DiVoid #7551 Phase 3,
    /// design #7580). A tile id absent from this dictionary is not a terrain variant.
    /// </summary>
    public IReadOnlyDictionary<int, ResolvedTileTerrain> TileTerrains { get; init; } = new Dictionary<int, ResolvedTileTerrain>();

    /// <summary>Named spawn cells (tile units) keyed by spawn name. Empty when the level declares none.</summary>
    public IReadOnlyDictionary<string, GridPosition> Spawns { get; init; }
        = new Dictionary<string, GridPosition>();

    /// <summary>Name of the default spawn; null when the level declares no spawns.</summary>
    public string? DefaultSpawn { get; init; }

    /// <summary>The default spawn cell, or null when the level declares no spawns.</summary>
    public GridPosition? DefaultSpawnPosition
        => DefaultSpawn is { } name && Spawns.TryGetValue(name, out var cell) ? cell : null;

    /// <summary>
    /// Resolves a spawn by name. The entry path for future level transitions that enter a level
    /// at a specific named spawn rather than its default.
    /// </summary>
    public bool TryGetSpawn(string name, out GridPosition position) => Spawns.TryGetValue(name, out position);

    /// <summary>
    /// The bound tile set's default contact/contact-leave behavior per tile id (DiVoid #7738, design #7704
    /// §6 "type-level defaults... live on the tileset"). A tile id absent from this dictionary declares no
    /// default behavior. <see cref="EffectiveTileBehaviors"/> is the usual way to consume this together with
    /// <see cref="TileBehaviorOverrides"/> — most callers should not need to read this directly.
    /// </summary>
    public IReadOnlyDictionary<int, ResolvedBehaviorBinding> TileBehaviors { get; init; } = new Dictionary<int, ResolvedBehaviorBinding>();

    /// <summary>
    /// The level's sparse per-instance override/removal map (DiVoid #7738, design #7704 §6), keyed by
    /// (layer index, cell). A present entry with a non-null value REPLACES the tile type's default binding
    /// for that one instance; a present entry with a null value explicitly REMOVES the default (the instance
    /// has no behavior even though its type declares one); an absent key means "use the type default, if
    /// any" — see <see cref="EffectiveTileBehaviors"/>.
    /// </summary>
    public IReadOnlyDictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?> TileBehaviorOverrides { get; init; }
        = new Dictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?>();

    /// <summary>Grid-rect area triggers (DiVoid #7738, design #7704 §6). Empty when the level declares none.</summary>
    public IReadOnlyList<ResolvedAreaTrigger> Triggers { get; init; } = Array.Empty<ResolvedAreaTrigger>();

    /// <summary>The level's global behavior binding (DiVoid #7738, design #7704 §6), or null when the level declares none.</summary>
    public ResolvedBehaviorBinding? LevelScript { get; init; }

    /// <summary>
    /// Yields every scripted tile CELL in the level — the tileset default combined with any per-cell
    /// override/removal (DiVoid #7738, design #7704 §9.3 — "only cells whose resolved binding is non-empty
    /// are tracked"). This is the ONLY correct way to enumerate scripted tile cells; reading
    /// <see cref="TileBehaviors"/> or <see cref="TileBehaviorOverrides"/> alone is not enough, because an
    /// override can both ADD a behavior to a plain tile and REMOVE a type's default from one instance. Pure
    /// and Godot-free so the effective-binding computation is unit-testable without any runtime wiring.
    /// </summary>
    public IEnumerable<(int Layer, GridPosition Cell, ResolvedBehaviorBinding Binding)> EffectiveTileBehaviors()
    {
        for (var layerIndex = 0; layerIndex < Layers.Count; layerIndex++)
        {
            var layer = Layers[layerIndex];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var cell = new GridPosition(x, y);
                    var tileId = layer.Cells[y * Width + x];

                    if (TileBehaviorOverrides.TryGetValue((layerIndex, cell), out var overrideBinding))
                    {
                        if (overrideBinding is { } replacement)
                            yield return (layerIndex, cell, replacement);
                        continue; // present-but-null = explicit removal; either way the override is final.
                    }

                    if (tileId != LayerDefinition.EmptyCell && TileBehaviors.TryGetValue(tileId, out var defaultBinding))
                        yield return (layerIndex, cell, defaultBinding);
                }
            }
        }
    }
}

/// <summary>
/// A resolved grid-rect area trigger (DiVoid #7738, design #7704 §6) — the runtime counterpart of
/// <see cref="AreaTriggerDefinition"/>.
/// </summary>
public sealed class ResolvedAreaTrigger
{
    public string Name { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public required ResolvedBehaviorBinding Binding { get; init; }
}

public sealed class ResolvedLayer
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this layer collides. A non-collision layer never blocks the player.</summary>
    public bool Collision { get; init; }

    /// <summary>
    /// Parallax scroll factor relative to the camera (<c>1.0</c> = world-locked, <c>&lt;1.0</c> =
    /// slower background, <c>&gt;1.0</c> = faster foreground). Always <c>1.0</c> for a
    /// <see cref="Collision"/> layer (enforced by the loader).
    /// </summary>
    public float ScrollSpeed { get; init; } = 1.0f;

    /// <summary>
    /// Whether this layer tiles across the scroll extent (repeating parallax) rather than ending at a
    /// finite edge. Always <c>false</c> for a <see cref="Collision"/> layer (enforced by the loader).
    /// </summary>
    public bool Repeat { get; init; }

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();

    /// <summary>
    /// The resolved logical terrain paint, parallel to <see cref="Cells"/> and always the same length
    /// (DiVoid #7551 Phase 3, design #7580) — every entry is a declared terrain id or
    /// <see cref="LayerDefinition.EmptyCell"/>. Unlike <see cref="LayerDefinition.Terrain"/> this is ALWAYS
    /// fully populated (never empty) even for a layer with no terrain painted, so <c>TileMapLevelBuilder</c>
    /// can always index it in lockstep with <see cref="Cells"/> without a length check.
    /// </summary>
    public IReadOnlyList<int> Terrain { get; init; } = Array.Empty<int>();
}

/// <summary>
/// One logical terrain, resolved (DiVoid #7551 Phase 3, design #7580) — the runtime counterpart to
/// <see cref="TerrainDefinition"/>.
/// </summary>
public sealed class ResolvedTerrain
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Parsed author colour, or <c>null</c> when the terrain declares none.</summary>
    public RgbaColor? Color { get; init; }

    /// <summary>
    /// This terrain's author-designated fallback tile id (DiVoid #7638), resolved (and validated — a
    /// declared tile, itself a member of this terrain) from <see cref="TerrainDefinition.DefaultTile"/>, or
    /// <c>null</c> when the terrain declares none. <c>TileMapLevelBuilder</c> uses this to fill a
    /// terrain-painted cell that <c>TileMapLayer.SetCellsTerrainConnect</c> left empty (no declared variant
    /// matched its real neighbour pattern) instead of leaving it invisible.
    /// </summary>
    public int? DefaultTileId { get; init; }
}

/// <summary>
/// One terrain set, resolved (DiVoid #7551 Phase 3, design #7580) — the runtime counterpart to
/// <see cref="TerrainSetDefinition"/>. <c>TileSetBuilder</c> maps declaration order onto Godot's own
/// index-based terrain sets/terrains.
/// </summary>
public sealed class ResolvedTerrainSet
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public TerrainMatchMode MatchingMode { get; init; } = TerrainMatchMode.CornersAndSides;

    public IReadOnlyList<ResolvedTerrain> Terrains { get; init; } = Array.Empty<ResolvedTerrain>();
}

/// <summary>
/// A terrain-variant tile's resolved membership (DiVoid #7551 Phase 3, design #7580): which terrain set +
/// terrain it belongs to, and its peering bits. <see cref="TerrainSetId"/> is redundant with
/// <see cref="TerrainId"/> (a terrain belongs to exactly one set) but kept explicit so <c>TileSetBuilder</c>
/// never has to reverse-look-up the owning set.
/// </summary>
public sealed class ResolvedTileTerrain
{
    public int TerrainSetId { get; init; }

    public int TerrainId { get; init; }

    public TerrainPeering PeeringBits { get; init; }
}

/// <summary>
/// One animated tile's resolved playback data: every frame's bytes in author order (frame 0 = the tile's
/// <see cref="TileDefinition.Graphic"/>, then <see cref="TileDefinition.Frames"/> in order) plus the
/// playback speed. Always at least two frames — <see cref="TileDefinition.IsAnimated"/> requires
/// <c>Frames.Count &gt; 0</c>, which is exactly what makes an entry exist for a tile in the first place.
/// </summary>
public sealed class ResolvedAnimation
{
    public IReadOnlyList<byte[]> Frames { get; init; } = Array.Empty<byte[]>();

    /// <summary>Frames per second (Godot's <c>TileSetAtlasSource</c> animation speed unit).</summary>
    public double Speed { get; init; } = TileDefinition.DefaultAnimationSpeed;
}
