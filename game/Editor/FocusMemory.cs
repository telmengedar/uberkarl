using System;
using System.Collections.Generic;
using Godot;

namespace Uberkarl {

    /// <summary>Records a control grid's focus position across a rebuild and restores it, clamped to the new bounds.</summary>
    public sealed class FocusMemory {

        int lastRow;
        int lastCol;

        /// <summary>Resets the remembered position to the grid's origin.</summary>
        public void Reset() {
            lastRow = 0;
            lastCol = 0;
        }

        /// <summary>Subscribes every control in <paramref name="rows"/> to record its (row, column) position whenever it gains focus.</summary>
        public void Track(IReadOnlyList<IReadOnlyList<Control>> rows) {
            for (int r = 0; r < rows.Count; r++) {
                for (int c = 0; c < rows[r].Count; c++) {
                    int capturedRow = r;
                    int capturedCol = c;
                    rows[r][c].FocusEntered += () => {
                        lastRow = capturedRow;
                        lastCol = capturedCol;
                    };
                }
            }
        }

        /// <summary>Clamps the remembered position into <paramref name="rows"/>'s current bounds and grabs focus there, deferred. No-op when <paramref name="rows"/> is empty.</summary>
        public void Restore(IReadOnlyList<IReadOnlyList<Control>> rows) {
            if (rows.Count == 0)
                return;

            int restoreRow = Math.Clamp(lastRow, 0, rows.Count - 1);
            int restoreCol = Math.Clamp(lastCol, 0, rows[restoreRow].Count - 1);
            rows[restoreRow][restoreCol].CallDeferred(Control.MethodName.GrabFocus);
        }
    }
}
