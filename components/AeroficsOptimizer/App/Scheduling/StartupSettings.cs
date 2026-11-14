using System;
using System.IO;
using Microsoft.Win32;

namespace AeroficsLauncher;

internal static class StartupSettings
{
    internal const string ValueName = "AeroficsThreadSchedulerExtended";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Enabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }
    internal static string Command(string exe)
    {
        if (!Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.Contains('"') || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请从正式构建的 EXE 设置开机启动");
        string command = "\"" + exe + "\"";
        if (command.Length > 260) throw new InvalidOperationException("启动路径过长，请移到较短的目录");
        return command;
    }
    internal static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, Command(SystemOptimizer.App.App.EntryExecutablePath), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
