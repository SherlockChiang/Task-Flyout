# OAuth Scopes And Local Data Cleanup

This document records the current OAuth scope review and local cleanup behavior.

## Google

Requested scopes:

- `CalendarService.Scope.Calendar`: required for creating, editing, deleting, and reading calendar events.
- `TasksService.Scope.Tasks`: required for creating, editing, completing, and reading Google Tasks.
- `GmailService.Scope.GmailModify`: required for listing and reading messages,
  marking messages read/starred, managing labels, archiving or moving messages
  to trash, and composing/sending mail. Google's scope catalog defines this
  scope as covering read, compose, and send.

The separate `GmailReadonly` and `GmailSend` scopes are not requested because
they are subsets of the Gmail capability already granted by `GmailModify` for
this app. Task Flyout does not request `https://mail.google.com/` and does not
permanently delete messages while bypassing trash. See Google's
[Gmail scope catalog](https://developers.google.com/workspace/gmail/api/auth/scopes).

All three Google scopes are requested together from an explicit
Connect/Reconnect action. Runtime calendar, task, mail, polling, and mutation
paths restore the protected token silently and never call the browser
authorization broker.

Local cleanup on account removal:

- Removes the connected account entry.
- Removes cached calendar/task ranges for the provider.
- Clears the DPAPI-protected Google token store.
- Deletes the legacy `GoogleToken` folder if present.

## Microsoft

**2026-10-07 follow-up:** standalone Outlook setup is isolated from agenda
authorization; it does not create an agenda account or initialize an agenda
client. School-account admin consent remains blocked and live acceptance is
pending. See [the consent review](microsoft-oauth-consent.md) for the source
evidence, implementation follow-up, and the unsent IT review draft.

Requested scopes:

- `Calendars.ReadWrite`: required for creating, editing, deleting, and reading Outlook calendar events.
- `Tasks.ReadWrite`: required for creating, editing, completing, and reading Microsoft To Do tasks.
- `User.Read`: required by Microsoft Graph sign-in/profile bootstrap.
- `Mail.ReadWrite`: required for listing, reading, and marking Outlook messages as read.
- `Mail.Send`: required when sending Outlook mail. Task Flyout creates an immutable-ID draft so a timed-out send can be confirmed without sending a duplicate.

Microsoft authorization is incremental:

- The Microsoft account Connect/Reconnect action requests only `User.Read`, `Calendars.ReadWrite`, and `Tasks.ReadWrite`.
- The explicit Add Outlook action restores or requests `User.Read` and `Mail.ReadWrite`.
- Sending Outlook mail adds `Mail.Send` to the mailbox scopes. The send path uses the same mailbox client so draft creation and sending remain covered.

Runtime calendar/task and mailbox access use silent token acquisition. Only
an explicit connection or Send action can supply an interactive callback for
Microsoft mail. Reading messages, changing read/unread state, and background
polling cannot open a browser. Missing or revoked consent requires reconnection
from the Mail page. Explicit consent is completed before the send transport
timeout starts; a failed or canceled consent does not send a message.

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
- Google setup requests three scopes (Calendar, Tasks, and Gmail Modify).
  Microsoft separates agenda, mailbox, and sending scope sets; live acceptance
  and external tenant approval are recorded in the review above.
- Existing Google tokens containing the former five-scope superset remain valid
  and are restored silently.
- Existing partial-scope tokens produce a reconnect-required state. They are upgraded only after the user explicitly clicks Connect/Reconnect.
- Background sync, startup polling, message reading, mark-read, and task/calendar
  mutations cannot start an interactive browser flow. The explicit Microsoft
  Send action can request the additional sending consent when needed.

## Interaction Policy

Interactive OAuth is restricted to explicit connection actions and the
Microsoft Send action. Providers serialize authorization state so concurrent
startup sync and mail polling cannot launch duplicate authorization sessions.
A missing, revoked, or partial token on runtime restoration produces
`AuthorizationInteractionRequiredException`; feature code may show cached data
and a reconnect status but must not clear the token or launch a browser.
