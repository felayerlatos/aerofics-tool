using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AeroficsLauncher;

public sealed partial class ProcessSchedulerService : IDisposable
{
    internal Action<string>? Diagnostic { get; set; }
	public sealed class SafeProcessHandle : SafeHandle
	{
		public override bool IsInvalid
		{
			get
			{
				if (handle != IntPtr.Zero)
				{
					return handle == new IntPtr(-1);
				}
				return true;
			}
		}

		public SafeProcessHandle(nint handle)
			: base(IntPtr.Zero, ownsHandle: true)
		{
			SetHandle(handle);
		}

		protected override bool ReleaseHandle()
		{
			return NativeCloseHandle(handle);
		}

		[DllImport("kernel32.dll", EntryPoint = "CloseHandle")]
		private static extern bool NativeCloseHandle(nint handle);
	}

	private readonly object _gate = new object();

	private readonly string _storeDir;

	private readonly SchedulerSettings _defaults;

	private readonly Timer _timer;

	private readonly Func<Process[]> _enumerate;

	private readonly Func<SafeProcessHandle, string> _readPath;

	private SchedulerSettings _settings = new SchedulerSettings();

	private CpuSetTopology? _topology;

	private readonly Dictionary<string, RestoreState> _saved = new Dictionary<string, RestoreState>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<int> _blockedPids = new HashSet<int>();

	private readonly HashSet<string> _activeBindings = new HashSet<string>();

	private readonly List<string> _errors = new List<string>();

	private SchedulerStatus _status = new SchedulerStatus();

	private volatile bool _running;

	private volatile bool _stopRequested;

	private bool _disposed;

	private string _message = "未启动";

	private string? _lastSavedState;

	public SchedulerStatus Status => Volatile.Read(in _status);

	public CpuSetTopology? Topology => _topology;
    internal string StateDirectory => _storeDir;

	public event EventHandler? Changed;

	public ProcessSchedulerService(SchedulerSettings? defaults = null, string? stateDirectory = null)
		: this(defaults, stateDirectory, Process.GetProcesses)
	{
	}

	internal ProcessSchedulerService(SchedulerSettings? defaults, string? stateDirectory, Func<Process[]> enumerate, Func<SafeProcessHandle, string>? readPath = null)
	{
		_defaults = defaults ?? new SchedulerSettings();
		_enumerate = enumerate;
		_readPath = readPath ?? new Func<SafeProcessHandle, string>(ImagePath);
		_storeDir = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroficsLauncher", "SchedulerExtended");
		Directory.CreateDirectory(_storeDir);
		LoadState();
		if (_saved.Count > 0)
		{
			_message = "发现上次未恢复记录，请先点击“停止并恢复”";
		}
		PublishStatus();
		_timer = new Timer(delegate
		{
			Tick();
		}, null, -1, -1);
	}

	public SchedulerSettings LoadSettings()
	{
		lock (_gate)
		{
			if (_running)
			{
				return Clone(_settings);
			}
			string path = Path.Combine(_storeDir, "settings.json");
			try
			{
				_settings = (File.Exists(path) ? (JsonSerializer.Deserialize<SchedulerSettings>(File.ReadAllText(path)) ?? Clone(_defaults)) : Clone(_defaults));
			}
			catch
			{
				_settings = Clone(_defaults);
			}
			return Clone(_settings);
		}
	}

	private static SchedulerSettings Clone(SchedulerSettings s)
	{
		return new SchedulerSettings
		{
			GameCcd = s.GameCcd,
            GameCoreOrderEnabled = s.GameCoreOrderEnabled, BackgroundCoreOrderEnabled = s.BackgroundCoreOrderEnabled,
            GameCoreOrder = new(s.GameCoreOrder), BackgroundCoreOrder = new(s.BackgroundCoreOrder),
            CustomCpuSetIds = new(s.CustomCpuSetIds), StrictAffinity = s.StrictAffinity, GamePriority = s.GamePriority, Enforce = s.Enforce, MinimizeToTray = s.MinimizeToTray,
			BackgroundIsolation = s.BackgroundIsolation,
			GameProcesses = new List<string>(s.GameProcesses),
			BackgroundProcesses = new List<string>(s.BackgroundProcesses),
			ExcludedProcesses = new List<string>(s.ExcludedProcesses)
		};
	}

	public void SaveSettings(SchedulerSettings settings)
	{
		lock (_gate)
		{
			if (_running)
			{
				throw new InvalidOperationException("请先停止并恢复，再修改规则");
			}
			WriteAtomic("settings.json", JsonSerializer.Serialize(settings, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			_settings = Clone(settings);
		}
	}

	public void Start(SchedulerSettings settings)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_running)
			{
				return;
			}
			if (_saved.Count > 0)
			{
				throw new InvalidOperationException("还有上次未恢复记录，请先点击“停止并恢复”");
			}
			_topology = CpuSetTopologyReader.Read();
			if (!_topology.IsReliable)
			{
				throw new InvalidOperationException("无法可靠识别 CCD 拓扑，已阻止自动调度");
			}
			ValidateSettings(settings, _topology);
            SaveSettings(settings);
			_errors.Clear();
			_blockedPids.Clear();
			_activeBindings.Clear();
            _threadSamples.Clear();
            _blockedThreads.Clear();
			_stopRequested = false;
			_running = true;
			int num = ResolveGameCcd(settings.GameCcd);
			_message = ((num >= 0) ? $"运行中：游戏 CCD{num}" : "运行中：游戏全核心");
            if (settings.GameCoreOrderEnabled || settings.BackgroundCoreOrderEnabled)
                _message = $"运行中：游戏{(settings.GameCoreOrderEnabled ? "核心顺序" : "核心范围")} / 非游戏{(settings.BackgroundCoreOrderEnabled ? "核心顺序" : settings.BackgroundIsolation ? "后台隔离" : "不干预")}";
			PublishStatus();
			_timer.Change(0, -1);
		}
		Notify();
	}

	public void StopAndRestore()
	{
		_stopRequested = true;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			_running = false;
			_timer.Change(-1, -1);
			RestoreAll();
			_message = ((_saved.Count == 0) ? "已停止并恢复" : "已停止，仍有待恢复进程（见错误信息）");
			PublishStatus();
		}
		Notify();
	}

	private void Tick()
	{
		lock (_gate)
		{
			if (_disposed || !_running || _stopRequested || _topology == null)
			{
				return;
			}
			try
			{
				int num = ResolveGameCcd(_settings.GameCcd);
				uint[] array = _settings.CustomCpuSetIds.Count > 0 ? _settings.CustomCpuSetIds.ToArray() : ((num < 0) ? _topology.CpuSets.Select((CpuSetDescriptor c) => c.Id).ToArray() : _topology.Ccds[num].Select((CpuSetDescriptor c) => c.Id).Distinct().ToArray());
				uint[] backgroundIds = _topology.CpuSets.Select((CpuSetDescriptor c) => c.Id).Except(array).ToArray();
				Process[] array2 = _enumerate();
				try
				{
					Process[] array3 = array2;
					foreach (Process process in array3)
					{
						if (_stopRequested)
						{
							break;
						}
						ApplyToProcess(process, array, backgroundIds);
					}
					CleanupExited(array2.Select((Process p) => p.Id).ToHashSet());
				}
				finally
				{
					Process[] array3 = array2;
					for (int num2 = 0; num2 < array3.Length; num2++)
					{
						array3[num2].Dispose();
					}
				}
				SaveState();
			}
			catch (Exception ex)
			{
				AddError("扫描：" + ex.Message);
			}
			finally
			{
				PublishStatus();
				if (_running && !_stopRequested && !_disposed)
				{
					_timer.Change(1000, -1);
				}
			}
		}
		Notify();
	}

	private void ApplyToProcess(Process process, uint[] gameIds, uint[] backgroundIds)
	{
		int id = process.Id;
        Diagnostic?.Invoke("pid=" + id + " rules=" + string.Join(";", _settings.GameProcesses));
		string name = $"PID {id}";
		try
		{
			if (id <= 4 || id == Environment.ProcessId || _blockedPids.Contains(id))
			{
				return;
			}
			name = process.ProcessName + ".exe";
            Diagnostic?.Invoke("name=" + name);
            if (IsCriticalName(name)) return;
			bool num = _settings.GameProcesses.Any((string s) => NameMatches(s, name));
			bool flag = (_settings.BackgroundIsolation || _settings.BackgroundCoreOrderEnabled) && _settings.BackgroundProcesses.Any((string s) => NameMatches(s, name));
			if (!num && !flag)
			{
				return;
			}
			using SafeProcessHandle safeProcessHandle = OpenProcess(4096u, id);
			string path = _readPath(safeProcessHandle);
            Diagnostic?.Invoke("path=" + path);
			long num2 = StartTicks(safeProcessHandle);
			string text = id + ":" + num2;
			if (_settings.ExcludedProcesses.Any((string s) => MatchesSelector(s, path, name)))
			{
				return;
			}
			bool flag2 = _settings.GameProcesses.Any((string s) => MatchesSelector(s, path, name));
			bool flag3 = (_settings.BackgroundIsolation || _settings.BackgroundCoreOrderEnabled) && _settings.BackgroundProcesses.Any((string s) => MatchesSelector(s, path, name));
			if (!flag2 && !flag3)
			{
				return;
			}
			uint[] array = (flag2 ? gameIds : backgroundIds);
            bool useOrder = flag2 ? _settings.GameCoreOrderEnabled : _settings.BackgroundCoreOrderEnabled;
            bool applyProcessSettings = _settings.Enforce || !_saved.ContainsKey(text);
			if ((!useOrder && (array.Length == 0 || !applyProcessSettings)) || _stopRequested)
			{
				return;
			}
			using SafeProcessHandle handle = OpenProcess(5632u, id);
            Diagnostic?.Invoke("same=" + (StartTicks(handle) == num2) + "; pathSame=" + ImagePath(handle).Equals(path, StringComparison.OrdinalIgnoreCase));
			if (StartTicks(handle) == num2 && ImagePath(handle).Equals(path, StringComparison.OrdinalIgnoreCase))
			{
				uint[] processCpuSets = GetProcessCpuSets(handle);
				if (!GetProcessAffinityMask(handle, out var processMask, out var _))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
                if (!_saved.ContainsKey(text))
                {
                    uint priority = GetPriorityClass(handle);
                    if (priority == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var value = new RestoreState(id, num2, path, name, processCpuSets.ToList(),
                        ((UIntPtr)processMask).ToUInt64().ToString("X"), priority,
                        !useOrder && _settings.StrictAffinity, !useOrder,
                        flag2 && _settings.GamePriority != 0, new());
                    _saved[text] = value;
                    try { SaveState(); } catch { _saved.Remove(text); throw; }
                }
                if (!useOrder) SetCpuSets(handle, array);
                if (!useOrder && _settings.StrictAffinity)
                {
                    var selected = _topology!.CpuSets.Where(c => array.Contains(c.Id)).ToArray();
                    ulong mask = SelectionMask(selected);
                    if (!SetProcessAffinityMask(handle, new UIntPtr(mask))) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (applyProcessSettings && flag2 && _settings.GamePriority != 0 && !SetPriorityClass(handle, (uint)_settings.GamePriority))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (useOrder) ApplyCoreOrder(process, text, flag2 ? _settings.GameCoreOrder : _settings.BackgroundCoreOrder, processCpuSets);

				_activeBindings.Add(text);
			}
		}
		catch (Exception ex)
		{
			AddError($"{name} (PID {id})：{ex.Message}；本次调度已跳过，不再重试");
			_blockedPids.Add(id);
		}
	}

	private static bool NameMatches(string selector, string name)
	{
		return Path.GetFileNameWithoutExtension(selector.Trim()).Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
	}

	internal static bool MatchesSelector(string selector, string path, string name)
	{
		selector = selector.Trim();
		if (selector.Length == 0)
		{
			return false;
		}
		if (!selector.Contains(Path.DirectorySeparatorChar) && !selector.Contains(Path.AltDirectorySeparatorChar))
		{
			return NameMatches(selector, name);
		}
		return path.Equals(selector, StringComparison.OrdinalIgnoreCase);
	}

	private int ResolveGameCcd(string value)
	{
		if (_topology == null || value.Equals("All", StringComparison.OrdinalIgnoreCase) || value == "全核心")
		{
			return -1;
		}
		if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
		{
			if (_topology.Ccds.Count <= 1)
			{
				return -1;
			}
			return 0;
		}
		if (!int.TryParse(value, out var result) || result < 0 || result >= _topology.Ccds.Count)
		{
			return -1;
		}
		return result;
	}

	private void CleanupExited(HashSet<int> live)
	{
        foreach (var key in _threadSamples.Keys.Where(k => !live.Contains(k.ProcessId)).ToArray()) _threadSamples.Remove(key);
        _blockedThreads.RemoveWhere(k => !live.Contains(k.ProcessId));
		KeyValuePair<string, RestoreState>[] array = _saved.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<string, RestoreState> keyValuePair = array[i];
			if (!live.Contains(keyValuePair.Value.ProcessId))
			{
				_saved.Remove(keyValuePair.Key);
				continue;
			}
			try
			{
				using SafeProcessHandle handle = OpenProcess(4096u, keyValuePair.Value.ProcessId);
				if (StartTicks(handle) != keyValuePair.Value.StartTimeUtcTicks)
				{
					_saved.Remove(keyValuePair.Key);
				}
			}
			catch
			{
			}
		}
	}

	private void RestoreAll()
	{
		Process[] processes = Process.GetProcesses();
		HashSet<int> hashSet;
		try
		{
			hashSet = processes.Select((Process p) => p.Id).ToHashSet();
		}
		finally
		{
			Process[] array = processes;
			for (int num = 0; num < array.Length; num++)
			{
				array[num].Dispose();
			}
		}
		KeyValuePair<string, RestoreState>[] array2 = _saved.ToArray();
		for (int num = 0; num < array2.Length; num++)
		{
			KeyValuePair<string, RestoreState> keyValuePair = array2[num];
			RestoreState value = keyValuePair.Value;
			if (!hashSet.Contains(value.ProcessId))
			{
				_saved.Remove(keyValuePair.Key);
				continue;
			}
			try
			{
				using SafeProcessHandle handle = OpenProcess(5632u, value.ProcessId);
				if (StartTicks(handle) != value.StartTimeUtcTicks || !ImagePath(handle).Equals(value.ExecutablePath, StringComparison.OrdinalIgnoreCase))
				{
					_saved.Remove(keyValuePair.Key);
					continue;
				}
				if (value.CpuSetsChanged) SetCpuSets(handle, value.OriginalCpuSetIds.ToArray());
				if (value.AffinityChanged && ulong.TryParse(value.OriginalAffinityMaskHex, NumberStyles.HexNumber, null, out var result) && result != 0L && !SetProcessAffinityMask(handle, new UIntPtr(result)))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
                if (value.PriorityChanged && value.OriginalPriority != 0 && !SetPriorityClass(handle, value.OriginalPriority))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                RestoreCoreOrder(value);
				_saved.Remove(keyValuePair.Key);
			}
			catch (Exception ex)
			{
				AddError($"恢复 {value.ProcessName} ({value.ProcessId})：{ex.Message}");
			}
		}
		try
		{
			SaveState();
		}
		catch (Exception ex2)
		{
			AddError("保存恢复记录：" + ex2.Message);
		}
	}

	private void AddError(string message)
	{
		if (!_errors.Contains<string>(message, StringComparer.Ordinal))
		{
			if (_errors.Count >= 20)
			{
				_errors.RemoveAt(0);
			}
			_errors.Add(message);
		}
	}

	private void PublishStatus()
	{
		SchedulerStatus schedulerStatus = new SchedulerStatus
		{
			Running = _running,
			Message = _message,
			PendingRestoreCount = _saved.Count,
			GameCount = (_running ? _saved.Where<KeyValuePair<string, RestoreState>>((KeyValuePair<string, RestoreState> p) => _activeBindings.Contains(p.Key)).Count((KeyValuePair<string, RestoreState> p) => _settings.GameProcesses.Any((string s) => MatchesSelector(s, p.Value.ExecutablePath, p.Value.ProcessName))) : 0),
			BackgroundCount = (_running ? _saved.Where<KeyValuePair<string, RestoreState>>((KeyValuePair<string, RestoreState> p) => _activeBindings.Contains(p.Key)).Count((KeyValuePair<string, RestoreState> p) => !_settings.GameProcesses.Any((string s) => MatchesSelector(s, p.Value.ExecutablePath, p.Value.ProcessName)) && _settings.BackgroundProcesses.Any((string s) => MatchesSelector(s, p.Value.ExecutablePath, p.Value.ProcessName))) : 0)
		};
		schedulerStatus.Errors.AddRange(_errors);
		Volatile.Write(ref _status, schedulerStatus);
	}

	private void Notify()
	{
		try
		{
			this.Changed?.Invoke(this, EventArgs.Empty);
		}
		catch
		{
		}
	}

	private void LoadState()
	{
		string path = Path.Combine(_storeDir, "restore.json");
		if (!File.Exists(path))
		{
			return;
		}
		foreach (RestoreState item in JsonSerializer.Deserialize<List<RestoreState>>(File.ReadAllText(path)) ?? new List<RestoreState>())
		{
			_saved[item.ProcessId + ":" + item.StartTimeUtcTicks] = item;
		}
	}

	private void SaveState()
	{
		string text = JsonSerializer.Serialize(_saved.Values.ToArray(), new JsonSerializerOptions
		{
			WriteIndented = true
		});
		if (!(text == _lastSavedState))
		{
			WriteAtomic("restore.json", text);
			_lastSavedState = text;
		}
	}

	private void WriteAtomic(string name, string contents)
	{
		Directory.CreateDirectory(_storeDir);
		string text = Path.Combine(_storeDir, name);
		string text2 = text + ".new";
		File.WriteAllText(text2, contents);
		File.Move(text2, text, overwrite: true);
	}

	private static SafeProcessHandle OpenProcess(uint access, int pid)
	{
		SafeProcessHandle safeProcessHandle = new SafeProcessHandle(NativeOpenProcess(access, inherit: false, pid));
		if (!safeProcessHandle.IsInvalid)
		{
			return safeProcessHandle;
		}
		int lastWin32Error = Marshal.GetLastWin32Error();
		safeProcessHandle.Dispose();
		throw new Win32Exception(lastWin32Error);
	}

	private static string ImagePath(SafeProcessHandle handle)
	{
		StringBuilder stringBuilder = new StringBuilder(32768);
		uint length = (uint)stringBuilder.Capacity;
		if (!QueryFullProcessImageName(handle, 0u, stringBuilder, ref length))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
		return stringBuilder.ToString();
	}

	private static long StartTicks(SafeProcessHandle handle)
	{
		if (!GetProcessTimes(handle, out var creation, out var _, out var _, out var _))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
		return DateTime.FromFileTimeUtc(creation).Ticks;
	}

	private static uint[] GetProcessCpuSets(SafeProcessHandle handle)
	{
		uint returned;
		bool processDefaultCpuSets = GetProcessDefaultCpuSets(handle, null, 0u, out returned);
		int lastWin32Error = Marshal.GetLastWin32Error();
		if (!processDefaultCpuSets && lastWin32Error != 122)
		{
			throw new Win32Exception(lastWin32Error);
		}
		if (returned == 0)
		{
			return Array.Empty<uint>();
		}
		uint[] array = new uint[returned];
		if (!GetProcessDefaultCpuSets(handle, array, returned, out var _))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
		return array;
	}

	private static void SetCpuSets(SafeProcessHandle handle, uint[] ids)
	{
		if (!SetProcessDefaultCpuSets(handle, (ids.Length == 0) ? null : ids, (uint)ids.Length))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}

	public void Dispose()
	{
		_stopRequested = true;
		lock (_gate)
		{
			if (!_disposed)
			{
				_running = false;
				_timer.Change(-1, -1);
				if (_saved.Count > 0)
				{
					RestoreAll();
				}
				_disposed = true;
				_timer.Dispose();
				PublishStatus();
			}
		}
	}

    internal static bool IsCriticalName(string name) => new[] { "system.exe", "registry.exe", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe", "winlogon.exe", "svchost.exe", "dwm.exe", "audiodg.exe", "secure system.exe" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static ulong SelectionMask(IEnumerable<CpuSetDescriptor> selected)
    {
        var list = selected.ToArray();
        if (list.Length == 0 || list.Select(c => c.Group).Distinct().Count() != 1 || list.Any(c => c.LogicalProcessorIndex >= 64))
            throw new InvalidOperationException("硬亲和性必须选择同一处理器组的有效 CPU");
        return list.Aggregate(0UL, (mask, c) => mask | (1UL << c.LogicalProcessorIndex));
    }

    internal static void ValidateSettings(SchedulerSettings s, CpuSetTopology topology)
    {
        ValidateCoreOrder(s.GameCoreOrderEnabled, s.GameCoreOrder, topology, "游戏");
        ValidateCoreOrder(s.BackgroundCoreOrderEnabled, s.BackgroundCoreOrder, topology, "非游戏");
        if (s.CustomCpuSetIds.Count != s.CustomCpuSetIds.Distinct().Count() || s.CustomCpuSetIds.Any(id => !topology.CpuSets.Any(c => c.Id == id)))
            throw new InvalidOperationException("所选 CPU Set 已失效，请重新选择");
        if (!new[] { 0, 32, 32768, 128 }.Contains(s.GamePriority)) throw new InvalidOperationException("不支持此优先级");
        if (s.StrictAffinity && topology.CpuSets.Select(c => c.Group).Distinct().Count() != 1)
            throw new InvalidOperationException("多处理器组机器请使用 CPU Sets；硬亲和性已阻止");
    }

    private static void ValidateCoreOrder(bool enabled, List<uint> order, CpuSetTopology topology, string label)
    {
        if (!enabled) return;
        if (order.Count == 0 || order.Count != order.Distinct().Count() || order.Any(id => !topology.CpuSets.Any(c => c.Id == id)))
            throw new InvalidOperationException(label + "核心顺序为空或与当前 CPU 不符，请重新排序");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(SafeProcessHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(SafeProcessHandle handle, uint priority);

	[DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
	private static extern nint NativeOpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref uint length);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetProcessAffinityMask(SafeProcessHandle handle, out nuint processMask, out nuint systemMask);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetProcessAffinityMask(SafeProcessHandle handle, nuint mask);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetProcessDefaultCpuSets(SafeProcessHandle process, uint[]? ids, uint count);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetProcessDefaultCpuSets(SafeProcessHandle process, uint[]? ids, uint count, out uint returned);
}
