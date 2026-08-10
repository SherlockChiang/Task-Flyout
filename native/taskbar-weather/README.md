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

Any mismatch fails before taskbar memory or XAML is changed.

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
