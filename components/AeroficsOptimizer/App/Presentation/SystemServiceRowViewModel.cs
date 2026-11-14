using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed class SystemServiceRowViewModel
{
	public string Name { get; }

	public string DisplayName { get; }

	public string State { get; }

	public SystemServiceStartupType StartupType { get; }

	public string StartupTypeLabel { get; }

	public SystemServiceRowViewModel(SystemServiceDescriptor model)
	{
		Name = model.Name;
		DisplayName = model.DisplayName;
		State = model.State switch
		{
			SystemServiceState.Stopped => "已停止", 
			SystemServiceState.StartPending => "正在启动", 
			SystemServiceState.StopPending => "正在停止", 
			SystemServiceState.Running => "正在运行", 
			SystemServiceState.ContinuePending => "正在继续", 
			SystemServiceState.PausePending => "正在暂停", 
			SystemServiceState.Paused => "已暂停", 
			_ => "状态未知", 
		};
		StartupType = model.StartupType;
		StartupTypeLabel = model.StartupType switch
		{
			SystemServiceStartupType.Automatic => "自动", 
			SystemServiceStartupType.Manual => "手动", 
			SystemServiceStartupType.Disabled => "禁用", 
			SystemServiceStartupType.Boot => "启动", 
			SystemServiceStartupType.System => "系统", 
			_ => "未知", 
		};
	}
}
