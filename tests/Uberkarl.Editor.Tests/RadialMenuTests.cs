using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the engine-agnostic pop-in menu core: the radial geometry (which wedge a direction aims at, the
/// centre dead-zone) and the menu model that turns an aim into an editor intent ("menu → action routing").
/// None of this touches Godot — it is exactly the logic the pop-in surface delegates to, so wedge bucketing
/// and the wedge-to-outcome mapping are pinned down independent of any device or the engine.
/// </summary>
[TestFixture]
public sealed class RadialMenuTests
{
    private const double Deadzone = 0.35;

    // ----- radial geometry: direction -> wedge -----

    [Test]
    public void Geometry_CardinalDirections_MapToClockwiseWedges()
    {
        // Four wedges, clockwise from the top: up=0, right=1, down=2, left=3.
        Assert.Multiple(() =>
        {
            Assert.That(RadialGeometry.IndexAt(0, -1, 4, Deadzone), Is.EqualTo(0), "up");
            Assert.That(RadialGeometry.IndexAt(1, 0, 4, Deadzone), Is.EqualTo(1), "right");
            Assert.That(RadialGeometry.IndexAt(0, 1, 4, Deadzone), Is.EqualTo(2), "down");
            Assert.That(RadialGeometry.IndexAt(-1, 0, 4, Deadzone), Is.EqualTo(3), "left");
        });
    }

    [Test]
    public void Geometry_WithinDeadzone_SelectsNothing()
    {
        Assert.That(RadialGeometry.IndexAt(0, 0, 4, Deadzone), Is.EqualTo(-1));
        Assert.That(RadialGeometry.IndexAt(0.1, -0.1, 4, Deadzone), Is.EqualTo(-1), "short aim stays neutral");
    }

    [Test]
    public void Geometry_EmptyMenu_SelectsNothing()
    {
        Assert.That(RadialGeometry.IndexAt(0, -1, 0, Deadzone), Is.EqualTo(-1));
    }

    [Test]
    public void Geometry_SingleWedge_AlwaysThatWedge()
    {
        Assert.That(RadialGeometry.IndexAt(0, -1, 1, Deadzone), Is.EqualTo(0));
        Assert.That(RadialGeometry.IndexAt(-1, 0, 1, Deadzone), Is.EqualTo(0));
    }

    [Test]
    public void Geometry_AngleWrapsAtTheTop()
    {
        // A direction a hair to the left of straight-up must still land on wedge 0, not wrap to the last.
        Assert.That(RadialGeometry.IndexAt(-0.05, -1, 8, Deadzone), Is.EqualTo(0));
        Assert.That(RadialGeometry.IndexAt(0.05, -1, 8, Deadzone), Is.EqualTo(0));
    }

    [Test]
    public void Geometry_WedgeDirection_RoundTripsToItsIndex()
    {
        for (int count = 1; count <= 8; count++)
        {
            for (int i = 0; i < count; i++)
            {
                (double dx, double dy) = RadialGeometry.WedgeDirection(i, count);
                Assert.That(RadialGeometry.IndexAt(dx, dy, count, Deadzone), Is.EqualTo(i),
                    $"wedge {i} of {count} must resolve back to itself");
            }
        }
    }

    // ----- menu model: aim -> outcome (routing) -----

    [Test]
    public void Model_ResolvesAimToTheCorrectTileOutcome()
    {
        var tiles = new RadialMenuModel("Tiles", new[]
        {
            new RadialMenuItem("a", MenuOutcome.SelectTile(0)),
            new RadialMenuItem("b", MenuOutcome.SelectTile(1)),
            new RadialMenuItem("c", MenuOutcome.SelectTile(2)),
            new RadialMenuItem("d", MenuOutcome.SelectTile(3)),
        });

        MenuOutcome? up = tiles.Resolve(0, -1);
        Assert.That(up.HasValue, Is.True);
        Assert.That(up!.Value.Kind, Is.EqualTo(MenuOutcomeKind.SelectTile));
        Assert.That(up.Value.Index, Is.EqualTo(0));

        MenuOutcome? right = tiles.Resolve(1, 0);
        Assert.That(right!.Value.Index, Is.EqualTo(1));
    }

    [Test]
    public void Model_NeutralCentre_ResolvesToNoOutcome()
    {
        var tiles = new RadialMenuModel("Tiles", new[]
        {
            new RadialMenuItem("a", MenuOutcome.SelectTile(0)),
            new RadialMenuItem("b", MenuOutcome.SelectTile(1)),
        });

        Assert.That(tiles.Resolve(0, 0), Is.Null);
    }

    [Test]
    public void Model_RoutesEveryWedgeToItsOwnOutcome()
    {
        // The "Actions" menu mixes file commands and named actions; every wedge must route to its own intent.
        var actions = new RadialMenuModel("Actions", new[]
        {
            new RadialMenuItem("New", MenuOutcome.FileOp(EditorFileCommand.New)),
            new RadialMenuItem("Open", MenuOutcome.FileOp(EditorFileCommand.Open)),
            new RadialMenuItem("Save", MenuOutcome.FileOp(EditorFileCommand.Save)),
            new RadialMenuItem("Save As", MenuOutcome.FileOp(EditorFileCommand.SaveAs)),
            new RadialMenuItem("Undo", MenuOutcome.Invoke(EditorAction.Undo)),
            new RadialMenuItem("Redo", MenuOutcome.Invoke(EditorAction.Redo)),
        });

        for (int i = 0; i < actions.Count; i++)
        {
            (double dx, double dy) = RadialGeometry.WedgeDirection(i, actions.Count);
            MenuOutcome? resolved = actions.Resolve(dx, dy);
            Assert.That(resolved.HasValue, Is.True, $"wedge {i} must resolve");
            Assert.That(resolved!.Value, Is.EqualTo(actions.Items[i].Outcome),
                $"wedge {i} must route to its own outcome");
        }
    }

    [Test]
    public void Model_LayerAndActionOutcomes_CarryTheRightPayload()
    {
        Assert.That(MenuOutcome.SelectLayer(2).Kind, Is.EqualTo(MenuOutcomeKind.SelectLayer));
        Assert.That(MenuOutcome.SelectLayer(2).Index, Is.EqualTo(2));
        Assert.That(MenuOutcome.Invoke(EditorAction.ToggleTool).Action, Is.EqualTo(EditorAction.ToggleTool));
        Assert.That(MenuOutcome.FileOp(EditorFileCommand.SaveAs).File, Is.EqualTo(EditorFileCommand.SaveAs));
    }

    [Test]
    public void Model_SelectObjectTypeOutcome_CarriesTheRightPayload()
    {
        Assert.That(MenuOutcome.SelectObjectType(3).Kind, Is.EqualTo(MenuOutcomeKind.SelectObjectType));
        Assert.That(MenuOutcome.SelectObjectType(3).Index, Is.EqualTo(3));
    }

    [Test]
    public void Model_OutcomeAt_IsBoundsChecked()
    {
        var menu = new RadialMenuModel("Tiles", new[] { new RadialMenuItem("a", MenuOutcome.SelectTile(0)) });
        Assert.That(menu.OutcomeAt(0).HasValue, Is.True);
        Assert.That(menu.OutcomeAt(-1), Is.Null);
        Assert.That(menu.OutcomeAt(5), Is.Null);
    }

    [Test]
    public void Model_NullItems_IsAnEmptyMenu_ThatKeepsItsTitle()
    {
        var menu = new RadialMenuModel("Empty", null!);
        Assert.That(menu.Title, Is.EqualTo("Empty"));
        Assert.That(menu.Count, Is.EqualTo(0));
        Assert.That(menu.Resolve(0, -1), Is.Null);
        Assert.That(menu.IndexAt(0, -1), Is.EqualTo(-1));
    }

    [Test]
    public void Item_ExposesItsLabelAndOutcome()
    {
        var item = new RadialMenuItem("grass", MenuOutcome.SelectTile(4));
        Assert.That(item.Label, Is.EqualTo("grass"));
        Assert.That(item.Outcome.Index, Is.EqualTo(4));
    }

    [Test]
    public void Geometry_WedgeCenterAngle_IsZeroForAnEmptyMenu()
    {
        Assert.That(RadialGeometry.WedgeCenterAngle(0, 0), Is.EqualTo(0.0));
    }

    private const double InnerRadius = 30.0;
    private const double OuterRadius = 126.0;

    [Test]
    public void PositionalIndexAt_CardinalDirections_MapToTheSameClockwiseWedges_AsIndexAt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RadialGeometry.PositionalIndexAt(0, -80, 4, InnerRadius, OuterRadius), Is.EqualTo(0), "up");
            Assert.That(RadialGeometry.PositionalIndexAt(80, 0, 4, InnerRadius, OuterRadius), Is.EqualTo(1), "right");
            Assert.That(RadialGeometry.PositionalIndexAt(0, 80, 4, InnerRadius, OuterRadius), Is.EqualTo(2), "down");
            Assert.That(RadialGeometry.PositionalIndexAt(-80, 0, 4, InnerRadius, OuterRadius), Is.EqualTo(3), "left");
        });
    }

    [Test]
    public void PositionalIndexAt_InsideInnerRadius_SelectsNothing()
    {
        Assert.That(RadialGeometry.PositionalIndexAt(0, -10, 4, InnerRadius, OuterRadius), Is.EqualTo(-1));
        Assert.That(RadialGeometry.PositionalIndexAt(0, 0, 4, InnerRadius, OuterRadius), Is.EqualTo(-1));
    }

    [Test]
    public void PositionalIndexAt_OutsideOuterRadius_SelectsNothing_SoAClickThereCancels()
    {
        Assert.That(RadialGeometry.PositionalIndexAt(0, -500, 4, InnerRadius, OuterRadius), Is.EqualTo(-1));
    }

    [Test]
    public void PositionalIndexAt_ExactlyOnTheBoundaries_IsInclusive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RadialGeometry.PositionalIndexAt(0, -InnerRadius, 4, InnerRadius, OuterRadius),
                Is.EqualTo(0), "exactly at the inner radius counts as inside.");
            Assert.That(RadialGeometry.PositionalIndexAt(0, -OuterRadius, 4, InnerRadius, OuterRadius),
                Is.EqualTo(0), "exactly at the outer radius counts as inside.");
        });
    }

    [Test]
    public void PositionalIndexAt_EmptyMenu_SelectsNothing()
    {
        Assert.That(RadialGeometry.PositionalIndexAt(0, -80, 0, InnerRadius, OuterRadius), Is.EqualTo(-1));
    }

    [Test]
    public void RadialHighlight_FromUnhighlighted_Forward_LandsOnTheFirstWedge()
    {
        Assert.That(RadialHighlight.Step(highlighted: -1, count: 8, direction: +1), Is.EqualTo(0));
    }

    [Test]
    [Description("Plain CyclicSelection.Prev(-1, 8) computes 6, treating -1 as a valid index one short of 0 rather than as \"nothing highlighted\", silently skipping the last wedge.")]
    public void RadialHighlight_FromUnhighlighted_Backward_LandsOnTheLastWedge()
    {
        Assert.That(RadialHighlight.Step(highlighted: -1, count: 8, direction: -1), Is.EqualTo(7));
    }

    [Test]
    public void RadialHighlight_FromAHighlightedWedge_DelegatesToCyclicSelection()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RadialHighlight.Step(highlighted: 2, count: 8, direction: +1), Is.EqualTo(3));
            Assert.That(RadialHighlight.Step(highlighted: 0, count: 8, direction: -1), Is.EqualTo(7));
        });
    }

    [Test]
    public void RadialHighlight_EmptyMenu_SelectsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RadialHighlight.Step(highlighted: -1, count: 0, direction: +1), Is.EqualTo(-1));
            Assert.That(RadialHighlight.Step(highlighted: -1, count: 0, direction: -1), Is.EqualTo(-1));
        });
    }
}
