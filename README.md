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
[Privacy Policy](https://sherlockchiang.github.io/Task-Flyout/privacy.html)

English · [简体中文](README.zh-CN.md)

</div>

## Overview

Task Flyout lives in the Windows 11 system tray and brings your calendar, tasks,
mail, RSS feeds, and weather into a single native flyout. It syncs Google,
Microsoft, and iCloud calendars plus Google Tasks and Microsoft To Do, so you can
see and manage your day without opening a browser.

## Features

- **Calendar and tasks** — Two-way calendar sync with Google, Microsoft, and iCloud, plus Google Tasks and Microsoft To Do. Create, edit, and complete events and tasks directly from the tray.
- **Mail** — Connect Gmail, Outlook, or any IMAP/SMTP account. Background polling raises a native Windows notification when new mail arrives.
- **RSS reader** — Follow feeds in a built-in reader with per-feed image and privacy controls.
- **Weather** — A forecast pane powered by [Open-Meteo](https://open-meteo.com/), plus an optional taskbar weather bar. Weather settings can request the Windows-owned Widgets entry when the Web Experience Pack is available; the existing Task Flyout bar remains visible until Explorer confirms the native entry. Windows owns that entry's data and may rotate finance/news announcements; Task Flyout links to the supported Widgets notification controls but does not rewrite private Web Experience settings. A separately built, disabled-by-default Windhawk companion proof of concept lives under `windhawk/`; it is not installed by the MSIX.
- **Reminders** — Toast notifications a configurable number of minutes before an event starts.
- **Native design** — Built with WinUI 3, with Mica material, light/dark themes, and a per-calendar color palette.
- **Lightweight** — Tray-resident with launch-on-startup and background running. Switches to Windows 11 Efficiency Mode (EcoQoS) while collapsed to reduce CPU, power, and memory use.
- **Multilingual** — English, Simplified Chinese, and Traditional Chinese, following the system language by default.

## Installation

1. Download the latest `.zip` from the [Releases page](https://github.com/SherlockChiang/Task-Flyout/releases/latest).
2. Extract the archive to a local folder.
3. Open Windows PowerShell in the extracted folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

4. Approve the certificate trust prompt. The script then installs the signed app package.

### Google sign-in availability

Google sign-in in a public build depends on that build's OAuth verification
status. Do not bypass an unverified/blocked consent warning with an account that
contains real data. During development, use an allowlisted test user or build
with your own Desktop OAuth client as described below. Release notes will state
when the bundled public client is available for general Google accounts.

### iCloud Calendar

Connect with your Apple Account email and an app-specific password generated at
[account.apple.com](https://account.apple.com/). Apple requires two-factor
authentication for app-specific passwords; your main Apple Account password is
not accepted or stored. See [Apple Support](https://support.apple.com/102654).

## Privacy and security

Task Flyout has no developer-operated backend. Credentials and local caches are
protected on your device; data is sent only to the providers you configure and
is never collected by the developer. See the full [Privacy Policy](https://sherlockchiang.github.io/Task-Flyout/privacy.html).

The app declares the `runFullTrust` capability for desktop integration that
packaged WinUI apps cannot achieve through UWP-only APIs: the tray icon, startup
task registration, taskbar weather bar placement, and toast activation routing.
It is not used to run background installers, elevate privileges, or execute
downloaded code.

## Build from source

**Requirements**

- Windows 11 with SDK 10.0.26100 or later
- Visual Studio 2022 with the **Windows App SDK** and **.NET desktop** workloads, or the .NET 10 SDK
- A supported platform: `x64`

The tray calendar uses [DesktopFlyouts](https://github.com/0x5bfa/DesktopFlyouts)
for Windows 11-style placement, backdrop, activation, and light-dismiss behavior.
DesktopFlyouts.WinUI 1.4.0 currently limits packaged Task Flyout builds to x64.

**Google OAuth credentials**

Google Calendar/Tasks/Gmail sync needs your own Google OAuth client. The real
`credentials.json` is gitignored and never committed — copy the template and fill
in a Desktop (installed) OAuth client from the
[Google Cloud Console](https://console.cloud.google.com/apis/credentials):

```powershell
Copy-Item credentials.example.json credentials.json
# then edit credentials.json with your client_id / client_secret / project_id
```

It's embedded into the package at build time. A Desktop OAuth client secret is not
a true secret (the flow relies on PKCE/redirect), but for a public release rotate
the client and inject `credentials.json` from your build/CI rather than relying on
a long-lived checked-out value.

**Build**

```powershell
dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64
```

Or open `Task_Flyout.slnx` in Visual Studio 2022 and run the `Task_Flyout`
project.

## Tech stack

- WinUI 3 / Windows App SDK 2.1
- .NET 10 (`net10.0-windows10.0.26100.0`), C#
- DesktopFlyouts.WinUI 1.4.0
- SQLite for the local cache

## License

Released under the [GNU GPLv3](LICENSE).
