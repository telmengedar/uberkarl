using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Guards every property of <see cref="LevelDefinition"/>, <see cref="ObjectPlacement"/> and <see cref="TileDefinition"/> against silently dropping out of a save or a migration rewrite.</summary>
[TestFixture]
public sealed class ProjectionRoundTripGuardTests
{
    private const int TileSize = 16;

    private static readonly ResourcePath TileSetA = ResourcePath.Create("tilesets/first.json");
    private static readonly ResourcePath TileSetB = ResourcePath.Create("tilesets/second.json");
    private static readonly ResourcePath GraphicA = ResourcePath.Create("graphics/first/1.png");
    private static readonly ResourcePath GraphicB = ResourcePath.Create("graphics/second/1.png");
    private static readonly ResourcePath FrameGraphicA = ResourcePath.Create("graphics/first/2.png");
    private static readonly ResourcePath FrameGraphicB = ResourcePath.Create("graphics/second/2.png");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/set.json");
    private static readonly ResourcePath ObjectGraphicPath = ResourcePath.Create("objects/platform.png");
    private static readonly ResourcePath ScriptPath = ResourcePath.Create("scripts/override.poo");
    private static readonly ResourcePath SecondLevelPath = ResourcePath.Create("levels/second.json");
    private static readonly ResourcePath SoloLevelPath = ResourcePath.Create("levels/solo.json");
    private static readonly ResourcePath SoloTileSetPath = ResourcePath.Create("tilesets/solo.json");

    private const string ScriptSource = "$onContact = $other => { player.hurt(1); }\n{ \"onContact\": onContact }";

    [Test]
    [Description("The tile-set dedup pass rebuilds every level it rewrites from a fixed field list, so a property missing from that list is stripped from levels the author never opened.")]
    public void MigrationRewrite_PreservesEveryLevelProperty_ExceptTheTileSetItReplaces()
    {
        var packageBytes = BuildMigrationPackage();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var original = LevelContentSerializer.ReadLevel(package.ReadBytes(SecondLevelPath));

        var result = TileSetMigration.Migrate(package);
        Assert.That(result.LevelsRewritten, Is.EqualTo(1), "fixture did not trigger a rewrite; fix the fixture, not the assertion");

        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));
        var rewritten = LevelContentSerializer.ReadLevel(migrated.ReadBytes(SecondLevelPath));

        var before = SerializedProperties(LevelContentSerializer.WriteLevel(original));
        var after = SerializedProperties(LevelContentSerializer.WriteLevel(rewritten));
        before.Remove("tileSet");
        after.Remove("tileSet");

        Assert.That(after, Is.EqualTo(before));
        Assert.That(rewritten.TileSet, Is.Not.EqualTo(original.TileSet));
    }

    [Test]
    [Description("Object placements are carried verbatim on save today; this guard fails if a lossy field-by-field projection is ever reintroduced.")]
    public void EditorSaveRoundTrip_PreservesEveryObjectPlacementProperty()
    {
        var packageBytes = BuildSoloPackage();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var before = SerializedProperties(package.ReadBytes(SoloLevelPath))["objects"];

        var editable = EditableLevelReader.FromPackage(package, SoloLevelPath);
        var savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(editable));

        using var saved = PackageReader.Open(new MemoryStream(savedBytes));
        var after = SerializedProperties(saved.ReadBytes(SoloLevelPath))["objects"];

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    [Description("Editable tiles are projected field by field on save.")]
    public void EditorTileSetSaveRoundTrip_PreservesEveryTileProperty()
    {
        var packageBytes = BuildSoloPackage();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var before = SerializedProperties(package.ReadBytes(SoloTileSetPath))["tiles"];

        var editable = EditableTileSetReader.FromPackageBytes(packageBytes);
        var savedBytes = TileSetMergeWriter.Compose(package, TileSetMergeWriter.BuildContributions(editable));

        using var saved = PackageReader.Open(new MemoryStream(savedBytes));
        var after = SerializedProperties(saved.ReadBytes(SoloTileSetPath))["tiles"];

        Assert.That(after, Is.EqualTo(before));
    }

    private static readonly string[] KnownLevelDefinitionProperties =
    {
        nameof(LevelDefinition.TileSize),
        nameof(LevelDefinition.Width),
        nameof(LevelDefinition.Height),
        nameof(LevelDefinition.TileSet),
        nameof(LevelDefinition.BackgroundColor),
        nameof(LevelDefinition.Spawns),
        nameof(LevelDefinition.DefaultSpawn),
        nameof(LevelDefinition.Layers),
        nameof(LevelDefinition.TileBehaviorOverrides),
        nameof(LevelDefinition.Triggers),
        nameof(LevelDefinition.Objects),
        nameof(LevelDefinition.LevelScript),
    };

    private static readonly string[] KnownObjectPlacementProperties =
    {
        nameof(ObjectPlacement.ObjectSet),
        nameof(ObjectPlacement.ObjectId),
        nameof(ObjectPlacement.Cell),
        nameof(ObjectPlacement.Name),
        nameof(ObjectPlacement.Behavior),
    };

    private static readonly string[] KnownTileDefinitionProperties =
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
    [Description("A LevelDefinition property missing from this list is silently absent from what MigrationRewrite_PreservesEveryLevelProperty_ExceptTheTileSetItReplaces verifies, while every in-memory test still passes.")]
    public void LevelDefinition_EverySettableProperty_IsAccountedForByThisFixture()
        => AssertSettablePropertiesAreKnown(typeof(LevelDefinition), KnownLevelDefinitionProperties);

    [Test]
    [Description("An ObjectPlacement property missing from this list is silently absent from what EditorSaveRoundTrip_PreservesEveryObjectPlacementProperty verifies, while every in-memory test still passes.")]
    public void ObjectPlacement_EverySettableProperty_IsAccountedForByThisFixture()
        => AssertSettablePropertiesAreKnown(typeof(ObjectPlacement), KnownObjectPlacementProperties);

    [Test]
    [Description("A TileDefinition property missing from this list is silently absent from what EditorTileSetSaveRoundTrip_PreservesEveryTileProperty verifies, while every in-memory test still passes.")]
    public void TileDefinition_EverySettableProperty_IsAccountedForByThisFixture()
        => AssertSettablePropertiesAreKnown(typeof(TileDefinition), KnownTileDefinitionProperties);

    private static void AssertSettablePropertiesAreKnown(Type type, string[] knownProperties)
    {
        var settable = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(settable, Is.EqualTo(knownProperties.OrderBy(name => name, StringComparer.Ordinal).ToArray()),
            $"Add the property to the corresponding FullyPopulated fixture and to Known{type.Name}Properties.");
    }

    private static SortedDictionary<string, string> SerializedProperties(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            properties[property.Name] = property.Value.GetRawText();
        return properties;
    }

    private static ObjectSetDefinition ObjectSet() => new()
    {
        Objects = new[]
        {
            new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(ObjectGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
        },
    };

    private static ObjectPlacement FullyPopulatedPlacement() => new()
    {
        ObjectSet = ResourceReference.ToSelf(ObjectSetPath),
        ObjectId = "platform",
        Cell = new GridPosition(1, 0),
        Name = "platform-1",
        Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(ScriptPath)),
    };

    private static TileDefinition FullyPopulatedTile(ResourcePath graphic, ResourcePath frameGraphic) => new()
    {
        Id = 1,
        Name = "mossy stone",
        Graphic = ResourceReference.ToSelf(graphic),
        CollisionShape = CollisionShapeDefinition.FromRect(1, 2, 12, 10),
        Frames = new[] { ResourceReference.ToSelf(frameGraphic) },
        AnimationSpeed = TileDefinition.DefaultAnimationSpeed + 3,
        Terrain = 3,
        PeeringBits = TerrainPeering.North | TerrainPeering.East,
        Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(ScriptPath)),
    };

    private static LevelDefinition FullyPopulatedLevel(ResourcePath tileSet) => new()
    {
        TileSize = TileSize,
        Width = 2,
        Height = 1,
        TileSet = ResourceReference.ToSelf(tileSet),
        BackgroundColor = "#102030",
        Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
        DefaultSpawn = "start",
        Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = new[] { 1, 1 } } },
        TileBehaviorOverrides = new[]
        {
            new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0), Binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(ScriptPath)) },
        },
        Triggers = new[]
        {
            new AreaTriggerDefinition
            {
                Name = "heal-zone", X = 0, Y = 0, Width = 1, Height = 1,
                Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter),
            },
        },
        Objects = new[] { FullyPopulatedPlacement() },
        LevelScript = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol),
    };

    private static byte[] BuildMigrationPackage()
    {
        var definitionA = new TileSetDefinition { Tiles = new[] { FullyPopulatedTile(GraphicA, FrameGraphicA) } };
        var definitionB = new TileSetDefinition { Tiles = new[] { FullyPopulatedTile(GraphicB, FrameGraphicB) } };

        var firstLevel = new LevelDefinition
        {
            TileSize = TileSize,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetA),
            Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1, 1 } } },
        };

        var builder = new PackageBuilder().WithName("Projection Guard Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GraphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, GraphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameGraphicA, Encoding.UTF8.GetBytes("SOLID-TILE-FRAME-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameGraphicB, Encoding.UTF8.GetBytes("SOLID-TILE-FRAME-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, ScriptPath, Encoding.UTF8.GetBytes(ScriptSource));
        builder.AddResource(ResourceKind.TileSet, TileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, TileSetB, LevelContentSerializer.WriteTileSet(definitionB));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(ObjectSet()));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(firstLevel));
        builder.AddResource(ResourceKind.Level, SecondLevelPath, LevelContentSerializer.WriteLevel(FullyPopulatedLevel(TileSetB)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildSoloPackage()
    {
        var tileSet = new TileSetDefinition { Tiles = new[] { FullyPopulatedTile(GraphicA, FrameGraphicA) } };

        var builder = new PackageBuilder().WithName("Projection Guard Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GraphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameGraphicA, Encoding.UTF8.GetBytes("SOLID-TILE-FRAME-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, ScriptPath, Encoding.UTF8.GetBytes(ScriptSource));
        builder.AddResource(ResourceKind.TileSet, SoloTileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(ObjectSet()));
        builder.AddResource(ResourceKind.Level, SoloLevelPath, LevelContentSerializer.WriteLevel(FullyPopulatedLevel(SoloTileSetPath)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
