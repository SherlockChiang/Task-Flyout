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
[隐私政策](https://sherlockchiang.github.io/Task-Flyout/#privacy)

[English](README.md) · 简体中文

</div>

## 简介

Task Flyout 常驻 Windows 11 系统托盘，将日历、任务、邮件、RSS 与天气汇聚到一个原生小窗中。它与 Google Calendar 和 Microsoft To Do 双向同步，让你无需打开浏览器即可查看与管理日程。

## 功能

- **日历与任务** — 与 Google Calendar、Microsoft To Do 双向同步，可在托盘中直接新建、编辑、完成日程与任务。
- **邮件** — 支持 Gmail、Outlook 及任意 IMAP/SMTP 账户，后台定时抓取，新邮件抵达时弹出原生 Windows 通知。
- **RSS 阅读器** — 内置阅读器订阅源，并提供按源的图片与隐私加载控制。
- **天气** — 由 [Open-Meteo](https://open-meteo.com/) 驱动的天气面板，并可选启用任务栏天气栏。
- **提醒** — 在日程开始前的自定义分钟数弹出通知。
- **原生设计** — 基于 WinUI 3 构建，支持 Mica 材质、明暗主题，以及按日历区分的配色方案。
- **轻量** — 常驻托盘，支持开机自启与后台运行；收起时切换至 Windows 11 效能模式 (EcoQoS)，降低 CPU、功耗与内存占用。
- **多语言** — 内置简体中文与英文，默认跟随系统语言。

## 安装

1. 在 [Releases 页面](https://github.com/SherlockChiang/Task-Flyout/releases/latest) 下载最新的 `.zip` 压缩包。
2. 将压缩包解压到本地文件夹。
3. 右键点击 `install.bat`，选择 **以管理员身份运行**。
4. 脚本会自动导入受信任证书并安装应用。

### Google 登录提示

Task Flyout 仍在 Google 的应用验证流程中，因此授权页面可能出现「未验证应用」警告。应用完全在本地运行。如需继续，请点击页面底部的 **高级 (Advanced)**，再点击 **转到 Task_Flyout (不安全)**。

## 隐私与安全

Task Flyout 作为公共 OAuth 客户端在本地运行。所有凭证、邮件与日程数据均保存在你的设备上，绝不会被收集、存储或上传至任何第三方服务器。完整内容见[隐私政策](https://sherlockchiang.github.io/Task-Flyout/#privacy)。

应用声明 `runFullTrust` 能力，是为了实现纯 UWP API 无法覆盖的桌面集成：托盘图标、开机启动任务、任务栏天气栏定位，以及通知激活路由。它不会用于后台安装程序、提权或执行下载的代码。

## 从源码构建

**环境要求**

- Windows 11（SDK 10.0.19041 或更高）
- 安装了 **Windows App SDK** 与 **.NET 桌面**工作负载的 Visual Studio 2022，或 .NET 10 SDK
- 受支持的平台：`x86`、`x64` 或 `ARM64`

**构建**

```powershell
dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64
```

或在 Visual Studio 2022 中打开 `Task_Flyout.slnx` 并运行 `Task_Flyout` 项目。

## 技术栈

- WinUI 3 / Windows App SDK 1.8
- .NET 10（`net10.0-windows10.0.19041.0`），C#
- 本地缓存使用 SQLite

## 许可证

基于 [GNU GPLv3](LICENSE) 发布。
