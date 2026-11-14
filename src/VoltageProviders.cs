using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AeroficsLauncher;

internal sealed class HwInfoVoltageProvider : IVoltageProvider
{
    public string Name => VoltageText.SourceName(VoltageSource.HwInfoSharedMemory);
    public bool TryRead(out VoltageSnapshot snapshot, out string error)
    {
        try
        {
            snapshot = Read();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            snapshot = null!;
            error = ex.Message;
            return false;
        }
    }

    internal static VoltageSnapshot Read()
    {
        using var map = MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
        using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        Mutex? mutex = null;
        bool held = false;
        try
        {
            if (Mutex.TryOpenExisting(@"Global\HWiNFO_SM2_MUTEX", out mutex))
            {
                try { held = mutex.WaitOne(3000); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("传感器数据忙，请稍后重试");
            }
            if (view.ReadUInt32(0) != 0x53695748 || view.ReadUInt32(4) != 2)
                throw new IOException("HWiNFO 共享内存状态或版本无效");
            uint offset = view.ReadUInt32(32), size = view.ReadUInt32(36), count = view.ReadUInt32(40);
            if (size < 316 || count == 0 || count > 100000 || (long)offset + (long)size * count > view.Capacity)
                throw new IOException("传感器数据尚未准备好");
            var values = new List<VoltageCoreSample>();
            for (uint i = 0; i < count; i++)
            {
                long pos = offset + (long)i * size;
                var bytes = new byte[128];
                view.ReadArray(pos + 12, bytes, 0, bytes.Length);
                string label = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                var match = Regex.Match(label, @"^(?:(?<kind>P|E)-?)?Core\s*#?\s*(?<core>\d+)\s*VID$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                double voltage = view.ReadDouble(pos + 284);
                if (double.IsFinite(voltage) && voltage > 0 && voltage < 3)
                    values.Add(new VoltageCoreSample(int.Parse(match.Groups["core"].Value), voltage, -1, match.Groups["kind"].Value.ToUpperInvariant()));
            }
            var groups = values.GroupBy(v => v.CoreId).ToArray();
            if (groups.Length < 2) throw new IOException("未找到有效的逐核心 VID 数据");
            var readings = values.GroupBy(v => v.CoreId).Select(g => g.OrderByDescending(v => v.Voltage).First()).OrderBy(v => v.CoreId).ToArray();
            var topology = VidGroups.Topology();
            var ccd = topology.Where(c => c.Id >= 0 && c.L3 >= 0).GroupBy(c => c.L3).OrderBy(g => g.Min(c => c.Id)).Select((g, i) => g.Select(c => (c.Id, i))).SelectMany(x => x).ToDictionary(x => x.Id, x => x.i);
            readings = readings.Select(r => r with { CcdIndex = ccd.TryGetValue(r.CoreId, out var i) ? i : -1 }).ToArray();
            string vendor = VidGroups.Vendor();
            return new VoltageSnapshot(VoltageSource.HwInfoSharedMemory, view.ReadInt64(12), vendor, 0, 0, "", readings, topology);
        }
        finally { if (held) mutex!.ReleaseMutex(); mutex?.Dispose(); }
    }
}

internal sealed class AmdSmuVoltageProvider : IVoltageProvider, IDisposable
{
    private readonly string _root;
    private readonly object _gate = new();
    private Assembly? _assembly;
    private object? _cpu;
    private string _lastError = "";
    public string Name => VoltageText.SourceName(VoltageSource.AmdSmuReadOnly);
    public AmdSmuVoltageProvider(string root) => _root = root;

    public bool TryRead(out VoltageSnapshot snapshot, out string error)
    {
        lock (_gate)
        {
            try { snapshot = ReadCore(); error = ""; return true; }
            catch (Exception ex) { snapshot = null!; error = _lastError = ex.GetBaseException().Message; return false; }
        }
    }

    private VoltageSnapshot ReadCore()
    {
        string dll = Path.Combine(_root, "ryzen", "ZenStates-Core.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("缺少 AMD SMU 组件", dll);
        _assembly ??= Assembly.LoadFrom(dll);
        var cpuType = _assembly.GetType("ZenStates.Core.Cpu") ?? throw new IOException("SMU CPU 类型缺失");
        var initType = _assembly.GetType("ZenStates.Core.CpuInitSettings") ?? throw new IOException("SMU 初始化类型缺失");
        _cpu ??= Activator.CreateInstance(cpuType, Activator.CreateInstance(initType)) ?? throw new IOException("AMD SMU 初始化失败");
        var status = cpuType.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)?.GetValue(_cpu)?.ToString() ?? "";
        if (status.Contains("Failed", StringComparison.OrdinalIgnoreCase) || status.Contains("Unsupported", StringComparison.OrdinalIgnoreCase))
            throw new IOException("AMD SMU 初始化失败：" + status);
        cpuType.GetMethod("RefreshPowerTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(_cpu, null);
        var tableField = cpuType.GetField("powerTable", BindingFlags.NonPublic | BindingFlags.Instance);
        var table = tableField?.GetValue(_cpu) ?? throw new IOException("AMD SMU 电源表不可用");
        var values = table.GetType().GetProperty("Table", BindingFlags.Public | BindingFlags.Instance)?.GetValue(table) as Array
            ?? throw new IOException("AMD SMU 电源表格式不受支持");
        var info = cpuType.GetField("systemInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_cpu);
        string cpuName = info?.GetType().GetProperty("CpuName")?.GetValue(info)?.ToString() ?? "AMD CPU";
        uint cpuid = Convert.ToUInt32(info?.GetType().GetProperty("CpuId")?.GetValue(info) ?? 0u);
        int family = (int)((cpuid >> 8) & 0xF); if (family == 0xF) family += (int)((cpuid >> 20) & 0xFF);
        int model = (int)((cpuid >> 4) & 0xF) | (int)(((cpuid >> 16) & 0xF) << 4);
        int count = Convert.ToInt32(info?.GetType().GetProperty("PhysicalCoreCount")?.GetValue(info) ?? 0);
        var topology = VidGroups.Topology();
        var known = topology.Where(c => c.Id >= 0).OrderBy(c => c.Id).ToArray();
        if (count <= 0) count = known.Length;
        var layout = SmuLayouts.Resolve(family, model, count, values.Length * 4);
        if (layout == null) throw new IOException($"未找到 AMD family {family:X} model {model:X} 的只读 VID 表布局");
        var result = new List<VoltageCoreSample>();
        for (int i = 0; i < layout.Value.CoreCount; i++)
        {
            int index = layout.Value.OffsetBytes / 4 + i;
            if (index >= values.Length) throw new IOException("AMD SMU 电源表长度不足");
            double voltage = Convert.ToDouble(values.GetValue(index), System.Globalization.CultureInfo.InvariantCulture);
            if (!double.IsFinite(voltage) || voltage <= 0 || voltage >= 3) throw new IOException("AMD SMU 核心电压数据无效");
            int core = i < known.Length ? known[i].Id : i;
            int ccd = i < known.Length ? known[i].L3 : -1;
            result.Add(new VoltageCoreSample(core, voltage, ccd));
        }
        return new VoltageSnapshot(VoltageSource.AmdSmuReadOnly, DateTime.UtcNow.Ticks, "AuthenticAMD", family, model, cpuName, result, topology,
            "SMU 只读电源表；未调用任何调压或频率写入命令");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_cpu is IDisposable disposable) disposable.Dispose();
            _cpu = null;
        }
    }
}

internal static class SmuLayouts
{
    internal readonly record struct Layout(int OffsetBytes, int CoreCount);
    internal static Layout? Resolve(int family, int model, int cores, int tableBytes)
    {
        if (family != 0x19 && family != 0x1A) return null;
        // Layouts are copied from the cleaned VoltageReader table definitions.
        if (model == 0x61 && cores >= 16 && tableBytes >= 2236) return new Layout(1236, 16);
        if (model == 0x61 && cores >= 8 && tableBytes >= 1704) return new Layout(1204, 8);
        if (model == 0x44 && cores >= 8 && tableBytes >= 2236) return new Layout(1236, 16);
        return null;
    }
}
