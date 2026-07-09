# OAuth Scopes And Local Data Cleanup

This document records the current OAuth scope review and local cleanup behavior.

## Google

Requested scopes:

- `CalendarService.Scope.Calendar`: required for creating, editing, deleting, and reading calendar events.
- `TasksService.Scope.Tasks`: required for creating, editing, completing, and reading Google Tasks.
- `GmailService.Scope.GmailReadonly`: requested when Gmail mail is used; required for listing and reading Gmail messages.
- `GmailService.Scope.GmailModify`: requested only when marking Gmail messages as read.
- `GmailService.Scope.GmailSend`: requested only when sending Gmail replies/new messages from Task Flyout.

Local cleanup on account removal:

- Removes the connected account entry.
- Removes cached calendar/task ranges for the provider.
- Clears the DPAPI-protected Google token store.
- Deletes the legacy `GoogleToken` folder if present.

## Microsoft

Requested scopes:

- `Calendars.ReadWrite`: required for creating, editing, deleting, and reading Outlook calendar events.
- `Tasks.ReadWrite`: required for creating, editing, completing, and reading Microsoft To Do tasks.
- `User.Read`: required by Microsoft Graph sign-in/profile bootstrap.
- `Mail.Read`: requested when Outlook mail is used; required for listing and reading Outlook messages.
- `Mail.ReadWrite`: requested only when marking Outlook messages as read.
- `Mail.Send`: requested only when sending Outlook replies/new messages from Task Flyout.

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
- Google Calendar/Tasks setup now requests only Calendar and Tasks scopes. Gmail read, modify, and send scopes are requested from the mail feature path that needs each capability.
- Microsoft Calendar/To Do setup now requests only Calendar, Tasks, and User scopes. Outlook read, write, and send scopes are requested from the mail feature path that needs each capability.
- Existing broad-scope tokens remain valid; account removal is still the supported way to force a fresh least-privilege OAuth grant.

## Incremental Consent Follow-Up

The app now separates Google Calendar/Tasks setup from Gmail mail scopes and splits mail action scopes by capability:

- Calendar only: request calendar read/write and no mail scopes.
- Tasks only: request task read/write and no mail scopes.
- Mail read: request read access only for folder/message/body loading.
- Mail mark-read: request Gmail modify or Microsoft read/write only when a message is marked read.
- Mail send: request Gmail send or Microsoft send only when sending a message.

Existing users whose stored tokens already contain broader scopes keep working without forced re-authentication.

## Mail Scope Split Risk Notes

Mail read/action split is tied to UX because automatic mark-read is a write-like action during normal reading:

- Selecting a message marks it as read, which needs Gmail `GmailModify` or Microsoft `Mail.ReadWrite`.
- Reply/compose needs Gmail `GmailSend` or Microsoft `Mail.Send`.
- Folder/message listing and body fetch now use read-only scopes.

Current behavior:

1. `AutoMarkMailAsRead` now exists and defaults to the current behavior for existing users.
2. Settings exposes `AutoMarkMailAsRead`; disabling it lets message reading stay read-only.
3. Remote mark-read requests modify/write scope only when the app actually marks a message as read.
4. Sending requests send scope only when the user sends a composed/reply message.
5. Keep existing broad-scope tokens valid; do not force reauth unless a requested action fails for missing scope.

Remaining UX follow-up: add copy around compose/auto-mark-read explaining that those actions may trigger an additional consent prompt.
