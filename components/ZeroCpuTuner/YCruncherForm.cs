using System.Diagnostics;

namespace ZeroCpuTuner;

public sealed class YCruncherForm : Form
{
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckedListBox algorithms = new() { Height = 150, Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly NumericUpDown memory = Number(512, 262144, 2048);
    private readonly NumericUpDown duration = Number(1, 3600, 60);
    private readonly NumericUpDown total = Number(1, 1440, 15);
    private readonly NumericUpDown threads = Number(1, Environment.ProcessorCount, Environment.ProcessorCount);
    private readonly TextBox output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 14, FontStyle.Regular, GraphicsUnit.Pixel) };
    private readonly Label status = new() { Text = "就绪 · 测试不会改写 CO / PBO", AutoSize = true };
    private readonly Label command = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Button start = new() { Text = "开始测试", AutoSize = true };
    private readonly Button stop = new() { Text = "停止", AutoSize = true, Enabled = false };
    private readonly Panel settings = new() { Dock = DockStyle.Fill };
    private Process? process;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private bool closeAfter;
    private string? lastLog;

    public YCruncherForm()
    {
        Text = "Zero CPU / y-cruncher 测试中心";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1060, 790); MinimumSize = new Size(940, 740);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        foreach (var h in new[] { 55, 260, 55, 45 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Y-CRUNCHER  /  压力测试与 π 跑分", Font = new Font(Font.FontFamily, 28, FontStyle.Regular, GraphicsUnit.Pixel), AutoSize = true }, 0, 0);
        root.Controls.Add(settings, 0, 1);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 165)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        settings.Controls.Add(grid);
        mode.Items.AddRange(["组件压力测试", "π 基准测试"]); mode.SelectedIndex = 0;
        size.Items.AddRange(["25m", "50m", "100m", "250m", "500m", "1b", "2.5b"]); size.SelectedIndex = 0;
        grid.Controls.Add(Row("模式", mode), 0, 0);
        foreach (var a in new[] { "BKT", "BBP", "SFT", "SFTv4", "SNT", "SVT", "FFT", "FFTv4", "NTT63", "VT3" }) algorithms.Items.Add(a, a == "VT3");
        grid.Controls.Add(algorithms, 1, 1);
        grid.Controls.Add(new Label { Text = "压力项目（可多选）", AutoSize = true }, 1, 0);
        var parameters = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        parameters.Controls.Add(Row("内存 MiB", memory)); parameters.Controls.Add(Row("每项秒数", duration)); parameters.Controls.Add(Row("总时长 / 分", total));
        grid.Controls.Add(parameters, 0, 1);
        grid.Controls.Add(Row("π 位数", size, new Label { Text = "线程", AutoSize = true }, threads), 0, 2);
        grid.Controls.Add(new Label { Text = "引擎自动选择 CPU 指令集；不支持的项目以日志为准。", Dock = DockStyle.Fill }, 1, 2);
        root.Controls.Add(command, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(start); actions.Controls.Add(stop);
        var logs = new Button { Text = "打开日志文件夹", AutoSize = true };
        logs.Click += (_, _) => { Directory.CreateDirectory(AppPaths.DataRoot); Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataRoot) { UseShellExecute = true }); };
        actions.Controls.Add(logs); root.Controls.Add(actions, 0, 3); root.Controls.Add(output, 0, 4); root.Controls.Add(status, 0, 5);
        start.Click += async (_, _) => await Run(); stop.Click += (_, _) => cancellation?.Cancel();
        mode.SelectedIndexChanged += (_, _) => RefreshOptions(); size.SelectedIndexChanged += (_, _) => Preview();
        foreach (var n in new[] { memory, duration, total, threads }) n.ValueChanged += (_, _) => Preview();
        algorithms.ItemCheck += (_, _) => { if (IsHandleCreated) BeginInvoke(Preview); };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; closeAfter = true; cancellation?.Cancel(); } };
        ApplyTheme(root);
        mode.ForeColor = size.ForeColor = Color.Black;
        mode.BackColor = size.BackColor = Color.White;
        foreach (var combo in new[] { mode, size })
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 24;
            combo.DrawItem += (_, e) =>
            {
                e.DrawBackground();
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString(), Font, e.Bounds, Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
        }
        RefreshOptions();
    }
    private static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 105 };
    private static FlowLayoutPanel Row(string text, params Control[] controls)
    {
        var row = new FlowLayoutPanel { Width = 550, Height = 43, WrapContents = false };
        row.Controls.Add(new Label { Text = text, Width = 115, Padding = new Padding(0, 5, 0, 0) }); row.Controls.AddRange(controls); return row;
    }
    private static void ApplyTheme(Control c)
    {
        c.BackColor = Color.FromArgb(20, 28, 39); c.ForeColor = Color.FromArgb(225, 235, 245);
        if (c is Button button) button.FlatStyle = FlatStyle.Flat;
        foreach (Control child in c.Controls) ApplyTheme(child);
    }
    private void RefreshOptions()
    {
        bool stress = mode.SelectedIndex == 0;
        algorithms.Enabled = memory.Enabled = duration.Enabled = total.Enabled = stress;
        size.Enabled = threads.Enabled = !stress; Preview();
    }
    private string[] Arguments()
    {
        List<string> args = ["pause:-2", "skip-warnings", "priority:-1", "PII:-1"];
        if (mode.SelectedIndex == 0)
        {
            args.AddRange(["stress", "-M:" + ((long)memory.Value * 1024 * 1024), "-D:" + duration.Value, "-TL:" + ((int)total.Value * 60)]);
            args.AddRange(algorithms.CheckedItems.Cast<string>());
        }
        else args.AddRange(["bench", size.Text, "-TD:" + threads.Value, "-od:0"]);
        return args.ToArray();
    }
    private void Preview() => command.Text = "命令预览：y-cruncher " + string.Join(' ', Arguments());
    private async Task Run()
    {
        if (busy) return;
        if (mode.SelectedIndex == 0 && algorithms.CheckedItems.Count == 0) { status.Text = "请至少选择一个压力项目。"; return; }
        busy = true; settings.Enabled = start.Enabled = false; stop.Enabled = true;
        cancellation = new(); output.Clear(); status.Text = "正在启动…";
        bool detectedError = false; bool observedPass = false;
        var clock = Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => status.Text = "运行中 · " + clock.Elapsed.ToString(@"hh\:mm\:ss") + " · 可随时停止";
        try
        {
            AppPaths.Extract();
            lastLog = Path.Combine(AppPaths.DataRoot, "ycruncher-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
            using var writer = new StreamWriter(lastLog);
            writer.WriteLine(string.Join(' ', Arguments()));
            var info = ChildProcess.Info(AppPaths.StressExe, Arguments());
            info.WorkingDirectory = AppPaths.DataRoot;
            process = Process.Start(info) ?? throw new IOException("y-cruncher 启动失败。");
            using var registration = cancellation.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            async Task Drain(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    writer.WriteLine(line);
                    if (line.Contains(": Failed", StringComparison.OrdinalIgnoreCase) || line.Contains("Exception Encountered", StringComparison.OrdinalIgnoreCase) || line.Contains("Fatal Error", StringComparison.OrdinalIgnoreCase)) detectedError = true;
                    if (line.Contains(": Passed", StringComparison.OrdinalIgnoreCase)) observedPass = true;
                    if (output.TextLength > 180000) output.Text = output.Text[^90000..];
                    output.AppendText(line + Environment.NewLine);
                }
            }
            timer.Start();
            await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync());
            timer.Stop();
            status.Text = cancellation.IsCancellationRequested ? "已手动停止 · 不判定为通过" : detectedError || process.ExitCode != 0 ? $"测试出错 · 退出码 {process.ExitCode} · 请查看日志" : mode.SelectedIndex == 0 ? observedPass ? "测试结束 · 已观察到通过记录；详细轮次见日志" : "测试已退出 · 未观察到通过记录，请查看日志" : "π 跑分结束 · 结果见输出日志";
        }
        catch (Exception e) { status.Text = "运行失败：" + e.Message; output.AppendText(e + Environment.NewLine); }
        finally
        {
            timer.Stop(); process?.Dispose(); process = null; cancellation.Dispose(); cancellation = null;
            busy = false; settings.Enabled = start.Enabled = true; stop.Enabled = false;
            if (closeAfter) Close();
        }
    }
}
