using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using AeroficsLauncher;

namespace AeroficsScheduler;

internal static partial class SelfTests
{
    internal static int Run(string[] args)
    {
        string report = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "aerofics-tests.json");
        var results = new List<object>();
        int failures = 0;
        void Test(string name, Action action)
        {
            try { action(); results.Add(new { name, status = "PASS" }); }
            catch (Exception ex) { failures++; results.Add(new { name, status = "FAIL", error = ex.ToString() }); }
        }
        Test("original encrypted resource verification", SchedulerProtection.Verify);
        Test("path selectors are exact; bare names case insensitive", () => {
            Assert(ProcessSchedulerService.MatchesSelector("GAME.exe", @"C:\Games\game.exe", "game.exe"));
            Assert(ProcessSchedulerService.MatchesSelector(@"C:\Games\game.exe", @"c:\games\GAME.EXE", "game.exe"));
            Assert(!ProcessSchedulerService.MatchesSelector(@"C:\Other\game.exe", @"C:\Games\game.exe", "game.exe"));
            Assert(!ProcessSchedulerService.MatchesSelector("", "", "game.exe"));
        });
        Test("critical processes never eligible", () => Assert(ProcessSchedulerService.IsCriticalName("LSASS.exe") && !ProcessSchedulerService.IsCriticalName("game.exe")));
        Test("affinity mask preserves bit 63", () => Assert(ProcessSchedulerService.SelectionMask(new[] { Cpu(1, 0, 0), Cpu(2, 0, 63) }) == 0x8000000000000001UL));
        Test("cross group mask rejected", () => Throws(() => ProcessSchedulerService.SelectionMask(new[] { Cpu(1, 0, 0), Cpu(2, 1, 0) })));
        Test("unknown CPU IDs, realtime priority and multi group hard affinity rejected", () => {
            var topology = new CpuSetTopology { CpuSets = new[] { Cpu(1, 0, 0), Cpu(2, 1, 0) } };
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { CustomCpuSetIds = new() { 9 } }, topology));
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { GamePriority = 256 }, topology));
            Throws(() => ProcessSchedulerService.ValidateSettings(new() { StrictAffinity = true }, topology));
        });
        Test("startup command quotes spaces and rejects host or relative paths", () => {
            Assert(StartupSettings.Command(@"C:\Some App\scheduler.exe") == '"' + @"C:\Some App\scheduler.exe" + '"');
            Throws(() => StartupSettings.Command("scheduler.exe")); Throws(() => StartupSettings.Command(@"C:\dotnet.exe"));
        });
        Test("power plan enumeration is read only", () => Assert(PowerPlanService.List().Any(p => p.Id == PowerPlanService.Active())));
        Test("settings round trip; journal folder confined to supplied test directory", () => {
            var dir = TestDir(report, "settings");
            using var service = new ProcessSchedulerService(stateDirectory: dir);
            service.SaveSettings(new() { CustomCpuSetIds = new() { 42 }, GamePriority = 32768, Enforce = true, StrictAffinity = true, GameProcesses = new() { @"C:\Game\game.exe" } });
            var loaded = service.LoadSettings(); Assert(loaded.CustomCpuSetIds.SequenceEqual(new uint[] { 42 }) && loaded.GamePriority == 32768 && loaded.Enforce && loaded.StrictAffinity);
            loaded.CustomCpuSetIds.Clear(); Assert(service.LoadSettings().CustomCpuSetIds.Count == 1);
        });
        Test("native topology has unique group/LP and valid cache groups", () => {
            var t = CpuSetTopologyReader.Read(); Assert(t.IsReliable && t.CpuSets.Select(c => (c.Group, c.LogicalProcessorIndex)).Distinct().Count() == t.CpuSets.Count);
            Assert(t.Ccds.All(g => g.Select(c => (c.Group, c.LastLevelCacheIndex)).Distinct().Count() == 1));
        });
        Test("child only: CPU Sets + priority + guard + original state restoration", () => NativeRoundTrip(report, false));
        Test("child only: hard affinity restoration", () => {
            if (CpuSetTopologyReader.Read().CpuSets.Select(c => c.Group).Distinct().Count() != 1) { results.Add(new { name = "hard affinity native", status = "SKIP: multi group" }); return; }
            NativeRoundTrip(report, true);
        });
        Test("child only: named background receives complement pool and restores CPU Sets", () => {
            using var child = Child();
            try {
                var t = CpuSetTopologyReader.Read(); Assert(t.CpuSets.Count > 1);
                var original = ReadSets(child.Handle);
                string dir = TestDir(report, "background");
                using var service = new ProcessSchedulerService(null, dir, () => new[] { Process.GetProcessById(child.Id) });
                service.Start(new() { CustomCpuSetIds = new() { t.CpuSets[0].Id }, BackgroundProcesses = new() { child.MainModule!.FileName } });
                Wait(() => service.Status.BackgroundCount == 1, () => string.Join(";", service.Status.Errors));
                Assert(ReadSets(child.Handle).Order().SequenceEqual(t.CpuSets.Skip(1).Select(c => c.Id).Order()));
                service.StopAndRestore(); Assert(ReadSets(child.Handle).SequenceEqual(original));
            } finally { if (!child.HasExited) child.Kill(); }
        });
        Test("child only: exclusion rules prevent changes", () => {
            using var child = Child(); var dir = TestDir(report, "excluded");
            try {
                using var service = new ProcessSchedulerService(null, dir, () => new[] { Process.GetProcessById(child.Id) });
                service.Start(new() { GameProcesses = new() { child.MainModule!.FileName }, ExcludedProcesses = new() { child.MainModule!.FileName }, GamePriority = 32768 });
                Thread.Sleep(1200); Assert(service.Status.PendingRestoreCount == 0); service.StopAndRestore();
            } finally { if (!child.HasExited) child.Kill(); }
        });
        Test("child only: stale process identity in recovery never modified", () => {
            using var child = Child(); var dir = TestDir(report, "stale"); var original = child.PriorityClass;
            try {
                var state = new RestoreState(child.Id, 1, child.MainModule!.FileName, child.ProcessName + ".exe", new(), "1", 128, true);
                File.WriteAllText(Path.Combine(dir, "restore.json"), JsonSerializer.Serialize(new[] { state }));
                using var service = new ProcessSchedulerService(stateDirectory: dir); service.StopAndRestore(); child.Refresh();
                Assert(service.Status.PendingRestoreCount == 0 && child.PriorityClass == original);
            } finally { if (!child.HasExited) child.Kill(); }
        });
        CoreOrderTests(report, Test);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { failures, results, scope = "Uploaded executables never executed. Only this rebuilt program and its own temporary child used. No power plan or startup changes." }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
    private static void NativeRoundTrip(string report, bool hard)
    {
        using var child = Child();
        try
        {
            string dir = TestDir(report, hard ? "hard" : "soft");
            child.Refresh(); var originalPriority = child.PriorityClass; var originalAffinity = child.ProcessorAffinity;
            var originalSets = ReadSets(child.Handle);
            var topology = CpuSetTopologyReader.Read();
            var cpu = topology.CpuSets.First(c => c.Group == 0 && ((ulong)originalAffinity.ToInt64() & (1UL << c.LogicalProcessorIndex)) != 0);
            var settings = new SchedulerSettings { CustomCpuSetIds = new() { cpu.Id }, GamePriority = 32768, Enforce = true, StrictAffinity = hard, GameProcesses = new() { child.MainModule!.FileName } };
            using var service = new ProcessSchedulerService(null, dir, () => new[] { Process.GetProcessById(child.Id) });
            var trace = new System.Collections.Concurrent.ConcurrentQueue<string>(); service.Diagnostic = trace.Enqueue;
            service.Start(settings);
            Wait(() => { child.Refresh(); return service.Status.PendingRestoreCount == 1 && child.PriorityClass == ProcessPriorityClass.AboveNormal; }, () => string.Join(";", service.Status.Errors) + " TRACE: " + string.Join(";", trace));
            Assert(File.ReadAllText(Path.Combine(dir, "restore.json")).Contains(child.Id.ToString()));
            Assert(ReadSets(child.Handle).SequenceEqual(new uint[] { cpu.Id }));
            if (hard) { child.Refresh(); Assert((ulong)child.ProcessorAffinity.ToInt64() == (1UL << cpu.LogicalProcessorIndex)); }
            child.PriorityClass = ProcessPriorityClass.Normal;
            Wait(() => { child.Refresh(); return child.PriorityClass == ProcessPriorityClass.AboveNormal; }, () => "guard did not reapply");
            service.StopAndRestore(); child.Refresh();
            Assert(service.Status.PendingRestoreCount == 0 && child.PriorityClass == originalPriority && child.ProcessorAffinity == originalAffinity);
            Assert(ReadSets(child.Handle).SequenceEqual(originalSets));
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } }
    }
    private static Process Child()
    {
        string exe = Path.GetFileName(Environment.ProcessPath!).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ? Path.Combine(AppContext.BaseDirectory, "Aerofics.ThreadScheduler.Extended.exe") : Environment.ProcessPath!;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--test-child");
        string ready = Path.Combine(Path.GetTempPath(), "aerofics-child-" + Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add("--ready"); start.ArgumentList.Add(ready);
        var child = Process.Start(start) ?? throw new Exception("cannot start own child");
        try { Wait(() => File.Exists(ready), () => "child did not signal ready"); child.Refresh(); return child; }
        catch { if (!child.HasExited) child.Kill(); child.Dispose(); throw; }
        finally { if (File.Exists(ready)) File.Delete(ready); }
    }
    private static string TestDir(string report, string name) { var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "test-state", name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
    private static CpuSetDescriptor Cpu(uint id, ushort group, byte lp) => new() { Id = id, Group = group, LogicalProcessorIndex = lp };
    private static uint[] ReadSets(nint process)
    {
        bool ok = GetProcessDefaultCpuSets(process, null, 0, out uint count);
        if (!ok && Marshal.GetLastWin32Error() != 122) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (count == 0) return Array.Empty<uint>();
        var data = new uint[count]; if (!GetProcessDefaultCpuSets(process, data, count, out _)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); return data;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessDefaultCpuSets(nint process, uint[]? ids, uint count, out uint returned);
    private static void Wait(Func<bool> condition, Func<string> error) { var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < 5000) { if (condition()) return; Thread.Sleep(100); } throw new Exception(error()); }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation rejection"); }
}
