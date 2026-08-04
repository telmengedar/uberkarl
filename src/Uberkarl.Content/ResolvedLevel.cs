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

    /// <summary>Tile ids flagged as solid in the tile set. Only enforced on layers whose <see cref="ResolvedLayer.Collision"/> is true.</summary>
    public IReadOnlySet<int> CollidingTileIds { get; init; } = new HashSet<int>();

    /// <summary>
    /// Resolved animation data (ordered frame bytes + speed) keyed by tile id, for exactly the tiles
    /// <see cref="TileDefinition.IsAnimated"/> flags (DiVoid #7551 Phase 2, design #7580). A tile id absent
    /// from this dictionary is a simple tile — its one frame is already in <see cref="TileGraphics"/>.
    /// <c>TileSetBuilder</c> (Godot-side) is the only consumer: it maps an entry here to a
    /// <c>TileSetAtlasSource</c> with N animation frames + speed, native Godot playback.
    /// </summary>
    public IReadOnlyDictionary<int, ResolvedAnimation> TileAnimations { get; init; } = new Dictionary<int, ResolvedAnimation>();

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
