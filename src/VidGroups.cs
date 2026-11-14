using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
namespace AeroficsLauncher;

public record VidReading(uint Sensor, int Core, double Value, string Kind = "");
public record CoreTopology(int Id, int L3, byte Efficiency, string Kind);
public static class VidGroups
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [StructLayout(LayoutKind.Sequential)]
    private struct Affinity { public ulong Mask; public ushort Group; public ushort R0, R1, R2; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadGroupAffinity(IntPtr thread, ref Affinity affinity, out Affinity previous);

    public static string Vendor()
    {
        if (!X86Base.IsSupported) return "";
        var v = X86Base.CpuId(0, 0);
        return Encoding.ASCII.GetString(BitConverter.GetBytes(v.Ebx).Concat(BitConverter.GetBytes(v.Edx)).Concat(BitConverter.GetBytes(v.Ecx)).ToArray());
    }
    public static List<CoreTopology> Topology()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0xffff, IntPtr.Zero, ref length);
        if (length == 0) throw new IOException("系统未提供 CPU 拓扑");
        var memory = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0xffff, memory, ref length))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var cores = new List<(ushort Group, ulong Mask, byte Efficiency)>();
            var caches = new List<List<(ushort Group, ulong Mask)>>();
            for (int pos = 0; pos < length;)
            {
                var p = IntPtr.Add(memory, pos);
                int rel = Marshal.ReadInt32(p), size = Marshal.ReadInt32(p, 4);
                if (size < 8 || (long)pos + size > length) throw new IOException("CPU 拓扑格式无效");
                if (rel == 0 && size >= 48)
                    cores.Add(((ushort)Marshal.ReadInt16(p, 40), (ulong)Marshal.ReadInt64(p, 32), Marshal.ReadByte(p, 9)));
                if (rel == 2 && size >= 56 && Marshal.ReadByte(p, 8) == 3)
                {
                    int count = Math.Max(1, (int)(ushort)Marshal.ReadInt16(p, 38));
                    var masks = new List<(ushort, ulong)>();
                    for (int j = 0; j < count && 40 + (j + 1) * 16 <= size; j++)
                        masks.Add(((ushort)Marshal.ReadInt16(p, 48 + j * 16), (ulong)Marshal.ReadInt64(p, 40 + j * 16)));
                    caches.Add(masks);
                }
                pos += size;
            }
            var result = new List<CoreTopology>();
            string vendor = Vendor();
            foreach (var c in cores)
            {
                int id = -1;
                string kind = "";
                var affinity = new Affinity { Group = c.Group, Mask = c.Mask & (~c.Mask + 1) };
                if (!SetThreadGroupAffinity(GetCurrentThread(), ref affinity, out var previous))
                    throw new IOException("无法绑定核心以核对 CPUID");
                try
                {
                    if (vendor == "AuthenticAMD" && (uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax >= 0x8000001e)
                        id = X86Base.CpuId(unchecked((int)0x8000001e), 0).Ebx & 255;
                    else if (vendor == "GenuineIntel")
                    {
                        int max = X86Base.CpuId(0, 0).Eax;
                        if (max >= 0x1a)
                        {
                            int type = (int)((uint)X86Base.CpuId(0x1a, 0).Eax >> 24);
                            kind = type == 0x40 ? "P" : type == 0x20 ? "E" : "";
                        }
                        int leaf = max >= 0x1f ? 0x1f : 0xb;
                        if (max >= leaf)
                        {
                            var smt = X86Base.CpuId(leaf, 0);
                            if (smt.Ebx != 0) id = (int)((uint)smt.Edx >> (smt.Eax & 31));
                        }
                    }
                }
                finally { SetThreadGroupAffinity(GetCurrentThread(), ref previous, out _); }
                                int l3 = caches.FindIndex(masks => masks.Any(m => m.Group == c.Group && (m.Mask & c.Mask) == c.Mask));
                // Before Zen 3, one CCD may contain multiple L3/CCX groups; do not call those CCDs.
                if (vendor == "AuthenticAMD")
                {
                    int signature = X86Base.CpuId(1, 0).Eax;
                    int family = (signature >> 8) & 15;
                    if (family == 15) family += (signature >> 20) & 255;
                    if (family < 0x19) l3 = -1;
                }
                result.Add(new CoreTopology(id, l3, c.Efficiency, kind));
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public static string Grade(double score) => score < 15 ? "优秀" : score < 26 ? "中等" : "菜";
    public static string Format(VidReading[] readings, string vendor, List<CoreTopology> topology, List<VidReading[]>? samples = null)
    {
        if (readings.Select(r => r.Core).Distinct().Count() != readings.Length)
            throw new IOException("逐核心 VID 编号重复，无法可靠分组");
        var assignments = new List<(VidReading Reading, string Group)>();
        string? warning = null;
        if (vendor == "AuthenticAMD")
        {
            var known = topology.Where(c => c.Id >= 0 && c.L3 >= 0).ToArray();
            var l3s = known.GroupBy(c => c.L3).OrderBy(g => g.Min(c => c.Id)).Select(g => g.Key).ToArray();
            if (known.Length != topology.Count || known.Select(c => c.Id).Distinct().Count() != known.Length ||
                readings.Any(r => !known.Any(c => c.Id == r.Core)))
                warning = "CCD 归属未能确认，显示整体结果";
            else
                foreach (var r in readings)
                {
                    int l3 = known.Single(c => c.Id == r.Core).L3;
                    assignments.Add((r, l3s.Length > 1 ? "CCD" + Array.IndexOf(l3s, l3) : ""));
                }
        }
        else if (vendor == "GenuineIntel")
        {
            foreach (var r in readings)
            {
                string kind = r.Kind;
                if (kind.Length == 0)
                {
                    var matches = topology.Where(c => c.Id == r.Core).ToArray();
                    if (matches.Length == 1) kind = matches[0].Kind;
                }
                if (kind.Length == 0 && topology.Any(c => c.Kind.Length > 0))
                { warning = "P/E 核归属未能确认，显示整体结果"; break; }
                assignments.Add((r, kind == "P" ? "P核" : kind == "E" ? "E核" : ""));
            }
        }
        if (assignments.Count != readings.Length || warning != null)
            assignments = readings.Select(r => (r, "")).ToList();
        var groups = assignments.GroupBy(a => a.Group).ToArray();
        if (samples != null)
        {
            var ids = readings.Select(v => (v.Sensor, v.Core, v.Kind)).ToHashSet();
            if (samples.Count < 2 || samples.Any(s => s.Length != readings.Length ||
                !ids.SetEquals(s.Select(v => (v.Sensor, v.Core, v.Kind)))))
                throw new IOException("采样期间核心数据缺失或发生变化，请重新测试");
        }
        (double Min, double Max) Bounds(IEnumerable<VidReading> group)
        {
            var members = group.ToArray();
            if (samples == null) return (members.Min(v => v.Value), members.Max(v => v.Value));
            var ids = members.Select(v => (v.Sensor, v.Core, v.Kind)).ToHashSet();
            var bounds = samples.Select(s => s.Where(v => ids.Contains((v.Sensor, v.Core, v.Kind))).ToArray())
                .Select(s => (Min: s.Min(v => v.Value), Max: s.Max(v => v.Value))).ToArray();
            return (bounds.Average(v => v.Min), bounds.Average(v => v.Max));
        }
        string CoreReferences(IEnumerable<VidReading> group)
        {
            var members = group.OrderBy(v => v.Core).ToArray();
            var ids = members.Select(v => (v.Sensor, v.Core, v.Kind)).ToHashSet();
            var frames = (samples ?? new List<VidReading[]> { readings })
                .Select(s => s.Where(v => ids.Contains((v.Sensor, v.Core, v.Kind))).ToArray())
                .Select(s => (Values: s.ToDictionary(v => (v.Sensor, v.Core, v.Kind), v => (decimal)v.Value),
                    Minimum: s.Min(v => (decimal)v.Value))).ToArray();
            var lines = new StringBuilder("分核负压参考：");
            foreach (var core in members)
            {
                // Compare against the minimum in this group at each refresh, then average.
                // Decimal arithmetic preserves expected rounding at half-step VID values.
                decimal difference = frames.Average(f => f.Values[(core.Sensor, core.Core, core.Kind)] - f.Minimum);
                int reference = -(int)decimal.Round(difference / 0.003m, 0, MidpointRounding.AwayFromZero);
                lines.Append($"\nCore {core.Core}：{reference}");
            }
            return lines.ToString();
        }
        string period = samples == null ? "" : $"10 秒平均（{samples.Count} 次有效刷新）\n";
        if (groups.Length == 1 && groups[0].Key == "")
        {
            var bounds = Bounds(readings); double min = bounds.Min, max = bounds.Max, diff = max - min;
            return period + (warning == null ? "" : warning + "\n") +
                $"核心数：{readings.Length}    读取时间：{DateTime.Now:HH:mm:ss}\n" +
                $"最小 VID：{min:F6} V\n最大 VID：{max:F6} V\n压差：{diff:F6} V\n压差 ÷ 0.003 V：{diff / 0.003:F3}  {Grade(diff / 0.003)}\n" +
                CoreReferences(readings);
        }
        var text = new StringBuilder();
        text.Append(period); text.AppendLine($"读取时间：{DateTime.Now:HH:mm:ss}");
        foreach (var group in groups.OrderBy(g => g.Key == "P核" ? "0" : g.Key))
        {
            var bounds = Bounds(group.Select(v => v.Reading)); double min = bounds.Min, max = bounds.Max;
            string range = string.Join(",", group.Select(v => v.Reading.Core).OrderBy(n => n));
            text.AppendLine($"{group.Key}（Core {range}）");
            text.AppendLine($"VID {min:F4}～{max:F4} V  压差 {max - min:F4} V");
            text.AppendLine(group.Count() < 2 ? "仅一个有效核心，无法计算组内压差评分" : $"压差 ÷ 0.003：{(max - min) / 0.003:F3}  {Grade((max - min) / 0.003)}");
            text.AppendLine(CoreReferences(group.Select(v => v.Reading)));
        }
        return text.ToString().TrimEnd();
    }
}


