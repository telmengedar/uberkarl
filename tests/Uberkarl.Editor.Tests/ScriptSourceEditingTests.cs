using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers the source editor's save-never-refuses round trip and the "every bound subject moves" half of the milestone acceptance.</summary>
[TestFixture]
public sealed class ScriptSourceEditingTests
{
    private const int TileSize = 16;
    private const int Width = 6;
    private const int Height = 4;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath ObjectGraphicPath = ResourcePath.Create("objects/widget.png");
    private static readonly ResourcePath DoorOpener = ResourcePath.Create("scripts/door-opener.poo");

    private const string WorkingHandler = """
        $onUpdate = $delta => { self.setState("moved", true); }
        { "onUpdate": onUpdate }
        """;

    private const string BrokenHandler = "$onUpdate = $delta => { ";

    [Test]
    [Description("Red-first form of design #8769 §5.6d: a save that refuses a broken script would fail this. The source editor's Save never consults the validator.")]
    public void SaveReload_BrokenScript_SurvivesVerbatim()
    {
        (byte[] packageBytes, EditableLevel level) = BuildFixture();
        LevelEditSession session = new LevelEditSession(level);
        BindDoorOpenerToAPlacedObject(session, packageBytes, BehaviorScriptTemplates.For(BehaviorSubjectKind.Object));
        session.UpsertScriptSource(DoorOpener, BrokenHandler);

        byte[] bytes = SaveOntoFixture(session, packageBytes);

        using Package reopened = PackageReader.Open(new MemoryStream(bytes));
        EditableLevel reloadedLevel = EditableLevelReader.FromPackage(reopened);
        Assert.That(reloadedLevel.Scripts[DoorOpener], Is.EqualTo(BrokenHandler));
    }

    [Test]
    [Description("The dual of the broken-script save: a valid edit must still survive the same round trip and still compile clean -- a mutation breaking the round trip for every script, not only broken ones, must go red here too.")]
    public void SaveReload_ValidScript_SurvivesVerbatim_AndStillCompilesClean()
    {
        (byte[] packageBytes, EditableLevel level) = BuildFixture();
        LevelEditSession session = new LevelEditSession(level);
        BindDoorOpenerToAPlacedObject(session, packageBytes, BehaviorScriptTemplates.For(BehaviorSubjectKind.Object));
        session.UpsertScriptSource(DoorOpener, WorkingHandler);

        byte[] bytes = SaveOntoFixture(session, packageBytes);

        using Package reopened = PackageReader.Open(new MemoryStream(bytes));
        EditableLevel reloadedLevel = EditableLevelReader.FromPackage(reopened);
        Assert.That(reloadedLevel.Scripts[DoorOpener], Is.EqualTo(WorkingHandler));
        Assert.That(BehaviorSourceValidator.Validate(reloadedLevel.Scripts[DoorOpener], BehaviorScriptRole.Behavior), Is.Null);
    }

    private static void BindDoorOpenerToAPlacedObject(LevelEditSession session, byte[] packageBytes, string initialSource)
    {
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableObjectType objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
        session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 0, 0, "door-a");

        session.UpsertScriptSource(DoorOpener, initialSource);
        session.AssignObjectBehavior(session.Level.FindObjectIndexAt(0, 0), BehaviorBinding.FromScript(ResourceReference.ToSelf(DoorOpener)));
    }

    [Test]
    [Description("Design #8769 acceptance, M5b half: editing a shared script moves every bound subject, not just the one the author happened to be looking at when they opened the editor -- a text-box test cannot see this, only a shared-resource test can.")]
    public void EditingASharedScript_MovesEveryBoundSubject()
    {
        (List<(string Name, bool Fired)> results, bool anyQuarantined) = RunSharedScriptScenario(WorkingHandler);

        Assert.Multiple(() =>
        {
            Assert.That(anyQuarantined, Is.False);
            Assert.That(results.Single(r => r.Name == "door-a").Fired, Is.True, "door-a must run the edited handler");
            Assert.That(results.Single(r => r.Name == "door-b").Fired, Is.True, "door-b must run the SAME edited handler -- the sharing half of the acceptance");
        });
    }

    [Test]
    [Description("The dual of the sharing test: a mutation that breaks the edited script must quarantine every bound subject, not silently leave one working -- pins the shared-resource property from the failure side too.")]
    public void EditingASharedScriptToSomethingBroken_QuarantinesEveryBoundSubject()
    {
        (_, bool anyQuarantined) = RunSharedScriptScenario(BrokenHandler);

        Assert.That(anyQuarantined, Is.True);
    }

    private static (List<(string Name, bool Fired)> Results, bool AnyQuarantined) RunSharedScriptScenario(string editedSource)
    {
        (byte[] packageBytes, EditableLevel level) = BuildFixture();
        LevelEditSession session = new LevelEditSession(level);
        using (Package package = PackageReader.Open(new MemoryStream(packageBytes)))
        {
            EditableObjectType objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 0, 0, "door-a");
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 4, 0, "door-b");
        }
        int objectAIndex = level.FindObjectIndexAt(0, 0);
        int objectBIndex = level.FindObjectIndexAt(4, 0);

        session.UpsertScriptSource(DoorOpener, BehaviorScriptTemplates.For(BehaviorSubjectKind.Object));
        BehaviorBinding binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(DoorOpener));
        session.AssignObjectBehavior(objectAIndex, binding);
        session.AssignObjectBehavior(objectBIndex, binding);

        session.UpsertScriptSource(DoorOpener, editedSource);

        ResolvedLevel projection = EditableLevelSnapshot.ToResolvedLevel(level);
        BehaviorLoader loader = new BehaviorLoader(BehaviorScriptBudgets.DefaultBehavior(), BehaviorScriptBudgets.DefaultInit());
        List<(string Name, bool Fired)> results = new List<(string, bool)>();
        bool anyQuarantined = false;

        foreach (ResolvedObjectPlacement placement in new[] { projection.Objects.Single(o => o.Name == "door-a"), projection.Objects.Single(o => o.Name == "door-b") })
        {
            IntentBuffer intents = new IntentBuffer();
            BehaviorSubject subject = new BehaviorSubject(placement.Name, "object", placement.Name, intents);
            CompiledBehavior compiled = loader.CompileBinding(placement.Binding!, BehaviorGlobals.Compose(subject, new BehaviorLevel(intents), new BehaviorPlayer(intents), new BehaviorEvent()));

            if (compiled.IsQuarantined)
            {
                anyQuarantined = true;
                results.Add((placement.Name, false));
                continue;
            }

            compiled.Handlers[BehaviorEventKind.OnUpdate].Invoke(0.016);
            bool fired = intents.Drain().Any(intent => intent is SetStateIntent setState && setState.Key == "moved" && setState.Value is true);
            results.Add((placement.Name, fired));
        }

        return (results, anyQuarantined);
    }

    private static byte[] SaveOntoFixture(LevelEditSession session, byte[] existingPackageBytes)
    {
        using Package existing = PackageReader.Open(new MemoryStream(existingPackageBytes));
        return session.Save(existing);
    }

    private static (byte[] PackageBytes, EditableLevel Level) BuildFixture()
    {
        ObjectDefinition[] objectDefinitions =
        {
            new ObjectDefinition
            {
                Id = "widget",
                Graphic = ResourceReference.ToSelf(ObjectGraphicPath),
                CollisionRole = ObjectCollisionRole.Solid,
            },
        };
        ObjectSetDefinition objectSet = new ObjectSetDefinition { Objects = objectDefinitions };

        LevelDefinition level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = new int[Width * Height] } },
        };

        TileSetDefinition tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        PackageBuilder builder = new PackageBuilder().WithName("Script Source Editing Fixture").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("WIDGET-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using MemoryStream buffer = new MemoryStream();
        builder.Write(buffer);
        byte[] packageBytes = buffer.ToArray();

        return (packageBytes, EditableLevelReader.FromPackageBytes(packageBytes));
    }
}
