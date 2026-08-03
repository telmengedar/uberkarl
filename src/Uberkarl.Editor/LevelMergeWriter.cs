using System.IO;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Replaces <c>EditableLevelWriter</c>'s "fabricate a whole package around one level" with the
/// package-as-VFS merge (DiVoid #7571/#7572): a level's save turns into a set of resource
/// <b>contributions</b> — the paths it owns and the bytes for them — which are then either
/// <see cref="Compose"/>d onto an existing archive (identity + every sibling resource carried forward
/// unchanged, contribution paths added-or-replaced) or used to <see cref="BuildFresh"/> a brand-new
/// archive (the one path that legitimately fabricates a package — because the archive really is new).
/// </summary>
public static class LevelMergeWriter
{
    /// <summary>
    /// The set of resource contributions this level owns: its tile graphics, its tile set, and its level
    /// definition, each at the level's own namespaced paths. Pure — no IO, no knowledge of any archive
    /// this might be merged into.
    /// </summary>
    public static IReadOnlyList<PendingResource> BuildContributions(EditableLevel level)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        var contributions = new List<PendingResource>(level.Tiles.Count + 2);

        foreach (var tile in level.Tiles)
            contributions.Add(new PendingResource(tile.GraphicPath, ResourceKind.TileGraphic, "image/png", tile.Graphic, attribution: null));

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
        contributions.Add(new PendingResource(
            level.TileSetPath, ResourceKind.TileSet, PackageFormat.DefaultMediaType,
            LevelContentSerializer.WriteTileSet(tileSetDefinition), attribution: null));

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
        contributions.Add(new PendingResource(
            level.LevelPath, ResourceKind.Level, PackageFormat.DefaultMediaType,
            LevelContentSerializer.WriteLevel(levelDefinition), attribution: null));

        return contributions;
    }

    /// <summary>
    /// Merges <paramref name="contributions"/> onto <paramref name="existingPackage"/>: the result's
    /// identity (id/name/version/attribution/forkedFrom/dependencies) is the existing package's,
    /// unchanged; every existing resource whose path is not among the contributions is carried forward
    /// byte-for-byte; contribution paths are added if new, replaced if already present. This is the fix
    /// for the #7570 §16.7 boundary — saving a level into a package that holds other resources must never
    /// clobber them.
    /// </summary>
    public static byte[] Compose(Package existingPackage, IReadOnlyList<PendingResource> contributions)
    {
        if (existingPackage is null)
            throw new ArgumentNullException(nameof(existingPackage));
        if (contributions is null)
            throw new ArgumentNullException(nameof(contributions));

        var builder = new PackageBuilder().SeedFrom(existingPackage);
        foreach (var contribution in contributions)
            builder.AddOrReplaceResource(contribution.Kind, contribution.Path, contribution.Payload, contribution.MediaType, contribution.Attribution);

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Mints a brand-new archive containing only <paramref name="contributions"/> — a fresh
    /// <see cref="PackageId"/>, <paramref name="newPackageName"/> as the archive's display name (never a
    /// level's), and the starter attribution the editor has always defaulted a freshly-created package
    /// to. The only path that legitimately fabricates a package: the archive really is new here.
    /// </summary>
    public static byte[] BuildFresh(string newPackageName, IReadOnlyList<PendingResource> contributions)
    {
        if (string.IsNullOrWhiteSpace(newPackageName))
            throw new ArgumentException("Package name must not be empty.", nameof(newPackageName));
        if (contributions is null)
            throw new ArgumentNullException(nameof(contributions));

        var builder = new PackageBuilder()
            .WithId(PackageId.New())
            .WithName(newPackageName)
            .WithVersion("0.1.0")
            .WithAttribution(new Attribution { Author = "Uberkarl", License = "CC0-1.0" });

        foreach (var contribution in contributions)
            builder.AddResource(contribution.Kind, contribution.Path, contribution.Payload, contribution.MediaType, contribution.Attribution);

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
