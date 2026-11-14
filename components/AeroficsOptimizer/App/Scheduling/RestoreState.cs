using System.Collections.Generic;

namespace AeroficsLauncher;

public sealed record ThreadRestoreState(int ThreadId, long CreationTime, ushort OriginalGroup, byte OriginalProcessor);

public sealed record RestoreState(int ProcessId, long StartTimeUtcTicks, string ExecutablePath, string ProcessName,
    List<uint> OriginalCpuSetIds, string OriginalAffinityMaskHex, uint OriginalPriority = 0,
    bool AffinityChanged = false, bool CpuSetsChanged = true, bool PriorityChanged = true,
    List<ThreadRestoreState>? Threads = null);
