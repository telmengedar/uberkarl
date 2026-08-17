namespace Uberkarl.Behavior.Tests;

using System.Threading;

/// <summary>Test-only host global that blocks the calling thread without spending any script step budget, proving <see cref="Pooshit.Scripting.ScriptLimits.Timeout"/> is a real wall-clock backstop distinct from <see cref="Pooshit.Scripting.ScriptLimits.MaxSteps"/> (DiVoid #7862).</summary>
internal sealed class BlockingHostCallStub
{
    /// <summary>Blocks the calling thread for <paramref name="milliseconds"/>, simulating a host call that takes real time without doing any script-visible work.</summary>
    public void Block(double milliseconds) => Thread.Sleep((int)milliseconds);
}
