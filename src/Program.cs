using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroficsLauncher;

public sealed class ProgramItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";
    [JsonIgnore] public string FullPath { get; set; } = "";
    [JsonIgnore] public Bitmap? Icon { get; set; }
}

public sealed class LauncherConfig
{
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("background")] public string Background { get; set; } = "";
    [JsonPropertyName("programs")] public List<ProgramItem> Programs { get; set; } = new();
    [JsonPropertyName("optimizerExe")] public string OptimizerExe { get; set; } = "Extras/AeroficsOptimizer-2.0.0.exe";
    [JsonPropertyName("tunerExe")] public string TunerExe { get; set; } = "Extras/ZeroCpuTuner.exe";
    [JsonPropertyName("voltageProvider")] public string VoltageProvider { get; set; } = "HwInfoThenAmdSmu";
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--verify-package")
            {
                var config = Package.Load();
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { Success = true, config.Root, Programs = config.Programs.Count,
                    Optimizer = File.Exists(Path.Combine(config.Root, config.OptimizerExe)), Tuner = File.Exists(Path.Combine(config.Root, config.TunerExe)),
                    Motherboard = MotherboardInfo.Read() }));
                return;
            }
            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var loaded = Package.Load();
            foreach (var item in loaded.Programs)
            {
                item.FullPath = Path.Combine(loaded.Root, item.Exe);
                try { using var icon = Icon.ExtractAssociatedIcon(item.FullPath); item.Icon = icon?.ToBitmap(); }
                catch { item.Icon = null; }
            }
            Application.Run(new LauncherForm(loaded));
        }
        catch (Exception ex)
        {
            MessageBox.Show("工具箱启动失败：\n" + ex.Message, "Aerofics 工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
