using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using AeroficsLauncher;

namespace AeroficsScheduler;

internal static class CoreOrderTestChild
{
    [DllImport("kernel32.dll")] private static extern int GetCurrentThreadId();
    internal static void Run(string[] args)
    {
        string ready = args.SkipWhile(a => a != "--ready").Skip(1).First();
        string control = args.SkipWhile(a => a != "--control").Skip(1).First();
        int firstId = 0, lateId = 0, active = 0;
        var first = new Thread(() => { Volatile.Write(ref firstId, GetCurrentThreadId()); Work(() => Volatile.Read(ref active) == 0); }) { IsBackground = true };
        first.Start();
        while (Volatile.Read(ref firstId) == 0) Thread.Sleep(10);
        File.WriteAllText(ready, firstId.ToString());
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 30000)
        {
            if (File.Exists(control) && lateId == 0)
            {
                Volatile.Write(ref active, 1);
                var late = new Thread(() => { Volatile.Write(ref lateId, GetCurrentThreadId()); Work(() => true); }) { IsBackground = true };
                late.Start();
                while (Volatile.Read(ref lateId) == 0) Thread.Sleep(10);
                File.WriteAllText(ready + ".late", lateId.ToString());
            }
            Thread.Sleep(50);
        }
    }
    private static void Work(Func<bool> active)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 30000)
        {
            if (active()) { var work = Stopwatch.StartNew(); while (work.ElapsedMilliseconds < 25) Thread.SpinWait(1000); }
            Thread.Sleep(75);
        }
    }
}

internal static partial class SelfTests
{
    private static void CoreOrderTests(string report, Action<string, Action> test)
    {
        test("independent core orders survive save, load and clone", () => {
            using var service = new ProcessSchedulerService(stateDirectory: TestDir(report, "orders-settings"));
            service.SaveSettings(new() { GameCoreOrderEnabled = true, BackgroundCoreOrderEnabled = true,
                GameCoreOrder = new() { 3, 1, 2 }, BackgroundCoreOrder = new() { 2, 1, 3 } });
            var loaded = service.LoadSettings();
            Assert(loaded.GameCoreOrderEnabled && loaded.BackgroundCoreOrderEnabled);
            Assert(loaded.GameCoreOrder.SequenceEqual(new uint[] { 3, 1, 2 }));
            Assert(loaded.BackgroundCoreOrder.SequenceEqual(new uint[] { 2, 1, 3 }));
            loaded.GameCoreOrder.Clear(); Assert(service.LoadSettings().GameCoreOrder.Count == 3);
        });
        test("empty, duplicate and unavailable enabled core orders rejected", () => {
            var t = new CpuSetTopology { CpuSets = new[] { Cpu(1, 0, 0), Cpu(2, 0, 1) } };
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { GameCoreOrderEnabled = true }, t));
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { GameCoreOrderEnabled = true, GameCoreOrder = new() { 1, 1 } }, t));
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { BackgroundCoreOrderEnabled = true, BackgroundCoreOrder = new() { 9 } }, t));
        });
        test("native game order: busy thread, late thread, no affinity lock, full restore", () => NativeCoreOrder(report, true));
        test("native non-game order independent of game order and isolation toggle", () => NativeCoreOrder(report, false));
        test("native order respects existing hard affinity and thread CPU Sets", () => NativeRestrictedOrder(report));
    }

    private sealed class PreferenceChild : IDisposable
    {
        internal readonly Process Process;
        internal readonly string Ready, Control;
        internal int BusyId => int.Parse(File.ReadAllText(Ready));
        internal PreferenceChild()
        {
            string prefix = Path.Combine(Path.GetTempPath(), "aerofics-order-" + Guid.NewGuid().ToString("N"));
            Ready = prefix + ".ready"; Control = prefix + ".control";
            string exe = Path.GetFileName(Environment.ProcessPath!).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(AppContext.BaseDirectory, "Aerofics.ThreadScheduler.Extended.exe") : Environment.ProcessPath!;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--core-order-child", "--ready", Ready, "--control", Control }) start.ArgumentList.Add(arg);
            Process = System.Diagnostics.Process.Start(start)!;
            try { Wait(() => File.Exists(Ready) && int.TryParse(File.ReadAllText(Ready), out _), () => "preference child not ready"); }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(); Process.WaitForExit(3000); }
            Process.Dispose();
            foreach (var path in new[] { Ready, Ready + ".late", Control }) if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void NativeCoreOrder(string report, bool game)
    {
        using var child = new PreferenceChild();
        var p = child.Process;
        p.Refresh(); var beforeAffinity = p.ProcessorAffinity; var beforeSets = ReadSets(p.Handle); var beforePriority = p.PriorityClass;
        var topology = CpuSetTopologyReader.Read();
        var original = ReadIdeal(child.BusyId);
        var cpus = topology.CpuSets.Where(c => c.Group == original.Group &&
            (((ulong)beforeAffinity.ToInt64() & (1UL << c.LogicalProcessorIndex)) != 0)).OrderBy(c => c.LogicalProcessorIndex).ToArray();
        Assert(cpus.Length >= 2);
        var settings = new SchedulerSettings { GameCoreOrderEnabled = true, BackgroundCoreOrderEnabled = true,
            GameCoreOrder = cpus.Select(c => c.Id).ToList(), BackgroundCoreOrder = cpus.Reverse().Select(c => c.Id).ToList(),
            BackgroundIsolation = false, GameCcd = "All", Enforce = false };
        string path = p.MainModule!.FileName;
        if (game) { settings.GameProcesses.Add(path); settings.BackgroundProcesses.Add(path); } // game classification wins
        else settings.BackgroundProcesses.Add(path);
        var target = game ? cpus[0] : cpus[^1];
        string dir = TestDir(report, game ? "native-game-order" : "native-background-order");
        using var service = new ProcessSchedulerService(null, dir, () => new[] { Process.GetProcessById(p.Id) });
        service.Start(settings);
        Wait(() => (game ? service.Status.GameCount : service.Status.BackgroundCount) == 1 && SameIdeal(ReadIdeal(child.BusyId), target),
            () => "busy thread did not use its category's first core: " + string.Join(";", service.Status.Errors));
        p.Refresh(); Assert(p.ProcessorAffinity == beforeAffinity && p.PriorityClass == beforePriority && ReadSets(p.Handle).SequenceEqual(beforeSets));
        Assert(game ? service.Status.BackgroundCount == 0 : service.Status.GameCount == 0);
        File.WriteAllText(child.Control, "new worker");
        Wait(() => File.Exists(child.Ready + ".late") && int.TryParse(File.ReadAllText(child.Ready + ".late"), out _), () => "new thread missing");
        int lateId = int.Parse(File.ReadAllText(child.Ready + ".late"));
        Wait(() => SameIdeal(ReadIdeal(lateId), target), () => "late busy thread not managed without guard");
        string journal = File.ReadAllText(Path.Combine(dir, "restore.json"));
        var snapshot = JsonSerializer.Deserialize<List<RestoreState>>(journal)!.Single();
        Assert(snapshot.Threads!.Any(t => t.ThreadId == lateId));
        service.StopAndRestore();
        Assert(service.Status.PendingRestoreCount == 0);
        Assert(ReadIdeal(child.BusyId).Group == original.Group && ReadIdeal(child.BusyId).Number == original.Number);
        foreach (var t in snapshot.Threads!)
        {
            nint handle = OpenNativeThread(0x40, false, (uint)t.ThreadId);
            if (handle == 0) continue;
            try {
                if (!NativeThreadTimes(handle, out long created, out _, out _, out _) || created != t.CreationTime) continue;
                if (!NativeReadIdeal(handle, out var ideal)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Assert(ideal.Group == t.OriginalGroup && ideal.Number == t.OriginalProcessor);
            } finally { CloseNativeThread(handle); }
        }
        // Reopen a persisted journal, as after an interrupted run.
        string recoveryDir = TestDir(report, "order-recovery");
        File.WriteAllText(Path.Combine(recoveryDir, "restore.json"), journal);
        WriteIdeal(child.BusyId, target);
        using var recovered = new ProcessSchedulerService(stateDirectory: recoveryDir);
        recovered.StopAndRestore();
        Assert(recovered.Status.PendingRestoreCount == 0);
        Assert(ReadIdeal(child.BusyId).Group == original.Group && ReadIdeal(child.BusyId).Number == original.Number);
    }

    private static void NativeRestrictedOrder(string report)
    {
        using var child = new PreferenceChild();
        var p = child.Process; p.Refresh();
        var before = p.ProcessorAffinity;
        var group = ReadIdeal(child.BusyId).Group;
        var cpus = CpuSetTopologyReader.Read().CpuSets.Where(c => c.Group == group &&
            (((ulong)before.ToInt64() & (1UL << c.LogicalProcessorIndex)) != 0)).OrderBy(c => c.LogicalProcessorIndex).ToArray();
        Assert(cpus.Length >= 2);
        var allowed = cpus[^1];
        p.ProcessorAffinity = new IntPtr((long)(1UL << allowed.LogicalProcessorIndex));
        using (var service = new ProcessSchedulerService(null, TestDir(report, "order-hard-existing"), () => new[] { Process.GetProcessById(p.Id) }))
        {
            service.Start(new() { GameCoreOrderEnabled = true, GameCoreOrder = cpus.Select(c => c.Id).ToList(), GameProcesses = new() { p.MainModule!.FileName } });
            Wait(() => service.Status.GameCount == 1 && SameIdeal(ReadIdeal(child.BusyId), allowed), () => string.Join(";", service.Status.Errors));
            service.StopAndRestore(); Assert(service.Status.PendingRestoreCount == 0);
            p.Refresh(); Assert(p.ProcessorAffinity.ToInt64() == (long)(1UL << allowed.LogicalProcessorIndex));
        }
        p.ProcessorAffinity = before;
        nint thread = OpenNativeThread(0x60, false, (uint)child.BusyId);
        Assert(thread != 0);
        try
        {
            Assert(NativeSetThreadSets(thread, new[] { allowed.Id }, 1));
            using var service = new ProcessSchedulerService(null, TestDir(report, "order-thread-sets-existing"), () => new[] { Process.GetProcessById(p.Id) });
            service.Start(new() { GameCoreOrderEnabled = true, GameCoreOrder = cpus.Select(c => c.Id).ToList(), GameProcesses = new() { p.MainModule!.FileName } });
            Wait(() => service.Status.GameCount == 1 && SameIdeal(ReadIdeal(child.BusyId), allowed), () => string.Join(";", service.Status.Errors));
            service.StopAndRestore(); Assert(service.Status.PendingRestoreCount == 0);
            var actual = new uint[2]; Assert(NativeGetThreadSets(thread, actual, 2, out uint count) && count == 1 && actual[0] == allowed.Id);
        }
        finally { NativeSetThreadSets(thread, null, 0); CloseNativeThread(thread); }
    }

    private static bool SameIdeal(ProcessSchedulerService.ProcessorNumber number, CpuSetDescriptor cpu) => number.Group == cpu.Group && number.Number == cpu.LogicalProcessorIndex;
    private static ProcessSchedulerService.ProcessorNumber ReadIdeal(int tid)
    {
        nint handle = OpenNativeThread(0x40, false, (uint)tid);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { if (!NativeReadIdeal(handle, out var result)) throw new Win32Exception(Marshal.GetLastWin32Error()); return result; }
        finally { CloseNativeThread(handle); }
    }
    private static void WriteIdeal(int tid, CpuSetDescriptor cpu)
    {
        nint handle = OpenNativeThread(0x20, false, (uint)tid);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { var number = new ProcessSchedulerService.ProcessorNumber { Group = cpu.Group, Number = cpu.LogicalProcessorIndex }; Assert(NativeWriteIdeal(handle, ref number, 0)); }
        finally { CloseNativeThread(handle); }
    }
    [DllImport("kernel32.dll", EntryPoint = "OpenThread", SetLastError = true)] private static extern nint OpenNativeThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle")] private static extern bool CloseNativeThread(nint handle);
    [DllImport("kernel32.dll", EntryPoint = "GetThreadIdealProcessorEx", SetLastError = true)] private static extern bool NativeReadIdeal(nint handle, out ProcessSchedulerService.ProcessorNumber number);
    [DllImport("kernel32.dll", EntryPoint = "SetThreadIdealProcessorEx", SetLastError = true)] private static extern bool NativeWriteIdeal(nint handle, ref ProcessSchedulerService.ProcessorNumber number, nint previous);
    [DllImport("kernel32.dll", EntryPoint = "GetThreadTimes", SetLastError = true)] private static extern bool NativeThreadTimes(nint handle, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "SetThreadSelectedCpuSets", SetLastError = true)] private static extern bool NativeSetThreadSets(nint handle, uint[]? ids, uint count);
    [DllImport("kernel32.dll", EntryPoint = "GetThreadSelectedCpuSets", SetLastError = true)] private static extern bool NativeGetThreadSets(nint handle, uint[] ids, uint count, out uint returned);
}
