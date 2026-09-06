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

Each process gets a random `run_id`. New records are appended in the background to `%LOCALAPPDATA%\TaskFlyout\Logs\performance-diagnostics-v2.csv` (or the corresponding packaged local app-data location). The log rotates to one `.1` backup at 2 MB. Pending writes, including the active write, are capped at 256; overflow drops new records without waiting for disk. Exit performs a best-effort two-second flush with a final writer-health record. Abrupt exit or unavailable storage can still lose the tail.

The v2 CSV retains `run_id,sequence,timestamp,scenario,metric,start,end,duration,outcome,source` and adds `schema_version,build_id,build_version,configuration,architecture,operation_id,sample_kind,timing_basis,dropped_before,write_failures_before`. `build_id` is the application assembly MVID, not just the source SHA, so locally changed binaries are not assumed identical. Archive the source SHA and signed-package hash separately. Operation IDs are random diagnostic correlations, never provider/user IDs. Do not add account names, message/feed/calendar content, query text, URLs, locations, tokens, or arbitrary content fields.

Captured milestones include process start to interactive tray, first flyout display, Calendar cached display and actual remote completion, Mail folders and successful HTML render, RSS successful article display, process-wide WebView2 initialization, and Weather Bar attachment/display. Calendar cache short-circuits are intentionally excluded from the remote-sync metric.

The summarizer reads both original ten-column logs and v2 logs; old files are not rewritten. By default runs stay separate. After confirming identical fixture, display, power, prewarm and diagnostic settings, use `-CombineRuns` to combine matching v2 build/configuration/architecture/scenario/source/sample-kind/timing-basis groups. Legacy records always stay per-run because their build identity is unknown. For initial P95 screening, collect at least 20 successful samples per comparable group (independent process runs for first attempts):

```powershell
.\scripts\summarize-performance.ps1 "$env:LOCALAPPDATA\TaskFlyout\Logs\performance-diagnostics-v2.csv"
# Only for verified comparable runs; include the .1 file when needed:
.\scripts\summarize-performance.ps1 -Path "$env:LOCALAPPDATA\TaskFlyout\Logs\performance-diagnostics-v2.csv" -CombineRuns -AsObject
```

The summarizer uses nearest-rank percentiles; P95 requires at least 20 successful samples. Started/cancelled/superseded/ignored/failed operations do not enter successful-duration percentiles. Unfinished operations, sequence gaps or reported writer losses suppress percentiles for affected groups. It does not infer that every process must perform every action. It rejects invalid durations/conflicting duplicate rows, deduplicates repeated input files, and does not assign duration percentiles to v2 milestones/status records. The `-AsObject` output retains all grouping fields and outcome counts. Run `scripts/test-performance-summary.ps1` for synthetic regression checks, not runtime performance results.

### Implemented Interaction Slice (2026-09-06)

| Scenario | Cumulative observations from command/input-handler entry | Terminal observation |
| --- | --- | --- |
| `flyout_open` | `command_to_efficiency_restored`, `command_to_accounts_ready`, `command_to_cached_content_applied`, `command_to_open_state` | `command_to_rendering` |
| `search_open` | `command_to_snapshot_ready`, `command_to_results_applied` | `command_to_rendering` |
| `search_query` | `input_to_results_applied` | `input_to_rendering` |

Checkpoints share an operation ID and monotonic start; they are cumulative,
not individual phase durations. Each operation records `started`, then at most
one terminal outcome. First attempts per scenario/process are `cold_attempt`,
subsequent attempts `warm_attempt`, even if the first was cancelled. These labels
do not assert a cold disk cache or successful prewarm. Hidden interval and power
conditions must still be recorded separately in the measurement protocol.

`Rendering` callbacks are pre-presentation observations, not evidence that a
particular frame reached the screen. Visibility/lifecycle guards reject closed
or superseded observations; closing releases the event subscription. The Flyout
command begins before efficiency restore, account hydration and construction,
but after the tray library's gesture-recognition wait. Search input starts at
the user TextChanged handler, not the physical keystroke. Actual input-to-photon
latency, independent shell feedback, cached visual presentation, remote sync,
UI stalls, allocations/GC and frame pacing still need the remaining F0-01 work.

## Metrics

### Local Release Control Builder

The first signed control is ready: [1.4.4.5 artifact and pending runtime protocol](performance-control-1.4.4.5.md).
It has not been installed or measured by this pass.

Use a committed implementation with the existing current-user signing certificate:

```powershell
.\scripts\build-performance-control.ps1 -Version 1.4.4.5 -CertificateThumbprint 15303040F7CEECFDE9C023F15481D15C39CCDB46 -DescribeOnly
.\scripts\build-performance-control.ps1 -Version 1.4.4.5 -CertificateThumbprint 15303040F7CEECFDE9C023F15481D15C39CCDB46
```

The builder generates a separate manifest under `.testbuild`, selects it through
the opt-in `TaskFlyoutPackageManifest` project property, and verifies the evaluated
manifest before building. Normal builds retain the original manifest and SDK
import chain. Each output directory includes the requested version and source
commit; existing output is never overwritten. The only permitted tracked local
differences are the user's manifest and archived Windhawk companion, which are
hash-checked for preservation. Ignored local OAuth build inputs remain local and
must not be copied into measurement notes.

This is a Release x64, self-contained .NET, non-trimmed control with the project's
existing ReadyToRun setting. The normal sideload hook signs bundled native
components; it does not run them or probe Explorer. Package signing uses only the
specified existing certificate. There is no certificate creation, private-key
export, trust-store change, installation, running-process termination or setting
change. Diagnostics remain opt-in in the resulting package.

`scripts/test-performance-package-plan.ps1` checks the read-only plan, invalid
inputs, alternate/default manifest selection, SDK import chain and source
manifest preservation without signing or installing. Actual package signature,
manifest version, build identity and payload hashes require separate verification
after the build finishes.

### Planned Responsiveness Pass (2026-09-05)

The user accepted the functional tray behavior in Debug package `1.4.4.4`.
Keep that artifact as a regression reference, not a Release performance result.
F0-01 is in progress: the first instrumentation slice above exists, but no runtime
timings or frame-pacing measurements have been collected for this pass. The
metrics below remain requirements beyond the explicitly implemented fields.

Existing instrumentation needs care: `MarkOnce` records a zero-duration milestone
once per process; `tray_click_to_open_request` ends after requesting a toggle.
Neither measures repeat-open input-to-visible latency. The v2 observations add
command correlation and separate build/sample groups, but still cannot be
treated as actual frame presentation or pooled across differing power/fixtures.

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
