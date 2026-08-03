using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Builds the resource contribution(s) a level owns on save. Historically (pre-DiVoid #7551 Phase 1a)
/// this also fabricated a fresh <c>tileset.json</c> + every tile graphic from <c>EditableLevel.Tiles</c> on
/// EVERY save, namespaced under the level's own slug — the per-level tileset redundancy Toni flagged: N
/// levels saved via the editor meant N private tileset copies, even when identical.
///
/// <b>Under the shared-tileset correction, a level REFERENCES its tile set — it does not own it</b> (design
/// #7580, directly extending #7572's "a level references (not owns) the tileset, so it drops out of the
/// level's contributions"). A level's own contribution is now just its <c>level.json</c>, carrying whatever
/// <see cref="EditableLevel.TileSetReference"/> it is currently bound to; the tile set's own resource
/// (definition + graphics) is <see cref="TileSetMergeWriter"/>'s job, saved (or not) independently. The
/// Godot glue (<c>game/Editor/LevelEditor.cs</c>) is what decides whether to also compose a tile set's
/// contributions into the same save — this writer only ever speaks for the level.
/// </summary>
public static class LevelMergeWriter
{
    /// <summary>
    /// The level's own resource contribution: just its <c>level.json</c>, at its own namespaced path,
    /// carrying the currently-bound <see cref="EditableLevel.TileSetReference"/> as a reference (never a
    /// contribution the level fabricates). Pure — no IO, no knowledge of any archive this might be merged
    /// into.
    /// </summary>
    public static IReadOnlyList<PendingResource> BuildContributions(EditableLevel level)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        var levelDefinition = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSetReference,
            BackgroundColor = level.BackgroundColor,
            Spawns = new Dictionary<string, GridPosition>(level.Spawns),
            DefaultSpawn = level.DefaultSpawn,
            Layers = level.Layers
                .Select(layer => new LayerDefinition
                {
                    Name = layer.Name,
                    Collision = layer.Collision,
                    ScrollSpeed = layer.ScrollSpeed,
                    Repeat = layer.Repeat,
                    Cells = layer.Cells.ToArray(),
                })
                .ToArray(),
        };

        return new[]
        {
            new PendingResource(
                level.LevelPath, ResourceKind.Level, PackageFormat.DefaultMediaType,
                LevelContentSerializer.WriteLevel(levelDefinition), attribution: null),
        };
    }

    /// <summary>Merges <paramref name="contributions"/> onto <paramref name="existingPackage"/>. Delegates to <see cref="PackageMergeWriter.Compose"/> — see that type for the shared contract.</summary>
    public static byte[] Compose(Package existingPackage, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.Compose(existingPackage, contributions);

    /// <summary>Mints a brand-new archive containing only <paramref name="contributions"/>. Delegates to <see cref="PackageMergeWriter.BuildFresh"/> — see that type for the shared contract.</summary>
    public static byte[] BuildFresh(string newPackageName, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.BuildFresh(newPackageName, contributions);
}
