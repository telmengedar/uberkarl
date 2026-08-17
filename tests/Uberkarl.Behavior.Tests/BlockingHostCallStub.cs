namespace Uberkarl.Behavior.Tests;

using System.Threading;

/// <summary>Test-only host global whose method blocks the calling thread without consuming script steps.</summary>
internal sealed class BlockingHostCallStub
{
    /// <summary>Blocks the calling thread for <paramref name="milliseconds"/>.</summary>
    public void Block(double milliseconds) => Thread.Sleep((int)milliseconds);
}
