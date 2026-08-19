using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Loads an object set's object types from a package.</summary>
public static class EditableObjectSetReader
{
    /// <summary>Loads every object type <paramref name="reference"/> declares, from an already-opened package.</summary>
    public static IReadOnlyList<EditableObjectType> FromPackage(Package package, ResourceReference reference)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (!reference.IsSelf && reference.Package != package.Id)
            throw new LevelContentException("Editing an object set that lives in another package is not supported.");

        var objectSet = LevelContentSerializer.ReadObjectSet(package.ReadBytes(reference.Path));
        var types = new List<EditableObjectType>(objectSet.Objects.Count);
        foreach (var definition in objectSet.Objects)
        {
            if (!definition.Graphic.IsSelf && definition.Graphic.Package != package.Id)
                throw new LevelContentException($"Object '{definition.Id}' graphic lives in another package; cross-package graphics are not editable in this increment.");
            types.Add(new EditableObjectType(definition, package.ReadBytes(definition.Graphic.Path)));
        }

        return types;
    }
}
