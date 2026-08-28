using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>The mutable object set under edit — the façade the object-set editor UI drives.</summary>
public sealed class ObjectSetEditSession
{
    private const string IdBase = "object";

    private readonly List<EditableObjectType> types;

    private ObjectSetEditSession(string name, ResourcePath objectSetPath, IReadOnlyList<EditableObjectType> types, bool isAttached)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ObjectSetPath = objectSetPath;
        this.types = new List<EditableObjectType>(types ?? throw new ArgumentNullException(nameof(types)));
        IsAttached = isAttached;
    }

    /// <summary>Display name; the base for the slug.</summary>
    public string Name { get; }

    /// <summary>The in-package path this object set is stored at.</summary>
    public ResourcePath ObjectSetPath { get; private set; }

    /// <summary>What a placement records.</summary>
    public ResourceReference Reference => ResourceReference.ToSelf(ObjectSetPath);

    /// <summary>True once this set occupies a stable, namespaced resource slot.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>True when there are unsaved edits since the last successful save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The set's object types, in declaration order.</summary>
    public IReadOnlyList<EditableObjectType> Types => types;

    /// <summary>Creates an empty, unattached set.</summary>
    public static ObjectSetEditSession CreateBlank(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        string slug = ObjectSetResourcePaths.Slugify(name);
        return new ObjectSetEditSession(name, ObjectSetResourcePaths.ObjectSetPath(slug), Array.Empty<EditableObjectType>(), isAttached: false);
    }

    /// <summary>Reads an already-persisted object set from a package.</summary>
    public static ObjectSetEditSession FromPackage(Package package, ResourceReference reference)
    {
        IReadOnlyList<EditableObjectType> loaded = EditableObjectSetReader.FromPackage(package, reference);
        string name = ObjectSetResourcePaths.SlugFromObjectSetPath(reference.Path) ?? reference.Path.Value;
        return new ObjectSetEditSession(name, reference.Path, loaded, isAttached: true);
    }

    /// <summary>Imports <paramref name="graphic"/> as a new type with <paramref name="collisionRole"/>. Returns the new type's id.</summary>
    public string AddType(byte[] graphic, ObjectCollisionRole collisionRole)
    {
        if (graphic is null || graphic.Length == 0)
            throw new ArgumentException("Object graphic must not be empty.", nameof(graphic));

        string id = ObjectSetResourcePaths.UniqueSlug(IdBase, candidate => ContainsId(candidate));
        ResourcePath graphicPath = ObjectSetResourcePaths.GraphicPath(CurrentSlug, id);
        ObjectDefinition definition = new ObjectDefinition
        {
            Id = id,
            Name = null,
            Graphic = ResourceReference.ToSelf(graphicPath),
            CollisionRole = collisionRole,
        };
        types.Add(new EditableObjectType(definition, graphic));
        IsDirty = true;
        return id;
    }

    /// <summary>Renames the type with <paramref name="id"/>. <c>false</c> when the type does not exist or nothing changed.</summary>
    public bool RenameType(string id, string? name)
    {
        int index = types.FindIndex(type => type.Definition.Id == id);
        if (index < 0)
            return false;

        string? normalized = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        EditableObjectType current = types[index];
        if (string.Equals(current.Definition.Name, normalized, StringComparison.Ordinal))
            return false;

        types[index] = new EditableObjectType(WithValues(current.Definition, normalized, current.Definition.CollisionRole, current.Definition.Graphic), current.Graphic);
        IsDirty = true;
        return true;
    }

    /// <summary>Sets the collision role of the type with <paramref name="id"/>. <c>false</c> when the type does not exist or the role is unchanged.</summary>
    public bool SetCollisionRole(string id, ObjectCollisionRole role)
    {
        int index = types.FindIndex(type => type.Definition.Id == id);
        if (index < 0)
            return false;

        EditableObjectType current = types[index];
        if (current.Definition.CollisionRole == role)
            return false;

        types[index] = new EditableObjectType(WithValues(current.Definition, current.Definition.Name, role, current.Definition.Graphic), current.Graphic);
        IsDirty = true;
        return true;
    }

    /// <summary>Replaces the bytes at the type's existing graphic path. <c>false</c> when the type does not exist.</summary>
    public bool ReplaceGraphic(string id, byte[] graphic)
    {
        if (graphic is null || graphic.Length == 0)
            throw new ArgumentException("Object graphic must not be empty.", nameof(graphic));

        int index = types.FindIndex(type => type.Definition.Id == id);
        if (index < 0)
            return false;

        EditableObjectType current = types[index];
        types[index] = new EditableObjectType(current.Definition, graphic);
        IsDirty = true;
        return true;
    }

    /// <summary>Removes the type with <paramref name="id"/> unconditionally. <c>false</c> when the type does not exist.</summary>
    public bool RemoveType(string id)
    {
        int index = types.FindIndex(type => type.Definition.Id == id);
        if (index < 0)
            return false;

        types.RemoveAt(index);
        IsDirty = true;
        return true;
    }

    /// <summary>Attaches this set to a namespaced resource slot, uniquified against <paramref name="existingResources"/>. No-op when already attached.</summary>
    public void EnsureAttached(IReadOnlyList<ResourceEntry> existingResources)
    {
        if (existingResources is null)
            throw new ArgumentNullException(nameof(existingResources));
        if (IsAttached)
            return;

        string baseSlug = ObjectSetResourcePaths.Slugify(Name);
        string slug = ObjectSetResourcePaths.UniqueSlug(baseSlug, candidate => Contains(existingResources, ObjectSetResourcePaths.ObjectSetPath(candidate)));
        ObjectSetPath = ObjectSetResourcePaths.ObjectSetPath(slug);

        for (int i = 0; i < types.Count; i++)
        {
            EditableObjectType current = types[i];
            ResourcePath graphicPath = ObjectSetResourcePaths.GraphicPath(slug, current.Definition.Id);
            types[i] = new EditableObjectType(WithValues(current.Definition, current.Definition.Name, current.Definition.CollisionRole, ResourceReference.ToSelf(graphicPath)), current.Graphic);
        }

        IsAttached = true;
        IsDirty = true;
    }

    /// <summary>This set's resource contributions. Empty when <see cref="Types"/> is empty.</summary>
    public IReadOnlyList<PendingResource> BuildContributions() =>
        Types.Count == 0 ? Array.Empty<PendingResource>() : ObjectSetMergeWriter.BuildContributions(ObjectSetPath, Types);

    /// <summary>Clears the dirty flag.</summary>
    public void MarkSaved() => IsDirty = false;

    /// <summary>Re-marks the session dirty.</summary>
    public void MarkDirty() => IsDirty = true;

    private string CurrentSlug => ObjectSetResourcePaths.SlugFromObjectSetPath(ObjectSetPath) ?? ObjectSetResourcePaths.Slugify(Name);

    private bool ContainsId(string candidate)
    {
        foreach (EditableObjectType type in types)
        {
            if (type.Definition.Id == candidate)
                return true;
        }

        return false;
    }

    private static ObjectDefinition WithValues(ObjectDefinition source, string? name, ObjectCollisionRole collisionRole, ResourceReference graphic) =>
        new ObjectDefinition
        {
            Id = source.Id,
            Name = name,
            Graphic = graphic,
            CollisionRole = collisionRole,
            Behavior = source.Behavior,
            State = source.State,
        };

    private static bool Contains(IReadOnlyList<ResourceEntry> resources, ResourcePath path)
    {
        foreach (ResourceEntry entry in resources)
        {
            if (entry.Path == path)
                return true;
        }

        return false;
    }
}
