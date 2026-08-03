using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Builds the resource contributions a <b>shared tile set</b> owns on save: its tile graphics and its
/// tile set definition, each at the tile set's own namespaced paths (DiVoid #7551 Phase 1a, design #7580) —
/// the counterpart to <see cref="LevelMergeWriter"/> but for <see cref="EditableTileSet"/>. A level that
/// binds this tile set carries only a <see cref="Uberkarl.Packages.ResourceReference"/> to it (see
/// <see cref="LevelMergeWriter"/>'s doc comment) — the tile set is what actually owns and persists the
/// tile/graphic content.
/// </summary>
public static class TileSetMergeWriter
{
    /// <summary>
    /// The set of resource contributions this tile set owns: its tile graphics and its tile set
    /// definition, each at the tile set's own namespaced paths. Pure — no IO, no knowledge of any archive
    /// this might be merged into.
    /// </summary>
    public static IReadOnlyList<PendingResource> BuildContributions(EditableTileSet tileSet)
    {
        if (tileSet is null)
            throw new ArgumentNullException(nameof(tileSet));

        var contributions = new List<PendingResource>(tileSet.Tiles.Count + 1);

        foreach (var tile in tileSet.Tiles)
            contributions.Add(new PendingResource(tile.GraphicPath, ResourceKind.TileGraphic, "image/png", tile.Graphic, attribution: null));

        var tileSetDefinition = new TileSetDefinition
        {
            Tiles = tileSet.Tiles
                .Select(tile => new TileDefinition
                {
                    Id = tile.Id,
                    Name = tile.Name,
                    Graphic = ResourceReference.ToSelf(tile.GraphicPath),
                    Collides = tile.Collides,
                })
                .ToArray(),
        };
        contributions.Add(new PendingResource(
            tileSet.TileSetPath, ResourceKind.TileSet, PackageFormat.DefaultMediaType,
            LevelContentSerializer.WriteTileSet(tileSetDefinition), attribution: null));

        return contributions;
    }

    /// <summary>Merges <paramref name="contributions"/> onto <paramref name="existingPackage"/>. Delegates to <see cref="PackageMergeWriter.Compose"/> — see that type for the shared contract.</summary>
    public static byte[] Compose(Package existingPackage, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.Compose(existingPackage, contributions);

    /// <summary>Mints a brand-new archive containing only <paramref name="contributions"/>. Delegates to <see cref="PackageMergeWriter.BuildFresh"/> — see that type for the shared contract.</summary>
    public static byte[] BuildFresh(string newPackageName, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.BuildFresh(newPackageName, contributions);
}
