using System.Diagnostics;

namespace AeroficsLauncher;

internal static class SensorReadiness
{
    // A stalled readiness probe must not prevent the next test stage.
    internal static async Task<bool> WaitAsync(Func<Task> probe, TimeSpan timeout, Func<bool> closed)
    {
        var timer = Stopwatch.StartNew();
        while (!closed())
        {
            TimeSpan remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) return false;
            try
            {
                await probe().WaitAsync(remaining);
                return true;
            }
            catch (TimeoutException) { return false; }
            catch (Exception) { /* Readiness failure is retried until the deadline. */ }
            remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) return false;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500));
        }
        return false;
    }
}
