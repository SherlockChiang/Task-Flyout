# Performance Baseline Checklist

Use this checklist before and after performance-sensitive changes. Record results in the PR or release notes so optimizations have comparable numbers.

## Environment

- Build: `Debug` or packaged release, include commit SHA.
- Machine: CPU, RAM, Windows version, display scaling, power mode.
- Network: online/offline, VPN/proxy status, approximate latency if relevant.
- Accounts: connected providers enabled for the run, with private account names omitted.
- Data size: approximate mail folders/messages, RSS subscriptions/articles, calendars/tasks.

## Metrics

| Area | How to Measure | Record |
| --- | --- | --- |
| Cold startup | Start app after process exit. Use `%LOCALAPPDATA%\Packages\TaskFlyout*\RoamingState\Logs\startup.csv` if packaged, or the app roaming log path used by the dev run. | `totalMs`, key startup marks, visible tray time. |
| Tray idle memory | Wait 60 seconds with flyout closed. Capture Task Manager working set/private bytes. | Working set, private bytes, CPU idle %. |
| Flyout first open | Start app, open flyout once after tray appears. Measure stopwatch or screen recording. | Time to visible shell, time to populated agenda. |
| Mail first HTML render | Open Mail page and first HTML message after cold start. | Time to list, time to body visible, WebView2 initialization delay. |
| RSS article open | Open RSS page, select article with images disabled and then enabled. | Time to text visible, image fetch completion, blocked image count. |
| Weather refresh | Trigger manual refresh for configured city/current location. | Time to current conditions, source used, error/fallback status. |

## Procedure

1. Close existing Task Flyout processes.
2. Clear only measurement noise if needed; do not clear app data unless the test explicitly says cold profile.
3. Run each scenario three times.
4. Record median and worst run.
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

The soak samples process handles, working set, private memory, and threads every 10 seconds. It writes `TestResults\packaged-soak.csv` and fails when median handle growth exceeds 25 or median private-memory growth exceeds 64 MB. Override duration and limits with `TASKFLYOUT_SOAK_MINUTES`, `TASKFLYOUT_SOAK_MAX_HANDLE_GROWTH`, and `TASKFLYOUT_SOAK_MAX_PRIVATE_MB_GROWTH`.

The `Quality` workflow runs unit/resource checks for pull requests, packaged smoke after a successful beta release, and packaged soak on its weekly schedule or manual dispatch.
