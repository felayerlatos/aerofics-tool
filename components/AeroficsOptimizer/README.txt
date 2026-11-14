Aerofics 性能工作台 2.0

将线程调度器 1.2 与“系统优化独立版”合并为一个窗口。
Windows x64，包含完整 .NET 10 桌面运行库；启动器使用 Windows 自带的 .NET Framework 4.8。

使用
1. 将 AeroficsOptimizer-2.0.0.exe 放在固定位置，双击打开。
2. 在“进程规则”添加游戏和非游戏程序。只有列入名单的程序会接受调度。
3. 在“核心顺序”为两类程序分别启用、排序。选择一行后可上移、下移、置顶或置底。
4. 保存规则并开始调度。“停止并恢复”会恢复本次调度更改过的进程和线程状态。
5. 系统设置、电源、启动项、服务与 NVIDIA 设置仅在对应按钮点击后应用。需要写入系统的功能使用右上角“管理员模式”。

核心顺序说明
顺序表示 Windows 理想处理器偏好。每轮按线程 CPU 用量排列，将较忙线程依次安排到靠前的可用核心。
它不是操作系统层面的严格执行次序，也不会保证所有线程始终只在指定核心运行。
程序原本已有的硬亲和性与 CPU Sets 限制仍然生效。
启用核心顺序后，该类程序采用排序策略，替代本工具的核心范围、后台分池及硬亲和性设置；游戏进程优先级仍可单独设置。
未启用核心顺序的类别继续使用原来的核心范围规则。SMT 兄弟线程在界面单独标明。

系统模块
保留独立版的 21 项系统设置、Win32 调度、电源计划、启动项、服务优化、3 套 NVIDIA 预设与动态 P-State。
系统模块内的应用与恢复保持各自原有行为；底部“停止并恢复”只控制线程调度及旧调度器待恢复电源记录。
程序不会在打开时自动应用系统优化。

单文件与占用
发布文件采用 LZMA2 压缩；首次打开自动释放运行库，后续打开直接使用版本缓存。
版本缓存：%LOCALAPPDATA%\AeroficsOptimizer\Runtime\2.0.0-20261114.1
运行时占用高于下载 EXE 的大小；关闭程序后可以删除版本缓存，下次打开会重建。
线程规则与恢复记录沿用：%LOCALAPPDATA%\AeroficsLauncher\SchedulerExtended
系统优化恢复记录沿用：%LOCALAPPDATA%\SystemOptimizer\Data
从 1.2 升级无需重填规则；请先退出旧线程调度器。开机启动指向外层 EXE，因此请在启用后保持文件位置不变。
如果旧版已设置开机启动，请在新版中先取消再重新勾选，以更新启动路径。

开发与验证
App：WPF 界面、调度逻辑、系统优化视图模型。
App\lib：沿用独立版的三个功能组件 DLL；这些组件的完整源码不包含在本包中。
Launcher：.NET Framework 4.8 启动器源码。
Build.ps1：需要 .NET SDK 10 和 7-Zip。运行 .\Build.ps1 -DotnetPath <dotnet.exe> -OutputRoot <新输出目录>。
完整运行库不可裁剪，因为 WPF 与现有功能组件需要它。7-Zip、.NET 许可在 Licenses 文件夹。

可复现命令（路径含空格时请加引号）：
AeroficsOptimizer-2.0.0.exe --runtime-dir <测试缓存父目录> --install-check <报告.json>
AeroficsOptimizer-2.0.0.exe --runtime-dir <测试缓存父目录> --scheduler-self-test --report <报告.json>
AeroficsOptimizer-2.0.0.exe --runtime-dir <测试缓存父目录> --self-test <报告.json> --data-dir <临时规则目录>
调度测试仅更改程序自行创建的测试进程并恢复；系统模块检查为只读，不应用优化。读取受权限限制时，报告明确列出 blockedChecks。

淘宝 aerofics 超频 / 微信 aerofics2
