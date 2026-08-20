using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the M4 assignment model (design #8049 §7): <see cref="EditableLevel"/>'s subject-targeting
/// lookups (<see cref="EditableLevel.FindTriggerIndexAt"/>/<see cref="EditableLevel.FindBehaviorSubjectAt"/>),
/// the four <c>Set*Behavior</c>/<c>SetLevelScript</c> mutators with undo/redo through
/// <see cref="LevelEditSession"/>, and — the S3 acceptance's "persists and takes effect" half — that an
/// assignment survives save/reload through both the standalone <see cref="LevelLoader"/> path and the
/// in-editor playtest projection (<see cref="EditableLevelSnapshot"/>), for all four subject kinds.
/// </summary>
[TestFixture]
public sealed class BehaviorAssignmentTests
{
    private const int TileSize = 16;
    private const int Width = 6;
    private const int Height = 4;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath ObjectGraphicPath = ResourcePath.Create("objects/widget.png");

    [Test]
    public void FindBehaviorSubjectAt_PrioritizesObjectOverTriggerOverTile()
    {
        var (packageBytes, level) = BuildFixture();
        using var package = PackageReader.Open(new System.IO.MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
        var session = new LevelEditSession(level);

        var tileOnly = level.FindBehaviorSubjectAt(0, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(tileOnly.Found, Is.True, "a tile alone at the cell must resolve.");
            Assert.That(tileOnly.Kind, Is.EqualTo(BehaviorSubjectKind.Tile));
        });

        var triggerOnly = level.FindBehaviorSubjectAt(0, 2, 0);
        Assert.Multiple(() =>
        {
            Assert.That(triggerOnly.Found, Is.True, "a trigger rect with no tile/object inside it must still resolve.");
            Assert.That(triggerOnly.Kind, Is.EqualTo(BehaviorSubjectKind.Trigger));
            Assert.That(triggerOnly.Index, Is.EqualTo(0));
        });

        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 2, 0, "on-trigger");
        var objectOverTrigger = level.FindBehaviorSubjectAt(0, 2, 0);
        Assert.Multiple(() =>
        {
            Assert.That(objectOverTrigger.Kind, Is.EqualTo(BehaviorSubjectKind.Object), "an object placed on a trigger's cell must win over the trigger.");
            Assert.That(objectOverTrigger.Index, Is.EqualTo(0));
        });

        var none = level.FindBehaviorSubjectAt(0, 5, 3);
        Assert.That(none.Found, Is.False, "a cell with no tile, trigger, or object must resolve to None.");
    }

    [Test]
    [Description("A cell exactly one past a trigger's rect on either axis must not match it.")]
    public void FindTriggerIndexAt_RectBoundary_IsExclusiveOnTheFarEdge()
    {
        var (_, level) = BuildFixture();

        Assert.Multiple(() =>
        {
            Assert.That(level.FindTriggerIndexAt(2, 0), Is.EqualTo(0), "the rect's own top-left cell must match.");
            Assert.That(level.FindTriggerIndexAt(3, 0), Is.EqualTo(-1), "one cell past a 1-wide rect's right edge must not match.");
            Assert.That(level.FindTriggerIndexAt(2, 1), Is.EqualTo(-1), "one cell past a 1-tall rect's bottom edge must not match.");
            Assert.That(level.FindTriggerIndexAt(1, 0), Is.EqualTo(-1), "one cell before the rect's left edge must not match.");
        });
    }

    [Test]
    [Description("Every other fixture here is single-layer, so a dropped layer filter or a hard-coded layer 0 would survive unnoticed -- this pins both FindTileBehaviorOverrideIndex and FindBehaviorSubjectAt against a non-zero layer.")]
    public void FindTileBehaviorOverrideIndex_AndFindBehaviorSubjectAt_DistinguishNonZeroLayer()
    {
        var (_, level) = BuildMultiLayerFixture();

        level.SetTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 10.0 }));
        level.SetTileBehaviorOverride(1, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 99.0 }));

        var layer0Index = level.FindTileBehaviorOverrideIndex(0, 0, 0);
        var layer1Index = level.FindTileBehaviorOverrideIndex(1, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(2), "layer 0 and layer 1 overrides at the same cell must not collide into one entry.");
            Assert.That(layer0Index, Is.Not.EqualTo(layer1Index));
            Assert.That(level.TileBehaviorOverrides[layer0Index].Binding!.Parameters["amount"], Is.EqualTo(10.0));
            Assert.That(level.TileBehaviorOverrides[layer1Index].Binding!.Parameters["amount"], Is.EqualTo(99.0));

            var subjectOnLayer1 = level.FindBehaviorSubjectAt(1, 0, 0);
            Assert.That(subjectOnLayer1.Kind, Is.EqualTo(BehaviorSubjectKind.Tile));
            Assert.That(subjectOnLayer1.Layer, Is.EqualTo(1), "an assignment made on the active layer (1) must resolve against layer 1, not silently alias onto layer 0.");
        });
    }

    [Test]
    public void SetTileBehaviorOverride_ThenAgain_UpsertsInPlace_NotAppendingASecondEntry()
    {
        var (_, level) = BuildFixture();

        level.SetTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 10.0 }));
        Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1));

        level.SetTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 25.0 }));

        Assert.Multiple(() =>
        {
            Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1), "re-assigning the same cell must replace, not append.");
            Assert.That(level.TileBehaviorOverrides[0].Binding!.Parameters["amount"], Is.EqualTo(25.0));
        });
    }

    [Test]
    public void SetObjectBehavior_ReplacesOwnOverride_AndBecomesTheEffectiveBehavior()
    {
        var (packageBytes, level) = BuildFixture();
        using var package = PackageReader.Open(new System.IO.MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
        var session = new LevelEditSession(level);
        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 4, 0, "mover");

        Assert.That(level.Objects[0].EffectiveBehavior, Is.Null, "the fixture's object type declares no default -- nothing bound yet.");

        level.SetObjectBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol, new Dictionary<string, object?> { ["speed"] = 40.0, ["range"] = 48.0 }));

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects[0].Placement.Behavior!.PredefinedId, Is.EqualTo("patrol"));
            Assert.That(level.Objects[0].EffectiveBehavior!.Parameters["speed"], Is.EqualTo(40.0));
            Assert.That(level.Objects[0].Placement.Name, Is.EqualTo("mover"), "SetObjectBehavior rebuilds the placement field by field -- Name must survive untouched.");
            Assert.That(level.Objects[0].Placement.Cell, Is.EqualTo(new GridPosition(4, 0)), "SetObjectBehavior rebuilds the placement field by field -- Cell must survive, or assigning a behavior would silently teleport the object.");
        });
    }

    [Test]
    public void SetTriggerBehavior_ReplacesBinding_KeepingRectAndNameUnchanged()
    {
        var (_, level) = BuildFixture();

        level.SetTriggerBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 35.0 }));

        AreaTriggerDefinition trigger = level.Triggers[0];
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Name, Is.EqualTo("heal-zone"));
            Assert.That(trigger.X, Is.EqualTo(2));
            Assert.That(trigger.Binding.Parameters["amount"], Is.EqualTo(35.0));
            Assert.That(trigger.Y, Is.EqualTo(0), "SetTriggerBehavior rebuilds the trigger field by field -- Y must survive untouched.");
            Assert.That(trigger.Width, Is.EqualTo(1), "SetTriggerBehavior rebuilds the trigger field by field -- Width must survive untouched.");
            Assert.That(trigger.Height, Is.EqualTo(1), "SetTriggerBehavior rebuilds the trigger field by field -- Height must survive untouched.");
        });
    }

    [Test]
    public void SetLevelScript_FromNull_SetsIt()
    {
        var (_, level) = BuildFixture();
        Assert.That(level.LevelScript, Is.Null);

        level.SetLevelScript(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol));

        Assert.That(level.LevelScript!.PredefinedId, Is.EqualTo("patrol"));
    }

    [Test]
    public void Session_AssignTileBehaviorOverride_IsUndoRedoable()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        session.AssignTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact));
        Assert.Multiple(() =>
        {
            Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1));
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.CanUndo, Is.True);
        });

        session.Undo();
        Assert.That(level.TileBehaviorOverrides, Is.Empty);

        session.Redo();
        Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1));
    }

    [Test]
    [Description("SetTileBehaviorOverrideCommand.Revert's replace branch (re-assigning an already-overridden cell) has no other coverage -- every other undo test here assigns from nothing.")]
    public void Session_AssignTileBehaviorOverride_UndoAfterReassignment_RestoresFirstBinding()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        session.AssignTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 10.0 }));
        session.AssignTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 25.0 }));
        Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1), "re-assigning the same cell replaces, not appends.");

        session.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(level.TileBehaviorOverrides, Has.Count.EqualTo(1), "undo of a re-assignment must restore the replaced entry, not remove it.");
            Assert.That(level.TileBehaviorOverrides[0].Binding!.Parameters["amount"], Is.EqualTo(10.0), "undo must restore the FIRST binding's parameters, not leave the second one in place.");
        });
    }

    [Test]
    public void Session_AssignObjectBehavior_UndoRestoresThePriorNullOverride()
    {
        var (packageBytes, level) = BuildFixture();
        using var package = PackageReader.Open(new System.IO.MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
        var session = new LevelEditSession(level);
        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 4, 0, "mover");

        session.AssignObjectBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol, new Dictionary<string, object?> { ["speed"] = 40.0 }));
        Assert.That(level.Objects[0].EffectiveBehavior!.Parameters["speed"], Is.EqualTo(40.0));

        session.Undo();
        Assert.That(level.Objects[0].EffectiveBehavior, Is.Null);

        session.Redo();
        Assert.That(level.Objects[0].EffectiveBehavior!.Parameters["speed"], Is.EqualTo(40.0));
    }

    [Test]
    public void Session_AssignTriggerBehavior_IsUndoRedoable()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        session.AssignTriggerBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 35.0 }));
        Assert.That(level.Triggers[0].Binding.Parameters["amount"], Is.EqualTo(35.0));

        session.Undo();
        Assert.That(level.Triggers[0].Binding.Parameters, Is.Empty, "the fixture's original trigger binding carries no explicit parameters.");

        session.Redo();
        Assert.That(level.Triggers[0].Binding.Parameters["amount"], Is.EqualTo(35.0));
    }

    [Test]
    public void Session_AssignLevelScript_IsUndoRedoable()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        session.AssignLevelScript(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol));
        Assert.That(level.LevelScript!.PredefinedId, Is.EqualTo("patrol"));

        session.Undo();
        Assert.That(level.LevelScript, Is.Null);

        session.Redo();
        Assert.That(level.LevelScript!.PredefinedId, Is.EqualTo("patrol"));
    }

    [Test]
    [Description("The S3 acceptance's persistence half: an assignment on any of the four subject kinds must survive save/reload through both runtime projections, not just one.")]
    public void AssignedBehaviors_OnAllFourSubjectKinds_SurviveSaveAndReload_ThroughBothRuntimeProjections()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using (var package = PackageReader.Open(new System.IO.MemoryStream(packageBytes)))
        {
            var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 4, 0, "mover");
        }

        session.AssignTileBehaviorOverride(0, 0, 0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 25.0 }));
        session.AssignTriggerBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 35.0 }));
        session.AssignObjectBehavior(0, BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol, new Dictionary<string, object?> { ["speed"] = 40.0, ["range"] = 48.0 }));
        session.AssignLevelScript(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol, new Dictionary<string, object?> { ["speed"] = 12.0 }));

        byte[] savedBytes;
        using (var package = PackageReader.Open(new System.IO.MemoryStream(packageBytes)))
            savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(level));

        using var registry = new PackageRegistry(PackageReader.Open(new System.IO.MemoryStream(savedBytes)));
        ResolvedLevel resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            var (_, _, tileOverrideBinding) = resolved.EffectiveTileBehaviors().Single(entry => entry.Layer == 0 && entry.Cell == new GridPosition(0, 0));
            Assert.That(tileOverrideBinding.PredefinedId, Is.EqualTo("hurtOnContact"));
            Assert.That(tileOverrideBinding.Parameters["amount"], Is.EqualTo(25.0));

            Assert.That(resolved.Triggers[0].Binding.Parameters["amount"], Is.EqualTo(35.0));

            Assert.That(resolved.Objects[0].Binding!.Parameters["speed"], Is.EqualTo(40.0));
            Assert.That(resolved.Objects[0].Binding!.Parameters["range"], Is.EqualTo(48.0));

            Assert.That(resolved.LevelScript!.Parameters["speed"], Is.EqualTo(12.0));
        });

        ResolvedLevel playtestProjection = EditableLevelSnapshot.ToResolvedLevel(level);
        Assert.Multiple(() =>
        {
            var (_, _, tileOverrideBinding) = playtestProjection.EffectiveTileBehaviors().Single(entry => entry.Layer == 0 && entry.Cell == new GridPosition(0, 0));
            Assert.That(tileOverrideBinding.PredefinedId, Is.EqualTo("hurtOnContact"));
            Assert.That(tileOverrideBinding.Parameters["amount"], Is.EqualTo(25.0),
                "the in-editor playtest overlay must resolve the same override the standalone loader does, or S3 would work in standalone play but silently not in the editor's own playtest.");

            Assert.That(playtestProjection.Objects[0].Binding!.Parameters["speed"], Is.EqualTo(40.0));
        });
    }

    private static (byte[] PackageBytes, EditableLevel Level) BuildFixture()
    {
        const int PlacedTileId = 1;
        const int PlacedTileCellIndex = 0;

        int[] cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        cells[PlacedTileCellIndex] = PlacedTileId;

        var objectDefinitions = new[]
        {
            new ObjectDefinition
            {
                Id = "widget",
                Graphic = ResourceReference.ToSelf(ObjectGraphicPath),
                CollisionRole = ObjectCollisionRole.Solid,
            },
        };
        var objectSet = new ObjectSetDefinition { Objects = objectDefinitions };

        var trigger = new AreaTriggerDefinition
        {
            Name = "heal-zone",
            X = 2,
            Y = 0,
            Width = 1,
            Height = 1,
            Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter),
        };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            Triggers = new[] { trigger },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Behavior Assignment Fixture").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("WIDGET-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new System.IO.MemoryStream();
        builder.Write(buffer);
        byte[] packageBytes = buffer.ToArray();

        return (packageBytes, EditableLevelReader.FromPackageBytes(packageBytes));
    }

    /// <summary>Two-layer fixture (both layers hold a tile at the same (0,0) cell) for the layer-index assertions <see cref="BuildFixture"/>'s single layer cannot exercise.</summary>
    private static (byte[] PackageBytes, EditableLevel Level) BuildMultiLayerFixture()
    {
        const int PlacedTileId = 1;
        const int PlacedTileCellIndex = 0;

        int[] layer0Cells = new int[Width * Height];
        Array.Fill(layer0Cells, LayerDefinition.EmptyCell);
        layer0Cells[PlacedTileCellIndex] = PlacedTileId;

        int[] layer1Cells = new int[Width * Height];
        Array.Fill(layer1Cells, LayerDefinition.EmptyCell);
        layer1Cells[PlacedTileCellIndex] = PlacedTileId;

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, Cells = layer0Cells },
                new LayerDefinition { Name = "overlay", Collision = false, Cells = layer1Cells },
            },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = PlacedTileId, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Behavior Assignment Multi-Layer Fixture").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new System.IO.MemoryStream();
        builder.Write(buffer);
        byte[] packageBytes = buffer.ToArray();

        return (packageBytes, EditableLevelReader.FromPackageBytes(packageBytes));
    }
}
