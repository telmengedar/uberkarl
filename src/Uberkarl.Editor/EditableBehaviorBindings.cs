using System.Text;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Reads and resolves <see cref="BehaviorBinding"/>s for the editor's authoring model.</summary>
internal static class EditableBehaviorBindings
{
    /// <summary>
    /// Validates <paramref name="binding"/> against <paramref name="package"/> and returns it unchanged (or
    /// <c>null</c> when <paramref name="binding"/> is <c>null</c>). A script binding's resource must live in
    /// <paramref name="package"/> itself; its source is read once into <paramref name="scripts"/>, keyed by
    /// its <see cref="ResourcePath"/>, so a later save can re-emit it.
    /// </summary>
    public static BehaviorBinding? Capture(Package package, BehaviorBinding? binding, string role, IDictionary<ResourcePath, string> scripts)
    {
        if (binding is null)
            return null;

        if (binding.IsPredefined)
            return binding;

        ResourceReference reference = binding.Script!.Value;
        if (!reference.IsSelf && reference.Package != package.Id)
            throw new LevelContentException(
                $"{role} script lives in another package; cross-package behavior scripts are not editable in this increment.");

        if (!scripts.ContainsKey(reference.Path))
            scripts[reference.Path] = Encoding.UTF8.GetString(package.ReadBytes(reference.Path));

        return binding;
    }

    /// <summary>
    /// Resolves <paramref name="binding"/> (or returns <c>null</c> when <paramref name="binding"/> is
    /// <c>null</c>) against <paramref name="scripts"/> — the in-memory script table a <see cref="Capture"/>
    /// call populated. A predefined binding passes its id/parameters through unchanged; a script binding's
    /// source is looked up by its <see cref="ResourcePath"/>.
    /// </summary>
    public static ResolvedBehaviorBinding? Resolve(BehaviorBinding? binding, IReadOnlyDictionary<ResourcePath, string> scripts)
    {
        if (binding is null)
            return null;

        if (binding.IsPredefined)
            return ResolvedBehaviorBinding.FromPredefined(binding.PredefinedId!, binding.Parameters);

        ResourceReference reference = binding.Script!.Value;
        if (!scripts.TryGetValue(reference.Path, out string? source))
            throw new LevelContentException($"Script resource '{reference.Path}' has no known source.");

        return ResolvedBehaviorBinding.FromScript(source);
    }
}
