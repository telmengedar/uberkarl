using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The façade the <c>TileSetEditor</c> UI drives (DiVoid #7551 Phase 1b) — the counterpart to
/// <see cref="LevelEditSession"/> but for a shared <see cref="EditableTileSet"/>. Exposes add/remove/
/// rename/set-collides as intent-level calls and the attach/save surface that namespaces the tile set's
/// resources the first time it is actually persisted. Tile edits are not on an undo stack this increment
/// (mirrors the layer-management panel's own operations, which are likewise not undoable — only cell
/// paint/erase is) — a destructive op (remove) requires the caller to gate it behind a confirm press, the
/// same convention <c>LayerManagerPanel</c> uses for layer delete.
/// </summary>
public sealed class TileSetEditSession
{
    public TileSetEditSession(EditableTileSet tileSet)
    {
        TileSet = tileSet ?? throw new ArgumentNullException(nameof(tileSet));
    }

    /// <summary>The tile set under edit.</summary>
    public EditableTileSet TileSet { get; }

    /// <summary>True when there are unsaved edits since the last successful save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Imports a graphic as a new simple tile. Returns the new tile's id.</summary>
    public int AddTile(byte[] graphic, bool collides, string? name = null)
    {
        var id = TileSet.AddTile(graphic, collides, name);
        IsDirty = true;
        return id;
    }

    /// <summary>Removes the tile with <paramref name="id"/>. No-op (returns <c>false</c>) when it does not exist.</summary>
    public bool RemoveTile(int id)
    {
        var happened = TileSet.RemoveTile(id);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>Renames the tile with <paramref name="id"/> (DiVoid #7513 — the on-screen keyboard). No-op when unchanged.</summary>
    public bool RenameTile(int id, string? name)
    {
        var happened = TileSet.RenameTile(id, name);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>Sets whether the tile with <paramref name="id"/> is solid. No-op when unchanged.</summary>
    public bool SetTileCollides(int id, bool collides)
    {
        var happened = TileSet.SetTileCollides(id, collides);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>
    /// Appends a new animation frame to the tile with <paramref name="id"/> (DiVoid #7551 Phase 2). The
    /// tile's second frame is the simple→animated structural transition. No-op (returns <c>false</c>) when
    /// the tile does not exist.
    /// </summary>
    public bool AddFrame(int id, byte[] graphic)
    {
        var happened = TileSet.AddFrame(id, graphic);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>
    /// Removes the animation frame at <paramref name="frameIndex"/> (0-based into the tile's frames beyond
    /// its primary graphic) from the tile with <paramref name="id"/>. Removing the last one is the
    /// animated→simple structural transition. No-op (returns <c>false</c>) when the tile does not exist or
    /// the index is out of range.
    /// </summary>
    public bool RemoveFrame(int id, int frameIndex)
    {
        var happened = TileSet.RemoveFrame(id, frameIndex);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>Sets the animation speed (frames per second) of the tile with <paramref name="id"/>. No-op when unchanged.</summary>
    public bool SetAnimationSpeed(int id, double speed)
    {
        var happened = TileSet.SetAnimationSpeed(id, speed);
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>Renames the tile set itself. Blank/whitespace-only input is a no-op (mirrors <see cref="LevelEditSession.RenameLevel"/>).</summary>
    public bool Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var happened = TileSet.Rename(name.Trim());
        if (happened)
            IsDirty = true;
        return happened;
    }

    /// <summary>The set of resource contributions (tileset.json + tile graphics, at this tile set's own namespaced paths) this tile set owns.</summary>
    public IReadOnlyList<PendingResource> BuildContributions() => TileSetMergeWriter.BuildContributions(TileSet);

    /// <summary>
    /// Establishes this tile set as a brand-new resource (mirrors <see cref="LevelEditSession.AttachAsNewResource"/>):
    /// derives a slug from the tile set's current name, uniquified against <paramref name="existingResources"/>.
    /// </summary>
    public void AttachAsNewResource(IReadOnlyList<ResourceEntry> existingResources)
    {
        if (existingResources is null)
            throw new ArgumentNullException(nameof(existingResources));

        var baseSlug = TileSetResourcePaths.Slugify(TileSet.Name);
        var slug = TileSetResourcePaths.UniqueSlug(baseSlug, candidate => Contains(existingResources, TileSetResourcePaths.TileSetPath(candidate)));
        TileSet.Attach(slug, overwriteTileSetPath: null);
        IsDirty = true;
    }

    /// <summary>
    /// Ensures this tile set occupies a stable resource slot, attaching it as new only if it never has
    /// been (mirrors the level's own "first save routes through attach" rule). A save orchestrating BOTH a
    /// level and its bound tile set calls this rather than <see cref="AttachAsNewResource"/> unconditionally
    /// — once a shared tile set has a home, saving the level that references it must never move it.
    /// </summary>
    public void EnsureAttached(IReadOnlyList<ResourceEntry> existingResources)
    {
        if (!TileSet.IsAttached)
            AttachAsNewResource(existingResources);
    }

    /// <summary>Establishes this tile set as the explicit replacement for an existing tile set resource (mirrors <see cref="LevelEditSession.AttachToExistingResource"/>).</summary>
    public void AttachToExistingResource(ResourcePath tileSetPath)
    {
        var slug = TileSetResourcePaths.SlugFromTileSetPath(tileSetPath) ?? TileSetResourcePaths.Slugify(TileSet.Name);
        TileSet.Attach(slug, overwriteTileSetPath: tileSetPath);
        IsDirty = true;
    }

    /// <summary>Clears the dirty flag after a caller-orchestrated save has succeeded (used when this session's contributions were composed as part of a combined level+tileset save rather than through this session's own save call).</summary>
    public void MarkSaved() => IsDirty = false;

    /// <summary>Re-marks the session dirty (used if a save write fails after bytes were already produced).</summary>
    public void MarkDirty() => IsDirty = true;

    private static bool Contains(IReadOnlyList<ResourceEntry> resources, ResourcePath path)
    {
        foreach (var entry in resources)
        {
            if (entry.Path == path)
                return true;
        }

        return false;
    }
}
