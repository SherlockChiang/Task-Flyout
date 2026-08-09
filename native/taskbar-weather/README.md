# Task Flyout Standalone Taskbar Weather

This directory contains the x64 native components for the experimental
WebView-free Windows 11 taskbar weather path.

- `TaskFlyout.TaskbarBroker.exe` runs as the current desktop user, verifies the
  real Explorer taskbar owner, and gates private taskbar integration on an exact
  Windows and `Taskbar.View.dll` PE profile.
- `TaskFlyout.TaskbarHost.dll` is the narrowly scoped in-process Explorer host.
  The current scaffold exposes a versioned probe contract and a no-op thread
  hook only. It does not inject or modify XAML yet.
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

