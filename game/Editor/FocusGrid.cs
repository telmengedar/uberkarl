using System;
using System.Collections.Generic;
using Godot;

namespace Uberkarl {

    /// <summary>
    /// Builds spatial 2D focus-neighbour wiring for a summoned panel's control layout: within a row,
    /// left/right steps to the adjacent column; across rows, up/down steps to the same column (clamped to
    /// the target row's width) in the row above/below. Every edge of the grid pins to itself — top row up,
    /// bottom row down, row-start left, row-end right, plus <c>FocusNext</c>/<c>FocusPrevious</c> — so a
    /// stick/D-pad aim or a keyboard Tab can never bounce focus off the panel onto whatever sits
    /// underneath it (the contained-focus-chain convention <see cref="PackageBrowser"/> already uses for
    /// its single-column list, generalised to two dimensions).
    ///
    /// Toni's playtest fix (DiVoid #7512): a directional press should land on the control that is actually
    /// in that on-screen direction, not the next stop in one long flat chain — this is that spatial
    /// neighbour wiring. <see cref="LayerManagerPanel"/> is the first (and, per task scope, only) caller;
    /// the shape is reusable by any future summoned panel laid out as rows of controls (the package
    /// browser, #7470, is the noted next adopter).
    /// </summary>
    public static class FocusGrid {

        /// <summary>
        /// Wires <paramref name="rows"/> (each entry the ordered controls of one on-screen row, top to
        /// bottom) into a contained spatial grid. A row may have fewer columns than its neighbours — the
        /// vertical step clamps to the shortest of the two rows' widths, so e.g. a single-control header
        /// row above a multi-column data row still gets a well-defined "down" target.
        /// </summary>
        public static void Contain(IReadOnlyList<IReadOnlyList<Control>> rows) {
            NodePath self = new NodePath(".");
            for (int r = 0; r < rows.Count; r++) {
                IReadOnlyList<Control> row = rows[r];
                for (int c = 0; c < row.Count; c++) {
                    Control control = row[c];
                    control.FocusNeighborLeft = c > 0 ? control.GetPathTo(row[c - 1]) : self;
                    control.FocusNeighborRight = c < row.Count - 1 ? control.GetPathTo(row[c + 1]) : self;
                    control.FocusNeighborTop = r > 0 ? control.GetPathTo(ColumnNeighbor(rows[r - 1], c)) : self;
                    control.FocusNeighborBottom = r < rows.Count - 1 ? control.GetPathTo(ColumnNeighbor(rows[r + 1], c)) : self;
                    control.FocusNext = self;
                    control.FocusPrevious = self;
                }
            }
        }

        static Control ColumnNeighbor(IReadOnlyList<Control> row, int column) =>
            row[Math.Min(column, row.Count - 1)];
    }
}
