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
7. Do not treat the fallback child window as a native Widgets control: true Explorer/XAML injection is intentionally outside the WinUI app boundary and is not part of this verification.
8. Block or delay creation of the native entry and confirm the custom bar never disappears into an empty taskbar slot. After the initial 10-second fast verification window, confirm retries back off to approximately 30 seconds and automatically hand off if the native entry later appears.
9. Turn off Task Flyout's own weather provider while native mode remains selected and confirm Windows Widgets stays enabled; its weather data is independent of Task Flyout's provider.

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
