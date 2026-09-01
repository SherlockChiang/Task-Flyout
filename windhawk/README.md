# Task Flyout Windhawk companion

This directory contains an experimental Explorer companion for the taskbar
weather integration. It is separate from the WinUI application because a
normal desktop process cannot insert or replace Explorer's internal
`Taskbar.AugmentedEntryPointButton` XAML content.

## Current proof of concept

`task-flyout-weather-companion.wh.cpp` performs a deliberately narrow native-
shell weather preview:

- hooks the same `TaskbarFrame::OnTaskbarLayoutChildBoundsChanged` symbol family
  used by Windhawk's official taskbar mods;
- requires the exact Widgets class, element name, and `WidgetsButton`
  automation ID before changing anything;
- appends `TaskFlyoutWeatherHost` inside the existing
  `AugmentedEntryPointContentGrid`;
- leaves the Windows Adaptive Card measured but makes it transparent, preserving
  the width variables used by the Luminosity theme;
- reads a versioned, length-bounded snapshot from Task Flyout on a background
  worker; the pipe is scoped by user SID and Windows session, and Explorer's UI
  thread performs no pipe or network I/O;
- restores the Windows content when the app bridge is disabled, the pipe is
  unavailable, the response is invalid, or the weather snapshot is stale;
- keeps the native outer button, background, hover states, click target, and
  accessibility behavior;
- records the original local opacity and hit-test values and restores them when
  the preview is disabled or the mod unloads; if a disconnected XAML child
  rejects restoration, the visible host keeps a primitive-only recovery snapshot
  for the next load instead of exposing a blank native surface;
- is disabled by default and requires both Windows 11 build 26200 and the exact
  reviewed `Taskbar.View.dll` PE fingerprint before the private ABI is touched.

The native outer button still opens the Windows Widgets board when clicked.
Task Flyout activation remains a later phase because its XAML event subscription
must be revoked safely on every taskbar thread before Explorer can unload the mod.
The optional static preview is a separate diagnostics setting and is off by
default.

## Safety boundary

This is an experimental, version-sensitive Explorer injection. A resolved
symbol is not sufficient by itself: the runtime XAML tree must also pass all
three identity checks. A mismatch causes a no-op. Do not install or enable the
companion on a daily-use Explorer session until compile-only validation, unload
restoration, Explorer restart, secondary-monitor, and Luminosity width checks
have passed.

The companion intentionally avoids XAML Diagnostics. Windhawk's taskbar styler
already owns that single-consumer facility, so a second watcher can conflict
with the user's active theme.

## Planned phases

1. Compile and statically review the disabled-by-default preview.
2. Validate enable/disable/unload behavior on the current Windows build and
   Luminosity theme.
3. Add a bounded per-user named-pipe weather snapshot channel. Completed as a
   compile-only implementation; runtime validation remains pending.
4. Add an authenticated activation path for opening Task Flyout's weather page.
5. Add multi-monitor/restart diagnostics and a strict compatibility matrix.

No file in this directory is packaged into the MSIX application.

## Compile-only validation

With Windhawk installed in its default location, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\windhawk\build-companion.ps1 -Mode Syntax
powershell -ExecutionPolicy Bypass -File .\windhawk\build-companion.ps1 -Mode Link
```

The link build is written under `.testbuild\windhawk`, which is ignored by Git.
The script only invokes Windhawk's bundled compiler; it does not copy the DLL to
Windhawk, update Windhawk configuration, load the mod, or restart Explorer.
