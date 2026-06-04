<div align="center">

<img src="/docs/poster.png" alt="Task Flyout" width="100%" />

# Task Flyout

A modern Windows 11 tray companion for your calendar, tasks, mail, RSS, and weather.

[![Platform](https://img.shields.io/badge/Platform-Windows%2011-blue.svg?style=flat-square)](#)
[![Release](https://img.shields.io/github/v/release/SherlockChiang/Task-Flyout?style=flat-square)](https://github.com/SherlockChiang/Task-Flyout/releases/latest)
[![Tech](https://img.shields.io/badge/WinUI%203%20%7C%20.NET%2010-purple.svg?style=flat-square)](#)
[![License](https://img.shields.io/badge/License-GPLv3-green.svg?style=flat-square)](LICENSE)

[Website](https://sherlockchiang.github.io/Task-Flyout/) ·
[Download](https://github.com/SherlockChiang/Task-Flyout/releases/latest) ·
[Privacy Policy](https://sherlockchiang.github.io/Task-Flyout/#privacy)

English · [简体中文](README.zh-CN.md)

</div>

## Overview

Task Flyout lives in the Windows 11 system tray and brings your calendar, tasks,
mail, RSS feeds, and weather into a single native flyout. It syncs with Google
Calendar and Microsoft To Do, so you can see and manage your day without opening
a browser.

## Features

- **Calendar and tasks** — Two-way sync with Google Calendar and Microsoft To Do. Create, edit, and complete events and tasks directly from the tray.
- **Mail** — Connect Gmail, Outlook, or any IMAP/SMTP account. Background polling raises a native Windows notification when new mail arrives.
- **RSS reader** — Follow feeds in a built-in reader with per-feed image and privacy controls.
- **Weather** — A forecast pane powered by [Open-Meteo](https://open-meteo.com/), plus an optional taskbar weather bar.
- **Reminders** — Toast notifications a configurable number of minutes before an event starts.
- **Native design** — Built with WinUI 3, with Mica material, light/dark themes, and a per-calendar color palette.
- **Lightweight** — Tray-resident with launch-on-startup and background running. Switches to Windows 11 Efficiency Mode (EcoQoS) while collapsed to reduce CPU, power, and memory use.
- **Multilingual** — English and Simplified Chinese, following the system language by default.

## Installation

1. Download the latest `.zip` from the [Releases page](https://github.com/SherlockChiang/Task-Flyout/releases/latest).
2. Extract the archive to a local folder.
3. Right-click `install.bat` and choose **Run as administrator**.
4. The script imports the trusted certificate and installs the app.

### Google sign-in warning

Task Flyout is still going through Google's app verification, so the OAuth
consent screen may show an "unverified app" warning. The app runs entirely on
your machine. To continue, click **Advanced** at the bottom of the page, then
**Go to Task_Flyout (unsafe)**.

## Privacy and security

Task Flyout runs locally as a public OAuth client. All credentials, mail, and
calendar data stay on your device and are never collected, stored, or uploaded to
any third-party server. See the full [Privacy Policy](https://sherlockchiang.github.io/Task-Flyout/#privacy).

The app declares the `runFullTrust` capability for desktop integration that
packaged WinUI apps cannot achieve through UWP-only APIs: the tray icon, startup
task registration, taskbar weather bar placement, and toast activation routing.
It is not used to run background installers, elevate privileges, or execute
downloaded code.

## Build from source

**Requirements**

- Windows 11 (SDK 10.0.19041 or later)
- Visual Studio 2022 with the **Windows App SDK** and **.NET desktop** workloads, or the .NET 10 SDK
- A supported platform: `x86`, `x64`, or `ARM64`

**Build**

```powershell
dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64
```

Or open `Task_Flyout.slnx` in Visual Studio 2022 and run the `Task_Flyout`
project.

## Tech stack

- WinUI 3 / Windows App SDK 1.8
- .NET 10 (`net10.0-windows10.0.19041.0`), C#
- SQLite for the local cache

## License

Released under the [GNU GPLv3](LICENSE).
