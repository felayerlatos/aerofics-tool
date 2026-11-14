using System.Diagnostics;
namespace AeroficsLauncher;
public sealed partial class LauncherForm
{
    private ProgramItem? _externalHwInfo;

    private ProgramItem? SelectHwInfo()
    {
        if (_externalHwInfo != null && File.Exists(_externalHwInfo.FullPath)) return _externalHwInfo;
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HWiNFO64", "HWiNFO64.exe");
        if (!File.Exists(installed))
        {
            using var picker = new OpenFileDialog
            {
                Title = "选择已安装并有权使用的 HWiNFO64（公开版不内置）",
                Filter = "HWiNFO64|HWiNFO64.exe|程序文件|*.exe", CheckFileExists = true
            };
            if (picker.ShowDialog(this) != DialogResult.OK) return null;
            installed = picker.FileName;
        }
        return _externalHwInfo = new ProgramItem { Name = "HWiNFO", FullPath = installed };
    }
    public static (long Timestamp, VidReading[] Readings) ReadVidSnapshot()
    {
        var snapshot = HwInfoVoltageProvider.Read();
        return (snapshot.Timestamp, snapshot.Cores.Select(c => new VidReading(0, c.CoreId, c.Voltage, c.Kind)).ToArray());
    }

    public static string ReadCoreVids()
    {
        var snapshot = HwInfoVoltageProvider.Read();
        List<CoreTopology> topology;
        try { topology = VidGroups.Topology(); } catch { topology = new(); }
        var readings = snapshot.Cores.Select(c => new VidReading(0, c.CoreId, c.Voltage, c.Kind)).ToArray();
        return $"数据来源：{VoltageText.SourceName(snapshot.Source)}\n" + VidGroups.Format(readings, snapshot.Vendor, topology);
    }

    private async Task<string> ReadAverageAsync()
    {
        if (!_hwInfoProvider.TryRead(out var sourceSnapshot, out var initialError)) throw new IOException(initialError);
        var topology = sourceSnapshot.Topology.ToList();
        var vendor = sourceSnapshot.Vendor;
        var samples = new List<VidReading[]>();
        long previous = sourceSnapshot.Timestamp;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (IsDisposed) throw new OperationCanceledException("窗口已关闭");
            if (!_hwInfoProvider.TryRead(out var snapshot, out var error)) throw new IOException(error);
            if (snapshot.Timestamp > previous)
            {
                samples.Add(snapshot.Cores.Select(c => new VidReading(0, c.CoreId, c.Voltage, c.Kind)).ToArray());
                previous = snapshot.Timestamp;
            }
            _testResult = $"正在采样：{Math.Min(10, timer.Elapsed.TotalSeconds):F1}/10 秒\n有效刷新：{samples.Count} 次";
            Invalidate();
            var remaining = TimeSpan.FromSeconds(10) - timer.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200));
        }
        if (samples.Count < 2) throw new IOException("10 秒内有效刷新不足，请确认 HWiNFO 正在更新且刷新周期小于 5 秒");
        return $"数据来源：{VoltageText.SourceName(sourceSnapshot.Source)}\n" + VidGroups.Format(samples[^1], vendor, topology, samples)
            .Replace("最小 VID：", "平均最小 VID：").Replace("最大 VID：", "平均最大 VID：").Replace("压差：", "平均压差：");
    }
    private async Task RunQualityTestAsync()
    {
        if (_testing) return;
        var hwinfo = SelectHwInfo();
        if (hwinfo == null) return;
        var cpuz = _config.Programs.FirstOrDefault(p => p.Name.Equals("CPU-Z", StringComparison.OrdinalIgnoreCase));
        if (hwinfo == null || !File.Exists(hwinfo.FullPath) || cpuz == null || !File.Exists(cpuz.FullPath))
        { _testResult = "找不到 HWiNFO 或 CPU-Z"; Invalidate(); return; }
        _testing = true;
        try
        {
            var startupTimer = Stopwatch.StartNew();
            ConfigureSensorsOnly(hwinfo);
            if (Process.GetProcessesByName("HWiNFO64").Length == 0)
                Process.Start(new ProcessStartInfo { FileName = hwinfo.FullPath, WorkingDirectory = Path.GetDirectoryName(hwinfo.FullPath)!, UseShellExecute = true });
            _testResult = "等待 HWiNFO 传感器就绪（最多 10 秒）…"; Invalidate();
            bool ready = await SensorReadiness.WaitAsync(
                async () => { if (!_hwInfoProvider.TryRead(out _, out var error)) throw new IOException(error); await Task.CompletedTask; },
                TimeSpan.FromSeconds(10) - startupTimer.Elapsed, () => IsDisposed);
            if (IsDisposed) return;
            if (!ready)
            {
                _testResult = "检测 HWiNFO 失败，请联系作者";
                return;
            }
            if (Process.GetProcessesByName("cpuz").Length == 0)
                Process.Start(new ProcessStartInfo { FileName = cpuz.FullPath, WorkingDirectory = Path.GetDirectoryName(cpuz.FullPath)!, UseShellExecute = true });
            _testResult = "HWiNFO 传感器已就绪\nCPU-Z 已启动，请点击“稳定性测试”，确认后开始采样。"; Invalidate();
            var confirmation = MessageBox.Show(this,
                "CPU-Z 已启动。请在 CPU-Z 窗口中点击“稳定性测试”，然后点击“确定”开始体质测试采样。",
                "请启动 CPU-Z 稳定性测试", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (confirmation != DialogResult.OK)
            {
                _testResult = "已取消体质测试";
                return;
            }
            _testResult = "CPU-Z 稳定性测试已确认，等待 5 秒后采样 10 秒…"; Invalidate();
            await Task.Delay(5000);
            var result = await ReadAverageAsync();
            _testResult = result;
            Directory.CreateDirectory(Package.ResultDirectory);
            File.WriteAllText(Path.Combine(Package.ResultDirectory, "体质测试结果.txt"), result);
        }
        catch (Exception ex) { _testResult = "测试未完成：" + ex.Message; }
        finally { _testing = false; if (!IsDisposed) Invalidate(); }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);

    private static void ConfigureSensorsOnly(ProgramItem item)
    {
        if (!Path.GetFileName(item.FullPath).Equals("HWiNFO64.exe", StringComparison.OrdinalIgnoreCase)) return;
        var ini = Path.ChangeExtension(item.FullPath, ".INI");
        if (!WritePrivateProfileString("Settings", "SensorsOnly", "1", ini))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "无法设置 HWiNFO 仅传感器模式");
    }

}
