using System.IO;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Loads an <see cref="EditableLevel"/> from a package. Unlike <see cref="LevelLoader"/> (which
/// produces the runtime <see cref="ResolvedLevel"/> and drops the authoring detail — graphic paths,
/// per-tile collide flags, spawn/metadata provenance) this keeps everything needed to edit and
/// re-save the level losslessly. Tile graphics are read out as bytes and held in memory, so the
/// returned model has no further dependency on the source package.
///
/// This increment edits self-contained packages (every tile graphic is a self reference in the same
/// package, as the sample is). A tile graphic that points at another package is out of scope for the
/// MVP and surfaces as a clear <see cref="LevelContentException"/>.
/// </summary>
public static class EditableLevelReader
{
    /// <summary>Loads an editable level from raw <c>.pkg</c> bytes.</summary>
    public static EditableLevel FromPackageBytes(byte[] packageBytes)
    {
        if (packageBytes is null)
            throw new ArgumentNullException(nameof(packageBytes));

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        return FromPackage(package);
    }

    /// <summary>Loads an editable level from an already-opened package, using its first level resource.</summary>
    public static EditableLevel FromPackage(Package package)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        return FromPackage(package, FindLevelPath(package));
    }

    /// <summary>Loads the level at <paramref name="levelPath"/> from an already-opened package.</summary>
    public static EditableLevel FromPackage(Package package, ResourcePath levelPath)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (!package.Contains(levelPath))
            throw new LevelContentException($"Package does not contain a resource at '{levelPath}'.");

        var entry = package.GetEntry(levelPath);
        if (entry.Kind != ResourceKind.Level)
            throw new LevelContentException($"Resource '{levelPath}' is not a level resource (kind '{entry.Kind}').");

        var levelDefinition = LevelContentSerializer.ReadLevel(package.ReadBytes(levelPath));

        var tileSetReference = levelDefinition.TileSet;
        if (!tileSetReference.IsSelf && tileSetReference.Package != package.Id)
            throw new LevelContentException("Editing a level whose tile set lives in another package is not supported.");
        var tileSetPath = tileSetReference.Path;
        var tileSetDefinition = LevelContentSerializer.ReadTileSet(package.ReadBytes(tileSetPath));

        var tiles = new List<EditableTile>(tileSetDefinition.Tiles.Count);
        foreach (var tile in tileSetDefinition.Tiles)
        {
            if (!tile.Graphic.IsSelf && tile.Graphic.Package != package.Id)
                throw new LevelContentException(
                    $"Tile {tile.Id} graphic lives in another package; cross-package graphics are not editable in this increment.");
            var graphicBytes = package.ReadBytes(tile.Graphic.Path);
            tiles.Add(new EditableTile(tile.Id, tile.Graphic.Path, graphicBytes, tile.Collides));
        }

        var layers = new List<EditableLayer>(levelDefinition.Layers.Count);
        foreach (var layer in levelDefinition.Layers)
        {
            layers.Add(new EditableLayer(
                layer.Name,
                layer.Collision,
                layer.ScrollSpeed,
                layer.Repeat,
                layer.Cells.ToArray()));
        }

        var manifest = package.Manifest;
        return new EditableLevel(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Attribution,
            manifest.ForkedFrom,
            levelPath,
            tileSetPath,
            levelDefinition.TileSize,
            levelDefinition.Width,
            levelDefinition.Height,
            levelDefinition.BackgroundColor,
            new Dictionary<string, GridPosition>(levelDefinition.Spawns),
            levelDefinition.DefaultSpawn,
            tiles,
            layers);
    }

    private static ResourcePath FindLevelPath(Package package)
    {
        foreach (var entry in package.Manifest.Resources)
        {
            if (entry.Kind == ResourceKind.Level)
                return entry.Path;
        }

        throw new LevelContentException("Package does not contain a level resource.");
    }
}
