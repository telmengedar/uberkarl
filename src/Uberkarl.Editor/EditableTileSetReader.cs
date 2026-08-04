using System.IO;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Loads an <see cref="EditableTileSet"/> from a package (DiVoid #7551 Phase 1a, design #7580) — the
/// counterpart to <see cref="EditableLevelReader"/> but for a standalone shared tile set resource. Used
/// both to seed a level's palette cache (<see cref="EditableLevelReader"/> calls this internally) and,
/// directly, whenever the Godot glue (<c>LevelEditor</c>) wants to open the level's currently-bound tile
/// set for editing in <c>TileSetEditor</c>, or the "bind tileset" affordance resolves a different one the
/// author picked.
///
/// Same same-package restriction as <see cref="EditableLevelReader"/>: a tile set (or a tile graphic
/// inside it) that lives in another package is out of scope for this increment and surfaces as a clear
/// <see cref="LevelContentException"/>.
/// </summary>
public static class EditableTileSetReader
{
    /// <summary>Loads an editable tile set from raw <c>.pkg</c> bytes, using its first tile set resource.</summary>
    public static EditableTileSet FromPackageBytes(byte[] packageBytes)
    {
        if (packageBytes is null)
            throw new ArgumentNullException(nameof(packageBytes));

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        return FromPackage(package);
    }

    /// <summary>Loads an editable tile set from an already-opened package, using its first tile set resource.</summary>
    public static EditableTileSet FromPackage(Package package)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        return FromPackage(package, ResourceReference.ToSelf(FindTileSetPath(package)));
    }

    /// <summary>Loads the tile set <paramref name="tileSetReference"/> points at, from an already-opened package.</summary>
    public static EditableTileSet FromPackage(Package package, ResourceReference tileSetReference)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (!tileSetReference.IsSelf && tileSetReference.Package != package.Id)
            throw new LevelContentException("Editing a tile set that lives in another package is not supported.");

        var tileSetPath = tileSetReference.Path;
        if (!package.Contains(tileSetPath))
            throw new LevelContentException($"Package does not contain a resource at '{tileSetPath}'.");

        var entry = package.GetEntry(tileSetPath);
        if (entry.Kind != ResourceKind.TileSet)
            throw new LevelContentException($"Resource '{tileSetPath}' is not a tile set resource (kind '{entry.Kind}').");

        var tileSetDefinition = LevelContentSerializer.ReadTileSet(package.ReadBytes(tileSetPath));

        var tiles = new List<EditableTile>(tileSetDefinition.Tiles.Count);
        foreach (var tile in tileSetDefinition.Tiles)
        {
            if (!tile.Graphic.IsSelf && tile.Graphic.Package != package.Id)
                throw new LevelContentException(
                    $"Tile {tile.Id} graphic lives in another package; cross-package graphics are not editable in this increment.");
            var graphicBytes = package.ReadBytes(tile.Graphic.Path);

            var frames = new List<EditableTileFrame>(tile.Frames.Count);
            foreach (var frame in tile.Frames)
            {
                if (!frame.IsSelf && frame.Package != package.Id)
                    throw new LevelContentException(
                        $"Tile {tile.Id} animation frame lives in another package; cross-package graphics are not editable in this increment.");
                frames.Add(new EditableTileFrame(frame.Path, package.ReadBytes(frame.Path)));
            }

            tiles.Add(new EditableTile(tile.Id, tile.Graphic.Path, graphicBytes, tile.Collides, tile.Name, frames, tile.AnimationSpeed));
        }

        // Loaded from a real package resource, so this tile set already occupies a stable slot — isAttached
        // is true and tileSetPath is preserved verbatim, even if it predates the per-resource namespacing
        // scheme (a legacy fixed-constant path like "tileset.json" still round-trips fine). The tile set's
        // own display name comes from ITS resource path, mirroring EditableLevelReader's DisplayNameFromPath.
        return new EditableTileSet(DisplayNameFromPath(tileSetPath), tileSetPath, tiles, isAttached: true);
    }

    private static ResourcePath FindTileSetPath(Package package)
    {
        foreach (var entry in package.Manifest.Resources)
        {
            if (entry.Kind == ResourceKind.TileSet)
                return entry.Path;
        }

        throw new LevelContentException("Package does not contain a tile set resource.");
    }

    private static string DisplayNameFromPath(ResourcePath path)
    {
        var value = path.Value;
        var slash = value.LastIndexOf('/');
        var fileName = slash >= 0 ? value[(slash + 1)..] : value;
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
