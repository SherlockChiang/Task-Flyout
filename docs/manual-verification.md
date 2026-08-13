# Manual Verification Checklist

Use this checklist for flows that require real Windows credentials, DPAPI, PasswordVault, WebView2, or OAuth provider state and cannot be fully covered by the pure unit test project.

## Account Removal Cleanup

### Google Calendar/Tasks/Gmail

1. Connect a Google account and complete an initial sync.
2. Confirm calendar/task data appears in Calendar, Tasks, and Flyout.
3. Open Mail and confirm Gmail folders/messages load if Gmail mail was configured.
4. Remove the Google calendar account from Calendar or MainWindow account list.
5. Restart the app.
6. Confirm Google calendar/task data no longer appears.
7. Confirm the protected Google token store was cleared by reconnecting Google and observing a fresh OAuth prompt.
8. Confirm the legacy `%APPDATA%\TaskFlyout\GoogleToken` folder is absent if it existed before removal.

### Microsoft Calendar/To Do

1. Connect a Microsoft account and complete an initial sync.
2. Confirm calendar/task data appears in Calendar, Tasks, and Flyout.
3. Remove the Microsoft account from Calendar or MainWindow account list.
4. Restart the app.
5. Confirm Microsoft calendar/task data no longer appears.
6. Confirm `%APPDATA%\TaskFlyout\ms_auth_record.bin` is removed.
7. Reconnect Microsoft and confirm the app performs an interactive authentication flow again.

### Mail Accounts

1. Add Outlook, Gmail, or IMAP mail accounts.
2. Load folders and messages, then open several message bodies.
3. Remove the mail account from Mail.
4. Restart the app.
5. Confirm the account is absent from the Mail tree.
6. Confirm folders/messages from that account are absent from local cache views.
7. For IMAP, confirm the account password is removed from Windows Credential Manager / PasswordVault.
8. Confirm new mail notifications no longer fire for the removed account.

## Status And Onboarding

1. Start with no calendar accounts, no mail accounts, and weather disabled.
2. Open the main window and confirm it opens the setup page automatically.
3. Use the setup shortcuts to navigate to Mail and Weather setup.
4. Complete Google, Microsoft, Mail, and Weather setup.
5. Confirm the setup checklist shows `Setup complete.` and the app stores `OnboardingChecklistCompleted`.
6. Restart the app and confirm it opens the normal Calendar page instead of forcing setup.

## Error State Checks

1. Disable network access after loading Mail, RSS, Weather, and Calendar once.
2. Trigger refresh in each page.
3. Confirm visible error states include the last successful load/sync/refresh time where implemented.
4. Re-enable network and confirm refresh recovers without restarting the app.

## Weather Location Permission

1. Start the app with weather enabled but without opening the Weather page.
2. Confirm Windows does not show a location permission prompt during startup.
3. Open Weather and confirm the auto-follow toggle is off unless it was turned on in the current session.
4. Click `Use current location` and confirm this is the first point where Windows may ask for location permission.
5. Deny permission and confirm the Weather page shows the denied-permission status without enabling auto-follow.
6. Allow permission, click `Use current location`, and confirm the city/coordinates update and weather refreshes.
7. Turn on auto-follow and confirm tracking starts only after the toggle action.
8. Turn off auto-follow, restart the app, and confirm startup does not request location permission.

## Weather Saved Locations And Alert Details

1. Migrate from a build containing one `location_v1` and `cache_v1` record; confirm it appears as the active saved location and no weather coordinates remain in LocalSettings.
2. Add five cities through the existing city search and confirm the sixth is blocked with `5 of 5` feedback.
3. Rename locations, switch among them, and confirm each switch immediately displays that location's last successful protected cache before refreshing it; restart and confirm IDs, aliases, active selection, retry state, and caches persist.
4. Use current location repeatedly and confirm one current-location entry is updated rather than additional entries being created. Confirm no automatic location rotation occurs.
5. Remove a location, accept confirmation, and confirm another stable entry becomes active; verify the removed cache is not retained after the next weather cache write.
6. Right-click Weather Bar and confirm its menu dynamically lists all saved locations as one radio group plus `Manage locations`; select each and confirm refresh targets it.
7. With no alert, left-click Weather Bar and confirm direct Weather navigation is unchanged.
8. With a forecast-derived alert, left-click and confirm the detail flyout header exactly matches the bar label, and shows type, derivable start/end, precipitation probability, wind and temperature values, thresholds, the full untruncated active location, disclaimer, and `Open Weather`.

## Weather Bar Multi-Display And Explorer Recovery

1. Enable Weather and the taskbar Weather Bar with two displays connected and taskbars visible on both displays.
2. Confirm the bar remains on its currently attached valid taskbar across normal polling; if no attachment exists, confirm the primary taskbar is preferred and a secondary taskbar is used when the primary is unavailable.
3. Change one display's scale/DPI and taskbar position, then confirm the bar remains inside the selected taskbar and does not overlap native Widgets or active FluentFlyout media controls.
4. Open Settings and confirm Weather Bar Diagnostics shows the taskbar class, Widgets bridge source, monitor rectangle, DPI, taskbar rectangle, bar rectangle, and fallback reason without HWND values, window titles, process IDs, paths, location names, or account data.
5. Click `Reattach taskbar` and confirm the bar briefly re-evaluates its attachment, remains visible, and the diagnostic geometry refreshes.
6. Restart Windows Explorer from Task Manager while the bar is enabled. Confirm the app does not crash, the stale bar disappears with Explorer, and a fresh bar normally attaches within 2 seconds after a supported taskbar returns; the 30-second watchdog remains a fallback.
7. Disable the Weather Bar, restart Windows Explorer, then re-enable the bar. Confirm a fresh native window attaches immediately instead of reusing the hidden pre-restart window.
8. Repeat the Explorer restart while only a secondary taskbar is discoverable and confirm recovery does not wait for `Shell_TrayWnd`.
9. Disconnect the display hosting the selected taskbar and confirm polling invalidates stale taskbar, FluentFlyout, and placement state before attaching to the remaining taskbar.

## Native Windows Widgets Mode

1. Open Weather settings and confirm the new native Widgets switch is off by default; the existing Task Flyout bar and its field/alert controls behave as before.
2. On a Windows 11 profile with the Windows Web Experience Pack registered, turn on the switch. Confirm the existing Task Flyout child window remains visible while the request is pending, the previous `TaskbarDa` value is captured, and only after Explorer materializes its own `Taskbar.AugmentedEntryPointButton` entry is the custom window removed.
3. Click the native entry and confirm Windows opens the Widgets board and owns the weather/news data. The app must not draw a second weather pill over the native control.
4. Restart Explorer and confirm the native entry returns without starting a custom weather-bar watchdog; open Settings diagnostics and verify the requested mode, native availability reason, and entry-point state.
5. Turn the switch off (or disable the master Weather Bar toggle) and confirm the captured `TaskbarDa` value is restored and the custom bar returns when weather is enabled. If the current value no longer matches Task Flyout's owned `DWORD 1`, leave that newer value intact; simulate one failed restore and confirm the persisted snapshot is retried.
6. On a machine without the Web Experience Pack, turn on the switch and confirm the status explains the missing package, the Store repair button is offered, and the existing Task Flyout bar remains the visible fallback.
7. Do not treat the fallback child window as a native Widgets control: the ordinary WinUI app path does not inject Explorer XAML. The separately built experimental Windhawk companion has its own checklist below and is not installed by the MSIX.
8. Block or delay creation of the native entry and confirm the custom bar never disappears into an empty taskbar slot. After the initial 10-second fast verification window, confirm retries back off to approximately 30 seconds and automatically hand off if the native entry later appears.
9. Turn off Task Flyout's own weather provider while native mode remains selected and confirm Windows Widgets stays enabled; its weather data is independent of Task Flyout's provider.
10. Allow a finance, sports, or news announcement to rotate onto the native entry and confirm Settings identifies it as Windows-managed content rather than claiming Task Flyout injection. Use the new button, open Widgets Settings > Notifications, turn off taskbar announcements, and confirm the entry returns to weather; optionally turn off notification badges as well.

## Experimental Standalone Taskbar Weather

Run these checks only in a disposable Windows user profile or VM. The standalone
Host uses a temporary thread hook to load code into Explorer, and the first safe
lifecycle deliberately keeps the Host module pinned until that Explorer process
exits.

1. Build the isolated Release native matrix and confirm all tests/import checks pass. Sign `TaskFlyout.TaskbarBroker.exe` and `TaskFlyout.TaskbarHost.dll` with the same existing package certificate; do not copy a certificate or private key into the repository.
2. Run `scripts\test-standalone-taskbar-weather.ps1 -DescribeOnly`. Confirm it reports that Explorer will be mutated, no package will be installed, Explorer will not be restarted automatically, and every attempted start has up to three bounded stop attempts.
3. Disable the packaged app's standalone mode and confirm no controller is active, then snapshot the VM/profile. Record the Explorer PID, Windows build, loaded `Taskbar.View.dll` timestamp/size/checksum, original `TaskbarDa` value and registry kind, display topology, DPI, taskbar alignment, auto-hide, and light/dark theme. The harness requires a `not-started` baseline and will not adopt an existing controller.
4. Run the harness only after reviewing its exact-profile probe, using both `-AllowExplorerInjection` and `-DisposableSessionConfirmation TASK_FLYOUT_DISPOSABLE_EXPLORER_SESSION`. Keep signature verification enabled; optionally pass `-ExpectedSignerThumbprint "YOUR_THUMBPRINT"`. Use `-AllowUnsignedDevelopmentBuild` only for an isolated throwaway build and never combine it with an expected signer.
5. For the API v10 diagnostic-only checkpoint, confirm start returns one fixed `bootstrap-*` diagnostic and the immediate status query returns the identical value. The probe first reads `Window::Current().Content()` and requires its public `CoreWindow` HWND to be the exact `Shell_TrayWnd`, then uses that root as the explicit subtree for one center-point query. Only `root-bootstrap-validated` is successful; missing root, failed root/subtree query, wrong HWND association, frame absence/ambiguity, bounded enumeration overflow, and tree mismatch all fail closed. Confirm the harness then receives `stopped` or `not-started` cleanup and `not-started` from its final status query without waiting for `mount-ready` or entering the hold interval. For a later mounting build, retain the existing `mount-ready`/hold expectations. Force one rejected/ambiguous start in a test build and confirm the `finally` path still attempts idempotent stop. The harness must never install a package, change `TaskbarDa`, kill Explorer, or restart it.
6. For the API v11 diagnostic-only checkpoint, confirm start reports `private-bridge-awaiting-callback` until one naturally occurring TaskbarFrame layout callback arrives; the controller must not request a relayout. The callback may only resolve the private pointer under the exact detour TLS token and owner thread, then run the complete `Taskbar.TaskbarFrame` tree profile. Only `private-bridge-validated` succeeds; every bridge gate stage and `private-bridge-tree-profile-mismatch` fail closed. Confirm the harness bounds the wait, never starts the weather worker, never creates or retains a view/lease, and always verifies `not-started` after cleanup.
7. Start the signed packaged app with weather data available, enable the experimental standalone mode, and confirm the Task Flyout fallback remains visible until an authenticated native ready report arrives. The independent pure-XAML weather button must occupy the bounded left taskbar slot and show only Task Flyout weather—not finance, sports, news, a WebView, a white rectangle, or an independent HWND.
8. Verify native taskbar hover/pressed/focus/high-contrast visuals and keyboard accessibility. Click the button repeatedly and confirm requests coalesce, Explorer's UI thread remains responsive, and Task Flyout opens directly to Weather.
9. Rebuild the taskbar tree by changing alignment/theme and by toggling auto-hide. When the owned Button detaches, confirm authenticated lost or the 12-second lease timeout restores the fallback; a later higher-generation ready report may hide it again only after the lost report is accepted.
10. Repeat at 100%, 125%, 150%, and 200% DPI, with primary and secondary taskbars, display disconnect/reconnect, and light/dark/high-contrast themes. Confirm bounded width, no overlap, one owned Button per frame, and no cross-thread XAML access or stale secondary-taskbar lease.
11. Restart Explorer manually inside the disposable session. Confirm old PID/nonce/generation reports are rejected, fallback returns, suppression/start recovery targets only the new Explorer, and the new Button must prove mount readiness before handoff.
12. Run an intentionally mismatched local profile and confirm probe/start fail before installing the detour or changing XAML. Also stop the app pipe or pause owner-thread proofs and confirm readiness expires fail-closed instead of replaying stale ready.
13. Disable standalone mode and exit the app. Confirm stop acknowledgement precedes exact owned `TaskbarDa` restoration, later user/policy changes are preserved, the custom Button is removed, click tokens are revoked, and fallback behavior returns. If cleanup is ambiguous, run the Broker `stop` command manually or restart only the disposable Explorer session.
14. Restart the disposable Explorer once more to release the deliberately pinned Host module, then compare Explorer crashes, handle count, taskbar responsiveness, registry state, and app diagnostics with the baseline before accepting M3-07/M3-08.

## Experimental Windhawk Weather Companion

Run these checks only in a disposable Explorer session after the compile-only
checkpoint has been reviewed. The companion is not part of the MSIX.

1. Run `windhawk\build-companion.ps1 -Mode Syntax` and `-Mode Link`; confirm both use the installed Windhawk engine path and neither copies files into `ProgramData`, changes Windhawk configuration, loads a DLL, or restarts Explorer.
2. Confirm the mod setting `enabled` defaults to false. With it false, install/preview the local mod and verify the native Widgets entry and its finance/weather rotation remain unchanged.
3. Before enabling, record Windows build plus the loaded taskbar module PE timestamp, image size, and checksum. Confirm they exactly match the allowlisted profile; alter one local profile constant in a test build and verify initialization fails before `pThis` is dereferenced.
4. Enable the static preview and confirm exactly one `TaskFlyoutWeatherHost` appears inside the existing native Widgets button. Finance, sports, and news text must no longer be visible; no independent HWND, white rectangle, or second taskbar item may appear.
5. Confirm Luminosity still owns the outer position, background, border, radius, hover state, and width. The hidden Adaptive Card must remain measured, and the preview text must not clip at 100%, 125%, 150%, or 200% display scale.
6. Hover and click the entry. Confirm hover remains native and the proof-of-concept click still opens Windows Widgets; it must not claim Task Flyout activation until the IPC phase is implemented.
7. Change the preview text repeatedly and confirm one host is updated rather than duplicated. Trigger Windows ticker/template refreshes and confirm newly realized native children stay hidden but measured.
8. Test primary and secondary taskbars, display disconnect/reconnect, Explorer restart, and a delayed Widgets entry. Each taskbar frame must have at most one host and no cross-thread XAML access.
9. Disable and unload the mod. Confirm the host is removed and the exact prior local opacity/hit-test values return. Repeat while forcing a template rebuild; a failed child restore must retain recovery metadata rather than exposing a blank entry.
10. Hang the taskbar UI thread in a disposable VM long enough to exceed the owner-dispatch wait. Confirm settings/unload returns after the bounded wait, the retained helper keeps callback code loaded, and cleanup finishes after the taskbar thread resumes.
11. Do not ship or expose a Task Flyout UI switch until every check above passes and M3-04 supplies a bounded per-user weather snapshot/activation channel with no blocking Explorer UI-thread I/O.

## Integration Test Environment Notes

These checks require a dedicated Windows user profile or disposable VM because they touch system credential stores and provider OAuth state.

- Use test-only Google and Microsoft accounts with no personal data.
- Snapshot `%APPDATA%\TaskFlyout` and `%LOCALAPPDATA%\TaskFlyout` before each run.
- Clear Windows Credential Manager entries created by the app after each IMAP test.
- Do not run token cleanup tests against a developer's primary Microsoft account because Azure Identity/MSAL may reuse platform SSO state outside the app-controlled `ms_auth_record.bin` file.
- Capture `Logs/startup.csv`, `Logs/weatherbar-theme.csv`, and crash logs only after confirming they do not contain tokens or message bodies.

Recommended automation boundary:

- Keep pure parser, sanitizer, URI, and policy logic in `Tests/Task_Flyout.Tests`.
- Run credential-store and OAuth cleanup as manual or VM-isolated integration tests.
- Treat provider consent screens as external dependencies; validate app-side state before and after the flow rather than asserting provider UI text.
