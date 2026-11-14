using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace AeroficsLauncher;

public sealed partial class LauncherForm : Form
{
    private readonly LauncherConfig _config;
    private readonly HwInfoVoltageProvider _hwInfoProvider = new();
    private readonly Bitmap _wallpaper;
    private Bitmap? _backdrop;
    private readonly List<GlassButton> _toolButtons = new();
    private readonly GlassButton _launch = new() { Name = "LaunchZeroCpuTuner", Text = "以管理员身份启动", Warm = true, Compact = true };
    private readonly GlassButton _install = new() { Name = "InstallAeroficsOptimizer", Text = "↓  安装到桌面", Primary = true, Compact = true };
    private readonly GlassButton _test = new() { Name = "QualityTest", Text = "一键体质测试", Compact = true };
    private readonly GlassButton _close = new() { Name = "ExitToolbox", Text = "×", Compact = true };
    private readonly GlassButton _minimize = new() { Name = "MinimizeToolbox", Text = "—", Compact = true };
    private readonly Label _motherboardLabel = new()
    {
        Name = "MotherboardModel", AccessibleName = "主板型号", TextAlign = ContentAlignment.MiddleRight,
        AutoEllipsis = true, BackColor = Color.Transparent, ForeColor = Color.FromArgb(191, 216, 237)
    };
    private readonly ToolTip _tips = new();
    private readonly RichTextBox _resultsBox = new()
    {
        Name = "QualityTestResults", ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(13, 24, 42), ForeColor = Color.FromArgb(205, 220, 239),
        ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true, DetectUrls = false, TabStop = true
    };
    private bool _testing;
    private string _testResult = "点击「一键体质测试」后在这里查看结果。";
    private float _scale = 1;
    private RectangleF _left, _right, _results;
    private readonly Dictionary<string, Font> _fonts = new();

    public LauncherForm(LauncherConfig config)
    {
        _config = config;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aerofics.Wallpaper.jpg")
            ?? throw new IOException("缺少背景图片");
        using var original = new Bitmap(resource);
        _wallpaper = Blur(original);
        Text = "Aerofics 超频工具箱 · 2026.11.15";
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1000, 720);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(8, 15, 28);
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        foreach (var item in config.Programs)
        {
            var button = new GlassButton { Name = "Tool_" + item.Name, Text = item.Name, AccessibleName = item.Name, ToolIcon = item.Icon, Backdrop = () => _backdrop };
            button.Click += (_, _) => OpenTool(item);
            _toolButtons.Add(button);
            Controls.Add(button);
        }
        foreach (var button in new[] { _launch, _install, _test, _close, _minimize }) { button.Backdrop = () => _backdrop; Controls.Add(button); }
        _launch.Click += (_, _) => LaunchTuner();
        _install.Click += (_, _) => InstallOptimizer();
        _test.Click += async (_, _) => await RunQualityTestAsync();
        _close.Click += (_, _) => Close();
        _minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _tips.SetToolTip(_close, "退出（Esc）");
        _tips.SetToolTip(_minimize, "最小化");
        _motherboardLabel.Text = "主板：" + MotherboardInfo.Read();
        _tips.SetToolTip(_motherboardLabel, _motherboardLabel.Text);
        Controls.Add(_motherboardLabel);
        Controls.Add(_resultsBox);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Resize += (_, _) => Reflow();
        Reflow();
    }

    private Font Ui(float pixels, bool bold = false)
    {
        float size = Math.Max(11, pixels * _scale);
        string key = $"{size:F2}-{bold}";
        if (!_fonts.TryGetValue(key, out var font)) _fonts[key] = font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return font;
    }

    private float S(float value) => value * _scale;
    private void Reflow()
    {
        if (ClientSize.Width < 200 || ClientSize.Height < 200) return;
        _scale = Math.Clamp(Math.Min(ClientSize.Width / 1280f, ClientSize.Height / 900f), .68f, 2f);
        float pad = S(28), side = S(255), gap = S(20), top = S(180), bottom = ClientSize.Height - S(80);
        _left = new RectangleF(pad, top, side, bottom - top);
        _right = new RectangleF(ClientSize.Width - pad - side, top, side, bottom - top);
        var center = new RectangleF(_left.Right + gap, top, _right.Left - gap - (_left.Right + gap), bottom - top);
        float gridTop = center.Top + S(35), gridGap = S(10), resultHeight = S(116);
        float cardWidth = (center.Width - 3 * gridGap) / 4;
        int rows = (int)Math.Ceiling((_toolButtons.Count + 2) / 4f);
        float cardHeight = (center.Height - S(35) - resultHeight - S(18) - (rows - 1) * gridGap) / rows;
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            _toolButtons[i].Bounds = Rectangle.Round(new RectangleF(center.Left + (i % 4) * (cardWidth + gridGap), gridTop + (i / 4) * (cardHeight + gridGap), cardWidth, cardHeight));
            _toolButtons[i].Font = Ui(15);
        }
        _test.Bounds = Rectangle.Round(new RectangleF(center.Left + 2 * (cardWidth + gridGap), gridTop + (rows - 1) * (cardHeight + gridGap), 2 * cardWidth + gridGap, cardHeight));
        _test.Font = Ui(17, true);
        _results = new RectangleF(center.Left, bottom - resultHeight, center.Width, resultHeight);
        _resultsBox.Bounds = Rectangle.Round(new RectangleF(_results.Left + S(17), _results.Top + S(39), _results.Width - S(34), _results.Height - S(53)));
        _resultsBox.Font = Ui(14);
        _launch.Bounds = Rectangle.Round(new RectangleF(_left.Left + S(21), _left.Bottom - S(69), _left.Width - S(42), S(47)));
        _install.Bounds = Rectangle.Round(new RectangleF(_right.Left + S(21), _right.Bottom - S(69), _right.Width - S(42), S(47)));
        _launch.Font = _install.Font = Ui(16, true);
        _close.Bounds = Rectangle.Round(new RectangleF(ClientSize.Width - S(60), S(22), S(36), S(31)));
        _minimize.Bounds = Rectangle.Round(new RectangleF(ClientSize.Width - S(104), S(22), S(36), S(31)));
        _close.Font = _minimize.Font = Ui(22);
        _motherboardLabel.Font = Ui(13);
        _motherboardLabel.Bounds = Rectangle.Round(new RectangleF(ClientSize.Width * .44f, ClientSize.Height - S(44), ClientSize.Width * .56f - pad, S(24)));
        RebuildBackdrop();
        Invalidate(true);
    }

    private void RebuildBackdrop()
    {
        if (_backdrop?.Size == ClientSize) return;
        _backdrop?.Dispose();
        _backdrop = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var g = Graphics.FromImage(_backdrop);
        g.Clear(Color.FromArgb(8, 15, 28));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float cover = Math.Max(ClientSize.Width / (float)_wallpaper.Width, ClientSize.Height / (float)_wallpaper.Height);
        int w = (int)(_wallpaper.Width * cover), h = (int)(_wallpaper.Height * cover);
        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = .40f };
        attributes.SetColorMatrix(matrix);
        g.DrawImage(_wallpaper, new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h), 0, 0, _wallpaper.Width, _wallpaper.Height, GraphicsUnit.Pixel, attributes);
        using var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(0, 4, 10, 24), Color.FromArgb(115, 4, 10, 24), 90);
        g.FillRectangle(shade, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_backdrop != null) g.DrawImageUnscaled(_backdrop, Point.Empty);
        TextAt(g, "aerofics", new RectangleF(S(28), S(23), S(190), S(48)), 39, Color.FromArgb(128, 232, 255), true);
        TextAt(g, "超频工具箱", new RectangleF(S(235), S(29), S(240), S(25)), 18, Color.White);
        TextAt(g, "HARDWARE · PERFORMANCE", new RectangleF(S(235), S(57), S(270), S(24)), 12, Color.FromArgb(178, 193, 215));
        TextAt(g, "掌控硬件，释放性能。", new RectangleF(S(28), S(106), S(610), S(39)), 29, Color.White, true);
        TextAt(g, "硬件检测 · 性能调校 · 系统优化", new RectangleF(S(28), S(146), S(610), S(23)), 14, Color.FromArgb(178, 193, 215));
        Panel(g, _left, Color.FromArgb(160, 255, 196, 123));
        Panel(g, _right, Color.FromArgb(160, 128, 232, 255));
        Panel(g, _results, Color.FromArgb(65, 161, 204, 255));
        TextAt(g, "常用工具", new RectangleF(_results.X, _left.Y, _results.Width, S(24)), 17, Color.White);
        SideText(g, _left, "实验性功能", 21, 15, Color.FromArgb(231, 197, 155));
        SideText(g, _left, "CPU", 65, 36, Color.FromArgb(255, 204, 145), true);
        SideText(g, _left, "ZeroCpuTuner", 127, 24, Color.White, true);
        SideText(g, _left, "CPU 调校工具", 164, 14, Color.FromArgb(178, 193, 215));
        SideText(g, _left, "请以管理员\n模式运行", 217, 27, Color.FromArgb(255, 224, 184), true, 85);
        SideText(g, _left, "仅推荐 983\n及以下等级使用", 321, 20, Color.FromArgb(255, 207, 153), true, 72);
        SideText(g, _left, "不推荐 9850、9950\n或未来的新 CPU 使用", 410, 19, Color.FromArgb(255, 181, 191), true, 77);
        SideText(g, _right, "桌面安装", 21, 15, Color.FromArgb(178, 193, 215));
        SideText(g, _right, "↓", 57, 49, Color.FromArgb(128, 232, 255), true, 67);
        SideText(g, _right, "aerofics 优化器", 127, 24, Color.White, true);
        SideText(g, _right, "AeroficsOptimizer · 2.0.0", 164, 13, Color.FromArgb(178, 193, 215));
        SideText(g, _right, "点击在桌面安装", 225, 23, Color.White, false, 40);
        SideText(g, _right, "aerofics 优化器", 268, 24, Color.FromArgb(128, 232, 255), true, 43);
        SideText(g, _right, "点击后确认同意，\n将 EXE 复制一份到桌面，\n方便以后直接打开。", 339, 16, Color.FromArgb(193, 206, 225), false, 95);
        SideText(g, _right, "AeroficsOptimizer-2.0.0.exe", 453, 12, Color.FromArgb(193, 206, 225), false, 43);
        TextAt(g, "体质测试结果", new RectangleF(_results.Left + S(17), _results.Top + S(11), _results.Width - S(34), S(25)), 15, Color.White);
        if (_resultsBox.Text != _testResult) _resultsBox.Text = _testResult;
        _test.Enabled = !_testing;
        if (_test.Text != (_testing ? "正在测试…" : "一键体质测试")) _test.Text = _testing ? "正在测试…" : "一键体质测试";
        TextAt(g, "电脑超频 · 系统优化\n微信 aerofics2   ·   QQ 981401384", new RectangleF(S(28), ClientSize.Height - S(62), ClientSize.Width * .43f, S(47)), 12, Color.FromArgb(178, 193, 215));
    }

    private void SideText(Graphics g, RectangleF panel, string text, float top, float size, Color color, bool bold = false, float height = 38)
        => TextAt(g, text, new RectangleF(panel.Left + S(22), panel.Top + S(top), panel.Width - S(44), S(height)), size, color, bold);

    private void TextAt(Graphics g, string text, RectangleF r, float size, Color color, bool bold = false)
    {
        TextRenderer.DrawText(g, text, Ui(size, bold), Rectangle.Round(r), color,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void Panel(Graphics g, RectangleF r, Color border)
    {
        using var path = RoundedPath(r, S(20));
        using var fill = new SolidBrush(Color.FromArgb(170, 13, 24, 44));
        using var pen = new Pen(border, 1);
        g.FillPath(fill, path); g.DrawPath(pen, path);
    }

    private void InstallOptimizer()
    {
        string destination = BundledTools.DesktopOptimizerPath();
        bool exists = File.Exists(destination);
        string text = "将 AeroficsOptimizer-2.0.0.exe 复制一份到桌面。" + (exists ? "\n桌面已有同名文件，同意后将替换。" : "");
        using var dialog = new InstallDialog(text);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            BundledTools.CopyOptimizer(_config, destination, exists);
            MessageBox.Show(this, "已复制到桌面：\n" + destination, "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, "复制失败：\n" + ex.Message, "Aerofics 优化器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LaunchTuner()
    {
        try { Process.Start(BundledTools.TunerStartInfo(_config)); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* User cancelled the Windows elevation prompt. */ }
        catch (Exception ex) { MessageBox.Show(this, "启动失败：\n" + ex.Message, "ZeroCpuTuner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenTool(ProgramItem item)
    {
        try
        {
            ConfigureSensorsOnly(item);
            Process.Start(new ProcessStartInfo { FileName = item.FullPath, WorkingDirectory = Path.GetDirectoryName(item.FullPath)!, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, "启动失败：\n" + ex.Message, item.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    internal static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    private static Bitmap Blur(Bitmap source)
    {
        using var small = new Bitmap(Math.Max(1, source.Width / 3), Math.Max(1, source.Height / 3));
        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, new Rectangle(Point.Empty, small.Size)); }
        var result = new Bitmap(source.Width, source.Height);
        using (var g = Graphics.FromImage(result)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(small, new Rectangle(Point.Empty, result.Size)); }
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wallpaper.Dispose(); _backdrop?.Dispose(); _tips.Dispose();
            foreach (var item in _config.Programs) item.Icon?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) foreach (var font in _fonts.Values) font.Dispose();
    }
}
