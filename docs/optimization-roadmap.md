# Task Flyout 优化路线图

本文档记录本次项目扫描后发现的性能、用户体验、安全与质量优化项。内容以可执行为目标，每项包含影响范围、观察依据和建议方向。

## 项目快照

- 应用类型：Windows 11 托盘常驻 WinUI 3 桌面应用。
- 核心功能：日历、任务、邮件、RSS、天气、提醒、托盘图标、任务栏天气栏、开机启动、Toast 激活。
- 已具备的优势：托盘折叠时启用 Efficiency Mode、WebView2 懒加载和释放、邮件/RSS HTML 过滤、RSS SSRF 防护、DPAPI 保护本地存储、RSS SQLite 分页、天气请求合并、IMAP 轮询退避、WebView2 缓存清理、全局 Regex 超时防 ReDoS。
- 当前主要风险：大型 service/page 类较多、定时器刷新路径较多、`async void` 事件处理较多、应用 capability 较宽、本地工作区存在未跟踪的凭据/证书文件、高风险服务的自动化测试覆盖仍有限。

## 优先级说明

- P0：安全或数据丢失风险，发布前优先处理。
- P1：对启动、响应速度、隐私或可靠性有高影响。
- P2：中等影响的有效改进。
- P3：锦上添花或清理项。

## 性能优化

| 优先级 | 区域 | 观察 | 建议 |
| --- | --- | --- | --- |
| P1 | 启动路径 | `App.xaml.cs` 在启动路径中初始化托盘、通知服务、邮件轮询、天气栏 watchdog、主题监听、可选 flyout 预热和位置跟踪。 | 增加轻量启动耗时埋点，将非必要工作延迟到首个 idle 或窗口可见时执行。托盘创建保持即时，天气栏 watchdog、内存诊断和可选服务预热在禁用或不可见时延后。 |
| P1 | 定时器 | Flyout、天气栏、通知、邮件轮询、焦点、日历点刷新、时钟、主题刷新等位置存在多个 `DispatcherTimer`。 | 建立定时器清单，确认每个定时器随窗口可见性正确启停。间隔重叠的后台刷新优先合并。 |
| P1 | Flyout 日历标记 | `FlyoutWindow.xaml.cs` 通过视觉树扫描和延迟刷新维护日历标记点。 | 对月份切换和滚动场景做 profile。按显示月份缓存 day item 查找结果，仅在显示模式或月份变化时失效。 |
| P1 | 邮件内存 | 邮件缓存可能同时保留 folder、message、纯文本正文和 HTML 正文，单项正文上限为 80 KB 文本和 160 KB HTML。 | 按账号/文件夹统计缓存大小，更积极地淘汰最近未打开的正文。列表行保持 metadata-only，详情打开时再加载正文。 |
| P1 | WebView2 | 邮件和 RSS 已懒创建并释放 WebView2，但首次初始化仍然昂贵。 | 保持当前懒加载策略，先测量首次打开耗时。仅在用户启用且内存诊断可接受时考虑可选预热。 |
| P1 | RSS 图片 | RSS 已有本地图片缓存和清理，但远程图片抓取仍可能影响阅读器响应。 | 增加按 host 的并发限制，并把请求取消绑定到文章选择变化。文章不再选中时停止继续抓图。 |
| P2 | SQLite 本地存储 | `LocalSqliteStore.WriteProtectedTextAsync` 使用 `Task.Run` 包装同步写入，保护存储每次操作都会新开连接。 | 如果写入变频繁，增加小型异步写队列或复用串行连接。继续保持参数化 SQL。 |
| P2 | 天气 | 天气请求已合并并缓存 30 分钟。 | 为被城市/来源切换取代的 UI 刷新增加取消。持久化最近失败时间，避免启动后重试风暴。 |
| P2 | 发布体积 | 非 Debug 启用 ReadyToRun，但 trimming 对所有配置关闭。 | 评估发布体积和启动速度取舍。如 WinUI/MSIX 下 trimming 不安全，应记录原因，并优先清理资源体积。 |
| P2 | 诊断基线 | 已有 `MemoryDiagnosticsService` 和 `StartupDiagnostics`，并新增 `docs/performance-baseline.md` 记录可重复性能检查表。 | 后续优化前后按检查表记录冷启动、托盘 idle 内存、flyout 首次打开、邮件首次 HTML 渲染、RSS 文章打开、天气刷新。 |

## 用户体验优化

| 优先级 | 区域 | 观察 | 建议 |
| --- | --- | --- | --- |
| P1 | 首次使用 | Google OAuth 凭据和 provider 初始化失败时可能暴露偏技术化的错误。 | 增加首次使用 checklist 或账号设置状态面板，展示 Google/Microsoft/邮件/天气的准备状态和下一步操作。 |
| P1 | 后台行为 | 关闭窗口可能最小化到托盘，也可能退出，行为由设置决定并带确认流程；Settings 已在 `RunInBackground` 附近说明后台保留的托盘、同步、提醒和邮件轮询行为。 | 后续如托盘菜单也显示该状态，可同步复用同一说明。 |
| P1 | 离线/错误状态 | 邮件、RSS、天气、同步、OAuth 都可能独立失败。 | 统一各页面的空状态、加载状态和错误状态，提供重试按钮和最后成功时间。 |
| P1 | 邮件隐私 | 未信任发件人的远程图片默认阻止，并提供单次显示和信任发件人。 | 在 banner 中更明确说明：已阻止远程内容、当前发件人信任状态、操作是单次还是永久。 |
| P1 | RSS 阅读器 | RSS 远程资源默认阻止，启用后通过安全代理抓取。 | 在阅读器头部展示每个 feed 的图片/隐私控制，并在图片被阻止时给出可见提示。 |
| P2 | 天气权限 | manifest 仍需声明 `location`，因为天气页提供“使用当前位置”和“自动跟随位置”。启动路径已不再恢复定位监听或请求权限，只有用户点击当前位置或手动打开自动跟随才会调用 Windows 位置权限。 | 手工验证 Windows 权限拒绝/允许路径、启动不弹位置权限和自动跟随关闭路径。 |
| P2 | 长耗时操作 | 同步、邮件拉取、RSS 刷新、图标包导入、WebView2 缓存清理都可能耗时；邮件拉取、RSS 刷新/添加订阅/添加文件夹、天气刷新、天气图标包导入/删除、WebView2 缓存清理已在运行中禁用重复入口。 | 继续统一其它耗时操作的进度、重复触发防护和可取消路径。 |
| P2 | 通知设置 | 邮件和提醒通知会路由到应用页面；Settings 已说明日程提醒、新邮件轮询通知和天气警报分别生效，天气警报在天气页设置。 | 后续如需更细控制，可继续拆分提醒、新邮件、天气警报的独立设置和状态验证。 |
| P2 | 本地化 | README 声明支持英文和简体中文。 | 审计新增和现有硬编码 UI 字符串，尤其是异常 fallback 和状态消息，确保两种语言完整。 |
| P2 | 无障碍 | 自定义 flyout、任务栏天气栏、图标字体和密集列表可能存在无障碍缺口；RSS、邮件主要操作按钮、天气设置操作按钮和 Settings 缓存清理按钮已补充 accessible name/tooltip。 | 继续审计其它页面的纯图标按钮、键盘导航、高对比度、缩放和屏幕阅读器标签。 |
| P3 | 设置可发现性 | 功能开关可能分散在多个页面。 | 按主题重组设置：通用、同步、邮件隐私、RSS 隐私、天气/位置、诊断。 |

## 安全与隐私优化

| 优先级 | 区域 | 观察 | 建议 |
| --- | --- | --- | --- |
| P0 | 本地敏感文件 | 工作区存在 `credentials.json`、`Secrets.cs` 和 `Task_Flyout_TemporaryKey.pfx`。`.gitignore` 已忽略它们，本次扫描中 `git ls-files` 未显示这些文件被跟踪。 | 保持未跟踪状态。如这些文件曾被共享或发布，轮换发布凭据/证书。优先通过本地开发配置或 CI secret 注入 `credentials.json`，不要发布私有签名密钥。 |
| P0 | Manifest capability | `Package.appxmanifest` 声明 `runFullTrust` 和 `location`。`location` 仍被当前位置和自动跟随天气功能使用；启动路径已避免自动请求位置权限。 | 保持 README/privacy 文档与实际用途一致，并手工验证启动、拒绝/允许权限、自动跟随开关路径。 |
| P1 | WebView2 远程资源 | 邮件嵌入资源策略已阻止本机、私网和 `.local` HTTPS/HTTP host；RSS 启用后通过 SSRF-safe client 代理 HTTPS 资源。WebView2/RSS 资源策略已有单元测试。 | 若邮件目标只是远程图片，考虑阻止非图片资源类型。 |
| P1 | HTML 清洗 | 邮件 sanitizer 基于正则，已有超时保护和测试，覆盖 entity 编码危险 URL、空白/控制字符协议混淆、CSS escape dangerous style、namespaced dangerous tags、oversized input fallback、malformed dangerous tags、`srcset` 远程候选和 trusted `meta refresh`。正则 sanitizer 天然较脆弱。 | 若维护成本升高，考虑基于 HTML parser 的 sanitizer。 |
| P1 | RSS 解析 | RSS fetcher 有最大字节数、重定向限制、DNS pin 到公网 IP 和 XML 解析；feed scheme、redirect scheme、redirect hop 上限、malformed XML fallback、resolved-address private host policy 和 XML 安全设置已有测试。 | 后续可在独立网络测试环境补真实 DNS-rebinding integration。 |
| P1 | 外部 URI 打开 | 邮件/RSS WebView 导航和打开浏览器动作走 `SafeUriLauncher`；Safe URI tests 覆盖 scheme、本机/私网 host、超长 URL 和 userinfo 欺骗链接；通知 activation parser 也有校验。 | 继续在手工验证中覆盖邮件/RSS/Toast 的实际点击路径。 |
| P1 | OAuth scope | Google/Microsoft 已拆分日历/任务与邮件 scope，邮件读取、标记已读、发送也按能力延迟授权；设置和写邮件界面已补充额外 consent 说明。 | 手工验证既有 broad-scope token 与新 least-privilege 授权路径。 |
| P1 | Token 和密码存储 | 本地 token/password 使用 DPAPI 和 PasswordVault，Google legacy token 有迁移。 | 增加按 provider 登出/移除本地 token 的设置项。确认删除账号时清除 token、消息正文和相关本地缓存。 |
| P1 | 日志 | 崩溃日志会将异常消息和 stack trace 写入本地 roaming logs；写入前已通过 `DiagnosticsRedactor` 脱敏 bearer/basic auth、cookie、URL userinfo、敏感 query 和常见 key/value secret。 | 继续避免主动记录邮件正文、OAuth 响应正文或完整外部 URL；新增诊断应复用日志脱敏 helper。 |
| P2 | 依赖审计 | `Task_Flyout.csproj` 对 SQLite advisory GHSA-2m69-gcr7-jv3q 做了有理由的 suppress。 | 每次依赖更新时复查；一旦 SQLitePCLRaw/Microsoft.Data.Sqlite 链路提供修复版本，移除 suppress 并升级。 |
| P2 | 网络超时 | 邮件、RSS、天气设置了 timeout；Google/Microsoft SDK 请求更多依赖 SDK 默认行为。 | 为用户触发的同步刷新增加显式取消路径，避免 OAuth/sync 流程出现无限等待感。 |

## 测试与质量待办

| 优先级 | 区域 | 建议 |
| --- | --- | --- |
| P1 | 安全测试 | `NetworkSafety`、WebView2/RSS 资源策略、RSS XML 安全、RSS URL/redirect scheme/hop 上限、RSS malformed XML fallback、RSS resolved-address private host policy、Safe URI launcher、通知 activation parser 和邮件 sanitizer 边界已有测试。 | 后续主要是需要真实网络/凭据/系统环境的集成验证。 |
| P1 | 缓存测试 | WebView2 cache prune、邮件正文 volatile LRU、邮件持久账号/文件夹排序 policy 和 JSON fallback recovery 已提取为纯逻辑并测试，覆盖低于上限不删除、按时间删除到目标大小、忽略 0 字节项、跳过当前邮件、持久顺序去重、未知项保序、空 JSON、malformed JSON 和 null deserialize fallback。 | 后续可继续覆盖更复杂的缓存迁移和旧字段兼容场景。 |
| P1 | 同步测试 | Google/Microsoft task 日期半开区间、已完成任务包含规则、recurrence 映射、事件时间窗口和 item 模型映射 policy 已提取为纯逻辑并测试，覆盖去除时间部分、起止边界、反向区间、Google RRULE、Microsoft pattern type、创建事件频率映射、全天事件、跨午夜事件、事件/任务字段规范化和 Google page token 去重/终止。Microsoft Graph 分页仍依赖 SDK PageIterator，后续需要 mock/integration 覆盖。 |
| P2 | 性能基线 | 已新增 `docs/performance-baseline.md`，覆盖环境记录、冷启动、托盘 idle 内存、flyout 首次打开、邮件 HTML、RSS 文章和天气刷新测量流程。优化前后结果继续写入 PR 或 release notes。 |
| P2 | 错误处理 | 日志脱敏 helper 已测试 bearer/basic auth、cookie、URL userinfo、敏感 query 和常见 key/value secret；用户可见错误消息 helper 已测试脱敏、空白折叠、空消息 fallback 和长度限制，并接入 RSS 错误状态。继续测试 OAuth 过期、IMAP 认证失败、WebView2 runtime 缺失等 fallback。 |

## 建议执行顺序

1. 先处理发布卫生：确认私有凭据/证书未被跟踪，记录签名和凭据注入方式，轮换可能暴露过的内容。
2. 补安全测试：WebView2 资源策略、RSS XML 安全、sanitizer 绕过用例。
3. 增加启动和首次打开性能埋点，让后续优化基于数据而不是猜测。
4. 改进首次使用、离线/错误状态、邮件/RSS/天气隐私提示。
5. Profile 并优化最重的可见路径：flyout 日历点刷新、邮件正文缓存、RSS 图片抓取、WebView2 首次渲染。

## 本次扫描备注

- 本次扫描时，工作区已有未提交修改：`Package.appxmanifest`、`Views/MailPage.xaml.cs`、`WeatherBarWindow.xaml`、`WeatherBarWindow.xaml.cs`。本次文档更新未修改这些文件。
- 本地工作区存在敏感文件，但本次对 `credentials.json`、`Secrets.cs`、`Task_Flyout_TemporaryKey.pfx` 执行 `git ls-files` 时没有发现它们被 Git 跟踪。
