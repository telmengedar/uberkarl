using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="ObjectSetEditSession"/> and the <see cref="EditableLevel"/> object-type authoring seams.</summary>
[TestFixture]
public sealed class ObjectSetAuthoringTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 2;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath PlatformGraphicPath = ResourcePath.Create("objects/platform.png");

    private static byte[] Png(string marker) => Encoding.UTF8.GetBytes(marker);

    [Test]
    public void AddType_MintsSequentialIds_ObjectThenObject2_ForABlankSession_TypesGrows()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");

        string first = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        string second = session.AddType(Png("B"), ObjectCollisionRole.Passthrough);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("object"));
            Assert.That(second, Is.EqualTo("object-2"));
            Assert.That(session.Types, Has.Count.EqualTo(2));
            Assert.That(session.Types[0].Definition.CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
            Assert.That(session.Types[1].Definition.CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
        });
    }

    [Test]
    public void AddType_MarksTheSessionDirty()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        Assert.That(session.IsDirty, Is.False, "a freshly-created session must start clean.");

        session.AddType(Png("A"), ObjectCollisionRole.Solid);

        Assert.That(session.IsDirty, Is.True);
    }

    [Test]
    public void AddType_RejectsEmptyGraphic()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        Assert.Throws<ArgumentException>(() => session.AddType(Array.Empty<byte>(), ObjectCollisionRole.Solid));
    }

    [Test]
    public void AddType_SetsAProvisionalGraphicPath_DerivedFromTheCurrentName()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Forest Objects");

        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo("object"), "pinning the id literally so the graphic-path literal below is unambiguous.");
            Assert.That(session.Types[0].Definition.Graphic.Path, Is.EqualTo(ResourcePath.Create("objects/forest-objects/object.png")));
        });
    }

    [Test]
    [Description("I4: a newly added type's sprite must land in its set's own already-established slug namespace, not a freshly re-derived one from Name — this is what AddType's CurrentSlug/SlugFromObjectSetPath delegation exists for.")]
    public void AddType_OnAnAlreadyAttachedSession_MintsTheGraphicPathInTheEstablishedSlug_NotARederivedOne()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        session.AddType(Png("A"), ObjectCollisionRole.Solid);
        session.EnsureAttached(Array.Empty<ResourceEntry>());

        string secondId = session.AddType(Png("B"), ObjectCollisionRole.Solid);

        Assert.Multiple(() =>
        {
            Assert.That(secondId, Is.EqualTo("object-2"));
            Assert.That(session.Types[1].Definition.Graphic.Path, Is.EqualTo(ResourcePath.Create("objects/untitled-objects/object-2.png")));
        });
    }

    [Test]
    [Description("I4: a type added to a session loaded from a package must land in the loaded set's own slug, not one re-derived from Name (ObjectSetDefinition has no name field, #8170).")]
    public void AddType_OnASessionLoadedFromAPackage_MintsTheGraphicPathInTheLoadedSetsOwnSlug()
    {
        byte[] packageBytes = BuildPackageBytesWithOneType(behavior: null, state: new Dictionary<string, object?>());
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        ObjectSetEditSession session = ObjectSetEditSession.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath));

        string newId = session.AddType(Png("B"), ObjectCollisionRole.Solid);

        Assert.Multiple(() =>
        {
            Assert.That(newId, Is.EqualTo("object"));
            Assert.That(session.Types[1].Definition.Graphic.Path, Is.EqualTo(ResourcePath.Create("objects/demo/object.png")));
        });
    }

    [Test]
    public void RenameType_ChangesTheName_LeavesTheIdUnchanged_ReturnsFalseForAnUnknownId()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);

        bool happened = session.RenameType(id, "Moving Platform");
        bool unknown = session.RenameType("nonexistent", "X");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.Types[0].Definition.Name, Is.EqualTo("Moving Platform"));
            Assert.That(session.Types[0].Definition.Id, Is.EqualTo(id));
            Assert.That(unknown, Is.False);
        });
    }

    [Test]
    public void RenameType_BlankName_NormalizesToNull()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        session.RenameType(id, "Moving Platform");

        session.RenameType(id, "   ");

        Assert.That(session.Types[0].Definition.Name, Is.Null);
    }

    [Test]
    public void RenameType_UnchangedName_IsNoOp_ReturnsFalse()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        session.RenameType(id, "Moving Platform");
        session.MarkSaved();

        bool happened = session.RenameType(id, "Moving Platform");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.IsDirty, Is.False, "a no-op rename must not re-dirty an already-saved session.");
        });
    }

    [Test]
    public void SetCollisionRole_TogglesAndMarksDirty_ReturnsFalseForUnchangedOrUnknownId()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        session.MarkSaved();

        bool changed = session.SetCollisionRole(id, ObjectCollisionRole.Passthrough);
        bool unchanged = session.SetCollisionRole(id, ObjectCollisionRole.Passthrough);
        bool unknown = session.SetCollisionRole("nonexistent", ObjectCollisionRole.Solid);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(session.Types[0].Definition.CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
            Assert.That(session.IsDirty, Is.True);
            Assert.That(unchanged, Is.False);
            Assert.That(unknown, Is.False);
        });
    }

    [Test]
    public void ReplaceGraphic_ReplacesTheBytesAtTheExistingPath_ReturnsFalseForAnUnknownId()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        ResourcePath originalPath = session.Types[0].Definition.Graphic.Path;

        bool happened = session.ReplaceGraphic(id, Png("B"));
        bool unknown = session.ReplaceGraphic("nonexistent", Png("C"));

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.Types[0].Graphic, Is.EqualTo(Png("B")));
            Assert.That(session.Types[0].Definition.Graphic.Path, Is.EqualTo(originalPath), "replacing a graphic must never move its path.");
            Assert.That(unknown, Is.False);
        });
    }

    [Test]
    public void ReplaceGraphic_RejectsEmptyGraphic()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        Assert.Throws<ArgumentException>(() => session.ReplaceGraphic(id, Array.Empty<byte>()));
    }

    [Test]
    public void RemoveType_DropsTheType_ReturnsTrue_UnknownIdIsNoOp()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);

        bool happened = session.RemoveType(id);
        bool unknown = session.RemoveType(id);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.Types, Is.Empty);
            Assert.That(unknown, Is.False);
        });
    }

    [Test]
    public void EnsureAttached_AgainstAPackageAlreadyHoldingTheDerivedPath_Uniquifies_AndRemapsEveryTypesGraphicPath()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string id = session.AddType(Png("A"), ObjectCollisionRole.Solid);
        ResourceEntry[] existingResources =
        {
            new ResourceEntry { Path = ResourcePath.Create("objectsets/untitled-objects.json"), Kind = ResourceKind.ObjectSet },
        };

        session.EnsureAttached(existingResources);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo("object"), "pinning the id literally so the graphic-path literal below is unambiguous.");
            Assert.That(session.ObjectSetPath, Is.EqualTo(ResourcePath.Create("objectsets/untitled-objects-2.json")));
            Assert.That(session.IsAttached, Is.True);
            Assert.That(session.Types[0].Definition.Graphic.Path, Is.EqualTo(ResourcePath.Create("objects/untitled-objects-2/object.png")));
        });
    }

    [Test]
    public void EnsureAttached_SecondCall_IsANoOp_NeverMovesAnAlreadyAttachedSet()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        session.AddType(Png("A"), ObjectCollisionRole.Solid);
        session.EnsureAttached(Array.Empty<ResourceEntry>());
        ResourcePath pathAfterFirst = session.ObjectSetPath;

        session.EnsureAttached(new[] { new ResourceEntry { Path = pathAfterFirst, Kind = ResourceKind.ObjectSet } });

        Assert.That(session.ObjectSetPath, Is.EqualTo(pathAfterFirst), "an object set that already has a home must never be moved by a later save.");
    }

    [Test]
    [Description("I4's divergent case: after a uniquifying EnsureAttached, Slugify(Name) and the set's actual slug differ, so a type added afterward must use the set's own slug.")]
    public void AddType_AfterAUniquifyingEnsureAttached_MintsTheGraphicPathInTheUniquifiedSlug()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");
        session.AddType(Png("A"), ObjectCollisionRole.Solid);
        ResourceEntry[] existingResources =
        {
            new ResourceEntry { Path = ResourcePath.Create("objectsets/untitled-objects.json"), Kind = ResourceKind.ObjectSet },
        };
        session.EnsureAttached(existingResources);

        string secondId = session.AddType(Png("B"), ObjectCollisionRole.Solid);

        Assert.Multiple(() =>
        {
            Assert.That(secondId, Is.EqualTo("object-2"));
            Assert.That(session.Types[1].Definition.Graphic.Path, Is.EqualTo(ResourcePath.Create("objects/untitled-objects-2/object-2.png")),
                "Slugify(Name) would derive 'untitled-objects'; the set's actual slug after uniquification is 'untitled-objects-2'.");
        });
    }

    [Test]
    public void BuildContributions_OnASessionWithZeroTypes_ReturnsEmpty()
    {
        ObjectSetEditSession session = ObjectSetEditSession.CreateBlank("Untitled Objects");

        IReadOnlyList<PendingResource> contributions = session.BuildContributions();

        Assert.That(contributions, Is.Empty);
    }

    [Test]
    public void RenameAndToggleRole_PreservesBehaviorAndState_ThroughBuildContributionsAndReadObjectSet()
    {
        BehaviorBinding behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter);
        Dictionary<string, object?> state = new Dictionary<string, object?> { ["charges"] = 3L };
        byte[] packageBytes = BuildPackageBytesWithOneType(behavior, state);

        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        ObjectSetEditSession session = ObjectSetEditSession.FromPackage(package, ResourceReference.ToSelf(ObjectSetPath));

        session.RenameType("platform", "Moving Platform");
        session.SetCollisionRole("platform", ObjectCollisionRole.Passthrough);

        IReadOnlyList<PendingResource> contributions = session.BuildContributions();
        PendingResource objectSetResource = FindByPath(contributions, ObjectSetPath);
        ObjectSetDefinition reread = LevelContentSerializer.ReadObjectSet(objectSetResource.Payload);

        Assert.Multiple(() =>
        {
            Assert.That(reread.Objects, Has.Count.EqualTo(1));
            Assert.That(reread.Objects[0].Name, Is.EqualTo("Moving Platform"));
            Assert.That(reread.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
            Assert.That(reread.Objects[0].Behavior?.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));
            Assert.That(reread.Objects[0].State["charges"], Is.EqualTo(3L));
        });
    }

    [Test]
    public void CountPlacementsOfType_ReturnsTheMatchingCount_ZeroForOthers()
    {
        EditableLevel level = BlankLevel();
        ResourceReference objectSet = ResourceReference.ToSelf(ObjectSetPath);
        ResourceReference otherObjectSet = ResourceReference.ToSelf(ResourcePath.Create("objectsets/other.json"));
        level.InsertObject(0, MakePlacement(objectSet, "platform", x: 0, y: 0));
        level.InsertObject(1, MakePlacement(objectSet, "platform", x: 1, y: 0));
        level.InsertObject(2, MakePlacement(objectSet, "jump-block", x: 2, y: 0));

        Assert.Multiple(() =>
        {
            Assert.That(level.CountPlacementsOfType(objectSet, "platform"), Is.EqualTo(2));
            Assert.That(level.CountPlacementsOfType(objectSet, "jump-block"), Is.EqualTo(1));
            Assert.That(level.CountPlacementsOfType(objectSet, "nonexistent"), Is.EqualTo(0));
            Assert.That(level.CountPlacementsOfType(otherObjectSet, "platform"), Is.EqualTo(0), "a same-id type in a DIFFERENT object set must not be counted.");
        });
    }

    [Test]
    public void RefreshObjectTypes_AfterACollisionRoleChange_UpdatesTheCachedRole_KeepsPlacementReferenceIdentical()
    {
        EditableLevel level = BlankLevel();
        ResourceReference objectSet = ResourceReference.ToSelf(ObjectSetPath);
        EditableObjectPlacement placement = MakePlacement(objectSet, "platform", x: 0, y: 0);
        level.InsertObject(0, placement);

        ObjectDefinition updated = new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough };
        EditableObjectType[] types = { new EditableObjectType(updated, Png("NEW-PLATFORM-PNG")) };

        level.RefreshObjectTypes(objectSet, types);

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
            Assert.That(level.Objects[0].Graphic, Is.EqualTo(Png("NEW-PLATFORM-PNG")));
            Assert.That(level.Objects[0].Placement, Is.SameAs(placement.Placement), "the authored placement must be carried verbatim, never rewritten (#8247).");
        });
    }

    [Test]
    public void RefreshObjectTypes_APlacementWithItsOwnBehaviorOverride_KeepsTheOverride_NotTheTypesNewDefault()
    {
        EditableLevel level = BlankLevel();
        ResourceReference objectSet = ResourceReference.ToSelf(ObjectSetPath);
        BehaviorBinding placementOverride = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter);
        ObjectPlacement authored = new ObjectPlacement { ObjectSet = objectSet, ObjectId = "platform", Cell = new GridPosition(0, 0), Behavior = placementOverride };
        EditableObjectPlacement placement = new EditableObjectPlacement(authored, ObjectCollisionRole.Solid, Png("PLATFORM-PNG"), placementOverride, new Dictionary<string, object?>());
        level.InsertObject(0, placement);

        BehaviorBinding typeDefault = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact);
        ObjectDefinition updated = new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid, Behavior = typeDefault };
        EditableObjectType[] types = { new EditableObjectType(updated, Png("PLATFORM-PNG")) };

        level.RefreshObjectTypes(objectSet, types);

        Assert.That(level.Objects[0].EffectiveBehavior, Is.SameAs(placementOverride), "a placement's own behavior override must survive a refresh unreplaced by the type's new default.");
    }

    [Test]
    public void RefreshObjectTypes_APlacementWhoseTypeIsNoLongerDeclared_IsLeftUntouched()
    {
        EditableLevel level = BlankLevel();
        ResourceReference objectSet = ResourceReference.ToSelf(ObjectSetPath);
        EditableObjectPlacement placement = MakePlacement(objectSet, "removed-type", x: 0, y: 0);
        level.InsertObject(0, placement);

        level.RefreshObjectTypes(objectSet, Array.Empty<EditableObjectType>());

        Assert.That(level.Objects[0], Is.SameAs(placement));
    }

    [Test]
    public void RefreshObjectTypes_APlacementOfADifferentObjectSet_IsUntouched()
    {
        EditableLevel level = BlankLevel();
        ResourceReference objectSet = ResourceReference.ToSelf(ObjectSetPath);
        ResourceReference otherObjectSet = ResourceReference.ToSelf(ResourcePath.Create("objectsets/other.json"));
        EditableObjectPlacement placement = MakePlacement(otherObjectSet, "platform", x: 0, y: 0);
        level.InsertObject(0, placement);

        ObjectDefinition updated = new ObjectDefinition { Id = "platform", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough };
        EditableObjectType[] types = { new EditableObjectType(updated, Png("NEW-PLATFORM-PNG")) };

        level.RefreshObjectTypes(objectSet, types);

        Assert.That(level.Objects[0], Is.SameAs(placement), "a placement of a DIFFERENT object set must not be rebuilt at all.");
    }

    [Test]
    public void RebindObjectSet_RewritesTheObjectSetReference_CarriesEveryOtherFieldVerbatim()
    {
        EditableLevel level = BlankLevel();
        ResourceReference from = ResourceReference.ToSelf(ResourcePath.Create("objectsets/untitled-objects.json"));
        ResourceReference to = ResourceReference.ToSelf(ResourcePath.Create("objectsets/untitled-objects-2.json"));
        EditableObjectPlacement placement = MakePlacement(from, "platform", x: 1, y: 0);
        level.InsertObject(0, placement);

        level.RebindObjectSet(from, to);

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects[0].Placement.ObjectSet, Is.EqualTo(to));
            Assert.That(level.Objects[0].Placement.ObjectId, Is.EqualTo("platform"));
            Assert.That(level.Objects[0].Placement.Cell, Is.EqualTo(new GridPosition(1, 0)));
            Assert.That(level.Objects[0].CollisionRole, Is.EqualTo(placement.CollisionRole));
        });
    }

    [Test]
    public void RebindObjectSet_APlacementOfADifferentObjectSet_IsUntouched()
    {
        EditableLevel level = BlankLevel();
        ResourceReference from = ResourceReference.ToSelf(ResourcePath.Create("objectsets/untitled-objects.json"));
        ResourceReference to = ResourceReference.ToSelf(ResourcePath.Create("objectsets/untitled-objects-2.json"));
        ResourceReference other = ResourceReference.ToSelf(ResourcePath.Create("objectsets/other.json"));
        EditableObjectPlacement placement = MakePlacement(other, "platform", x: 1, y: 0);
        level.InsertObject(0, placement);

        level.RebindObjectSet(from, to);

        Assert.That(level.Objects[0], Is.SameAs(placement));
    }

    [Test]
    [Description("A22 (design amendment 2026-08-27, #9879 CF-1) — the redo route to the same hole: undoing a placement pushes it onto redo, so removing the type and discarding must close that route too, not only the undo stack's.")]
    public void PlaceThenUndoRemoveTypeAndDiscard_ThenRedo_ReinstatesNothing()
    {
        ObjectSetEditSession objectSetSession = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string typeId = objectSetSession.AddType(Png("A"), ObjectCollisionRole.Solid);
        EditableObjectType objectType = objectSetSession.Types[0];
        EditableLevel level = BlankLevel();
        LevelEditSession session = new LevelEditSession(level);

        session.PlaceObject(null, objectSetSession.Reference, objectType, x: 0, y: 0);
        session.Undo();
        objectSetSession.RemoveType(typeId);
        session.DiscardHistoryForObjectTypeRemoval();

        CellChange? redone = session.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(redone, Is.Null);
            Assert.That(session.CanRedo, Is.False);
            Assert.That(level.Objects, Is.Empty);
        });
    }

    [Test]
    [Description("A23 (design amendment 2026-08-27, §9.4's addendum) — an undo that reinstates a placement must see the type's current cache: place, erase, toggle the type's collision role, undo, refresh — the restored placement carries the new role.")]
    public void PlaceEraseToggleCollisionRoleUndoThenRefresh_RestoredPlacementCarriesTheNewRole()
    {
        ObjectSetEditSession objectSetSession = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string typeId = objectSetSession.AddType(Png("A"), ObjectCollisionRole.Solid);
        EditableObjectType objectType = objectSetSession.Types[0];
        EditableLevel level = BlankLevel();
        LevelEditSession session = new LevelEditSession(level);

        session.PlaceObject(null, objectSetSession.Reference, objectType, x: 0, y: 0);
        session.EraseObjectAt(0, 0);
        objectSetSession.SetCollisionRole(typeId, ObjectCollisionRole.Passthrough);

        session.Undo();
        level.RefreshObjectTypes(objectSetSession.Reference, objectSetSession.Types);

        Assert.Multiple(() =>
        {
            Assert.That(level.Objects, Has.Count.EqualTo(1));
            Assert.That(level.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
            Assert.That(level.Objects[0].Placement.ObjectId, Is.EqualTo(typeId));
        });
    }

    [Test]
    [Description("A24 (design amendment 2026-08-27, §5.7) — discarding history for an object-type removal is not itself a dirtying mutation: IsDirty is left exactly as prior edits set it, and the call is a safe no-op when the history is already empty.")]
    public void DiscardHistoryForObjectTypeRemoval_DoesNotItselfDirtyTheLevel_SafeOnEmptyHistory()
    {
        LevelEditSession cleanSession = new LevelEditSession(BlankLevel());
        cleanSession.DiscardHistoryForObjectTypeRemoval();
        Assert.That(cleanSession.IsDirty, Is.False, "a discard on an empty, clean history must not dirty the level.");

        ObjectSetEditSession objectSetSession = ObjectSetEditSession.CreateBlank("Untitled Objects");
        objectSetSession.AddType(Png("A"), ObjectCollisionRole.Solid);
        LevelEditSession dirtySession = new LevelEditSession(BlankLevel());
        dirtySession.PlaceObject(null, objectSetSession.Reference, objectSetSession.Types[0], x: 0, y: 0);
        Assert.That(dirtySession.IsDirty, Is.True, "placement must already have dirtied the level.");

        dirtySession.DiscardHistoryForObjectTypeRemoval();

        Assert.Multiple(() =>
        {
            Assert.That(dirtySession.IsDirty, Is.True, "the discard neither sets nor clears IsDirty -- it leaves whatever prior edits already set.");
            Assert.That(dirtySession.CanUndo, Is.False);
        });
    }

    [Test]
    [Description("A25 (design amendment 2026-08-27, §9.3's amendment) — pins the accepted cost: the discard is unscoped, so history unrelated to objects is discarded too.")]
    public void DiscardHistoryForObjectTypeRemoval_AlsoDiscardsUnrelatedTileHistory()
    {
        EditableTile tile = new EditableTile(1, ResourcePath.Create("tiles/grass.png"), Png("GRASS"), CollisionShapeDefinition.Full);
        int[] cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        EditableLayer layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        EditableLevel level = new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            new[] { tile }, new[] { layer },
            new Dictionary<ResourcePath, string>());
        LevelEditSession session = new LevelEditSession(level);
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True, "painting a tile must be on the undo stack.");

        ObjectSetEditSession objectSetSession = ObjectSetEditSession.CreateBlank("Untitled Objects");
        string typeId = objectSetSession.AddType(Png("A"), ObjectCollisionRole.Solid);
        objectSetSession.RemoveType(typeId);
        session.DiscardHistoryForObjectTypeRemoval();

        Assert.That(session.CanUndo, Is.False, "the tile paint, unrelated to objects, is discarded too -- the accepted cost, not a bug.");
    }

    private static PendingResource FindByPath(IReadOnlyList<PendingResource> contributions, ResourcePath path)
    {
        foreach (PendingResource candidate in contributions)
        {
            if (candidate.Path == path)
                return candidate;
        }

        throw new InvalidOperationException($"No contribution at '{path.Value}'.");
    }

    private static byte[] BuildPackageBytesWithOneType(BehaviorBinding? behavior, IReadOnlyDictionary<string, object?> state)
    {
        ObjectSetDefinition objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition
                {
                    Id = "platform", Name = null,
                    Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid,
                    Behavior = behavior, State = state,
                },
            },
        };

        PackageBuilder builder = new PackageBuilder().WithName("Object Authoring Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, Png("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));

        using MemoryStream buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static EditableObjectPlacement MakePlacement(ResourceReference objectSet, string objectId, int x, int y) => new EditableObjectPlacement(
        new ObjectPlacement { ObjectSet = objectSet, ObjectId = objectId, Cell = new GridPosition(x, y) },
        ObjectCollisionRole.Solid,
        Png("PLACEMENT-PNG"),
        effectiveBehavior: null,
        state: new Dictionary<string, object?>());

    private static EditableLevel BlankLevel()
    {
        int[] cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        EditableLayer layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Array.Empty<EditableTile>(), new[] { layer },
            new Dictionary<ResourcePath, string>());
    }
}
