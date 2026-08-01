using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

[TestFixture]
public sealed class LevelSerializationTests
{
    [Test]
    public void Level_RoundTrips_ThroughSerializer()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 3,
            Height = 2,
            TileSet = ResourceReference.ToSelf(ResourcePath.Create("tileset.json")),
            Layers = new[]
            {
                new LayerDefinition { Name = "ground", Cells = new[] { 1, 1, 1, 2, LayerDefinition.EmptyCell, 2 } },
            },
        };

        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.TileSize, Is.EqualTo(16));
            Assert.That(restored.Width, Is.EqualTo(3));
            Assert.That(restored.Height, Is.EqualTo(2));
            Assert.That(restored.TileSet, Is.EqualTo(original.TileSet));
            Assert.That(restored.Layers, Has.Count.EqualTo(1));
            Assert.That(restored.Layers[0].Name, Is.EqualTo("ground"));
            Assert.That(restored.Layers[0].Cells, Is.EqualTo(new[] { 1, 1, 1, 2, LayerDefinition.EmptyCell, 2 }));
        });
    }

    [Test]
    public void TileSet_RoundTrips_ThroughSerializer()
    {
        var grass = ResourceReference.ToSelf(ResourcePath.Create("tiles/grass.png"));
        var dirt = ResourceReference.ToSelf(ResourcePath.Create("tiles/dirt.png"));
        var original = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = grass },
                new TileDefinition { Id = 2, Graphic = dirt },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles, Has.Count.EqualTo(2));
            Assert.That(restored.Tiles[0].Id, Is.EqualTo(1));
            Assert.That(restored.Tiles[0].Graphic, Is.EqualTo(grass));
            Assert.That(restored.Tiles[1].Id, Is.EqualTo(2));
            Assert.That(restored.Tiles[1].Graphic, Is.EqualTo(dirt));
        });
    }

    [Test]
    public void ReadLevel_OnInvalidJson_ThrowsLevelContentException()
    {
        var garbage = System.Text.Encoding.UTF8.GetBytes("{ not json ]");

        Assert.Throws<LevelContentException>(() => LevelContentSerializer.ReadLevel(garbage));
    }
}
