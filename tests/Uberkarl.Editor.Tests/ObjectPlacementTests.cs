using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers the M2 object palette: <see cref="EditableLevel"/> object-list mutations, <see cref="LevelEditSession"/> placement/removal with undo/redo, and both runtime projections.</summary>
[TestFixture]
public sealed class ObjectPlacementTests
{
    private const int TileSize = 16;
    private const int Width = 6;
    private const int Height = 4;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath ObjectGraphicPath = ResourcePath.Create("objects/widget.png");
    private static readonly ResourcePath SecondObjectGraphicPath = ResourcePath.Create("objects/gadget.png");
    private static readonly ResourcePath ObjectScriptPath = ResourcePath.Create("scripts/widget.poo");

    private const string ObjectScriptSource = "$onSpawn = [] => { self.setState(\"seen\", true); }\n{ \"onSpawn\": onSpawn }";

    [Test]
    public void InsertObject_ThenRemoveObjectAt_RoundTripsTheSameInstance()
    {
        var level = BlankLevel();
        var placement = MakePlacement(x: 1, y: 0, name: "a");

        level.InsertObject(0, placement);
        Assert.That(level.Objects, Has.Count.EqualTo(1));

        var removed = level.RemoveObjectAt(0);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.SameAs(placement));
            Assert.That(level.Objects, Is.Empty);
        });
    }

    [Test]
    public void FindObjectIndexAt_ReturnsTheFirstMatchingCell_OrMinusOne()
    {
        var level = BlankLevel();
        level.InsertObject(0, MakePlacement(x: 1, y: 0, name: "a"));
        level.InsertObject(1, MakePlacement(x: 3, y: 2, name: "b"));

        Assert.Multiple(() =>
        {
            Assert.That(level.FindObjectIndexAt(1, 0), Is.EqualTo(0));
            Assert.That(level.FindObjectIndexAt(3, 2), Is.EqualTo(1));
            Assert.That(level.FindObjectIndexAt(0, 0), Is.EqualTo(-1));
        });
    }

    [Test]
    [Description("Fixtures elsewhere in this file disambiguate objects by X alone; this one pins the Y half of the hit test.")]
    public void FindObjectIndexAt_SameColumn_DisambiguatesByRow()
    {
        var level = BlankLevel();
        level.InsertObject(0, MakePlacement(x: 2, y: 0, name: "top"));
        level.InsertObject(1, MakePlacement(x: 2, y: 3, name: "bottom"));

        Assert.Multiple(() =>
        {
            Assert.That(level.FindObjectIndexAt(2, 0), Is.EqualTo(0));
            Assert.That(level.FindObjectIndexAt(2, 3), Is.EqualTo(1));
            Assert.That(level.FindObjectIndexAt(2, 1), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Session_PlaceObject_OutOfBounds_IsNoOp_AndDoesNotMarkDirty()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];

        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, Width, 0);

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects, Is.Empty);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_PlaceObject_InsertsIt_MarksDirty_AndIsUndoRedoable()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];

        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 2, 1, "widget-1");

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects, Has.Count.EqualTo(1));
            Assert.That(level.Objects[0].Placement.Name, Is.EqualTo("widget-1"));
            Assert.That(level.Objects[0].Placement.Cell, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.CanUndo, Is.True);
        });

        session.Undo();
        Assert.That(level.Objects, Is.Empty, "undo must remove exactly the placed object.");

        session.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(level.Objects, Has.Count.EqualTo(1), "redo must re-insert the placed object.");
            Assert.That(level.Objects[0].Placement.Name, Is.EqualTo("widget-1"));
        });
    }

    [Test]
    public void Session_PlaceObject_CapturesAScriptBoundTypesSource_IntoTheLevelsScriptTable()
    {
        var (packageBytes, level) = BuildScriptBoundFixture();
        var session = new LevelEditSession(level);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];

        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 0, 0);

        Assert.That(level.Scripts.ContainsKey(ObjectScriptPath), Is.True,
            "placing an object whose type's default behavior is a script must capture that script's source into the level's table, or it silently fails to round-trip on save.");
        Assert.That(level.Scripts[ObjectScriptPath], Is.EqualTo(ObjectScriptSource));
    }

    [Test]
    public void Session_EraseObjectAt_RemovesOnlyTheOccupyingObject_AndIsUndoable()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 1, 0, "a");
        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 3, 0, "b");

        var erasedEmptyCell = session.EraseObjectAt(0, 0);
        Assert.That(erasedEmptyCell, Is.False, "erasing an empty cell must no-op, not remove the nearest object.");

        var erased = session.EraseObjectAt(1, 0);
        Assert.Multiple(() =>
        {
            Assert.That(erased, Is.True);
            Assert.That(level.Objects, Has.Count.EqualTo(1));
            Assert.That(level.Objects[0].Placement.Name, Is.EqualTo("b"), "erasing must remove the object AT that cell, not the wrong one.");
        });

        session.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(level.Objects, Has.Count.EqualTo(2));
            Assert.That(level.Objects.Select(o => o.Placement.Name), Is.EqualTo(new[] { "a", "b" }),
                "undo must restore the removed object at its ORIGINAL index, not merely to the set -- order is load-bearing (LevelMergeWriter writes Objects in list order).");
        });
    }

    [Test]
    public void PlacedObject_SurvivesSaveAndReload_ThroughTheStandaloneLevelLoader_TheLevelPlayPath()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
        {
            var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 2, 1, "widget-1");
        }

        byte[] savedBytes;
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
            savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(level));

        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(savedBytes)));
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Objects, Has.Count.EqualTo(1),
                "the editor-placed object did not reach the standalone LevelLoader path game/Play/LevelPlay.cs uses -- it would not run in standalone LevelPlay.");
            Assert.That(resolved.Objects[0].Name, Is.EqualTo("widget-1"));
            Assert.That(resolved.Objects[0].Cell, Is.EqualTo(new GridPosition(2, 1)));
            Assert.That(resolved.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
            Assert.That(resolved.Objects[0].Binding, Is.Not.Null, "the placed object's type-default behavior must still resolve after the round trip.");
            Assert.That(resolved.Objects[0].Binding!.IsPredefined, Is.True);
            Assert.That(resolved.Objects[0].Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.Patrol));
        });
    }

    [Test]
    [Description("Undo after placement must also round-trip clean: the removal is undoable, not just the placement.")]
    public void RemovedObject_AfterPlacementUndo_SurvivesSaveAndReload_AsAbsent()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
        {
            var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 2, 1, "widget-1");
        }
        session.Undo();

        byte[] savedBytes;
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
            savedBytes = LevelMergeWriter.Compose(package, LevelMergeWriter.BuildContributions(level));

        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(savedBytes)));
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Objects, Is.Empty, "an undone placement must not resurrect on save/reload.");
    }

    [Test]
    [Description("EditableLevelSnapshot.ToResolvedLevel feeds the in-editor playtest, a separate consumer from the standalone LevelLoader path above -- a placement's binding/collision role must survive THIS projection too.")]
    public void PlacedObject_ProjectsThroughTheEditorPlaytestSnapshot_WithItsBindingAndCollisionRoleIntact()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
        {
            var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 2, 1, "widget-1");
        }

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Objects, Has.Count.EqualTo(1));
            Assert.That(resolved.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
            Assert.That(resolved.Objects[0].Binding, Is.Not.Null,
                "a placement's effective behavior must reach the playtest projection, or an object that works in standalone play would do nothing in an in-editor playtest.");
            Assert.That(resolved.Objects[0].Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.Patrol));
        });
    }

    [Test]
    public void EditableObjectSetReader_ReadsEveryDeclaredType_WithItsGraphicBytes()
    {
        var packageBytes = BuildPackageBytes(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol), out _, includeSecondObjectType: true);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var types = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath));

        Assert.Multiple(() =>
        {
            Assert.That(types.Select(t => t.Definition.Id), Is.EqualTo(new[] { "widget", "gadget" }));
            Assert.That(types[0].Definition.CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
            Assert.That(types[0].Graphic, Is.EqualTo(Encoding.UTF8.GetBytes("WIDGET-PNG")));
            Assert.That(types[1].Definition.CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
            Assert.That(types[1].Graphic, Is.EqualTo(Encoding.UTF8.GetBytes("GADGET-PNG")));
        });
    }

    [Test]
    public void EditableObjectSetReader_ObjectSetInAnotherPackage_ThrowsLevelContentException()
    {
        var (packageBytes, _) = BuildFixture();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var foreignReference = new ResourceReference(PackageId.New(), ObjectSetPath);

        Assert.Throws<LevelContentException>(() => EditableObjectSetReader.FromPackage(package, foreignReference));
    }

    [Test]
    public void EditableObjectSetReader_ObjectGraphicInAnotherPackage_ThrowsLevelContentException()
    {
        var packageBytes = BuildPackageBytes(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol), out _, foreignGraphic: true);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        Assert.Throws<LevelContentException>(() => EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath)));
    }

    private static EditableLevel BlankLevel()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Array.Empty<EditableTile>(),
            new[] { new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells) },
            new Dictionary<ResourcePath, string>());
    }

    private static EditableObjectPlacement MakePlacement(int x, int y, string name) => new(
        new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "widget", Cell = new GridPosition(x, y), Name = name },
        ObjectCollisionRole.Solid,
        Encoding.UTF8.GetBytes("WIDGET-PNG"),
        effectiveBehavior: null,
        state: new Dictionary<string, object?>());

    private static (byte[] PackageBytes, EditableLevel Level) BuildFixture()
    {
        var packageBytes = BuildPackageBytes(BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol), out _);
        var level = EditableLevelReader.FromPackageBytes(packageBytes);
        return (packageBytes, level);
    }

    private static (byte[] PackageBytes, EditableLevel Level) BuildScriptBoundFixture()
    {
        var packageBytes = BuildPackageBytes(BehaviorBinding.FromScript(ResourceReference.ToSelf(ObjectScriptPath)), out _);
        var level = EditableLevelReader.FromPackageBytes(packageBytes);
        return (packageBytes, level);
    }

    private static byte[] BuildPackageBytes(BehaviorBinding? objectTypeBehavior, out ObjectSetDefinition objectSet, bool foreignGraphic = false, bool includeSecondObjectType = false)
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var objectDefinitions = new List<ObjectDefinition>
        {
            new ObjectDefinition
            {
                Id = "widget",
                Graphic = foreignGraphic ? new ResourceReference(PackageId.New(), ObjectGraphicPath) : ResourceReference.ToSelf(ObjectGraphicPath),
                CollisionRole = ObjectCollisionRole.Solid,
                Behavior = objectTypeBehavior,
            },
        };
        if (includeSecondObjectType)
        {
            objectDefinitions.Add(new ObjectDefinition
            {
                Id = "gadget",
                Graphic = ResourceReference.ToSelf(SecondObjectGraphicPath),
                CollisionRole = ObjectCollisionRole.Solid,
            });
        }

        objectSet = new ObjectSetDefinition { Objects = objectDefinitions.ToArray() };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Object Placement Fixture").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        if (!foreignGraphic)
            builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("WIDGET-PNG"), "image/png");
        if (includeSecondObjectType)
            builder.AddResource(ResourceKind.Sprite, SecondObjectGraphicPath, Encoding.UTF8.GetBytes("GADGET-PNG"), "image/png");
        if (objectTypeBehavior is { IsScript: true })
            builder.AddResource(ResourceKind.Script, ObjectScriptPath, Encoding.UTF8.GetBytes(ObjectScriptSource));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
