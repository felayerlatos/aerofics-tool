using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace ZeroCpuTuner;

public sealed class SensorSession : IAsyncDisposable
{
    private Process? process;
    private Task<string>? errors;
    private Task<string>? output;
    private MemoryMappedFile? mapping;
    private MemoryMappedViewAccessor? view;
    private CpuTopology topology = new();
    private long consumed;
    public bool Running => process is { HasExited: false } && view != null;
    public async Task Start(CpuTopology map, CancellationToken ct)
    {
        topology = map;
        var mapName = "Local\\ZeroCpuTuner-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        string[] args = [AppPaths.Sensor, "--map-name", mapName, "--interval", "300", "--parent-pid", Environment.ProcessId.ToString(),
            "--expected-map-hash", map.MapHash, "--expected-maximum-physical-slots", map.MaximumPhysicalSlots.ToString(),
            "--expected-physical-slots", string.Join(',', map.CoreSlots.Select(c => c.PhysicalSlot))];
        process = Process.Start(ChildProcess.Info(AppPaths.Dotnet, args, Path.GetDirectoryName(AppPaths.Sensor))) ?? throw new IOException("无法启动传感器。");
        output = process.StandardOutput.ReadToEndAsync();
        errors = process.StandardError.ReadToEndAsync();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 20)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited) throw new IOException("VID 传感器退出：" + await errors);
            if (mapping == null)
            {
                try
                {
                    mapping = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
                    view = mapping.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read);
                }
                catch (FileNotFoundException) { }
            }
            if (Read() != null) return;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("20 秒内未收到有效 VID 数据。请退出其他占用 SMU 的调压程序后重新连接。");
    }
    public VidFrame? Read()
    {
        if (view == null || process == null || process.HasExited) return null;
        long seq = view.ReadInt64(8);
        if (seq <= 0 || (seq & 1) != 0) return null;
        if (view.ReadInt32(0) != 1296193107 || view.ReadInt32(4) != 4) return null;
        if (view.ReadInt32(40) != 2 || view.ReadInt32(24) != topology.ActiveCoreCount) return null;
        long ticks = view.ReadInt64(16);
        if (ticks <= DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || Math.Abs(DateTime.UtcNow.Ticks - ticks) > TimeSpan.FromSeconds(3).Ticks) return null;
        var hash = new byte[64];
        view.ReadArray(68, hash, 0, hash.Length);
        if (view.ReadInt32(64) != 64 || Encoding.ASCII.GetString(hash) != topology.MapHash ||
            view.ReadInt32(28) != topology.Identity.Family || view.ReadInt32(32) != topology.Identity.Model ||
            view.ReadUInt32(60) != topology.Identity.SmuTableVersion || view.ReadInt32(52) != topology.MaximumPhysicalSlots) return null;
        var values = new double[topology.ActiveCoreCount];
        for (int i = 0; i < values.Length; i++)
        {
            long offset = 144 + i * 32;
            var core = topology.CoreSlots[i];
            if (view.ReadInt32(offset + 12) != core.PhysicalSlot || view.ReadInt32(offset + 8) != core.ProcessorGroup ||
                view.ReadUInt64(offset + 24) != core.ProcessorMask) return null;
            values[i] = view.ReadDouble(offset);
            if (!double.IsFinite(values[i]) || values[i] is < 0.4 or > 1.6) return null;
        }
        if (view.ReadInt64(8) != seq) return null;
        return new(seq, new DateTime(ticks, DateTimeKind.Utc), values);
    }
    public async Task<VidWindow> Window(int count, CancellationToken ct, Action? checkLoad = null)
    {
        var samples = new List<double[]>();
        var start = Stopwatch.StartNew();
        while (samples.Count < count)
        {
            ct.ThrowIfCancellationRequested();
            checkLoad?.Invoke();
            var frame = Read();
            if (frame != null && frame.Sequence > consumed)
            {
                samples.Add(frame.Values);
                consumed = frame.Sequence;
            }
            if (start.Elapsed.TotalSeconds > Math.Max(10, count * .6 + 5)) throw new IOException("VID 新帧不足或数据已失效，停止自动调校。");
            await Task.Delay(60, ct);
        }
        var median = new double[topology.ActiveCoreCount];
        var range = new double[median.Length];
        for (int core = 0; core < median.Length; core++)
        {
            var sorted = samples.Select(s => s[core]).Order().ToArray();
            median[core] = (sorted[(count - 1) / 2] + sorted[count / 2]) / 2;
            // Trim one value at each end when measuring qualification noise.
            range[core] = sorted[count - 2] - sorted[1];
        }
        return new(median, range, count);
    }
    public async ValueTask DisposeAsync()
    {
        view?.Dispose(); view = null;
        mapping?.Dispose(); mapping = null;
        if (process != null)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            if (errors != null) await errors;
            if (output != null) await output;
            process.Dispose(); process = null;
        }
    }
}
