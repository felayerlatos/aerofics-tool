using System.Diagnostics;
using System.Drawing.Imaging;

namespace ZeroCpuTuner;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--core-load")
            return CoreLoad.Run(int.Parse(args[1]), ulong.Parse(args[2]), int.Parse(args[3]));
        int dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Length) AppPaths.DataRoot = Path.GetFullPath(args[dataIndex + 1]);
        if (args.Length >= 2 && args[0] == "--check-install") return CheckInstall(args[1]).GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--diagnose") return Diagnose(args[1]).GetAwaiter().GetResult();
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--render-ui")
        {
            using Form form = args.Contains("--ycruncher") ? new YCruncherForm() : new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show(); Application.DoEvents();
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
            bmp.Save(args[1], ImageFormat.Png);
            form.Close(); return 0;
        }
        using var instance = new Mutex(false, "Local\\ZeroCpuTuner.UI");
        bool owns;
        try { owns = instance.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns) { MessageBox.Show("Zero CPU 已在运行，请使用已有窗口。"); return 0; }
        try { Application.Run(new MainForm()); }
        finally { instance.ReleaseMutex(); }
        return 0;
    }
    private static async Task<int> CheckInstall(string reportPath)
    {
        try
        {
            AppPaths.Extract();
            var help = await ChildProcess.Run(AppPaths.Dotnet, [AppPaths.Backend, "--help"], workingDirectory: Path.GetDirectoryName(AppPaths.Backend));
            var recipes = await ChildProcess.Run(AppPaths.Dotnet, [AppPaths.Sensor, "--list-recipes"], workingDirectory: Path.GetDirectoryName(AppPaths.Sensor));
            bool pass = help.ExitCode == 0 && help.Output.Contains("--get-offsets-json") && recipes.ExitCode == 0 && recipes.Output.Contains("0x621202") && File.Exists(AppPaths.StressExe);
            JsonFiles.Write(reportPath, new { version = "1.0.0", passed = pass, backend = help.ExitCode, sensor = recipes.ExitCode,
                yCruncherPresent = File.Exists(AppPaths.StressExe), help = help.Output, recipes = recipes.Output,
                administrator = AppPaths.IsAdmin, hardwareWrites = 0, stressStarted = false });
            return pass ? 0 : 1;
        }
        catch (Exception e) { JsonFiles.Write(reportPath, new { passed = false, error = e.Message, hardwareWrites = 0 }); return 1; }
    }
    private static async Task<int> Diagnose(string reportPath)
    {
        AppPaths.Extract();
        try
        {
            var backend = new SmuBackend();
            var snapshot = await backend.ReadCo();
            JsonFiles.Write(reportPath, new { success = true, administrator = AppPaths.IsAdmin, snapshot, hardwareWrites = 0 });
            return 0;
        }
        catch (Exception e) { JsonFiles.Write(reportPath, new { success = false, administrator = AppPaths.IsAdmin, error = e.Message, hardwareWrites = 0 }); return 1; }
    }
}
