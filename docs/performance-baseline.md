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
