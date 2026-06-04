<div align="center">

<img src="/docs/poster.png" alt="Task Flyout Banner" width="100%" />

# Task Flyout

[English](#english) | [简体中文](#简体中文)

[![Platform](https://img.shields.io/badge/Platform-Windows%2011-blue.svg?style=flat-square)](#)
[![GitHub Release](https://img.shields.io/github/v/release/SherlockChiang/Task-Flyout?style=flat-square)](#)
[![Tech Stack](https://img.shields.io/badge/Tech-WinUI%203%20%7C%20C%23-purple.svg?style=flat-square)](#)
[![License](https://img.shields.io/badge/license-%20%20GNU%20GPLv3%20-green.svg?style=flat-square)](#)

[**🌐 Website**](https://sherlockchiang.github.io/Task-Flyout/) • [**📥 Download Beta**](https://github.com/SherlockChiang/Task-Flyout/releases/latest)

</div>

---

## English

**Your whole day, one click from the tray.**<br>
Task Flyout is a modern Windows 11 tray companion that brings your calendar, tasks, mail, RSS, and weather into a single, native flyout — so you never have to open a browser tab just to check what's next.

### ✨ Features

**📌 Everything in one flyout**
* 📅 **Calendar & Tasks** — Two-way sync with **Google Calendar** and **Microsoft To Do**. Create, edit, and complete tasks and events right from the tray.
* 📬 **Mail** — Connect **Gmail, Outlook, or any IMAP/SMTP** account. Background polling delivers native Windows toasts the moment new mail arrives.
* 📰 **RSS Reader** — Follow your favorite feeds with a built-in reader and fine-grained image & privacy controls.
* 🌤️ **Weather** — A live forecast pane powered by [Open-Meteo](https://open-meteo.com/), plus an optional **taskbar weather bar** that's always in view.
* 🔔 **Smart Reminders** — Get a toast a configurable number of minutes before any event begins.

**🎨 Designed for Windows 11**
* **Native & Modern** — Built with WinUI 3, with Mica material and immersive dark mode that blends into the desktop.
* 🌈 **Monet Palette** — Assign a signature color to each calendar and task list; it flows through the sidebar and every view.
* ⚡ **Lightweight & Efficient** — Lives quietly in the tray with launch-on-startup and background running. Collapse it and it switches to **Windows 11 Efficiency Mode (EcoQoS)** to cut CPU, power, and memory use.
* 🌍 **Multilingual** — Built-in English and Simplified Chinese, switching automatically with your system language.

### 🚀 Installation
1. Go to the [Releases page](https://github.com/SherlockChiang/Task-Flyout/releases/latest) and download the latest `.zip` package.
2. Extract the archive to a local folder.
3. **Right-click** the `install.bat` script and select **"Run as administrator"**.
4. The script will automatically import the trusted certificate and install the app.

### ⚠️ Google OAuth Notice
**Regarding the "Unverified App" warning:**
This app is currently undergoing Google's official verification process. You might see a red security warning during authorization. Please be assured that the app runs entirely locally.
* **Workaround**: Click **"Advanced"** at the bottom of the authorization page, then click **"Go to Task_Flyout (unsafe)"** to continue.

### 🔒 Privacy & Security
**Your data belongs to you.**
This software runs locally on your device as a "Public Client". All OAuth credentials, mail, and calendar data are saved on your local machine and are **NEVER** collected, stored, or uploaded to any third-party servers. Read our full [Privacy Policy](https://sherlockchiang.github.io/Task-Flyout/#privacy).

**Why the app declares `runFullTrust`:**
Task Flyout uses the restricted `runFullTrust` capability for desktop integration that packaged WinUI apps cannot provide through UWP-only APIs: the system tray icon, startup task registration, taskbar weather bar placement, and toast activation routing. It is not used to run background installers, elevate privileges, or execute arbitrary downloaded code.

---

## 简体中文

**你的一整天，从托盘一键直达。**<br>
Task Flyout 是一款现代化的 Windows 11 托盘助手，将日历、任务、邮件、RSS 与天气汇聚到一个原生小窗中——再也不用为了看一眼接下来的安排而专门打开浏览器。

### ✨ 核心特性

**📌 一个小窗，承载全部**
* 📅 **日历与任务** — 与 **Google Calendar** 和 **Microsoft To Do** 双向同步。在托盘小窗中即可新建、修改、完成任务与日程。
* 📬 **邮件** — 支持 **Gmail、Outlook 以及任意 IMAP/SMTP** 账户。后台定时抓取，新邮件抵达即弹出原生 Windows 通知。
* 📰 **RSS 阅读器** — 内置阅读器订阅你喜爱的源，并提供精细的图片与隐私加载控制。
* 🌤️ **天气** — 由 [Open-Meteo](https://open-meteo.com/) 驱动的实时天气面板，并可选开启常驻视野的**任务栏天气栏**。
* 🔔 **智能提醒** — 在日程开始前的自定义分钟数，准时弹出通知。

**🎨 为 Windows 11 而生**
* **原生现代** — 基于 WinUI 3 打造，支持 Mica（云母）材质与沉浸式暗色模式，完美融入桌面。
* 🌈 **莫奈调色盘** — 为每个日历与任务列表指定专属代表色，并贯穿侧栏与各个视图。
* ⚡ **极致轻量高效** — 常驻系统托盘，支持开机自启与后台静默运行。收起后自动切换至 **Windows 11 效能模式 (EcoQoS)**，降低 CPU、功耗与内存占用。
* 🌍 **多语言支持** — 内置简体中文与英文，随系统语言自动切换。

### 🚀 安装指南
1. 前往 [Releases 页面](https://github.com/SherlockChiang/Task-Flyout/releases/latest) 下载最新的 `.zip` 压缩包。
2. 将压缩包解压到本地文件夹。
3. **右键**点击文件夹中的 `install.bat`，选择 **“以管理员身份运行”**。
4. 脚本会自动导入证书并安装应用，安装完成后即可在系统菜单中找到 **Task Flyout**。

### ⚠️ 登录与授权须知
**关于 Google 账号的“未验证”提示：**
本应用正在进行 Google 官方的安全验证流程。在此期间，授权页面可能会出现红色的安全警告。请放心，应用完全在您的本地运行。
* **跳过方法**：在授权页面点击底部的 **“高级 (Advanced)”** -> 点击 **“转到 Task_Flyout (不安全)”** 即可继续。

### 🔒 隐私与安全
**您的数据，只属于您。**
本软件作为“公共客户端 (Public Client)”运行在您的本地设备上。所有的 OAuth 凭证、邮件与日程数据均保存在您的本地计算机中，**绝不会**收集、存储或上传任何敏感信息至任何第三方服务器。阅读完整的 [隐私政策 (Privacy Policy)](https://sherlockchiang.github.io/Task-Flyout/#privacy)。

**为什么声明 `runFullTrust`：**
Task Flyout 使用受限的 `runFullTrust` 能力，是为了实现纯 UWP API 无法覆盖的桌面集成：系统托盘图标、开机启动任务、任务栏天气栏定位，以及通知激活路由。它不会用于后台安装程序、提权或执行任意下载代码。

---
<div align="center">
&copy; 2026 Task Flyout. Built with ❤️ and WinUI 3
</div>
