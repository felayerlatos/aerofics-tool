using System.Collections.Generic;

namespace SystemOptimizer.App.Configuration;

internal static class ServiceGroupCatalog
{
	internal static IReadOnlyList<ServiceGroupDefinition> All { get; } = new _003C_003Ez__ReadOnlyArray<ServiceGroupDefinition>(new ServiceGroupDefinition[10]
	{
		Group("legacy_specialized", "普通电脑通常用不到的服务", "展台、零售演示、智能卡和旧式企业组件所需，普通家用或游戏电脑通常不会使用。", "禁用后，展台模式、智能卡登录和少数旧式企业软件会不可用；普通游戏、办公和上网通常不受影响。", "中风险", Exact("AJRouter"), Exact("AssignedAccessManagerSvc"), Exact("AxInstSV"), Exact("RetailDemo"), Exact("ScDeviceEnum"), Exact("SCPolicySvc"), Exact("NetTcpPortSharing")),
		Group("print_scan_fax", "打印、扫描和传真", "连接打印机、扫描仪、传真机或从部分相机导入照片时使用。", "禁用后不能打印、扫描或收发传真，部分相机导入功能也会不可用。", "中风险", Exact("Spooler"), Exact("PrintNotify"), PerUser("PrintWorkflowUserSvc"), Exact("stisvc"), Exact("Fax")),
		Group("bluetooth", "蓝牙设备", "连接蓝牙耳机、手柄、键盘、鼠标和手机时使用。", "禁用后蓝牙耳机、手柄、键鼠、手机互联及其他蓝牙设备会停止工作。", "中风险", Exact("bthserv"), Exact("BTAGService"), Exact("BthAvctpSvc"), PerUser("BluetoothUserService")),
		Group("vpn_proxy", "VPN、拨号和自动代理", "连接公司或学校 VPN、宽带拨号以及自动获取代理设置时使用。", "禁用后 VPN、拨号上网和单位网络的自动代理可能无法连接。", "高风险", Exact("RasAuto"), Exact("RasMan"), Exact("SstpSvc"), Exact("IKEEXT"), Exact("PolicyAgent"), Exact("RemoteAccess"), Exact("WinHttpAutoProxySvc")),
		Group("local_network", "局域网共享、投屏和热点", "访问共享文件、NAS、网络打印机、电视投屏或开启移动热点时使用。", "禁用后局域网共享、NAS、网络打印机、投屏和移动热点可能不可用；普通网页和游戏联网通常不受影响。", "高风险", Exact("FDResPub"), Exact("fdPHost"), Exact("SSDPSRV"), Exact("upnphost"), Exact("lltdsvc"), Exact("LanmanServer"), Exact("LanmanWorkstation"), Exact("lmhosts"), Exact("SharedAccess")),
		Group("remote_control", "远程桌面和远程管理", "允许别人通过 Windows 远程桌面或管理工具连接这台电脑。", "禁用后别人不能通过 Windows 远程桌面、远程注册表或 Windows 远程管理连接本机。", "高风险", Exact("TermService"), Exact("SessionEnv"), Exact("UmRdpService"), Exact("RemoteRegistry"), Exact("WinRM")),
		Group("windows_diagnostics", "Windows 故障诊断和错误报告", "Windows 查找故障、运行疑难解答、生成错误报告时使用。", "禁用后系统仍能正常使用，但自动排查、错误报告和部分修复建议会减少。", "中风险", Exact("DiagTrack"), Exact("diagnosticshub.standardcollector.service"), Exact("diagsvc"), Exact("DPS"), Exact("WdiServiceHost"), Exact("WdiSystemHost"), Exact("WerSvc")),
		Group("windows_media_player", "Windows Media Player 媒体共享", "把 Windows Media Player 媒体库共享给电视或局域网设备时使用。", "禁用后不能向局域网设备共享媒体库，本机播放音乐和视频不受影响。", "低风险", Exact("WMPNetworkSvc")),
		Group("microsoft_store", "微软商店和商店应用", "从 Microsoft Store 安装、更新或验证应用许可证时使用。", "禁用后微软商店及部分系统自带应用可能无法安装、更新或验证许可证。", "高风险", Exact("InstallService"), Exact("ClipSVC"), Exact("AppXSvc"), Exact("LicenseManager"), Exact("PushToInstall"), Exact("AppReadiness")),
		Group("location_sensors_mobile", "定位、传感器和手机功能", "地图定位、屏幕自动旋转、蜂窝网络、手机联动和 NFC 功能使用。", "禁用后定位、自动旋转、离线地图、蜂窝网络、电话联动和 NFC 可能不可用；笔记本及二合一设备请谨慎。", "中风险", Exact("MapsBroker"), Exact("lfsvc"), Exact("SensorDataService"), Exact("SensrSvc"), Exact("SensorService"), Exact("PhoneSvc"), Exact("SmsRouter"), Exact("WalletService"))
	});

	private static ServiceGroupDefinition Group(string id, string title, string description, string disabledImpact, string riskLabel, params ServiceNameRule[] rules)
	{
		return new ServiceGroupDefinition(id, title, description, disabledImpact, riskLabel, rules);
	}

	private static ServiceNameRule Exact(string name)
	{
		return new ServiceNameRule(name);
	}

	private static ServiceNameRule PerUser(string name)
	{
		return new ServiceNameRule(name, MatchPerUserSuffix: true);
	}
}
