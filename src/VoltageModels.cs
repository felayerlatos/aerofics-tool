using System.Collections.ObjectModel;

namespace AeroficsLauncher;

public enum VoltageSource
{
    HwInfoSharedMemory,
    AmdSmuReadOnly
}

public sealed record VoltageCoreSample(int CoreId, double Voltage, int CcdIndex = -1, string Kind = "");

public sealed record VoltageSnapshot(
    VoltageSource Source,
    long Timestamp,
    string Vendor,
    int CpuFamily,
    int CpuModel,
    string CpuName,
    IReadOnlyList<VoltageCoreSample> Cores,
    IReadOnlyList<CoreTopology> Topology,
    string Note = "");

public interface IVoltageProvider
{
    string Name { get; }
    bool TryRead(out VoltageSnapshot snapshot, out string error);
}

internal static class VoltageText
{
    internal static string SourceName(VoltageSource source) => source switch
    {
        VoltageSource.HwInfoSharedMemory => "HWiNFO Shared Memory",
        VoltageSource.AmdSmuReadOnly => "AMD SMU 只读接口",
        _ => source.ToString()
    };
}
