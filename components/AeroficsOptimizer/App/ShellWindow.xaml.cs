using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SystemOptimizer.App.Presentation;
using SystemOptimizer.App.Views;

namespace SystemOptimizer.App;

public partial class ShellWindow : Window
{
    internal SystemToolsViewModel SystemModel { get; } = new();
    internal SchedulerViewModel SchedulerModel { get; }
    public object Scheduler => SchedulerModel;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private ShellTrayIcon? _tray;
    private bool _ready, _closeAllowed;
    private string _currentPage = "overview";
    internal static readonly string[] PageKeys = { "overview", "rules", "order", "system", "win32", "power", "startup", "services", "nvidia" };
    private static readonly string[] Titles = { "本机性能概览", "进程规则", "核心优先顺序", "系统设置", "Win32 调度", "电源计划", "启动项管理", "服务优化", "NVIDIA 预设" };
    private static readonly string[] Subtitles = {
        "线程调度与系统设置，在一个工作台中管理。", "分别管理游戏、非游戏与排除程序。", "给游戏与非游戏程序各自安排一套核心顺序。",
        "21 项独立设置，按项目应用与恢复。", "设置前台与后台的处理器时间片偏好。", "切换、导入与管理电源方案。", "查看和管理登录 Windows 时运行的程序。",
        "按服务或功能分组管理启动类型。", "全局驱动配置与动态 P-State 设置。" };

    public ShellWindow()
    {
        SchedulerModel = new(App.SchedulerStateDirectory);
        InitializeComponent(); DataContext = this;
        AccessStatus.Text = App.IsReadOnlySession ? "只读预览" : App.IsAdministrator ? "管理员" : "普通权限";
        ElevateButton.Visibility = App.IsAdministrator || App.IsReadOnlySession ? Visibility.Collapsed : Visibility.Visible;
        _ready = true; Navigate("overview");
        Closing += ClosingAsync;
        Closed += (_, _) => { _tray?.Dispose(); SchedulerModel.Dispose(); };
        SourceInitialized += (_, _) => {
            int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
            _tray = new ShellTrayIcon(this, RestoreWindow, async () => await SchedulerModel.StopAsync(), Close);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && SchedulerModel.MinimizeToTray && _tray?.Show() == true) Hide(); };
    }
    internal Task InitializeAsync() => SystemModel.RefreshSummaryAsync();
    internal void Navigate(string key)
    {
        _currentPage = key;
        int index = Array.IndexOf(PageKeys, key); if (index < 0) index = 0;
        if (Navigation.SelectedIndex != index) Navigation.SelectedIndex = index;
        PageTitle.Text = Titles[index]; PageSubtitle.Text = Subtitles[index];
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch {
                "rules" => new SchedulerRulesView(), "order" => new CoreOrderView(), "system" => new SystemSettingsView(),
                "win32" => new Win32SchedulingView(), "power" => new PowerPlansView(), "startup" => new StartupItemsView(),
                "services" => new ServicesOptimizationView(), "nvidia" => new NvidiaProfilesView(), _ => new OverviewView() };
            page.DataContext = key is "overview" or "rules" or "order" ? SchedulerModel : SystemModel;
            _pages[key] = page;
        }
        PageHost.Content = page;
    }
    private async void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || Navigation.SelectedItem is not ListBoxItem item) return;
        string key = item.Tag.ToString()!; Navigate(key);
        try {
            if (key == "startup") await SystemModel.RefreshStartupItemsAsync();
            if (key == "services") await SystemModel.RefreshServicesAsync();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "读取状态失败"); }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => SchedulerModel.Save();
    private async void Start_Click(object sender, RoutedEventArgs e) => await SchedulerModel.StartAsync();
    private async void Stop_Click(object sender, RoutedEventArgs e) => await SchedulerModel.StopAsync();
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); _tray?.Hide(); }
    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_closeAllowed) return;
        if (SchedulerModel.Busy) { e.Cancel = true; return; }
        if (SchedulerModel.Running || SchedulerModel.HasPending)
        {
            e.Cancel = true;
            await SchedulerModel.StopAsync();
            if (!SchedulerModel.HasPending) { _closeAllowed = true; Close(); }
            else { RestoreWindow(); MessageBox.Show(this, "仍有待恢复的进程，请在总览中查看错误后重试。", "恢复尚未完成"); }
        }
    }
    private async void Elevate_Click(object sender, RoutedEventArgs e)
    {
        if (SchedulerModel.Busy) return;
        if (SchedulerModel.Running || SchedulerModel.HasPending) { await SchedulerModel.StopAsync(); if (SchedulerModel.HasPending) return; }
        try
        {
            App.ReleaseInstance();
            var process = Process.Start(new ProcessStartInfo(App.EntryExecutablePath) { UseShellExecute = true, Verb = "runas" });
            if (process != null) { _closeAllowed = true; Close(); } else App.AcquireInstance();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { App.AcquireInstance(); }
        catch (Exception ex) { App.AcquireInstance(); MessageBox.Show(this, ex.Message, "无法切换权限"); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
