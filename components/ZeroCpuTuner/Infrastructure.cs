using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace ZeroCpuTuner;

public static class AppPaths
{
    private static readonly object logGate = new();
    public static string DataRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCpuTuner");
    public static string BundleRoot => Path.Combine(DataRoot, "engine-1.0.0-r3");
    public static string Recovery => Path.Combine(DataRoot, "recovery.json");
    public static string Session => Path.Combine(DataRoot, "session-start.json");
    public static string Profile => Path.Combine(DataRoot, "last-profile.json");
    public static string Backend => Path.Combine(BundleRoot, "backend", "ryzen-smu-cli.dll");
    public static string Sensor => Path.Combine(BundleRoot, "backend", "CpuSensor.dll");
    public static string StressExe => Path.Combine(BundleRoot, "y-cruncher v0.8.5.9543", "y-cruncher.exe");
    public static string Dotnet
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("需要 .NET 8 x64 运行环境。", path);
            return path;
        }
    }
    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void Extract()
    {
        if (File.Exists(Path.Combine(BundleRoot, ".complete"))) return;
        Directory.CreateDirectory(BundleRoot);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new FileNotFoundException("缺少内置组件。");
        using var zip = new ZipArchive(stream);
        zip.ExtractToDirectory(BundleRoot, true);
        File.WriteAllText(Path.Combine(BundleRoot, ".complete"), "1.0.0-r3");
    }
    public static void Log(string text)
    {
        Directory.CreateDirectory(DataRoot);
        lock (logGate) File.AppendAllText(Path.Combine(DataRoot, "activity.log"), $"{DateTimeOffset.Now:O} {text}\n");
    }
}

public sealed record ProcessResult(int ExitCode, string Output, string Error);
public static class ChildProcess
{
    public static ProcessStartInfo Info(string file, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task<ProcessResult> Run(string file, IEnumerable<string> args, int seconds = 30, CancellationToken ct = default, string? workingDirectory = null)
    {
        using var p = Process.Start(Info(file, args, workingDirectory)) ?? throw new IOException("无法启动组件。");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { await p.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            if (!p.HasExited) p.Kill(true);
            await p.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"组件运行超过 {seconds} 秒，已停止进程树。");
        }
        return new(p.ExitCode, await stdout, await stderr);
    }
}

public sealed class SmuBackend
{
    private async Task<string> Call(params string[] args)
    {
        var r = await ChildProcess.Run(AppPaths.Dotnet, new[] { AppPaths.Backend }.Concat(args), 35, workingDirectory: Path.GetDirectoryName(AppPaths.Backend));
        if (r.ExitCode != 0) throw new IOException($"SMU 后端失败（{r.ExitCode}）：{r.Error}\n{r.Output}".Trim());
        return r.Output;
    }
    public async Task<CoSnapshot> ReadCo()
    {
        var state = JsonFiles.ParseOutput<CoSnapshot>(await Call("--get-offsets-json"));
        state.Validate();
        return state;
    }
    public async Task WriteCo(int[] offsets, string map)
    {
        if (offsets.Length is < 1 or > 64 || offsets.Any(v => v is < -50 or > 10)) throw new ArgumentException("CO 范围为 -50～+10。");
        await Call("--offset", string.Join(',', offsets), "--expected-map-hash", map);
    }
    public async Task<CoSnapshot> VerifyCo(int[] expected, string map)
    {
        CoSnapshot? last = null;
        int consecutive = 0;
        for (int i = 0; i < 5; i++)
        {
            last = await ReadCo();
            if (last.Topology.MapHash != map) throw new IOException("CPU 拓扑已变化，停止写入。");
            consecutive = last.Offsets.SequenceEqual(expected) ? consecutive + 1 : 0;
            if (consecutive == 2) return last;
            await Task.Delay(180);
        }
        throw new IOException("CO 未连续两次回读为目标值，实际：" + string.Join(',', last!.Offsets));
    }
    public async Task<PboState> ReadPbo()
    {
        var state = JsonFiles.ParseOutput<PboState>(await Call("--get-pbo-frequency-json"));
        if (state.Schema != 1 || state.CpuId.Length != 64 ||
            (state.Supported && state.CurrentMhz is not (>= 1000 and <= 7000)))
            throw new InvalidDataException("PBO 状态无效。");
        return state;
    }
    public async Task<PboState> WritePbo(PboState before, int target)
    {
        if (!before.Supported || target is < 1000 or > 7000) throw new ArgumentException("PBO 目标无效或不受支持。");
        var receipt = JsonFiles.ParseOutput<PboReceipt>(await Call("--set-pbo-frequency-mhz", target.ToString(), "--expected-frequency-mhz", before.CurrentMhz!.Value.ToString(), "--expected-cpu-id", before.CpuId));
        if (receipt.Schema != 1 || receipt.CpuId != before.CpuId || receipt.PreviousMhz != before.CurrentMhz || receipt.RequestedMhz != target || receipt.ActualMhz != target)
            throw new IOException("PBO 写入回执与目标不一致。");
        var after = await ReadPbo();
        if (after.CpuId != before.CpuId || after.CurrentMhz != target) throw new IOException("PBO 最终回读不一致。");
        return after;
    }
}
