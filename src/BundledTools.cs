using System.Diagnostics;

namespace AeroficsLauncher;

public static class BundledTools
{
    public static string OptimizerPath(LauncherConfig config) => Path.Combine(config.Root, config.OptimizerExe);
    public static string DesktopOptimizerPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AeroficsOptimizer-2.0.0.exe");

    // The confirmation dialog is owned by LauncherForm. Never copy on application startup.
    public static void CopyOptimizer(LauncherConfig config, string destination, bool overwrite)
    {
        File.Copy(OptimizerPath(config), destination, overwrite);
    }

    public static ProcessStartInfo TunerStartInfo(LauncherConfig config)
    {
        string exe = Path.Combine(config.Root, config.TunerExe);
        return new ProcessStartInfo
        {
            FileName = exe, WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true, Verb = "runas"
        };
    }
}
