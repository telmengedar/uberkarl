namespace Uberkarl.Behavior;

using Pooshit.Scripting.Errors;

/// <summary>Runs a script entry point and turns a native guard breach or any other failure into a quarantine reason.</summary>
public static class ScriptExecutionGuard
{
    /// <summary>Runs <paramref name="action"/>, reporting success via the return value.</summary>
    public static bool TryRun<T>(Func<T> action, out T? result, out string? failureReason)
    {
        try {
            result = action();
            failureReason = null;
            return true;
        }
        catch (OperationCanceledException ex) {
            failureReason = $"cancelled: {ex.Message}";
        }
        catch (ScriptAbortException ex) {
            failureReason = $"exceeded budget ({ex.GetType().Name}): {ex.Message}";
        }
        catch (Exception ex) {
            failureReason = $"threw: {ex}";
        }

        result = default;
        return false;
    }
}
