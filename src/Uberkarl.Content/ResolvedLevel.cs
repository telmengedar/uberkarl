namespace Uberkarl.Content;

public sealed class ResolvedLevel
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public IReadOnlyList<ResolvedLayer> Layers { get; init; } = Array.Empty<ResolvedLayer>();

    public IReadOnlyDictionary<int, byte[]> TileGraphics { get; init; } = new Dictionary<int, byte[]>();

    /// <summary>Tile ids flagged as solid in the tile set. Only enforced on layers whose <see cref="ResolvedLayer.Collision"/> is true.</summary>
    public IReadOnlySet<int> CollidingTileIds { get; init; } = new HashSet<int>();

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

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
