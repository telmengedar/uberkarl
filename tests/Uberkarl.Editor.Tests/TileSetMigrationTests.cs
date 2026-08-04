using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="TileSetMigration"/> (DiVoid #7551 Phase 1a, design #7580 §12): the mechanical pass
/// that dedups byte-identical per-level tile set resources within a package onto one shared, surviving
/// resource — the exact per-level redundancy Toni flagged, and the shape it took organically once the
/// editor's per-save fabrication (the pre-#7551 behaviour) accumulated duplicate content across two
/// "New" levels saved into the same package.
/// </summary>
[TestFixture]
public sealed class TileSetMigrationTests
{
    private const int TileSize = 16;

    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");

    [Test]
    public void Migrate_DedupsIdenticalTileSets_RewritesTheSecondLevelsReference_RemovesTheDuplicate()
    {
        var (packageBytes, tileSetA, tileSetB, graphicA, graphicB) = BuildTwoLevelsWithIdenticalTileSets();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(1));
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(1));
            Assert.That(result.OrphanedGraphicsRemoved, Is.EqualTo(1));

            // Exactly one tileset resource survives — the redundancy is gone.
            Assert.That(migrated.Manifest.Resources.Count(e => e.Kind == ResourceKind.TileSet), Is.EqualTo(1));
            Assert.That(migrated.Contains(tileSetA), Is.True, "the first-sighted (canonical) tile set survives verbatim.");
            Assert.That(migrated.Contains(tileSetB), Is.False, "the duplicate is removed.");

            // Both levels are still present and BOTH now reference the surviving canonical resource.
            var firstLevel = LevelContentSerializer.ReadLevel(migrated.ReadBytes(ResourcePath.Create("levels/first.json")));
            var secondLevel = LevelContentSerializer.ReadLevel(migrated.ReadBytes(ResourcePath.Create("levels/second.json")));
            Assert.That(firstLevel.TileSet, Is.EqualTo(ResourceReference.ToSelf(tileSetA)));
            Assert.That(secondLevel.TileSet, Is.EqualTo(ResourceReference.ToSelf(tileSetA)), "the second level's reference is rewritten onto the survivor.");

            // The duplicate's own exclusive graphic is gone too; the surviving tileset's own graphic is untouched.
            Assert.That(migrated.Contains(graphicA), Is.True);
            Assert.That(migrated.Contains(graphicB), Is.False);

            // Render-identical: both levels' cells (the observable content) are byte-for-byte unchanged by migration.
            Assert.That(firstLevel.Layers[0].Cells, Is.EqualTo(new[] { 1 }));
            Assert.That(secondLevel.Layers[0].Cells, Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void Migrate_LeavesDistinctTileSets_Untouched()
    {
        var builder = new PackageBuilder().WithName("Mixed Pack");
        var tileSetA = ResourcePath.Create("tilesets/a.json");
        var tileSetB = ResourcePath.Create("tilesets/b.json");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-A"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, ResourcePath.Create("tiles/dirt.png"), Encoding.UTF8.GetBytes("DIRT-B"), "image/png");
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), Collides = true } },
        }));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/dirt.png")), Collides = false } },
        }));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));
        using var package = ToPackage(builder);

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(0));
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(0));
            Assert.That(migrated.Manifest.Resources.Count(e => e.Kind == ResourceKind.TileSet), Is.EqualTo(2), "two genuinely distinct tile sets must both survive.");
        });
    }

    [Test]
    public void Migrate_WithNoLevels_IsANoOp_StillRoundTrips()
    {
        var builder = new PackageBuilder().WithName("Empty");
        using var package = ToPackage(builder);

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(0));
            Assert.That(migrated.Id, Is.EqualTo(package.Id));
            Assert.That(migrated.Manifest.Name, Is.EqualTo("Empty"));
        });
    }

    // Simulates the exact organic redundancy this PR fixes prospectively: two "New" levels, each seeded
    // from the SAME default palette (so their generated tileset.json + graphic bytes are byte-identical),
    // saved into the same package under distinct namespaced slots — precisely what the pre-#7551
    // per-save-fabrication behaviour produced.
    private static (byte[] Bytes, ResourcePath TileSetA, ResourcePath TileSetB, ResourcePath GraphicA, ResourcePath GraphicB) BuildTwoLevelsWithIdenticalTileSets()
    {
        var tileSetA = ResourcePath.Create("tilesets/first.json");
        var tileSetB = ResourcePath.Create("tilesets/second.json");
        var graphicA = ResourcePath.Create("graphics/first/1.png");
        var graphicB = ResourcePath.Create("graphics/second/1.png");

        var builder = new PackageBuilder().WithName("Redundant Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, graphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, graphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");

        var definitionA = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicA), Collides = true } } };
        var definitionB = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicB), Collides = true } } };
        // definitionA and definitionB reference DIFFERENT per-level-namespaced graphic paths (exactly the
        // real shape: graphics/first/1.png vs graphics/second/1.png), so their serialized JSON is NOT
        // byte-identical — but the graphic BYTES those paths resolve to ARE identical, which is what the
        // content-aware signature (not a raw byte comparison) is for.
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(definitionB));

        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return (buffer.ToArray(), tileSetA, tileSetB, graphicA, graphicB);
    }

    private static LevelDefinition MinimalLevel(ResourcePath tileSetPath) => new()
    {
        TileSize = TileSize,
        Width = 1,
        Height = 1,
        TileSet = ResourceReference.ToSelf(tileSetPath),
        Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1 } } },
    };

    private static Package ToPackage(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return PackageReader.Open(new MemoryStream(buffer.ToArray()));
    }
}
