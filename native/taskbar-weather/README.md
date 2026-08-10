# Task Flyout Standalone Taskbar Weather

This directory contains the x64 native components for the experimental
WebView-free Windows 11 taskbar weather path.

- `TaskFlyout.TaskbarBroker.exe` runs as the current desktop user, verifies the
  real Explorer taskbar owner, and gates private taskbar integration on an exact
  Windows and `Taskbar.View.dll` PE profile.
- `TaskFlyout.TaskbarHost.dll` is the narrowly scoped in-process Explorer host.
  It now contains a standard `Windows.UI.Xaml` weather view composed from a
  `Grid`, `Border`, `StackPanel`, and `TextBlock` controls. The thread hook is
  still a no-op, so this stage does not install or modify the taskbar tree.
  The separately tested detour controller is default-off and is not reached
  by the exported hook callback yet.
- The host also builds a reversible, append-only mount lease for a standard
  XAML `Button`. It requires an exact taskbar-tree profile, dispatcher thread
  access, a build-specific structure allowlist, and a proven free left-side
  slot. The latter two gates intentionally have no permissive default, so the
  lease cannot mutate Explorer until the runtime scanner supplies evidence.
- The native binaries must not link WebView, browser, or network libraries.
  Weather networking and cache ownership remain in the Task Flyout app; the
  host will consume only the existing sanitized named-pipe snapshot protocol.

Build and run the compile-only checks from the repository root:

```powershell
.\scripts\build-native-taskbar-weather.ps1 -Configuration Release
```

The script configures an x64 Visual Studio build under `.testbuild`, runs the
native profile tests, checks imports for WebView/network dependencies, and
performs a read-only probe of the current Explorer taskbar. It never installs or
loads the host into Explorer.

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

The future private `Taskbar.View.dll` detour uses the x64 subset of MinHook
`v1.3.4`, pinned to commit `c3fcafdc10146beb5919319d0683e44e3c30d537`.
It is built as a static implementation detail and is not yet connected to the
host in this stage. Upstream provenance and the BSD license are retained under
`third_party/minhook`.

The controller's first safe lifecycle pins the host DLL for the lifetime of
Explorer after activation, retains a `Taskbar.View.dll` lease until the
trampoline is drained and removed, and quarantines rather than unloading on a
failed disable/remove/restore. This avoids remote `SetWindowsHookEx` unload
races; dynamic DLL unloading is deliberately deferred until a disposable
Explorer-session test exists.

The private TaskbarFrame pointer is not projected directly. A separate bridge
first requires the active exact-profile detour and owner thread, reads the
allowlisted IInspectable slot through bounded current-process memory reads,
checks the IUnknown vtable methods point to executable image memory, then
verifies
the projected `Taskbar.TaskbarFrame` class and XAML dispatcher. The exported
entry hook remains inert, so this bridge is compile-tested but not invoked in
Explorer yet.

The live slot probe accepts only the development tree's exact direct-child
fingerprint (`BackgroundControl` plus `TaskbarFrameRepeater`, and optionally
the exact button identity already owned by the lease). It snapshots bounded
realized repeater child rectangles in RootGrid coordinates and feeds the
geometry gate. Unknown children, invalid transforms, or a changed tree fail
closed before append/update.
