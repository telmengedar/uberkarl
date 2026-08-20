using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers M5a script-table upsert, slug-collision checking, and the milestone's acceptance case.</summary>
[TestFixture]
public sealed class ScriptResourceAuthoringTests
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

    [Test]
    public void UpsertScript_NewPath_AddsEntry()
    {
        var (_, level) = BuildFixture();

        level.UpsertScript(DoorOpener, "{ }");

        Assert.That(level.Scripts, Has.Count.EqualTo(1));
        Assert.That(level.Scripts[DoorOpener], Is.EqualTo("{ }"));
    }

    [Test]
    [Description("Upsert replaces, not appends -- a second write to the same path stays one table entry.")]
    public void UpsertScript_ExistingPath_ReplacesText_NotAppending()
    {
        var (_, level) = BuildFixture();
        level.UpsertScript(DoorOpener, "first");

        level.UpsertScript(DoorOpener, "second");

        Assert.That(level.Scripts, Has.Count.EqualTo(1));
        Assert.That(level.Scripts[DoorOpener], Is.EqualTo("second"));
    }

    [Test]
    public void Session_UpsertScriptSource_MarksDirty_AndWritesThroughToTheLevel()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        session.UpsertScriptSource(DoorOpener, "{ }");

        Assert.Multiple(() =>
        {
            Assert.That(session.IsDirty, Is.True);
            Assert.That(level.Scripts[DoorOpener], Is.EqualTo("{ }"));
        });
    }

    [Test]
    public void Session_NewScriptSlugTaken_TrueForEntryAlreadyInTheLevelsOwnTable()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);
        session.UpsertScriptSource(DoorOpener, "{ }");

        var isTaken = session.NewScriptSlugTaken();

        Assert.That(isTaken("door-opener"), Is.True);
    }

    [Test]
    [Description("A minted slug must not collide with a sibling resource already in the open package, even when the level's own table is still empty.")]
    public void Session_NewScriptSlugTaken_TrueForPackageSiblingResource_WhenSupplied()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);
        var siblingResources = new[] { new ResourceEntry { Path = DoorOpener, Kind = ResourceKind.Script } };

        var isTaken = session.NewScriptSlugTaken(siblingResources);

        Assert.That(isTaken("door-opener"), Is.True);
    }

    [Test]
    [Description("DiVoid #8786 §3: a sibling that differs only in case still collides on extraction to a case-insensitive filesystem, so it must block the slug too.")]
    public void Session_NewScriptSlugTaken_TrueForPackageSiblingResource_DifferingOnlyInCase()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);
        var siblingResources = new[] { new ResourceEntry { Path = ResourcePath.Create("scripts/Door-Opener.poo"), Kind = ResourceKind.Script } };

        var isTaken = session.NewScriptSlugTaken(siblingResources);

        Assert.That(isTaken("door-opener"), Is.True);
    }

    [Test]
    public void Session_NewScriptSlugTaken_FalseWhenNeitherTableNorPackageHasIt()
    {
        var (_, level) = BuildFixture();
        var session = new LevelEditSession(level);

        var isTaken = session.NewScriptSlugTaken(Array.Empty<ResourceEntry>());

        Assert.That(isTaken("door-opener"), Is.False);
    }

    [Test]
    [Description("DiVoid #8772 acceptance: create a script on one object, bind a second to it from the list, save -- the package gains exactly one script resource, both bindings name it, playtest reports no quarantine.")]
    public void Acceptance_CreateScriptOnOneObject_BindSecondFromList_Save_GainsExactlyOneScriptResource_BothBindingsNameIt_NoQuarantine()
    {
        var (packageBytes, level) = BuildFixture();
        var session = new LevelEditSession(level);
        using (var package = PackageReader.Open(new MemoryStream(packageBytes)))
        {
            var objectType = EditableObjectSetReader.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath))[0];
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 0, 0, "door-a");
            session.PlaceObject(package, ResourceReference.ToSelf(ObjectSetPath), objectType, 4, 0, "door-b");
        }
        int objectAIndex = level.FindObjectIndexAt(0, 0);
        int objectBIndex = level.FindObjectIndexAt(4, 0);

        var creationPicker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object, level.Scripts.Keys.ToList());
        int newScriptRow = creationPicker.Choices.Count - 1;
        Assert.That(creationPicker.Choices[newScriptRow].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.NewScript));
        Assert.That(creationPicker.SelectChoice(newScriptRow), Is.True);
        Assert.That(creationPicker.Stage, Is.EqualTo(BehaviorAssignmentStage.NamingNewScript));
        Assert.That(creationPicker.CreateNewScript("Door Opener", session.NewScriptSlugTaken()), Is.True);

        session.UpsertScriptSource(creationPicker.MintedScriptPath!.Value, creationPicker.MintedScriptSource!);
        session.AssignObjectBehavior(objectAIndex, creationPicker.Result!);

        Assert.That(level.Scripts, Has.Count.EqualTo(1));
        Assert.That(level.Scripts.ContainsKey(DoorOpener), Is.True);

        var sharingPicker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object, level.Scripts.Keys.ToList());
        int existingScriptRow = sharingPicker.ApplicablePredefineds.Count;
        Assert.That(sharingPicker.Choices[existingScriptRow].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.ExistingScript));
        Assert.That(sharingPicker.SelectChoice(existingScriptRow), Is.True);
        Assert.That(sharingPicker.Stage, Is.EqualTo(BehaviorAssignmentStage.Complete));
        Assert.That(sharingPicker.MintedScriptPath, Is.Null, "the sharing pick must write nothing to the table.");

        session.AssignObjectBehavior(objectBIndex, sharingPicker.Result!);

        Assert.That(level.Scripts, Has.Count.EqualTo(1), "binding a second object to the same script must not mint a second table entry.");

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects[objectAIndex].Placement.Behavior!.Script!.Value.Path, Is.EqualTo(DoorOpener));
            Assert.That(level.Objects[objectBIndex].Placement.Behavior!.Script!.Value.Path, Is.EqualTo(DoorOpener));
        });

        var contributions = session.BuildContributions();
        var scriptResources = contributions.Where(c => c.Kind == ResourceKind.Script).ToList();
        Assert.That(scriptResources, Has.Count.EqualTo(1));
        Assert.That(scriptResources[0].Path, Is.EqualTo(DoorOpener));

        var projection = EditableLevelSnapshot.ToResolvedLevel(level);
        var loader = new BehaviorLoader(BehaviorScriptBudgets.DefaultBehavior(), BehaviorScriptBudgets.DefaultInit());
        var intents = new IntentBuffer();

        foreach (var placement in new[] { projection.Objects.Single(o => o.Name == "door-a"), projection.Objects.Single(o => o.Name == "door-b") })
        {
            var subject = new BehaviorSubject(placement.Name, "object", placement.Name, intents);
            var globals = new Dictionary<string, object>
            {
                ["self"] = subject,
                ["level"] = new BehaviorLevel(intents),
                ["player"] = new BehaviorPlayer(intents),
                ["event"] = new BehaviorEvent(),
            };
            var compiled = loader.CompileBinding(placement.Binding!, globals);

            Assert.That(compiled.IsQuarantined, Is.False, $"'{placement.Name}' must not quarantine -- reason: {compiled.QuarantineReason}");
        }
    }

    private static (byte[] PackageBytes, EditableLevel Level) BuildFixture()
    {
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

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = new int[Width * Height] } },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Script Authoring Fixture").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, ObjectGraphicPath, Encoding.UTF8.GetBytes("WIDGET-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        byte[] packageBytes = buffer.ToArray();

        return (packageBytes, EditableLevelReader.FromPackageBytes(packageBytes));
    }
}
