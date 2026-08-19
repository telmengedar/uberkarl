using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>One object type's editor-facing data: its definition plus resolved graphic bytes for the object palette.</summary>
public sealed class EditableObjectType
{
    public EditableObjectType(ObjectDefinition definition, byte[] graphic)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Graphic = graphic ?? throw new ArgumentNullException(nameof(graphic));
    }

    /// <summary>The authored object type, unchanged.</summary>
    public ObjectDefinition Definition { get; }

    /// <summary>The object's graphic bytes.</summary>
    public byte[] Graphic { get; }
}
