using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace AeroficsLauncher;

internal static class Package
{
    // Change this when replacing bundled tools. UI-only updates reuse their settings.
    private const string PayloadVersion = "20261115-public-1";
    private static string DataDirectory
    {
        get
        {
            string? local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(local)) local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "AeroficsLauncher");
        }
    }

    public static string ResultDirectory => Path.Combine(DataDirectory, "Results");

    public static LauncherConfig Load()
    {
        string parent = Path.Combine(DataDirectory, "Packages");
        string root = Path.Combine(parent, PayloadVersion);
        Directory.CreateDirectory(parent);
        using var mutex = new Mutex(false, @"Local\AeroficsPackage_" + PayloadVersion);
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("另一个工具箱正在准备资源，请稍后重试");
            if (!File.Exists(Path.Combine(root, ".complete")))
            {
                string staging = Path.Combine(parent, PayloadVersion + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aerofics.Payload.enc")
                        ?? throw new IOException("内置工具包缺失");
                    using var buffer = new MemoryStream();
                    resource.CopyTo(buffer);
                    var encrypted = buffer.ToArray();
                    if (encrypted.Length < 28) throw new IOException("内置工具包损坏");
                    var plain = new byte[encrypted.Length - 28];
                    try
                    {
                        using (var aes = new AesGcm(PayloadKey.Value, 16))
                            aes.Decrypt(encrypted.AsSpan(0, 12), encrypted.AsSpan(28), encrypted.AsSpan(12, 16), plain);
                        using var zip = new ZipArchive(new MemoryStream(plain, false), ZipArchiveMode.Read);
                        zip.ExtractToDirectory(staging);
                    }
                    finally { CryptographicOperations.ZeroMemory(plain); }
                    ReadConfig(staging);
                    File.WriteAllText(Path.Combine(staging, ".complete"), PayloadVersion);
                    if (Directory.Exists(root)) throw new IOException("工具缓存不完整，请关闭工具后清理此目录再试：" + root);
                    Directory.Move(staging, root);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }
            return ReadConfig(root);
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static LauncherConfig ReadConfig(string root)
    {
        var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(Path.Combine(root, "launcher.json")))
            ?? throw new IOException("内置配置无效");
        config.Root = root;
        foreach (var item in config.Programs)
            if (!File.Exists(Path.Combine(root, item.Exe))) throw new IOException("程序缺失：" + item.Name);
        foreach (string exe in new[] { config.OptimizerExe, config.TunerExe })
            if (!File.Exists(Path.Combine(root, exe))) throw new IOException("附加程序缺失：" + exe);
        return config;
    }
}
