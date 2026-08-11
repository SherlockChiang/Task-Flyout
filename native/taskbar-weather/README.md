# Task Flyout Standalone Taskbar Weather

This directory contains the x64 native components for the experimental
WebView-free Windows 11 taskbar weather path.

- `TaskFlyout.TaskbarBroker.exe` runs as the current desktop user, verifies the
  real Explorer taskbar owner, and gates private taskbar integration on an exact
  Windows and `Taskbar.View.dll` PE profile.
- `TaskFlyout.TaskbarHost.dll` is the narrowly scoped in-process Explorer host.
  It now contains a standard `Windows.UI.Xaml` weather view composed from a
  `Grid`, `Border`, `StackPanel`, and `TextBlock` controls. The exported
  `WH_CALLWNDPROC` entry hook accepts only the private start/stop/status control
  message; without that message the host remains inert. The controller is
  default-off and starts the detour only on the taskbar owner thread.
- The host also builds a reversible, append-only mount lease for a standard
  XAML `Button`. It requires an exact taskbar-tree profile, dispatcher thread
  access, a build-specific structure allowlist, and a proven free left-side
  slot. The latter two gates intentionally have no permissive default, so the
  lease cannot mutate Explorer until the runtime scanner supplies evidence.
- The native binaries must not link WebView, browser, or network libraries.
  Weather networking and cache ownership remain in the Task Flyout app; the
  host consumes only the existing sanitized named-pipe snapshot protocol and
  sends the app-owned `open-weather` activation command.

Build and run the compile-only checks from the repository root:

```powershell
.\scripts\build-native-taskbar-weather.ps1 -Configuration Release
```

The script configures an x64 Visual Studio build under `.testbuild`, runs the
native profile tests, checks imports for WebView/network dependencies, and
performs a read-only probe of the current Explorer taskbar. It never installs or
loads the host into Explorer.

After a supported probe, the broker can dispatch one explicit control command:

```powershell
.\TaskFlyout.TaskbarBroker.exe start
.\TaskFlyout.TaskbarBroker.exe status
.\TaskFlyout.TaskbarBroker.exe stop
```

These commands mutate the current Explorer process. Run them only in a
disposable Explorer session. The repository harness requires two explicit
opt-ins, validates matching Authenticode signatures by default, polls bounded
mount status, and attempts idempotent stop after every attempted start. Its safe
description path does not launch the Broker:

```powershell
.\scripts\test-standalone-taskbar-weather.ps1 -DescribeOnly
```

See `docs/manual-verification.md` for the runtime matrix and the full opt-in
invocation. The harness never installs a package, changes `TaskbarDa`, kills
Explorer, or restarts it.

Each command revalidates the taskbar owner and PE profile, installs a temporary
thread-specific `WH_CALLWNDPROC` hook, sends the registered control message, and
removes the hook before exiting. The request carries a per-dispatch nonce and a
message-only broker window. The Explorer hook posts one bounded controller
status back to that exact window; the broker waits at most one second after hook
removal and reports `acknowledged`, `controller-rejected`, timeout, or malformed
reply separately. An acknowledged start also returns that nonce so the app can
bind later pipe reports to the exact Explorer process and controller generation.
No pointer is dereferenced across processes, and Explorer never waits on the
broker.

`status` is a fail-closed readiness query. It returns `mount-ready` only after
the controller revalidates, on the taskbar XAML owner thread, that the exact
owned Button is still a loaded, visible, arranged child of the live RootGrid
with its click handler and view identity intact. Historical mount state,
controller startup, and a merely appended but unarranged element report
`mount-pending`; an inactive controller reports `not-started`.

The initial allowlist is intentionally limited to the development profile:

- Windows build `26200`
- `Taskbar.View.dll` timestamp `0x6A3CD591`
- image size `0x0098B000`
- checksum `0x00985653`
- `TaskbarFrame::OnTaskbarLayoutChildBoundsChanged` RVA `0x001F3540`
- exact 20-byte x64 function prologue

The broker validates the PE profile and on-disk prologue. The Explorer host
then holds a module reference and validates the loaded PE fingerprint, image
bounds, executable page, and in-memory prologue again. Any mismatch fails
before a detour is created or taskbar XAML is changed. The private
`IInspectable` slot is used only by this exact profile.

The XAML view has no background surface of its own. It is designed to sit
inside a taskbar-owned button so that Windows continues to own hover, pressed,
focus, sizing, and accessibility visuals. Incoming strings are bounded and
sanitized before they reach Explorer; the native view never accepts arbitrary
XAML, URI, image, script, or HTML content.

After the guarded detour starts, a retained MTA worker polls the current-user,
current-session `TaskFlyout.Weather.v1` named pipe every 15 seconds. The worker
uses 4-byte little-endian framing, a 16 KiB response limit, overlapped I/O with
bounded waits and an interruptible stop event, strict UTF-8 conversion,
version/status validation, and the server-provided UTC timestamp. Cancelled
overlapped operations are drained before their stack state is released. Data
older than two hours or more than five minutes in the future is rejected. Only
the sanitized icon, temperature, and alert-or-condition model crosses to the
taskbar layout callback; the worker never calls XAML. Missing, malformed,
unavailable, or stale data clears the cached model and requests a taskbar
relayout. The XAML Button click handler captures only a stateless signal
callback. It coalesces repeated clicks through an auto-reset event; the same
MTA worker performs one non-retried `open-weather` exchange, so Explorer's UI
thread never opens the pipe or parses JSON. The XAML owner thread also
revalidates the complete mount identity after each guarded layout callback and
publishes only a numeric ready/lost snapshot. The worker reports state changes
over the same pipe and requests another asynchronous taskbar layout pass every
five seconds; it renews ready only after a newer owner-thread proof. A ready
observation older than twelve seconds is downgraded to lost instead of being
replayed. The app accepts a
report only when the kernel-reported pipe client PID and the start nonce match
its current controller generation. Controller shutdown first revokes that
generation, rejects new activation signals, then signals and joins the worker
with a two-second owner-thread bound. It still attempts to revoke every Click
token and restore the owned XAML lease if the worker misses that bound;
incomplete cleanup is reported as a rejected stop rather than discarded state.

The private `Taskbar.View.dll` detour uses the x64 subset of MinHook `v1.3.4`,
pinned to commit `c3fcafdc10146beb5919319d0683e44e3c30d537`. It is built as a
static implementation detail and is connected to the guarded host controller;
the broker supplies the explicit one-shot control-message sender. Upstream
provenance and the BSD license are retained under `third_party/minhook`.

The controller's first safe lifecycle pins the host DLL for the lifetime of
Explorer after activation, retains a `Taskbar.View.dll` lease until the
trampoline is drained and removed, and quarantines rather than unloading on a
failed disable/remove/restore. This avoids remote `SetWindowsHookEx` unload
races; dynamic DLL unloading is deliberately deferred until a disposable
Explorer-session test exists.

The private TaskbarFrame pointer is not projected directly. A separate bridge
first requires the active exact-profile detour, the owner thread, and a TLS
token proving the exact callback scope. It treats slot 3 as the embedded
IInspectable interface address, bounds the vtable with current-process memory
reads, invokes `QueryInterface(IFrameworkElement)` inside an SEH boundary, and
verifies the returned vtable before transferring ownership to C++/WinRT. The
projected object must then match `Taskbar.TaskbarFrame` and its XAML dispatcher.
The entry hook remains inert until an explicit control message, so this path is
compile-tested and not automatically injected by the build script.

The live slot probe accepts only the development tree's exact direct-child
fingerprint (`BackgroundControl` plus `TaskbarFrameRepeater`, and optionally
the exact button identity already owned by the lease). It snapshots bounded
realized repeater child rectangles in RootGrid coordinates and feeds the
geometry gate. Unknown children, invalid transforms, or a changed tree fail
closed before append/update.
