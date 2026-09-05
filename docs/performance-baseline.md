# Performance Baseline

Use this checklist before and after performance-sensitive changes. Record results in the PR or release notes so optimizations have comparable numbers.

## Environment

- Build: `Debug` or packaged release, include commit SHA.
- Machine: CPU, RAM, Windows version, display scaling, power mode.
- Network: online/offline, VPN/proxy status, approximate latency if relevant.
- Accounts: connected providers enabled for the run, with private account names omitted.
- Data size: approximate mail folders/messages, RSS subscriptions/articles, calendars/tasks.

## Automatic Diagnostics

Performance diagnostics are off by default. Enable them with the local setting `PerformanceDiagnosticsEnabled=true` or set `TASKFLYOUT_PERFORMANCE_DIAGNOSTICS=1` before starting the process. Restart the app after changing either option.

Flyout preloading is off by default and can be enabled under **Settings > System & General**. The preload runs after startup only when the app is below the memory-usage ceiling and has sufficient headroom; memory pressure skips it.

Each process gets a random `run_id`. Records are appended in the background to `%LOCALAPPDATA%\TaskFlyout\Logs\performance-diagnostics.csv` (or the corresponding packaged local app-data location). The log rotates to one `.1` backup at 2 MB. Exit performs a best-effort two-second flush.

The long-form CSV schema is `run_id,sequence,timestamp,scenario,metric,start,end,duration,outcome,source`. These fields contain fixed diagnostic identifiers and timings only. Do not add account names, message/feed/calendar content, URLs, locations, identifiers, or arbitrary content fields.

Captured milestones include process start to interactive tray, first flyout display, Calendar cached display and actual remote completion, Mail folders and successful HTML render, RSS successful article display, process-wide WebView2 initialization, and Weather Bar attachment/display. Calendar cache short-circuits are intentionally excluded from the remote-sync metric.

Collect at least 20 independent runs, then summarize one or more current/rotated files:

```powershell
.\scripts\summarize-performance.ps1 "$env:LOCALAPPDATA\TaskFlyout\Logs\performance-diagnostics.csv"
```

The summarizer uses nearest-rank percentiles. It always reports P50, reports P95 only with at least 20 successful samples, and includes success, failure, and per-run missing counts where the log permits them.

## Metrics

### Planned Responsiveness Pass (2026-09-05)

The user accepted the functional tray behavior in Debug package `1.4.4.4`.
Keep that artifact as a regression reference, not a Release performance result.
F0-01 is planned: no timings or frame-pacing measurements have been collected for
this pass, and the metrics below are requirements, not currently implemented
diagnostic fields.

Existing instrumentation needs care: `MarkOnce` records a zero-duration milestone
once per process; `tray_click_to_open_request` ends after requesting a toggle.
Neither measures repeat-open input-to-visible latency. The current summarizer
groups only by scenario/metric/source, so do not feed it mixed build versions,
configurations, cold/warm runs or power settings and call that a comparable P95.

| Scenario | Separate observations | Required regression check |
| --- | --- | --- |
| Cold tray / first Flyout | Process-to-tray; command-to-first feedback; command-to-cached-content; remote completion separately. | No immediate close; cold hydration cannot defeat double-click. |
| Warm Flyout reopen | Repeat after short and long hidden intervals; efficiency transition cost, trim attempts, working set/private bytes, page-fault deltas where available. | Single-click toggles, double-click opens main, outside click dismisses; preserve pinning semantics. |
| Global search | Command-to-search-shell, immutable snapshot/index cost, last keystroke-to-results, canceled/superseded jobs. | Input and close remain responsive; account isolation and query generation preserved. |
| Calendar | Month/week navigation, background publication apply, changed-day count, layout work and actual presentation stalls while scrolling. | Keep current date/scroll/editor and both event endpoints; no full rebuild for unchanged data. |
| Mail list | Folder switch, load more, local filtering and unread-state apply; collection resets and selection changes. | No scroll/selection jump, unread convergence and correct-account notification navigation. |
| Mail/RSS detail | HTML preparation, WebView environment initialization, navigation-ready and network separately. | No stale selected message/article, unchanged sanitization and remote-image blocking. |

Protocol for F0-01 implementation and subsequent experiments:

1. Build a signed Release control with the accepted source behavior, then a
   single-change treatment with the same diagnostics. Record both source SHAs,
   package versions/hashes, data sizes, DPI/refresh rate and power/prewarm settings.
   Do not overwrite an installed build, alter settings or clear user data just
   to create a baseline without the corresponding implementation/test step.
2. Add bounded, opt-in spans for each operation, using monotonic elapsed time and
   opaque operation generations. UI callbacks only capture timings/counters;
   file output remains off-thread. Do not log account IDs, titles, text or HTML.
   Compare diagnostics on/off to detect measurement overhead.
3. Record physical-input latency separately from command-dispatch latency when
   collecting input traces. The tray library's double-click recognition delay
   must not be silently excluded from reported end-to-end response, or changed
   as a shortcut to a faster metric.
4. Keep cold and warm scenarios separate. Use at least 20 successful samples per
   scenario for an initial P95 screen; retain failure/cancel/missing counts and
   collect more samples before treating small tail differences as reliable.
   Never pool Debug and Release or different fixtures into one percentile.
5. Use short UI/CPU/GC/presentation traces for slow cases. Dispatcher queue delay
   is a responsiveness proxy, not frame rate; actual frame intervals must come
   from a presentation trace. A proposed first-feedback target is about 100ms
   after command receipt, with application UI work over 50ms prioritized for
   investigation. Calibrate these targets after the baseline; network and
   configured gesture-recognition delays are separate costs.
6. Compare unchanged control/treatment scenarios as well as the target. Keep
   normal foreground priority and bounded idle resource use; explicitly report
   any memory/latency tradeoff. Run the existing idle soak separately, then a
   bounded repeated-open/navigation scenario to expose churn. No default global
   WebView prewarm, forced GC or unconditional page caching.

### Existing Measurement Checklist

| Area | How to Measure | Record |
| --- | --- | --- |
| Cold startup | Start app after process exit with performance diagnostics enabled. | `startup/tray_interactive` duration. |
| Tray idle memory | Wait 60 seconds with flyout closed. Capture Task Manager working set/private bytes. | Working set, private bytes, CPU idle %. |
| Flyout first open | Start app, open flyout once after tray appears. Measure stopwatch or screen recording. | Time to visible shell, time to populated agenda. |
| Mail first HTML render | Open Mail page and first HTML message after cold start. | Time to list, time to body visible, WebView2 initialization delay. |
| RSS article open | Open RSS page, select article with images disabled and then enabled. | Time to text visible, image fetch completion, blocked image count. |
| Weather refresh | Trigger manual refresh for configured city/current location. | Time to current conditions, source used, error/fallback status. |

## Procedure

1. Close existing Task Flyout processes.
2. Clear only measurement noise if needed; do not clear app data unless the test explicitly says cold profile.
3. Run each scenario at least 20 times when establishing an automatic P95 baseline.
4. Record nearest-rank P50/P95 plus success, failure, and missing counts.
5. Note any external service failure instead of hiding it.

## Guardrails

- Do not include account names, email subjects, message bodies, OAuth URLs, tokens, or exact home/work locations in notes.
- If logs are attached, run them through the diagnostic redaction path or manually inspect them first.
- Compare packaged release numbers separately from `Debug`; WinUI/WebView2 startup behavior can differ significantly.

## Automated Packaged Checks

Run the installed-package smoke test after installing a current x64 package:

```powershell
.\scripts\test-packaged-smoke.ps1
```

The smoke test uses invariant UI Automation IDs to verify package activation, localized accessible navigation names, keyboard invocation of Calendar, Tasks, and Mail, the 640 x 700 narrow Calendar layout, and close-to-tray reactivation.

Run the default 10-minute soak with:

```powershell
.\scripts\test-packaged-soak.ps1
```

The soak activates the package directly into its tray-idle state, waits 60 seconds for normal runtime initialization, then samples process handles, working set, private memory, and threads every 10 seconds. It writes `TestResults\packaged-soak.csv` and fails when 10-minute median handle growth exceeds 150 or median private-memory growth exceeds 64 MB. The handle guard includes the packaged .NET 10 and Windows App SDK runtime's observed thread-handle churn; established local and hosted-runner baselines were 81-123 handles with stable or declining private memory. Override duration and limits with `TASKFLYOUT_SOAK_MINUTES`, `TASKFLYOUT_SOAK_MAX_HANDLE_GROWTH`, and `TASKFLYOUT_SOAK_MAX_PRIVATE_MB_GROWTH`.

The beta workflow runs packaged smoke against the exact candidate payload before entering the signing environment or publishing. The `Quality` workflow runs unit/resource checks for pushes and pull requests, and manually or weekly rechecks the latest published beta with packaged smoke and soak.
