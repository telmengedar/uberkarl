namespace Uberkarl.Packages;

public sealed class PendingResource
{
    public PendingResource(ResourcePath path, string kind, string mediaType, byte[] payload, Attribution? attribution)
    {
        Path = path;
        Kind = kind;
        MediaType = mediaType;
        Payload = payload;
        Attribution = attribution;
    }

    public ResourcePath Path { get; }

    public string Kind { get; }

    public string MediaType { get; }

    public byte[] Payload { get; }

    public Attribution? Attribution { get; }
}
