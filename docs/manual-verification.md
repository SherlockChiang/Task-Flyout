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
