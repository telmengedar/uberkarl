using Godot;

namespace Uberkarl {

    /// <summary>
    /// Builds the editor's <see cref="Theme"/> in one place so every panel, toolbar, and list shares one
    /// consistent dark look — "a good UI is king". Built in code rather than a <c>.tres</c> for now so the
    /// palette lives beside the layout it styles; it can be promoted to a resource file later without
    /// touching call sites. Colours: a dark slate shell, lighter raised panels, and a single amber accent
    /// used for the active tool and selection.
    /// </summary>
    public static class EditorTheme {

        public static readonly Color Shell = new Color(0.10f, 0.11f, 0.13f);
        public static readonly Color Panel = new Color(0.16f, 0.17f, 0.20f);
        public static readonly Color PanelRaised = new Color(0.22f, 0.23f, 0.27f);
        public static readonly Color Accent = new Color(0.98f, 0.68f, 0.20f);
        public static readonly Color Text = new Color(0.90f, 0.91f, 0.94f);
        public static readonly Color TextDim = new Color(0.62f, 0.64f, 0.70f);

        public static Theme Build() {
            Theme theme = new Theme();

            StyleBoxFlat panel = Filled(Panel);
            panel.SetContentMarginAll(8);
            theme.SetStylebox("panel", "PanelContainer", panel);
            theme.SetStylebox("panel", "Panel", Filled(Panel));

            // Buttons: raised panel by default, accent when pressed/hovered.
            theme.SetStylebox("normal", "Button", Button(PanelRaised, 6));
            theme.SetStylebox("hover", "Button", Button(Lighten(PanelRaised, 0.06f), 6));
            theme.SetStylebox("pressed", "Button", Button(Accent, 6));
            theme.SetStylebox("focus", "Button", Outline(Accent, 6));
            theme.SetColor("font_color", "Button", Text);
            theme.SetColor("font_pressed_color", "Button", Shell);
            theme.SetColor("font_hover_color", "Button", Text);
            theme.SetConstant("h_separation", "HBoxContainer", 6);
            theme.SetConstant("separation", "VBoxContainer", 6);

            // Item lists (palette + layer selector).
            StyleBoxFlat listBg = Filled(new Color(0.12f, 0.13f, 0.16f));
            listBg.SetContentMarginAll(4);
            theme.SetStylebox("panel", "ItemList", listBg);
            theme.SetStylebox("focus", "ItemList", Outline(Accent, 4));
            theme.SetColor("font_color", "ItemList", Text);
            theme.SetColor("font_selected_color", "ItemList", Shell);
            theme.SetStylebox("selected", "ItemList", Filled(Accent));
            theme.SetStylebox("selected_focus", "ItemList", Filled(Accent));
            theme.SetStylebox("cursor", "ItemList", Outline(Accent, 2));
            theme.SetStylebox("cursor_unfocused", "ItemList", Outline(TextDim, 2));

            // Labels.
            theme.SetColor("font_color", "Label", Text);

            return theme;
        }

        static StyleBoxFlat Filled(Color color) {
            StyleBoxFlat box = new StyleBoxFlat { BgColor = color };
            return box;
        }

        static StyleBoxFlat Button(Color color, int radius) {
            StyleBoxFlat box = new StyleBoxFlat { BgColor = color };
            box.SetCornerRadiusAll(radius);
            box.SetContentMarginAll(6);
            box.ContentMarginLeft = 12;
            box.ContentMarginRight = 12;
            return box;
        }

        static StyleBoxFlat Outline(Color color, int radius) {
            StyleBoxFlat box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), DrawCenter = false };
            box.SetCornerRadiusAll(radius);
            box.SetBorderWidthAll(2);
            box.BorderColor = color;
            return box;
        }

        static Color Lighten(Color color, float amount)
            => new Color(color.R + amount, color.G + amount, color.B + amount);
    }
}
