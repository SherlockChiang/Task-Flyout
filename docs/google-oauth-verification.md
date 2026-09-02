# Google OAuth Verification Resubmission Packet

This document is the repository source of truth for resubmitting Task Flyout's
Google OAuth verification. It does not claim that Google will approve the app.
The previous review was closed because no update was received within 90 days;
the rejection email did not identify a scope, privacy, or data-safety violation.

Official references checked on 2026-09-01:

- [OAuth app verification](https://support.google.com/cloud/answer/13463073)
- [Restricted scope verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification)
- [Gmail API scopes](https://developers.google.com/workspace/gmail/api/auth/scopes)
- [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy)

## Final Scope Set

| Scope | User-facing use | Why a narrower scope is insufficient | Representative API calls |
| --- | --- | --- | --- |
| `https://www.googleapis.com/auth/calendar` | Display calendars and events; create, edit, and delete events. | Read-only calendar access cannot provide the visible event editing workflow. | Calendar list/events list, insert, update, delete. |
| `https://www.googleapis.com/auth/tasks` | Display task lists and tasks; create, edit, complete, and delete tasks. | Read-only task access cannot provide the visible task management workflow. | Tasklists list; tasks list, insert, update, delete. |
| `https://www.googleapis.com/auth/gmail.modify` | List labels and messages, open bodies, mark read/starred, archive, move between labels, move to trash/restore, and send mail. | `gmail.readonly` cannot change message state; `gmail.send` cannot read or manage messages. Google's catalog states that `gmail.modify` covers read, compose, and send, so requesting all three would be redundant. | `Users.Labels.List`; `Users.Messages.List`, `Get`, `Modify`, `Trash`, `Untrash`, and `Send`. |

Task Flyout does not request `https://mail.google.com/` and does not immediately
and permanently delete Gmail messages while bypassing trash.

## Architecture And Data Flow

1. The user explicitly clicks Connect Google or Add Gmail.
2. The system browser completes Google's installed-app OAuth flow.
3. Access and refresh tokens are encrypted locally with Windows DPAPI
   (`CurrentUser` plus application entropy).
4. The installed app communicates directly with Google APIs. There is no
   developer-operated backend, analytics endpoint, advertising SDK, or remote
   token store.
5. Calendar, task, and mail metadata needed for offline or notification features
   is cached locally with DPAPI protection. Gmail bodies are fetched on demand
   and stripped from persistent message windows; one unsent recovery draft can
   be retained locally with DPAPI protection until sent or discarded.
6. Removing only the Gmail feature deletes that mail account, draft, and cache
   while preserving shared Google authorization used by Calendar/Tasks. Choosing
   Disconnect completely clears Google authorization first, then removes Google
   calendar, task, mail, draft, and embedded-browser data.

Google user data is used only for prominent user-facing Task Flyout features. It
is not sold, transferred to advertisers or data brokers, used for advertising,
credit decisions, profiling, or training generalized AI/ML models. The developer
cannot read it because the app has no developer backend.

Because restricted data remains on the user's device and is not stored or
transmitted through a developer server, the repository records that server-side
restricted-data processing is absent. Google makes the final determination on
whether an external security assessment is required.

## Public Website And Branding

Before submitting, verify these values in OAuth Branding and Search Console:

- App name: `Task Flyout` everywhere, including the consent screen and video.
- Home page: `https://sherlockchiang.github.io/Task-Flyout/`.
- Privacy policy: `https://sherlockchiang.github.io/Task-Flyout/privacy.html`.
- Both URLs are public, use the same verified domain, and the home page visibly
  describes Calendar, Tasks, and Gmail features and links to the privacy policy.
- The support email and every developer-contact mailbox are monitored.
- Branding is verified and published before requesting Data Access verification.
- Only production-ready OAuth clients remain in the verification project.

Do not put reviewer credentials, refresh tokens, client secrets, or personal
mail/calendar data in this repository or in a public issue.

## Live Console Audit (2026-09-01)

A read-only review of the production Google Auth Platform project found:

- Branding contains the expected `Task Flyout` name, public home page, privacy
  policy, authorized `sherlockchiang.github.io` domain, logo, support contact,
  and developer contact. Google still displays the previous review issue that
  ownership of `https://sherlockchiang.github.io/Task-Flyout/` was not verified,
  so the brand is not currently displayed to users.
- A subsequent read-only Search Console review found that the exact URL-prefix
  property already exists, the signed-in project owner is a verified owner, and
  HTML-tag verification is currently successful. The property was added on
  2026-03-25, and the verification meta tag is also present on the published
  `master:/docs/index.html`. The Cloud Branding issue is therefore a stale
  previous-review finding that must be resubmitted as resolved, not evidence
  that ownership is presently absent.
- Audience is External and In production. The console reports 3 of the
  unverified-app lifetime limit of 100 users.
- One active Desktop OAuth client named `Task Flyout` is present.
- Data Access still lists the former five-scope set: Calendar, Tasks, Gmail
  Modify, Gmail Readonly, and Gmail Send. Both scope-justification fields and
  the YouTube demonstration field are empty.
- Verification Center records that the previous Branding and Data Access
  submissions were not approved. `Prepare for verification` is disabled until
  the homepage ownership issue is resolved and the brand is verified/published.
- The overview reports current contact information and correctly configured
  project owners/editors. It also reports that no Cloud Billing account is
  associated; this is recorded for completeness and is not treated here as a
  verified submission requirement.

Required order from the observed console state:

1. Completed on 2026-09-01: PR #2 was merged as `1837427`, master Quality run
   `33518068424` passed, and Pages deployment `33518065281` published the
   updated `docs/` site. The live home page and privacy policy expose the
   Calendar, Tasks, Gmail Modify, data-flow, retention, and Limited Use text.
2. Current action: in Branding, mark the previous homepage-ownership issue as
   resolved and request brand re-verification; do not recreate the already
   verified Search Console property.
3. Remove Gmail Readonly and Gmail Send from Data Access so the console matches
   the three scopes requested by the current app build.
4. Enter the scope justifications and unlisted YouTube URL.
5. Return to Verification Center only after `Prepare for verification` becomes
   enabled.

As of 2026-09-02, step 3 is complete: Data Access was saved with exactly
Calendar, Tasks, and Gmail Modify. The scope-justification fields and YouTube
field remain empty, and Verification Center still requires the brand to be
verified and published before `Prepare for verification` can be used.

## Paste-Ready Scope Justification

Task Flyout is a local-first Windows 11 productivity app. Users explicitly
connect Google to view and manage Google Calendar events, Google Tasks, and Gmail
inside the app. Calendar access is read/write because the UI creates, edits, and
deletes events. Tasks access is read/write because the UI creates, edits,
completes, and deletes tasks. Gmail Modify is required because the Mail UI reads
messages and performs visible user-requested state changes including mark
read/starred, label changes, archive, trash/restore, and send. Gmail Readonly plus
Gmail Send would still not permit those state changes, while Gmail Modify already
covers read, compose, and send; therefore the app requests Gmail Modify alone.
The app does not request full mail access and does not permanently delete Gmail
messages while bypassing trash. Tokens and caches are DPAPI-protected on the
user's Windows device, requests go directly to Google, and no Google user data is
sent to or stored by a developer-operated server.

## Demonstration Video Shot List

Upload one unlisted YouTube video. Use a dedicated reviewer/test account with
synthetic data and an English Google authorization flow.

1. Show the public Task Flyout home page and privacy-policy URL.
2. Start from a disconnected app and click the visible Connect Google action.
3. Record the English Google sign-in/consent flow. Keep the browser address bar
   readable so reviewers can see the OAuth client ID, and show the consent screen
   displaying the exact app name `Task Flyout` and the three requested scopes.
4. Calendar: load events, create one, edit it, and delete it.
5. Tasks: load a list, create one task, edit/complete it, and delete it.
6. Gmail: load labels/messages, open a body, mark a message read/starred, perform
   an archive or label action and undo it, move a message to trash/restore it,
   then send a synthetic message to the same test account.
7. Restart the app to show silent token restoration and direct data loading.
8. Revoke access from the test Google account, restart Task Flyout, and show the
   reconnect-required state without a browser appearing from background sync.
9. Reconnect explicitly, then choose Disconnect completely and show that Google
   calendar/tasks/mail disappear and a later connection requires consent again.

Do not edit the recording in a way that hides the consent URL, app name, or the
connection between a requested scope and its user-facing workflow. Blur only
credentials and synthetic message content that is not needed as evidence.

## Reviewer Test Instructions

Provide these instructions and test-account credentials only through Google's
verification form or requested secure channel:

1. Install the signed x64 release on Windows 11 and launch Task Flyout.
2. Select Add account, choose Google, and sign in with the supplied test account.
3. Use Calendar to create/edit/delete the event named `OAuth Review Event`.
4. Use Tasks to create/complete/delete `OAuth Review Task`.
5. Open Mail, select Gmail Inbox, open `OAuth Review Message`, change its state,
   archive/restore it, and send a reply to the same test account.
6. Remove the Google account and choose Disconnect completely to verify deletion.

The test account must contain only synthetic data and remain usable throughout
the review. Verify the password and any 2-step-verification instructions before
submission; never commit them.

## Console Submission Checklist

- Publish the updated home page and privacy policy first; verify both return 200
  without sign-in and the privacy link stays on the same domain.
- In Data Access, declare exactly the three scopes in this document and remove
  `gmail.readonly` and `gmail.send` from the requested list.
- Add up to three relevant documentation links, including the home page, privacy
  policy, and public repository or feature documentation.
- Select the permitted application type that accurately describes a local
  productivity/email client.
- Paste the scope justification, add the unlisted YouTube link, reviewer steps,
  and secure test credentials, then submit the new request.
- Check Verification Center plus the support/developer-contact inboxes and spam
  folders at least weekly. Respond to every Trust & Safety request before its
  deadline; the previous ticket was closed after 90 days without an update.
- If Google requests a security assessment, answer with the exact direct-device
  architecture and follow its determination rather than claiming an exemption.

## Repository Evidence

- Scope declaration: `Services/ProviderAuthorizationScopePolicy.cs`
- Explicit-only Google authorization: `Services/GoogleSyncProvider.cs`
- Gmail API operations: `Services/MailService.cs`
- Provider-wide deletion: `Services/ProviderAuthorizationLifecycle.cs` and
  `App.xaml.cs`
- Protected token storage: `Services/ProtectedGoogleDataStore.cs`
- Public disclosures: `docs/index.html` and `docs/privacy.html`
- Manual runtime checks: `docs/manual-verification.md`
