# Task Flyout Improvement Todo

This file is the local source of truth for project improvements. Keep each implementation focused, verify it independently, and record its commit after completion.

Status values:

- `TODO`: not started
- `IN PROGRESS`: active work
- `DONE`: implemented and verified
- `BLOCKED`: requires an external decision, credential, service, or environment

## P0 - Release And Data Integrity

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P0-01 | DONE | Security | Split CI restore/test/build from signing and release secrets. | Tests run without OAuth/signing secrets; signing secrets exist only in a protected release job; release permissions are scoped to that job. | `ci: isolate beta signing secrets` |
| P0-02 | DONE | Data integrity | Serialize and version calendar cache persistence. | An older asynchronous save cannot overwrite a newer cache snapshot; failures are observable; concurrency behavior is tested. | `fix: serialize calendar cache persistence` |
| P0-03 | DONE | Privacy | Fail account removal when Google legacy token cleanup fails. | Legacy token deletion errors propagate; the account remains available for retry; failure behavior is tested. | `privacy: fail closed on legacy token cleanup` |
| P0-04 | DONE | UX | Allow onboarding to finish without configuring every integration. | Users can finish or skip setup; onboarding completion is versioned and independent of provider readiness. | `ux: make onboarding integrations optional` |

## P1 - Responsiveness And Core UX

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P1-01 | DONE | Performance | Move RSS initialization and first-page loading off the UI thread. | First page load does not synchronously initialize/query/decrypt the full RSS cache; the first page is loaded asynchronously. | `perf: move RSS initialization off the UI thread` |
| P1-02 | DONE | Performance | Stop loading 1,000 RSS articles before SQL paging. | RSS service keeps subscriptions/folders in memory and queries article pages directly; startup allocations are reduced. | `perf: avoid preloading RSS articles` |
| P1-03 | DONE | Lifecycle | Cancel RSS refresh when the page unloads. | Feed and image work observe page cancellation; unloaded pages receive no refresh UI updates. | `fix: cancel RSS refresh on page unload` |
| P1-04 | DONE | Performance | Replace serial eager RSS article-image downloads with lazy bounded downloads. | Feed metadata is persisted without waiting for article images; reader images load on demand; global/per-host proxy concurrency is bounded. | `perf: defer RSS article image downloads` |
| P1-05 | DONE | Correctness | Make weather city suggestions typed, debounced, and cancellable. | Stale searches cannot replace newer results; city and coordinates update atomically; stale results are rejected. | `fix: prevent stale weather city selections` |
| P1-06 | DONE | UX | Add adaptive layouts for Calendar, Mail, and Tasks. | Shared wide, medium, and narrow breakpoints collapse auxiliary panes and provide single-pane navigation so primary actions remain reachable at high scaling. | `ux: add adaptive core layouts` |
| P1-07 | DONE | Accessibility | Localize accessible names, tooltips, onboarding, and RSS reader messages. | English and Simplified Chinese resources have parity; English UI exposes no hard-coded Chinese accessibility text. | `localization: complete accessible UI resources` |
| P1-08 | DONE | Accessibility | Make weather bar and agenda cards keyboard-invokable. | Controls expose Invoke semantics, Enter/Space activation, localized names, and visible focus. | `accessibility: make weather and agenda keyboard invokable` |
| P1-09 | DONE | UX | Enable scrolling in constrained flyout content and zoom in mail HTML. | All flyout controls remain reachable at high scaling; mail supports touch and keyboard zoom. | `accessibility: enable flyout scrolling and mail zoom` |
| P1-10 | DONE | Reliability | Unify task mutation states and visible retry behavior. | Calendar, Tasks, and Flyout use a shared per-task mutation queue and consistently show pending, failed, queued, succeeded, and retry states instead of silent rollback. | `reliability: expose task mutation retries` |
| P1-11 | DONE | UX | Preserve quick-create input when submission fails. | The form remains available; errors are localized and redacted; retry is direct. | `ux: preserve failed quick-create input` |
| P1-12 | DONE | Security | Define provider-wide mail/calendar authorization lifecycle. | Feature removal explicitly preserves shared authorization; complete disconnect removes provider tokens and all local mail/calendar/task data, failing closed if token cleanup fails. | `security: define provider disconnect lifecycle` |

## P2 - Privacy, Efficiency, And Maintainability

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P2-01 | DONE | Privacy | Protect weather location/cache and add a deletion action. | Coordinates and cache migrate to DPAPI storage; users can stop tracking and clear weather/location data; the existing location notice explains provider use. | `privacy: protect weather location data` |
| P2-02 | DONE | Privacy | Couple WebView2 browsing-data cleanup to mail/RSS deletion. | Shared-profile site data, history, and disk cache are cleared after sensitive data deletion; both confirmation dialogs explain the cross-reader impact. | `privacy: clear embedded browser data` |
| P2-03 | DONE | Security | Default RSS to HTTPS and block silent downgrade redirects. | HTTP requires per-subscription approval; HTTPS-to-HTTP downgrade is rejected. | `security: require approval for HTTP RSS feeds` |
| P2-04 | DONE | Security | Make local-network RSS permission subscription-specific. | Local access is scoped to an encrypted per-subscription authority, visible at approval, and rejected when redirects change host, port, or protocol. | `security: scope RSS local network access` |
| P2-05 | DONE | Security | Replace exception-string logging with structured diagnostics. | Persisted exceptions contain only allowlisted metadata and are stored in local, non-roaming app data with existing retention limits. | `diagnostics: persist structured exception metadata` |
| P2-06 | DONE | Supply chain | Allowlist NuGet sources and enforce locked restore for release builds. | Restore sources are explicit; every CI/release restore uses locked mode; vulnerability checks remain clean. | `security: lock release package restore` |
| P2-07 | DONE | Performance | Bound Google calendar request concurrency and add throttling retries. | Calendar fan-out has a configured limit and handles 429/transient failures with bounded backoff. | `perf: bound Google calendar requests` |
| P2-08 | DONE | Performance | Reduce calendar cache deep cloning. | Published snapshots reuse unchanged day buckets; tests show isolated bucket cloning and less than one-quarter of full-clone allocations for an unchanged large cache. | `perf: reuse unchanged calendar cache buckets` |
| P2-09 | DONE | Performance | Bound RSS host-gate retention and reduce payload copies. | Host coordination storage is fixed at 64 gates with global concurrency 4; WebView image payloads stream directly without a second full-buffer copy. | `perf: bound RSS host concurrency gates`, `perf: stream proxied images to WebView` |
| P2-10 | DONE | Startup | Remove protected SQLite work from the synchronous startup path. | Calendar account hydration runs once in deferred background work after tray creation; windows and scheduled services await the shared task before consuming accounts. | `perf: defer protected account hydration` |
| P2-11 | DONE | Efficiency | Rotate diagnostics and batch notification-state writes. | Notification checks persist state at most once; long-running diagnostic logs rotate at a fixed size limit. | `perf: batch notification state writes`, `diagnostics: bound log retention` |
| P2-12 | DONE | Localization | Respect culture first-day-of-week and consistent language fallback. | Calendar follows the selected culture; unsupported weather languages fall back to English. | `localization: respect culture calendar layout` |
| P2-13 | DONE | Architecture | Extract shared mutation, account-removal, status, and remote-image coordinators. | Pages use shared task queues/status mapping, provider lifecycle orchestration, general status formatting, and a singleton safe remote-image proxy without a UI rewrite. | `refactor: consolidate shared page coordinators` |
| P2-14 | TODO | Testing | Add localization, accessibility, responsive UI, lifecycle, and soak coverage. | Resource parity is automated; packaged smoke tests cover keyboard/narrow layouts; soak checks track handles and memory. | |

## P3 - Product Features

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P3-01 | DONE | Feature | Add a unified account health and offline center. | Users can see provider health, last success, cached state, pending mutations, and reconnect/retry actions. | `feat: add account health and offline center` |
| P3-02 | DONE | Feature | Add local search for Tasks, Mail, and RSS. | Debounced local search covers cached task metadata, loaded mail metadata, and encrypted RSS metadata with filter-aware paging. | `feat: add local search across core pages` |
| P3-03 | DONE | Feature | Add RSS OPML import/export. | Import previews new, duplicate, folder, and HTTP counts, maps folders without eager network fetches, and export produces portable OPML. | `feat: add RSS OPML import and export` |
| P3-04 | DONE | Feature | Add RSS read/unread and starred states. | Opening marks articles read, list actions toggle read and starred state, state survives refresh, and SQLite paging supports All, Unread, and Starred filters. | `feat: add RSS article states and filters` |
| P3-05 | DONE | Feature | Add complete task editing. | Users can edit all provider-supported title, due date, notes, completion, and deletion fields; provider/list identity is honored and unsupported time, recurrence, and move fields are explicit. | `feat: add list-aware task editing` |
| P3-06 | DONE | Feature | Add toast actions for snooze, complete, and open. | Agenda toasts use opaque protected capabilities, strict activation schemas, persisted 10-minute snooze, and shared task mutation for eligible completion. | `feat: add secure agenda toast actions` |
| P3-07 | DONE | Feature | Add protected compose draft recovery. | DPAPI-protected drafts autosave across navigation and exit, offer restore/discard, survive send failures, and are securely deleted after success or account removal. | `feat: add protected compose draft recovery` |
| P3-08 | DONE | Feature | Add mail attachments. | Compose supports ephemeral attachment selection/removal, bounded file and total sizes, staged progress, and Outlook, Gmail, and SMTP payloads. | `feat: add bounded mail attachments` |
| P3-09 | DONE | Feature | Add concise tray quick actions and status. | The flat tray menu exposes new item, sync, compose, weather, open, and exit with one privacy-safe status line. | `feat: add tray quick actions and status` |

## Verification Baseline

Before this todo was created:

- `dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj -c Debug --no-restore`: 382 passed, 0 failed, 0 skipped.
- `dotnet list Task_Flyout.csproj package --vulnerable --include-transitive --no-restore`: no known vulnerable packages from the configured sources.
- `credentials.json`, `Secrets.cs`, and `*.pfx` are not tracked by Git.
