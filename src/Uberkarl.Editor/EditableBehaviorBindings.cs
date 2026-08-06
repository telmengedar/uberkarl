using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Resolves a <see cref="BehaviorBinding"/> against an already-opened <see cref="Package"/> — the editor
/// readers' (<see cref="EditableTileSetReader"/>/<see cref="EditableLevelReader"/>) same-package-only
/// counterpart to <see cref="BehaviorBindingResolver"/> (which needs a full <c>IResourceResolver</c> for
/// cross-package resolution the editor does not support for any other resource kind either — see the
/// existing "cross-package graphics are not editable in this increment" restriction both readers already
/// enforce for tile graphics/frames; this mirrors that exact restriction for behavior scripts).
///
/// <para>
/// <b>DiVoid #7747:</b> before this existed, neither reader carried a level's/tileset's authored
/// tile/trigger/level-script bindings into the editor's authoring model AT ALL — <see cref="EditableTile"/>,
/// <see cref="EditableLevel"/> had no fields for them. <see cref="EditableLevelSnapshot.ToResolvedLevel"/>
/// (what <c>PlaytestOverlay</c> plays) therefore always produced a <c>ResolvedLevel</c> with empty
/// <c>TileBehaviors</c>/<c>Triggers</c>/no <c>LevelScript</c>, regardless of what the package actually
/// authored — so a scripted tile (e.g. the demo hurt-on-contact spike) silently did nothing when played from
/// the editor's playtest overlay even though the identical package plays correctly stand-alone via
/// <c>LevelPlay</c>/<c>LevelLoader</c>. This type is the read-through resolution step that closes that gap;
/// assigning/editing a binding through the editor UI remains P3 scope (DiVoid #7738's own known-gap note).
/// </para>
/// </summary>
internal static class EditableBehaviorBindings
{
    /// <summary>
    /// Resolves <paramref name="binding"/> (or returns <c>null</c> when <paramref name="binding"/> is
    /// <c>null</c> — "this subject declares no behavior" round-trips as no entry, exactly like every other
    /// optional reference these readers already handle). A predefined binding passes its id/parameters
    /// through unchanged; a script binding's resource must live in <paramref name="package"/> itself — a
    /// cross-package script surfaces the same typed <see cref="LevelContentException"/> a cross-package tile
    /// graphic already does.
    /// </summary>
    public static ResolvedBehaviorBinding? Resolve(Package package, BehaviorBinding? binding, string role)
    {
        if (binding is null)
            return null;

        if (binding.IsPredefined)
            return ResolvedBehaviorBinding.FromPredefined(binding.PredefinedId!, binding.Parameters);

        ResourceReference reference = binding.Script!.Value;
        if (!reference.IsSelf && reference.Package != package.Id)
            throw new LevelContentException(
                $"{role} script lives in another package; cross-package behavior scripts are not editable in this increment.");

        var source = System.Text.Encoding.UTF8.GetString(package.ReadBytes(reference.Path));
        return ResolvedBehaviorBinding.FromScript(source);
    }
}
