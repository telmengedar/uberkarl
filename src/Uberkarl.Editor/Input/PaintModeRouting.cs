using System;

namespace Uberkarl.Editor.Input;

/// <summary>The editor's paint-mode vocabulary — what a cell press/erase currently means.</summary>
public enum PaintModeKind
{
    /// <summary>Painting/erasing a concrete tile id.</summary>
    Tile,

    /// <summary>Painting/erasing a logical terrain (DiVoid #7551 Phase 3).</summary>
    Terrain,

    /// <summary>Placing/erasing a free-moving object.</summary>
    Object,

    /// <summary>Placing/erasing a two-corner area trigger (design #8049 M4b).</summary>
    TriggerRect,
}

/// <summary>What a cell press/erase should do, resolved from the active <see cref="PaintModeKind"/> (design #8049 M4b routing matrix).</summary>
public enum CellRoutingDecision
{
    /// <summary>Paint/erase the active concrete tile.</summary>
    PaintTile,

    /// <summary>Paint/erase the active terrain.</summary>
    PaintTerrain,

    /// <summary>Place the active object type.</summary>
    PlaceObject,

    /// <summary>Erase the concrete tile or terrain paint at the cell (Tile/Terrain modes share one erase call).</summary>
    EraseCell,

    /// <summary>Erase the object occupying the cell, if any.</summary>
    EraseObject,

    /// <summary>Press the two-corner trigger tool — starts or completes a rect, per <see cref="Editor.TriggerRectTool.Press"/>.</summary>
    TriggerRectPress,

    /// <summary>Cancel a pending trigger first corner (the erase action interrupts an in-progress placement).</summary>
    CancelTriggerCorner,

    /// <summary>Erase the trigger occupying the cell, if any (no corner pending).</summary>
    EraseTrigger,
}

/// <summary>Resolves a cell press/erase into a <see cref="CellRoutingDecision"/> from the active <see cref="PaintModeKind"/> alone — no Godot type, no session, no mutation.</summary>
public static class PaintModeRouting
{
    /// <summary>What the primary action (mouse click, or the paint action at the grid cursor, with the Paint tool active) does in <paramref name="mode"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not one of the declared <see cref="PaintModeKind"/> members.</exception>
    public static CellRoutingDecision ResolvePress(PaintModeKind mode) => mode switch
    {
        PaintModeKind.Tile => CellRoutingDecision.PaintTile,
        PaintModeKind.Terrain => CellRoutingDecision.PaintTerrain,
        PaintModeKind.Object => CellRoutingDecision.PlaceObject,
        PaintModeKind.TriggerRect => CellRoutingDecision.TriggerRectPress,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"{nameof(PaintModeRouting)}.{nameof(ResolvePress)} has no routing for '{mode}'."),
    };

    /// <summary>What the erase action does in <paramref name="mode"/> — reached either via the Erase tool's primary action or the dedicated erase action, regardless of tool. <paramref name="triggerCornerPending"/> only matters for <see cref="PaintModeKind.TriggerRect"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not one of the declared <see cref="PaintModeKind"/> members.</exception>
    public static CellRoutingDecision ResolveErase(PaintModeKind mode, bool triggerCornerPending) => mode switch
    {
        PaintModeKind.Tile => CellRoutingDecision.EraseCell,
        PaintModeKind.Terrain => CellRoutingDecision.EraseCell,
        PaintModeKind.Object => CellRoutingDecision.EraseObject,
        PaintModeKind.TriggerRect => triggerCornerPending ? CellRoutingDecision.CancelTriggerCorner : CellRoutingDecision.EraseTrigger,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"{nameof(PaintModeRouting)}.{nameof(ResolveErase)} has no routing for '{mode}'."),
    };
}
