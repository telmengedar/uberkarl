namespace Uberkarl.Editor.Input;

/// <summary>Decides whether a directional (stick/D-pad/arrows) reading should drive a radial's highlight
/// this frame, or whether a pointer-set highlight should be left alone.</summary>
public static class MenuAimArbitration
{
    /// <summary>What a Transient-phase aim reading should do to the radial's current highlight.</summary>
    public enum AimAction
    {
        /// <summary>Leave the highlight exactly as it is.</summary>
        Ignore,
        /// <summary>Drive the highlight from the directional reading.</summary>
        ApplyDirectional,
        /// <summary>Clear the highlight — a neutral reading with no pointer-set highlight to protect.</summary>
        ClearHighlight,
    }

    /// <summary>True when <paramref name="dx"/>/<paramref name="dy"/> carries a genuine directional
    /// deflection — a magnitude past <paramref name="deadzone"/>. Below that, the reading is indistinguishable
    /// from "nothing is being held" and must not override whatever aim source (the pointer) last set the
    /// highlight.</summary>
    public static bool DirectionalAimPresent(double dx, double dy, double deadzone = MenuModel.DefaultDeadzone)
    {
        double magnitude = System.Math.Sqrt((dx * dx) + (dy * dy));
        return magnitude > deadzone;
    }

    /// <summary>The Transient-phase aim decision. A live directional reading always wins. Once it goes
    /// neutral, a highlight the directional source itself set is cleared, while a pointer-set highlight
    /// is left alone. A no-op outside <see cref="MenuSessionState.Transient"/>.</summary>
    public static AimAction Resolve(MenuSessionState state, bool directionalAimPresent, bool hasPointerHighlight)
    {
        if (state != MenuSessionState.Transient)
            return AimAction.Ignore;
        if (directionalAimPresent)
            return AimAction.ApplyDirectional;
        return hasPointerHighlight ? AimAction.Ignore : AimAction.ClearHighlight;
    }
}
