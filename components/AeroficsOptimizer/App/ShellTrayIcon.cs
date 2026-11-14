using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace SystemOptimizer.App;
internal sealed class ShellTrayIcon : IDisposable
{
    private const int Callback = 0x8051;
    private readonly HwndSource _source;
    private readonly Action _restore;
    private readonly ContextMenu _menu;
    private NotifyData _data;
    private bool _shown;
    internal ShellTrayIcon(Window window, Action restore, Action stop, Action close)
    {
        _restore = restore;
        var hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(hwnd)!; _source.AddHook(Hook);
        _data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4,
            CallbackMessage = Callback, Icon = LoadIcon(0, (nint)32512), Tip = "Aerofics 性能工作台", Info = "", InfoTitle = "" };
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        foreach (var item in new[] { ("显示工作台", restore), ("停止并恢复", stop), ("退出", close) })
        { var menu = new MenuItem { Header = item.Item1 }; menu.Click += (_, _) => item.Item2(); _menu.Items.Add(menu); }
    }
    internal bool Show() { if (!_shown) _shown = Shell_NotifyIcon(0, ref _data); return _shown; }
    internal void Hide() { if (_shown) { Shell_NotifyIcon(2, ref _data); _shown = false; } }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Callback) { if ((long)lParam == 0x203) _restore(); else if ((long)lParam == 0x205) { SetForegroundWindow(hwnd); _menu.IsOpen = true; } handled = true; }
        return 0;
    }
    public void Dispose() { Hide(); _source.RemoveHook(Hook); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags; public int CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
