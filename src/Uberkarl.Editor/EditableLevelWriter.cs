using System.IO;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Serializes an <see cref="EditableLevel"/> back into a self-contained <c>.pkg</c> byte array via the
/// package format's <see cref="PackageBuilder"/>. It re-emits the tile graphics, the tile set, and the
/// level definition at their preserved in-package paths, so a load → edit → save → reload cycle
/// reproduces the level (with the edited cells). The package identity and metadata are carried through
/// unchanged — saving edits an existing package in place rather than minting a fork.
/// </summary>
public static class EditableLevelWriter
{
    /// <summary>Builds the package bytes for the current state of the level.</summary>
    public static byte[] ToPackageBytes(EditableLevel level)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        var builder = new PackageBuilder()
            .WithId(level.PackageId)
            .WithName(level.Name)
            .WithVersion(level.Version);
        if (level.Attribution is { } attribution)
            builder.WithAttribution(attribution);
        if (level.ForkedFrom is { } forkedFrom)
            builder.WithForkedFrom(forkedFrom);

        foreach (var tile in level.Tiles)
            builder.AddResource(ResourceKind.TileGraphic, tile.GraphicPath, tile.Graphic, "image/png");

        var tileSetDefinition = new TileSetDefinition
        {
            Tiles = level.Tiles
                .Select(tile => new TileDefinition
                {
                    Id = tile.Id,
                    Graphic = ResourceReference.ToSelf(tile.GraphicPath),
                    Collides = tile.Collides,
                })
                .ToArray(),
        };
        builder.AddResource(ResourceKind.TileSet, level.TileSetPath, LevelContentSerializer.WriteTileSet(tileSetDefinition));

        var levelDefinition = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = ResourceReference.ToSelf(level.TileSetPath),
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
        builder.AddResource(ResourceKind.Level, level.LevelPath, LevelContentSerializer.WriteLevel(levelDefinition));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
