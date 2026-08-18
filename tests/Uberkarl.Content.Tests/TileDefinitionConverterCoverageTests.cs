using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>Guards <see cref="TileDefinition"/>, the one authored shape whose serialization is hand-written.</summary>
[TestFixture]
public sealed class TileDefinitionConverterCoverageTests
{
    private static readonly ResourcePath GraphicPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath FramePath = ResourcePath.Create("tiles/grass-2.png");
    private static readonly ResourcePath ScriptPath = ResourcePath.Create("scripts/tile.poo");

    /// <summary>Every settable property of <see cref="TileDefinition"/>, as of the last review of this guard.</summary>
    private static readonly string[] KnownProperties =
    {
        nameof(TileDefinition.Id),
        nameof(TileDefinition.Name),
        nameof(TileDefinition.Graphic),
        nameof(TileDefinition.CollisionShape),
        nameof(TileDefinition.Frames),
        nameof(TileDefinition.AnimationSpeed),
        nameof(TileDefinition.Terrain),
        nameof(TileDefinition.PeeringBits),
        nameof(TileDefinition.Behavior),
    };

    [Test]
    [Description("A tile property missing from either converter half is silently absent from disk while every in-memory test still passes.")]
    public void EverySettableProperty_IsAccountedForByThisFixture()
    {
        var settable = typeof(TileDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.That(settable, Is.EqualTo(KnownProperties.OrderBy(name => name).ToArray()),
            "Add the property to both TileDefinitionJsonConverter.Read and .Write, then to KnownProperties "
            + "and the round-trip test below.");
    }

    [Test]
    public void EverySettableProperty_SurvivesARoundTripThroughTheConverter()
    {
        var original = new TileDefinition
        {
            Id = 7,
            Name = "mossy stone",
            Graphic = ResourceReference.ToSelf(GraphicPath),
            CollisionShape = CollisionShapeDefinition.FromRect(1, 2, 12, 10),
            Frames = new[] { ResourceReference.ToSelf(FramePath) },
            AnimationSpeed = TileDefinition.DefaultAnimationSpeed + 3,
            Terrain = 2,
            PeeringBits = TerrainPeering.North | TerrainPeering.East,
            Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(ScriptPath)),
        };

        var restored = LevelContentSerializer.ReadTileSet(
                LevelContentSerializer.WriteTileSet(new TileSetDefinition { Tiles = new[] { original } }))
            .Tiles.Single();

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(original.Id));
            Assert.That(restored.Name, Is.EqualTo(original.Name));
            Assert.That(restored.Graphic, Is.EqualTo(original.Graphic));
            Assert.That(restored.CollisionShape.Kind, Is.EqualTo(original.CollisionShape.Kind));
            Assert.That(restored.Frames, Is.EqualTo(original.Frames));
            Assert.That(restored.AnimationSpeed, Is.EqualTo(original.AnimationSpeed));
            Assert.That(restored.Terrain, Is.EqualTo(original.Terrain));
            Assert.That(restored.PeeringBits, Is.EqualTo(original.PeeringBits));
            Assert.That(restored.Behavior?.Script, Is.EqualTo(original.Behavior?.Script));
        });
    }

    [Test]
    [Description("A round-trip built from defaults passes even when the converter drops the property, which is how a dropped property hides.")]
    public void TheRoundTripFixture_UsesNonDefaultValues_SoADroppedPropertyCannotHide()
    {
        var defaults = new TileDefinition { Id = 0, Graphic = ResourceReference.ToSelf(GraphicPath) };

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Name, Is.Null);
            Assert.That(defaults.CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None));
            Assert.That(defaults.Frames, Is.Empty);
            Assert.That(defaults.AnimationSpeed, Is.EqualTo(TileDefinition.DefaultAnimationSpeed));
            Assert.That(defaults.Terrain, Is.Null);
            Assert.That(defaults.PeeringBits, Is.EqualTo(TerrainPeering.None));
            Assert.That(defaults.Behavior, Is.Null);
        });
    }
}
