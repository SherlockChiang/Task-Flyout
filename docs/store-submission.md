# Microsoft Store Submission

## Package

- Product ID: `9MTFWNB3XQVG`
- Package identity: `Uranus92.TaskFlyout`
- Publisher: `CN=FDE9F71C-A397-410B-81EE-36E18F4325E5`
- Architectures: x86, x64, ARM64
- Privacy policy: https://sherlockchiang.github.io/Task-Flyout/privacy.html
- Support URL: https://github.com/SherlockChiang/Task-Flyout/issues

Before building, set `Package.appxmanifest` to a four-part version that is higher
than every package already submitted to Partner Center. Store upload packages are
unsigned locally; Microsoft signs them after certification.

Build the upload package from a Visual Studio Developer PowerShell:

```powershell
msbuild Task_Flyout.csproj /restore `
  /p:RestoreLockedMode=true `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:GenerateAppxPackageOnBuild=true `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxBundle=Always `
  '/p:AppxBundlePlatforms=x86|x64|ARM64' `
  /p:AppxPackageSigningEnabled=false `
  /p:AppxPackageDir="StorePackages\"
```

Upload the resulting `StorePackages/*.msixupload`, not the unsigned test
`.msixbundle`.

## English Listing

### Short description

Calendar, tasks, mail, RSS, and weather in one native Windows tray flyout.

### Description

Task Flyout keeps your day within reach from the Windows system tray. View and
manage calendars and tasks, follow Gmail, Outlook, or IMAP mail, read RSS feeds,
and check the weather without keeping a browser open.

Features include Google Calendar and Microsoft To Do synchronization, native new
mail and reminder notifications, configurable background refresh, a built-in RSS
reader with remote-image privacy controls, and an optional taskbar weather bar.
The interface follows Windows light and dark themes and supports English and
Simplified Chinese.

Task Flyout is local-first. It has no developer-operated backend and collects no
telemetry. Account credentials and private cached data are protected using
Windows security facilities. Network communication goes directly to services
that the user configures.

### Release notes

- Improved mail synchronization reliability, pagination, and offline recovery.
- Added safer Gmail, Outlook, and SMTP send confirmation to avoid duplicate mail.
- Strengthened encrypted local storage and account-data cleanup.
- Improved RSS data cleanup, accessibility announcements, and localized status messages.

## Simplified Chinese Listing

### Short description

在一个原生 Windows 托盘小窗中查看日历、任务、邮件、RSS 和天气。

### Description

Task Flyout 常驻 Windows 系统托盘，让你的日程触手可及。无需一直打开浏览器，即可查看和管理日历与任务、收取 Gmail、Outlook 或 IMAP 邮件、阅读 RSS，并查看天气。

应用支持 Google Calendar 与 Microsoft To Do 同步、原生新邮件与日程提醒、可配置的后台刷新、带远程图片隐私控制的 RSS 阅读器，以及可选的任务栏天气栏。界面跟随 Windows 明暗主题，并支持英文和简体中文。

Task Flyout 采用本地优先设计，不运营开发者服务器，也不收集遥测数据。账户凭据和私有缓存使用 Windows 安全机制保护，网络通信仅直接连接用户主动配置的服务。

### Release notes

- 改进邮件同步、分页和离线恢复的可靠性。
- 增加更安全的 Gmail、Outlook 和 SMTP 发送确认，避免重复发送。
- 加强本地加密存储和账户数据清理。
- 改进 RSS 数据清理、无障碍状态播报和本地化状态提示。

## Partner Center Checklist

- Confirm that the package version exceeds the latest submitted version.
- Upload the `.msixupload` and wait for package validation to complete.
- Select PC device family only; the app requires desktop full-trust integration.
- Declare the `runFullTrust` restricted capability for the tray icon, startup task,
  taskbar weather bar, and notification activation. It is not used for elevation,
  installers, or downloaded code execution.
- Explain the location capability: it is used only when the user enables current
  location or automatic weather-following behavior.
- Complete the age rating questionnaire. The app does not provide developer-hosted
  social, gambling, or mature content, but mail and RSS can display user-selected
  third-party content.
- Set pricing and availability.
- Add English and Simplified Chinese listings only.
- Provide at least one current desktop screenshot for each listing. Recommended
  captures: overview flyout, calendar/tasks, mail, RSS, and weather/settings.
- Verify every screenshot contains no real names, addresses, mail subjects, tokens,
  or account identifiers.
- Add the privacy policy and support URLs listed above.
- In certification notes, state that external account features require the user's
  own Google, Microsoft, or IMAP/SMTP account and that basic RSS/weather behavior
  can be reviewed without credentials.
- Publish gradually or to a private audience first, then verify install, upgrade,
  startup task, tray icon, notifications, account removal, and uninstall cleanup.
