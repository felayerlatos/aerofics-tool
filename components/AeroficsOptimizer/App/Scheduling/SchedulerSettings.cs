using System.Collections.Generic;

namespace AeroficsLauncher;

public sealed class SchedulerSettings
{
	public bool GameCoreOrderEnabled { get; set; }
	public bool BackgroundCoreOrderEnabled { get; set; }
	public List<uint> GameCoreOrder { get; set; } = new();
	public List<uint> BackgroundCoreOrder { get; set; } = new();

	public List<uint> CustomCpuSetIds { get; set; } = new();

    public bool StrictAffinity { get; set; }

    public int GamePriority { get; set; }

    public bool Enforce { get; set; }
    public bool MinimizeToTray { get; set; }

	public string GameCcd { get; set; } = "Auto";

	public bool BackgroundIsolation { get; set; } = true;

	public List<string> ExcludedProcesses { get; set; } = new List<string>();

	public List<string> GameProcesses { get; set; } = new List<string>();

	public List<string> BackgroundProcesses { get; set; } = new List<string>();
}
