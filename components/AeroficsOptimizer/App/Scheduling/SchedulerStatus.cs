using System.Collections.Generic;

namespace AeroficsLauncher;

public sealed class SchedulerStatus
{
	public bool Running { get; internal set; }

	public string Message { get; internal set; } = "未启动";

	public int GameCount { get; internal set; }

	public int BackgroundCount { get; internal set; }

	public int PendingRestoreCount { get; internal set; }

	public List<string> Errors { get; } = new List<string>();
}
