using System.Diagnostics;
using Microsoft.Win32;

namespace ZeroCpuTuner;

public sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(14, 19, 27);
    private static readonly Color Card = Color.FromArgb(25, 33, 45);
    private static readonly Color Field = Color.FromArgb(18, 25, 35);
    private static readonly Color TextColor = Color.FromArgb(231, 237, 244);
    private static readonly Color Muted = Color.FromArgb(151, 166, 187);
    private static readonly Color Mint = Color.FromArgb(92, 219, 184);
    private readonly TuningEngine engine = new();
    private readonly Label status = LabelOf("尚未连接 · 启动不会应用参数", 11, Mint);
    private readonly Label hardware = LabelOf(CpuBrand(), 10, Muted);
    private readonly Label validation = LabelOf("连接后读取实际 CO 与核心映射。", 10, Muted);
    private readonly Label operation = LabelOf("就绪", 10, Muted);
    private readonly Label countdown = LabelOf("00 / 05   ·   预计 12:30", 18, Mint);
    private readonly Label pboState = LabelOf("当前上限 —   /   会话初值 —", 10, Muted);
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Field, ForeColor = Muted, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly TableLayoutPanel coreGroups = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2 };
    private readonly NumericUpDown step = Number(1, 20, 2, 60);
    private readonly NumericUpDown start = Number(0, 30, 30, 65);
    private readonly NumericUpDown memory = Number(512, 4096, 1024, 85);
    private readonly NumericUpDown pboOffset = Number(-800, 200, 0, 90);
    private readonly CheckBox retreat = new() { Text = "计算报错后，负 CO 向 0 回调 2", AutoSize = true, Checked = true, ForeColor = TextColor, Margin = new Padding(0, 8, 0, 4) };
    private readonly List<(NumericUpDown Input, TrackBar Slider, Label Vid, Label Readback)> rows = [];
    private readonly List<Control> mutations = [];
    private readonly Button connect;
    private readonly Button cruncher;
    private readonly Button stop;
    private readonly System.Windows.Forms.Timer telemetry = new() { Interval = 350 };
    private CancellationTokenSource? current;
    private bool busy;
    private bool closing;
    private bool syncing;
    public MainForm()
    {
        Text = "Zero CPU · 独立降压调校";
        BackColor = Background; ForeColor = TextColor; Font = UiFont(9.5f);
        ClientSize = new Size(1360, 960); MinimumSize = new Size(1160, 820);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), ColumnCount = 1, RowCount = 4, BackColor = Background };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var title = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        title.Controls.Add(LabelOf("ZERO  /  CPU 调校", 25, TextColor));
        title.Controls.Add(hardware); title.Controls.Add(status);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 0, 0) };
        connect = ButtonOf("连接 / 重新读取", async () => await Run("读取 CPU", ct => engine.Connect(ct)), true, 155);
        tools.Controls.Add(connect);
        cruncher = ButtonOf("y-cruncher", () => { using var form = new YCruncherForm(); form.ShowDialog(this); }, false, 155);
        tools.Controls.Add(cruncher);
        var elevate = ButtonOf(AppPaths.IsAdmin ? "管理员模式" : "以管理员重开", Elevate, false, 155);
        elevate.Enabled = !AppPaths.IsAdmin; tools.Controls.Add(elevate);
        header.Controls.Add(title, 0, 0); header.Controls.Add(tools, 1, 0); layout.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
        layout.Controls.Add(body, 0, 1);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(15), Margin = new Padding(0, 0, 12, 8), RowCount = 5, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        left.Controls.Add(LabelOf("逐核心 Curve Optimizer", 16, TextColor), 0, 0);
        var adjust = Flow(); adjust.Controls.Add(LabelOf("步长", 10, Muted)); adjust.Controls.Add(step);
        adjust.Controls.Add(Mutation("全核继续负压", () => Shift(null, -(int)step.Value), false, 130));
        adjust.Controls.Add(Mutation("全核减少负压", () => Shift(null, (int)step.Value), false, 130));
        adjust.Controls.Add(Mutation("CO 全部设为 0", () => { SetDraft(new int[rows.Count]); validation.Text = "CO 待应用值已设为 0，尚未写入。"; }, false, 130));
        left.Controls.Add(adjust, 0, 1);
        coreGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); coreGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var empty = LabelOf("连接 CPU 后显示每核 VID、当前 CO 和待应用值。\n\n每个 CCD 单独分组，使用后端返回的物理核心映射。", 13, Muted);
        empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter; empty.AutoSize = false;
        coreGroups.Controls.Add(empty, 0, 0); coreGroups.SetColumnSpan(empty, 2);
        left.Controls.Add(coreGroups, 0, 2); left.Controls.Add(validation, 0, 3);
        var apply = Flow();
        apply.Controls.Add(Mutation("应用 CO 并回读", async () => await Run("应用 CO", ct => engine.ApplyManual(Draft(), ct)), true, 165));
        apply.Controls.Add(Mutation("恢复本次连接初值", async () => { if (engine.SessionStart != null && Confirm("将 CO 和 PBO 恢复到本次连接时读到的值。")) await Run("恢复连接初值", _ => engine.Restore(engine.SessionStart)); }, false, 170));
        apply.Controls.Add(Mutation("恢复中断前参数", RestorePending, false, 150));
        left.Controls.Add(apply, 0, 4); body.Controls.Add(left, 0, 0);

        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0, 0, 0, 8) };
        body.Controls.Add(right, 1, 0);
        var autoCard = PanelOf("01   自动分核 / VID 对齐", 180);
        autoCard.Controls.Add(LabelOf("先验证逐核响应，再用 BKT 搜索有界候选。", 9, Muted));
        var settings = Flow(36); settings.Controls.Add(LabelOf("起始 CO −", 10, TextColor)); settings.Controls.Add(start);
        settings.Controls.Add(LabelOf("内存 MiB", 10, TextColor)); settings.Controls.Add(memory); autoCard.Controls.Add(settings);
        autoCard.Controls.Add(Mutation("开始自动检测并对齐", async () =>
        {
            if (Confirm($"将进行逐核响应负载与 BKT 测试，并从 CO -{start.Value} 开始调校。\n结果只作为待验证候选。请先保存正在进行的工作。"))
                await Run("自动分核", async ct => { await engine.Align((int)start.Value, (int)memory.Value, ct); });
        }, true, 325));
        autoCard.Controls.Add(LabelOf("建议目标 6.25 mV · 通常需数分钟", 9, Muted)); right.Controls.Add(autoCard);

        var stressCard = PanelOf("02   稳定性 / y-cruncher VT3", 191);
        stressCard.Controls.Add(countdown); stressCard.Controls.Add(retreat);
        stressCard.Controls.Add(Mutation("验证当前参数 · VT3 × 5", async () =>
        {
            if (Confirm("测试实际当前 CO：每轮 150 秒，共五轮，预计 12 分 30 秒。\n" + (retreat.Checked ? "出现明确计算错误时，负 CO 向 0 回调 2，然后停止等待手动重测。" : "出现计算错误时保持参数并停止。")))
                await Run("VT3 稳定性测试", ct => engine.TestVt3((int)memory.Value, retreat.Checked, ct));
        }, true, 325));
        stressCard.Controls.Add(LabelOf("五轮通过仅代表本次负载测试结果。", 9, Muted)); right.Controls.Add(stressCard);

        var pboCard = PanelOf("03   PBO 频率上限", 158); pboCard.Controls.Add(pboState);
        var frequency = Flow(35); frequency.Controls.Add(LabelOf("相对本次初值", 10, TextColor)); frequency.Controls.Add(pboOffset); frequency.Controls.Add(LabelOf("MHz", 10, Muted)); pboCard.Controls.Add(frequency);
        var pboButtons = Flow(38);
        pboButtons.Controls.Add(Mutation("应用频率", async () => { if (engine.SessionStart?.PboMhz is int baseline) await Run("应用 PBO", ct => engine.ApplyPbo(baseline + (int)pboOffset.Value, ct)); }, true, 145));
        pboButtons.Controls.Add(Mutation("恢复初值", async () => { if (engine.SessionStart?.PboMhz is int baseline) await Run("恢复 PBO", ct => engine.ApplyPbo(baseline, ct)); }, false, 145));
        pboCard.Controls.Add(pboButtons); right.Controls.Add(pboCard);

        var fileCard = PanelOf("参数与诊断", 107);
        var files = Flow(39);
        files.Controls.Add(Mutation("保存参数", SaveProfile, false, 100));
        files.Controls.Add(Mutation("载入参数", LoadProfile, false, 100));
        files.Controls.Add(ButtonOf("导出诊断", ExportDiagnostic, false, 100));
        fileCard.Controls.Add(files);
        fileCard.Controls.Add(LabelOf("保存不等于应用；组件不创建开机任务。", 9, Muted)); right.Controls.Add(fileCard);
        right.SizeChanged += (_, _) => { foreach (Control card in right.Controls) card.Width = Math.Max(320, right.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4); };

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 5, 0, 0) };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); progress.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        operation.Dock = DockStyle.Fill; operation.TextAlign = ContentAlignment.MiddleLeft; progress.Controls.Add(operation, 0, 0);
        stop = ButtonOf("停止当前操作", () => { current?.Cancel(); operation.Text = "正在停止负载并完成必要回读 / 恢复…"; }, false, 150); stop.Enabled = false;
        progress.Controls.Add(stop, 1, 0); bottom.Controls.Add(progress, 0, 0); bottom.Controls.Add(log, 0, 1); layout.Controls.Add(bottom, 0, 2);
        layout.Controls.Add(LabelOf("ZERO CPU 1.0   ·   CO 是曲线参数，单位不是 mV   ·   独立界面 / SMU 后端 / y-cruncher 0.8.5", 9, Muted), 0, 3);

        engine.Message += message => OnUi(() => AddLog(message));
        engine.StateChanged += () => OnUi(UpdateState);
        engine.StressProgress += (rounds, elapsed) => OnUi(() => countdown.Text = $"{rounds:00} / 05   ·   剩余约 " + TimeSpan.FromSeconds(Math.Max(0, 750 - elapsed.TotalSeconds)).ToString(@"mm\:ss"));
        telemetry.Tick += (_, _) => UpdateTelemetry(); telemetry.Start();
        FormClosing += OnClosing;
        SetEnabled();
        if (File.Exists(AppPaths.Recovery))
        {
            try { if (JsonFiles.Read<Profile>(AppPaths.Recovery).Pending) { status.Text = "上次操作中断 · 请连接后检查恢复记录"; AddLog("发现未结束的操作记录。启动未应用任何参数，可连接后选择“恢复中断前参数”。"); } }
            catch (Exception e) { AddLog("恢复记录读取失败：" + e.Message); }
        }
    }
    private static string CpuBrand()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Windows x64 · AMD Ryzen";
    }
    // Layout dimensions are pixels. Point fonts independently followed display DPI,
    // growing inside fixed-height rows on high-scaling Windows desktops.
    private static Font UiFont(float points) => new("Microsoft YaHei UI", points * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static Label LabelOf(string text, float size, Color color) => new() { Text = text, AutoSize = true, Font = UiFont(size), ForeColor = color, Margin = new Padding(0, 3, 0, 6) };
    private static NumericUpDown Number(int min, int max, int value, int width) => new() { Minimum = min, Maximum = max, Value = value, Width = width, BackColor = Field, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, Margin = new Padding(3, 2, 10, 2) };
    private static FlowLayoutPanel Flow(int height = 40) => new() { Dock = DockStyle.Top, Height = height, WrapContents = false, AutoSize = false, Margin = Padding.Empty };
    private static FlowLayoutPanel PanelOf(string title, int height)
    {
        var panel = new FlowLayoutPanel { Width = 395, Height = height, BackColor = Card, Padding = new Padding(13, 10, 8, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        panel.ControlAdded += (_, e) =>
        {
            if (e.Control is FlowLayoutPanel child)
            {
                child.Dock = DockStyle.None;
                child.Width = Math.Max(290, panel.ClientSize.Width - panel.Padding.Horizontal - 2);
            }
        };
        panel.SizeChanged += (_, _) =>
        {
            foreach (Control child in panel.Controls)
                if (child is FlowLayoutPanel) child.Width = Math.Max(290, panel.ClientSize.Width - panel.Padding.Horizontal - 2);
        };
        panel.Controls.Add(LabelOf(title, 13, TextColor)); return panel;
    }
    private static Button ButtonOf(string text, Action action, bool primary = false, int width = 130)
    {
        var b = new TunerButton { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(26, 103, 85) : Color.FromArgb(39, 52, 69), ForeColor = TextColor, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 7, 5) };
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(48, 155, 127) : Color.FromArgb(59, 74, 94);
        b.Click += (_, _) => action(); return b;
    }
    private Button Mutation(string text, Action action, bool primary = false, int width = 130)
    {
        var b = ButtonOf(text, action, primary, width); mutations.Add(b); return b;
    }
    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
    private void AddLog(string message)
    {
        if (log.TextLength > 32000) log.Text = log.Text[^16000..];
        log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\r\n");
        operation.Text = message.Replace('\n', ' ')[..Math.Min(115, message.Length)];
    }
    private bool Confirm(string message) => MessageBox.Show(this, message, "开始 CPU 操作", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
    private async Task Run(string name, Func<CancellationToken, Task> work)
    {
        if (busy) return;
        busy = true; current = new(); SetEnabled(); operation.Text = name + "…";
        try { await work(current.Token); operation.Text = name + "已结束"; }
        catch (OperationCanceledException) { AddLog("已停止；已执行恢复或保留操作前参数。"); }
        catch (Exception e) { AddLog(e.Message); MessageBox.Show(this, e.Message, name + "未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { current.Dispose(); current = null; busy = false; UpdateState(); SetEnabled(); }
    }
    private void SetEnabled()
    {
        connect.Enabled = !busy;
        cruncher.Enabled = !busy;
        foreach (var control in mutations) control.Enabled = !busy && engine.Snapshot != null;
        foreach (var row in rows) { row.Input.Enabled = !busy; row.Slider.Enabled = !busy; }
        start.Enabled = memory.Enabled = retreat.Enabled = step.Enabled = pboOffset.Enabled = !busy;
        stop.Enabled = busy;
    }
    private void UpdateState()
    {
        if (engine.Snapshot == null) return;
        var state = engine.Snapshot;
        if (rows.Count != state.Offsets.Length) BuildRows(state.Topology);
        SetDraft(state.Offsets);
        for (int i = 0; i < rows.Count; i++) rows[i].Readback.Text = "已读 " + state.Offsets[i];
        status.Text = $"已连接 · {state.Topology.ActiveCoreCount} 核 / {state.Topology.LogicalProcessorCount} 线程 · SMU {state.Topology.Identity.SmuTableVersion:X6}";
        validation.Text = engine.Validation;
        if (engine.Pbo?.CurrentMhz is int actual && engine.SessionStart?.PboMhz is int baseline)
        {
            pboState.Text = $"当前 {actual} MHz / 初值 {baseline} MHz";
            pboOffset.Value = Math.Clamp(actual - baseline, -800, 200);
        }
        else pboState.Text = "PBO 不可用：" + (engine.Pbo?.Reason ?? "尚未读取");
        SetEnabled();
    }
    private void BuildRows(CpuTopology topology)
    {
        rows.Clear(); coreGroups.Controls.Clear(); coreGroups.RowStyles.Clear();
        var indexed = new (NumericUpDown, TrackBar, Label, Label)[topology.ActiveCoreCount];
        var groups = topology.CoreSlots.GroupBy(c => c.CcdIndex).OrderBy(g => g.Key).ToArray();
        coreGroups.RowCount = (groups.Length + 1) / 2;
        for (int r = 0; r < coreGroups.RowCount; r++) coreGroups.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / coreGroups.RowCount));
        for (int g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0, 4, 9, 0) };
            var groupHeader = Flow(38); groupHeader.Width = 360;
            groupHeader.Controls.Add(LabelOf($"CCD {group.Key}", 12, Mint));
            var indexes = group.Select(c => c.UiIndex).ToArray();
            groupHeader.Controls.Add(Mutation("−", () => Shift(indexes, -(int)step.Value), false, 44));
            groupHeader.Controls.Add(Mutation("+", () => Shift(indexes, (int)step.Value), false, 44));
            panel.Controls.Add(groupHeader);
            foreach (var core in group)
            {
                var box = new TableLayoutPanel { Width = 360, Height = 51, ColumnCount = 3, RowCount = 2, BackColor = Field, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(7, 3, 3, 0) };
                box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); box.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 73));
                box.RowStyles.Add(new RowStyle(SizeType.Absolute, 21)); box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var label = LabelOf("核心 " + core.UiIndex, 10, TextColor);
                var vid = LabelOf("VID —", 9, Mint);
                var readback = LabelOf("已读 —", 9, Muted);
                var slider = new TrackBar { Minimum = -50, Maximum = 10, TickStyle = TickStyle.None, AutoSize = false, Height = 24, Dock = DockStyle.Fill, BackColor = Field, Margin = Padding.Empty };
                var input = Number(-50, 10, 0, 64);
                label.Margin = vid.Margin = readback.Margin = input.Margin = Padding.Empty;
                input.ValueChanged += (_, _) => { if (!syncing) { slider.Value = (int)input.Value; validation.Text = "待应用值已修改 · 点击“应用 CO 并回读”生效"; } };
                slider.ValueChanged += (_, _) => { if (!syncing) input.Value = slider.Value; };
                box.Controls.Add(label, 0, 0); box.Controls.Add(vid, 1, 0); box.Controls.Add(readback, 2, 0);
                box.Controls.Add(slider, 0, 1); box.SetColumnSpan(slider, 2); box.Controls.Add(input, 2, 1);
                indexed[core.UiIndex] = (input, slider, vid, readback); panel.Controls.Add(box);
                panel.SizeChanged += (_, _) => box.Width = Math.Max(290, panel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            }
            coreGroups.Controls.Add(panel, g % 2, g / 2);
        }
        rows.AddRange(indexed);
    }
    private void UpdateTelemetry()
    {
        var frame = engine.Frame;
        for (int i = 0; i < rows.Count; i++) rows[i].Vid.Text = frame != null && i < frame.Values.Length ? $"VID {frame.Values[i]:F3} V" : "VID —";
    }
    private int[] Draft() => rows.Select(r => (int)r.Input.Value).ToArray();
    private void SetDraft(int[] values)
    {
        syncing = true;
        for (int i = 0; i < Math.Min(values.Length, rows.Count); i++) { rows[i].Input.Value = values[i]; rows[i].Slider.Value = values[i]; }
        syncing = false;
    }
    private void Shift(int[]? indexes, int amount)
    {
        foreach (int i in indexes ?? Enumerable.Range(0, rows.Count).ToArray()) rows[i].Input.Value = Math.Clamp((int)rows[i].Input.Value + amount, -50, 10);
    }
    private void SaveProfile()
    {
        using var picker = new SaveFileDialog { Filter = "CPU 参数 (*.json)|*.json", FileName = "zero-cpu-profile.json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { engine.SaveCurrent(picker.FileName); AddLog("已保存实际回读参数；未应用的滑块值不包含在此档案中。"); }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
    }
    private void LoadProfile()
    {
        using var picker = new OpenFileDialog { Filter = "CPU 参数 (*.json)|*.json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var profile = JsonFiles.Read<Profile>(picker.FileName); profile.Snapshot.Validate();
            if (profile.Snapshot.Topology.MapHash != engine.Snapshot!.Topology.MapHash) throw new IOException("档案与当前 CPU 拓扑不一致。");
            SetDraft(profile.Snapshot.Offsets);
            validation.Text = "已载入 CO 待应用值；请点击应用。";
            AddLog("已载入 CO 到编辑区，未写入硬件。档案内 PBO 仅作记录，需单独应用。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "载入失败"); }
    }
    private async void RestorePending()
    {
        if (!File.Exists(AppPaths.Recovery)) { MessageBox.Show(this, "没有恢复记录。"); return; }
        try
        {
            var profile = JsonFiles.Read<Profile>(AppPaths.Recovery);
            if (Confirm($"恢复至 {profile.SavedAt:yyyy-MM-dd HH:mm:ss} 的操作前参数？\n{profile.Status}"))
                await Run("恢复记录", _ => engine.Restore(profile));
        }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
    }
    private void ExportDiagnostic()
    {
        using var picker = new SaveFileDialog { Filter = "诊断 JSON (*.json)|*.json", FileName = "zero-cpu-diagnostic.json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            JsonFiles.Write(picker.FileName, new { version = "1.0.0", cpu = CpuBrand(), administrator = AppPaths.IsAdmin, snapshot = engine.Snapshot, pbo = engine.Pbo,
                validation = engine.Validation, frame = engine.Frame, pending = File.Exists(AppPaths.Recovery) ? JsonFiles.Read<Profile>(AppPaths.Recovery) : null,
                log = log.Text, hardwareWritesVerifiedInDevelopment = false });
            AddLog("已导出当前状态和运行日志。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
    }
    private void Elevate()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            Close();
        }
        catch (Exception e) { AddLog("未切换管理员模式：" + e.Message); }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        if (busy) { current?.Cancel(); e.Cancel = true; operation.Text = "已请求停止，完成回读 / 恢复后可关闭。"; return; }
        e.Cancel = true;
        closing = true; telemetry.Stop();
        await engine.DisposeAsync();
        Close();
    }
    private sealed class TunerButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 8, Height - 8));
        }
    }
}
