# Outlook IMAP / SMTP OAuth2

Added on 2026-10-07 as an alternative to Outlook's Microsoft Graph connection.
This is not a way to bypass organizational consent or mailbox protocol policy.
No Entra registration or tenant setting has been changed by this implementation.

## Connection choices

Mail → Add account → **Outlook IMAP (OAuth2)**. Enter the Microsoft **sign-in
email**, choose Personal Outlook.com or Work/school Microsoft 365, then sign in
using the system browser. Setup verifies the IMAP connection before adding the
account. No Microsoft password is entered into or stored by Task Flyout.

| Setting | Personal Outlook.com | Work/school Exchange Online |
| --- | --- | --- |
| Sign-in authority | `consumers` | `organizations` |
| IMAP | `outlook.office365.com:993`, SSL/TLS | Same |
| SMTP | `smtp-mail.outlook.com:587`, STARTTLS | `smtp.office365.com:587`, STARTTLS |
| Authentication | SASL XOAUTH2 | SASL XOAUTH2 |

This first version targets standard public-cloud Microsoft mailboxes, not shared
mailboxes, sign-in aliases, on-premises Exchange, sovereign clouds or POP. Choose
the actual Microsoft sign-in email: initial setup rejects a different signed-in
username. Reconnection is subsequently bound to the stored MSAL home-account ID.
Existing Outlook Graph and custom password-based IMAP accounts are preserved.
Adding an OAuth account does not convert or overwrite a password IMAP account.
Do not keep both connections polling the same mailbox if duplicate notifications
are unwanted; accounts remain independent and notifications are account-scoped.

## App registration prerequisites

The existing app registration identified by `Secrets.MicrosoftClientId` is reused.
Before live acceptance, the owner must check its configuration in Microsoft Entra:

1. The supported account types must permit the intended personal/organizational
   users. Using a different authority cannot expand a registration's audience.
2. Under **API permissions → APIs my organization uses → Office 365 Exchange
   Online → Delegated permissions**, configure `IMAP.AccessAsUser.All` and
   `SMTP.Send`. These are **not** Graph permissions and not app-only permissions.
3. Keep the desktop public-client/system-browser redirect `http://localhost`
   configured. No client secret is needed in this desktop application.
4. Follow the tenant's consent process. Do not disable its restrictions to make
   the app work. We have not inspected or changed these external settings.

Adding permissions to the registration is not the same as granting user consent.
The application initially requests only
`https://outlook.office.com/IMAP.AccessAsUser.All`. It requests
`https://outlook.office.com/SMTP.Send` separately on explicit Send, before starting
the mail transport timeout. MSAL handles the standard sign-in/session scopes and
refresh tokens. It does not use Graph access tokens for either protocol.

## Mailbox prerequisites and limitations

- Personal Outlook.com: Settings → Mail → Forwarding and IMAP → allow IMAP, Save.
- Work/school: the administrator may have disabled IMAP, SMTP AUTH, or user
  consent for this application. Receiving can succeed while sending is blocked.
  OAuth2 does not override these protocol or tenant restrictions.
- “Need admin approval” can still appear. Publisher verification and admin
  consent are external requirements, unrelated to the MSIX signing certificate.
- Do not use a Microsoft password or an application password as an automatic
  fallback. The new option has no basic-authentication fallback.

## Local lifecycle

- Each mail account has a dedicated Windows DPAPI-protected MSAL cache under
  the application-local `TaskFlyout/OutlookImapAuth` directory. Only a canonical
  GUID mail ID can determine its file name. No new plaintext token store is used.
- Its non-secret home-account identity is saved with the existing protected mail
  metadata. The Graph/calendar cache and authorization record are not touched.
- Folder/message loading, read/unread changes, other IMAP mutations and polling
  use the existing IMAP implementation and only silently acquire/refresh tokens.
  Missing consent offers explicit reconnection, never a background browser.
- Explicit Add/reconnect and Send may open a browser and are bounded to five
  minutes; account setup supports Cancel. IMAP's silent acquisition has a 30-second
  limit followed by the existing 10-second connection/authentication limit.
- OAuth tokens are only sent to the fixed Microsoft endpoints above using
  enforced TLS. Modified/custom endpoint settings are rejected.
- Removing this mail account first clears its dedicated authorization. Cleanup
  failure retains the account for retry. It does not disconnect another IMAP
  account, Graph, Calendar or Tasks.
- Failed/cancelled new-account setup attempts to clear its token cache before
  returning. If cache cleanup itself fails, that error is surfaced; deletion
  cannot be claimed successful. Tokens are not revoked server-side by local
  cleanup, and the user's system-browser sign-in session is not cleared.

## Verification and pending acceptance

The 31 new pure-logic tests cover endpoints, TLS, account-type authorities,
operation-specific scopes, account identity matching, and isolated/path-safe cache
names. Existing authorization-flow tests cover silent restoration and explicit
fallback/cancellation/denial. They do **not** exercise MSAL, real IMAP/SMTP,
DPAPI token persistence, or UI events. App build and final test results are
recorded in the local todo.

Manual acceptance after registration configuration and a requested test package:

1. Connect a personal mailbox with IMAP enabled; cancel/deny and retry; do not
   leave a partially configured account. Test a wrong account selection.
2. Restart normally; open folders/body and change read/unread state. Verify the
   change both in Task Flyout and another authorized mail client.
3. Connect two distinct Microsoft mailboxes. Confirm folders, content, unread
   counts, notifications and token restoration stay account-specific.
4. Invalidate consent, then allow polling or load folders. No browser should open;
   explicit reconnect must target the same account without a duplicate entry.
5. Explicitly send only a user-approved test message. Check SMTP incremental
   consent, cancellation, delivery and the existing send-outcome confirmation UI.
6. Remove one account, verify its cache is gone and the other IMAP and Graph/
   calendar connections still restore. Exercise cleanup failure/retry separately.
7. Try the school account under school policy. Admin-required or disabled-protocol
   responses are external blockers, not proof of a defective password field.

No real account login, outgoing message, installation, app restart or external
consent change was performed while implementing this option.

## Sources

- [Exchange protocol OAuth and SASL XOAUTH2](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth)
- [Outlook.com POP, IMAP and SMTP settings](https://support.microsoft.com/en-us/office/pop-imap-and-smtp-settings-for-outlook-com-d088b986-291d-42b8-9564-9c414e2aa040)
- [Exchange Online authenticated SMTP settings](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission)
- [Consent review and unsent school-IT draft](microsoft-oauth-consent.md)
