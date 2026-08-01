namespace Uberkarl.Content;

public sealed class LevelContentException : Exception
{
    public LevelContentException(string message)
        : base(message)
    {
    }

    public LevelContentException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
