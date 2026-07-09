# OAuth Scopes And Local Data Cleanup

This document records the current OAuth scope review and local cleanup behavior.

## Google

Requested scopes:

- `CalendarService.Scope.Calendar`: required for creating, editing, deleting, and reading calendar events.
- `TasksService.Scope.Tasks`: required for creating, editing, completing, and reading Google Tasks.
- `GmailService.Scope.GmailReadonly`: required for listing and reading Gmail messages.
- `GmailService.Scope.GmailSend`: required for sending Gmail replies/new messages from Task Flyout.
- `GmailService.Scope.GmailModify`: required for marking Gmail messages as read.

Local cleanup on account removal:

- Removes the connected account entry.
- Removes cached calendar/task ranges for the provider.
- Clears the DPAPI-protected Google token store.
- Deletes the legacy `GoogleToken` folder if present.

## Microsoft

Requested scopes:

- `Calendars.ReadWrite`: required for creating, editing, deleting, and reading Outlook calendar events.
- `Tasks.ReadWrite`: required for creating, editing, completing, and reading Microsoft To Do tasks.
- `Mail.ReadWrite`: required for reading mail and marking messages as read.
- `Mail.Send`: required for sending Outlook replies/new messages from Task Flyout.
- `User.Read`: required by Microsoft Graph sign-in/profile bootstrap.

Local cleanup on account removal:

- Removes the connected account entry.
- Removes cached calendar/task ranges for the provider.
- Deletes the DPAPI-protected Microsoft authentication record file `ms_auth_record.bin`.
- Azure Identity also owns the platform token cache created with `TokenCachePersistenceOptions { Name = "TaskFlyout_MSAL_Cache" }`. The current Azure Identity API used here does not expose a targeted delete operation for that cache, so the app clears its local authentication record and forces re-authentication on the next connection. If a supported targeted cache removal API becomes available, wire it into `MicrosoftSyncProvider.ClearLocalAuthorizationAsync()`.

## Mail Accounts

Local cleanup on mail account removal:

- Removes the mail account entry.
- Removes IMAP passwords from Windows PasswordVault for IMAP accounts.
- Disconnects persistent IMAP polling clients.
- Clears in-memory and persistent folder/message caches for that account.
- Removes known-unread notification tracking IDs for that account.

## Notes

- OAuth tokens and local mail/calendar/task caches are stored only on the device.
- Google and Microsoft mail send/modify/write scopes are intentionally broad because the app supports in-app send, mark-read, create, edit, and delete operations.
- If a future release splits read-only and write features, scopes should be revisited for incremental consent.

## Incremental Consent Follow-Up

The current app asks for the scopes needed by the full feature set because account setup immediately enables calendar/task sync plus mail read/send/mark-read features. A future incremental-consent design should split setup into feature toggles:

- Calendar only: request calendar read/write and no mail scopes.
- Tasks only: request task read/write and no mail scopes.
- Mail read: request read access only.
- Mail actions: request send/modify scopes only when the user enables send or mark-read actions.

Before implementing this, update the setup UI to explain which features each toggle enables, and add migration logic for existing users whose stored tokens already contain the broader scopes.
