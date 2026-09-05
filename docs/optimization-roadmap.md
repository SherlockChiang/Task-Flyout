# Task Flyout 优化路线图

## 2026-09-05 重新评估与当前规划

本节取代下方早期扫描的执行顺序；下方保留为历史观察，不表示问题仍然全部存在。
执行状态以 [improvement-todo.md](improvement-todo.md) 的当前队列为准。
本次复查基于 `d186d51` 及托盘回归修复 `433c171`，覆盖入口、窗口生命周期、
账号路由、同步/邮件缓存、搜索、存储、CI 和近期提交；不是逐行全库审计，也没有
重新运行真实 Google 账号或 Explorer 注入实验。

### 对上一阶段的判断

- 保留已完成的账号级 provider/token/cache 路由、邮件未读状态收敛、日程时间修正、
  后台刷新发布以及 Flyout 激活交接，不做整体重写。
- `1f19378` 将单击与双击绑定到相同行为，是需求回归。`433c171` 已恢复单击
  Flyout、双击主窗口，并取消迟到的单击请求；1264 项单元测试及 Debug x64 构建
  通过不等于真实任务栏手势验收通过。
- 多 Google 账号的主体实现已经存在，但两个真实测试账号的添加、重连、移除、
  通知定位尚未完成打包验收，因此不能把“多账户支持”扩大成所有 provider 都已验证。
- 原生任务栏天气维持用户要求的存档暂停状态。它不再占用当前迭代，不安装或启用
  Windhawk companion，不进行新的 Explorer 注入尝试。

### 新复核的重点

| 优先级 | 证据 | 风险及处理方向 |
| --- | --- | --- |
| P1 | `MainWindow.xaml.cs:BuildGlobalSearchSnapshot` 的日程 GroupBy 和结果 ID 只用 Provider、CalendarId、Id、DateKey，没有 AccountId。 | 两个账号的同 ID 日程会被搜索去重。先补账号级键和冲突用例，再做两账号验收；此处不是已证实的跨账号写入漏洞。 |
| P0 | `Services/ProtectedLocalStore.cs:ReadText` 在 DPAPI 解密失败后直接读成文本并写回原文件。 | 损坏密文或其他用户的密文可能被误当旧明文迁移并覆盖。增加版本格式、严格旧格式识别、原子写入和原始数据保留测试。 |
| P1 | `.github/workflows/quality.yml` 的 PR 路径只运行链接源码的测试项目；packaged job 只在手动/定时触发，下载最新 beta。 | PR 绿色不能证明 WinUI 能构建，也不能证明当前提交的 UI 通过。增加无私密凭据的应用构建门禁；候选包验收绑定确切 commit、版本与 SHA-256。 |
| P1 | `MainWindow.xaml.cs:OpenGlobalSearch` 在显示搜索层前同步建立快照，包含 RSS 最多 1000 篇的读取及 HtmlContent 拷贝。 | 大缓存下搜索首次打开可能阻塞。先显示搜索框，再异步、可取消、按版本发布 metadata-only 快照。 |
| P1 | `Services/LocalSqliteStore.cs:GetAppDataPath` 仍返回 RoamingRoot。 | DPAPI 是设备/用户绑定数据，迁移到 LocalRoot 需要验证、备份和恢复流程；不能直接修改目录常量就发布。 |
| P2 | `Services/MailService.cs` 的 `_mutationGates` 按邮件/操作保留 SemaphoreSlim；任务队列已在 Users 为零时清理，但失败重试委托仍保留。 | 按真实持有者生命周期限制邮件 gate 和重试引用；不要对已经清理的任务队列重复“修复”。 |
| P2 | 测试项目手动链接生产源码；MailService 约 4457 行、MailPage 约 3200 行、App 约 2241 行。 | 先补跨模块行为测试，再逐个抽离不依赖 WinUI 的边界。暂不把拆文件数量或新 policy 数量当作维护收益。 |

### 分阶段执行

| 阶段 | 范围 | 完成门槛 |
| --- | --- | --- |
| A：收敛当前候选版 | 双击回归；搜索账号键；双 Google 账号、未读、通知、日程和轻关闭验收。 | 固定一个签名候选包；记录冷/热启动、隐藏/打开/动画中/固定状态的托盘手势，以及双账号隔离矩阵。失败项单独修复，不叠加新功能。 |
| B：可信交付 | PR 无秘密应用构建门禁；明确当前 PR 与已发布 beta 的测试对象。 | PR #3 仍保持草稿，待当前候选版验收和关键路由补漏通过后再决定合并；不自动合并或公开发布。 |
| C：数据恢复 | M1-03 受保护文件格式，随后 M1-05 本地存储迁移及 M1-04 通知令牌保留。 | 损坏/外来密文不覆盖，迁移可重试、可回退；退出、断电模拟和账号移除测试覆盖。 |
| D：响应与常驻 | M1-07 搜索异步化；M2-02 性能实测；M2-01 邮件协调状态上限；按证据推进 M1-08 持久化。 | 同机、同数据、同签名配置记录启动、首次 Flyout、搜索、idle 内存和 10 分钟 soak；至少 20 个成功样本才报 P95。未测量不宣称性能提升。 |
| E：UI 与维护 | M1-02 启动开关反馈；M2-05 无障碍；M2-06 区域时间、缩放、动态状态；最后增量提取 Core。 | 不重设计页面；检查 100/150/200% DPI、窄窗口、键盘、固定/取消固定、跨日/全天事件及账号来源标识。一次一个边界、一个提交。 |
| 外部：OAuth | 保留现有 Gmail 功能；沿用已有三 scope 文档，功能稳定后录演示。 | 先重新核实 Branding 和验证中心状态，再补范围理由与演示。控制台提交及审核所需外部动作另行确认；不承诺 Google 审核时间或结果。 |

### Git 与任务管理约束

- 当前工作位于 `codex/oauth-resubmission-followup`，PR #3 为草稿。复查时该分支相对
  本地 `origin/master` 已有 18 个提交、58 个变更文件；停止往其中加入无关功能。
- 本地 `master` 落后于其远端跟踪分支 127 个提交。这是本地分支陈旧，不等于远端
  缺少代码。不要 reset、强推、自动 rebase 或强行切换脏工作树。
- 用户的 `Package.appxmanifest`（1.4.4.0）及 Windhawk 源码改动继续保留、不暂存。
  测试包的临时版本号不能混入产品源码提交，签名私钥不能进入 Git。
- 每项记录实现提交、自动化结果、运行时结果；没有运行时证据就写“待验收”。
  已存档实验用 PAUSED 表示，不继续显示为正在执行的工作。
- 近期目标是可验证的候选版，不是“全项目优化完成”。A 阶段先按少量独立提交推进；
  性能工作量依赖测量，OAuth 依赖外部状态，当前不做虚假的总工期承诺。

---

## 早期扫描存档

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
| P1 | OAuth scope | Google/Microsoft 首次连接会一次申请该 provider 的日历、任务和邮件完整功能 scope；后台同步和功能调用只允许 silent token acquisition。 | 手工验证既有 token 迁移、首次完整 consent、后台授权失效只提示 reconnect 且不会自行打开浏览器。 |
| P1 | Token 和密码存储 | 本地 token/password 使用 DPAPI 和 PasswordVault，Google legacy token 有迁移。 | 增加按 provider 登出/移除本地 token 的设置项。确认删除账号时清除 token、消息正文和相关本地缓存。 |
| P1 | 日志 | 崩溃日志会将异常消息和 stack trace 写入本地 roaming logs；写入前已通过 `DiagnosticsRedactor` 脱敏 bearer/basic auth、cookie、URL userinfo、敏感 query 和常见 key/value secret。 | 继续避免主动记录邮件正文、OAuth 响应正文或完整外部 URL；新增诊断应复用日志脱敏 helper。 |
| P2 | 依赖审计 | `Task_Flyout.csproj` 对 SQLite advisory GHSA-2m69-gcr7-jv3q 做了有理由的 suppress。 | 每次依赖更新时复查；一旦 SQLitePCLRaw/Microsoft.Data.Sqlite 链路提供修复版本，移除 suppress 并升级。 |
| P2 | 网络超时 | 邮件、RSS、天气设置了 timeout；Google/Microsoft SDK 请求更多依赖 SDK 默认行为。 | 为用户触发的同步刷新增加显式取消路径，避免 OAuth/sync 流程出现无限等待感。 |
| P0 | Explorer 进程内扩展 | 独立任务栏天气需要在 Explorer 内运行私有 XAML host；错误 ABI、未完成回调或不完整恢复会直接影响任务栏稳定性。 | 将原生 host 隔离为显式启用的独立组件；精确校验 OS/PE/XAML 树签名；未知版本在修改前失败关闭；Explorer 线程不得联网、使用 WebView 或解析不受信任 XAML；卸载前必须完成 owner-thread 恢复并等待回调归零。 |

## 测试与质量待办

| 优先级 | 区域 | 建议 |
| --- | --- | --- |
| P1 | 安全测试 | `NetworkSafety`、WebView2/RSS 资源策略、RSS XML 安全、RSS URL/redirect scheme/hop 上限、RSS malformed XML fallback、RSS resolved-address private host policy、Safe URI launcher、通知 activation parser 和邮件 sanitizer 边界已有测试。 | 后续主要是需要真实网络/凭据/系统环境的集成验证。 |
| P1 | 缓存测试 | WebView2 cache prune、邮件正文 volatile LRU、邮件持久账号/文件夹排序 policy 和 JSON fallback recovery 已提取为纯逻辑并测试，覆盖低于上限不删除、按时间删除到目标大小、忽略 0 字节项、跳过当前邮件、持久顺序去重、未知项保序、空 JSON、malformed JSON 和 null deserialize fallback。 | 后续可继续覆盖更复杂的缓存迁移和旧字段兼容场景。 |
| P1 | 同步测试 | Google/Microsoft task 日期半开区间、已完成任务包含规则、recurrence 映射、事件时间窗口和 item 模型映射 policy 已提取为纯逻辑并测试，覆盖去除时间部分、起止边界、反向区间、Google RRULE、Microsoft pattern type、创建事件频率映射、全天事件、跨午夜事件、事件/任务字段规范化和 Google page token 去重/终止。Microsoft Graph 分页仍依赖 SDK PageIterator，后续需要 mock/integration 覆盖。 |
| P0 | 原生任务栏注入 | 新的 Broker/Host 会跨进程进入 Explorer，普通单元测试无法证明 ABI 和卸载安全。 | CI 先执行无 WebView import、PE 指纹门控、协议边界和 compile-only 检查；任何用户开关前必须在可丢弃 Explorer 会话完成启用、禁用、超时恢复、更新失配和多屏矩阵。 |
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
