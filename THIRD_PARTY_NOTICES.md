# 第三方组件与公开发布范围

第三方软件的版权和许可证归各自作者所有。本项目的界面、入口和封装不改变这些许可证。

## 从公开载荷移除

| 软件 | 公开版本处理 | 原始来源 |
| --- | --- | --- |
| AIDA64 | 移除程序、入口和密钥 | [FinalWire 授权说明](https://www.aida64.com/licensing) |
| HWiNFO64 | 不内嵌；体质测试调用用户自行安装的版本。官方 EULA 要求内嵌/捆绑获得明确批准 | [官方 EULA](https://www.hwinfo.com/files/license.pdf) |
| Geek Uninstaller | 不内嵌；官方免费版本限个人使用 | [官方下载页](https://geekuninstaller.com/download) |
| Thaiphoon Burner | 依本项目公开发布范围移除，不在本仓库提供二进制 | [开发者网站](http://www.softnology.biz/) |

## 保留的独立组件

- ZeroCpuTuner 的原创 C# 组件与硬件后端许可说明保留在 `components/ZeroCpuTuner/licenses`。其中包含 GPL-3.0 文本、MoBai Tool / ryzen-smu-cli / ZenStates-Core、InpOut、WinRing0 的原始声明。
- y-cruncher 是独立闭源程序，其随包许可明确允许免费镜像与分发；商业使用需联系作者。保留其原始 Read Me，不将其重新标注为本项目的开源代码。
- AeroficsOptimizer 的 .NET、7-Zip 许可说明保留在 `components/AeroficsOptimizer/Licenses`。
- 其他工具随其原文件及原有许可文件分发。重新组织界面不会授予超过原许可的使用权。
- 背景图为本项目生成的图片；商标、工具图标归各自权利人所有。

本仓库不上传软件密钥、个人运行日志或用户数据。重建输入只服务于复现本项目已声明的公开发布范围。
