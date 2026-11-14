# Aerofics 超频工具箱

Windows x64 硬件工具启动器。二次元机房背景采用 60% 透明度与轻微虚化，常用工具居中，ZeroCpuTuner 位于左侧，Aerofics 优化器位于右侧，主板型号显示在右下角。

![工具箱主界面](docs/main-window.jpg)

## 使用

从 [Releases](https://github.com/felayerlatos/aerofics-tool/releases) 下载发布版 `AeroficsToolbox-v2026.11.15-win-x64.exe`，在 Windows 10/11 x64 上运行。

- 左侧 **ZeroCpuTuner**：点击后请求管理员权限并启动。界面明确标注：实验性功能；仅推荐 983 及以下等级使用；不推荐 9850、9950 或未来的新 CPU 使用。
- 右侧 **Aerofics 优化器**：点击“安装到桌面”后先确认。同意才复制 `AeroficsOptimizer-2.0.0.exe` 到当前用户桌面；同名文件存在时明确提示替换。
- 中间为 10 个工具入口及“一键体质测试”。体质测试使用用户自行安装并有权使用的 HWiNFO64；公开版不捆绑 HWiNFO。
- 右上角可最小化、退出，也可按 Esc 退出。

工具箱主程序自带 .NET 运行时。原样内置的 **ZeroCpuTuner 需要 .NET 8 Desktop Runtime x64**；可从 [Microsoft 官方页面](https://dotnet.microsoft.com/download/dotnet/8.0) 安装。

首次运行将工具准备到 `%LOCALAPPDATA%\AeroficsLauncher\Packages\20261115-public-1`，之后复用缓存并保留工具设置。EXE 所在目录不会摊开临时文件。体质测试结果写入 `%LOCALAPPDATA%\AeroficsLauncher\Results`。

## 公开版工具范围

| 内置工具 | 入口用途 |
| --- | --- |
| CPU-Z、GPU-Z | 硬件信息 |
| DDU、Dism++ | 驱动清理、系统维护 |
| Everything | 文件搜索 |
| NVIDIA Inspector、Profile Inspector | 显卡与驱动配置 |
| Ryzen Tool、ZenTimings | CPU / 内存参数 |
| TM5 | 内存测试 |
| AeroficsOptimizer 2.0.0 | 确认后复制到桌面 |
| ZeroCpuTuner | 管理员启动的实验性 CPU 调校 |

**公开源码及发布载荷不包含 AIDA64、其密钥、Thaiphoon、Geek Uninstaller、HWiNFO 二进制或个人运行日志。**

HWiNFO 仅作为用户自行提供的外部依赖。程序名称在移除清单和文档中的出现不代表包含该软件。第三方许可说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 源码结构

```text
src/                         工具箱主程序（C# / WinForms）
assets/                      背景图片与图标
components/AeroficsOptimizer/ 优化器可修改工程、封装源码和依赖库
components/ZeroCpuTuner/      CPU 调校组件源码与许可证
verification/                实际资源、界面、复制流程检查
Prepare-Tools.ps1             从现有工具目录准备公开版载荷
Pack-Payload.ps1              原文件封装与 AES-GCM 资源打包
Build.ps1                     生成 Windows x64 单 EXE
```

大型第三方二进制、生成的资源包、发布成品、bin/obj、日志不进入 Git 历史。发布时将 EXE、源码压缩包和重建输入包作为独立 Release 附件；GitHub 自动源码归档不包含第三方二进制。

## 构建

要求 Windows x64、PowerShell 7、.NET 8 SDK。

使用发布的重建输入包时，将其中的 `tools` 目录解压后执行：

```powershell
pwsh ./Build.ps1 -ToolsDirectory ./build-inputs/tools
```

也可以用自己的原始工具目录和两个独立 EXE 准备载荷：

```powershell
pwsh ./Prepare-Tools.ps1 `
  -OriginalToolsDirectory C:/MyTools `
  -OptimizerExe C:/MyBuilds/AeroficsOptimizer-2.0.0.exe `
  -TunerExe C:/MyBuilds/ZeroCpuTuner.exe `
  -Destination ./build/tools
pwsh ./Build.ps1 -ToolsDirectory ./build/tools
```

`Prepare-Tools.ps1` 排除公开版移除清单中的程序和历史日志。`Pack-Payload.ps1` 会再次拒绝包含这些程序或 AIDA64 密钥的输入目录。生成的 `payload.enc` 与 `PayloadKey.g.cs` 必须成对使用。AES 封装只用于单文件分发，不作为防破解机制。

两个附加 EXE 的字节保持原样；封装不修改 ZeroCpuTuner 的 SMU 后端。单独重建组件时，参见各自目录的说明。AeroficsOptimizer 需要 .NET 10 SDK 和 7-Zip，其 `App/lib` 下三个既有功能库只有二进制，完整库源码不在本仓库中。ZeroCpuTuner 的原始后端输入位于重建输入包 `zero-tuner-payload.zip`，复制为 `components/ZeroCpuTuner/payload.zip` 后可直接 `dotnet publish`。

## 验证

```powershell
dotnet run --project ./verification/Verify.csproj -c Release -- `
  ./qa/check C:/MyBuilds/AeroficsOptimizer-2.0.0.exe C:/MyBuilds/ZeroCpuTuner.exe
```

验证编译后的真实界面和内置资源、原 EXE 的逐字节一致性、缓存复用、复制及覆盖、安装确认取消，以及 1600×900、1280×800、1024×768 下的按钮与主板信息布局。验证过程不运行硬件调校和压力测试。

版本记录见 [CHANGELOG.md](CHANGELOG.md)。本仓库未统一替换各组件已有许可证；没有许可证声明的原创部分暂未另行授予开源许可。
