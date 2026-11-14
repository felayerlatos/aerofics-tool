using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AeroficsLauncher;
using Microsoft.Win32;

namespace SystemOptimizer.App.Presentation;

internal sealed class CoreChoice : INotifyPropertyChanged
{
    internal CpuSetDescriptor Cpu { get; }
    internal CoreChoice(CpuSetDescriptor cpu, int smt) { Cpu = cpu; Smt = smt; }
    public string Processor => $"CPU {Cpu.LogicalProcessorIndex}";
    public string Details => $"组 {Cpu.Group}   ·   物理核 {Cpu.CoreIndex}   ·   L3 {Cpu.LastLevelCacheIndex}   ·   SMT {Smt}";
    public int Smt { get; }
    public uint Id => Cpu.Id;
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed record CcdChoice(string Id, string Label);
internal sealed record PriorityChoice(int Value, string Label);

internal sealed class SchedulerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProcessSchedulerService _service;
    private readonly PowerPlanService _legacyPower;
    private readonly Dispatcher _dispatcher;
    private bool _busy;
    public CpuSetTopology Topology { get; }
    public ObservableCollection<CoreChoice> Cpus { get; } = new();
    public ObservableCollection<CoreChoice> GameOrder { get; } = new();
    public ObservableCollection<CoreChoice> BackgroundOrder { get; } = new();
    public ObservableCollection<CcdChoice> CcdChoices { get; } = new();
    public IReadOnlyList<PriorityChoice> Priorities { get; } = new[] {
        new PriorityChoice(0, "保持原优先级"), new PriorityChoice(32, "正常"),
        new PriorityChoice(32768, "高于正常"), new PriorityChoice(128, "高") };
    public string CpuName { get; } = ReadCpuName();
    public string CpuSummary => $"{Topology.CpuSets.Select(c => (c.Group, c.CoreIndex)).Distinct().Count()} 核心 / {Topology.CpuSets.Count} 线程";
    public string CcdSummary => $"{Topology.Ccds.Count} 个 L3 分组";
    public bool Running => _service.Status.Running;
    public bool Busy => _busy;
    public bool CanEdit => !Running && !Busy;
    public bool CanStart => CanEdit && !App.IsReadOnlySession;
    public bool CanStop => !Busy && !App.IsReadOnlySession;
    public bool StrictAvailable => Topology.CpuSets.Select(c => c.Group).Distinct().Count() == 1;
    public bool HasPending => _service.Status.PendingRestoreCount > 0 || _legacyPower.Pending;
    public string ModeLabel => Running ? "正在调度" : "尚未启动";
    public int ActiveGames => _service.Status.GameCount;
    public int ActiveBackgrounds => _service.Status.BackgroundCount;
    public int PendingCount => _service.Status.PendingRestoreCount;
    public int RuleCount => Split(Games).Count + Split(Backgrounds).Count;
    public string ErrorDetails => string.Join("\n", _service.Status.Errors.TakeLast(6));
    private string _message = "规则保存后，点击开始调度即可生效。";
    public string Message { get => _message; private set => Set(ref _message, value); }
    private string _games = "", _backgrounds = "", _excluded = "", _gameCcd = "Auto";
    private bool _gameEnabled, _backgroundEnabled, _backgroundIsolation, _strict, _enforce, _custom, _tray, _startup;
    private int _priority;
    public string Games { get => _games; set { Set(ref _games, value); Changed(nameof(RuleCount)); } }
    public string Backgrounds { get => _backgrounds; set { Set(ref _backgrounds, value); Changed(nameof(RuleCount)); } }
    public string Excluded { get => _excluded; set => Set(ref _excluded, value); }
    public string GameCcd { get => _gameCcd; set => Set(ref _gameCcd, value); }
    public bool GameCoreOrderEnabled { get => _gameEnabled; set => Set(ref _gameEnabled, value); }
    public bool BackgroundCoreOrderEnabled { get => _backgroundEnabled; set => Set(ref _backgroundEnabled, value); }
    public bool BackgroundIsolation { get => _backgroundIsolation; set => Set(ref _backgroundIsolation, value); }
    public bool StrictAffinity { get => _strict; set => Set(ref _strict, value); }
    public bool Enforce { get => _enforce; set => Set(ref _enforce, value); }
    public bool UseCustomCpus { get => _custom; set => Set(ref _custom, value); }
    public bool MinimizeToTray { get => _tray; set => Set(ref _tray, value); }
    public int GamePriority { get => _priority; set => Set(ref _priority, value); }
    public bool StartupAvailable { get; private set; } = !App.IsReadOnlySession;
    public string StartupLabel { get; private set; } = "登录 Windows 时打开工作台";
    public bool StartupEnabled
    {
        get => _startup;
        set
        {
            if (_startup == value || !StartupAvailable) return;
            try { StartupSettings.Set(value); _startup = value; }
            catch (Exception ex) { Message = "开机启动设置失败：" + ex.Message; }
            Changed();
        }
    }

    internal SchedulerViewModel(string? stateDirectory = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _service = new(stateDirectory: stateDirectory);
        _legacyPower = new(_service.StateDirectory);
        try { Topology = CpuSetTopologyReader.Read(); }
        catch (Exception ex) { Topology = new(); Message = ex.Message; }
        var settings = _service.LoadSettings();
        _games = string.Join("\n", settings.GameProcesses); _backgrounds = string.Join("\n", settings.BackgroundProcesses);
        _excluded = string.Join("\n", settings.ExcludedProcesses); _gameCcd = settings.GameCcd;
        _gameEnabled = settings.GameCoreOrderEnabled; _backgroundEnabled = settings.BackgroundCoreOrderEnabled;
        _backgroundIsolation = settings.BackgroundIsolation; _strict = settings.StrictAffinity; _enforce = settings.Enforce;
        _priority = settings.GamePriority; _custom = settings.CustomCpuSetIds.Count > 0; _tray = settings.MinimizeToTray;
        var siblings = Topology.CpuSets.GroupBy(c => (c.Group, c.CoreIndex))
            .SelectMany(g => g.OrderBy(c => c.LogicalProcessorIndex).Select((c, index) => (c.Id, index))).ToDictionary(x => x.Id, x => x.index);
        foreach (var cpu in Topology.CpuSets) Cpus.Add(new(cpu, siblings[cpu.Id]) { Selected = settings.CustomCpuSetIds.Contains(cpu.Id) });
        LoadOrder(GameOrder, settings.GameCoreOrder, false); LoadOrder(BackgroundOrder, settings.BackgroundCoreOrder, true);
        CcdChoices.Add(new("Auto", "自动选择 L3 分组")); CcdChoices.Add(new("All", "使用全部核心"));
        for (int i = 0; i < Topology.Ccds.Count; i++) CcdChoices.Add(new(i.ToString(), $"CCD / L3 组 {i} · {Topology.Ccds[i].Select(c => c.CoreIndex).Distinct().Count()} 核心"));
        if (!CcdChoices.Any(c => c.Id == _gameCcd)) _gameCcd = "Auto";
        try { _startup = StartupSettings.Enabled(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        { StartupAvailable = false; StartupLabel = "当前权限无法读取开机启动状态"; }
        _service.Changed += ServiceChanged;
        if (HasPending) Message = "发现尚未恢复的记录，请点击停止并恢复。";
    }

    private void LoadOrder(ObservableCollection<CoreChoice> target, List<uint> saved, bool reverse)
    {
        var defaults = DefaultOrder(reverse).ToArray();
        foreach (var id in saved.Distinct()) { var c = Cpus.FirstOrDefault(x => x.Id == id); if (c != null) target.Add(c); }
        foreach (var c in defaults.Where(c => !target.Contains(c))) target.Add(c);
    }
    private IEnumerable<CoreChoice> DefaultOrder(bool reverse) => Cpus.OrderBy(c => c.Cpu.Group)
        .ThenBy(c => reverse ? -c.Cpu.LastLevelCacheIndex : c.Cpu.LastLevelCacheIndex).ThenBy(c => c.Smt).ThenBy(c => c.Cpu.CoreIndex);
    internal void Preset(ObservableCollection<CoreChoice> order, string mode)
    {
        if (!CanEdit) return;
        var sorted = mode switch { "reverse" => order.Reverse().ToArray(), "physical" => order.OrderBy(c => c.Smt).ToArray(), _ => DefaultOrder(mode == "l3-reverse").ToArray() };
        order.Clear(); foreach (var c in sorted) order.Add(c);
    }
    internal void Move(ObservableCollection<CoreChoice> order, CoreChoice? selected, int delta)
    {
        if (!CanEdit || selected == null) return;
        int index = order.IndexOf(selected); if (index >= 0) order.Move(index, Math.Clamp(index + delta, 0, order.Count - 1));
    }
    internal void AddPrograms(string target, IEnumerable<string> items)
    {
        if (!CanEdit) return;
        var current = target == "games" ? Games : target == "backgrounds" ? Backgrounds : Excluded;
        var merged = string.Join("\n", RunningProcessCatalog.Merge(Split(current), items));
        if (target == "games") Games = merged; else if (target == "backgrounds") Backgrounds = merged; else Excluded = merged;
    }
    internal SchedulerSettings ReadSettings()
    {
        var selected = UseCustomCpus ? Cpus.Where(c => c.Selected).Select(c => c.Id).ToList() : new();
        if (UseCustomCpus && selected.Count == 0) throw new InvalidOperationException("请至少选择一个自选 CPU。");
        return new() { GameProcesses = Split(Games), BackgroundProcesses = Split(Backgrounds), ExcludedProcesses = Split(Excluded),
            GameCcd = GameCcd, BackgroundIsolation = BackgroundIsolation, CustomCpuSetIds = selected, StrictAffinity = StrictAffinity,
            Enforce = Enforce, GamePriority = GamePriority, MinimizeToTray = MinimizeToTray,
            GameCoreOrderEnabled = GameCoreOrderEnabled, BackgroundCoreOrderEnabled = BackgroundCoreOrderEnabled,
            GameCoreOrder = GameOrder.Select(c => c.Id).ToList(), BackgroundCoreOrder = BackgroundOrder.Select(c => c.Id).ToList() };
    }
    internal void Save()
    {
        if (!CanStart) return;
        try { _service.SaveSettings(ReadSettings()); Message = "规则已保存，点击开始调度后生效。"; }
        catch (Exception ex) { Message = ex.Message; }
    }
    internal async Task StartAsync()
    {
        if (!CanStart) return;
        try { var settings = ReadSettings(); SetBusy(true); await Task.Run(() => _service.Start(settings)); Message = _service.Status.Message; }
        catch (Exception ex) { Message = "无法开始：" + ex.Message; }
        finally { SetBusy(false); Refresh(); }
    }
    internal async Task StopAsync()
    {
        if (Busy) return;
        try { SetBusy(true); await Task.Run(() => { _service.StopAndRestore(); _legacyPower.Restore(); }); Message = _service.Status.Message; }
        catch (Exception ex) { Message = "恢复失败：" + ex.Message; }
        finally { SetBusy(false); Refresh(); }
    }
    private void SetBusy(bool value) { _busy = value; Refresh(); }
    private void ServiceChanged(object? sender, EventArgs e)
    {
        if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(Refresh);
    }
    private void Refresh()
    {
        if (Running && !Busy && _service.Status.Errors.Count > 0) Message = _service.Status.Errors.Last();
        foreach (string property in new[] { nameof(Running), nameof(Busy), nameof(CanEdit), nameof(CanStart), nameof(CanStop), nameof(ModeLabel), nameof(ActiveGames), nameof(ActiveBackgrounds), nameof(PendingCount), nameof(ErrorDetails) }) Changed(property);
    }
    internal static List<string> Split(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static string ReadCpuName()
    {
        try { return Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "CPU")?.ToString()?.Trim() ?? "CPU"; }
        catch { return "本机处理器"; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(property); }
    public void Dispose() { _service.Changed -= ServiceChanged; _service.Dispose(); }
}
