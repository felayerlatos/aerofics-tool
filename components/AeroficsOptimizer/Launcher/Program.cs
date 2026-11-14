using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Aerofics 性能工作台")]
[assembly: AssemblyProduct("Aerofics 性能工作台")]
[assembly: AssemblyCompany("Aerofics")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

internal static class Program
{
    internal const string Release = "2.0.0-20261114.1";
    private const string AppName = "AeroficsOptimizer.App.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroficsOptimizer", "Runtime");
        var forwarded = new List<string>();
        string checkReport = null;
        bool quiet = args.Any(a => a == "--self-test" || a == "--scheduler-self-test");
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--runtime-dir")
                {
                    if (++i == args.Length) throw new ArgumentException("--runtime-dir 缺少路径。");
                    root = Path.GetFullPath(args[i]);
                }
                else if (args[i] == "--install-check")
                {
                    if (++i == args.Length) throw new ArgumentException("--install-check 缺少报告路径。");
                    checkReport = Path.GetFullPath(args[i]);
                    quiet = true;
                }
                else forwarded.Add(args[i]);
            }
            string cache = Path.Combine(root, Release);
            if (quiet || Ready(cache)) return Launch(cache, forwarded, quiet, checkReport);
            int exit = 1;
            Exception error = null;
            using (var splash = new Splash())
            {
                splash.Shown += async delegate
                {
                    try { exit = await Task.Run(() => Launch(cache, forwarded, false, null)); }
                    catch (Exception ex) { error = ex; }
                    splash.Close();
                };
                Application.Run(splash);
            }
            if (error != null) throw error;
            return exit;
        }
        catch (Exception ex)
        {
            if (checkReport != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(checkReport));
                File.WriteAllText(checkReport, new JavaScriptSerializer().Serialize(new { passed = false, error = ex.ToString() }), Encoding.UTF8);
            }
            if (!quiet) MessageBox.Show("无法启动性能工作台：\n\n" + ex.Message, "Aerofics", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool Ready(string cache)
    {
        string marker = Path.Combine(cache, "package.ready");
        return File.Exists(Path.Combine(cache, AppName)) && File.Exists(marker) && File.ReadAllText(marker) == Release;
    }

    private static int Launch(string cache, List<string> args, bool wait, string checkReport)
    {
        var timer = Stopwatch.StartNew();
        bool cached = Ready(cache);
        EnsureRuntime(cache);
        if (checkReport != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(checkReport));
            File.WriteAllText(checkReport, new JavaScriptSerializer().Serialize(new {
                passed = true, cached = cached, elapsedMilliseconds = timer.ElapsedMilliseconds,
                release = Release, runtimeDirectory = cache, executable = Path.Combine(cache, AppName),
                runtimeBundled = File.Exists(Path.Combine(cache, "coreclr.dll")) && File.Exists(Path.Combine(cache, "PresentationFramework.dll"))
            }), Encoding.UTF8);
            return 0;
        }
        var info = new ProcessStartInfo(Path.Combine(cache, AppName), string.Join(" ", args.Select(Quote))) {
            UseShellExecute = false, WorkingDirectory = cache, CreateNoWindow = wait
        };
        info.EnvironmentVariables["AEROFICS_ENTRY"] = Assembly.GetExecutingAssembly().Location;
        using (var process = Process.Start(info))
        {
            if (process == null) throw new IOException("主程序未启动。");
            if (!wait) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static void EnsureRuntime(string cache)
    {
        if (Ready(cache)) return;
        using (var gate = new Mutex(false, @"Local\AeroficsOptimizer.Unpack." + Release))
        {
            bool owned;
            try { owned = gate.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new IOException("另一个窗口仍在准备运行库，请稍后重试。");
            string stage = null;
            try
            {
                if (Ready(cache)) return;
                if (Directory.Exists(cache)) throw new IOException("运行缓存不完整，请删除以下版本目录后重新打开：\n" + cache);
                string parent = Path.GetDirectoryName(cache);
                Directory.CreateDirectory(parent);
                stage = Path.Combine(parent, Release + ".unpacking-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                string extractor = Path.Combine(stage, "7z.exe");
                string archive = Path.Combine(stage, "runtime.7z");
                string payload = Path.Combine(stage, "files");
                WriteResource("Extractor.exe.deflate", extractor, true);
                WriteResource("Extractor.dll.deflate", Path.Combine(stage, "7z.dll"), true);
                WriteResource("Runtime.7z", archive, false);
                var info = new ProcessStartInfo(extractor, "x -y -o" + Quote(payload) + " " + Quote(archive)) {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var unpack = Process.Start(info))
                {
                    if (unpack == null) throw new IOException("无法准备运行库。");
                    var stdout = unpack.StandardOutput.ReadToEndAsync();
                    var stderr = unpack.StandardError.ReadToEndAsync();
                    unpack.WaitForExit();
                    Task.WaitAll(stdout, stderr);
                    if (unpack.ExitCode != 0) throw new IOException("运行库解包失败（" + unpack.ExitCode + "）：" + stderr.Result + "\n" + stdout.Result);
                }
                if (!File.Exists(Path.Combine(payload, AppName))) throw new IOException("安装包中缺少主程序。");
                File.WriteAllText(Path.Combine(payload, "package.ready"), Release);
                Directory.Move(payload, cache);
            }
            finally
            {
                // stage is always a new directory generated by this invocation.
                if (stage != null && Directory.Exists(stage))
                {
                    try { Directory.Delete(stage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                gate.ReleaseMutex();
            }
        }
    }

    private static void WriteResource(string name, string destination, bool compressed)
    {
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var output = File.Create(destination))
        {
            if (resource == null) throw new IOException("安装包缺少资源：" + name);
            if (compressed) { using (var decoder = new DeflateStream(resource, CompressionMode.Decompress)) decoder.CopyTo(output); }
            else resource.CopyTo(output);
        }
    }

    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
            else { result.Append('\\', slashes); result.Append(c); }
            slashes = 0;
        }
        result.Append('\\', slashes * 2); result.Append('"');
        return result.ToString();
    }
}

internal sealed class Splash : Form
{
    internal Splash()
    {
        Text = "Aerofics 性能工作台";
        ClientSize = new Size(510, 220);
        BackColor = Color.FromArgb(16, 24, 36);
        ForeColor = Color.FromArgb(234, 241, 248);
        Font = new Font("Microsoft YaHei UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false; MinimizeBox = false; ControlBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }
        Controls.Add(new Label { Text = "AEROFICS", ForeColor = Color.FromArgb(112, 224, 202), AutoSize = true, Left = 32, Top = 27, Font = new Font(Font.FontFamily, 12, FontStyle.Bold) });
        Controls.Add(new Label { Text = "正在准备性能工作台", AutoSize = true, Left = 30, Top = 65, Font = new Font(Font.FontFamily, 21, FontStyle.Bold) });
        Controls.Add(new Label { Text = "首次打开需要释放运行库，后续启动将直接使用缓存。", AutoSize = true, Left = 32, Top = 116, ForeColor = Color.FromArgb(164, 181, 196) });
        Controls.Add(new ProgressBar { Left = 34, Top = 164, Width = 440, Height = 6, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 26 });
    }
}
