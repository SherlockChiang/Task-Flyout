# OAuth Scopes And Local Data Cleanup

This document records the current OAuth scope review and local cleanup behavior.

## Google

Requested scopes:

- `CalendarService.Scope.Calendar`: required for creating, editing, deleting, and reading calendar events.
- `TasksService.Scope.Tasks`: required for creating, editing, completing, and reading Google Tasks.
- `GmailService.Scope.GmailReadonly`: required for listing and reading Gmail messages.
- `GmailService.Scope.GmailModify`: required when marking Gmail messages as read.
- `GmailService.Scope.GmailSend`: required when sending Gmail replies/new messages from Task Flyout.

All five Google scopes are requested together from an explicit Connect/Reconnect action. Runtime calendar, task, mail, polling, and mutation paths restore the protected token silently and never call the browser authorization broker.

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
- `Mail.ReadWrite`: required for listing, reading, and marking Outlook messages as read.
- `Mail.Send`: required when sending Outlook mail. Task Flyout creates an immutable-ID draft so a timed-out send can be confirmed without sending a duplicate.

All five Microsoft scopes are requested together from an explicit Connect/Reconnect action. Runtime Graph access uses `AcquireTokenSilent`; only the explicit connection action can call `AcquireTokenInteractive`.

Local cleanup on account removal:

- Removes the connected account entry.
- Removes cached calendar/task ranges for the provider.
- Deletes the DPAPI-protected Microsoft authentication record file `ms_auth_record.bin`.
- MSAL owns the persistent `TaskFlyout_MSAL_Cache.nocae` token cache. Account removal registers the known stores with MSAL and calls `RemoveAsync` for every cached account, then deletes the protected home-account identifier. If any authorization cleanup step fails, the app keeps the business account so the user can retry.

## Mail Accounts

Local cleanup on mail account removal:

- Removes the mail account entry.
- Removes IMAP passwords from Windows PasswordVault for IMAP accounts.
- Disconnects persistent IMAP polling clients.
- Clears in-memory and persistent folder/message caches for that account.
- Removes known-unread notification tracking IDs for that account.

## Notes

- OAuth tokens and local mail/calendar/task caches are stored only on the device.
- Google and Microsoft setup request each provider's complete current feature scope set once.
- Existing complete-scope tokens remain valid and are restored silently.
- Existing partial-scope tokens produce a reconnect-required state. They are upgraded only after the user explicitly clicks Connect/Reconnect.
- Background sync, startup polling, message reading, mark-read, sending, and task/calendar mutations cannot start an interactive browser flow.

## Interaction Policy

Interactive OAuth is restricted to the Google and Microsoft Connect/Reconnect buttons. Providers serialize authorization state so concurrent startup sync and mail polling cannot launch duplicate authorization sessions. A missing, revoked, or partial token remains stored and produces `AuthorizationInteractionRequiredException`; feature code may show cached data and a reconnect status but must not clear the token or launch a browser.
