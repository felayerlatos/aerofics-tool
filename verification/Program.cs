using AeroficsLauncher;
using System.Reflection;
using System.Text.Json;

internal static class Verify
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Verify <output-directory> <original-optimizer-exe> <original-tuner-exe>");
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(output, "LocalAppData"));
        var assembly = typeof(LauncherForm).Assembly;
        var load = assembly.GetType("AeroficsLauncher.Package")!.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
        var config = (LauncherConfig)load.Invoke(null, null)!;
        Check(config.Programs.Count == 10, "Ten public-package tools are present");
        string[] excluded = { "AIDA64", "HWiNFO", "Geek Uninstaller", "Thaiphoon" };
        Check(config.Programs.All(p => !excluded.Contains(p.Name, StringComparer.OrdinalIgnoreCase)), "Excluded tool entries are absent");
        Check(!Directory.GetDirectories(config.Root).Any(p => excluded.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)), "Excluded tool folders are absent");
        Check(!Directory.GetFiles(config.Root, "pkey.txt", SearchOption.AllDirectories).Any(), "No AIDA64 key file");
        Check(EqualFiles(BundledTools.OptimizerPath(config), args[1]), "Bundled optimizer bytes match original EXE");
        var tuner = BundledTools.TunerStartInfo(config);
        Check(EqualFiles(tuner.FileName, args[2]), "Bundled tuner and SMU payload bytes match original EXE");
        Check(tuner.UseShellExecute && tuner.Verb == "runas" && tuner.WorkingDirectory == Path.GetDirectoryName(tuner.FileName), "Tuner launch requests administrator permission and correct working directory");
        string copied = Path.Combine(output, "AeroficsOptimizer-2.0.0.exe");
        BundledTools.CopyOptimizer(config, copied, false);
        Check(EqualFiles(copied, args[1]), "Optimizer copy produces the exact original EXE");
        bool refusedOverwrite = false;
        try { BundledTools.CopyOptimizer(config, copied, false); } catch (IOException) { refusedOverwrite = true; }
        Check(refusedOverwrite, "Existing file is not replaced without overwrite approval");
        File.WriteAllText(copied, "temporary overwrite target");
        BundledTools.CopyOptimizer(config, copied, true);
        Check(EqualFiles(copied, args[1]), "Approved replacement restores exact EXE bytes");
        var marker = Path.Combine(config.Root, ".complete");
        var markerTime = File.GetLastWriteTimeUtc(marker);
        var configFile = Path.Combine(config.Root, "tm5", "TM5.ini");
        var settings = File.ReadAllBytes(configFile);
        File.AppendAllText(configFile, "\r\n; cache reuse check\r\n");
        var customSettings = File.ReadAllBytes(configFile);
        var reused = (LauncherConfig)load.Invoke(null, null)!;
        Check(reused.Root == config.Root && File.GetLastWriteTimeUtc(marker) == markerTime && File.ReadAllBytes(configFile).SequenceEqual(customSettings), "Warm launch keeps cached tool settings");
        File.WriteAllBytes(configFile, settings);

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        foreach (var item in config.Programs)
        {
            item.FullPath = Path.Combine(config.Root, item.Exe);
            using var icon = Icon.ExtractAssociatedIcon(item.FullPath);
            item.Icon = icon?.ToBitmap();
        }
        using var form = new LauncherForm(config);
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        var previews = new List<object>();
        foreach (var size in new[] { new Size(1600,900), new Size(1280,800), new Size(1024,768) })
        {
            form.ClientSize = size;
            form.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(size.Width, size.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            string file = Path.Combine(output, $"preview-{size.Width}x{size.Height}.png");
            bitmap.Save(file);
            var buttons = form.Controls.OfType<Button>().ToArray();
            Check(buttons.Length == 15 && buttons.All(b => form.ClientRectangle.Contains(b.Bounds)), "All buttons fit at " + size);
            var board = form.Controls.Find("MotherboardModel", true).Single();
            var results = form.Controls.Find("QualityTestResults", true).Single();
            Check(form.ClientRectangle.Contains(board.Bounds) && !board.Bounds.IntersectsWith(results.Bounds), "Motherboard stays clear of test results");
            Check(form.Controls.Find("LaunchZeroCpuTuner", true).Single().Right < results.Left && form.Controls.Find("InstallAeroficsOptimizer", true).Single().Left > results.Right, "Tuner left and optimizer right");
            previews.Add(new { size.Width, size.Height, Buttons = buttons.Length });
        }
        string desktopPath = BundledTools.DesktopOptimizerPath();
        bool desktopBefore = File.Exists(desktopPath);
        using var timer = new System.Windows.Forms.Timer { Interval = 80 };
        bool cancelled = false;
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<InstallDialog>().FirstOrDefault();
            if (dialog == null) return;
            timer.Stop(); cancelled = true;
            using var image = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
            image.Save(Path.Combine(output, "install-confirmation.png"));
            ((Button)dialog.Controls.Find("CancelInstall", true).Single()).PerformClick();
        };
        timer.Start();
        ((Button)form.Controls.Find("InstallAeroficsOptimizer", true).Single()).PerformClick();
        Check(cancelled && File.Exists(desktopPath) == desktopBefore, "Real install button opens confirmation; cancellation leaves desktop untouched");
        var report = new { Success = true, Programs = config.Programs.Select(p=>p.Name), Excluded = excluded, ExactBundledExecutables = true,
            CacheReuse = true, CopyAndOverwriteVerified = true, AdministratorStartInfo = true, InstallCancellation = true,
            Previews = previews, Scope = "Compiled launcher, real embedded package, copy path and confirmation UI verified. Hardware tuning and stress tests were not executed." };
        File.WriteAllText(Path.Combine(output,"verification.json"), JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    private static bool EqualFiles(string first,string second)
    {
        using var a=File.OpenRead(first); using var b=File.OpenRead(second);
        if(a.Length!=b.Length) return false;
        byte[] x=new byte[65536], y=new byte[65536];
        int read; while((read=a.Read(x))>0) { b.ReadExactly(y.AsSpan(0,read)); if(!x.AsSpan(0,read).SequenceEqual(y.AsSpan(0,read))) return false; }
        return true;
    }
    private static void Check(bool valid,string message) { if(!valid) throw new Exception(message); Console.WriteLine("PASS: "+message); }
}
