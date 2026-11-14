using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SystemOptimizer.App.Configuration;
using SystemOptimizer.App.Services;
using SystemOptimizer.Core.Operations;
using SystemOptimizer.Core.SystemTools;
using SystemOptimizer.Infrastructure.SystemTools;
using SystemOptimizer.Windows.SystemTools;
using Microsoft.Win32;

namespace SystemOptimizer.App.Presentation;

internal sealed class SystemToolsViewModel : INotifyPropertyChanged
{
	private readonly IPowerPlanService _powerPlans;

	private readonly IStartupItemService _startupItems;

	private readonly ISystemServiceConfigurationService _services;

	private readonly IVirtualizationSecurityInspector _virtualizationSecurity;

	private readonly IVirtualizationSecurityChangeExecutor _virtualizationSecurityExecutor;

	private readonly SystemToolsRecoveryStore _recoveryStore;

	private readonly WindowsNvidiaProfilePresetService _nvidiaProfilePresets = new WindowsNvidiaProfilePresetService();

	private readonly WindowsNvidiaDynamicPStateService _nvidiaPowerState = new WindowsNvidiaDynamicPStateService();

	private readonly WindowsNvidiaProfilePresetService _legacyNvidiaPowerState = WindowsNvidiaProfilePresetService.CreatePowerStatePreferenceService();

	private readonly WindowsWin32PrioritySeparationService _win32Priority = new WindowsWin32PrioritySeparationService();

	private readonly WindowsSystemOptimizationService _systemOptimizations = new WindowsSystemOptimizationService();

	private readonly Dictionary<string, StartupItemRecoveryEntry> _startupSnapshots = new Dictionary<string, StartupItemRecoveryEntry>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, SystemServiceRecoveryEntry> _serviceSnapshots = new Dictionary<string, SystemServiceRecoveryEntry>(StringComparer.OrdinalIgnoreCase);

	private SystemToolsRecoveryDocument _recoveryDocument = SystemToolsRecoveryDocument.Empty;

	private PowerPlanRecoveryEntry? _powerPlanRecovery;

	private string? _recoveryStoreError;

	private PowerPlanRowViewModel? _selectedPowerPlan;

	private StartupItemRowViewModel? _selectedStartupItem;

	private SystemServiceRowViewModel? _selectedService;

	private ServiceStartupTypeOption? _selectedServiceStartupType;

	private string _powerPlanStatus = "尚未读取电源计划";

	private string _powerPlanImportStatus = "正在查找本机 .pow 文件";

	private string _advancedPowerOptionsStatus = "可解锁 Windows 隐藏的高级电源选项";

	private PowerPlanImportRowViewModel? _selectedPowerPlanImport;

	private string _startupItemsStatus = "打开页面后自动读取启动项";

	private string _servicesStatus = "打开页面后自动读取 Windows 服务";

	private string _vbsStatus = "尚未检测 VBS 与内存完整性";

	private string _systemOptimizationStatus = "正在读取可用优化项";

	private NvidiaProfilePresetOption? _selectedNvidiaProfilePreset;

	private string _nvidiaProfilePresetStatus = "选择预设后可预览修改规模；应用时会保存首次操作前的恢复基线。";

	private string _nvidiaPowerStateStatus = "尚未检测 NVIDIA 动态 P-State 状态。";

	private Win32PriorityPresetOption? _selectedWin32PriorityPreset;

	private string _win32PriorityCustomValue = "38";

	private string _win32PriorityStatus = "尚未读取 Win32 前台程序调度值。";

	public ObservableCollection<PowerPlanRowViewModel> PowerPlans { get; } = new ObservableCollection<PowerPlanRowViewModel>();

	public ObservableCollection<PowerPlanImportRowViewModel> PowerPlanImports { get; } = new ObservableCollection<PowerPlanImportRowViewModel>();

	public ObservableCollection<StartupItemRowViewModel> StartupItems { get; } = new ObservableCollection<StartupItemRowViewModel>();

	public ObservableCollection<SystemServiceRowViewModel> Services { get; } = new ObservableCollection<SystemServiceRowViewModel>();

	public ObservableCollection<ServiceGroupViewModel> ServiceGroups { get; } = new ObservableCollection<ServiceGroupViewModel>();

	public ObservableCollection<SystemOptimizationGroupViewModel> OptimizationGroups { get; } = new ObservableCollection<SystemOptimizationGroupViewModel>();

	public IReadOnlyList<ServiceStartupTypeOption> ServiceStartupTypeOptions { get; }

	public IReadOnlyList<NvidiaProfilePresetOption> NvidiaProfilePresets { get; }

	public IReadOnlyList<Win32PriorityPresetOption> Win32PriorityPresets { get; }

	public PowerPlanRowViewModel? SelectedPowerPlan
	{
		get
		{
			return _selectedPowerPlan;
		}
		set
		{
			SetField(ref _selectedPowerPlan, value, "SelectedPowerPlan");
		}
	}

	public PowerPlanImportRowViewModel? SelectedPowerPlanImport
	{
		get
		{
			return _selectedPowerPlanImport;
		}
		set
		{
			SetField(ref _selectedPowerPlanImport, value, "SelectedPowerPlanImport");
		}
	}

	public StartupItemRowViewModel? SelectedStartupItem
	{
		get
		{
			return _selectedStartupItem;
		}
		set
		{
			SetField(ref _selectedStartupItem, value, "SelectedStartupItem");
		}
	}

	public SystemServiceRowViewModel? SelectedService
	{
		get
		{
			return _selectedService;
		}
		set
		{
			if (SetField(ref _selectedService, value, "SelectedService") && value != null)
			{
				SelectedServiceStartupType = ServiceStartupTypeOptions.FirstOrDefault((ServiceStartupTypeOption option) => option.Value == value.StartupType) ?? ServiceStartupTypeOptions[1];
			}
		}
	}

	public ServiceStartupTypeOption? SelectedServiceStartupType
	{
		get
		{
			return _selectedServiceStartupType;
		}
		set
		{
			SetField(ref _selectedServiceStartupType, value, "SelectedServiceStartupType");
		}
	}

	public NvidiaProfilePresetOption? SelectedNvidiaProfilePreset
	{
		get
		{
			return _selectedNvidiaProfilePreset;
		}
		set
		{
			SetField(ref _selectedNvidiaProfilePreset, value, "SelectedNvidiaProfilePreset");
		}
	}

	public string NvidiaProfilePresetStatus
	{
		get
		{
			return _nvidiaProfilePresetStatus;
		}
		private set
		{
			SetField(ref _nvidiaProfilePresetStatus, value, "NvidiaProfilePresetStatus");
		}
	}

	public bool HasNvidiaProfilePresetBackup => _nvidiaProfilePresets.HasBackup;

	public string NvidiaPowerStateStatus
	{
		get
		{
			return _nvidiaPowerStateStatus;
		}
		private set
		{
			SetField(ref _nvidiaPowerStateStatus, value, "NvidiaPowerStateStatus");
		}
	}

	public bool HasNvidiaPowerStateBackup
	{
		get
		{
			if (!_nvidiaPowerState.HasBackup)
			{
				return _legacyNvidiaPowerState.HasBackup;
			}
			return true;
		}
	}

	public Win32PriorityPresetOption? SelectedWin32PriorityPreset
	{
		get
		{
			return _selectedWin32PriorityPreset;
		}
		set
		{
			if (SetField(ref _selectedWin32PriorityPreset, value, "SelectedWin32PriorityPreset"))
			{
				OnPropertyChanged("IsCustomWin32PriorityValue");
			}
		}
	}

	public bool IsCustomWin32PriorityValue => SelectedWin32PriorityPreset?.IsCustom ?? false;

	public string Win32PriorityCustomValue
	{
		get
		{
			return _win32PriorityCustomValue;
		}
		set
		{
			SetField(ref _win32PriorityCustomValue, value, "Win32PriorityCustomValue");
		}
	}

	public string Win32PriorityStatus
	{
		get
		{
			return _win32PriorityStatus;
		}
		private set
		{
			SetField(ref _win32PriorityStatus, value, "Win32PriorityStatus");
		}
	}

	public bool HasWin32PriorityBackup => _win32Priority.HasBackup;

	public string PowerPlanStatus
	{
		get
		{
			return _powerPlanStatus;
		}
		private set
		{
			SetField(ref _powerPlanStatus, value, "PowerPlanStatus");
		}
	}

	public string PowerPlanImportStatus
	{
		get
		{
			return _powerPlanImportStatus;
		}
		private set
		{
			SetField(ref _powerPlanImportStatus, value, "PowerPlanImportStatus");
		}
	}

	public string AdvancedPowerOptionsStatus
	{
		get
		{
			return _advancedPowerOptionsStatus;
		}
		private set
		{
			SetField(ref _advancedPowerOptionsStatus, value, "AdvancedPowerOptionsStatus");
		}
	}

	public string StartupItemsStatus
	{
		get
		{
			return _startupItemsStatus;
		}
		private set
		{
			SetField(ref _startupItemsStatus, value, "StartupItemsStatus");
		}
	}

	public string ServicesStatus
	{
		get
		{
			return _servicesStatus;
		}
		private set
		{
			SetField(ref _servicesStatus, value, "ServicesStatus");
		}
	}

	public string VbsStatus
	{
		get
		{
			return _vbsStatus;
		}
		private set
		{
			SetField(ref _vbsStatus, value, "VbsStatus");
		}
	}

	public string SystemOptimizationStatus
	{
		get
		{
			return _systemOptimizationStatus;
		}
		private set
		{
			SetField(ref _systemOptimizationStatus, value, "SystemOptimizationStatus");
		}
	}

	public ICommand RefreshSystemToolsCommand { get; }

	public ICommand RefreshPowerPlansCommand { get; }

	public ICommand ApplyPowerPlanCommand { get; }

	public ICommand DeletePowerPlanCommand { get; }

	public ICommand RestorePowerPlanCommand { get; }

	public ICommand ActivateHighPerformanceCommand { get; }

	public ICommand ActivateUltimatePerformanceCommand { get; }

	public ICommand RefreshPowerPlanImportsCommand { get; }

	public ICommand ImportSelectedPowerPlanCommand { get; }

	public ICommand BrowsePowerPlanCommand { get; }

	public ICommand UnlockAdvancedPowerOptionsCommand { get; }

	public ICommand RestoreAdvancedPowerOptionsCommand { get; }

	public ICommand RefreshStartupItemsCommand { get; }

	public ICommand ToggleStartupItemCommand { get; }

	public ICommand RestoreStartupItemCommand { get; }

	public ICommand RefreshServicesCommand { get; }

	public ICommand ApplyServiceChangeCommand { get; }

	public ICommand RestoreServiceCommand { get; }

	public ICommand DisableServiceGroupCommand { get; }

	public ICommand RestoreServiceGroupCommand { get; }

	public ICommand DisableVbsCommand { get; }

	public ICommand RestoreVbsCommand { get; }

	public ICommand ApplyNvidiaProfilePresetCommand { get; }

	public ICommand RestoreNvidiaProfilePresetCommand { get; }

	public ICommand ApplyNvidiaPowerStateCommand { get; }

	public ICommand RestoreNvidiaPowerStateCommand { get; }

	public ICommand RefreshWin32PriorityCommand { get; }

	public ICommand ApplyWin32PriorityCommand { get; }

	public ICommand RestoreWin32PriorityCommand { get; }

	public ICommand RefreshOptimizationsCommand { get; }

	public ICommand ApplyOptimizationCommand { get; }

	public ICommand RestoreOptimizationCommand { get; }

	public ICommand OpenDeviceManagerCommand { get; }

	public ICommand OpenVisualEffectsCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public SystemToolsViewModel()
		: this(new WindowsPowerPlanService(), new WindowsStartupItemService(), new WindowsServiceConfigurationService(), new WindowsVirtualizationSecurityInspector(), CreateVirtualizationSecurityExecutor(), new SystemToolsRecoveryStore())
	{
	}

	internal SystemToolsViewModel(IPowerPlanService powerPlans, IStartupItemService startupItems, ISystemServiceConfigurationService services, IVirtualizationSecurityInspector virtualizationSecurity, IVirtualizationSecurityChangeExecutor virtualizationSecurityExecutor, SystemToolsRecoveryStore recoveryStore)
	{
		_powerPlans = powerPlans;
		_startupItems = startupItems;
		_services = services;
		_virtualizationSecurity = virtualizationSecurity;
		_virtualizationSecurityExecutor = virtualizationSecurityExecutor;
		_recoveryStore = recoveryStore ?? throw new ArgumentNullException("recoveryStore");
		RefreshSystemToolsCommand = new AsyncCommand(RefreshSummaryAsync);
		RefreshPowerPlansCommand = new AsyncCommand(RefreshPowerPlansAsync);
		ApplyPowerPlanCommand = new AsyncCommand(ApplyPowerPlanAsync, requiresAdministrator: true);
		DeletePowerPlanCommand = new AsyncCommand(DeletePowerPlanAsync, requiresAdministrator: true);
		RestorePowerPlanCommand = new AsyncCommand(RestorePowerPlanAsync, requiresAdministrator: true);
		ActivateHighPerformanceCommand = new AsyncCommand(() => ActivateBuiltInPowerPlanAsync(BuiltInPowerPlan.HighPerformance), requiresAdministrator: true);
		ActivateUltimatePerformanceCommand = new AsyncCommand(() => ActivateBuiltInPowerPlanAsync(BuiltInPowerPlan.UltimatePerformance), requiresAdministrator: true);
		RefreshPowerPlanImportsCommand = new AsyncCommand(RefreshPowerPlanImportsAsync);
		ImportSelectedPowerPlanCommand = new AsyncCommand(ImportSelectedPowerPlanAsync, requiresAdministrator: true);
		BrowsePowerPlanCommand = new AsyncCommand(BrowsePowerPlanAsync, requiresAdministrator: true);
		UnlockAdvancedPowerOptionsCommand = new AsyncCommand(UnlockAdvancedPowerOptionsAsync, requiresAdministrator: true);
		RestoreAdvancedPowerOptionsCommand = new AsyncCommand(RestoreAdvancedPowerOptionsAsync, requiresAdministrator: true);
		RefreshStartupItemsCommand = new AsyncCommand(RefreshStartupItemsAsync);
		ToggleStartupItemCommand = new AsyncCommand(ToggleStartupItemAsync, requiresAdministrator: true);
		RestoreStartupItemCommand = new AsyncCommand(RestoreStartupItemAsync, requiresAdministrator: true);
		RefreshServicesCommand = new AsyncCommand(RefreshServicesAsync);
		ApplyServiceChangeCommand = new AsyncCommand(ApplyServiceChangeAsync, requiresAdministrator: true);
		RestoreServiceCommand = new AsyncCommand(RestoreServiceAsync, requiresAdministrator: true);
		DisableServiceGroupCommand = new AsyncCommand(DisableServiceGroupAsync, requiresAdministrator: true);
		RestoreServiceGroupCommand = new AsyncCommand(RestoreServiceGroupAsync, requiresAdministrator: true);
		DisableVbsCommand = new AsyncCommand(CreateVbsDisablePlanAsync, requiresAdministrator: true);
		RestoreVbsCommand = new AsyncCommand(RestoreVbsAsync, requiresAdministrator: true);
		ApplyNvidiaProfilePresetCommand = new AsyncCommand(ApplyNvidiaProfilePresetAsync, requiresAdministrator: true);
		RestoreNvidiaProfilePresetCommand = new AsyncCommand(RestoreNvidiaProfilePresetAsync, requiresAdministrator: true);
		ApplyNvidiaPowerStateCommand = new AsyncCommand(ApplyNvidiaPowerStateAsync, requiresAdministrator: true);
		RestoreNvidiaPowerStateCommand = new AsyncCommand(RestoreNvidiaPowerStateAsync, requiresAdministrator: true);
		RefreshWin32PriorityCommand = new AsyncCommand(RefreshWin32PriorityAsync);
		ApplyWin32PriorityCommand = new AsyncCommand(ApplyWin32PriorityAsync, requiresAdministrator: true);
		RestoreWin32PriorityCommand = new AsyncCommand(RestoreWin32PriorityAsync, requiresAdministrator: true);
		RefreshOptimizationsCommand = new AsyncCommand(RefreshOptimizationsAsync);
		ApplyOptimizationCommand = new AsyncCommand(ApplyOptimizationAsync, requiresAdministrator: true);
		RestoreOptimizationCommand = new AsyncCommand(RestoreOptimizationAsync, requiresAdministrator: true);
		OpenDeviceManagerCommand = new AsyncCommand(OpenDeviceManagerAsync);
		OpenVisualEffectsCommand = new AsyncCommand(OpenVisualEffectsAsync);
		ServiceStartupTypeOptions = new _003C_003Ez__ReadOnlyArray<ServiceStartupTypeOption>(new ServiceStartupTypeOption[3]
		{
			new ServiceStartupTypeOption("自动", SystemServiceStartupType.Automatic),
			new ServiceStartupTypeOption("手动", SystemServiceStartupType.Manual),
			new ServiceStartupTypeOption("禁用", SystemServiceStartupType.Disabled)
		});
		_selectedServiceStartupType = ServiceStartupTypeOptions[1];
		NvidiaProfilePresets = NvidiaProfilePresetCatalog.Load();
		_selectedNvidiaProfilePreset = NvidiaProfilePresets.FirstOrDefault();
		Win32PriorityPresets = Win32PriorityPresetCatalog.All;
		_selectedWin32PriorityPreset = Win32PriorityPresets.FirstOrDefault();
		LoadRecoveryState();
	}

	public async Task RefreshSummaryAsync()
	{
		await RefreshPowerPlansAsync();
		await RefreshPowerPlanImportsAsync();
		await RefreshVirtualizationSecurityAsync();
		await RefreshNvidiaPowerStateAsync();
		await RefreshWin32PriorityAsync();
		await RefreshOptimizationsAsync();
	}

	private async Task RefreshOptimizationsAsync()
	{
		SystemOptimizationStatus = "正在读取系统优化状态…";
		try
		{
			IReadOnlyList<SystemOptimizationDescriptor> readOnlyList = await _systemOptimizations.EnumerateAsync();
			HashSet<string> selectedIds = (from item in OptimizationGroups.SelectMany((SystemOptimizationGroupViewModel @group) => @group.Items)
				where item.IsBusy
				select item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			OptimizationGroups.Clear();
			foreach (IGrouping<string, SystemOptimizationDescriptor> item in from item in readOnlyList
				group item by item.Group)
			{
				OptimizationGroups.Add(new SystemOptimizationGroupViewModel(item.Key, item.Select((SystemOptimizationDescriptor item) =>
				{
					SystemOptimizationRowViewModel systemOptimizationRowViewModel = new SystemOptimizationRowViewModel(item);
					systemOptimizationRowViewModel.IsBusy = selectedIds.Contains(systemOptimizationRowViewModel.Id);
					return systemOptimizationRowViewModel;
				}).ToArray()));
			}
			SystemOptimizationStatus = $"已读取 {readOnlyList.Count} 项系统设置";
		}
		catch (Exception ex)
		{
			SystemOptimizationStatus = "读取失败：" + ex.Message;
		}
	}

	private async Task ApplyOptimizationAsync(object? parameter)
	{
		if (parameter is SystemOptimizationRowViewModel { IsBusy: false } item && (!(item.RiskLabel == "高风险") || MessageBox.Show(item.Title + "\n\n" + item.Description + "\n\n该项目可能影响系统功能，程序会记录恢复基线。是否继续？", "确认高风险优化", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes) && (!(item.Id == "shader_cache") || MessageBox.Show("将删除当前用户的 DirectX、NVIDIA 和 AMD 着色器缓存。首次进入游戏可能短暂卡顿，且删除操作不可恢复。是否继续？", "清理着色器缓存", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes))
		{
			item.IsBusy = true;
			item.StateLabel = "正在应用…";
			OperationResult<SystemOptimizationOperationResult> operationResult = await _systemOptimizations.ApplyAsync(item.Id);
			string operationStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? operationResult.Value.Message : (operationResult.Error?.Message ?? "系统优化应用失败"));
			item.IsBusy = false;
			await RefreshOptimizationsAsync();
			SystemOptimizationStatus = operationStatus;
		}
	}

	private async Task RestoreOptimizationAsync(object? parameter)
	{
		if (parameter is SystemOptimizationRowViewModel { IsBusy: false } item)
		{
			item.IsBusy = true;
			item.StateLabel = "正在恢复…";
			OperationResult<SystemOptimizationOperationResult> operationResult = await _systemOptimizations.RestoreAsync(item.Id);
			string operationStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? operationResult.Value.Message : (operationResult.Error?.Message ?? "系统设置恢复失败"));
			item.IsBusy = false;
			await RefreshOptimizationsAsync();
			SystemOptimizationStatus = operationStatus;
		}
	}

	private Task OpenDeviceManagerAsync()
	{
		Process.Start(new ProcessStartInfo("devmgmt.msc")
		{
			UseShellExecute = true
		});
		SystemOptimizationStatus = "已打开设备管理器";
		return Task.CompletedTask;
	}

	private Task OpenVisualEffectsAsync()
	{
		Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe")
		{
			UseShellExecute = true
		});
		SystemOptimizationStatus = "已打开 Windows 性能选项";
		return Task.CompletedTask;
	}

	private async Task RefreshPowerPlansAsync()
	{
		PowerPlanStatus = "正在读取电源计划…";
		try
		{
			OperationResult<IReadOnlyList<PowerPlanDescriptor>> operationResult = await _powerPlans.EnumerateAsync();
			if (!operationResult.IsSuccess || operationResult.Value == null)
			{
				PowerPlanStatus = operationResult.Error?.Message ?? "无法读取电源计划";
				return;
			}
			PowerPlans.Clear();
			foreach (PowerPlanDescriptor item in operationResult.Value)
			{
				PowerPlans.Add(new PowerPlanRowViewModel(item));
			}
			SelectedPowerPlan = PowerPlans.FirstOrDefault((PowerPlanRowViewModel plan) => plan.IsActive) ?? PowerPlans.FirstOrDefault();
			SystemToolsViewModel systemToolsViewModel = this;
			string powerPlanStatus;
			if (SelectedPowerPlan == null)
			{
				powerPlanStatus = "没有检测到可用电源计划";
			}
			else if (_recoveryStoreError != null)
			{
				powerPlanStatus = "当前：" + SelectedPowerPlan.Name + "；" + _recoveryStoreError;
			}
			else
			{
				powerPlanStatus = (((object)_powerPlanRecovery != null) ? ("当前：" + SelectedPowerPlan.Name + "；保留了跨重启恢复记录") : ("当前：" + SelectedPowerPlan.Name));
			}
			systemToolsViewModel.PowerPlanStatus = powerPlanStatus;
		}
		catch (Exception ex)
		{
			PowerPlanStatus = "读取失败：" + ex.Message;
		}
	}

	private Task RefreshPowerPlanImportsAsync()
	{
		PowerPlanImportStatus = "正在查找桌面和下载目录中的 .pow 文件…";
		string previous = SelectedPowerPlanImport?.FilePath;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
		}.Where(Directory.Exists))
		{
			try
			{
				foreach (string item2 in Directory.EnumerateFiles(item, "*.pow", new EnumerationOptions
				{
					RecurseSubdirectories = true,
					IgnoreInaccessible = true,
					AttributesToSkip = FileAttributes.ReparsePoint
				}).Take(200))
				{
					hashSet.Add(item2);
				}
			}
			catch
			{
			}
		}
		PowerPlanImports.Clear();
		foreach (string item3 in hashSet.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
		{
			PowerPlanImports.Add(new PowerPlanImportRowViewModel(item3));
		}
		SelectedPowerPlanImport = PowerPlanImports.FirstOrDefault((PowerPlanImportRowViewModel item) => item.FilePath.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? PowerPlanImports.FirstOrDefault();
		PowerPlanImportStatus = ((PowerPlanImports.Count == 0) ? "没有自动找到 .pow 文件；可手动选择导入" : $"已发现 {PowerPlanImports.Count} 个本机电源计划文件");
		return Task.CompletedTask;
	}

	private async Task ImportSelectedPowerPlanAsync()
	{
		if (SelectedPowerPlanImport == null)
		{
			PowerPlanImportStatus = "请先选择一个 .pow 文件";
		}
		else
		{
			await ImportPowerPlanAsync(SelectedPowerPlanImport.FilePath);
		}
	}

	private async Task BrowsePowerPlanAsync()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选择 Windows 电源计划",
			Filter = "Windows 电源计划 (*.pow)|*.pow",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() == true)
		{
			await ImportPowerPlanAsync(openFileDialog.FileName);
			await RefreshPowerPlanImportsAsync();
		}
	}

	private async Task ImportPowerPlanAsync(string path)
	{
		if (MessageBox.Show("将导入并立即启用：\n" + Path.GetFileName(path) + "\n\n电源计划可能包含只适用于特定硬件的设置。是否继续？", "导入电源计划", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		PowerPlanRowViewModel powerPlanRowViewModel = PowerPlans.FirstOrDefault((PowerPlanRowViewModel plan) => plan.IsActive);
		bool flag = powerPlanRowViewModel == null;
		if (!flag)
		{
			flag = !(await StagePowerPlanMutationAsync(powerPlanRowViewModel.Id));
		}
		if (flag)
		{
			PowerPlanImportStatus = PowerPlanStatus;
			return;
		}
		PowerPlanImportStatus = "正在由 Windows 导入并回读验证…";
		OperationResult<PowerPlanDescriptor> operationResult = await _powerPlans.ImportAsync(path, activate: true);
		if (operationResult.IsSuccess && (object)operationResult.Value != null)
		{
			bool flag2 = CommitPowerPlanRecovery(operationResult.Value.Id);
			PowerPlanImportStatus = (flag2 ? ("已导入并启用：" + operationResult.Value.Name) : ("已导入并启用：" + operationResult.Value.Name + "；" + _recoveryStoreError));
		}
		else
		{
			PowerPlanImportStatus = (operationResult.Error?.Message ?? "电源计划导入失败") + "；恢复记录已保留";
		}
		await RefreshPowerPlansAsync();
	}

	private async Task UnlockAdvancedPowerOptionsAsync()
	{
		if (MessageBox.Show("将显示 Windows 电源选项中原本隐藏的高级项目。程序会保存原始 Attributes 值，可随时精确恢复。\n\n是否继续？", "解锁高级电源选项", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
		{
			OperationResult<int> operationResult = await _powerPlans.UnlockAdvancedOptionsAsync();
			AdvancedPowerOptionsStatus = (operationResult.IsSuccess ? $"已解锁 {operationResult.Value} 项隐藏设置；重新打开电源选项即可查看" : (operationResult.Error?.Message ?? "解锁失败"));
		}
	}

	private async Task RestoreAdvancedPowerOptionsAsync()
	{
		OperationResult<int> operationResult = await _powerPlans.RestoreAdvancedOptionsAsync();
		AdvancedPowerOptionsStatus = (operationResult.IsSuccess ? $"已按备份恢复 {operationResult.Value} 项高级设置显示状态" : (operationResult.Error?.Message ?? "恢复失败"));
	}

	private async Task ApplyPowerPlanAsync()
	{
		if (SelectedPowerPlan == null)
		{
			PowerPlanStatus = "请先选择一个电源计划";
			return;
		}
		PowerPlanRowViewModel current = PowerPlans.FirstOrDefault((PowerPlanRowViewModel plan) => plan.IsActive);
		if (current == null)
		{
			PowerPlanStatus = "无法确认当前活动电源计划";
		}
		else if (current.Id == SelectedPowerPlan.Id)
		{
			PowerPlanStatus = "当前已经是：" + SelectedPowerPlan.Name;
		}
		else if (await StagePowerPlanMutationAsync(current.Id))
		{
			OperationResult<PowerPlanDescriptor> operationResult = await _powerPlans.SetActiveAsync(SelectedPowerPlan.Id, current.Id);
			if (operationResult.IsSuccess && (object)operationResult.Value != null)
			{
				bool flag = CommitPowerPlanRecovery(operationResult.Value.Id);
				PowerPlanStatus = (flag ? ("已切换到：" + operationResult.Value.Name) : ("已切换到：" + operationResult.Value.Name + "；" + _recoveryStoreError));
			}
			else
			{
				PowerPlanStatus = (operationResult.Error?.Message ?? "电源计划切换失败") + "；恢复记录已保留";
			}
			await RefreshPowerPlansAsync();
		}
	}

	private async Task DeletePowerPlanAsync()
	{
		PowerPlanRowViewModel selected = SelectedPowerPlan;
		if (selected == null)
		{
			PowerPlanStatus = "请先选择一个电源计划";
		}
		else if (selected.IsActive)
		{
			PowerPlanStatus = "不能删除当前计划，请先切换到其他方案";
		}
		else if (MessageBox.Show("确定删除电源计划“" + selected.Name + "”吗？\n\n此操作不可撤销。", "删除电源计划", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			PowerPlanRowViewModel powerPlanRowViewModel = PowerPlans.FirstOrDefault((PowerPlanRowViewModel plan) => plan.IsActive);
			OperationResult<bool> result = await _powerPlans.DeleteAsync(selected.Id, powerPlanRowViewModel?.Id);
			await RefreshPowerPlansAsync();
			PowerPlanStatus = (result.IsSuccess ? ("已删除：" + selected.Name) : (result.Error?.Message ?? "电源计划删除失败"));
		}
	}

	private async Task RestorePowerPlanAsync()
	{
		if ((object)_powerPlanRecovery == null)
		{
			PowerPlanStatus = _recoveryStoreError ?? "没有可恢复的电源计划记录";
			return;
		}
		PowerPlanRowViewModel powerPlanRowViewModel = PowerPlans.FirstOrDefault((PowerPlanRowViewModel plan) => plan.IsActive);
		if (powerPlanRowViewModel == null)
		{
			PowerPlanStatus = "无法确认当前活动电源计划";
			return;
		}
		if (!_powerPlanRecovery.MutationPending && powerPlanRowViewModel.Id != _powerPlanRecovery.ManagedActivePlanId && powerPlanRowViewModel.Id != _powerPlanRecovery.Original.ActivePlanId)
		{
			PowerPlanStatus = "当前电源计划不是程序上次写入的状态，为避免覆盖外部修改，已取消恢复";
			return;
		}
		OperationResult<PowerPlanDescriptor> operationResult = await _powerPlans.RestoreAsync(_powerPlanRecovery.Original, powerPlanRowViewModel.Id);
		if (operationResult.IsSuccess && (object)operationResult.Value != null)
		{
			PowerPlanStatus = (RemovePowerPlanRecovery() ? ("已恢复：" + operationResult.Value.Name) : ("已恢复：" + operationResult.Value.Name + "；" + _recoveryStoreError));
		}
		else
		{
			PowerPlanStatus = operationResult.Error?.Message ?? "恢复失败";
		}
		await RefreshPowerPlansAsync();
	}

	private async Task ActivateBuiltInPowerPlanAsync(BuiltInPowerPlan plan)
	{
		PowerPlanRowViewModel powerPlanRowViewModel = PowerPlans.FirstOrDefault((PowerPlanRowViewModel item) => item.IsActive);
		if (powerPlanRowViewModel == null)
		{
			PowerPlanStatus = "无法确认当前活动电源计划";
		}
		else if (await StagePowerPlanMutationAsync(powerPlanRowViewModel.Id))
		{
			PowerPlanStatus = ((plan == BuiltInPowerPlan.HighPerformance) ? "正在启用高性能计划…" : "正在创建并启用卓越性能计划…");
			OperationResult<PowerPlanDescriptor> operationResult = await _powerPlans.ActivateBuiltInAsync(plan);
			if (operationResult.IsSuccess && (object)operationResult.Value != null)
			{
				bool flag = CommitPowerPlanRecovery(operationResult.Value.Id);
				PowerPlanStatus = (flag ? ("已启用：" + operationResult.Value.Name) : ("已启用：" + operationResult.Value.Name + "；" + _recoveryStoreError));
			}
			else
			{
				PowerPlanStatus = (operationResult.Error?.Message ?? "无法启用内置电源计划") + "；恢复记录已保留";
			}
			await RefreshPowerPlansAsync();
		}
	}

	internal async Task RefreshStartupItemsAsync()
	{
		StartupItemsStatus = "正在读取启动项…";
		OperationResult<IReadOnlyList<StartupItemDescriptor>> operationResult = await _startupItems.EnumerateAsync();
		if (!operationResult.IsSuccess || operationResult.Value == null)
		{
			StartupItemsStatus = operationResult.Error?.Message ?? "无法读取启动项";
			return;
		}
		StartupItems.Clear();
		foreach (StartupItemDescriptor item in operationResult.Value)
		{
			StartupItems.Add(new StartupItemRowViewModel(item, _startupSnapshots.ContainsKey(item.Id)));
		}
		SystemToolsViewModel systemToolsViewModel = this;
		string startupItemsStatus;
		if (_recoveryStoreError != null)
		{
			startupItemsStatus = $"{StartupItems.Count} 个启动项；{_recoveryStoreError}";
		}
		else
		{
			startupItemsStatus = ((_startupSnapshots.Count > 0) ? $"{StartupItems.Count} 个启动项；{_startupSnapshots.Count} 项保留了跨重启恢复记录" : $"{StartupItems.Count} 个启动项");
		}
		systemToolsViewModel.StartupItemsStatus = startupItemsStatus;
	}

	private async Task ToggleStartupItemAsync(object? parameter)
	{
		StartupItemRowViewModel item = (parameter as StartupItemRowViewModel) ?? SelectedStartupItem;
		if (item == null)
		{
			StartupItemsStatus = "请先选择一个启动项";
			return;
		}
		OperationResult<StartupItemSnapshot> operationResult = await _startupItems.CaptureAsync(item.Id);
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			StartupItemsStatus = operationResult.Error?.Message ?? "无法读取启动项当前状态";
			await RefreshStartupItemsAsync();
			return;
		}
		bool targetEnabled = !operationResult.Value.Item.IsEnabled;
		if (!StageStartupRecovery(operationResult.Value, targetEnabled))
		{
			StartupItemsStatus = _recoveryStoreError ?? "无法在修改启动项前保存恢复记录";
			return;
		}
		OperationResult<StartupItemDescriptor> operationResult2 = await _startupItems.SetEnabledAsync(operationResult.Value, targetEnabled);
		string operationStatus;
		if (operationResult2.IsSuccess)
		{
			StartupItemDescriptor? value = operationResult2.Value;
			if ((object)value != null && value.IsEnabled == targetEnabled)
			{
				operationStatus = (CommitStartupRecovery(item.Id, operationResult2.Value.IsEnabled) ? ("已" + (operationResult2.Value.IsEnabled ? "启用" : "停用") + "并完成注册表回读：" + operationResult2.Value.Name) : $"已{(operationResult2.Value.IsEnabled ? "启用" : "停用")}：{operationResult2.Value.Name}；{_recoveryStoreError}");
				goto IL_03a7;
			}
		}
		operationStatus = (operationResult2.Error?.Message ?? "启动项修改失败") + "；恢复记录已保留";
		goto IL_03a7;
		IL_03a7:
		await RefreshStartupItemsAsync();
		StartupItemsStatus = operationStatus;
	}

	private async Task RestoreStartupItemAsync(object? parameter)
	{
		StartupItemRowViewModel item = (parameter as StartupItemRowViewModel) ?? SelectedStartupItem;
		if (item == null || !_startupSnapshots.TryGetValue(item.Id, out StartupItemRecoveryEntry recovery))
		{
			StartupItemsStatus = _recoveryStoreError ?? "所选启动项没有可恢复的修改记录";
			return;
		}
		if (item.IsEnabled != recovery.ManagedIsEnabled && item.IsEnabled != recovery.Original.Item.IsEnabled)
		{
			StartupItemsStatus = "启动项状态不是程序记录的原始值或目标值，为避免覆盖外部修改，已取消恢复";
			return;
		}
		OperationResult<StartupItemDescriptor> operationResult = await _startupItems.RestoreAsync(recovery.Original);
		string operationStatus;
		if (!operationResult.IsSuccess || operationResult.Value?.IsEnabled != recovery.Original.Item.IsEnabled)
		{
			operationStatus = operationResult.Error?.Message ?? "恢复失败";
		}
		else
		{
			operationStatus = (RemoveStartupRecovery(item.Id) ? ("已恢复并完成注册表回读：" + operationResult.Value.Name) : ("已恢复：" + operationResult.Value.Name + "；" + _recoveryStoreError));
		}
		await RefreshStartupItemsAsync();
		StartupItemsStatus = operationStatus;
	}

	internal async Task RefreshServicesAsync()
	{
		ServicesStatus = "正在读取 Windows 服务…";
		OperationResult<IReadOnlyList<SystemServiceDescriptor>> operationResult = await _services.EnumerateAsync();
		if (!operationResult.IsSuccess || operationResult.Value == null)
		{
			ServicesStatus = operationResult.Error?.Message ?? "无法读取 Windows 服务";
			return;
		}
		Services.Clear();
		foreach (SystemServiceDescriptor item in operationResult.Value.Where((SystemServiceDescriptor item) => item.CanChangeStartupType).OrderBy((SystemServiceDescriptor item) => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
		{
			Services.Add(new SystemServiceRowViewModel(item));
		}
		RebuildServiceGroups();
		SystemToolsViewModel systemToolsViewModel = this;
		string servicesStatus;
		if (_recoveryStoreError != null)
		{
			servicesStatus = $"{Services.Count} 个可配置服务；{_recoveryStoreError}";
		}
		else
		{
			servicesStatus = ((_serviceSnapshots.Count > 0) ? $"{Services.Count} 个可配置服务；{_serviceSnapshots.Count} 项保留了跨重启恢复记录" : $"{Services.Count} 个可配置服务；请选择后再修改启动方式");
		}
		systemToolsViewModel.ServicesStatus = servicesStatus;
	}

	private async Task ApplyServiceChangeAsync()
	{
		if (SelectedService == null || (object)SelectedServiceStartupType == null)
		{
			ServicesStatus = "请先选择服务和启动方式";
		}
		else
		{
			if (MessageBox.Show($"将“{SelectedService.DisplayName}”的启动方式改为“{SelectedServiceStartupType.Label}”。\n\n" + "错误的服务配置可能影响系统功能，是否继续？", "确认服务设置", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			SystemServiceStartupType targetStartupType = SelectedServiceStartupType.Value;
			if (SelectedService.StartupType == targetStartupType)
			{
				ServicesStatus = "所选服务已经是该启动方式";
				return;
			}
			SystemServiceConfigurationSnapshot original;
			if (_serviceSnapshots.TryGetValue(SelectedService.Name, out SystemServiceRecoveryEntry value))
			{
				original = value.Original;
			}
			else
			{
				OperationResult<SystemServiceConfigurationSnapshot> operationResult = await _services.CaptureAsync(SelectedService.Name);
				if (!operationResult.IsSuccess || (object)operationResult.Value == null)
				{
					ServicesStatus = operationResult.Error?.Message ?? "无法创建服务配置快照";
					return;
				}
				original = operationResult.Value;
			}
			if (!StageServiceRecovery(original, targetStartupType))
			{
				ServicesStatus = _recoveryStoreError ?? "无法在修改服务前保存恢复记录";
				return;
			}
			OperationResult<SystemServiceDescriptor> operationResult2 = await _services.SetStartupTypeAsync(SelectedService.Name, targetStartupType, SelectedService.StartupType);
			string operationStatus;
			if (!operationResult2.IsSuccess || (object)operationResult2.Value == null)
			{
				operationStatus = (operationResult2.Error?.Message ?? "服务设置失败") + "；恢复记录已保留";
			}
			else
			{
				operationStatus = (CommitServiceRecovery(SelectedService.Name, operationResult2.Value.StartupType) ? ("已更新：" + operationResult2.Value.DisplayName) : ("已更新：" + operationResult2.Value.DisplayName + "；" + _recoveryStoreError));
			}
			await RefreshServicesAsync();
			ServicesStatus = operationStatus;
		}
	}

	private async Task RestoreServiceAsync()
	{
		if (SelectedService == null || !_serviceSnapshots.TryGetValue(SelectedService.Name, out SystemServiceRecoveryEntry value))
		{
			ServicesStatus = _recoveryStoreError ?? "所选服务没有可恢复的修改记录";
			return;
		}
		if (SelectedService.StartupType != value.ManagedStartupType && SelectedService.StartupType != value.Original.StartupType)
		{
			ServicesStatus = "服务启动方式不是程序上次写入的状态，为避免覆盖外部修改，已取消恢复";
			return;
		}
		OperationResult<SystemServiceDescriptor> operationResult = await _services.RestoreAsync(value.Original, SelectedService.StartupType);
		string operationStatus;
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			operationStatus = operationResult.Error?.Message ?? "恢复失败";
		}
		else
		{
			operationStatus = (RemoveServiceRecovery(SelectedService.Name) ? ("已恢复：" + operationResult.Value.DisplayName) : ("已恢复：" + operationResult.Value.DisplayName + "；" + _recoveryStoreError));
		}
		await RefreshServicesAsync();
		ServicesStatus = operationStatus;
	}

	private void RebuildServiceGroups()
	{
		HashSet<string> hashSet = (from @group in ServiceGroups
			where @group.IsExpanded
			select @group.Definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		ServiceGroups.Clear();
		foreach (ServiceGroupDefinition definition in ServiceGroupCatalog.All)
		{
			SystemServiceRowViewModel[] array = Services.Where((SystemServiceRowViewModel service) => definition.Matches(service.Name)).ToArray();
			ServiceGroupViewModel serviceGroupViewModel = new ServiceGroupViewModel(definition, array, array.Count((SystemServiceRowViewModel service) => _serviceSnapshots.ContainsKey(service.Name)));
			serviceGroupViewModel.IsExpanded = hashSet.Contains(definition.Id);
			ServiceGroups.Add(serviceGroupViewModel);
		}
	}

	private async Task DisableServiceGroupAsync(object? parameter)
	{
		if (!(parameter is ServiceGroupViewModel group))
		{
			ServicesStatus = "未识别要禁用的服务组";
		}
		else if (group.Services.Count == 0)
		{
			ServicesStatus = "本机没有“" + group.Title + "”组中的可配置服务";
		}
		else if (!group.CanDisable)
		{
			ServicesStatus = "“" + group.Title + "”中检测到的服务已经全部禁用";
		}
		else
		{
			if (MessageBox.Show($"将禁用“{group.Title}”中本机存在且当前未禁用的服务。\n\n{group.DisabledImpact}\n\n" + "本次只修改启动方式，不强制终止正在运行的服务。程序会在写入前保存每项原启动方式；恢复按钮会恢复原值，而不是统一改成自动。是否继续？", "确认禁用服务组：" + group.Title, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			group.IsBusy = true;
			ServicesStatus = "正在保存“" + group.Title + "”服务组的原始配置…";
			SystemToolsRecoveryDocument previousRecoveryDocument = _recoveryDocument;
			List<ServiceGroupMutationCandidate> candidates = new List<ServiceGroupMutationCandidate>();
			foreach (SystemServiceRowViewModel service in group.Services)
			{
				OperationResult<SystemServiceConfigurationSnapshot> operationResult;
				try
				{
					operationResult = await _services.CaptureAsync(service.Name);
				}
				catch (Exception ex)
				{
					group.IsBusy = false;
					ServicesStatus = "读取服务“" + service.DisplayName + "”时发生异常：" + ex.Message;
					return;
				}
				if (!operationResult.IsSuccess || (object)operationResult.Value == null)
				{
					group.IsBusy = false;
					ServicesStatus = operationResult.Error?.Message ?? ("无法读取服务：" + service.DisplayName);
					return;
				}
				if (operationResult.Value.StartupType != SystemServiceStartupType.Disabled)
				{
					SystemServiceConfigurationSnapshot original = (_serviceSnapshots.TryGetValue(service.Name, out SystemServiceRecoveryEntry value) ? value.Original : operationResult.Value);
					candidates.Add(new ServiceGroupMutationCandidate(service.DisplayName, operationResult.Value, original));
				}
			}
			if (candidates.Count == 0)
			{
				group.IsBusy = false;
				ServicesStatus = "“" + group.Title + "”中检测到的服务已经全部禁用";
				await RefreshServicesAsync();
				return;
			}
			if (!StageServiceGroupRecovery(candidates))
			{
				group.IsBusy = false;
				ServicesStatus = _recoveryStoreError ?? "无法在禁用服务组前保存完整恢复记录";
				return;
			}
			List<ServiceGroupMutationCandidate> changed = new List<ServiceGroupMutationCandidate>();
			string failureMessage = null;
			ServiceGroupMutationCandidate uncertainCandidate = null;
			foreach (ServiceGroupMutationCandidate candidate in candidates)
			{
				ServicesStatus = $"正在禁用“{group.Title}”：{candidate.DisplayName}…";
				OperationResult<SystemServiceDescriptor> operationResult2;
				try
				{
					operationResult2 = await _services.SetStartupTypeAsync(candidate.Before.Name, SystemServiceStartupType.Disabled, candidate.Before.StartupType);
				}
				catch (Exception ex2)
				{
					uncertainCandidate = candidate;
					failureMessage = "修改服务“" + candidate.DisplayName + "”时发生异常：" + ex2.Message;
					break;
				}
				if (operationResult2.IsSuccess)
				{
					SystemServiceDescriptor? value2 = operationResult2.Value;
					if ((object)value2 != null && value2.StartupType == SystemServiceStartupType.Disabled)
					{
						changed.Add(candidate);
						continue;
					}
				}
				uncertainCandidate = candidate;
				failureMessage = operationResult2.Error?.Message ?? ("服务“" + candidate.DisplayName + "”回读后仍不是禁用启动方式");
				break;
			}
			string operationStatus;
			if (failureMessage == null)
			{
				operationStatus = (CommitServiceGroupRecovery(candidates.Select((ServiceGroupMutationCandidate serviceGroupMutationCandidate) => serviceGroupMutationCandidate.Before.Name), SystemServiceStartupType.Disabled) ? $"已禁用“{group.Title}”中的 {changed.Count} 项服务；原启动方式已保存" : ("服务已禁用，但恢复记录仍处于待确认状态：" + _recoveryStoreError));
			}
			else
			{
				bool flag = await RollbackDisabledServiceGroupAsync(changed);
				if ((object)uncertainCandidate != null)
				{
					bool flag2 = flag;
					flag = flag2 & await EnsureServiceSnapshotAsync(uncertainCandidate.Before);
				}
				bool flag3 = flag && SaveRecoveryDocument(previousRecoveryDocument);
				operationStatus = ((flag & flag3) ? ("禁用“" + group.Title + "”未完成，已恢复本次操作前状态。原因：" + failureMessage) : ("禁用“" + group.Title + "”失败，且无法确认全部回滚；恢复记录已保留。原因：" + failureMessage));
			}
			group.IsBusy = false;
			await RefreshServicesAsync();
			ServicesStatus = operationStatus;
		}
	}

	private async Task RestoreServiceGroupAsync(object? parameter)
	{
		if (!(parameter is ServiceGroupViewModel group))
		{
			ServicesStatus = "未识别要恢复的服务组";
			return;
		}
		List<ServiceGroupRestoreCandidate> candidates = new List<ServiceGroupRestoreCandidate>();
		foreach (SystemServiceRowViewModel service in group.Services)
		{
			if (_serviceSnapshots.TryGetValue(service.Name, out SystemServiceRecoveryEntry recovery))
			{
				OperationResult<SystemServiceConfigurationSnapshot> operationResult;
				try
				{
					operationResult = await _services.CaptureAsync(service.Name);
				}
				catch (Exception ex)
				{
					ServicesStatus = "读取服务“" + service.DisplayName + "”时发生异常：" + ex.Message;
					return;
				}
				if (!operationResult.IsSuccess || (object)operationResult.Value == null)
				{
					ServicesStatus = operationResult.Error?.Message ?? ("无法读取服务：" + service.DisplayName);
					return;
				}
				if (operationResult.Value.StartupType != recovery.ManagedStartupType && operationResult.Value.StartupType != recovery.Original.StartupType)
				{
					ServicesStatus = "服务“" + service.DisplayName + "”已被其他程序改为不同状态；为避免覆盖外部修改，已取消整组恢复";
					return;
				}
				candidates.Add(new ServiceGroupRestoreCandidate(service.DisplayName, operationResult.Value, recovery));
				recovery = null;
			}
		}
		if (candidates.Count == 0)
		{
			ServicesStatus = "“" + group.Title + "”没有由本程序保存的恢复记录";
		}
		else
		{
			if (MessageBox.Show($"将把“{group.Title}”中的 {candidates.Count} 项服务恢复为首次修改前的启动方式。\n\n" + "不会把所有服务统一设置为自动。是否继续？", "恢复服务组：" + group.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			group.IsBusy = true;
			List<ServiceGroupRestoreCandidate> restored = new List<ServiceGroupRestoreCandidate>();
			string failureMessage = null;
			ServiceGroupRestoreCandidate uncertainCandidate = null;
			foreach (ServiceGroupRestoreCandidate candidate in candidates)
			{
				ServicesStatus = $"正在恢复“{group.Title}”：{candidate.DisplayName}…";
				OperationResult<SystemServiceDescriptor> operationResult2;
				try
				{
					operationResult2 = await _services.RestoreAsync(candidate.Recovery.Original, candidate.Before.StartupType);
				}
				catch (Exception ex2)
				{
					uncertainCandidate = candidate;
					failureMessage = "恢复服务“" + candidate.DisplayName + "”时发生异常：" + ex2.Message;
					break;
				}
				if (!operationResult2.IsSuccess || operationResult2.Value?.StartupType != candidate.Recovery.Original.StartupType)
				{
					uncertainCandidate = candidate;
					failureMessage = operationResult2.Error?.Message ?? ("服务“" + candidate.DisplayName + "”回读后与原启动方式不一致");
					break;
				}
				if (candidate.Before.StartupType != candidate.Recovery.Original.StartupType)
				{
					restored.Add(candidate);
				}
			}
			string operationStatus;
			if (failureMessage == null)
			{
				operationStatus = (RemoveServiceGroupRecovery(candidates.Select((ServiceGroupRestoreCandidate serviceGroupRestoreCandidate) => serviceGroupRestoreCandidate.Before.Name)) ? $"已恢复“{group.Title}”中的 {candidates.Count} 项服务记录" : ("服务已恢复，但无法清理恢复记录：" + _recoveryStoreError));
			}
			else
			{
				bool flag = await ReapplyManagedServiceGroupAsync(restored);
				if ((object)uncertainCandidate != null)
				{
					bool flag2 = flag;
					flag = flag2 & await EnsureServiceSnapshotAsync(uncertainCandidate.Before);
				}
				operationStatus = (flag ? ("恢复“" + group.Title + "”未完成，已重新应用恢复前状态。原因：" + failureMessage) : ("恢复“" + group.Title + "”失败，且无法确认已恢复项目全部撤销；恢复记录已保留。原因：" + failureMessage));
			}
			group.IsBusy = false;
			await RefreshServicesAsync();
			ServicesStatus = operationStatus;
		}
	}

	private async Task RefreshVirtualizationSecurityAsync()
	{
		OperationResult<VirtualizationSecurityStatus> operationResult = await _virtualizationSecurity.InspectAsync();
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			VbsStatus = operationResult.Error?.Message ?? "无法读取 VBS 状态";
			return;
		}
		VbsStatus = $"VBS：{FormatFeatureState(operationResult.Value.VirtualizationBasedSecurity)} · 内存完整性：{FormatFeatureState(operationResult.Value.MemoryIntegrity)}";
	}

	private async Task CreateVbsDisablePlanAsync()
	{
		OperationResult<VirtualizationSecurityChangePlan> operationResult = await _virtualizationSecurity.CreateDisablePlanAsync();
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			VbsStatus = operationResult.Error?.Message ?? "无法创建 VBS 关闭计划";
		}
		else if (operationResult.Value.Changes.Count == 0)
		{
			VbsStatus = "VBS 与内存完整性当前未配置为启用";
		}
		else if (!operationResult.Value.CanApplySafely)
		{
			VbsStatus = operationResult.Value.Warning;
		}
		else if (MessageBox.Show("将关闭 VBS 与内存完整性的已启用注册表配置。\n\n操作会先保存原值，完成后需要重启；安全防护能力可能降低。是否继续？", "确认关闭 VBS", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			OperationResult<VirtualizationSecurityExecutionResult> operationResult2 = await _virtualizationSecurityExecutor.ApplyAsync(operationResult.Value);
			VbsStatus = ((operationResult2.IsSuccess && (object)operationResult2.Value != null) ? operationResult2.Value.Message : (operationResult2.Error?.Message ?? "VBS 设置修改失败"));
		}
	}

	private async Task RestoreVbsAsync()
	{
		OperationResult<VirtualizationSecurityExecutionResult> operationResult = await _virtualizationSecurityExecutor.RestoreAsync();
		VbsStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? operationResult.Value.Message : (operationResult.Error?.Message ?? "没有可恢复的 VBS 设置"));
		await RefreshVirtualizationSecurityAsync();
	}

	private async Task ApplyNvidiaProfilePresetAsync()
	{
		NvidiaProfilePresetOption selectedNvidiaProfilePreset = SelectedNvidiaProfilePreset;
		if ((object)selectedNvidiaProfilePreset == null)
		{
			NvidiaProfilePresetStatus = "请先选择一个 NVIDIA 预设";
		}
		else if (_legacyNvidiaPowerState.HasBackup)
		{
			NvidiaProfilePresetStatus = "旧版本的 NVIDIA 电源偏好仍有恢复记录；请先在“禁用动态 P-State”卡片中恢复，再应用全局预设";
		}
		else
		{
			if (MessageBox.Show($"即将把“{selectedNvidiaProfilePreset.Name}”中的 {selectedNvidiaProfilePreset.SettingCount} 项原始 NIP 设置写入 NVIDIA 全局配置。\n\n" + "无需安装 NVIDIA Profile Inspector；不适用于本机驱动的项目会自动跳过。所有游戏都可能受到影响，是否继续？", "应用 NVIDIA 全局预设", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			NvidiaProfilePresetStatus = "正在验证并应用 " + selectedNvidiaProfilePreset.Name + "…";
			try
			{
				OperationResult<NvidiaProfilePresetApplyResult> operationResult = await _nvidiaProfilePresets.ApplyNipAsync(selectedNvidiaProfilePreset.Name, selectedNvidiaProfilePreset.NipXml);
				NvidiaProfilePresetStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? operationResult.Value.Message : (operationResult.Error?.Message ?? "NVIDIA 预设应用失败"));
			}
			catch (Exception ex)
			{
				NvidiaProfilePresetStatus = "NVIDIA 预设应用失败：" + ex.Message;
			}
			finally
			{
				OnPropertyChanged("HasNvidiaProfilePresetBackup");
			}
		}
	}

	private async Task ApplyNvidiaPowerStateAsync()
	{
		if (MessageBox.Show("禁用 NVIDIA 动态 P-State 后，显卡不会再频繁切换低频状态，可尝试缓解因降频或频率波动造成的卡顿和延迟。\n\n这不是通用的帧率提升：待机功耗、温度和风扇噪声可能上升，笔记本续航也可能缩短。设置需要重启后完整生效，是否继续？", "禁用 NVIDIA 动态 P-State", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		NvidiaPowerStateStatus = "正在禁用 NVIDIA 动态 P-State 并回读验证…";
		try
		{
			if (_legacyNvidiaPowerState.HasBackup)
			{
				NvidiaPowerStateStatus = "正在恢复旧版本修改过的 NVIDIA 电源偏好…";
				OperationResult<NvidiaProfilePresetApplyResult> operationResult = await _legacyNvidiaPowerState.RestoreAsync();
				if (!operationResult.IsSuccess)
				{
					NvidiaPowerStateStatus = operationResult.Error?.Message ?? "旧版本 NVIDIA 电源偏好恢复失败，已停止新设置。";
					return;
				}
			}
			OperationResult<NvidiaDynamicPStateOperationResult> operationResult2 = await _nvidiaPowerState.DisableAsync();
			NvidiaPowerStateStatus = ((operationResult2.IsSuccess && (object)operationResult2.Value != null) ? (operationResult2.Value.Message + " 重启后完整生效；驱动重新安装后可能需要重新应用。") : (operationResult2.Error?.Message ?? "NVIDIA 动态 P-State 禁用失败"));
		}
		catch (Exception ex)
		{
			NvidiaPowerStateStatus = "NVIDIA 动态 P-State 禁用失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasNvidiaPowerStateBackup");
		}
	}

	private async Task RestoreNvidiaPowerStateAsync()
	{
		NvidiaPowerStateStatus = "正在恢复首次修改前的 NVIDIA 动态调频设置…";
		try
		{
			bool flag = false;
			if (_nvidiaPowerState.HasBackup)
			{
				OperationResult<NvidiaDynamicPStateOperationResult> operationResult = await _nvidiaPowerState.RestoreAsync();
				if (!operationResult.IsSuccess)
				{
					NvidiaPowerStateStatus = operationResult.Error?.Message ?? "NVIDIA 动态 P-State 恢复失败";
					return;
				}
				flag = true;
			}
			if (_legacyNvidiaPowerState.HasBackup)
			{
				OperationResult<NvidiaProfilePresetApplyResult> operationResult2 = await _legacyNvidiaPowerState.RestoreAsync();
				if (!operationResult2.IsSuccess)
				{
					NvidiaPowerStateStatus = operationResult2.Error?.Message ?? "旧版本 NVIDIA 电源偏好恢复失败";
					return;
				}
				flag = true;
			}
			NvidiaPowerStateStatus = (flag ? "已恢复本程序首次修改前的 NVIDIA 动态调频设置；重启后完整生效。" : "没有找到本程序保存的 NVIDIA 动态调频恢复记录。");
		}
		catch (Exception ex)
		{
			NvidiaPowerStateStatus = "NVIDIA 动态 P-State 恢复失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasNvidiaPowerStateBackup");
		}
	}

	private async Task RefreshNvidiaPowerStateAsync()
	{
		try
		{
			OperationResult<NvidiaDynamicPStateInspection> operationResult = await _nvidiaPowerState.InspectAsync();
			if (!operationResult.IsSuccess || (object)operationResult.Value == null)
			{
				NvidiaPowerStateStatus = operationResult.Error?.Message ?? "NVIDIA 动态 P-State 状态读取失败。";
				return;
			}
			NvidiaDynamicPStateInspection value = operationResult.Value;
			SystemToolsViewModel systemToolsViewModel = this;
			string nvidiaPowerStateStatus;
			if (value.AdapterCount != 0)
			{
				if (value.IsFullyDisabled)
				{
					nvidiaPowerStateStatus = $"已禁用动态 P-State（{value.DisabledAdapterCount}/{value.AdapterCount} 个 NVIDIA 显卡实例）；重启后完整生效。";
				}
				else
				{
					nvidiaPowerStateStatus = (value.IsPartiallyDisabled ? $"部分 NVIDIA 显卡已禁用动态 P-State（{value.DisabledAdapterCount}/{value.AdapterCount}），建议统一设置。" : $"动态 P-State 仍由 NVIDIA 驱动管理（检测到 {value.AdapterCount} 个显卡实例）。");
				}
			}
			else
			{
				nvidiaPowerStateStatus = "未检测到可配置的 NVIDIA 显卡驱动实例。";
			}
			systemToolsViewModel.NvidiaPowerStateStatus = nvidiaPowerStateStatus;
			if (_legacyNvidiaPowerState.HasBackup)
			{
				NvidiaPowerStateStatus += " 检测到旧版本恢复记录，恢复或重新应用时会先安全迁移。";
			}
		}
		catch (Exception ex)
		{
			NvidiaPowerStateStatus = "NVIDIA 动态 P-State 状态读取失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasNvidiaPowerStateBackup");
		}
	}

	private async Task RefreshWin32PriorityAsync()
	{
		try
		{
			OperationResult<Win32PrioritySeparationInspection> operationResult = await _win32Priority.InspectAsync();
			if (!operationResult.IsSuccess || (object)operationResult.Value == null)
			{
				Win32PriorityStatus = operationResult.Error?.Message ?? "Win32 前台程序调度值读取失败。";
				return;
			}
			if (operationResult.Value.Exists)
			{
				int? currentValue = operationResult.Value.CurrentValue;
				if (currentValue.HasValue)
				{
					int value = currentValue.GetValueOrDefault();
					Win32PriorityPresetOption win32PriorityPresetOption = Win32PriorityPresets.FirstOrDefault((Win32PriorityPresetOption option) => option.Value == value);
					Win32PriorityStatus = (((object)win32PriorityPresetOption == null) ? $"当前值：0x{value:X2}（十进制 {value}），未匹配内置方案。" : $"当前值：0x{value:X2}（十进制 {value}）· {win32PriorityPresetOption.Name}。");
					return;
				}
			}
			Win32PriorityStatus = "当前未设置独立数值，由 Windows 使用系统默认调度方式。";
		}
		catch (Exception ex)
		{
			Win32PriorityStatus = "Win32 前台程序调度值读取失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasWin32PriorityBackup");
		}
	}

	private async Task ApplyWin32PriorityAsync()
	{
		Win32PriorityPresetOption selectedWin32PriorityPreset = SelectedWin32PriorityPreset;
		int value;
		if ((object)selectedWin32PriorityPreset == null)
		{
			Win32PriorityStatus = "请先选择一个调度方案。";
		}
		else if (!TryResolveWin32PriorityValue(selectedWin32PriorityPreset, out value))
		{
			Win32PriorityStatus = "自定义值必须是 0–63 的十进制，或 0x00–0x3F 的十六进制。";
		}
		else
		{
			if (MessageBox.Show($"将应用“{selectedWin32PriorityPreset.Name}”（0x{value:X2} / 十进制 {value}）。\n\n效果：{selectedWin32PriorityPreset.Effect}\n\n影响：{selectedWin32PriorityPreset.Impact}\n\n重启后完整生效，是否继续？", "调整 Win32 前台程序调度", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			Win32PriorityStatus = $"正在应用 0x{value:X2} 并回读验证…";
			try
			{
				OperationResult<Win32PrioritySeparationOperationResult> operationResult = await _win32Priority.ApplyAsync(value);
				Win32PriorityStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? (operationResult.Value.Message + " 重启后完整生效。") : (operationResult.Error?.Message ?? "Win32 前台程序调度应用失败。"));
			}
			catch (Exception ex)
			{
				Win32PriorityStatus = "Win32 前台程序调度应用失败：" + ex.Message;
			}
			finally
			{
				OnPropertyChanged("HasWin32PriorityBackup");
			}
		}
	}

	private async Task RestoreWin32PriorityAsync()
	{
		Win32PriorityStatus = "正在恢复首次修改前的 Win32 前台程序调度值…";
		try
		{
			OperationResult<Win32PrioritySeparationOperationResult> operationResult = await _win32Priority.RestoreAsync();
			Win32PriorityStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? (operationResult.Value.Message + " 重启后完整生效。") : (operationResult.Error?.Message ?? "Win32 前台程序调度恢复失败。"));
		}
		catch (Exception ex)
		{
			Win32PriorityStatus = "Win32 前台程序调度恢复失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasWin32PriorityBackup");
		}
	}

	private bool TryResolveWin32PriorityValue(Win32PriorityPresetOption preset, out int value)
	{
		int? value2 = preset.Value;
		if (value2.HasValue)
		{
			int valueOrDefault = value2.GetValueOrDefault();
			value = valueOrDefault;
			return true;
		}
		string text = Win32PriorityCustomValue.Trim();
		NumberStyles style = NumberStyles.Integer;
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(2);
			style = NumberStyles.AllowHexSpecifier;
		}
		if (int.TryParse(text, style, CultureInfo.InvariantCulture, out value))
		{
			int num = value;
			if (num >= 0)
			{
				return num <= 63;
			}
			return false;
		}
		return false;
	}

	private async Task RestoreNvidiaProfilePresetAsync()
	{
		NvidiaProfilePresetStatus = "正在恢复首次应用预设前的 NVIDIA 全局设置…";
		try
		{
			OperationResult<NvidiaProfilePresetApplyResult> operationResult = await _nvidiaProfilePresets.RestoreAsync();
			NvidiaProfilePresetStatus = ((operationResult.IsSuccess && (object)operationResult.Value != null) ? operationResult.Value.Message : (operationResult.Error?.Message ?? "NVIDIA 全局设置恢复失败"));
		}
		catch (Exception ex)
		{
			NvidiaProfilePresetStatus = "NVIDIA 全局设置恢复失败：" + ex.Message;
		}
		finally
		{
			OnPropertyChanged("HasNvidiaProfilePresetBackup");
		}
	}

	private void LoadRecoveryState()
	{
		OperationResult<SystemToolsRecoveryDocument> operationResult = _recoveryStore.Load();
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			_recoveryStoreError = operationResult.Error?.Message ?? "无法读取系统工具恢复记录";
		}
		else
		{
			ApplyRecoveryDocument(operationResult.Value);
		}
	}

	private void ApplyRecoveryDocument(SystemToolsRecoveryDocument document)
	{
		_recoveryDocument = document;
		_powerPlanRecovery = document.PowerPlan;
		_startupSnapshots.Clear();
		foreach (StartupItemRecoveryEntry startupItem in document.StartupItems)
		{
			_startupSnapshots[startupItem.Original.Item.Id] = startupItem;
		}
		_serviceSnapshots.Clear();
		foreach (SystemServiceRecoveryEntry service in document.Services)
		{
			_serviceSnapshots[service.Original.Name] = service;
		}
		_recoveryStoreError = null;
	}

	private bool SaveRecoveryDocument(SystemToolsRecoveryDocument document)
	{
		if (_recoveryStoreError != null)
		{
			return false;
		}
		OperationResult<SystemToolsRecoveryDocument> operationResult = _recoveryStore.Save(document);
		if (!operationResult.IsSuccess || (object)operationResult.Value == null)
		{
			_recoveryStoreError = operationResult.Error?.Message ?? "无法保存系统工具恢复记录";
			return false;
		}
		ApplyRecoveryDocument(operationResult.Value);
		return true;
	}

	private async Task<bool> StagePowerPlanMutationAsync(Guid currentPlanId)
	{
		if (_recoveryStoreError != null)
		{
			PowerPlanStatus = _recoveryStoreError;
			return false;
		}
		PowerPlanSnapshot powerPlanSnapshot = _powerPlanRecovery?.Original;
		if ((object)powerPlanSnapshot == null)
		{
			OperationResult<PowerPlanSnapshot> operationResult = await _powerPlans.CaptureAsync();
			if (!operationResult.IsSuccess || (object)operationResult.Value == null || operationResult.Value.ActivePlanId != currentPlanId)
			{
				PowerPlanStatus = operationResult.Error?.Message ?? "写入前无法确认并保存当前电源计划";
				return false;
			}
			powerPlanSnapshot = operationResult.Value;
		}
		PowerPlanRecoveryEntry powerPlan = new PowerPlanRecoveryEntry(powerPlanSnapshot, currentPlanId, MutationPending: true);
		if (!SaveRecoveryDocument(_recoveryDocument with
		{
			PowerPlan = powerPlan
		}))
		{
			PowerPlanStatus = _recoveryStoreError ?? "无法在切换电源计划前保存恢复记录";
			return false;
		}
		return true;
	}

	private bool CommitPowerPlanRecovery(Guid managedPlanId)
	{
		if ((object)_powerPlanRecovery == null)
		{
			_recoveryStoreError = "电源计划已切换，但恢复记录不存在";
			return false;
		}
		return SaveRecoveryDocument(_recoveryDocument with
		{
			PowerPlan = _powerPlanRecovery with
			{
				ManagedActivePlanId = managedPlanId,
				MutationPending = false
			}
		});
	}

	private bool RemovePowerPlanRecovery()
	{
		return SaveRecoveryDocument(_recoveryDocument with
		{
			PowerPlan = null
		});
	}

	private bool StageStartupRecovery(StartupItemSnapshot current, bool targetEnabled)
	{
		if (_recoveryStoreError != null)
		{
			return false;
		}
		StartupItemRecoveryEntry element = new StartupItemRecoveryEntry(_startupSnapshots.TryGetValue(current.Item.Id, out StartupItemRecoveryEntry value) ? value.Original : current, targetEnabled, MutationPending: true);
		return SaveRecoveryDocument(_recoveryDocument with
		{
			StartupItems = _recoveryDocument.StartupItems.Where((StartupItemRecoveryEntry entry) => !string.Equals(entry.Original.Item.Id, current.Item.Id, StringComparison.OrdinalIgnoreCase)).Append(element).OrderBy((StartupItemRecoveryEntry entry) => entry.Original.Item.Id, StringComparer.OrdinalIgnoreCase)
				.ToArray()
		});
	}

	private bool CommitStartupRecovery(string itemId, bool managedEnabled)
	{
		if (!_startupSnapshots.TryGetValue(itemId, out StartupItemRecoveryEntry value))
		{
			_recoveryStoreError = "启动项已修改，但恢复记录不存在";
			return false;
		}
		StartupItemRecoveryEntry element = value with
		{
			ManagedIsEnabled = managedEnabled,
			MutationPending = false
		};
		return SaveRecoveryDocument(_recoveryDocument with
		{
			StartupItems = _recoveryDocument.StartupItems.Where((StartupItemRecoveryEntry entry) => !string.Equals(entry.Original.Item.Id, itemId, StringComparison.OrdinalIgnoreCase)).Append(element).OrderBy((StartupItemRecoveryEntry entry) => entry.Original.Item.Id, StringComparer.OrdinalIgnoreCase)
				.ToArray()
		});
	}

	private bool RemoveStartupRecovery(string itemId)
	{
		return SaveRecoveryDocument(_recoveryDocument with
		{
			StartupItems = _recoveryDocument.StartupItems.Where((StartupItemRecoveryEntry entry) => !string.Equals(entry.Original.Item.Id, itemId, StringComparison.OrdinalIgnoreCase)).ToArray()
		});
	}

	private bool StageServiceGroupRecovery(IReadOnlyList<ServiceGroupMutationCandidate> candidates)
	{
		if (_recoveryStoreError != null)
		{
			return false;
		}
		HashSet<string> names = candidates.Select((ServiceGroupMutationCandidate candidate) => candidate.Before.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		IEnumerable<SystemServiceRecoveryEntry> second = candidates.Select((ServiceGroupMutationCandidate candidate) => new SystemServiceRecoveryEntry(candidate.Original, SystemServiceStartupType.Disabled, MutationPending: true));
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Where((SystemServiceRecoveryEntry entry) => !names.Contains(entry.Original.Name)).Concat(second).OrderBy((SystemServiceRecoveryEntry entry) => entry.Original.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray()
		});
	}

	private bool CommitServiceGroupRecovery(IEnumerable<string> serviceNames, SystemServiceStartupType managedStartupType)
	{
		HashSet<string> names = serviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (names.Count == 0 || names.Any((string name) => !_serviceSnapshots.ContainsKey(name)))
		{
			_recoveryStoreError = "服务组已修改，但部分恢复记录不存在";
			return false;
		}
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Select((SystemServiceRecoveryEntry entry) => (!names.Contains(entry.Original.Name)) ? entry : entry with
			{
				ManagedStartupType = managedStartupType,
				MutationPending = false
			}).ToArray()
		});
	}

	private bool RemoveServiceGroupRecovery(IEnumerable<string> serviceNames)
	{
		HashSet<string> names = serviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Where((SystemServiceRecoveryEntry entry) => !names.Contains(entry.Original.Name)).ToArray()
		});
	}

	private async Task<bool> RollbackDisabledServiceGroupAsync(IReadOnlyList<ServiceGroupMutationCandidate> changed)
	{
		bool succeeded = true;
		for (int index = changed.Count - 1; index >= 0; index--)
		{
			ServiceGroupMutationCandidate candidate = changed[index];
			try
			{
				OperationResult<SystemServiceDescriptor> operationResult = await _services.RestoreAsync(candidate.Before, SystemServiceStartupType.Disabled);
				succeeded &= operationResult.IsSuccess && operationResult.Value?.StartupType == candidate.Before.StartupType;
			}
			catch
			{
				succeeded = false;
			}
		}
		return succeeded;
	}

	private async Task<bool> ReapplyManagedServiceGroupAsync(IReadOnlyList<ServiceGroupRestoreCandidate> restored)
	{
		bool succeeded = true;
		for (int index = restored.Count - 1; index >= 0; index--)
		{
			ServiceGroupRestoreCandidate candidate = restored[index];
			try
			{
				OperationResult<SystemServiceDescriptor> operationResult = await _services.SetStartupTypeAsync(candidate.Before.Name, candidate.Recovery.ManagedStartupType, candidate.Recovery.Original.StartupType);
				succeeded &= operationResult.IsSuccess && operationResult.Value?.StartupType == candidate.Recovery.ManagedStartupType;
			}
			catch
			{
				succeeded = false;
			}
		}
		return succeeded;
	}

	private async Task<bool> EnsureServiceSnapshotAsync(SystemServiceConfigurationSnapshot expected)
	{
		_ = 1;
		try
		{
			OperationResult<SystemServiceConfigurationSnapshot> operationResult = await _services.CaptureAsync(expected.Name);
			if (!operationResult.IsSuccess || (object)operationResult.Value == null)
			{
				return false;
			}
			if (operationResult.Value.StartupType == expected.StartupType)
			{
				return true;
			}
			OperationResult<SystemServiceDescriptor> operationResult2 = await _services.SetStartupTypeAsync(expected.Name, expected.StartupType, operationResult.Value.StartupType);
			return operationResult2.IsSuccess && operationResult2.Value?.StartupType == expected.StartupType;
		}
		catch
		{
			return false;
		}
	}

	private bool StageServiceRecovery(SystemServiceConfigurationSnapshot original, SystemServiceStartupType targetStartupType)
	{
		if (_recoveryStoreError != null)
		{
			return false;
		}
		if (_serviceSnapshots.TryGetValue(original.Name, out SystemServiceRecoveryEntry value))
		{
			original = value.Original;
		}
		SystemServiceRecoveryEntry element = new SystemServiceRecoveryEntry(original, targetStartupType, MutationPending: true);
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Where((SystemServiceRecoveryEntry entry) => !string.Equals(entry.Original.Name, original.Name, StringComparison.OrdinalIgnoreCase)).Append(element).OrderBy((SystemServiceRecoveryEntry entry) => entry.Original.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray()
		});
	}

	private bool CommitServiceRecovery(string serviceName, SystemServiceStartupType managedStartupType)
	{
		if (!_serviceSnapshots.TryGetValue(serviceName, out SystemServiceRecoveryEntry value))
		{
			_recoveryStoreError = "服务已修改，但恢复记录不存在";
			return false;
		}
		SystemServiceRecoveryEntry element = value with
		{
			ManagedStartupType = managedStartupType,
			MutationPending = false
		};
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Where((SystemServiceRecoveryEntry entry) => !string.Equals(entry.Original.Name, serviceName, StringComparison.OrdinalIgnoreCase)).Append(element).OrderBy((SystemServiceRecoveryEntry entry) => entry.Original.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray()
		});
	}

	private bool RemoveServiceRecovery(string serviceName)
	{
		return SaveRecoveryDocument(_recoveryDocument with
		{
			Services = _recoveryDocument.Services.Where((SystemServiceRecoveryEntry entry) => !string.Equals(entry.Original.Name, serviceName, StringComparison.OrdinalIgnoreCase)).ToArray()
		});
	}

	private static string FormatFeatureState(ConfiguredFeatureState state)
	{
		return state switch
		{
			ConfiguredFeatureState.Enabled => "已启用", 
			ConfiguredFeatureState.Disabled => "已关闭", 
			ConfiguredFeatureState.NotConfigured => "未配置", 
			_ => "状态未知", 
		};
	}

	private static IVirtualizationSecurityChangeExecutor CreateVirtualizationSecurityExecutor()
	{
		return new VirtualizationSecurityChangeExecutor(new WindowsVirtualizationSecurityRegistryAccessor(), new JsonVirtualizationSecurityBackupStore());
	}

	private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	private void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
