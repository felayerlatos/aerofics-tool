using System.Diagnostics;

namespace ZeroCpuTuner;

public sealed class TuningEngine : IAsyncDisposable
{
    private readonly SmuBackend backend = new();
    private SensorSession? sensor;
    public CoSnapshot? Snapshot { get; private set; }
    public PboState? Pbo { get; private set; }
    public Profile? SessionStart { get; private set; }
    public string Validation { get; private set; } = "未测试";
    public event Action<string>? Message;
    public event Action? StateChanged;
    public event Action<int, TimeSpan>? StressProgress;
    public VidFrame? Frame => sensor?.Read();
    private void Log(string message) { AppPaths.Log(message); Message?.Invoke(message); }
    private void Notify() => StateChanged?.Invoke();
    public async Task Connect(CancellationToken ct)
    {
        if (!AppPaths.IsAdmin) throw new UnauthorizedAccessException("读取 CPU 硬件需要管理员权限。请点击右上角“以管理员重开”。");
        await Task.Run(AppPaths.Extract, ct);
        await StopSensor();
        ct.ThrowIfCancellationRequested();
        Snapshot = await backend.ReadCo();
        try { Pbo = await backend.ReadPbo(); }
        catch (Exception e) { Pbo = null; Log("CO 读取成功；PBO 暂不可用：" + e.Message); }
        if (SessionStart == null)
        {
            SessionStart = Capture("本次连接初值");
            SessionStart.RecoveryScope = "CPU";
            JsonFiles.Write(AppPaths.Session, SessionStart);
        }
        Validation = "已读取当前 CO，稳定性未测试";
        Notify();
        await ResumeSensor(ct);
        Log($"已连接 {Snapshot.Topology.ActiveCoreCount} 个物理核心；SMU 0x{Snapshot.Topology.Identity.SmuTableVersion:X6}。");
    }
    private Profile Capture(string status) => new()
    {
        CpuName = Pbo?.CpuName ?? "AMD Ryzen",
        Snapshot = new CoSnapshot { Schema = 1, Offsets = Snapshot!.Offsets.ToArray(), Topology = Snapshot.Topology },
        PboMhz = Pbo?.CurrentMhz, Status = status
    };
    private async Task StopSensor()
    {
        if (sensor == null) return;
        var old = sensor; sensor = null;
        await old.DisposeAsync();
    }
    private async Task ResumeSensor(CancellationToken ct = default, bool required = false)
    {
        await StopSensor();
        if (Snapshot == null) return;
        var candidate = new SensorSession();
        try
        {
            await candidate.Start(Snapshot.Topology, ct);
            sensor = candidate;
        }
        catch (Exception e)
        {
            await candidate.DisposeAsync();
            if (e is OperationCanceledException || required) throw;
            Log("CO 状态已保留；VID 连接未就绪：" + e.Message);
        }
    }
    private async Task<CoSnapshot> Fresh()
    {
        await StopSensor();
        var fresh = await backend.ReadCo();
        if (Snapshot != null && Snapshot.Topology.MapHash != fresh.Topology.MapHash)
            throw new IOException("CPU 拓扑与连接时不同，请重新启动组件。");
        Snapshot = fresh;
        return fresh;
    }
    private async Task ApplyStep(int[] offsets, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var before = await Fresh();
        if (offsets.Length != before.Offsets.Length) throw new ArgumentException("CO 数量与核心数不同。");
        if (!before.Offsets.SequenceEqual(offsets))
        {
            // Once a write starts, finish its readback before honoring cancellation.
            await backend.WriteCo(offsets, before.Topology.MapHash);
            Snapshot = await backend.VerifyCo(offsets, before.Topology.MapHash);
            Validation = "候选参数，未验证";
            Log("CO 写入并回读：" + string.Join(", ", offsets));
        }
        Notify();
    }
    private Profile Begin(string reason, string scope = "CO")
    {
        var recovery = Capture(reason);
        recovery.Pending = true;
        recovery.RecoveryScope = scope;
        JsonFiles.Write(AppPaths.Recovery, recovery);
        return recovery;
    }
    private static void Complete(Profile recovery)
    {
        recovery.Pending = false;
        JsonFiles.Write(AppPaths.Recovery, recovery);
    }
    private async Task Rollback(Profile recovery)
    {
        await StopSensor();
        var current = await backend.ReadCo();
        if (current.Topology.MapHash != recovery.Snapshot.Topology.MapHash) throw new IOException("恢复点与当前 CPU 拓扑不同，未写入恢复值。");
        Snapshot = current;
        if (recovery.RecoveryScope is "CO" or "CPU")
        {
            if (!current.Offsets.SequenceEqual(recovery.Snapshot.Offsets))
                await backend.WriteCo(recovery.Snapshot.Offsets, current.Topology.MapHash);
            Snapshot = await backend.VerifyCo(recovery.Snapshot.Offsets, current.Topology.MapHash);
        }
        if (recovery.RecoveryScope is "PBO" or "CPU" && recovery.PboMhz.HasValue)
        {
            var pbo = await backend.ReadPbo();
            Pbo = pbo.CurrentMhz == recovery.PboMhz ? pbo : await backend.WritePbo(pbo, recovery.PboMhz.Value);
        }
        Complete(recovery);
        Validation = "已恢复参数，稳定性状态未验证";
        Log("已回读确认恢复到操作前参数。");
        Notify();
    }
    private async Task FailAndRestore(Profile recovery, Exception original)
    {
        try { await Rollback(recovery); }
        catch (Exception restore)
        {
            throw new IOException("操作失败且恢复未完成。请使用恢复记录检查当前参数。\n原错误：" + original.Message + "\n恢复错误：" + restore.Message, original);
        }
    }
    public async Task ApplyManual(int[] offsets, CancellationToken ct)
    {
        await Fresh();
        var recovery = Begin("手动 CO 写入前");
        try
        {
            await ApplyStep(offsets, ct);
            ct.ThrowIfCancellationRequested();
            Complete(recovery);
            SaveCurrent();
        }
        catch (Exception e) { await FailAndRestore(recovery, e); throw; }
        finally { await ResumeSensor(); }
    }
    public async Task ApplyPbo(int target, CancellationToken ct)
    {
        if (SessionStart?.PboMhz is not int baseline || target < baseline - 800 || target > baseline + 200)
            throw new ArgumentException("PBO 限制为本次初值 -800～+200 MHz。");
        await Fresh();
        Pbo = await backend.ReadPbo();
        var recovery = Begin("PBO 写入前", "PBO");
        try
        {
            ct.ThrowIfCancellationRequested();
            Pbo = await backend.WritePbo(Pbo, target);
            ct.ThrowIfCancellationRequested();
            Validation = "PBO 已改变，稳定性未验证";
            Complete(recovery);
            SaveCurrent();
            Log($"PBO 上限已回读为 {Pbo.CurrentMhz} MHz。");
        }
        catch (Exception e) { await FailAndRestore(recovery, e); throw; }
        finally { Notify(); await ResumeSensor(); }
    }
    public void SaveCurrent(string? path = null)
    {
        if (Snapshot == null) throw new InvalidOperationException("请先读取硬件。");
        JsonFiles.Write(path ?? AppPaths.Profile, Capture(Validation));
    }
    public async Task Restore(Profile profile)
    {
        profile.Snapshot.Validate();
        await Rollback(profile);
        SaveCurrent();
        await ResumeSensor();
    }
    private static bool AutoRecipe(CpuTopology t)
    {
        var i = t.Identity;
        return i.Family == 26 && i.Model == 68 && i.SmuTableVersion is 0x620105 or 0x620205 or 0x621102 or 0x621202 ||
            i.Family == 25 && i.Model == 97 && i.SmuTableVersion is 0x540005 or 0x540208;
    }
    private async Task Qualify(CancellationToken ct)
    {
        var t = Snapshot!.Topology;
        if (!AutoRecipe(t)) throw new NotSupportedException("当前 SMU 表仅支持观察和手动 CO；自动分核暂支持原包的六种 LegacyCoreVid 配方。");
        await ResumeSensor(ct, true);
        foreach (var core in t.CoreSlots)
        {
            for (int cycle = 0; cycle < 2; cycle++)
            {
                Log($"核心 {core.UiIndex} 动态响应 {cycle + 1}/2（尚未写入 CO）");
                var before = await sensor!.Window(5, ct);
                VidWindow loaded;
                using var worker = CoreLoad.Start(core);
                try
                {
                    await Task.Delay(250, ct);
                    if (worker.HasExited) throw new IOException("单核响应负载未能启动，代码 " + worker.ExitCode);
                    loaded = await sensor.Window(6, ct, () => { if (worker.HasExited) throw new IOException("单核响应负载提前退出。"); });
                }
                finally
                {
                    if (!worker.HasExited) worker.Kill(true);
                    await worker.WaitForExitAsync();
                }
                var after = await sensor.Window(5, ct);
                int index = core.UiIndex;
                double delta = Math.Abs(loaded.Median[index] - (before.Median[index] + after.Median[index]) / 2) * 1000;
                double noise = Math.Max(before.Range[index], after.Range[index]) * 1000;
                if (delta < Math.Max(6.25, noise))
                    throw new IOException($"核心 {index} 未取得可区分的负载响应（变化 {delta:F2} mV，噪声 {noise:F2} mV）。未开始自动写入，请降低其他 CPU 负载后重试。");
            }
        }
        Log("全部核心已完成两轮动态响应采样。");
    }
    private static int[][] Groups(CpuTopology t) => t.CoreSlots.GroupBy(c => c.CcdIndex).OrderBy(g => g.Key).Select(g => g.Select(c => c.UiIndex).ToArray()).ToArray();
    private static double Spread(double[] values, int[][] groups) => groups.Max(g => (g.Max(i => values[i]) - g.Min(i => values[i])) * 1000);
    public static int[] Correct(double[] values, int[][] groups, int[] references, int[] offsets, int maxStep, int minimum)
    {
        var next = offsets.ToArray();
        for (int group = 0; group < groups.Length; group++)
            foreach (int i in groups[group])
            {
                if (i == references[group]) continue;
                double gap = (values[references[group]] - values[i]) * 1000;
                if (Math.Abs(gap) <= 3.125) continue;
                int step = Math.Clamp((int)Math.Round(Math.Abs(gap) / 3, MidpointRounding.AwayFromZero), 1, maxStep);
                next[i] = Math.Clamp(next[i] + Math.Sign(gap) * step, minimum, 0);
            }
        return next;
    }
    public async Task<AlignmentResult> Align(int startingMagnitude, int memoryMiB, CancellationToken ct)
    {
        if (startingMagnitude is < 0 or > 30) throw new ArgumentException("起始负压幅度为 0～30。");
        await Fresh();
        Profile? recovery = null;
        StressSession? stress = null;
        try
        {
            await Qualify(ct);
            await Fresh(); // Capture the actual values immediately before the first write.
            recovery = Begin("自动分核开始前");
            stress = new StressSession("BKT", 1, memoryMiB, Log);
            await Task.Delay(1200, ct);
            stress.Check();
            await ApplyStep(Enumerable.Repeat(-startingMagnitude, Snapshot!.Offsets.Length).ToArray(), ct);
            await ResumeSensor(ct, true);
            var groups = Groups(Snapshot.Topology);
            int[]? references = null;
            var candidates = new List<(int[] Values, double Score)>();
            var watch = Stopwatch.StartNew();
            int[] maxSteps = [14, 7, 3, 2, 2, 2, 2];
            for (int iteration = 0; ; iteration++)
            {
                stress.Check();
                var first = await sensor!.Window(16, ct, () => stress.Check());
                var second = await sensor.Window(16, ct, () => stress.Check());
                double a = Spread(first.Median, groups), b = Spread(second.Median, groups);
                double score = Math.Max(a, b);
                var values = a >= b ? first.Median : second.Median;
                references ??= groups.Select(g => g.MaxBy(i => values[i])).ToArray();
                candidates.Add((Snapshot.Offsets.ToArray(), score));
                Log($"候选 {iteration + 1}：同 CCD 最大差 {score:F3} mV；尚未验证稳定性。");
                if (iteration >= maxSteps.Length || score <= 6.251 || watch.Elapsed.TotalSeconds >= 150) break;
                var next = Correct(values, groups, references, Snapshot.Offsets, maxSteps[iteration], -startingMagnitude);
                if (next.SequenceEqual(Snapshot.Offsets)) break;
                await ApplyStep(next, ct);
                await ResumeSensor(ct, true);
            }
            var best = candidates.OrderBy(c => c.Score).ThenByDescending(c => c.Values.Sum()).First();
            await ApplyStep(best.Values, ct);
            await ResumeSensor(ct, true);
            var finalA = await sensor!.Window(16, ct, () => stress.Check());
            var finalB = await sensor.Window(16, ct, () => stress.Check());
            double finalScore = Math.Max(Spread(finalA.Median, groups), Spread(finalB.Median, groups));
            await stress.Finish();
            if (stress.Passed < 1) throw new IOException("BKT 没有报告完成轮次，候选未保留。");
            ct.ThrowIfCancellationRequested();
            Validation = $"自动候选 · 差值 {finalScore:F2} mV · 待 VT3";
            SaveCurrent();
            Complete(recovery);
            Log("自动分核完成；已保存候选。请单独执行 VT3，电压差不代表稳定性。");
            Notify();
            return new(Snapshot.Offsets.ToArray(), finalScore);
        }
        catch (Exception e)
        {
            if (stress != null) { await stress.DisposeAsync(); stress = null; }
            if (recovery != null) await FailAndRestore(recovery, e);
            throw;
        }
        finally
        {
            if (stress != null) await stress.DisposeAsync();
            await ResumeSensor();
        }
    }
    public async Task TestVt3(int memoryMiB, bool retreat, CancellationToken ct)
    {
        await Fresh();
        var recovery = Begin("VT3 开始前");
        StressSession? stress = null;
        try
        {
            await ResumeSensor(ct);
            stress = new StressSession("VT3", 150, memoryMiB, Log);
            await stress.RunFive(ct, (rounds, elapsed) => StressProgress?.Invoke(rounds, elapsed));
            await stress.DisposeAsync(); stress = null;
            ct.ThrowIfCancellationRequested();
            // Passing evidence belongs to the actual CO at test start, not an edited UI value.
            await Fresh();
            if (!Snapshot!.Offsets.SequenceEqual(recovery.Snapshot.Offsets)) throw new IOException("测试期间 CO 已变化，不能把五轮结果归到当前参数。");
            Validation = "当前 CO 已通过 VT3 5/5（150 秒/轮）";
            SaveCurrent();
            Complete(recovery);
            Log(Validation);
        }
        catch (CalculationFailure)
        {
            if (stress != null) { await stress.DisposeAsync(); stress = null; }
            await Fresh();
            if (!Snapshot!.Offsets.SequenceEqual(recovery.Snapshot.Offsets))
                throw new IOException("VT3 未通过，且 CO 被其他程序改变；没有自动回调，请检查恢复记录。");
            var adjusted = Snapshot.Offsets.Select(v => v < 0 ? Math.Min(0, v + 2) : v).ToArray();
            if (retreat && !adjusted.SequenceEqual(Snapshot.Offsets))
            {
                try
                {
                    await ApplyStep(adjusted, CancellationToken.None);
                    Validation = "VT3 未通过 · 负 CO 已 +2 · 待手动重测";
                    SaveCurrent(); Complete(recovery);
                }
                catch (Exception e) { await FailAndRestore(recovery, e); throw; }
            }
            else
            {
                Validation = "VT3 未通过 · 参数未改变";
                SaveCurrent(); Complete(recovery);
            }
            Log(Validation);
        }
        catch (Exception e)
        {
            if (stress != null) { await stress.DisposeAsync(); stress = null; }
            await FailAndRestore(recovery, e);
            throw;
        }
        finally
        {
            if (stress != null) await stress.DisposeAsync();
            Notify(); await ResumeSensor();
        }
    }
    public async ValueTask DisposeAsync() => await StopSensor();
}
