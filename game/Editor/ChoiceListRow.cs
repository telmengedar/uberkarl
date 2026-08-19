using Godot;

namespace Uberkarl {

    /// <summary>A <see cref="ChoiceList"/> row's primary and secondary display text, plus an optional leading icon.</summary>
    public readonly record struct ChoiceListRow(string Primary, string Secondary, Texture2D Icon = null);
}
