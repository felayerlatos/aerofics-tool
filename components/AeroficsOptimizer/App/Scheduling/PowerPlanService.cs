using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AeroficsLauncher;

internal sealed record PowerPlan(Guid Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record PowerRestore(Guid Original, Guid Applied);

internal sealed class PowerPlanService
{
    private readonly string _journal;
    public bool Pending => File.Exists(_journal);
    internal PowerPlanService(string directory) => _journal = Path.Combine(directory, "power-restore.json");
    internal static Guid Active()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    internal static List<PowerPlan> List()
    {
        var result = new List<PowerPlan>();
        for (uint i = 0; ; i++)
        {
            uint size = 16;
            uint status = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, out Guid id, ref size);
            if (status == 259) break;
            Check(status);
            uint length = 0;
            uint nameStatus = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref length);
            if (nameStatus != 0 && nameStatus != 234) Check(nameStatus);
            var buffer = new byte[length];
            Check(PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref length));
            result.Add(new(id, Encoding.Unicode.GetString(buffer).TrimEnd('\0')));
        }
        return result;
    }
    internal void Apply(Guid id)
    {
        var old = Pending ? JsonSerializer.Deserialize<PowerRestore>(File.ReadAllText(_journal))! : new PowerRestore(Active(), id);
        // Persist the previous plan before changing any system setting.
        Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
        File.WriteAllText(_journal + ".new", JsonSerializer.Serialize(old with { Applied = id }));
        File.Move(_journal + ".new", _journal, true);
        Check(PowerSetActiveScheme(IntPtr.Zero, ref id));
    }
    internal void Restore()
    {
        if (!Pending) return;
        var state = JsonSerializer.Deserialize<PowerRestore>(File.ReadAllText(_journal))!;
        var active = Active();
        if (active != state.Applied && active != state.Original)
            throw new InvalidOperationException("当前电源计划已被其他操作更改，未覆盖它。请在电源页选择原计划后再点击恢复：" + state.Original);
        var id = state.Original;
        Check(PowerSetActiveScheme(IntPtr.Zero, ref id));
        File.Delete(_journal);
    }
    private static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint id);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, ref Guid id);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint access, uint index, out Guid buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(nint root, ref Guid scheme, nint subgroup, nint setting, byte[]? buffer, ref uint length);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
