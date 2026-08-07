<div align="center">

<img src="/docs/poster.png" alt="Task Flyout" width="100%" />

# Task Flyout

专为 Windows 11 打造的托盘助手，集日历、任务、邮件、RSS 与天气于一体。

[![Platform](https://img.shields.io/badge/Platform-Windows%2011-blue.svg?style=flat-square)](#)
[![Release](https://img.shields.io/github/v/release/SherlockChiang/Task-Flyout?style=flat-square)](https://github.com/SherlockChiang/Task-Flyout/releases/latest)
[![Tech](https://img.shields.io/badge/WinUI%203%20%7C%20.NET%2010-purple.svg?style=flat-square)](#)
[![License](https://img.shields.io/badge/License-GPLv3-green.svg?style=flat-square)](LICENSE)

[官网](https://sherlockchiang.github.io/Task-Flyout/) ·
[下载](https://github.com/SherlockChiang/Task-Flyout/releases/latest) ·
[隐私政策](https://sherlockchiang.github.io/Task-Flyout/privacy.html)

[English](README.md) · 简体中文

</div>

## 简介

Task Flyout 常驻 Windows 11 系统托盘，将日历、任务、邮件、RSS 与天气汇聚到一个原生小窗中。它支持 Google、Microsoft 与 iCloud 日历以及 Google Tasks、Microsoft To Do，让你无需打开浏览器即可查看与管理日程。

## 功能

- **日历与任务** — 与 Google、Microsoft、iCloud 日历以及 Google Tasks、Microsoft To Do 双向同步，可在托盘中直接新建、编辑、完成日程与任务。
- **邮件** — 支持 Gmail、Outlook 及任意 IMAP/SMTP 账户，后台定时抓取，新邮件抵达时弹出原生 Windows 通知。
- **RSS 阅读器** — 内置阅读器订阅源，并提供按源的图片与隐私加载控制。
- **天气** — 由 [Open-Meteo](https://open-meteo.com/) 驱动的天气面板，并可选启用任务栏天气栏；天气设置可在 Web Experience Pack 可用时请求 Windows 原生 Widgets 入口，否则继续使用现有 Task Flyout 自绘天气栏。
- **提醒** — 在日程开始前的自定义分钟数弹出通知。
- **原生设计** — 基于 WinUI 3 构建，支持 Mica 材质、明暗主题，以及按日历区分的配色方案。
- **轻量** — 常驻托盘，支持开机自启与后台运行；收起时切换至 Windows 11 效能模式 (EcoQoS)，降低 CPU、功耗与内存占用。
- **多语言** — 内置简体中文、繁体中文与英文，默认跟随系统语言。

## 安装

1. 在 [Releases 页面](https://github.com/SherlockChiang/Task-Flyout/releases/latest) 下载最新的 `.zip` 压缩包。
2. 将压缩包解压到本地文件夹。
3. 在解压目录中打开 Windows PowerShell，然后运行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

4. 同意证书信任提示，脚本随后会安装已签名的应用包。

### Google 登录提示

Task Flyout 仍在 Google 的应用验证流程中，因此授权页面可能出现「未验证应用」警告。应用完全在本地运行。如需继续，请点击页面底部的 **高级 (Advanced)**，再点击 **转到 Task_Flyout (不安全)**。

### iCloud 日历

请使用 Apple 账户邮箱，以及在 [account.apple.com](https://account.apple.com/) 生成的 App 专用密码连接。Apple 要求账户先启用双重认证；Task Flyout 不接受也不会保存 Apple 账户主密码。详见 [Apple 支持](https://support.apple.com/zh-cn/102654)。

## 隐私与安全

Task Flyout 不运营开发者后端。凭据与本地缓存均在设备上受到保护；数据只会发送到你主动配置的服务提供方，开发者不会收集这些数据。完整内容见[隐私政策](https://sherlockchiang.github.io/Task-Flyout/privacy.html)。

应用声明 `runFullTrust` 能力，是为了实现纯 UWP API 无法覆盖的桌面集成：托盘图标、开机启动任务、任务栏天气栏定位，以及通知激活路由。它不会用于后台安装程序、提权或执行下载的代码。

## 从源码构建

**环境要求**

- Windows 11（SDK 10.0.26100 或更高）
- 安装了 **Windows App SDK** 与 **.NET 桌面**工作负载的 Visual Studio 2022，或 .NET 10 SDK
- 受支持的平台：`x64`

托盘日历使用 [DesktopFlyouts](https://github.com/0x5bfa/DesktopFlyouts)，
提供接近 Windows 11 原生浮窗的定位、背景、激活和失焦关闭行为。
DesktopFlyouts.WinUI 1.4.0 当前将 Task Flyout 打包架构限制为 x64。

**构建**

```powershell
dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64
```

或在 Visual Studio 2022 中打开 `Task_Flyout.slnx` 并运行 `Task_Flyout` 项目。

## 技术栈

- WinUI 3 / Windows App SDK 2.1
- .NET 10（`net10.0-windows10.0.26100.0`），C#
- DesktopFlyouts.WinUI 1.4.0
- 本地缓存使用 SQLite

## 许可证

基于 [GNU GPLv3](LICENSE) 发布。
