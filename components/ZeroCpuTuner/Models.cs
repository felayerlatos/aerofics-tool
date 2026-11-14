using System.Text.Json;

namespace ZeroCpuTuner;

public sealed class CpuIdentity
{
    public int Family { get; set; }
    public int Model { get; set; }
    public int Stepping { get; set; }
    public uint SmuTableVersion { get; set; }
}
public sealed class CoreSlot
{
    public int UiIndex { get; set; }
    public int PhysicalSlot { get; set; }
    public int CcdIndex { get; set; }
    public int CoreOnCcd { get; set; }
    public int ProcessorGroup { get; set; }
    public int ProcessorNumber { get; set; }
    public ulong ProcessorMask { get; set; }
}
public sealed class CpuTopology
{
    public int Schema { get; set; }
    public CpuIdentity Identity { get; set; } = new();
    public int ActiveCoreCount { get; set; }
    public int LogicalProcessorCount { get; set; }
    public int MaximumPhysicalSlots { get; set; }
    public CoreSlot[] CoreSlots { get; set; } = [];
    public string MapHash { get; set; } = "";
    public void Validate()
    {
        if (Schema != 1 || ActiveCoreCount is < 1 or > 64 || CoreSlots.Length != ActiveCoreCount ||
            MapHash.Length != 64 || !MapHash.All(Uri.IsHexDigit) ||
            !CoreSlots.Select(c => c.UiIndex).SequenceEqual(Enumerable.Range(0, ActiveCoreCount)) ||
            CoreSlots.Select(c => c.PhysicalSlot).Distinct().Count() != ActiveCoreCount ||
            CoreSlots.Any(c => c.PhysicalSlot < 0 || c.PhysicalSlot >= MaximumPhysicalSlots || c.ProcessorMask == 0 || c.CcdIndex < 0))
            throw new InvalidDataException("后端返回的 CPU 拓扑不完整，未允许写入。");
    }
}
public sealed class CoSnapshot
{
    public int Schema { get; set; }
    public int[] Offsets { get; set; } = [];
    public CpuTopology Topology { get; set; } = new();
    public void Validate()
    {
        Topology.Validate();
        if (Schema != 1 || Offsets.Length != Topology.ActiveCoreCount || Offsets.Any(x => x is < -50 or > 10))
            throw new InvalidDataException("后端返回的 CO 参数不完整或超出本组件范围。");
    }
}
public sealed class PboState
{
    public int Schema { get; set; }
    public string CpuId { get; set; } = "";
    public string CpuName { get; set; } = "";
    public bool Supported { get; set; }
    public int? CurrentMhz { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class PboReceipt
{
    public int Schema { get; set; }
    public string CpuId { get; set; } = "";
    public int PreviousMhz { get; set; }
    public int RequestedMhz { get; set; }
    public int ActualMhz { get; set; }
}
public sealed class Profile
{
    public int Schema { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public string CpuName { get; set; } = "";
    public CoSnapshot Snapshot { get; set; } = new();
    public int? PboMhz { get; set; }
    public string Status { get; set; } = "候选参数，未验证";
    public bool Pending { get; set; }
    public string RecoveryScope { get; set; } = "CO";
}
public sealed record VidFrame(long Sequence, DateTime UtcTime, double[] Values);
public sealed record VidWindow(double[] Median, double[] Range, int Count);
public sealed record AlignmentResult(int[] Offsets, double SpreadMv);

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options) ?? throw new InvalidDataException("JSON 文件为空。");
    public static void Write<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, file, true);
    }
    public static T ParseOutput<T>(string output)
    {
        // The backend emits diagnostics followed by a compact JSON line.
        foreach (var line in output.Split('\n').Reverse())
        {
            if (!line.TrimStart().StartsWith('{')) continue;
            try { return JsonSerializer.Deserialize<T>(line, Options) ?? throw new JsonException(); }
            catch (JsonException) { }
        }
        throw new InvalidDataException("后端未返回可解析的 JSON：" + output[..Math.Min(1800, output.Length)]);
    }
}
