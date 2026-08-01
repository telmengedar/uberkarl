using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

[TestFixture]
public sealed class LevelLoaderTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath DirtPath = ResourcePath.Create("tiles/dirt.png");

    [Test]
    public void Load_MaterializesGridAndGraphics()
    {
        var grassBytes = Encoding.UTF8.GetBytes("GRASS-PNG");
        var dirtBytes = Encoding.UTF8.GetBytes("DIRT-PNG");
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "ground", Cells = new[] { 1, 1, 2, LayerDefinition.EmptyCell } },
            },
        };

        using var registry = BuildRegistry(level, StandardTileSet(), grassBytes, dirtBytes);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TileSize, Is.EqualTo(16));
            Assert.That(resolved.Width, Is.EqualTo(2));
            Assert.That(resolved.Height, Is.EqualTo(2));
            Assert.That(resolved.Layers, Has.Count.EqualTo(1));
            Assert.That(resolved.Layers[0].Cells, Is.EqualTo(new[] { 1, 1, 2, LayerDefinition.EmptyCell }));
            Assert.That(resolved.TileGraphics[1], Is.EqualTo(grassBytes));
            Assert.That(resolved.TileGraphics[2], Is.EqualTo(dirtBytes));
        });
    }

    [Test]
    public void Load_WhenCellCountMismatchesGrid_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "ground", Cells = new[] { 1, 1, 2 } } },
        };

        using var registry = BuildRegistry(level, StandardTileSet(), new byte[] { 1 }, new byte[] { 2 });

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("cells"));
    }

    [Test]
    public void Load_WhenLayerReferencesUnknownTile_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "ground", Cells = new[] { 1, 9 } } },
        };

        using var registry = BuildRegistry(level, StandardTileSet(), new byte[] { 1 }, new byte[] { 2 });

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("undefined tile id 9"));
    }

    [Test]
    public void Load_WhenGraphicMissing_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "ground", Cells = new[] { 1 } } },
        };
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/missing.png")) } },
        };

        var builder = new PackageBuilder().WithName("Broken Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));

        using var registry = OpenRegistry(builder);

        Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
    }

    private static TileSetDefinition StandardTileSet() => new()
    {
        Tiles = new[]
        {
            new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) },
            new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(DirtPath) },
        },
    };

    private static PackageRegistry BuildRegistry(LevelDefinition level, TileSetDefinition tileSet, byte[] grass, byte[] dirt)
    {
        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, grass, "image/png");
        builder.AddResource(ResourceKind.TileGraphic, DirtPath, dirt, "image/png");
        return OpenRegistry(builder);
    }

    private static PackageRegistry OpenRegistry(PackageBuilder builder)
    {
        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
