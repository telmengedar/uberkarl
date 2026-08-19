using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers layer editing (DiVoid #7501 / design #7502): the pure rules (<see cref="LayerPropertyRules"/>,
/// <see cref="ScrollSpeedLadder"/>, <see cref="LayerNaming"/>), the <see cref="EditableLevel"/> structural
/// mutations, the <see cref="LevelEditSession"/> layer intents and their history policy — the
/// layer-index aliasing hazard that makes delete/reorder clear cell-edit history while add/property-set
/// preserve it — and the <see cref="MenuOutcome.OpenLayerManager"/> routing seam. No Godot dependency.
/// </summary>
[TestFixture]
public sealed class LayerEditingTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 3;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath WaterPath = ResourcePath.Create("tiles/water.png");

    // ----- LayerPropertyRules -----

    [Test]
    public void Coerce_CollisionOff_LeavesTripleUnchanged()
    {
        var coerced = LayerPropertyRules.Coerce(collision: false, scrollSpeed: 0.5f, repeat: true);

        Assert.Multiple(() =>
        {
            Assert.That(coerced.Collision, Is.False);
            Assert.That(coerced.ScrollSpeed, Is.EqualTo(0.5f));
            Assert.That(coerced.Repeat, Is.True);
            Assert.That(coerced.ScrollSpeedForced, Is.False);
            Assert.That(coerced.RepeatForced, Is.False);
        });
    }

    [Test]
    public void Coerce_CollisionOn_ForcesScrollTo1AndRepeatOff()
    {
        var coerced = LayerPropertyRules.Coerce(collision: true, scrollSpeed: 0.5f, repeat: true);

        Assert.Multiple(() =>
        {
            Assert.That(coerced.Collision, Is.True);
            Assert.That(coerced.ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(coerced.Repeat, Is.False);
            Assert.That(coerced.ScrollSpeedForced, Is.True);
            Assert.That(coerced.RepeatForced, Is.True);
        });
    }

    [Test]
    public void Coerce_CollisionOn_AlreadyValidTriple_ForcesNothing()
    {
        var coerced = LayerPropertyRules.Coerce(collision: true, scrollSpeed: 1.0f, repeat: false);

        Assert.Multiple(() =>
        {
            Assert.That(coerced.ScrollSpeedForced, Is.False);
            Assert.That(coerced.RepeatForced, Is.False);
        });
    }

    [Test]
    public void Editable_TruthTable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LayerPropertyRules.Editable(collision: false), Is.True);
            Assert.That(LayerPropertyRules.Editable(collision: true), Is.False);
        });
    }

    // ----- ScrollSpeedLadder -----

    [Test]
    public void Ladder_StepsUpAndDownThroughPresets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScrollSpeedLadder.Step(0.5f, +1), Is.EqualTo(0.75f));
            Assert.That(ScrollSpeedLadder.Step(0.75f, -1), Is.EqualTo(0.5f));
            Assert.That(ScrollSpeedLadder.Step(1.0f, +1), Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void Ladder_ClampsAtTheTopEnd()
    {
        Assert.That(ScrollSpeedLadder.Step(2.0f, +1), Is.EqualTo(2.0f));
    }

    [Test]
    public void Ladder_ClampsAtTheBottomEnd()
    {
        Assert.That(ScrollSpeedLadder.Step(0.25f, -1), Is.EqualTo(0.25f));
    }

    [Test]
    public void Ladder_Snap_FindsNearestPreset()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScrollSpeedLadder.Snap(0.6f), Is.EqualTo(0.5f));
            Assert.That(ScrollSpeedLadder.Snap(0.7f), Is.EqualTo(0.75f));
            Assert.That(ScrollSpeedLadder.Snap(10f), Is.EqualTo(2.0f));
            Assert.That(ScrollSpeedLadder.Snap(-5f), Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void Ladder_Step_FromOffLadderValue_ProceedsFromTheSnappedPosition()
    {
        // An off-ladder loaded value (e.g. a hand-authored 0.6) is preserved until touched (Toni's decision
        // on open question #2); once stepped, the step proceeds from the nearest preset (0.5), not from 0.6.
        Assert.That(ScrollSpeedLadder.Step(0.6f, +1), Is.EqualTo(0.75f));
        Assert.That(ScrollSpeedLadder.Step(0.6f, -1), Is.EqualTo(0.25f));
    }

    // ----- LayerNaming -----

    [Test]
    public void Naming_NextName_OnEmptyLevel_IsLayer1()
    {
        Assert.That(LayerNaming.NextName(Array.Empty<string>()), Is.EqualTo("Layer 1"));
    }

    [Test]
    public void Naming_NextName_SkipsUsedNames()
    {
        Assert.That(LayerNaming.NextName(new[] { "Layer 1", "Layer 2" }), Is.EqualTo("Layer 3"));
    }

    [Test]
    public void Naming_NextName_FillsAGap()
    {
        Assert.That(LayerNaming.NextName(new[] { "Layer 1", "Layer 3" }), Is.EqualTo("Layer 2"));
    }

    [Test]
    public void Naming_NextName_IgnoresUnrelatedNames()
    {
        Assert.That(LayerNaming.NextName(new[] { "terrain", "decor" }), Is.EqualTo("Layer 1"));
    }

    [Test]
    public void Naming_NextName_NullEnumerable_IsLayer1()
    {
        Assert.That(LayerNaming.NextName(null!), Is.EqualTo("Layer 1"));
    }

    // ----- EditableLevel structural mutations -----

    [Test]
    public void AppendLayer_AddsFullEmptyGridWithDefaultDisplayProperties()
    {
        var level = SampleLevel();

        var index = level.AppendLayer("Layer 2", collision: false, scrollSpeed: 1.0f, repeat: false);

        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(1));
            Assert.That(level.Layers, Has.Count.EqualTo(2));
            var added = level.Layers[1];
            Assert.That(added.Name, Is.EqualTo("Layer 2"));
            Assert.That(added.Collision, Is.False);
            Assert.That(added.ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(added.Repeat, Is.False);
            Assert.That(added.Cells, Has.Length.EqualTo(Width * Height));
            Assert.That(added.Cells, Is.All.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void AppendLayer_CoercesProposedPropertiesThatViolateTheInvariant()
    {
        var level = SampleLevel();

        level.AppendLayer("Collider", collision: true, scrollSpeed: 0.5f, repeat: true);

        var added = level.Layers[1];
        Assert.Multiple(() =>
        {
            Assert.That(added.ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(added.Repeat, Is.False);
        });
    }

    [Test]
    public void RemoveLayerAt_RemovesAndShiftsLaterIndices()
    {
        var level = SampleLevel();
        level.AppendLayer("Layer 2", false, 1.0f, false);
        level.AppendLayer("Layer 3", false, 1.0f, false);

        var removed = level.RemoveLayerAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(level.Layers, Has.Count.EqualTo(2));
            Assert.That(level.Layers[0].Name, Is.EqualTo("terrain"));
            Assert.That(level.Layers[1].Name, Is.EqualTo("Layer 3"));
        });
    }

    [Test]
    public void RemoveLayerAt_RefusesTheLastLayer()
    {
        var level = SampleLevel();

        var removed = level.RemoveLayerAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.False);
            Assert.That(level.Layers, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RemoveLayerAt_OutOfRange_IsNoOp()
    {
        var level = SampleLevel();
        Assert.That(level.RemoveLayerAt(5), Is.False);
        Assert.That(level.RemoveLayerAt(-1), Is.False);
    }

    [Test]
    public void MoveLayer_SwapsAdjacentLayersAndReportsNewIndex()
    {
        var level = SampleLevel();
        level.AppendLayer("Layer 2", false, 1.0f, false);
        level.AppendLayer("Layer 3", false, 1.0f, false);

        var newIndex = level.MoveLayer(0, +1);

        Assert.Multiple(() =>
        {
            Assert.That(newIndex, Is.EqualTo(1));
            Assert.That(level.Layers[0].Name, Is.EqualTo("Layer 2"));
            Assert.That(level.Layers[1].Name, Is.EqualTo("terrain"));
            Assert.That(level.Layers[2].Name, Is.EqualTo("Layer 3"));
        });
    }

    [Test]
    public void MoveLayer_AtTheEnd_IsClampedNoOp()
    {
        var level = SampleLevel();
        level.AppendLayer("Layer 2", false, 1.0f, false);

        Assert.Multiple(() =>
        {
            Assert.That(level.MoveLayer(0, -1), Is.EqualTo(0));
            Assert.That(level.MoveLayer(1, +1), Is.EqualTo(1));
            Assert.That(level.Layers[0].Name, Is.EqualTo("terrain"));
            Assert.That(level.Layers[1].Name, Is.EqualTo("Layer 2"));
        });
    }

    [Test]
    public void RenameLayer_ReplacesName_PreservingEveryOtherPropertyAndCellsArray()
    {
        var level = SampleLevel();
        var cellsBefore = level.Layers[0].Cells;

        var happened = level.RenameLayer(0, "backdrop");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(level.Layers[0].Name, Is.EqualTo("backdrop"));
            Assert.That(level.Layers[0].Collision, Is.True);
            Assert.That(level.Layers[0].ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(level.Layers[0].Repeat, Is.False);
            Assert.That(level.Layers[0].Cells, Is.SameAs(cellsBefore), "the Cells array must be reused, not copied.");
        });
    }

    [Test]
    public void RenameLayer_SameName_IsNoOp()
    {
        var level = SampleLevel(); // layer 0 is named "terrain"
        Assert.That(level.RenameLayer(0, "terrain"), Is.False);
    }

    [Test]
    public void RenameLayer_OutOfRangeIndex_Throws()
    {
        var level = SampleLevel();
        Assert.Throws<ArgumentOutOfRangeException>(() => level.RenameLayer(5, "x"));
    }

    [Test]
    public void RenameLayer_EmptyName_Throws()
    {
        var level = SampleLevel();
        Assert.Throws<ArgumentException>(() => level.RenameLayer(0, ""));
    }

    [Test]
    public void SetLayerProperties_CoercesAndReplacesInstance_PreservingCellsArray()
    {
        var level = SampleLevel();
        var cellsBefore = level.Layers[0].Cells;

        var happened = level.SetLayerProperties(0, collision: false, scrollSpeed: 0.5f, repeat: true);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(level.Layers[0].Collision, Is.False);
            Assert.That(level.Layers[0].ScrollSpeed, Is.EqualTo(0.5f));
            Assert.That(level.Layers[0].Repeat, Is.True);
            Assert.That(level.Layers[0].Cells, Is.SameAs(cellsBefore), "the Cells array must be reused, not copied.");
        });
    }

    [Test]
    public void SetLayerProperties_NoActualChange_IsNoOp()
    {
        var level = SampleLevel(); // terrain: collision:true, scrollSpeed:1, repeat:false
        var happened = level.SetLayerProperties(0, collision: true, scrollSpeed: 1.0f, repeat: false);
        Assert.That(happened, Is.False);
    }

    // ----- LevelEditSession layer intents + history policy -----

    [Test]
    public void Session_AddLayer_AppendsAutoNamedDisplayLayer_PreservesHistory_MarksDirty()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var result = session.AddLayer();

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(result.LayerIndex, Is.EqualTo(1));
            Assert.That(session.Level.Layers[1].Name, Is.EqualTo("Layer 1"));
            Assert.That(session.Level.Layers[1].Collision, Is.False);
            Assert.That(session.Level.Layers[1].ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(session.CanUndo, Is.True, "add is index-stable — cell-edit history must survive.");
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_DeleteLayer_RefusesLast_OtherwiseClearsHistoryAndReconciles()
    {
        var session = new LevelEditSession(SampleLevel());
        session.AddLayer(); // now 2 layers
        session.PaintCell(0, 0, 0, 1); // history recorded against layer 0

        var refusedOnLast = new LevelEditSession(SampleLevel()).DeleteLayer(0);
        Assert.That(refusedOnLast.Happened, Is.False, "the last layer must never be deletable.");

        var result = session.DeleteLayer(0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(session.Level.Layers, Has.Count.EqualTo(1));
            Assert.That(result.LayerIndex, Is.EqualTo(0), "reconciled active index clamps into range.");
            Assert.That(session.CanUndo, Is.False, "delete shifts indices — cell-edit history must clear.");
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_MoveLayer_SwapsClearsHistoryAndReportsNewIndex()
    {
        var session = new LevelEditSession(SampleLevel());
        session.AddLayer();
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var result = session.MoveLayer(0, +1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(result.LayerIndex, Is.EqualTo(1));
            Assert.That(session.Level.Layers[0].Name, Is.EqualTo("Layer 1"));
            Assert.That(session.CanUndo, Is.False, "move shifts indices — cell-edit history must clear.");
        });
    }

    [Test]
    public void Session_MoveLayer_NoOpAtEnd_LeavesHistoryIntact()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);

        var result = session.MoveLayer(0, -1); // already at the back; no-op

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.False);
            Assert.That(session.CanUndo, Is.True, "a no-op move must not disturb history.");
        });
    }

    [Test]
    public void Session_PaintThenSetPropertyThenUndo_StillRevertsThePaint()
    {
        // The key aliasing-safety case (design §9.4): a property edit replaces the EditableLayer instance
        // but reuses its Cells array, so a cell-undo recorded before the property edit still applies to
        // the same array and reverts correctly.
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Palette()));
        session.PaintCell(0, 1, 1, 1);
        Assert.That(session.Level.GetCell(0, 1, 1), Is.EqualTo(1));

        var setResult = session.SetRepeat(0, true); // no-op: layer 0 is collision (CreateBlank), repeat stays locked off
        Assert.That(setResult.Happened, Is.False);

        // Turn collision off first (the natural authoring order) so a property edit actually applies.
        session.SetCollision(0, false);
        session.SetRepeat(0, true);

        var undo = session.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo, Is.EqualTo(new CellChange(0, 1, 1, LayerDefinition.EmptyCell)));
            Assert.That(session.Level.GetCell(0, 1, 1), Is.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void Session_SetCollision_On_CoercesScrollAndRepeat_IndexStable_PreservesHistory()
    {
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Palette()));
        session.AddLayer(); // Layer 1: display (collision:false, scroll:1.0, repeat:false)
        session.SetCollision(1, false); // no-op, already false — keep it simple
        session.StepScrollSpeed(1, -1); // 1.0 -> 0.75
        session.SetRepeat(1, true);
        session.PaintCell(1, 0, 0, LayerDefinition.EmptyCell); // no-op paint, just to have SOME history point
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var result = session.SetCollision(1, true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(session.Level.Layers[1].Collision, Is.True);
            Assert.That(session.Level.Layers[1].ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(session.Level.Layers[1].Repeat, Is.False);
            Assert.That(session.CanUndo, Is.True, "property-set is index-stable — cell-edit history must survive.");
        });
    }

    [Test]
    public void Session_SetScrollSpeed_SetsAbsoluteValue_IndexStable_PreservesHistory()
    {
        // The layer panel's Scroll-stepper edit-mode commit path (DiVoid #7512): the panel steps a LOCAL
        // pending value through ScrollSpeedLadder without touching the session, then applies the final
        // value here in one absolute call on commit — unlike StepScrollSpeed's relative ladder step.
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Palette()));
        session.AddLayer(); // Layer 1: display (collision:false, scroll:1.0, repeat:false)
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var result = session.SetScrollSpeed(1, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(result.LayerIndex, Is.EqualTo(1));
            Assert.That(session.Level.Layers[1].ScrollSpeed, Is.EqualTo(0.5f));
            Assert.That(session.CanUndo, Is.True, "property-set is index-stable — cell-edit history must survive.");
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_SetScrollSpeed_IsNoOpWhileCollisionIsOn()
    {
        var session = new LevelEditSession(SampleLevel()); // terrain: collision true

        var result = session.SetScrollSpeed(0, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.False);
            Assert.That(session.Level.Layers[0].ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_SetScrollSpeed_SameValue_IsNoOp()
    {
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Palette()));
        session.AddLayer(); // scroll defaults to 1.0

        var result = session.SetScrollSpeed(1, 1.0f);

        Assert.That(result.Happened, Is.False);
    }

    [Test]
    public void Session_StepScrollSpeedAndSetRepeat_AreNoOpsWhileCollisionIsOn()
    {
        var session = new LevelEditSession(SampleLevel()); // terrain: collision true

        var stepResult = session.StepScrollSpeed(0, +1);
        var repeatResult = session.SetRepeat(0, true);

        Assert.Multiple(() =>
        {
            Assert.That(stepResult.Happened, Is.False);
            Assert.That(repeatResult.Happened, Is.False);
            Assert.That(session.Level.Layers[0].ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(session.Level.Layers[0].Repeat, Is.False);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_RenameLayer_AppliesTrimmedName_IndexStable_PreservesHistory_MarksDirty()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var result = session.RenameLayer(0, "  backdrop  ");

        Assert.Multiple(() =>
        {
            Assert.That(result.Happened, Is.True);
            Assert.That(result.LayerIndex, Is.EqualTo(0));
            Assert.That(session.Level.Layers[0].Name, Is.EqualTo("backdrop"), "the name must be trimmed.");
            Assert.That(session.CanUndo, Is.True, "rename is index-stable — cell-edit history must survive.");
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_RenameLayer_BlankOrWhitespaceOnly_IsNoOp_DoesNotMarkDirty()
    {
        var session = new LevelEditSession(SampleLevel());

        var blankResult = session.RenameLayer(0, "");
        var whitespaceResult = session.RenameLayer(0, "   ");

        Assert.Multiple(() =>
        {
            Assert.That(blankResult.Happened, Is.False);
            Assert.That(whitespaceResult.Happened, Is.False);
            Assert.That(session.Level.Layers[0].Name, Is.EqualTo("terrain"));
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_RenameLayer_OutOfRangeIndex_Throws()
    {
        var session = new LevelEditSession(SampleLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RenameLayer(5, "x"));
    }

    [Test]
    public void Session_LayerOps_SaveLoadRoundTrip_PreservesLayersPropsAndOrder()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildSamplePackageBytes()));
        var session = new LevelEditSession(EditableLevelReader.FromPackage(package));
        session.AddLayer();
        session.SetCollision(1, false);
        session.StepScrollSpeed(1, -1); // 1.0 -> 0.75
        session.SetRepeat(1, true);
        session.MoveLayer(1, -1); // backdrop now drawn first (back)

        var reloaded = EditableLevelReader.FromPackageBytes(session.Save(package));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Layers, Has.Count.EqualTo(2));
            Assert.That(reloaded.Layers[0].Name, Is.EqualTo("Layer 1"));
            Assert.That(reloaded.Layers[0].Collision, Is.False);
            Assert.That(reloaded.Layers[0].ScrollSpeed, Is.EqualTo(0.75f));
            Assert.That(reloaded.Layers[0].Repeat, Is.True);
            Assert.That(reloaded.Layers[1].Name, Is.EqualTo("terrain"));
            Assert.That(reloaded.Layers[1].Collision, Is.True);
        });
    }

    // ----- MenuOutcome.OpenLayerManager routing -----

    [Test]
    public void MenuOutcome_OpenLayerManager_CarriesTheRightKind()
    {
        var outcome = MenuOutcome.OpenLayerManager();
        Assert.That(outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenLayerManager));
    }

    [Test]
    public void MenuModel_ManageWedge_ResolvesToOpenLayerManager()
    {
        var menu = new MenuModel("Layers", new[]
        {
            new MenuItem("terrain", MenuOutcome.SelectLayer(0)),
            new MenuItem("Manage…", MenuOutcome.OpenLayerManager()),
        });

        var resolved = menu.Resolve(0, 1); // wedge 1 of 2 (bottom)

        Assert.That(resolved.HasValue, Is.True);
        Assert.That(resolved!.Value.Kind, Is.EqualTo(MenuOutcomeKind.OpenLayerManager));
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full),
        new EditableTile(5, WaterPath, Encoding.UTF8.GetBytes("WATER-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None),
    };

    private static EditableLevel SampleLevel()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { layer }, new Dictionary<ResourcePath, string>());
    }

    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full } },
        };
        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, Cells = cells },
            },
        };

        var builder = new PackageBuilder().WithName("Demo Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, Content.Json.LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, Content.Json.LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
