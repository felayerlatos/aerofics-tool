using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed class StartupItemRowViewModel
{
	public string Id { get; }

	public string Name { get; }

	public string Command { get; }

	public bool IsEnabled { get; }

	public string Location { get; }

	public bool HasRecovery { get; }

	public string SourceLabel { get; }

	public string State
	{
		get
		{
			if (!IsEnabled)
			{
				return "已停用";
			}
			return "已启用";
		}
	}

	public string ActionLabel
	{
		get
		{
			if (!IsEnabled)
			{
				return "启用";
			}
			return "停用";
		}
	}

	public StartupItemRowViewModel(StartupItemDescriptor model, bool hasRecovery)
	{
		Id = model.Id;
		Name = model.Name;
		Command = model.Command;
		IsEnabled = model.IsEnabled;
		Location = model.Location;
		HasRecovery = hasRecovery;
		SourceLabel = model.Location switch
		{
			"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run" => "当前用户", 
			"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run (64 位)" => "所有用户 · 64 位", 
			"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run (32 位)" => "所有用户 · 32 位", 
			_ => "注册表启动项", 
		};
	}
}
