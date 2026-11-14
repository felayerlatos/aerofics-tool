using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ZeroCpuTuner;

public sealed class CalculationFailure(string message) : Exception(message);

public sealed class StressSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task stdout;
    private readonly Task stderr;
    private int passed;
    private int failed;
    private int environmentError;
    private bool intentionallyStopped;
    private readonly string algorithm;
    private readonly Action<string> log;
    public int Passed => Volatile.Read(ref passed);
    public bool Running => !process.HasExited;
    public StressSession(string algorithm, int durationSeconds, int memoryMiB, Action<string> log)
    {
        this.algorithm = algorithm;
        this.log = log;
        if (algorithm is not ("BKT" or "VT3") || memoryMiB is < 512 or > 4096) throw new ArgumentException("压力测试参数无效。");
        var args = new[] { "pause:-2", "skip-warnings", "priority:-1", "PII:-1", "stress", "-M:" + ((long)memoryMiB * 1024 * 1024), "-D:" + durationSeconds, "-TL:900", algorithm };
        process = Process.Start(ChildProcess.Info(AppPaths.StressExe, args)) ?? throw new IOException("无法启动 y-cruncher。");
        stdout = Consume(process.StandardOutput);
        stderr = Consume(process.StandardError);
    }
    private async Task Consume(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Contains("Running " + algorithm + ": Failed", StringComparison.OrdinalIgnoreCase))
                Interlocked.Exchange(ref failed, 1);
            if (line.Contains("Running " + algorithm + ": Passed", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref passed);
            if (line.Contains("Exception Encountered", StringComparison.OrdinalIgnoreCase) || line.Contains("Fatal Error", StringComparison.OrdinalIgnoreCase))
                Interlocked.Exchange(ref environmentError, 1);
            if (line.Contains("Running ", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)) log(line.Trim());
            lock (logGate) File.AppendAllText(LogFile, line + "\n");
        }
    }
    private readonly object logGate = new();
    public string LogFile { get; } = Path.Combine(AppPaths.DataRoot, "stress-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
    public void Check(bool requireRunning = true)
    {
        if (Volatile.Read(ref failed) != 0) throw new CalculationFailure(algorithm + " 报告计算错误。");
        if (Volatile.Read(ref environmentError) != 0) throw new IOException(algorithm + " 运行环境错误，详见测试日志。");
        if (requireRunning && process.HasExited) throw new IOException($"{algorithm} 提前退出（{process.ExitCode}），未认定测试通过。");
    }
    public async Task Finish()
    {
        if (!process.HasExited)
        {
            intentionallyStopped = true;
            process.Kill(true);
        }
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Check(false);
        if (!intentionallyStopped && process.ExitCode != 0) throw new IOException("y-cruncher 异常退出：" + process.ExitCode);
    }
    public async Task RunFive(CancellationToken ct, Action<int, TimeSpan> progress)
    {
        var watch = Stopwatch.StartNew();
        while (Passed < 5)
        {
            ct.ThrowIfCancellationRequested();
            Check(false);
            if (process.HasExited)
            {
                await Task.WhenAll(stdout, stderr);
                Check(false);
                if (Passed >= 5 && process.ExitCode == 0) break;
                throw new IOException($"VT3 提前退出，只记录到 {Passed}/5 轮。");
            }
            if (watch.Elapsed.TotalSeconds > 900) throw new TimeoutException("VT3 超过 15 分钟，未确认通过。");
            progress(Math.Min(Passed, 5), watch.Elapsed);
            await Task.Delay(200, ct);
        }
        await Finish(); // Drain both output streams before declaring success.
        if (Passed < 5) throw new IOException("VT3 最终输出不足五轮。");
    }
    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) process.Kill(true);
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        process.Dispose();
    }
}

public static class CoreLoad
{
    [StructLayout(LayoutKind.Sequential)] private struct GroupAffinity
    {
        public UIntPtr Mask;
        public ushort Group;
        public ushort Reserved1, Reserved2, Reserved3;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadGroupAffinity(IntPtr thread, in GroupAffinity affinity, out GroupAffinity previous);
    public static int Run(int group, ulong mask, int parent)
    {
        var affinity = new GroupAffinity { Group = checked((ushort)group), Mask = new UIntPtr(mask) };
        if (!SetThreadGroupAffinity(GetCurrentThread(), affinity, out _)) return 3;
        using var owner = Process.GetProcessById(parent);
        double value = 1.1;
        var watch = Stopwatch.StartNew();
        while (!owner.HasExited && watch.Elapsed.TotalSeconds < 20)
        {
            for (int i = 1; i < 100000; i++) value = Math.Sqrt(value + i) + Math.Sin(value);
        }
        GC.KeepAlive(value);
        return 0;
    }
    public static Process Start(CoreSlot core) => Process.Start(ChildProcess.Info(Environment.ProcessPath!,
        ["--core-load", core.ProcessorGroup.ToString(), core.ProcessorMask.ToString(), Environment.ProcessId.ToString()])) ?? throw new IOException("无法启动单核响应负载。");
    public static async Task Stop(Process worker)
    {
        if (!worker.HasExited) worker.Kill(true);
        await worker.WaitForExitAsync();
        worker.Dispose();
    }
}
