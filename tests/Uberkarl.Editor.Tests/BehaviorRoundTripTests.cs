using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Save/reload round-trip coverage for the three behavior write sites.</summary>
[TestFixture]
public sealed class BehaviorRoundTripTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 2;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath ObjectGraphicPath = ResourcePath.Create("objects/platform.png");
    private static readonly ResourcePath OverrideScriptPath = ResourcePath.Create("scripts/override.poo");
    private static readonly ResourcePath ObjectScriptPath = ResourcePath.Create("scripts/object.poo");
    private static readonly ResourcePath LevelScriptPath = ResourcePath.Create("scripts/level.poo");
    private static readonly ResourcePath TileScriptPath = ResourcePath.Create("scripts/spike.poo");

    private const string OverrideScriptSource = "$onContact = [] => { self.hurt(5); }\n{ \"onContact\": onContact }";
    private const string ObjectScriptSource = "$onUpdate = [dt] => { self.moveBy(1, 0); }\n{ \"onUpdate\": onUpdate }";
    private const string LevelScriptSource = "$onLevelStart = [] => { self.setState(\"started\", true); }\n{ \"onLevelStart\": onLevelStart }";
    private const string TileScriptSource = "$onContact = [] => { self.hurt(10); }\n{ \"onContact\": onContact }";

    [Test]
    public void LevelSave_RoundTrips_TriggersObjectsTileOverridesAndLevelScript_IncludingScriptBindings()
    {
        var packageBytes = BuildLevelPackageBytes();
        var original = EditableLevelReader.FromPackageBytes(packageBytes);

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(original));
        var reloaded = EditableLevelReader.FromPackageBytes(savedBytes);

        var projection = EditableLevelSnapshot.ToResolvedLevel(reloaded);

        Assert.Multiple(() =>
        {
            var overrideKey = (0, new GridPosition(0, 0));
            Assert.That(projection.TileBehaviorOverrides.ContainsKey(overrideKey), Is.True,
                "the tile-behavior override was dropped by the save/reload round trip.");
            Assert.That(projection.TileBehaviorOverrides[overrideKey]!.IsScript, Is.True);
            Assert.That(projection.TileBehaviorOverrides[overrideKey]!.Script, Is.EqualTo(OverrideScriptSource));

            Assert.That(projection.Triggers, Has.Count.EqualTo(1), "the level's trigger was dropped by the save/reload round trip.");
            Assert.That(projection.Triggers[0].Name, Is.EqualTo("heal-zone"));
            Assert.That(projection.Triggers[0].Binding.IsPredefined, Is.True);
            Assert.That(projection.Triggers[0].Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));

            Assert.That(projection.Objects, Has.Count.EqualTo(1), "the level's placed object was dropped by the save/reload round trip.");
            Assert.That(projection.Objects[0].Name, Is.EqualTo("platform-1"));
            Assert.That(projection.Objects[0].Binding!.IsScript, Is.True);
            Assert.That(projection.Objects[0].Binding!.Script, Is.EqualTo(ObjectScriptSource));

            Assert.That(projection.LevelScript, Is.Not.Null, "the level script was dropped by the save/reload round trip.");
            Assert.That(projection.LevelScript!.IsScript, Is.True);
            Assert.That(projection.LevelScript!.Script, Is.EqualTo(LevelScriptSource));
        });
    }

    [Test]
    public void LevelSave_BuildFresh_EmitsTheScriptResourcesTheLevelReferences()
    {
        var packageBytes = BuildLevelPackageBytes();
        var original = EditableLevelReader.FromPackageBytes(packageBytes);

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var contributions = LevelMergeWriter.BuildContributions(original)
            .Concat(NonLevelNonScriptResourcesOf(package))
            .ToList();
        var freshBytes = LevelMergeWriter.BuildFresh("Fresh Behavior Pack", contributions);
        var reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        var projection = EditableLevelSnapshot.ToResolvedLevel(reloaded);

        Assert.Multiple(() =>
        {
            var overrideKey = (0, new GridPosition(0, 0));
            Assert.That(projection.TileBehaviorOverrides[overrideKey]!.Script, Is.EqualTo(OverrideScriptSource),
                "BuildFresh must emit the override's script resource, not rely on an existing archive already carrying it.");
            Assert.That(projection.Objects[0].Binding!.Script, Is.EqualTo(ObjectScriptSource),
                "BuildFresh must emit the object placement's script resource.");
            Assert.That(projection.LevelScript!.Script, Is.EqualTo(LevelScriptSource),
                "BuildFresh must emit the level script's resource.");
        });
    }

    [Test]
    public void TileSetSave_RoundTrips_TileTypeBehaviorBinding_IncludingAScriptBinding()
    {
        var packageBytes = BuildTileSetPackageBytes();
        var original = EditableTileSetReader.FromPackageBytes(packageBytes);

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var savedBytes = TileSetMergeWriter.Compose(package, TileSetMergeWriter.BuildContributions(original));
        var reloaded = EditableTileSetReader.FromPackageBytes(savedBytes);

        var grass = reloaded.Tiles.Single(t => t.Id == 1);
        var spike = reloaded.Tiles.Single(t => t.Id == 2);

        Assert.Multiple(() =>
        {
            Assert.That(grass.Behavior, Is.Null, "a plain tile must not gain a behavior it never declared.");
            Assert.That(spike.Behavior, Is.Not.Null, "the spike's scripted behavior was dropped by the save/reload round trip.");
            Assert.That(spike.Behavior!.IsScript, Is.True);

            var scriptPath = spike.Behavior!.Script!.Value.Path;
            Assert.That(reloaded.Scripts.ContainsKey(scriptPath), Is.True, "the spike's script source was not carried into the reloaded tile set's script table.");
            Assert.That(reloaded.Scripts[scriptPath], Is.EqualTo(TileScriptSource));
        });
    }

    [Test]
    public void TileSetSave_BuildFresh_EmitsTheSpikeTilesScriptResource()
    {
        var packageBytes = BuildTileSetPackageBytes();
        var original = EditableTileSetReader.FromPackageBytes(packageBytes);

        var freshBytes = TileSetMergeWriter.BuildFresh("Fresh Tile Pack", TileSetMergeWriter.BuildContributions(original));
        var reloaded = EditableTileSetReader.FromPackageBytes(freshBytes);

        var scriptPath = reloaded.Tiles.Single(t => t.Id == 2).Behavior!.Script!.Value.Path;
        Assert.That(reloaded.Scripts[scriptPath], Is.EqualTo(TileScriptSource),
            "BuildFresh must emit the spike's script resource, not rely on an existing archive already carrying it.");
    }

    [Test]
    public void BindTileSet_WithTheTileSetsScriptTable_LetsToResolvedLevelResolveAScriptBoundTileType()
    {
        var packageBytes = BuildTileSetPackageBytes();
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Array.Empty<EditableTile>());
        var tileSet = EditableTileSetReader.FromPackageBytes(packageBytes);

        level.BindTileSet(ResourceReference.ToSelf(TileSetPath), tileSet.Tiles, tileSet.Scripts);

        Assert.DoesNotThrow(() => EditableLevelSnapshot.ToResolvedLevel(level),
            "binding a tile set must carry its script table along with its tiles, or a script-bound tile type cannot be resolved for playtest/save.");
    }

    [Test]
    public void ObjectPlacement_ThatInheritsItsBehaviorFromTheObjectType_StaysUninheritedAfterSaveReload()
    {
        var packageBytes = BuildInheritedObjectBehaviorPackageBytes();
        var original = EditableLevelReader.FromPackageBytes(packageBytes);
        Assert.That(original.Objects[0].Placement.Behavior, Is.Null, "test setup must start with an inherited (unset) placement behavior.");

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(original));
        var reloaded = EditableLevelReader.FromPackageBytes(savedBytes);

        Assert.That(reloaded.Objects[0].Placement.Behavior, Is.Null,
            "saving must not pin the object type's default behavior onto the placement -- that silently severs the type/instance inheritance link.");

        var projection = EditableLevelSnapshot.ToResolvedLevel(reloaded);
        Assert.That(projection.Objects[0].Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.Patrol),
            "the effective behavior must still resolve from the type default even though the placement itself declares none.");
    }

    [Test]
    public void DuplicateTileBehaviorOverride_ThrowsATypedException_NotARawFrameworkOne()
    {
        var level = new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Array.Empty<EditableTile>(),
            new[] { new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, new int[Width * Height]) },
            new Dictionary<ResourcePath, string>(),
            tileBehaviorOverrides: new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0), Removed = true },
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0), Removed = true },
            });

        Assert.Throws<LevelContentException>(() => EditableLevelSnapshot.ToResolvedLevel(level));
    }

    [Test]
    [Description("Authored overrides reach the editor door unvalidated; it must reject the same content the package loader rejects.")]
    public void OverrideOnAnOutOfBoundsLayer_IsRejectedByTheEditorDoorToo()
    {
        var level = LevelWithOverride(new TileBehaviorOverride { Layer = 7, Cell = new GridPosition(0, 0), Removed = true });

        var ex = Assert.Throws<LevelContentException>(() => EditableLevelSnapshot.ToResolvedLevel(level));
        Assert.That(ex!.Message, Does.Contain("layer 7"));
    }

    [Test]
    public void OverrideOnAnOutOfBoundsCell_IsRejectedByTheEditorDoorToo()
    {
        var level = LevelWithOverride(new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(Width, 0), Removed = true });

        var ex = Assert.Throws<LevelContentException>(() => EditableLevelSnapshot.ToResolvedLevel(level));
        Assert.That(ex!.Message, Does.Contain($"{Width}x{Height}"));
    }

    [Test]
    public void OverrideDeclaringBothABindingAndRemoved_IsRejectedByTheEditorDoorToo()
    {
        var level = LevelWithOverride(new TileBehaviorOverride
        {
            Layer = 0,
            Cell = new GridPosition(0, 0),
            Removed = true,
            Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact),
        });

        var ex = Assert.Throws<LevelContentException>(() => EditableLevelSnapshot.ToResolvedLevel(level));
        Assert.That(ex!.Message, Does.Contain("exactly one"));
    }

    [Test]
    public void OverrideDeclaringNeitherABindingNorRemoved_IsRejectedByTheEditorDoorToo()
    {
        var level = LevelWithOverride(new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0) });

        var ex = Assert.Throws<LevelContentException>(() => EditableLevelSnapshot.ToResolvedLevel(level));
        Assert.That(ex!.Message, Does.Contain("neither"));
    }

    private static EditableLevel LevelWithOverride(TileBehaviorOverride entry) => new(
        "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
        TileSize, Width, Height, backgroundColor: null,
        new Dictionary<string, GridPosition>(), defaultSpawn: null,
        Array.Empty<EditableTile>(),
        new[] { new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, new int[Width * Height]) },
        new Dictionary<ResourcePath, string>(),
        tileBehaviorOverrides: new[] { entry });

    [Test]
    public void Migration_PreservesTileOverridesTriggersObjectsAndLevelScript_OnTheRewrittenLevel()
    {
        var packageBytes = BuildMigrationPackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var result = TileSetMigration.Migrate(package);
        Assert.That(result.LevelsRewritten, Is.EqualTo(1), "test setup did not exercise a rewrite -- fix the fixture, not the assertion below.");

        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));
        var reloaded = EditableLevelReader.FromPackage(migrated, ResourcePath.Create("levels/second.json"));
        var projection = EditableLevelSnapshot.ToResolvedLevel(reloaded);

        Assert.Multiple(() =>
        {
            var overrideKey = (0, new GridPosition(0, 0));
            Assert.That(projection.TileBehaviorOverrides.ContainsKey(overrideKey), Is.True,
                "the tile-behavior override was dropped by the migration's level rewrite.");
            Assert.That(projection.TileBehaviorOverrides[overrideKey]!.IsScript, Is.True);
            Assert.That(projection.TileBehaviorOverrides[overrideKey]!.Script, Is.EqualTo(OverrideScriptSource));

            Assert.That(projection.Triggers, Has.Count.EqualTo(1), "the trigger was dropped by the migration's level rewrite.");
            Assert.That(projection.Triggers[0].Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));

            Assert.That(projection.Objects, Has.Count.EqualTo(1), "the placed object was dropped by the migration's level rewrite.");
            Assert.That(projection.Objects[0].Name, Is.EqualTo("platform-1"));

            Assert.That(projection.LevelScript, Is.Not.Null, "the level script was dropped by the migration's level rewrite.");
            Assert.That(projection.LevelScript!.IsPredefined, Is.True);
        });
    }

    private static List<PendingResource> NonLevelNonScriptResourcesOf(Package package)
    {
        var resources = new List<PendingResource>();
        foreach (var entry in package.Manifest.Resources)
        {
            if (entry.Kind is ResourceKind.Level or ResourceKind.Script)
                continue;
            resources.Add(new PendingResource(entry.Path, entry.Kind, entry.MediaType, package.ReadBytes(entry.Path), attribution: null));
        }

        return resources;
    }

    private static byte[] BuildInheritedObjectBehaviorPackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition
                {
                    Id = "patroller", Graphic = ResourceReference.ToSelf(ObjectGraphicPath), CollisionRole = ObjectCollisionRole.Solid,
                    Behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol),
                },
            },
        };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            Objects = new[]
            {
                new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "patroller", Cell = new GridPosition(1, 0), Name = "patroller-1" },
            },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Inherited Object Behavior Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildLevelPackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(ObjectGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride
                {
                    Layer = 0, Cell = new GridPosition(0, 0),
                    Binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(OverrideScriptPath)),
                },
            },
            Triggers = new[]
            {
                new AreaTriggerDefinition
                {
                    Name = "heal-zone", X = 2, Y = 0, Width = 1, Height = 1,
                    Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 20 }),
                },
            },
            Objects = new[]
            {
                new ObjectPlacement
                {
                    ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1",
                    Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(ObjectScriptPath)),
                },
            },
            LevelScript = BehaviorBinding.FromScript(ResourceReference.ToSelf(LevelScriptPath)),
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Behavior Round Trip Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, OverrideScriptPath, Encoding.UTF8.GetBytes(OverrideScriptSource));
        builder.AddResource(ResourceKind.Script, ObjectScriptPath, Encoding.UTF8.GetBytes(ObjectScriptSource));
        builder.AddResource(ResourceKind.Script, LevelScriptPath, Encoding.UTF8.GetBytes(LevelScriptSource));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildTileSetPackageBytes()
    {
        var spikePath = ResourcePath.Create("tiles/spike.png");
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full },
                new TileDefinition
                {
                    Id = 2, Graphic = ResourceReference.ToSelf(spikePath), CollisionShape = CollisionShapeDefinition.Full,
                    Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(TileScriptPath)),
                },
            },
        };

        var builder = new PackageBuilder().WithName("Tile Behavior Round Trip Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, spikePath, Encoding.UTF8.GetBytes("SPIKE-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, TileScriptPath, Encoding.UTF8.GetBytes(TileScriptSource));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildMigrationPackageBytes()
    {
        var tileSetA = ResourcePath.Create("tilesets/first.json");
        var tileSetB = ResourcePath.Create("tilesets/second.json");
        var graphicA = ResourcePath.Create("graphics/first/1.png");
        var graphicB = ResourcePath.Create("graphics/second/1.png");
        var objectSetPath = ResourcePath.Create("objectsets/second.json");
        var objectGraphicPath = ResourcePath.Create("objects/platform.png");

        var definitionA = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicA), CollisionShape = CollisionShapeDefinition.Full } } };
        var definitionB = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicB), CollisionShape = CollisionShapeDefinition.Full } } };

        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(objectGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };

        var firstLevel = new LevelDefinition
        {
            TileSize = TileSize, Width = 1, Height = 1,
            TileSet = ResourceReference.ToSelf(tileSetA),
            Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1 } } },
        };

        var secondLevel = new LevelDefinition
        {
            TileSize = TileSize, Width = 1, Height = 1,
            TileSet = ResourceReference.ToSelf(tileSetB),
            Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1 } } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride
                {
                    Layer = 0, Cell = new GridPosition(0, 0),
                    Binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(OverrideScriptPath)),
                },
            },
            Triggers = new[]
            {
                new AreaTriggerDefinition
                {
                    Name = "heal-zone", X = 0, Y = 0, Width = 1, Height = 1,
                    Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 20 }),
                },
            },
            Objects = new[]
            {
                new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPath), ObjectId = "platform", Cell = new GridPosition(0, 0), Name = "platform-1" },
            },
            LevelScript = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol),
        };

        var builder = new PackageBuilder().WithName("Migration Behavior Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, graphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, graphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, objectGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, OverrideScriptPath, Encoding.UTF8.GetBytes(OverrideScriptSource));
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(definitionB));
        builder.AddResource(ResourceKind.ObjectSet, objectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(firstLevel));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(secondLevel));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
