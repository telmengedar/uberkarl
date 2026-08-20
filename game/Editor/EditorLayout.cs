using Godot;

namespace Uberkarl {

    /// <summary>The sanctioned layout helpers for a summoned <see cref="Control"/>.</summary>
    static class EditorLayout {

        /// <summary>Anchors and sizes <paramref name="control"/> to its parent's full rect, independent of
        /// whether it is already parented or of its own current size when this runs.</summary>
        public static void FillParent(Control control) =>
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        /// <summary>Anchors, offsets, and grows <paramref name="control"/> so it sits centered on its
        /// parent, independent of its size or of parenting order when this runs.</summary>
        public static void CenterInParent(Control control) {
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
            control.GrowHorizontal = Control.GrowDirection.Both;
            control.GrowVertical = Control.GrowDirection.Both;
        }

        /// <summary>Anchors and sizes <paramref name="control"/> to a top strip <paramref name="height"/>
        /// pixels tall spanning the parent's full width, using the anchors-and-offsets preset so it is
        /// safe regardless of parenting order.</summary>
        public static void PinTop(Control control, float height) {
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
            control.OffsetBottom = height;
        }
    }
}
