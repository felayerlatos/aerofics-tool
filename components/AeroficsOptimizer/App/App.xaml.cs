using System.IO;
using System.Security.Principal;
using System.Windows;
using SystemOptimizer.Windows.SystemTools;

namespace SystemOptimizer.App;

public partial class App : Application
{
    private static Mutex? _instance;
    private static bool _ownsInstance;
    internal static bool IsReadOnlySession { get; private set; }
    internal static string? SchedulerStateDirectory { get; private set; }
    internal static string EntryExecutablePath
    {
        get { var entry = Environment.GetEnvironmentVariable("AEROFICS_ENTRY"); return !string.IsNullOrEmpty(entry) && File.Exists(entry) ? entry : Environment.ProcessPath!; }
    }
    internal static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static bool IsWriteAllowed => !IsReadOnlySession && IsAdministrator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--nvidia-profile-worker", StringComparer.OrdinalIgnoreCase))
        {
            try { Shutdown((await WindowsNvidiaProfilePresetService.TryRunWorkerAsync(e.Args, Console.OpenStandardInput(), Console.OpenStandardOutput())) ?? 64); }
            catch { Shutdown(70); }
            return;
        }
        AeroficsScheduler.SchedulerProtection.Verify();
        if (e.Args.Contains("--test-child"))
        {
            var ready = e.Args.SkipWhile(a => a != "--ready").Skip(1).FirstOrDefault();
            if (ready != null) File.WriteAllText(ready, "ready");
            Thread.Sleep(30000); Shutdown(0); return;
        }
        if (e.Args.Contains("--core-order-child")) { AeroficsScheduler.CoreOrderTestChild.Run(e.Args); Shutdown(0); return; }
        if (e.Args.Contains("--scheduler-self-test")) { Shutdown(AeroficsScheduler.SelfTests.Run(e.Args)); return; }
        IsReadOnlySession = e.Args.Contains("--preview") || e.Args.Contains("--self-test");
        if (IsReadOnlySession)
        {
            var requested = e.Args.SkipWhile(a => a != "--data-dir").Skip(1).FirstOrDefault();
            SchedulerStateDirectory = requested ?? Path.Combine(Path.GetTempPath(), "AeroficsPreview", Guid.NewGuid().ToString("N"));
        }
        try
        {
            if (e.Args.Contains("--self-test"))
            {
                int index = Array.IndexOf(e.Args, "--self-test");
                string report = index + 1 < e.Args.Length ? e.Args[index + 1] : "self-test.json";
                Shutdown(await Verification.RunAsync(Path.GetFullPath(report)));
                return;
            }
            if (!IsReadOnlySession && !AcquireInstance()) { MessageBox.Show("线程调度器或性能工作台已在运行，请打开现有窗口。"); Shutdown(0); return; }
            var window = new ShellWindow();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            await window.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开性能工作台：" + ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    internal static bool AcquireInstance()
    {
        if (_ownsInstance) return true;
        _instance?.Dispose(); _instance = new Mutex(false, @"Local\Aerofics.ThreadScheduler.Extended");
        try { _ownsInstance = _instance.WaitOne(0); } catch (AbandonedMutexException) { _ownsInstance = true; }
        return _ownsInstance;
    }
    internal static void ReleaseInstance()
    {
        if (_ownsInstance) { _instance!.ReleaseMutex(); _ownsInstance = false; }
        _instance?.Dispose(); _instance = null;
    }
    protected override void OnExit(ExitEventArgs e) { ReleaseInstance(); base.OnExit(e); }
}
