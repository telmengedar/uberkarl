using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Guards the one asymmetry in the content model (DiVoid #8254). Every other authored shape is
/// reflection-serialized, so a new property persists for free. <see cref="TileDefinition"/> alone is
/// handled by a hand-written converter that enumerates each property explicitly in <c>Read</c> AND
/// <c>Write</c> — so a property added to the type and not to both halves is silently absent from disk and
/// silently default on load. It compiles, it works in memory, and every in-memory test still passes.
///
/// Nothing at the declaration site says a converter owns its serialization, and nothing at the converter
/// says it must be revisited when the type changes. These two tests are that missing signal.
/// </summary>
[TestFixture]
public sealed class TileDefinitionConverterCoverageTests
{
    private static readonly ResourcePath GraphicPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath FramePath = ResourcePath.Create("tiles/grass-2.png");
    private static readonly ResourcePath ScriptPath = ResourcePath.Create("scripts/tile.poo");

    /// <summary>
    /// Every settable property of <see cref="TileDefinition"/>, as of the last time this guard was reviewed.
    /// Adding a property here without teaching both halves of the converter about it is the defect this
    /// fixture exists to catch — update the converter first, then this list.
    /// </summary>
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
    public void EverySettableProperty_IsAccountedForByThisFixture()
    {
        var settable = typeof(TileDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.That(settable, Is.EqualTo(KnownProperties.OrderBy(name => name).ToArray()),
            "TileDefinition gained or lost a property. It is the ONLY authored shape whose serialization is "
            + "hand-written, so a new property must be added to BOTH TileDefinitionJsonConverter.Read and "
            + ".Write, then to KnownProperties and the round-trip test below. Without that it compiles, works "
            + "in memory, passes every other test, and silently never reaches disk (DiVoid #8254 / #8050).");
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

    /// <summary>
    /// Every value above is deliberately NON-default, because a round-trip test built from defaults passes
    /// even when the converter drops the property entirely — the restored object would carry the same
    /// default it was never given. That is precisely how a dropped property hides.
    /// </summary>
    [Test]
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
