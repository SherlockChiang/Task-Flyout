# Task Flyout Improvement Todo

This file is the local source of truth for project improvements. Keep each implementation focused, verify it independently, and record its commit after completion.

Status values:

- `TODO`: not started
- `IN PROGRESS`: active work
- `DONE`: implemented and verified
- `BLOCKED`: requires an external decision, credential, service, or environment

## P0 - Release And Data Integrity

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P0-01 | DONE | Security | Split CI restore/test/build from signing and release secrets. | Tests run without OAuth/signing secrets; signing secrets exist only in a protected release job; release permissions are scoped to that job. | `ci: isolate beta signing secrets` |
| P0-02 | DONE | Data integrity | Serialize and version calendar cache persistence. | An older asynchronous save cannot overwrite a newer cache snapshot; failures are observable; concurrency behavior is tested. | `fix: serialize calendar cache persistence` |
| P0-03 | DONE | Privacy | Fail account removal when Google legacy token cleanup fails. | Legacy token deletion errors propagate; the account remains available for retry; failure behavior is tested. | `privacy: fail closed on legacy token cleanup` |
| P0-04 | DONE | UX | Allow onboarding to finish without configuring every integration. | Users can finish or skip setup; onboarding completion is versioned and independent of provider readiness. | `ux: make onboarding integrations optional` |

## P1 - Responsiveness And Core UX

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P1-01 | DONE | Performance | Move RSS initialization and first-page loading off the UI thread. | First page load does not synchronously initialize/query/decrypt the full RSS cache; the first page is loaded asynchronously. | `perf: move RSS initialization off the UI thread` |
| P1-02 | DONE | Performance | Stop loading 1,000 RSS articles before SQL paging. | RSS service keeps subscriptions/folders in memory and queries article pages directly; startup allocations are reduced. | `perf: avoid preloading RSS articles` |
| P1-03 | DONE | Lifecycle | Cancel RSS refresh when the page unloads. | Feed and image work observe page cancellation; unloaded pages receive no refresh UI updates. | `fix: cancel RSS refresh on page unload` |
| P1-04 | DONE | Performance | Replace serial eager RSS article-image downloads with lazy bounded downloads. | Feed metadata is persisted without waiting for article images; reader images load on demand; global/per-host proxy concurrency is bounded. | `perf: defer RSS article image downloads` |
| P1-05 | DONE | Correctness | Make weather city suggestions typed, debounced, and cancellable. | Stale searches cannot replace newer results; city and coordinates update atomically; stale results are rejected. | `fix: prevent stale weather city selections` |
| P1-06 | DONE | UX | Add adaptive layouts for Calendar, Mail, and Tasks. | Shared wide, medium, and narrow breakpoints collapse auxiliary panes and provide single-pane navigation so primary actions remain reachable at high scaling. | `ux: add adaptive core layouts` |
| P1-07 | DONE | Accessibility | Localize accessible names, tooltips, onboarding, and RSS reader messages. | English and Simplified Chinese resources have parity; English UI exposes no hard-coded Chinese accessibility text. | `localization: complete accessible UI resources` |
| P1-08 | DONE | Accessibility | Make weather bar and agenda cards keyboard-invokable. | Controls expose Invoke semantics, Enter/Space activation, localized names, and visible focus. | `accessibility: make weather and agenda keyboard invokable` |
| P1-09 | DONE | UX | Enable scrolling in constrained flyout content and zoom in mail HTML. | All flyout controls remain reachable at high scaling; mail supports touch and keyboard zoom. | `accessibility: enable flyout scrolling and mail zoom` |
| P1-10 | DONE | Reliability | Unify task mutation states and visible retry behavior. | Calendar, Tasks, and Flyout use a shared per-task mutation queue and consistently show pending, failed, queued, succeeded, and retry states instead of silent rollback. | `reliability: expose task mutation retries` |
| P1-11 | DONE | UX | Preserve quick-create input when submission fails. | The form remains available; errors are localized and redacted; retry is direct. | `ux: preserve failed quick-create input` |
| P1-12 | DONE | Security | Define provider-wide mail/calendar authorization lifecycle. | Feature removal explicitly preserves shared authorization; complete disconnect removes provider tokens and all local mail/calendar/task data, failing closed if token cleanup fails. | `security: define provider disconnect lifecycle` |

## P2 - Privacy, Efficiency, And Maintainability

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P2-01 | DONE | Privacy | Protect weather location/cache and add a deletion action. | Coordinates and cache migrate to DPAPI storage; users can stop tracking and clear weather/location data; the existing location notice explains provider use. | `privacy: protect weather location data` |
| P2-02 | DONE | Privacy | Couple WebView2 browsing-data cleanup to mail/RSS deletion. | Shared-profile site data, history, and disk cache are cleared after sensitive data deletion; both confirmation dialogs explain the cross-reader impact. | `privacy: clear embedded browser data` |
| P2-03 | DONE | Security | Default RSS to HTTPS and block silent downgrade redirects. | HTTP requires per-subscription approval; HTTPS-to-HTTP downgrade is rejected. | `security: require approval for HTTP RSS feeds` |
| P2-04 | DONE | Security | Make local-network RSS permission subscription-specific. | Local access is scoped to an encrypted per-subscription authority, visible at approval, and rejected when redirects change host, port, or protocol. | `security: scope RSS local network access` |
| P2-05 | DONE | Security | Replace exception-string logging with structured diagnostics. | Persisted exceptions contain only allowlisted metadata and are stored in local, non-roaming app data with existing retention limits. | `diagnostics: persist structured exception metadata` |
| P2-06 | DONE | Supply chain | Allowlist NuGet sources and enforce locked restore for release builds. | Restore sources are explicit; every CI/release restore uses locked mode; vulnerability checks remain clean. | `security: lock release package restore` |
| P2-07 | DONE | Performance | Bound Google calendar request concurrency and add throttling retries. | Calendar fan-out has a configured limit and handles 429/transient failures with bounded backoff. | `perf: bound Google calendar requests` |
| P2-08 | DONE | Performance | Reduce calendar cache deep cloning. | Published snapshots reuse unchanged day buckets; tests show isolated bucket cloning and less than one-quarter of full-clone allocations for an unchanged large cache. | `perf: reuse unchanged calendar cache buckets` |
| P2-09 | DONE | Performance | Bound RSS host-gate retention and reduce payload copies. | Host coordination storage is fixed at 64 gates with global concurrency 4; WebView image payloads stream directly without a second full-buffer copy. | `perf: bound RSS host concurrency gates`, `perf: stream proxied images to WebView` |
| P2-10 | DONE | Startup | Remove protected SQLite work from the synchronous startup path. | Calendar account hydration runs once in deferred background work after tray creation; windows and scheduled services await the shared task before consuming accounts. | `perf: defer protected account hydration` |
| P2-11 | DONE | Efficiency | Rotate diagnostics and batch notification-state writes. | Notification checks persist state at most once; long-running diagnostic logs rotate at a fixed size limit. | `perf: batch notification state writes`, `diagnostics: bound log retention` |
| P2-12 | DONE | Localization | Respect culture first-day-of-week and consistent language fallback. | Calendar follows the selected culture; unsupported weather languages fall back to English. | `localization: respect culture calendar layout` |
| P2-13 | DONE | Architecture | Extract shared mutation, account-removal, status, and remote-image coordinators. | Pages use shared task queues/status mapping, provider lifecycle orchestration, general status formatting, and a singleton safe remote-image proxy without a UI rewrite. | `refactor: consolidate shared page coordinators` |
| P2-14 | DONE | Testing | Add localization, accessibility, responsive UI, lifecycle, and soak coverage. | Resource parity is automated; packaged smoke tests cover keyboard/narrow layouts; soak checks track handles and memory. | `test: add packaged smoke and soak coverage` |

## P3 - Product Features

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P3-01 | DONE | Feature | Add a unified account health and offline center. | Users can see provider health, last success, cached state, pending mutations, and reconnect/retry actions. | `feat: add account health and offline center` |
| P3-02 | DONE | Feature | Add local search for Tasks, Mail, and RSS. | Debounced local search covers cached task metadata, loaded mail metadata, and encrypted RSS metadata with filter-aware paging. | `feat: add local search across core pages` |
| P3-03 | DONE | Feature | Add RSS OPML import/export. | Import previews new, duplicate, folder, and HTTP counts, maps folders without eager network fetches, and export produces portable OPML. | `feat: add RSS OPML import and export` |
| P3-04 | DONE | Feature | Add RSS read/unread and starred states. | Opening marks articles read, list actions toggle read and starred state, state survives refresh, and SQLite paging supports All, Unread, and Starred filters. | `feat: add RSS article states and filters` |
| P3-05 | DONE | Feature | Add complete task editing. | Users can edit all provider-supported title, due date, notes, completion, and deletion fields; provider/list identity is honored and unsupported time, recurrence, and move fields are explicit. | `feat: add list-aware task editing` |
| P3-06 | DONE | Feature | Add toast actions for snooze, complete, and open. | Agenda toasts use opaque protected capabilities, strict activation schemas, persisted 10-minute snooze, and shared task mutation for eligible completion. | `feat: add secure agenda toast actions` |
| P3-07 | DONE | Feature | Add protected compose draft recovery. | DPAPI-protected drafts autosave across navigation and exit, offer restore/discard, survive send failures, and are securely deleted after success or account removal. | `feat: add protected compose draft recovery` |
| P3-08 | DONE | Feature | Add mail attachments. | Compose supports ephemeral attachment selection/removal, bounded file and total sizes, staged progress, and Outlook, Gmail, and SMTP payloads. | `feat: add bounded mail attachments` |
| P3-09 | DONE | Feature | Add concise tray quick actions and status. | The flat tray menu exposes new item, sync, compose, weather, open, and exit with one privacy-safe status line. | `feat: add tray quick actions and status` |

## Verification Baseline

Before this todo was created:

- `dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj -c Debug --no-restore`: 382 passed, 0 failed, 0 skipped.
- `dotnet list Task_Flyout.csproj package --vulnerable --include-transitive --no-restore`: no known vulnerable packages from the configured sources.
- `credentials.json`, `Secrets.cs`, and `*.pfx` are not tracked by Git.

## 2026-08 Maintenance Audit Backlog

This backlog records the next maintenance pass. Keep each change independently
reviewable, update its status in the implementation commit, and do not mix the
pre-existing iCloud/Traditional Chinese worktree with unrelated maintenance.

### P0 - Release Safety And Worktree Hygiene

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M0-01 | DONE | Git/CI | Gate beta publication on a tested package instead of every direct `master` push. | Candidate packaging and packaged smoke complete before publication; publication is manual or tag-driven; the required remote branch/environment protections are documented. | `ci: gate beta publication on packaged smoke` |
| M0-02 | DONE | Distribution | Make installation artifacts and documentation agree. | English and Chinese READMEs name the installer actually shipped by the beta workflow; the generated installer has a documented invocation path. | `docs: align beta install instructions` |
| M0-03 | DONE | Git | Split the existing iCloud, provider-capability, localization, weather, and documentation work into focused commits. | Every commit builds/tests at its dependency boundary; no required untracked source is omitted; unrelated maintenance is excluded. | `refactor: centralize provider capabilities`<br>`fix: preserve provider calendar colors`<br>`feat: implement iCloud CalDAV sync`<br>`feat: add iCloud account onboarding`<br>`feat(i18n): localize last-success status messages`<br>`feat(weather): localize forecasts and alerts`<br>`feat(i18n): add Traditional Chinese localization`<br>`fix(i18n): recognize Traditional Chinese content aliases`<br>`fix(i18n): guard resource-backed Chinese XAML fallbacks`<br>`fix(weather): resume tracking with specific location labels`<br>`docs: document iCloud and privacy handling`<br>`build: bump package version`<br>`chore(i18n): remove stale localization note` |

### P1 - Data Integrity, Reliability, And Visible Defects

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M1-01 | DONE | UX/Correctness | Stop representing flyout loading and sync failures as editable agenda items. | Loading, empty, and error states are non-invokable; sync failure exposes a direct retry action; regression coverage prevents placeholder editing. | `fix(flyout): separate agenda status from editable items` |
| M1-02 | TODO | UX/Reliability | Make startup-task changes transactional and visible. | The toggle is disabled while changing; every failure re-reads the real `StartupTask.State`; localized accessible feedback explains denial/failure. | `fix: report startup task state changes` |
| M1-03 | TODO | Data integrity | Add an explicit protected-file format before migrating plaintext. | DPAPI blobs carry a magic/version header; only positively identified legacy plaintext is migrated; corrupt or foreign-user blobs are preserved for recovery. | `fix: version protected local files` |
| M1-04 | TODO | Privacy/Lifecycle | Bound notification-action and verification-code retention. | Expired/invalid tokens are deleted on read and during bounded startup/heartbeat pruning; per-scope row counts remain bounded across restart. | `privacy: prune expired notification tokens` |
| M1-05 | TODO | Storage | Move device-local protected SQLite data from roaming to local app data. | Upgrade performs an atomic, verified, restart-safe migration with rollback; caches and device-bound DPAPI ciphertext no longer live under `%APPDATA%`. | `fix: migrate protected store to local app data` |
| M1-06 | TODO | Reliability | Propagate cancellation and timeout semantics through calendar/task mutations. | Provider list/CRUD APIs accept cancellation; page unload and exit release pending operations; ambiguous remote outcomes instruct the user to refresh. | `reliability: bound provider mutations` |
| M1-07 | TODO | Performance | Remove synchronous cache/decryption work from global-search opening. | The search shell appears before indexing; index construction is asynchronous, cancellable, generation-safe, and uses lightweight searchable DTOs. | `perf: build global search index asynchronously` |
| M1-08 | TODO | Performance | Move SQLite, DPAPI, and full-cache serialization off visible UI paths. | Locks contain memory-only changes/snapshot creation; a versioned background writer performs persistence; slow-disk tests show no long dispatcher stalls. | `perf: move cache persistence off ui thread` |
| M1-09 | TODO | Reliability | Replace blanket handling of unknown UI exceptions with scoped async boundaries. | Fire-and-forget operations use a named safe runner and restore flags/state; unknown global failures log and terminate or recover explicitly instead of always setting `Handled`. | `reliability: add scoped async exception boundaries` |
| M1-10 | DONE | Reliability/UI | Recover the taskbar weather bar after Explorer restarts. | Taskbar recreation triggers prompt reattachment or a throttled rebuild; disabled/re-enabled bars cannot reuse a dead native window; polling remains a fallback and the recovery matrix is tested. | `fix(weatherbar): recover after Explorer restarts` |
| M1-11 | DONE | Reliability/Startup | Keep tray-triggered Flyout construction behind stable account hydration. | The click path records request diagnostics and restores efficiency state on failure; the Flyout never enumerates the account collection while startup hydration is mutating it. | `fix(flyout): serialize account hydration before opening` |

### P2 - Long-Running Efficiency, Architecture, And UI Quality

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M2-01 | TODO | Memory | Bound per-message mail/task mutation coordination state. | Keyed gates are reference-counted and removed safely; completed intents/retries and removed-account state are cleared; stress tests show bounded dictionaries. | `perf: bound mutation coordination state` |
| M2-02 | IN PROGRESS | Startup | Make flyout prewarm explicit or usage-adaptive. | Explicit opt-in and memory-pressure gates are implemented; record packaged idle-memory and first-open P50/P95 before marking this item done. | `perf(flyout): make prewarm memory-aware` |
| M2-03 | TODO | Efficiency | Reduce taskbar weather-bar polling. | Native/display/theme events drive normal updates; slow polling is fallback only; unchanged geometry skips window-tree scans and diagnostics work. | `perf: reduce weather bar polling` |
| M2-04 | TODO | Architecture | Extract shared policy/domain code into a referenced Core project. | App and tests reference the same assembly rather than manually linking production files; extraction is incremental and preserves WinUI behavior. | `refactor: extract task flyout core` |
| M2-05 | TODO | Accessibility | Correct dynamic command semantics and async status announcements. | Pin/read/star/trust states expose current toggle semantics; icon buttons and task checkboxes have localized names; async errors/progress use appropriate live regions. | `accessibility: expose dynamic ui state` |
| M2-06 | TODO | UI/Localization | Normalize settings information architecture, contrast, time formats, and command sizing. | Destructive storage actions have a clear scope; high contrast does not depend on opacity/hard-coded colors; time follows regional preferences; shared icon-command sizing is used. | `ux: normalize settings and command presentation` |
| M2-07 | TODO | Tooling | Add deterministic formatting, analysis, coverage, and dependency maintenance. | `.editorconfig` and CI format/analyzer checks pass; Core coverage is collected with an agreed threshold; grouped dependency updates are automated; major upgrades remain isolated. | `build: add repository quality gates` |
| M2-08 | DONE | UI/Compatibility | Match the weather bar to the active Windhawk taskbar surface. | Luminosity Dock geometry, transparency, hover fill, and corner radius are honored without overlapping widgets; missing, disabled, or unknown Windhawk settings preserve the native fallback, while malformed overrides use safe theme defaults. | `style(weatherbar): match Windhawk Luminosity dock` |
| M2-09 | DONE | Efficiency/Lifecycle | Suspend Flyout refresh work while it is hidden. | Periodic sync runs only while visible; delayed reopen refresh is throttled, cancellable, generation-safe, and cannot resume against a shutting-down Flyout. | `perf(flyout): suspend hidden refresh work` |
| M2-10 | DONE | Efficiency | Avoid rebuilding unchanged Flyout agenda snapshots. | Reopening within the same calendar month and cache version skips date-key materialization and agenda cloning; changing month or cache version forces a coherent snapshot refresh; month reuse behavior is tested. | `perf(flyout): skip unchanged agenda snapshots` |
| M2-11 | DONE | Memory | Release rebuildable Flyout caches under memory pressure. | Medium/high memory pressure drops hidden Flyout weather-icon and dot-brush caches on the UI dispatcher; visible/opening Flyouts retain live UI state; cache-trim eligibility is tested. | `perf(flyout): trim rebuildable caches` |
| M2-12 | DONE | Responsiveness | Defer Flyout calendar-dot rendering until after the window opens. | The open request performs no dot-render queueing before `Show`; visible-state handling schedules dot work at low priority; failed dispatcher enqueue does not permanently suppress later refreshes. | `perf(flyout): defer calendar dots until visible` |
| M2-13 | DONE | Efficiency/UI | Avoid reapplying an unchanged effective Flyout theme. | Light, dark, and system-theme changes still propagate; repeated applications of the same effective theme return without redundant property updates. | `perf(flyout): skip redundant theme updates` |
| M2-14 | DONE | UI/Compatibility | Keep Windhawk taskbar-style detection correct across registry views and transient read failures. | The x64 Windhawk view is preferred, 32-bit installations remain discoverable, and a locked/inaccessible settings key immediately falls back to native taskbar geometry instead of retaining stale Dock placement. | `fix(weatherbar): harden Windhawk style detection` |
| M2-15 | DONE | UI/Diagnostics | Make the active taskbar surface and computed weather-bar slot visible in read-only diagnostics. | Settings diagnostics identify the detected Windhawk surface and slot bounds without exposing registry contents or personal data; native fallback remains explicit. | `feat(weatherbar): expose Windhawk surface diagnostics` |
| M2-16 | DONE | UI/Compatibility | Place the Luminosity weather bar in the theme's left reserved strip without a startup flash. | The native window stays hidden until it is attached and positioned; the themed bar begins at client x=0 and fills `DockMargin`; full-width or unusably narrow Dock configurations retain native placement. | `fix(weatherbar): anchor Luminosity bar to left reserve` |
| M2-17 | DONE | UI/Compatibility | Keep the left reserve safe around native Widgets and give the detached bar its own translucent surface. | Widget/obstacle overlap clips the reserve or falls back to native placement; exact DockMargin is preserved across DPI; the left-reserve bar has a non-white rest/hover fill and border instead of assuming the centered Dock blur is underneath. | `fix(weatherbar): harden Windhawk reserve geometry` |
| M2-18 | DONE | UI/Compatibility | Offer an explicit Windows-native Widgets mode while preserving the existing weather-bar fallback. | The default remains the Task Flyout bar; the native option detects the per-user Web Experience Pack, requests Explorer's `TaskbarDa` entry, keeps the fallback visible until a plausible native taskbar bridge exists, retries delayed/failed activation, restores the user's captured value across process restarts, and never injects XAML into Explorer. | `feat(weatherbar): add native Widgets mode policy`, `feat(weatherbar): switch between native Widgets and fallback bar`, `fix(weatherbar): verify native Widgets before handoff` |
| M2-19 | DONE | UI/Accuracy | Distinguish Windows-managed Widgets content from Task Flyout weather injection. | Settings state that Windows can rotate finance, sports, and news announcements; the app opens the supported Widgets board protocols and gives the exact manual path for disabling taskbar announcements without writing private Web Experience settings. | `fix(weatherbar): clarify Windows-managed widget content`, `fix(weatherbar): label Widgets navigation honestly` |
| M2-20 | DONE | UI/Compatibility | Establish a compile-only Windhawk companion POC that replaces content inside the native Widgets shell. | The mod is separate from the MSIX and disabled by default; exact OS/PE and class/name/automation signatures gate the private ABI; the original Adaptive Card remains measured for Luminosity; local values and orphan recovery metadata are restored on disable/unload; owner-thread dispatch cannot leave callbacks pointing into an unloaded DLL; the bundled Windhawk compiler produces an x64 DLL without installing or loading it. | `feat(windhawk): add native weather shell POC`, `fix(windhawk): harden companion owner-thread cleanup` |

### P3 - Product Follow-Ups

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M3-01 | TODO | Distribution | Add stable/beta update-channel support. | Users can discover and install signed updates without repeating manual certificate/package steps; rollback and channel behavior are documented. | `feat: add update channel support` |
| M3-02 | TODO | Notifications | Add quiet hours and per-calendar notification controls. | Global defaults remain simple; users can suppress selected calendars and configure a quiet interval without losing explicit snoozes. | `feat: add notification quiet hours` |
| M3-03 | TODO | UI/Compatibility | Runtime-validate and package the experimental Windhawk weather companion. | A disposable Explorer session verifies enable/disable/unload, Luminosity width, hover/click, primary/secondary taskbars, Explorer restart, and fail-closed behavior on an unlisted taskbar binary before any user-facing enable switch is added. | `test(windhawk): validate native weather shell` |
| M3-04 | IN PROGRESS | Integration | Feed Task Flyout weather and activation into the native-shell companion. | A versioned, length-bounded per-user channel performs no blocking I/O on Explorer's UI thread; stale/unavailable app state fails open; native-shell click activation opens Task Flyout Weather; shutdown, reconnect, and Explorer restart leave no callbacks or handles behind. | `feat(weatherbar): define companion IPC protocol`, `feat(weatherbar): host companion weather snapshots`, `feat(windhawk): bridge weather snapshots`, `feat(windhawk): activate Task Flyout weather` |
| M3-05 | IN PROGRESS | Architecture/Security | Replace the Windhawk runtime dependency with a standalone per-user taskbar broker and Explorer host. | The x64 broker and host build without Windhawk or WebView libraries; exact OS and `Taskbar.View.dll` fingerprints gate all private ABI use; unsupported systems fail closed before Explorer memory or XAML is changed. | `feat(taskbar): scaffold standalone weather host` |
| M3-06 | IN PROGRESS | UI/Compatibility | Inject an independent pure-XAML weather button into the Windows 11 taskbar. | The host uses standard `Windows.UI.Xaml` controls and taskbar theme resources, does not require the Windows Widgets entry, reserves a bounded left-side slot, and restores the original XAML tree on disable or unload. | `feat(taskbar): add pure xaml weather view`, `feat(taskbar): inject native xaml weather button` |
| M3-07 | IN PROGRESS | Integration/UI | Connect the standalone host to Task Flyout weather, activation, settings, and diagnostics. | The existing bounded per-user IPC supplies sanitized immutable snapshots; click opens Task Flyout Weather; a localized experimental switch and privacy-safe diagnostics expose active, unsupported, fallback, and recovery states. | `feat(taskbar): consume weather snapshot pipe`, `feat(taskbar): connect standalone weather host` |
| M3-08 | TODO | Verification/Distribution | Validate and package the standalone taskbar component. | Compile/import checks prove the native binaries have no WebView dependency; a disposable Explorer session covers enable/disable, crash recovery, DPI, auto-hide, theme, multi-monitor, Explorer restart, unsupported binaries, signed packaging, and rollback before default exposure. | `test(taskbar): validate standalone weather injection` |

M3-05 through M3-08 supersede the Windhawk runtime/package work in M3-03.
M3-03 remains as historical POC scope and must not be installed or enabled. The
protocol and app-side snapshot work already completed under M3-04 remains the
transport foundation for the standalone host.

### Audit Verification Baseline

- `dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet build Task_Flyout.csproj -c Release -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- Debug and Release unit runs: 800 passed, 0 failed, 0 skipped.
- Application and test dependency vulnerability scans: no known vulnerable packages from the configured source.
- `dotnet format Task_Flyout.slnx --verify-no-changes --no-restore`: failed on existing whitespace formatting; tracked by M2-07.
- Remote packaged smoke currently fails after publication rather than gating it; tracked by M0-01.

### 2026-08-07 Completion Verification

- `dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj --no-restore`: 877 passed, 0 failed, 0 skipped.
- `dotnet build Task_Flyout.csproj -c Release -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- The worktree is clean, and `credentials.json`, `Secrets.cs`, `*.pfx`, `bin/`, and `obj/` are not tracked.

### 2026-08-07 Windhawk Dock Verification

- Read-only inspection found Windows 11 Taskbar Styler 1.8 enabled with
  `Luminosity_variant_Dock`; the preset defaults are `DockMargin=250`,
  `DockMarginFix=500`, `DockHeight=58`, `DockTopGap=5`, and `bcr=10`.
- The current source maps that preset to client slot `(0, 5) 250x48` on a
  1920x58 taskbar, and the new diagnostics expose
  the resolved surface and slot after packaging.
- The Release x64 MSIX under `AppPackages\WindhawkCheck` was signed with the
  existing local sideload certificate and independently verified by SignTool and
  PowerShell. The installed package was not replaced during this pass, so no
  running tray state or user Windhawk settings were changed; screenshot
  verification remains pending installation approval.

### 2026-08-07 Windhawk Slot Fix Verification

- The left-reserve, obstacle clipping, and independent glass-surface fixes passed
  the full suite: 882 tests passed, with 0 failures or skips; Release x64 build
  completed with 0 warnings and 0 errors.
- A fresh signed package is available under `AppPackages\WindhawkFix`; SignTool
  verification succeeded with the existing certificate. It has not been
  installed over the currently running package yet.

### 2026-08-07 Native Widgets Mode Verification

- Added a persisted `WeatherBarMode` selector with a safe migration default of
  `TaskFlyout`; invalid values are normalized and native requests fall back to
  the existing bar.
- Added Windows Web Experience Pack and taskbar-entry detection, reversible
  `TaskbarDa` ownership, Explorer-restart recovery, localized settings/status
  text, and Store/taskbar-settings launch actions.
- Debug x64 build completed with 0 warnings and 0 errors; the full unit suite
  passed 916/916 after native handoff and TaskbarDa snapshot hardening.
- The Task Flyout bar now remains visible while Explorer materializes the native
  entry. Verification polls quickly for 10 seconds, then backs off to 30-second
  recovery checks; it also retries a failed cross-process TaskbarDa restoration.
- Native bridge confirmation now requires a visible left-side taskbar surface
  with plausible geometry. A failed custom HWND close keeps its object reference
  and hides/retries it instead of creating a duplicate child window.
- Runtime inspection of installed `1.4.2.0` confirmed that finance content is
  Windows Widgets' rotating announcement surface, not a stale Task Flyout bar.
  The UI now labels this mode as Windows-managed dynamic content and opens the
  Widgets board with instructions to disable taskbar announcements and badges.
- A separate, disabled-by-default Windhawk companion now proves that custom
  content can occupy the existing `AugmentedEntryPointContentGrid` without a
  second HWND or a second XAML Diagnostics consumer. Compile/link validation is
  complete; it has not been installed or loaded into Explorer. Runtime theme,
  unload, multi-monitor, IPC, and click-activation checks remain M3-03/M3-04.
- The current sandbox resolves as Windows 10 Pro 25H2 and has no registered
  Web Experience Pack for the sandbox SID, so a real native
  `AugmentedEntryPointButton` screenshot cannot be claimed here. Manual
  verification on a Windows 11 profile with Widgets enabled remains the release
  check; the ordinary app path intentionally does not inject XAML into Explorer.

### 2026-08-09 Windhawk Companion Compile Verification

- The experimental companion is disabled by default and remains outside the
  MSIX. It was not copied into Windhawk, loaded into Explorer, or enabled.
- The current allowlist targets Windows build `26200` and the loaded
  `Taskbar.View.dll` profile `TimeDateStamp=0x6A3CD591`,
  `SizeOfImage=0x0098B000`, `CheckSum=0x00985653`; an unknown profile fails
  before the internal FrameworkElement slot is read.
- `windhawk\build-companion.ps1 -Mode Syntax` passed with the real Windhawk
  compiler definitions, and `-Mode Link` produced an x64 DLL of 184,832 bytes
  with SHA-256
  `BF190118F9EBF1CC12B38D0796F170DC42EA0D7D7EB75C2D799CD7DE9D501622`.
- Owner-thread restoration uses a self-unhooking temporary callback. A helper
  owns an extra DLL reference and callers wait at most three seconds; if the
  taskbar is unresponsive, the helper remains valid until it can finish rather
  than blocking settings/unload indefinitely or calling unloaded code.
- Runtime validation is intentionally still pending under M3-03. Weather data
  IPC and Task Flyout click activation remain M3-04.

### 2026-08-09 Weather Companion Protocol Verification

- M3-04 now has a versioned JSON request/response contract with 4 KiB request
  and 16 KiB response limits. Commands are restricted to `ping`,
  `get-snapshot`, and `open-weather`; duplicate critical fields, malformed UTF-8,
  excessive nesting, and trailing JSON are rejected.
- Weather text is scalar-bounded before it reaches Explorer. C0/C1 and Unicode
  line separators cannot create taskbar line breaks, bidirectional override and
  isolate controls are removed, and emoji joiners/variation selectors remain
  intact.
- The 20 protocol-specific tests and the full 936-test suite pass. The Debug x64
  app build also completes with 0 warnings and 0 errors. The app-side server is
  verified below; the non-blocking Windhawk client remains the next M3-04 phase.

### 2026-08-09 Weather Companion Server Verification

- The default-off app bridge now owns a single first-instance named pipe scoped
  by current-user SID and Windows session, with `CurrentUserOnly` access,
  4-byte little-endian framing, one request per connection, and a two-second
  transaction deadline. The same server handle is reused between clients.
- Native-mode weather refresh no longer depends on the self-drawn weather
  window remaining alive. A background coordinator refreshes an immutable
  snapshot immediately, every 30 minutes, and after location changes; pipe
  handlers never perform network requests or manipulate WinUI.
- Shutdown cancels both pending pipe I/O and weather refresh. Integration tests
  cover oversize/truncated frames, same-user round trips, slow-client shutdown,
  and recovery by a second client after a partial-frame timeout.
- The 7 server-specific tests and full 943-test suite pass. Debug x64 builds with
  0 warnings and 0 errors. The Windhawk-side client is verified below; the
  visible experimental opt-in remains pending, so no Explorer process changed.

### 2026-08-09 Weather Companion Client Verification

- Windhawk companion version `0.2.0` now derives the same SID-and-session pipe
  name as the app, uses bounded overlapped I/O only on a retained background
  worker, validates the 16 KiB response before allocation, and parses the JSON
  through `Windows.Data.Json` before publishing an immutable UI snapshot.
- A valid snapshot updates the injected icon and compact temperature/condition
  text on each taskbar owner thread. Missing, malformed, unavailable, older-than-
  two-hour, or implausibly future data restores the untouched Windows Adaptive
  Card. Static preview injection is now a separate diagnostics-only setting that
  defaults off.
- Windhawk syntax and real x64 link both pass. The compile-only DLL is 206,848
  bytes with SHA-256
  `D485554023F0D077338941E2C1D096D07C36DE9C1B0872AC5A61931ADBC541A7`.
  It was not installed, enabled, or loaded into Explorer.
- The full .NET suite now passes 949/949 and Debug x64 still builds with 0
  warnings and 0 errors. Task Flyout click activation, localized app opt-in,
  owner-thread unload hardening, and runtime visual validation remain pending.

### 2026-08-09 Standalone Native Taskbar Direction

- Runtime inspection separates the taskbar surface from the Widgets board:
  Explorer loads `Taskbar.View.dll` plus the UWP Adaptive Cards renderer, while
  `Widgets.exe` loads `WebView2Loader.dll` and `EmbeddedBrowserWebView.dll`.
- The supported Windows Widgets API only hosts providers in the Widgets Board;
  it does not expose a taskbar-entry extension point. A weather button that is
  independent of Web Experience therefore requires a private Explorer host.
- The target design is a signed, per-user broker plus a narrowly scoped x64 host
  DLL. The host renders standard UWP XAML only, performs no network or WebView
  work, and reads sanitized snapshots through the existing named pipe.
- The current Windhawk POC remains compile-only reference material. It will not
  be installed, loaded, or extended as the product runtime path.

### 2026-08-10 Pure XAML Weather View

- Added a WebView-free `Windows.UI.Xaml` weather view composed only from
  `Grid`, `Border`, `StackPanel`, and `TextBlock` controls. It is intentionally
  hit-test transparent so a taskbar-owned button can retain hover, pressed,
  focus, and accessibility behavior.
- Added a native text boundary that removes control, line-separator, and bidi
  override/isolate characters, collapses whitespace, bounds all fields, and
  never splits a UTF-16 surrogate pair before text enters Explorer.
- The x64 Release host and broker compile, both native test executables pass,
  and import inspection finds no WebView or network library. The taskbar hook
  remains a no-op, so this stage did not load or modify Explorer.

### 2026-08-10 Standalone Taskbar Hook Profile

- The development `Taskbar.View.dll` PDB resolves
  `TaskbarFrame::OnTaskbarLayoutChildBoundsChanged` to RVA `0x001F3540`.
  Symbol lookup is an offline development step; no PDB or symbol service is
  used by the shipped host.
- Compatibility now requires the Windows build, PE timestamp/image
  size/checksum, target RVA, and exact 20-byte x64 prologue to match. The
  Explorer-side probe additionally holds a module reference and requires the
  loaded PE fingerprint plus an in-bounds executable memory region with the
  same bytes, so an updated, replaced, or already-patched target fails closed
  before MinHook or XAML mutation is reached.
- Release x64 compilation and both native tests pass. The entry hook remains a
  no-op and this verification did not load the host into Explorer.

### 2026-08-10 Default-off Detour Lifecycle

- Extracted the Explorer-side profile verifier so the runtime owns a retained
  `Taskbar.View.dll` module lease, checks the taskbar owner PID/thread, requires
  `MEM_IMAGE` executable target memory, and rechecks the loaded PE fingerprint
  and prologue immediately before MinHook creation and enable.
- Added a default-off detour state machine. It calls the original function
  first, counts active trampoline calls, blocks custom callbacks during stop,
  disables the hook before restoring, drains before removing, and enters
  quarantine on any restore/disable/drain/remove/uninitialize failure. A
  successfully activated host is pinned until Explorer exits; no dynamic
  remote DLL unload is attempted.
- The MinHook backend is pinned to the upstream x64 sources and now has a
  no-op compatibility test plus a four-step in-process smoke test. Release
  x64 build, four native tests, and WebView/network import inspection pass.
  The exported thread hook remains inert, so Explorer was not changed.

### 2026-08-10 Detour Shutdown Hardening

- The stop path now leaves the lifecycle lock before invoking XAML restoration
  or MinHook operations, so restoration can safely query runtime diagnostics
  without a lock inversion.
- After a successful disable, the original trampoline pointer is atomically
  cleared before active detours are drained. A thread that reached the detour
  late therefore cannot call a trampoline after it has been removed.
- Custom XAML callbacks have a separate drain counter and a C++ exception
  boundary. Restoration is also exception-isolated; any failure quarantines
  the pinned runtime instead of unloading executable code still referenced by
  Explorer.
- Release x64 compilation and all four native tests pass. The exported entry
  hook is still inert, so no DLL was loaded into Explorer during verification.

### 2026-08-10 Taskbar XAML Tree Probe

- Added a read-only, bounded probe for the Explorer taskbar visual tree. It
  requires the expected `Taskbar.TaskbarFrame` identity, one direct
  `RootGrid`, one direct `Taskbar.TaskbarBackground#BackgroundControl`, one
  direct `TaskbarFrameRepeater`, compatible geometry, and a shared `XamlRoot`.
- Duplicate, missing, oversized, detached, or unavailable trees fail closed;
  the probe never appends controls or changes Windows-owned properties.
- `LandmarksMatched` is deliberately not a mutation permit. A build-specific
  primary/secondary direct-child fingerprint is still required before the
  append-only mount can consume this probe.
- Added pure signature tests and included the probe in the native Release test
  matrix. Five native tests pass; the interactive taskbar remains unavailable
  in the build sandbox, so no Explorer runtime probe or injection was run.

### 2026-08-10 Append-only XAML Mount Lease

- Added a default-deny mount lease that creates only standard
  `Windows.UI.Xaml.Controls.Button`/`Grid` content and appends it to the
  probed `RootGrid`; no WebView, Adaptive Card, Widgets, or network code is
  involved in the view.
- The lease has explicit gates for a future build-specific direct-child
  allowlist, left-slot collision result, and XAML dispatcher thread access.
  Until those gates are supplied it returns `structure-not-allowlisted` or
  `left-slot-unavailable` and performs no mutation.
- Restore removes only the exact button object it appended, keeps state on
  failed removal, verifies the owned name/automation/content identity, and
  keeps only weak XAML references between callbacks. Six native Release tests
  pass; the host entry hook remains inert and Explorer was not loaded or
  changed.

### 2026-08-10 Default-deny Left Slot Geometry Policy

- Added a pure DIP geometry strategy for a left-anchored, vertically centered
  weather-button candidate. It accepts only a caller-proven current structure
  and blocker snapshot; either unknown flag returns `unknown-structure` and no
  actionable candidate.
- Frame, `RootGrid`, requested size, gap, and every blocker rectangle are
  finite and bounded. NaN, infinity, endpoint overflow, undersized slots, a
  root outside its frame, and more than 64 interactive blockers fail closed.
- Collision uses the requested safety gap on all four sides and distinguishes
  a usable candidate from an occupied one without reading or mutating XAML.
  It is not yet connected to the mount lease or entry hook, so this stage does
  not inject into or otherwise modify Explorer.

### 2026-08-10 Geometry-bound Mount Layout

- The mount lease now consumes a `TaskbarSlotGeometryInput`, re-reads the
  current frame/root dimensions on the owner thread, and refuses all slot
  statuses except `candidate-available`.
- The created standard XAML button uses the proven candidate rectangle for its
  width, height, and top/left margin; the old independent fixed margin is no
  longer a second layout authority. The entry hook is still inert until a
  live owner-thread blocker scanner supplies the structure proof and blocker
  span.

### 2026-08-10 Private TaskbarFrame ABI Bridge

- Added a default-off bridge for the development profile's private
  `TaskbarFrame` IInspectable slot. It requires an active exact-profile detour
  on the captured owner thread before reading any private object memory.
- The slot, interface object, and vtable are bounded with current-process
  memory queries; QueryInterface/AddRef/Release targets must be executable
  image code before
  C++/WinRT projection. The projected object must then match
  `Taskbar.TaskbarFrame` and confirm Dispatcher thread access.
- The bridge has a pure fail-closed gate test matrix. It is not connected to
  the exported entry hook and was not called against Explorer in this stage.

### 2026-08-10 Live Left-Slot Probe

- Added an owner-thread-only probe that accepts the exact two-child
  `RootGrid` fingerprint (`BackgroundControl` + `TaskbarFrameRepeater`) or the
  same tree plus the exact previously owned button. It rejects unknown root
  children and bounded-count violations.
- Realized repeater children are converted to RootGrid-space rectangles only
  after loaded/visibility/shared-XamlRoot/transform checks. The result owns a
  bounded blocker array and exposes a short-lived span to the geometry gate;
  it retains no XAML references after return.
- The probe is now compile-tested but not called by the inert host hook. A
  different taskbar build or an unobserved child shape remains default-deny.

### 2026-08-10 Guarded Host Controller Wiring

- Connected the verified frame bridge, live tree/slot probe, and reversible
  mount lease behind the default-off taskbar detour controller. The mount API
  now consumes the owner-thread `TaskbarSlotProbeResult` directly, so callers
  cannot supply an independent structure or blocker proof.
- Corrected the private ABI interpretation: slot 3 is the embedded
  IInspectable interface address. Raw `QueryInterface(IFrameworkElement)` is
  isolated behind an SEH boundary, the returned vtable is checked, and the
  bridge requires a TLS token for the exact active detour callback.
- The exported `WH_CALLWNDPROC` entry hook now accepts only the private start /
  stop control message and keeps the host inert otherwise. Release x64 build,
  import inspection, and all ten native tests pass. The broker's control-message
  sender, named-pipe weather snapshot wiring, and disposable Explorer-session
  validation remain pending; no Explorer injection was attempted in the build
  sandbox.

### 2026-08-10 One-shot Broker Control Transport

- Added explicit `start` and `stop` broker commands. Each command requires a
  supported disk-side profile, rechecks that the `Shell_TrayWnd` PID/thread did
  not change, verifies the adjacent host API, installs a thread-specific
  `WH_CALLWNDPROC` hook, and sends the private registered message with a bounded
  timeout.
- The temporary hook is removed after a synchronous dispatch. On timeout or
  removal failure the broker avoids an eager `FreeLibrary`; a successful start
  still relies on the host's existing self-pin before the hook is removed.
- Release x64, forbidden-import inspection, and all eleven native tests pass.
  The sandbox returns a structured `probe-rejected` result because it cannot see
  the interactive taskbar, so no Explorer injection was attempted. A controller
  acknowledgement channel and live named-pipe weather snapshots remain pending.

### 2026-08-10 Standalone Host Weather Snapshot Pipe

- The Explorer host now derives the same current-user SID and session-scoped
  `TaskFlyout.Weather.v1` pipe as the app and requests `get-snapshot` on a
  retained MTA worker. The XAML owner thread performs no pipe or JSON work.
- Framing is fixed at a four-byte little-endian header and a 16 KiB response
  limit. Connect and overlapped read/write waits are bounded by one query
  deadline; the stop event interrupts pending I/O and cancellation is drained
  before stack state is released. Strict UTF-8, protocol version/status,
  required temperature, and UTC freshness are checked before the existing
  native text sanitizer creates the immutable view model. Alert text takes
  precedence over the ordinary description when the server supplies it.
- Snapshots older than two hours or more than five minutes in the future are
  rejected. Missing, malformed, unavailable, stale, or failed queries clear a
  previously published model and request relayout instead of displaying stale
  weather indefinitely.
- Controller stop signals and joins the worker with a two-second owner-thread
  bound before XAML restoration and detour removal. JSON response, alert,
  freshness, length, cancellation, and status policy tests now accompany the
  existing native matrix. Release x64 compilation, forbidden-import inspection,
  and all twelve native tests pass. The build sandbox still cannot access an
  interactive taskbar, so no Explorer injection or live pipe round trip was
  attempted.
