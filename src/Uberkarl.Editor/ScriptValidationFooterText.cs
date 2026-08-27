namespace Uberkarl.Editor;

/// <summary>Formats a <see cref="Uberkarl.Behavior.BehaviorSourceValidator"/> verdict into the script source editor's validation footer line.</summary>
public static class ScriptValidationFooterText
{
    private const string CleanText = "compiles";

    /// <summary>The footer text for <paramref name="quarantineReason"/> — the reason verbatim, or a fixed clean-compile message when there is none.</summary>
    public static string Format(string? quarantineReason) => quarantineReason ?? CleanText;
}
