namespace Uberkarl.Behavior;

using Uberkarl.Packages;

/// <summary>
/// Turns an authored <see cref="BehaviorBinding"/> into a <see cref="ResolvedBehaviorBinding"/> against an
/// <see cref="IResourceResolver"/> — the content pipeline's Definition → Resolved step (design #7704 C-2)
/// applied to behavior bindings. Lives beside <see cref="BehaviorBinding"/> rather than in
/// <c>Uberkarl.Content</c> because it only needs <c>Uberkarl.Packages</c> (already a P0 dependency of this
/// core) and keeps binding-resolution logic colocated with the binding type itself; <c>Uberkarl.Content</c>'s
/// <c>LevelLoader</c> calls this exactly like it resolves every other package reference.
/// </summary>
public static class BehaviorBindingResolver
{
    /// <summary>
    /// Resolves <paramref name="binding"/>. A script binding's resource is read via
    /// <paramref name="resolver"/> and decoded as UTF-8 Pooscript source; a predefined binding passes its id
    /// and parameters through unchanged (resolving a predefined id to source text is a runtime concern —
    /// see <see cref="PredefinedBehaviors"/>).
    /// </summary>
    public static ResolvedBehaviorBinding Resolve(IResourceResolver resolver, BehaviorBinding binding)
    {
        if (resolver is null)
            throw new ArgumentNullException(nameof(resolver));
        if (binding is null)
            throw new ArgumentNullException(nameof(binding));

        if (binding.IsPredefined)
            return ResolvedBehaviorBinding.FromPredefined(binding.PredefinedId!, binding.Parameters);

        var payload = resolver.Resolve(binding.Script!.Value);
        var source = System.Text.Encoding.UTF8.GetString(payload);
        return ResolvedBehaviorBinding.FromScript(source);
    }
}
