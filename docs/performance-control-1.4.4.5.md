# Release Control 1.4.4.5

Status: packaged and statically verified; not installed or runtime-tested in
this pass. No interaction percentiles or performance improvement are claimed.
The accepted Debug `1.4.4.4` remains a functional reference, not a comparable
Release timing baseline.

## Artifact Identity

- Source commit: `38ce24032c981a36f10cfa2fbf2b8d1ede8d0fd2`.
- Instrumentation implementation: `107a82d791d8ca10ce8c26d4b4f3e32fa314976b`.
- Package:
  `AppPackages/PerformanceControl_1.4.4.5_38ce240/Task_Flyout_1.4.4.5_x64_Test/Task_Flyout_1.4.4.5_x64.msix`.
- Package SHA-256:
  `A655EC4F2D6DFBF0E196E144BA99C3053F469F0CFD584AFE723DD36254B6D9C4`.
- Package size: 123,503,409 bytes.
- Manifest: `Uranus92.TaskFlyout`, version `1.4.4.5`, architecture `x64`.
- Publisher: `CN=FDE9F71C-A397-410B-81EE-36E18F4325E5`.
- Existing signer: `15303040F7CEECFDE9C023F15481D15C39CCDB46`.
  SignTool and Authenticode verification passed. This local signature has no
  timestamp and is not a public release signature.
- Main assembly configuration: `Release`.
- Main assembly informational version:
  `1.0.0+38ce24032c981a36f10cfa2fbf2b8d1ede8d0fd2`.
- Main assembly MVID / v2 `build_id`: `0af25b29bbde4fb7932910b4f774e409`.
- .NET SDK used: `10.0.400`. Packaging used Visual Studio x64 MSBuild,
  self-contained .NET, ReadyToRun enabled and trimming disabled.

The package map points to the current `obj/x64/Release/net10.0-windows10.0.26100.0/win-x64/R2R/Task_Flyout.dll`.
Its hash matches the packaged DLL below. The ordinary `bin` DLL is pre-ReadyToRun
and has a different hash; it must not be used to claim a stale package.

| Payload | Packaged SHA-256 |
| --- | --- |
| `Task_Flyout.dll` | `77D3B8E3B515400D044E16299981FB4A4620DC18D53F8863E5E8D9FB05792908` |
| `TaskFlyout.TaskbarBroker.exe` | `4AF5FED61ED76E30DEFB75AC1BCE4A118722FABFBE0870DB3BD570B9F70EFAFD` |
| `TaskFlyout.TaskbarHost.dll` | `D429212C9BEFCCD774D5828C82E7E868CF4F0F6AB7A9D64318AA8004632FEF48` |

Both native payloads match the Release build outputs and have valid signatures
from the same existing certificate. They were not run, loaded into Explorer or
used for a compatibility probe. No key was created/exported and no trust store
was changed. The user's source manifest remains `1.4.4.0`; its hash and the
pre-existing Windhawk source hash are unchanged.

## Completed Verification

- Release application/MSIX build and signature verification passed.
- Normal Debug x64 build: zero warnings, zero errors.
- Release unit tests: 1283 passed, zero failed/skipped.
- Package-plan tests: 13 checks under both PowerShell 7 and Windows PowerShell.
- Summary regression tests: 20 synthetic checks passed; not performance samples.
- Manifest and PE metadata were inspected without executing the application.

## Next Runtime Session

Installation and application restart are separate user-controlled steps. Do not
run the generic latest-package installer as a measurement step: it may sign,
change trust and force-stop an existing process. Preserve user data/settings and
use this exact artifact, not whichever package has the newest file timestamp.

1. After installation is approved, exit Task Flyout normally and install this
   exact package. Record the installed version before testing. Keep the accepted
   Debug package available for functional comparison; do not pool its timings.
2. First check single-click open/toggle, double-click main-window handoff, outside
   dismissal without first clicking inside Flyout, and search open/input/close.
   Any regression prevents treating this as a usable performance control.
3. Record power mode, scaling/refresh rate, prewarm/efficiency preferences,
   approximate data volume and online/offline state without account names or
   content. These settings are not automatically recorded by v2 diagnostics.
4. For diagnostic runs, enable the existing local opt-in or start the actual app
   process with `TASKFLYOUT_PERFORMANCE_DIAGNOSTICS=1`. A shell/package activation
   may not inherit a changed terminal environment. Before collecting anything,
   confirm that a new v2 `run_id` has this document's `build_id` and `Release`
   configuration. Stop and resolve the activation path if no matching log appears.
5. Keep scenarios in separate process sessions: first open after restart; warm
   reopen after a fixed short hidden interval (for example 2 seconds); reopen
   after a fixed longer interval (for example 60 seconds); search open; search
   input. Use the same intervals and fixture for future treatments. First
   attempts are excluded from warm groups even when cancelled.
6. Collect at least 20 successful samples per comparable group before P95.
   Cold-attempt P95 requires independent process starts, not 20 reopens. Keep
   cancelled/ignored/superseded/unfinished records and both rotated log files.
   Compare diagnostic-on and diagnostic-off behavior before using small timing
   differences to justify changes.
7. Summarize with `scripts/summarize-performance.ps1 -Path <selected logs> -AsObject`.
   Only add `-CombineRuns` after checking matching fixture/settings. Rendering
   callbacks are pre-presentation observations; physical-input latency and frame
   pacing still require separate traces. GC/allocations/dispatcher stalls and
   memory-after-hide observations remain pending parts of F0-01.

## Installation Preflight

- The user approved installing this control and restarting for regression/sampling.
- Read-only preflight found installed `1.4.4.4` with package status `Ok`, still
  running from its installed WindowsApps directory. The target MSIX hash still
  matches the artifact identity above.
- Windows Computer Use returned no Task Flyout window or tray surface, so the
  app's normal tray Exit command could not be invoked or unsaved UI state
  inspected. No second app instance was launched to work around the hidden UI.
- Installation is paused until the user exits Task Flyout through its tray menu.
  No force-stop, installation, restart, setting change or timing collection was
  performed. Resume by confirming the old process has exited, then install the
  already-approved exact artifact. Do not treat this preflight as UI acceptance.
