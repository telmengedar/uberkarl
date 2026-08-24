namespace Uberkarl.Editor;

/// <summary>A grid-rect in cell units, as the two-corner trigger tool computes it before it becomes an <see cref="Content.AreaTriggerDefinition"/>.</summary>
public readonly record struct TriggerRect(int X, int Y, int Width, int Height);
