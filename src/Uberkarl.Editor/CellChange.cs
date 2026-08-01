namespace Uberkarl.Editor;

/// <summary>
/// Describes one grid cell that changed value, so the UI/canvas can update exactly that cell rather
/// than re-rendering the whole level. Returned by paint/erase/undo/redo on the session: a <c>null</c>
/// result means nothing changed (e.g. painting a cell that already holds the target tile).
/// </summary>
public readonly record struct CellChange(int LayerIndex, int X, int Y, int TileId);
