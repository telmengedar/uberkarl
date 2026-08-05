namespace Uberkarl.Behavior;

using System.Threading;

/// <summary>How a watchdog-guarded run ended.</summary>
public enum BehaviorWatchdogOutcomeKind
{
    /// <summary>The action returned within budget.</summary>
    Completed,

    /// <summary>The action threw (a Pooscript <c>ScriptRuntimeException</c>/<c>ScriptParserException</c> or any other exception).</summary>
    Faulted,

    /// <summary>The action did not return within budget — the subject must be quarantined.</summary>
    BudgetExceeded,
}

/// <summary>The result of one watchdog-guarded run.</summary>
public sealed class BehaviorWatchdogOutcome<T>
{
    public required BehaviorWatchdogOutcomeKind Kind { get; init; }

    public T? Value { get; init; }

    public Exception? Exception { get; init; }

    public bool Success => Kind == BehaviorWatchdogOutcomeKind.Completed;

    public static BehaviorWatchdogOutcome<T> Completed(T value) => new() { Kind = BehaviorWatchdogOutcomeKind.Completed, Value = value };

    public static BehaviorWatchdogOutcome<T> Faulted(Exception exception) => new() { Kind = BehaviorWatchdogOutcomeKind.Faulted, Exception = exception };

    public static BehaviorWatchdogOutcome<T> BudgetExceeded() => new() { Kind = BehaviorWatchdogOutcomeKind.BudgetExceeded };
}

/// <summary>
/// Enforces the per-invocation budget that makes the behavior layer freeze-proof (design #7704 §8.3/§8.4 —
/// "LOAD-BEARING"). Every script entry point — the compiler's one-time init execute AND every per-event
/// handler dispatch — runs through <see cref="Execute{T}"/>.
///
/// <para>
/// Two layers, deliberately NOT relying on cooperative cancellation alone (per DiVoid #7710: "treat CT as
/// available but don't rely on it alone" — Pooscript's cooperative cancellation, #7409, is being completed
/// in a separate session and was, as of this writing, empirically inconsistent: a <c>while(true)</c> loop
/// with a non-trivial body DID unwind promptly when its <see cref="CancellationTokenSource"/> was
/// cancelled, but nothing in the public Pooshit.Scripting API lets a per-event invocation of an
/// already-cached handler delegate (<c>LambdaMethod.Invoke</c>) accept a fresh per-call token — the
/// cancellation token a cached handler observes is fixed to whatever was active when the script was first
/// parsed and executed to discover its handlers. A pure cooperative watchdog can therefore never be
/// guaranteed to interrupt a handler that ignores or never re-checks that fixed token.):
/// </para>
/// <list type="number">
/// <item><b>Cooperative signal (best-effort):</b> the action's <paramref name="instanceCancellation"/> is
/// cancelled on budget breach, giving a cooperative interpreter a chance to unwind promptly (works today for
/// several loop shapes; not guaranteed for all).</item>
/// <item><b>Thread-abandonment (the actual guarantee):</b> the action always runs on a pool thread, raced
/// against the budget via <see cref="Task.WaitAny(Task[])"/>. On breach, this method returns immediately
/// without waiting further — the calling thread (the host's frame loop, or a test) is NEVER blocked past the
/// budget, regardless of whether the runaway thread ever actually stops. The abandoned task's eventual
/// fault/cancellation is observed and discarded so it can never surface as an unobserved-task-exception
/// later. This mirrors design #7704 §8.4's explicitly offered "worker-thread + abandon-on-timeout" bridge —
/// promoted here from "alternative interim" to the mechanism that actually satisfies the P0 acceptance gate,
/// since only thread-abandonment can be unit-tested to demonstrably survive a script that never checks
/// cancellation at all.</item>
/// </list>
/// </summary>
public sealed class BehaviorWatchdog
{
    public BehaviorWatchdog(TimeSpan budget)
    {
        if (budget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), "Watchdog budget must be positive.");

        Budget = budget;
    }

    /// <summary>The per-invocation wall-clock budget (design #7704 §8.3 — "a per-invocation wall-clock cap"). Instruction-count budgeting is not implemented in P0: Pooshit.Scripting exposes no instruction-count hook (see PR notes).</summary>
    public TimeSpan Budget { get; }

    /// <summary>
    /// Runs <paramref name="action"/> under the budget. Never blocks the caller longer than
    /// <see cref="Budget"/> (plus negligible thread-pool scheduling latency).
    /// </summary>
    /// <param name="action">The script entry point to run (an init execute, or a cached handler invocation).</param>
    /// <param name="instanceCancellation">The behavior instance's own cancellation source — cancelled on budget breach as the best-effort cooperative signal.</param>
    public BehaviorWatchdogOutcome<T> Execute<T>(Func<T> action, CancellationTokenSource instanceCancellation)
    {
        var task = Task.Run(action);

        // Task.WaitAny (unlike Task.Wait(timeout)) does NOT rethrow a faulted task's exception -- it just
        // reports completion, letting us distinguish Faulted from Completed ourselves below.
        var completedInBudget = Task.WaitAny(new Task[] { task }, Budget) == 0;
        if (completedInBudget)
        {
            if (task.IsFaulted)
                return BehaviorWatchdogOutcome<T>.Faulted(Unwrap(task.Exception!));

            return BehaviorWatchdogOutcome<T>.Completed(task.Result);
        }

        // Budget breached. Signal cooperative cancellation (best-effort) but do not wait on it any further --
        // the guarantee is that THIS call returns now, not that the runaway task ever actually stops.
        instanceCancellation.Cancel();

        // Observe the abandoned task's eventual result so a faulted/cancelled Task never becomes an
        // unobserved-task-exception later; the leaked thread itself is an accepted, documented cost
        // (design #7704 §8.4) -- the invariant is "never freeze the host", not "never leak a thread".
        task.ContinueWith(static abandoned => _ = abandoned.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        return BehaviorWatchdogOutcome<T>.BudgetExceeded();
    }

    private static Exception Unwrap(AggregateException aggregate) => aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate;
}
