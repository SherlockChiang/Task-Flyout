# Microsoft Outlook Consent Review

Reviewed on 2026-10-01 against source `0894b85` and the user's report from
candidate `1.4.5.5`. This is a source/documentation review, not an inspection
of the school's Microsoft Entra tenant or a captured OAuth request.

## Current conclusion

The user still sees **Need admin approval** and **unverified** when signing
in with a school account. Reducing requested scopes does not establish that
an organization will allow user consent. The earlier claim that the calendar
and task scopes explained this particular approval requirement was unproven.
The exact policy/error code requires the tenant's sign-in/consent diagnostics.

Microsoft lists delegated `Mail.ReadWrite` as not intrinsically requiring
admin consent. Organizations can nevertheless restrict user consent by app,
publisher, or permission. Microsoft's currently documented recommended policy
restricts user consent for both `Mail.Read` and `Mail.ReadWrite`, with separate
exceptions for selected mail clients. We have not established which policies
the school uses. Changing to read-only mail would not guarantee access and
would remove support for read/unread updates and other mailbox mutations.

Sources: [Graph permission reference](https://learn.microsoft.com/en-us/graph/permissions-reference#mailreadwrite),
[user/admin consent](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/user-admin-consent-overview),
[managed consent policies](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/manage-app-consent-policies).

## Scope and implementation evidence

| Entry/path in 1.4.5.5 | Source behavior | Implication |
| --- | --- | --- |
| First standalone Outlook add, no connected agenda account | `ConnectMailInteractivelyAsync` requests `User.Read` and `Mail.ReadWrite`. | No explicit calendar, task, or send scope in this interactive request. MSAL's standard sign-in/session scopes are separate. |
| Outlook add with an existing Microsoft agenda account | MailPage first calls `EnsureAuthorizedAsync`, whose silent restoration uses core calendar/task scopes. | Mail setup still unnecessarily depends on agenda authorization. |
| Successful standalone mail sign-in | The provider assigns a mail-scoped client to `_graphClient`, which is also the agenda client. | A non-null client does not prove calendar/task authorization. |
| Mail account saved or already present | `AddOutlookAccountAsync` calls `EnsureProviderAgendaAccountAsync`. | A mail-only connection can create an agenda account and try fetching calendars without their permissions. |
| Runtime mailbox authorization | `EnsureScopesAuthorizedAsync` silently acquires a token, then falls back to interactive consent. | Its callers include mail polling; explicit-only interaction is not fully enforced. |

Relevant files: `Services/ProviderAuthorizationScopePolicy.cs`,
`Services/MicrosoftSyncProvider.cs`, `Services/MailService.cs`, and
`Views/MailPage.xaml.cs`. These are code findings; no claim is made that the
post-consent defects caused the school's pre-consent approval screen.
The earlier 1343 passing pure-logic tests and successful package build did
not exercise these live MSAL/Graph paths.

## Available resolution paths

An alternative **Outlook IMAP (OAuth2)** option is now implemented in source;
see [its setup and acceptance checklist](outlook-imap-oauth.md). It uses delegated
Exchange IMAP/SMTP scopes, not Graph tokens. It still requires the registration
and mailbox protocol settings to permit access, and the school's consent policy
can require admin approval for this method too. Neither basic authentication nor
an application password is offered as a bypass. Real protocol acceptance and
registration configuration have not been performed.

1. Ask the school's IT administrator to review **Task Flyout** and its delegated
   mailbox permissions. Use the app's actual Application (client) ID from its
   Entra registration, not its display name alone. The administrator determines
   whether access can be approved and limited to the intended user(s).
2. For broader distribution, evaluate Microsoft Entra publisher verification.
   It requires a verified Partner Center organization and an associated app
   registration/domain meeting Microsoft's requirements. The existing local
   MSIX signing certificate does not provide this verification. Verified
   status is one policy input and cannot guarantee school approval.
3. A personal Outlook account can provide a separate functional smoke test.
   Its success would not establish that the school account is approved.

Source: [publisher verification requirements](https://learn.microsoft.com/en-us/entra/identity-platform/publisher-verification-overview).

## Draft for school IT (not sent)

Subject: Request to review Task Flyout Microsoft 365 delegated mailbox access

I would like to use Task Flyout, a Windows desktop client, with my university
mailbox. Microsoft sign-in currently reports “Need admin approval” and shows
the publisher as unverified.

- Application (client) ID: [copy from Task Flyout's Microsoft Entra app registration]
- User: [provide the university address privately in the IT ticket]
- Initial requested Graph delegated permissions: `User.Read`, `Mail.ReadWrite`.
- Purpose: identify the signed-in user; read messages; synchronize read/unread
  state; support mailbox actions such as archive and move to trash.
- Mail sending is a separate feature that requests `Mail.Send` on use. It is
  not needed for the initial mailbox connection.
- This flow uses interactive user sign-in, not app-only access to all mailboxes.

Could you check the applicable consent policy and whether this application can
be approved for my account under university policy? Please let me know if you
need a sign-in timestamp, error code, or correlation/request ID to locate the
failure. I am not requesting that organization-wide consent restrictions be
disabled.

Do not attach tokens, passwords, authorization codes, or the complete sign-in
URL to the ticket. The client ID is public app identification; it is not a
client secret. The placeholders above still need to be filled before sending.

## Implementation status and pending acceptance

- `304657c` removed automatic agenda creation and the mail-scoped assignment
  to the agenda client. Existing agenda entries are not automatically removed.
- The 2026-10-07 follow-up removes the agenda prerequisite from Outlook setup.
  The explicit Add Outlook action now restores the stored identity's mailbox
  token first, and requests only mailbox scopes if interaction is necessary.
- Runtime mailbox restoration now has no interactive callback. Missing or
  expired consent throws `AuthorizationInteractionRequiredException`; folder
  and message loading present a localized reconnect action through Add Outlook.
- The explicit Send action restores or requests sending consent before its
  transport timeout starts. It does not add calendar or task permissions.
  Its draft, send, confirmation, and cleanup requests retain the operation's
  client so polling cannot replace sending scopes during the operation.
- Explicit mail authorization is bounded to five minutes; account setup also
  exposes a Cancel button. Cancellation/failure releases the provider lock and
  restores the setup buttons. A different account returned for a known stored
  identity is rejected before replacing the protected home-account record.
- Nine new tests exercise the production fallback helper: silent reuse,
  runtime missing consent, explicit fallback, network failure, cancellation,
  and denial without retry. All 1352 Release tests pass. These tests do not run
  a real browser/MSAL session or inspect the school's tenant policies.
  Debug x64 application build passes with zero warnings and errors.
- Candidate `1.4.5.7` from `720017e` is built and signed with the existing
  certificate. Package identity, SignTool, and Authenticode checks pass; it has
  not been installed. The exact path and hash are in [the todo archive](improvement-todo.md#2026-10-07-outlook-explicit-authorization-candidate).
- Verify no-account, existing agenda, expired consent, denied/admin-required,
  browser cancellation, background polling, and later calendar opt-in paths.
- Record source/build validation separately from live tenant approval. Do not
  mark the school-account blocker resolved on the basis of another build.

No app registration, tenant setting, publisher status, consent grant, or
account data was changed while implementing this follow-up. No request was
sent to IT. Code completion does not resolve the external approval requirement.
