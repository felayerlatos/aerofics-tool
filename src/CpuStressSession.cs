using System.Diagnostics;

namespace AeroficsLauncher;

/// <summary>Dedicated CPU workers used by the one-click test. They stop on cancellation and never write hardware state.</summary>
internal sealed class CpuStressSession : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Thread> _workers = new();
    private CancellationTokenSource? _cancel;
    private int _started;
    private long _sink;
    public bool IsRunning { get { lock (_gate) return _cancel is not null; } }
    public int WorkerCount { get { lock (_gate) return _workers.Count; } }

    public void Start(int? workerCount = null)
    {
        lock (_gate)
        {
            if (_cancel is not null) return;
            int count = Math.Max(1, workerCount ?? Environment.ProcessorCount);
            _cancel = new CancellationTokenSource();
            _workers.Clear(); _started = 0;
            for (int i = 0; i < count; i++)
            {
                var thread = new Thread(() => Run(_cancel.Token))
                {
                    IsBackground = true,
                    Name = "Aerofics CPU stress",
                    Priority = ThreadPriority.AboveNormal
                };
                _workers.Add(thread); thread.Start();
            }
        }
        var deadline = Stopwatch.StartNew();
        while (Volatile.Read(ref _started) < WorkerCount && deadline.Elapsed < TimeSpan.FromSeconds(2)) Thread.Yield();
    }

    private void Run(CancellationToken token)
    {
        Interlocked.Increment(ref _started);
        long value = Environment.CurrentManagedThreadId * 7919L + 17;
        while (!token.IsCancellationRequested)
        {
            value = unchecked(value * 6364136223846793005L + 1442695040888963407L);
            value ^= value >> 29;
            if ((value & 0x3FFF) == 0) Thread.SpinWait(32);
        }
        Interlocked.Exchange(ref _sink, value);
    }

    public void Stop()
    {
        CancellationTokenSource? cancel;
        Thread[] workers;
        lock (_gate)
        {
            cancel = _cancel; if (cancel is null) return;
            _cancel = null; workers = _workers.ToArray(); _workers.Clear();
        }
        cancel.Cancel();
        foreach (var worker in workers) if (worker.IsAlive) worker.Join(2000);
        cancel.Dispose();
    }

    public void Dispose() => Stop();
}
