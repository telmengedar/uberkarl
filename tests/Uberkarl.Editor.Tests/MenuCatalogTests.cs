using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the engine-agnostic menu catalog: the pure builders that turn primitive editor state into a
/// <see cref="MenuModel"/>, so the Actions menu's size and the Tiles menu's three-segment concatenation
/// are unit-tested facts rather than untested Godot glue.
/// </summary>
[TestFixture]
public sealed class MenuCatalogTests
{
    [Test]
    [Description("Tiles occupy indices [0, 2], terrains [3, 4], objects [5, 7] — pins first/last of each segment plus both seams.")]
    public void BuildTilesMenu_FirstAndLastOfEachSegment_AndBothSeams_LandAtTheirExactIndices()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            paletteTileIds: new[] { 10, 11, 12 },
            paletteTerrainLabels: new[] { "grass", "water" },
            objectTypeLabels: new[] { "crate", "torch", "chest" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(8));

            Assert.That(menu.Items[0].Outcome, Is.EqualTo(MenuOutcome.SelectTile(0)), "first tile");
            Assert.That(menu.Items[2].Outcome, Is.EqualTo(MenuOutcome.SelectTile(2)), "last tile");

            Assert.That(menu.Items[3].Outcome, Is.EqualTo(MenuOutcome.SelectTerrain(0)), "seam 1: tiles -> terrains, first terrain");
            Assert.That(menu.Items[4].Outcome, Is.EqualTo(MenuOutcome.SelectTerrain(1)), "last terrain");

            Assert.That(menu.Items[5].Outcome, Is.EqualTo(MenuOutcome.SelectObjectType(0)), "seam 2: terrains -> objects, first object");
            Assert.That(menu.Items[7].Outcome, Is.EqualTo(MenuOutcome.SelectObjectType(2)), "last object");
        });
    }

    [Test]
    public void BuildTilesMenu_Labels_MatchTheWheelsOrderAndPrefixes()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            paletteTileIds: new[] { 4 },
            paletteTerrainLabels: new[] { "sand" },
            objectTypeLabels: new[] { "lever" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Title, Is.EqualTo("Tiles"));
            Assert.That(menu.Items[0].Label, Is.EqualTo("#4"));
            Assert.That(menu.Items[1].Label, Is.EqualTo("Terrain: sand"));
            Assert.That(menu.Items[2].Label, Is.EqualTo("Object: lever"));
        });
    }

    [Test]
    public void BuildTilesMenu_EmptySegments_ProduceNoGapInTheFlatList()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            paletteTileIds: new[] { 1, 2 },
            paletteTerrainLabels: System.Array.Empty<string>(),
            objectTypeLabels: new[] { "door" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(3));
            Assert.That(menu.Items[1].Outcome, Is.EqualTo(MenuOutcome.SelectTile(1)), "last tile");
            Assert.That(menu.Items[2].Outcome, Is.EqualTo(MenuOutcome.SelectObjectType(0)), "object immediately follows, no terrain gap");
        });
    }

    [Test]
    [Description("QA #8605 M9: an empty tile segment must not suppress the terrain segment. Terrains stand on their own, independent of whether any tiles precede them.")]
    public void BuildTilesMenu_EmptyTiles_StillEmitsTerrainsAndObjects()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            paletteTileIds: System.Array.Empty<int>(),
            paletteTerrainLabels: new[] { "grass", "water" },
            objectTypeLabels: new[] { "crate" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(3));
            Assert.That(menu.Items[0].Outcome, Is.EqualTo(MenuOutcome.SelectTerrain(0)), "terrain segment starts at 0 with no tile segment ahead of it");
            Assert.That(menu.Items[1].Outcome, Is.EqualTo(MenuOutcome.SelectTerrain(1)), "last terrain");
            Assert.That(menu.Items[2].Outcome, Is.EqualTo(MenuOutcome.SelectObjectType(0)), "object immediately follows the terrain segment");
        });
    }

    [Test]
    public void BuildTilesMenu_EmptyObjects_EndsOnTheTerrainSegment()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            paletteTileIds: new[] { 5 },
            paletteTerrainLabels: new[] { "lava" },
            objectTypeLabels: System.Array.Empty<string>());

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(2));
            Assert.That(menu.Items[0].Outcome, Is.EqualTo(MenuOutcome.SelectTile(0)), "tile segment");
            Assert.That(menu.Items[1].Outcome, Is.EqualTo(MenuOutcome.SelectTerrain(0)), "terrain segment is the last one present, no trailing object gap");
        });
    }

    [Test]
    public void BuildTilesMenu_AllEmpty_IsAnEmptyMenu_ThatKeepsItsTitle()
    {
        MenuModel menu = MenuCatalog.BuildTilesMenu(
            System.Array.Empty<int>(), System.Array.Empty<string>(), System.Array.Empty<string>());

        Assert.That(menu.Title, Is.EqualTo("Tiles"));
        Assert.That(menu.Count, Is.EqualTo(0));
    }

    [Test]
    [Description("A thirty-tile package (design #8525 §11 U3 acceptance) must build as one flat, unbroken segment with no cap — the whole point of the list surface.")]
    public void BuildTilesMenu_ThirtyTiles_BuildsOneUncappedSegment()
    {
        int[] tileIds = new int[30];
        for (int i = 0; i < tileIds.Length; i++)
            tileIds[i] = i;

        MenuModel menu = MenuCatalog.BuildTilesMenu(
            tileIds, System.Array.Empty<string>(), System.Array.Empty<string>());

        Assert.That(menu.Count, Is.EqualTo(30));
        Assert.That(menu.Items[29].Outcome, Is.EqualTo(MenuOutcome.SelectTile(29)));
    }

    [Test]
    public void BuildLayersMenu_OneEntryPerLayer_PlusATrailingManageEntry()
    {
        MenuModel menu = MenuCatalog.BuildLayersMenu(new[] { "Background", "Ground", "Foreground" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(4));
            Assert.That(menu.Items[0].Outcome, Is.EqualTo(MenuOutcome.SelectLayer(0)));
            Assert.That(menu.Items[2].Outcome, Is.EqualTo(MenuOutcome.SelectLayer(2)), "last layer");
            Assert.That(menu.Items[3].Label, Is.EqualTo("Manage…"));
            Assert.That(menu.Items[3].Outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenLayerManager));
        });
    }

    [Test]
    public void BuildLayersMenu_NoLayers_StillOffersManage_AndKeepsItsTitle()
    {
        MenuModel menu = MenuCatalog.BuildLayersMenu(System.Array.Empty<string>());

        Assert.Multiple(() =>
        {
            Assert.That(menu.Title, Is.EqualTo("Layers"));
            Assert.That(menu.Count, Is.EqualTo(1));
            Assert.That(menu.Items[0].Outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenLayerManager));
        });
    }

    [Test]
    [Description("The single-layer boundary: the layer segment is exactly one entry wide, so its first and last are the same row, immediately followed by the Manage seam.")]
    public void BuildLayersMenu_OneLayer_LayerRowAndManageSeamBothLandAtTheirExactIndices()
    {
        MenuModel menu = MenuCatalog.BuildLayersMenu(new[] { "Ground" });

        Assert.Multiple(() =>
        {
            Assert.That(menu.Count, Is.EqualTo(2));
            Assert.That(menu.Items[0].Label, Is.EqualTo("Ground"));
            Assert.That(menu.Items[0].Outcome, Is.EqualTo(MenuOutcome.SelectLayer(0)));
            Assert.That(menu.Items[1].Label, Is.EqualTo("Manage…"));
            Assert.That(menu.Items[1].Outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenLayerManager));
        });
    }

    [Test]
    [Description("QA #8616 W8: pins each surviving (Label, Outcome) pair in order, so a rebind, drop or reorder goes red -- a builder compared against itself cannot.")]
    public void BuildActionsMenu_LabelsAndOutcomes_MatchThePinnedMapping_InOrder()
    {
        (string Label, MenuOutcome Outcome)[] expected =
        {
            ("Open", MenuOutcome.FileOp(EditorFileCommand.Open)),
            ("Save", MenuOutcome.FileOp(EditorFileCommand.Save)),
            ("Undo", MenuOutcome.Invoke(EditorAction.Undo)),
            ("Redo", MenuOutcome.Invoke(EditorAction.Redo)),
            ("Tool", MenuOutcome.Invoke(EditorAction.ToggleTool)),
            ("Play", MenuOutcome.Invoke(EditorAction.Playtest)),
            ("More…", MenuOutcome.OpenActionsOverflow()),
        };

        MenuModel menu = MenuCatalog.BuildActionsMenu();

        Assert.That(menu.Title, Is.EqualTo("Actions"), "title is displayed data, not derivable from the entry count");
        Assert.That(menu.Count, Is.EqualTo(expected.Length), "entry count");

        Assert.That(menu.Items[6].Outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenActionsOverflow),
            "the More... wedge must route to the overflow, not merely be data-equal to whatever the factory returns");

        Assert.Multiple(() =>
        {
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(menu.Items[i].Label, Is.EqualTo(expected[i].Label), $"entry {i} label");
                Assert.That(menu.Items[i].Outcome, Is.EqualTo(expected[i].Outcome), $"entry {i} outcome");
            }
        });
    }

    [Test]
    [Description("DiVoid #8525 §11 U5 / #8628: the entries the radial trim moved off the wheel, reached through Actions' \"More...\" entry and rendered on the list surface so none of them becomes unreachable on a gamepad with no keyboard and no mouse -- only the gesture count changes. Pins each (Label, Outcome) pair against the pre-trim BuildActionsMenu entries they were moved from, in order, so a rebind, drop, or reorder goes red.")]
    public void BuildActionsOverflowMenu_LabelsAndOutcomes_MatchThePinnedMapping_InOrder()
    {
        (string Label, MenuOutcome Outcome)[] expected =
        {
            ("New", MenuOutcome.FileOp(EditorFileCommand.New)),
            ("Save As", MenuOutcome.FileOp(EditorFileCommand.SaveAs)),
            ("Resize…", MenuOutcome.OpenResizePanel()),
            ("Edit Tileset…", MenuOutcome.OpenTileSetEditor()),
            ("Bind Tileset…", MenuOutcome.OpenTileSetBindPanel()),
            ("Level Script…", MenuOutcome.AssignLevelScriptBehavior()),
        };

        MenuModel menu = MenuCatalog.BuildActionsOverflowMenu();

        Assert.That(menu.Title, Is.EqualTo("More"), "title is displayed data, not derivable from the entry count");
        Assert.That(menu.Count, Is.EqualTo(expected.Length), "entry count");

        Assert.Multiple(() =>
        {
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(menu.Items[i].Label, Is.EqualTo(expected[i].Label), $"entry {i} label");
                Assert.That(menu.Items[i].Outcome, Is.EqualTo(expected[i].Outcome), $"entry {i} outcome");
            }
        });
    }

    [Test]
    [Description("DiVoid #8628: Actions must fit MenuCatalog.RadialCap -- a regrowth past 8 is caught here as well as by EnforceRadialCap.")]
    public void BuildActionsMenu_FitsTheRadialCap_AfterU5Trim()
    {
        MenuModel menu = MenuCatalog.BuildActionsMenu();

        Assert.That(menu.Count, Is.EqualTo(7), "pinned post-U5 entry count -- if this just went red, read this test's [Description]");
        Assert.That(menu.Count, Is.LessThanOrEqualTo(MenuCatalog.RadialCap),
            $"Actions ({menu.Count} entries) must fit the radial cap ({MenuCatalog.RadialCap}).");
    }

    [Test]
    [Description("DiVoid #8628: the cap refusal itself needs a guard, pinned at its boundary. Eight entries -- exactly MenuCatalog.RadialCap -- is the largest menu the radial accepts, and must not be refused.")]
    public void EnforceRadialCap_EightEntries_DoesNotThrow()
    {
        MenuItem[] eightItems =
        {
            new MenuItem("1", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("2", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("3", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("4", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("5", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("6", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("7", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("8", MenuOutcome.Invoke(EditorAction.Undo)),
        };
        MenuModel menu = new MenuModel("Eight", eightItems);

        Assert.That(menu.Count, Is.EqualTo(MenuCatalog.RadialCap), "test fixture sanity: exactly at the cap");
        Assert.DoesNotThrow(() => MenuCatalog.EnforceRadialCap(menu));
    }

    [Test]
    [Description("DiVoid #8628: nine entries -- one past MenuCatalog.RadialCap -- is the smallest menu the radial must refuse, and must be refused loudly (a thrown exception) rather than silently truncated or rendered.")]
    public void EnforceRadialCap_NineEntries_ThrowsRatherThanSilentlyTruncating()
    {
        MenuItem[] nineItems =
        {
            new MenuItem("1", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("2", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("3", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("4", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("5", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("6", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("7", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("8", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("9", MenuOutcome.Invoke(EditorAction.Undo)),
        };
        MenuModel menu = new MenuModel("Nine", nineItems);

        var exception = Assert.Throws<System.ArgumentException>(() => MenuCatalog.EnforceRadialCap(menu));
        Assert.That(exception!.Message, Does.Contain("9"));
        Assert.That(exception.Message, Does.Contain("Nine"));
    }
}
