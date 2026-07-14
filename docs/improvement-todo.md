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
| P1-03 | TODO | Lifecycle | Cancel RSS refresh when the page unloads. | Feed, image, and database work observe page cancellation; unloaded pages receive no UI updates. | |
| P1-04 | TODO | Performance | Replace serial eager RSS image downloads with lazy bounded downloads. | Feed metadata appears before images; global/per-host concurrency is bounded; duplicate URLs are coalesced. | |
| P1-05 | TODO | Correctness | Make weather city suggestions typed, debounced, and cancellable. | Stale searches cannot replace newer results; city and coordinates update atomically; race behavior is tested. | |
| P1-06 | TODO | UX | Add adaptive layouts for Calendar, Mail, and Tasks. | Wide, medium, and narrow window states remain usable at 200% scaling without clipped primary actions. | |
| P1-07 | TODO | Accessibility | Localize accessible names, tooltips, onboarding, and RSS reader messages. | English and Simplified Chinese resources have parity; English UI exposes no hard-coded Chinese accessibility text. | |
| P1-08 | TODO | Accessibility | Make weather bar and agenda cards keyboard-invokable. | Controls expose Invoke semantics, Enter/Space activation, localized names, and visible focus. | |
| P1-09 | TODO | UX | Enable scrolling in constrained flyout content and zoom in mail HTML. | All flyout controls remain reachable at high scaling; mail supports touch and keyboard zoom. | |
| P1-10 | TODO | Reliability | Unify task mutation states and visible retry behavior. | Calendar, Tasks, and Flyout consistently show pending, failed, queued, and retry states instead of silent rollback. | |
| P1-11 | TODO | UX | Preserve quick-create input when submission fails. | The form or draft remains available; errors are localized and redacted; retry is direct. | |
| P1-12 | TODO | Security | Define provider-wide mail/calendar authorization lifecycle. | Removing one feature does not silently retain or invalidate shared authorization; complete disconnect is available. | |

## P2 - Privacy, Efficiency, And Maintainability

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P2-01 | TODO | Privacy | Protect weather location/cache and add a deletion action. | Coordinates and cache are protected; users can clear weather/location data; third-party recipients are explained. | |
| P2-02 | TODO | Privacy | Couple WebView2 browsing-data cleanup to mail/RSS deletion. | Relevant profile data is cleared after sensitive data deletion; shared-profile implications are documented or profiles are separated. | |
| P2-03 | TODO | Security | Default RSS to HTTPS and block silent downgrade redirects. | HTTP requires per-subscription approval; HTTPS-to-HTTP downgrade is rejected or explicitly confirmed. | |
| P2-04 | TODO | Security | Make local-network RSS permission subscription-specific. | Local access is scoped, visible, and revalidated when the destination changes. | |
| P2-05 | TODO | Security | Replace exception-string logging with structured diagnostics. | External URLs omit query/fragment; sensitive fields are allowlisted; logs rotate by age and size. | |
| P2-06 | TODO | Supply chain | Allowlist NuGet sources and enforce locked restore for release builds. | Restore sources are explicit; every CI/release restore uses locked mode; vulnerability checks remain clean. | |
| P2-07 | TODO | Performance | Bound Google calendar request concurrency and add throttling retries. | Calendar fan-out has a configured limit and handles 429/transient failures with bounded backoff. | |
| P2-08 | TODO | Performance | Reduce calendar cache deep cloning. | Unchanged day buckets are shared or persisted incrementally; allocation benchmark shows improvement. | |
| P2-09 | TODO | Performance | Bound RSS host-gate retention and reduce payload copies. | Host coordination storage is bounded; image/feed transfer avoids unnecessary full-buffer copies. | |
| P2-10 | TODO | Startup | Remove protected SQLite work from the synchronous startup path. | Startup diagnostics include the full launch path; account hydration is deferred or asynchronous. | |
| P2-11 | TODO | Efficiency | Rotate diagnostics and batch notification-state writes. | Logs have retention limits; one notification check persists state at most once. | |
| P2-12 | TODO | Localization | Respect culture first-day-of-week and consistent language fallback. | Calendar follows locale or user override; unsupported languages fall back to English. | |
| P2-13 | TODO | Architecture | Extract shared mutation, account-removal, status, and remote-image coordinators. | Duplicated page behavior uses testable shared components without a wholesale UI rewrite. | |
| P2-14 | TODO | Testing | Add localization, accessibility, responsive UI, lifecycle, and soak coverage. | Resource parity is automated; packaged smoke tests cover keyboard/narrow layouts; soak checks track handles and memory. | |

## P3 - Product Features

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| P3-01 | TODO | Feature | Add a unified account health and offline center. | Users can see provider health, last success, cached state, pending mutations, and reconnect/retry actions. | |
| P3-02 | TODO | Feature | Add local search for Tasks, Mail, and RSS. | Cached metadata is searchable with feature-appropriate filters and responsive cancellation. | |
| P3-03 | TODO | Feature | Add RSS OPML import/export. | Import previews changes and handles duplicates/folders; export produces portable OPML. | |
| P3-04 | TODO | Feature | Add RSS read/unread and starred states. | State persists locally and supports All, Unread, and Starred filters. | |
| P3-05 | TODO | Feature | Add complete task editing. | Users can edit supported title, date/time, notes, recurrence, provider/list, completion, and deletion fields. | |
| P3-06 | TODO | Feature | Add toast actions for snooze, complete, and open. | Arguments are validated; actions use the shared mutation path; privacy-safe variants remain available. | |
| P3-07 | TODO | Feature | Add protected compose draft recovery. | Unsent drafts survive navigation/process exit and can be discarded securely. | |
| P3-08 | TODO | Feature | Add mail attachments. | File and total size limits, removal, progress, and provider errors are handled. | |
| P3-09 | TODO | Feature | Add concise tray quick actions and status. | New item, sync, compose, weather, and error status are available without making the menu cluttered. | |

## Verification Baseline

Before this todo was created:

- `dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj -c Debug --no-restore`: 382 passed, 0 failed, 0 skipped.
- `dotnet list Task_Flyout.csproj package --vulnerable --include-transitive --no-restore`: no known vulnerable packages from the configured sources.
- `credentials.json`, `Secrets.cs`, and `*.pfx` are not tracked by Git.
