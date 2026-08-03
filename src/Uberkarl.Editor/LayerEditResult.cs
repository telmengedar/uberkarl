namespace Uberkarl.Editor;

/// <summary>
/// The result of a <see cref="LevelEditSession"/> layer-management intent (add/delete/move/property-set).
/// Unlike a cell edit (<see cref="CellChange"/>, one cell on one layer) a layer operation has no single
/// cell to report — instead it reports whether the operation actually happened (some are no-op-safe: a
/// blocked delete-last, a move at the end, a property set that changes nothing) and the affected layer's
/// resulting index, so the controller can reconcile which layer is active after a delete or reorder.
/// </summary>
public readonly record struct LayerEditResult(bool Happened, int LayerIndex);
