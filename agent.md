# Task Flyout Agent 说明

本文件用于指导 AI agent 和贡献者在本仓库中工作。

## 项目概览

Task Flyout 是一个 Windows 11 托盘常驻 WinUI 3 桌面应用，基于 Windows App SDK 和 .NET 10 构建。核心功能包括日历、任务、邮件、RSS、天气、提醒、托盘图标、开机启动、Toast 激活以及可选的任务栏天气栏。

## 技术栈

- 语言：C#，已启用 nullable reference types。
- UI：WinUI 3 / Windows App SDK。
- 目标框架：`net10.0-windows10.0.19041.0`。
- 打包：启用 MSIX tooling。
- 本地数据：SQLite、DPAPI 保护的本地存储。
- 外部集成：Google Calendar/Tasks/Gmail、Microsoft Graph/To Do、IMAP/SMTP、RSS、Open-Meteo、Windows 位置 API。
- 测试：xUnit 项目位于 `Tests/Task_Flyout.Tests`，直接编译部分纯逻辑源码。

## 常用命令

- 构建应用：`dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64`
- 运行测试：`dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj -c Debug`

## 仓库约定

- 保持改动小而聚焦，优先改进现有 service/page，避免无必要的大抽象。
- 除非任务明确要求重设计，否则保持现有 WinUI 视觉语言和 XAML 结构。
- 避免阻塞 UI 线程；网络、文件、SQLite、WebView2 缓存相关工作优先使用异步 API。
- 敏感数据不得进入 Git。`credentials.json`、`Secrets.cs`、`*.pfx`、`bin/`、`obj/` 和包输出应保持未跟踪状态。
- 不要移除现有安全加固，除非用等价或更强的方案替代。
- 新增用户可见文案时，如相关功能已本地化，应同时补充英文和简体中文资源。
- 修改 OAuth、邮件、WebView2、RSS、通知激活、URI 打开逻辑时，尽量增加或更新测试。

## 安全敏感区域

- `Services/MailHtmlSanitizer.cs`：邮件 HTML 进入 WebView2 前的清洗逻辑。
- `Services/WebView2RuntimeService.cs`：共享 WebView2 缓存和嵌入资源过滤。
- `Services/NetworkSafety.cs`：阻止 RSS 和 WebView2 资源加载访问私有/特殊 IP 段。
- `Services/SafeUriLauncher.cs`：外部 URI 打开前的校验。
- `Services/ProtectedLocalStore.cs`、`Services/LocalSqliteStore.cs`、`Services/ProtectedGoogleDataStore.cs`：基于 DPAPI 的本地 token/secret 存储。
- `Services/NotificationService.cs`：Toast 激活参数校验。
- `Package.appxmanifest`：声明 `runFullTrust` restricted capability 和 `location` capability。

## 性能敏感区域

- 启动路径：`App.xaml.cs` 创建核心服务、托盘图标、轮询、天气栏和可选 flyout 预热。
- Flyout 路径：`FlyoutWindow.xaml.cs` 加载日程缓存、日历标记、定时器、天气和同步刷新。
- 邮件路径：`Services/MailService.cs` 处理轮询、IMAP 连接、正文缓存、通知和消息分页。
- RSS 路径：`Services/RssService.cs` 处理 feed 解析、图片缓存、SQLite 分页和 SSRF 安全抓取。
- WebView2 路径：`Views/MailPage.xaml.cs` 和 `Views/RssPage.xaml.cs` 创建/释放用于 HTML 渲染的 WebView2 控件。
- 天气路径：`Services/WeatherService.cs` 合并并缓存天气请求，持久化天气缓存。

## 当前优化清单

当前性能、体验、安全优化清单见 `docs/optimization-roadmap.md`。发现新风险或完成优化项时，应同步更新该文档。

## 测试建议

- 优先在 `Tests/Task_Flyout.Tests` 中为 URL 校验、网络安全、HTML 清洗、验证码识别、解析/规范化逻辑增加纯逻辑测试。
- UI 密集型改动至少要构建应用，并记录手工验证步骤。
- 安全修复应覆盖反向测试：畸形输入、私有网络目标、不安全 URI scheme、超大 payload、超时行为。
