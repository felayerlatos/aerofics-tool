using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SystemOptimizer.App.Configuration;
using SystemOptimizer.Core.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed class ServiceGroupViewModel : INotifyPropertyChanged
{
	private bool _isBusy;

	private bool _isExpanded;

	internal ServiceGroupDefinition Definition { get; }

	public IReadOnlyList<SystemServiceRowViewModel> Services { get; }

	public string Title => Definition.Title;

	public string Description => Definition.Description;

	public string DisabledImpact => Definition.DisabledImpact;

	public string RiskLabel => Definition.RiskLabel;

	public int DisabledCount { get; }

	public int RecoveryCount { get; }

	public bool CanDisable
	{
		get
		{
			if (!_isBusy && Services.Count > 0)
			{
				return DisabledCount < Services.Count;
			}
			return false;
		}
	}

	public bool CanRestore
	{
		get
		{
			if (!_isBusy)
			{
				return RecoveryCount > 0;
			}
			return false;
		}
	}

	public bool IsFullyDisabled
	{
		get
		{
			if (Services.Count > 0)
			{
				return DisabledCount == Services.Count;
			}
			return false;
		}
	}

	public bool IsPartiallyDisabled
	{
		get
		{
			if (DisabledCount > 0)
			{
				return DisabledCount < Services.Count;
			}
			return false;
		}
	}

	public string StateLabel
	{
		get
		{
			if (Services.Count == 0)
			{
				return "本机无匹配服务";
			}
			if (DisabledCount == 0)
			{
				return "已启用";
			}
			if (DisabledCount == Services.Count)
			{
				return "已禁用";
			}
			return "部分禁用";
		}
	}

	public string CountLabel
	{
		get
		{
			if (Services.Count != 0)
			{
				return $"已检测 {Services.Count} 项 · 已禁用 {DisabledCount} 项" + ((RecoveryCount > 0) ? $" · 可恢复 {RecoveryCount} 项" : string.Empty);
			}
			return "未检测到可配置服务";
		}
	}

	public string MatchedServicesLabel
	{
		get
		{
			if (Services.Count != 0)
			{
				return string.Join("、", from service in Services.Take(8)
					select service.DisplayName) + ((Services.Count > 8) ? $" 等 {Services.Count} 项" : string.Empty);
			}
			return "当前 Windows 版本或硬件未安装本组服务。";
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (_isBusy != value)
			{
				_isBusy = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsBusy"));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CanDisable"));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CanRestore"));
			}
		}
	}

	public bool IsExpanded
	{
		get
		{
			return _isExpanded;
		}
		set
		{
			if (_isExpanded != value)
			{
				_isExpanded = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	internal ServiceGroupViewModel(ServiceGroupDefinition definition, IReadOnlyList<SystemServiceRowViewModel> services, int recoveryCount)
	{
		Definition = definition;
		Services = services;
		RecoveryCount = recoveryCount;
		DisabledCount = Enumerable.Count(services, (SystemServiceRowViewModel service) => service.StartupType == SystemServiceStartupType.Disabled);
	}
}
