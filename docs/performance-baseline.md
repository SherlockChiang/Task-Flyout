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

Each process gets a random `run_id`. Records are appended in the background to `%LOCALAPPDATA%\TaskFlyout\Logs\performance-diagnostics.csv` (or the corresponding packaged local app-data location). The log rotates to one `.1` backup at 2 MB. Exit performs a best-effort two-second flush.

The long-form CSV schema is `run_id,sequence,timestamp,scenario,metric,start,end,duration,outcome,source`. These fields contain fixed diagnostic identifiers and timings only. Do not add account names, message/feed/calendar content, URLs, locations, identifiers, or arbitrary content fields.

Captured milestones include process start to interactive tray, first flyout display, Calendar cached display and actual remote completion, Mail folders and successful HTML render, RSS successful article display, process-wide WebView2 initialization, and Weather Bar attachment/display. Calendar cache short-circuits are intentionally excluded from the remote-sync metric.

Collect at least 20 independent runs, then summarize one or more current/rotated files:

```powershell
.\scripts\summarize-performance.ps1 "$env:LOCALAPPDATA\TaskFlyout\Logs\performance-diagnostics.csv"
```

The summarizer uses nearest-rank percentiles. It always reports P50, reports P95 only with at least 20 successful samples, and includes success, failure, and per-run missing counts where the log permits them.

## Metrics

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
