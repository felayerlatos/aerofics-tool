using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace AeroficsLauncher;

public sealed partial class ProcessSchedulerService
{
    private readonly record struct ThreadKey(int ProcessId, long ProcessStart, int ThreadId, long CreationTime);
    private sealed record ThreadSample(long CpuTime, uint PreferredCpu);
    private sealed record ThreadCandidate(ThreadKey Key, SafeThreadHandle Handle, ProcessorNumber Original,
        CpuSetDescriptor[] Cpus, long CpuTime, long BusyTime, int PreviousRank);
    private readonly Dictionary<ThreadKey, ThreadSample> _threadSamples = new();
    private readonly HashSet<(int ProcessId, long ProcessStart, int ThreadId)> _blockedThreads = new();

    // Order is interpreted here. CPU Set array order itself is NOT a Windows preference ranking.
    private void ApplyCoreOrder(Process process, string processKey, List<uint> order, uint[] processCpuSets)
    {
        var state = _saved[processKey];
        var cpus = order.Select(id => _topology!.CpuSets.First(c => c.Id == id)).ToArray();
        var candidates = new List<ThreadCandidate>();
        var live = new HashSet<ThreadKey>();
        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                using (thread)
                {
                    int tid = thread.Id;
                    var blockedKey = (process.Id, state.StartTimeUtcTicks, tid);
                    if (_blockedThreads.Contains(blockedKey)) continue;
                    SafeThreadHandle? handle = null;
                    try
                    {
                        handle = OpenThreadForPreference(tid);
                        if (handle == null) continue; // Thread exited between enumeration and opening.
                        if (GetProcessIdOfThread(handle) != (uint)process.Id) continue;
                        if (!GetThreadTimes(handle, out long created, out _, out long kernel, out long user))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        if (!GetThreadIdealProcessorEx(handle, out var original) || !GetThreadGroupAffinity(handle, out var affinity))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        var selected = GetThreadCpuSets(handle);
                        var effectiveSets = selected.Length > 0 ? selected : processCpuSets;
                        // Respect existing hard affinity and CPU Sets. Do not move a thread's primary group.
                        var allowed = cpus.Where(c => c.Group == affinity.Group &&
                            (((ulong)affinity.Mask & (1UL << c.LogicalProcessorIndex)) != 0) &&
                            (effectiveSets.Length == 0 || effectiveSets.Contains(c.Id))).ToArray();
                        if (allowed.Length == 0) continue;
                        var key = new ThreadKey(process.Id, state.StartTimeUtcTicks, tid, created);
                        long total = kernel + user;
                        _threadSamples.TryGetValue(key, out var previous);
                        long busy = previous == null ? total : Math.Max(0, total - previous.CpuTime);
                        int rank = previous == null ? int.MaxValue : order.IndexOf(previous.PreferredCpu);
                        candidates.Add(new(key, handle, original, allowed, total, busy, rank));
                        live.Add(key);
                        handle = null; // Retain the same handle through capture, journaling and application.
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { }
                    catch (Exception ex)
                    {
                        _blockedThreads.Add(blockedKey);
                        AddError($"{state.ProcessName} 线程 {tid}：{ex.Message}；本次调度不再尝试此线程");
                    }
                    finally { handle?.Dispose(); }
                }
            }

            foreach (var key in _threadSamples.Keys.Where(k => k.ProcessId == process.Id && !live.Contains(k)).ToArray())
                _threadSamples.Remove(key);
            if (_stopRequested || candidates.Count == 0) return;

            var originals = new List<ThreadRestoreState>(state.Threads ?? new());
            bool journalChanged = false;
            foreach (var c in candidates)
            {
                if (originals.Any(t => t.ThreadId == c.Key.ThreadId && t.CreationTime == c.Key.CreationTime)) continue;
                originals.RemoveAll(t => t.ThreadId == c.Key.ThreadId);
                originals.Add(new(c.Key.ThreadId, c.Key.CreationTime, c.Original.Group, c.Original.Number));
                journalChanged = true;
            }
            if (journalChanged)
            {
                _saved[processKey] = state with { Threads = originals };
                try { SaveState(); } catch { _saved[processKey] = state; throw; }
            }

            var assigned = new Dictionary<uint, int>();
            foreach (var c in candidates.OrderByDescending(c => c.BusyTime).ThenBy(c => c.PreviousRank).ThenBy(c => c.Key.ThreadId))
            {
                if (_stopRequested) break;
                var target = c.Cpus.OrderBy(cpu => assigned.GetValueOrDefault(cpu.Id)).First();
                assigned[target.Id] = assigned.GetValueOrDefault(target.Id) + 1;
                var preferred = new ProcessorNumber { Group = target.Group, Number = target.LogicalProcessorIndex };
                if (c.Original.Group != preferred.Group || c.Original.Number != preferred.Number)
                {
                    if (!SetThreadIdealProcessorEx(c.Handle, ref preferred, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        _blockedThreads.Add((c.Key.ProcessId, c.Key.ProcessStart, c.Key.ThreadId));
                        AddError($"{state.ProcessName} 线程 {c.Key.ThreadId} 首选核心设置失败：{new Win32Exception(error).Message}；不再重试");
                        continue;
                    }
                }
                _threadSamples[c.Key] = new(c.CpuTime, target.Id);
            }
        }
        finally { foreach (var c in candidates) c.Handle.Dispose(); }
    }

    private static void RestoreCoreOrder(RestoreState state)
    {
        var errors = new List<string>();
        foreach (var original in state.Threads ?? Enumerable.Empty<ThreadRestoreState>())
        {
            try
            {
                using var handle = OpenThreadForPreference(original.ThreadId);
                if (handle == null || GetProcessIdOfThread(handle) != (uint)state.ProcessId) continue;
                if (!GetThreadTimes(handle, out long created, out _, out _, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (created != original.CreationTime) continue;
                if (!GetExitCodeThread(handle, out uint exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (exitCode != 259) continue;
                var preferred = new ProcessorNumber { Group = original.OriginalGroup, Number = original.OriginalProcessor };
                if (!SetThreadIdealProcessorEx(handle, ref preferred, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { }
            catch (Exception ex) { errors.Add($"线程 {original.ThreadId}：{ex.Message}"); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorNumber { public ushort Group; public byte Number; public byte Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GroupAffinity { public nuint Mask; public ushort Group, Reserved0, Reserved1, Reserved2; }

    private sealed class SafeThreadHandle : SafeHandle
    {
        public SafeThreadHandle(nint handle) : base(IntPtr.Zero, true) { SetHandle(handle); }
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => CloseThreadHandle(handle);
    }

    private static SafeThreadHandle? OpenThreadForPreference(int tid)
    {
        var handle = new SafeThreadHandle(NativeOpenThread(0x0060, false, (uint)tid));
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        if (error == 87) return null;
        throw new Win32Exception(error);
    }

    private static uint[] GetThreadCpuSets(SafeThreadHandle handle)
    {
        bool ok = GetThreadSelectedCpuSets(handle, null, 0, out uint count);
        if (!ok && Marshal.GetLastWin32Error() != 122) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (count == 0) return Array.Empty<uint>();
        var ids = new uint[count];
        if (!GetThreadSelectedCpuSets(handle, ids, count, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return ids;
    }

    [DllImport("kernel32.dll", EntryPoint = "OpenThread", SetLastError = true)] private static extern nint NativeOpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", EntryPoint = "CloseHandle")] private static extern bool CloseThreadHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessIdOfThread(SafeThreadHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadTimes(SafeThreadHandle handle, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadIdealProcessorEx(SafeThreadHandle handle, out ProcessorNumber number);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadIdealProcessorEx(SafeThreadHandle handle, ref ProcessorNumber number, nint previous);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadGroupAffinity(SafeThreadHandle handle, out GroupAffinity affinity);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadSelectedCpuSets(SafeThreadHandle handle, uint[]? ids, uint count, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeThread(SafeThreadHandle handle, out uint exitCode);
}
