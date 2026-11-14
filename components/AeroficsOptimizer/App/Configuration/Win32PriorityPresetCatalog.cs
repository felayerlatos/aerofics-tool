using System.Collections.Generic;

namespace SystemOptimizer.App.Configuration;

internal static class Win32PriorityPresetCatalog
{
	public static IReadOnlyList<Win32PriorityPresetOption> All { get; } = new _003C_003Ez__ReadOnlyArray<Win32PriorityPresetOption>(new Win32PriorityPresetOption[5]
	{
		new Win32PriorityPresetOption("foreground_max", "前台程序优先（推荐）", "0x26 / 38", "日常桌面、游戏以及主要操作一个前台程序的电脑。", "给前台程序更明显的处理器时间片倾斜，可改善高负载时的操作响应和输入跟手感。", "CPU 满载时，后台压缩、渲染、虚拟机或录制任务可能获得更少的处理时间；不会直接提高所有游戏帧率。", 38),
		new Win32PriorityPresetOption("foreground_balanced", "前台与后台兼顾", "0x25 / 37", "一边游戏一边直播、录制、编译，或需要后台任务保持吞吐的电脑。", "仍然优先照顾前台程序，但倾斜程度比推荐方案温和。", "前台响应提升可能不如 0x26 明显，换来后台任务在 CPU 忙碌时更稳定的处理机会。", 37),
		new Win32PriorityPresetOption("foreground_none", "取消额外前台偏置", "0x24 / 36", "排查调度兼容问题，或希望前台与后台程序尽量平均分配时间。", "减少 Windows 对前台程序的额外时间片奖励。", "游戏与桌面操作在 CPU 满载时可能更容易受后台任务影响，通常不作为游戏电脑的首选。", 36),
		new Win32PriorityPresetOption("background_services", "后台服务优先", "0x18 / 24", "以服务、远程任务、持续渲染或后台计算为主的专用电脑。", "降低前台程序的特殊待遇，让长时间运行的后台工作获得更稳定的处理机会。", "普通桌面和游戏操作的响应可能下降，不建议只为了游戏性能选择。", 24),
		new Win32PriorityPresetOption("custom", "自定义数值（高级）", "0–63 / 0x00–0x3F", "仅用于复现已经验证过的配置，或进行可回退的对比测试。", "直接写入你指定的 Win32 前台程序调度值。", "未经验证的组合可能让前台延迟或后台吞吐变差；不确定时请使用推荐方案。", null)
	});
}
