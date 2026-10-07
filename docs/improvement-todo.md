# Task Flyout Improvement Todo

This file is the local source of truth for project improvements. Keep each implementation focused, verify it independently, and record its commit after completion.

Status values:

- `TODO`: not started
- `IN PROGRESS`: active work
- `DONE`: implemented and verified
- `BLOCKED`: requires an external decision, credential, service, or environment
- `PAUSED`: intentionally archived; no further implementation or runtime attempts

## Current Queue - 2026-09-08 Implementation Resumed

This queue takes precedence over the older backlog order. The evidence and
sequencing rationale are in [optimization-roadmap.md](optimization-roadmap.md).
Do not treat policy-unit-test success as proof of WinUI event wiring, packaged
input behavior, live provider isolation, or private Explorer compatibility.

| Order | ID | Status | Next bounded outcome | Verification / dependency |
| --- | --- | --- | --- | --- |
| Accepted | M1-16 | DONE | Preserve the accepted single-click Flyout / double-click main-window / outside-click dismissal behavior. | User accepted candidate `1.4.4.4`; implementation `433c171`, 9 focused and 1264 total tests passed. Extended stress scenarios remain regression checks, not separately observed results. |
| Accepted | R1-03 | DONE | Preserve repaired mail-notification activation. | User reports the original symptom resolved on installed/running `1.4.4.6`; fix `0d4dad8`. This does not independently validate every cold-start or multi-account scenario. |
| Archived | F0-01 | PAUSED | Preserve the partial observations; no further benchmark runs requested. | User ended benchmarking on 2026-09-08. No complete P95, controlled prewarm comparison, frame trace or improvement claim. Retain the corrected toggle-without-restart annotation; do not ask for more samples. |
| 2 | R1-01 | DONE | Preserve account identity in global-search agenda deduplication and result keys. | Both operations use one structural identity, including account and item kind. Two-account collisions and original payload lookup are tested; 15 new tests, 1322 total, clean Debug x64 build. Not yet packaged or live-tested with two accounts. |
| Deferred | F1-01 | PAUSED | Keep existing efficiency and working-set policy. | Current observations do not justify a policy change; user ended the benchmark pass. No new trim or animation experiment. |
| Now | M1-07 | IN PROGRESS | Accept the normal asynchronous-search candidate. | Signed Release x64 `1.4.4.8` from `b17f429` is ready and statically verified; 1342 Debug/Release tests passed. No installation/restart or diagnostic sampling performed. Check ordinary search open/input/close and correct result navigation, not timing loops. |
| 5 | F1-02 | TODO | Apply calendar changes without rebuilding unchanged visible cells/timeline geometry. | Profile publication and manual-sync paths; avoid duplicate application of the same state, preserve end times, source account, date selection, scroll and editing state. |
| 6 | F1-03 | TODO | Update displayed mail/search results incrementally. | Existing bulk-load suppression already exists; target `_displayedItems.Clear()` and churn in `ApplyMailSearch`, preserve selection/scroll, unread convergence and account-scoped navigation. |
| 7 | F1-04 | TODO | Profile HTML preparation and page lifetime before adjusting reader caching. | Separate sanitization, WebView initialization, navigation and network; stale account/message work never displays; preserve sanitization/remote-resource restrictions; no default global WebView prewarm. |
| Release gate | M3-09 / R1-02 | IN PROGRESS | Finish multi-Google acceptance and add PR WinUI build validation. | Multi-account implementation awaits its own two-account matrix; R1-02 remains TODO. Tray acceptance does not approve either. Do not auto-merge or publish. |
| Data gate | M1-03 | TODO | Preserve unreadable protected files and version the format before plaintext migration. | Still a pre-release requirement and prerequisite for persistence changes; recognized legacy JSON only, unreadable ciphertext stays byte-identical, atomic migration tested. |
| Later | M1-05 / M1-04 / M1-08 | TODO | Safely migrate storage, prune obsolete tokens and remove measured UI persistence stalls. | M1-03 first for persistence/migration changes; separate rollback-tested commits, not a directory rename or blanket Task.Run on UI-bound collections. |
| Later | M2-02 / M2-01 | PAUSED / TODO | Retain prewarm as-is; mail coordination remains a later implementation item. | User ended benchmark work; do not request more prewarm/idle samples. No default or power-policy change is justified by the partial observations. |
| Later | M1-02 / M2-05 / M2-06 / M2-04 / M2-07 | TODO | Improve existing controls/accessibility and incrementally share tested Core code. | Keep current WinUI design; no broad redesign, whole-repo formatting or whole-service extraction in the responsiveness pass. |
| External | M0-06 | BLOCKED | Resume OAuth submission after core workflows pass packaged validation. | Retain existing Gmail scope/function decisions; latest recorded console audit is 2026-09-02, not a current live claim. Recheck Branding, prepare demo, obtain approval for external submission. |
| External | MS-AUTH-01 | BLOCKED | Resolve school-account Microsoft consent approval. | User still reports admin approval on `1.4.5.5` (`0894b85`). Scope reduction does not override tenant policy; exact policy requires tenant diagnostics. See [review and unsent IT draft](microsoft-oauth-consent.md). |
| Follow-up | MS-AUTH-02 | IN PROGRESS | Accept isolated Outlook setup and silent runtime authorization. | Code complete on 2026-10-07: no agenda prerequisite/automatic agenda creation, separate mailbox client, silent runtime restoration, explicit incremental sending consent, cancel and five-minute timeout. 1352 Release tests pass, including 9 fallback/cancellation/denial tests; Debug x64 build has zero warnings/errors. Browser/tenant and packaged UI acceptance remain pending; MS-AUTH-01 stays externally blocked. |
| Follow-up | MS-AUTH-03 | IN PROGRESS | Accept the alternative Outlook IMAP/SMTP OAuth2 connection. | Code complete: personal/work-school endpoints, dedicated account caches, silent runtime tokens, explicit IMAP setup and incremental SMTP consent, cancellation and cleanup. 1383 Release tests pass (31 new policy tests); Debug x64 builds with zero warnings/errors. Live MSAL/protocol and packaged UI acceptance remain pending. Entra delegated Exchange permissions and mailbox protocol access must be checked externally. See [setup and acceptance](outlook-imap-oauth.md); this is not an admin-consent bypass. |
| Paused | M3-03 through M3-08 | PAUSED | Preserve experimental taskbar-weather work as an archive. | No Windhawk installation, new Explorer injection or diagnostic rebuild/signing/runtime experiments. Normal candidate packaging may sign the bundled components without running them. |

Candidate acceptance must also cover external read/unread changes with Mail open,
new-mail notification navigation to the correct account/message, automatic agenda
updates while Flyout is hidden, and editing/rendering both event endpoints.
M1-14/M1-15 retain their implementation completion records; these live checks are
release gates, not invented claims that they have already passed.

Only one implementation outcome is active at a time. Each gets its own commit,
tests/build and pending-or-passed runtime record. Keep PR #3 a draft until the
candidate acceptance and account-routing gaps are resolved; do not auto-merge.
Except for the explicitly recorded F0-01 instrumentation slice, the responsiveness
items above remain planned and unmeasured. Retain
one accepted candidate for comparison and use a focused follow-up performance
branch/PR for substantive changes instead of indefinitely expanding PR #3.

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
| P2-14 | DONE | Testing | Add localization, accessibility, responsive UI, lifecycle, and soak coverage. | Resource parity is automated; packaged smoke tests cover keyboard/narrow layouts; soak checks track handles and memory. | `test: add packaged smoke and soak coverage` |

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

## 2026-08 Maintenance Audit Backlog

This backlog records the next maintenance pass. Keep each change independently
reviewable, update its status in the implementation commit, and do not mix the
pre-existing iCloud/Traditional Chinese worktree with unrelated maintenance.

### P0 - Release Safety And Worktree Hygiene

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M0-01 | DONE | Git/CI | Gate beta publication on a tested package instead of every direct `master` push. | Candidate packaging and packaged smoke complete before publication; publication is manual or tag-driven; the required remote branch/environment protections are documented. | `ci: gate beta publication on packaged smoke` |
| M0-02 | DONE | Distribution | Make installation artifacts and documentation agree. | English and Chinese READMEs name the installer actually shipped by the beta workflow; the generated installer has a documented invocation path. | `docs: align beta install instructions` |
| M0-03 | DONE | Git | Split the existing iCloud, provider-capability, localization, weather, and documentation work into focused commits. | Every commit builds/tests at its dependency boundary; no required untracked source is omitted; unrelated maintenance is excluded. | `refactor: centralize provider capabilities`<br>`fix: preserve provider calendar colors`<br>`feat: implement iCloud CalDAV sync`<br>`feat: add iCloud account onboarding`<br>`feat(i18n): localize last-success status messages`<br>`feat(weather): localize forecasts and alerts`<br>`feat(i18n): add Traditional Chinese localization`<br>`fix(i18n): recognize Traditional Chinese content aliases`<br>`fix(i18n): guard resource-backed Chinese XAML fallbacks`<br>`fix(weather): resume tracking with specific location labels`<br>`docs: document iCloud and privacy handling`<br>`build: bump package version`<br>`chore(i18n): remove stale localization note` |
| M0-04 | DONE | Privacy/OAuth | Remove redundant Google Gmail scopes from initial consent. | New consent requests Calendar, Tasks, and Gmail Modify only; legacy five-scope tokens remain valid; a token without Gmail Modify requires an explicit reconnect and background paths remain non-interactive. | `privacy(oauth): minimize Google scopes` |
| M0-05 | DONE | Privacy/OAuth | Prepare a complete Google OAuth verification resubmission packet and align public disclosures. | Scope declarations, data-flow and Limited Use disclosures, reviewer/video checklists, and English/Chinese privacy and README text agree with the implementation; public links and HTML structure validate; no credentials or tokens are included. | `docs(oauth): prepare Google verification resubmission` |
| M0-06 | BLOCKED | External/OAuth | Resubmit the Google OAuth verification request with published branding, a demo, and a synthetic reviewer account. | Branding and domain ownership are published; Data Access declares exactly the three scopes; the unlisted English demo and secure reviewer credentials are supplied; Verification Center accepts the request and contact inboxes are monitored. | External: Google Cloud Console, Search Console, YouTube |

### P1 - Data Integrity, Reliability, And Visible Defects

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M1-01 | DONE | UX/Correctness | Stop representing flyout loading and sync failures as editable agenda items. | Loading, empty, and error states are non-invokable; sync failure exposes a direct retry action; regression coverage prevents placeholder editing. | `fix(flyout): separate agenda status from editable items` |
| M1-02 | TODO | UX/Reliability | Make startup-task changes transactional and visible. | The toggle is disabled while changing; every failure re-reads the real `StartupTask.State`; localized accessible feedback explains denial/failure. | `fix: report startup task state changes` |
| M1-03 | TODO | Data integrity | Add an explicit protected-file format before migrating plaintext. | DPAPI blobs carry a magic/version header; only positively identified legacy plaintext is migrated; corrupt or foreign-user blobs are preserved for recovery. | `fix: version protected local files` |
| M1-04 | TODO | Privacy/Lifecycle | Bound notification-action and verification-code retention. | Expired/invalid tokens are deleted on read and during bounded startup/heartbeat pruning; per-scope row counts remain bounded across restart. | `privacy: prune expired notification tokens` |
| M1-05 | TODO | Storage | Move device-local protected SQLite data from roaming to local app data. | Upgrade performs an atomic, verified, restart-safe migration with rollback; caches and device-bound DPAPI ciphertext no longer live under `%APPDATA%`. | `fix: migrate protected store to local app data` |
| M1-06 | TODO | Reliability | Propagate cancellation and timeout semantics through calendar/task mutations. | Provider list/CRUD APIs accept cancellation; page unload and exit release pending operations; ambiguous remote outcomes instruct the user to refresh. | `reliability: bound provider mutations` |
| M1-07 | IN PROGRESS | Performance | Remove synchronous cache/decryption work from global-search opening. | Implemented and build/unit-verified; shell/commands first, cancellable background index/query, source invalidation and body-free RSS payloads. Real WinUI functional acceptance remains pending, not a benchmark requirement. | `perf: build global search index asynchronously` |
| M1-08 | TODO | Performance | Move SQLite, DPAPI, and full-cache serialization off visible UI paths. | Locks contain memory-only changes/snapshot creation; a versioned background writer performs persistence; slow-disk tests show no long dispatcher stalls. | `perf: move cache persistence off ui thread` |
| M1-09 | TODO | Reliability | Replace blanket handling of unknown UI exceptions with scoped async boundaries. | Fire-and-forget operations use a named safe runner and restore flags/state; unknown global failures log and terminate or recover explicitly instead of always setting `Handled`. | `reliability: add scoped async exception boundaries` |
| M1-10 | DONE | Reliability/UI | Recover the taskbar weather bar after Explorer restarts. | Taskbar recreation triggers prompt reattachment or a throttled rebuild; disabled/re-enabled bars cannot reuse a dead native window; polling remains a fallback and the recovery matrix is tested. | `fix(weatherbar): recover after Explorer restarts` |
| M1-11 | DONE | Reliability/Startup | Keep tray-triggered Flyout construction behind stable account hydration. | The click path records request diagnostics and restores efficiency state on failure; the Flyout never enumerates the account collection while startup hydration is mutating it. | `fix(flyout): serialize account hydration before opening` |
| M1-12 | DONE | Reliability/Calendar | Refresh agenda data while the Flyout remains hidden. | The app heartbeat force-refreshes the shared agenda range at the configured interval after account hydration, throttles failed attempts, invalidates the reminder snapshot, and checks upcoming events against the refreshed cache. | `fix(calendar): refresh agenda while flyout is hidden` |
| M1-13 | DONE | Reliability/Mail | Open new-mail notification targets directly after background-only polling. | Prior cache/folder-identity work is retained. R1-03 repaired SDK argument transport; user reports mail-notification navigation fixed on `1.4.4.6`. Broader multi-account/cold-start acceptance remains a separate gate. | `fix(mail): open notification targets reliably`; `0d4dad8` |
| M1-14 | DONE | Data integrity/Mail | Keep unread state coherent across provider, folder views, cache, notifications, and background polling. | Read/unread mutations update every cached identity for the account, reconcile authoritative provider state after refresh, and cannot resurrect stale unread badges or notifications. | `fix(mail): converge mutable state by provider identity`, `fix(mail): reconcile authoritative unread snapshots`, `fix(mail): synchronize unread UI and notifications` |
| M1-15 | DONE | Reliability/Calendar | Correct event editor times, timeline duration, date-strip overflow, and automatic refresh. | Existing events hydrate both start and end controls; the day timeline renders the complete interval; the date header has no unintended horizontal scrollbar; foreground and hidden refresh paths reliably publish fresh data without overlap or stale replacement. | `fix(calendar): align event times and timeline`, `fix(calendar): publish background refreshes to open views`, `fix(google): route mutations to source calendar` |
| M1-16 | DONE | Reliability/Flyout | Reliably dismiss an unpinned Flyout and preserve distinct tray gestures. | User accepted `1.4.4.4` single-click, double-click and outside-click behavior. Explicit dismissal cancels pending Flyout work and stale hydration continuations. Preserve pinned/opening/closing/cold-start stress cases as the ongoing regression matrix. | `fix(flyout): close reliably on focus loss`, `fix(flyout): preserve tray activation handoff`, `fix(flyout): complete tray activation handoff`, `433c171` |

### P2 - Long-Running Efficiency, Architecture, And UI Quality

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M2-01 | TODO | Memory | Bound per-message mail/task mutation coordination state. | Keyed gates are reference-counted and removed safely; completed intents/retries and removed-account state are cleared; stress tests show bounded dictionaries. | `perf: bound mutation coordination state` |
| M2-02 | IN PROGRESS | Startup | Make flyout prewarm explicit or usage-adaptive. | Explicit opt-in and memory-pressure gates are implemented; record packaged idle-memory and first-open P50/P95 before marking this item done. | `perf(flyout): make prewarm memory-aware` |
| M2-03 | TODO | Efficiency | Reduce taskbar weather-bar polling. | Native/display/theme events drive normal updates; slow polling is fallback only; unchanged geometry skips window-tree scans and diagnostics work. | `perf: reduce weather bar polling` |
| M2-04 | TODO | Architecture | Extract shared policy/domain code into a referenced Core project. | App and tests reference the same assembly rather than manually linking production files; extraction is incremental and preserves WinUI behavior. | `refactor: extract task flyout core` |
| M2-05 | TODO | Accessibility | Correct dynamic command semantics and async status announcements. | Pin/read/star/trust states expose current toggle semantics; icon buttons and task checkboxes have localized names; async errors/progress use appropriate live regions. | `accessibility: expose dynamic ui state` |
| M2-06 | TODO | UI/Localization | Normalize settings information architecture, contrast, time formats, and command sizing. | Destructive storage actions have a clear scope; high contrast does not depend on opacity/hard-coded colors; time follows regional preferences; shared icon-command sizing is used. | `ux: normalize settings and command presentation` |
| M2-07 | TODO | Tooling | Add deterministic formatting, analysis, coverage, and dependency maintenance. | `.editorconfig` and CI format/analyzer checks pass; Core coverage is collected with an agreed threshold; grouped dependency updates are automated; major upgrades remain isolated. | `build: add repository quality gates` |
| M2-08 | DONE | UI/Compatibility | Match the weather bar to the active Windhawk taskbar surface. | Luminosity Dock geometry, transparency, hover fill, and corner radius are honored without overlapping widgets; missing, disabled, or unknown Windhawk settings preserve the native fallback, while malformed overrides use safe theme defaults. | `style(weatherbar): match Windhawk Luminosity dock` |
| M2-09 | DONE | Efficiency/Lifecycle | Suspend Flyout refresh work while it is hidden. | Periodic sync runs only while visible; delayed reopen refresh is throttled, cancellable, generation-safe, and cannot resume against a shutting-down Flyout. | `perf(flyout): suspend hidden refresh work` |
| M2-10 | DONE | Efficiency | Avoid rebuilding unchanged Flyout agenda snapshots. | Reopening within the same calendar month and cache version skips date-key materialization and agenda cloning; changing month or cache version forces a coherent snapshot refresh; month reuse behavior is tested. | `perf(flyout): skip unchanged agenda snapshots` |
| M2-11 | DONE | Memory | Release rebuildable Flyout caches under memory pressure. | Medium/high memory pressure drops hidden Flyout weather-icon and dot-brush caches on the UI dispatcher; visible/opening Flyouts retain live UI state; cache-trim eligibility is tested. | `perf(flyout): trim rebuildable caches` |
| M2-12 | DONE | Responsiveness | Defer Flyout calendar-dot rendering until after the window opens. | The open request performs no dot-render queueing before `Show`; visible-state handling schedules dot work at low priority; failed dispatcher enqueue does not permanently suppress later refreshes. | `perf(flyout): defer calendar dots until visible` |
| M2-13 | DONE | Efficiency/UI | Avoid reapplying an unchanged effective Flyout theme. | Light, dark, and system-theme changes still propagate; repeated applications of the same effective theme return without redundant property updates. | `perf(flyout): skip redundant theme updates` |
| M2-14 | DONE | UI/Compatibility | Keep Windhawk taskbar-style detection correct across registry views and transient read failures. | The x64 Windhawk view is preferred, 32-bit installations remain discoverable, and a locked/inaccessible settings key immediately falls back to native taskbar geometry instead of retaining stale Dock placement. | `fix(weatherbar): harden Windhawk style detection` |
| M2-15 | DONE | UI/Diagnostics | Make the active taskbar surface and computed weather-bar slot visible in read-only diagnostics. | Settings diagnostics identify the detected Windhawk surface and slot bounds without exposing registry contents or personal data; native fallback remains explicit. | `feat(weatherbar): expose Windhawk surface diagnostics` |
| M2-16 | DONE | UI/Compatibility | Place the Luminosity weather bar in the theme's left reserved strip without a startup flash. | The native window stays hidden until it is attached and positioned; the themed bar begins at client x=0 and fills `DockMargin`; full-width or unusably narrow Dock configurations retain native placement. | `fix(weatherbar): anchor Luminosity bar to left reserve` |
| M2-17 | DONE | UI/Compatibility | Keep the left reserve safe around native Widgets and give the detached bar its own translucent surface. | Widget/obstacle overlap clips the reserve or falls back to native placement; exact DockMargin is preserved across DPI; the left-reserve bar has a non-white rest/hover fill and border instead of assuming the centered Dock blur is underneath. | `fix(weatherbar): harden Windhawk reserve geometry` |
| M2-18 | DONE | UI/Compatibility | Offer an explicit Windows-native Widgets mode while preserving the existing weather-bar fallback. | The default remains the Task Flyout bar; the native option detects the per-user Web Experience Pack, requests Explorer's `TaskbarDa` entry, keeps the fallback visible until a plausible native taskbar bridge exists, retries delayed/failed activation, restores the user's captured value across process restarts, and never injects XAML into Explorer. | `feat(weatherbar): add native Widgets mode policy`, `feat(weatherbar): switch between native Widgets and fallback bar`, `fix(weatherbar): verify native Widgets before handoff` |
| M2-19 | DONE | UI/Accuracy | Distinguish Windows-managed Widgets content from Task Flyout weather injection. | Settings state that Windows can rotate finance, sports, and news announcements; the app opens the supported Widgets board protocols and gives the exact manual path for disabling taskbar announcements without writing private Web Experience settings. | `fix(weatherbar): clarify Windows-managed widget content`, `fix(weatherbar): label Widgets navigation honestly` |
| M2-20 | DONE | UI/Compatibility | Establish a compile-only Windhawk companion POC that replaces content inside the native Widgets shell. | The mod is separate from the MSIX and disabled by default; exact OS/PE and class/name/automation signatures gate the private ABI; the original Adaptive Card remains measured for Luminosity; local values and orphan recovery metadata are restored on disable/unload; owner-thread dispatch cannot leave callbacks pointing into an unloaded DLL; the bundled Windhawk compiler produces an x64 DLL without installing or loading it. | `feat(windhawk): add native weather shell POC`, `fix(windhawk): harden companion owner-thread cleanup` |

### P3 - Product Follow-Ups

| ID | Status | Area | Work item | Acceptance criteria | Commit |
| --- | --- | --- | --- | --- | --- |
| M3-01 | TODO | Distribution | Add stable/beta update-channel support. | Users can discover and install signed updates without repeating manual certificate/package steps; rollback and channel behavior are documented. | `feat: add update channel support` |
| M3-02 | TODO | Notifications | Add quiet hours and per-calendar notification controls. | Global defaults remain simple; users can suppress selected calendars and configure a quiet interval without losing explicit snoozes. | `feat: add notification quiet hours` |
| M3-03 | PAUSED | UI/Compatibility | Runtime-validate and package the experimental Windhawk weather companion. | A disposable Explorer session verifies enable/disable/unload, Luminosity width, hover/click, primary/secondary taskbars, Explorer restart, and fail-closed behavior on an unlisted taskbar binary before any user-facing enable switch is added. | `test(windhawk): validate native weather shell` |
| M3-04 | PAUSED | Integration | Feed Task Flyout weather and activation into the native-shell companion. | A versioned, length-bounded per-user channel performs no blocking I/O on Explorer's UI thread; stale/unavailable app state fails open; native-shell click activation opens Task Flyout Weather; shutdown, reconnect, and Explorer restart leave no callbacks or handles behind. | `feat(weatherbar): define companion IPC protocol`, `feat(weatherbar): host companion weather snapshots`, `feat(windhawk): bridge weather snapshots`, `feat(windhawk): activate Task Flyout weather` |
| M3-05 | PAUSED | Architecture/Security | Replace the Windhawk runtime dependency with a standalone per-user taskbar broker and Explorer host. | The x64 broker and host build without Windhawk or WebView libraries; exact OS and `Taskbar.View.dll` fingerprints gate all private ABI use; unsupported systems fail closed before Explorer memory or XAML is changed. | `feat(taskbar): scaffold standalone weather host` |
| M3-06 | PAUSED | UI/Compatibility | Inject an independent pure-XAML weather button into the Windows 11 taskbar. | The host uses standard `Windows.UI.Xaml` controls and taskbar theme resources, does not require the Windows Widgets entry, reserves a bounded left-side slot, and restores the original XAML tree on disable or unload. | `feat(taskbar): add pure xaml weather view`, `feat(taskbar): inject native xaml weather button` |
| M3-07 | PAUSED | Integration/UI | Connect the standalone host to Task Flyout weather, activation, settings, and diagnostics. | The existing bounded per-user IPC supplies sanitized immutable snapshots; click opens Task Flyout Weather; a localized experimental switch and privacy-safe diagnostics expose active, unsupported, fallback, and recovery states. | `feat(taskbar): consume weather snapshot pipe`, `feat(taskbar): activate weather button`, `feat(taskbar): acknowledge host control`, `feat(weatherbar): add standalone mode policy`, `feat(taskbar): parse standalone broker responses`, `fix(taskbar): emit broker output as utf8`, `feat(taskbar): add bounded broker client`, `feat(taskbar): define standalone lifecycle states`, `feat(taskbar): coordinate standalone lifecycle`, `feat(taskbar): wire standalone app lifecycle`, `feat(taskbar): own widgets suppression safely`, `feat(taskbar): suppress native widgets for standalone`, `feat(weatherbar): expose standalone taskbar controls`, `feat(taskbar): expose mount readiness`, `feat(taskbar): parse mount status responses`, `feat(taskbar): hand off fallback after mount proof`, `feat(taskbar): authenticate mount lease reports`, `feat(taskbar): renew mount readiness lease`, `refactor(taskbar): isolate mount readiness state`, `test(taskbar): cover mount lease reducer edges`, `diagnostics(taskbar): expose mount pending reasons` |
| M3-08 | PAUSED | Verification/Distribution | Validate and package the standalone taskbar component. | Compile/import checks prove the native binaries have no WebView dependency; a disposable Explorer session covers enable/disable, crash recovery, DPI, auto-hide, theme, multi-monitor, Explorer restart, unsupported binaries, signed packaging, and rollback before default exposure. | `build(taskbar): stage standalone native artifacts`, `test(taskbar): gate disposable explorer validation`, `test(taskbar): validate standalone weather injection`, `build(taskbar): recover missing local signing key` |
| M3-09 | IN PROGRESS | Accounts/Architecture | Support multiple isolated Google accounts across Calendar, Tasks, and Gmail. | Each Google identity owns a stable account ID, protected token namespace, provider services, agenda/cache keys, and mail operations; add/remove/reconnect acts on one account without replacing or disconnecting another; migration preserves the existing single account and tests prevent cross-account reads, writes, and notifications. | `refactor(accounts): add account-scoped provider identity`, `feat(google): support multiple connected accounts`, `feat(accounts): add per-account reconnect controls` |

M3-05 through M3-08 supersede the Windhawk runtime/package work in M3-03.
M3-03 remains as historical POC scope and must not be installed or enabled. The
protocol and app-side snapshot work already completed under M3-04 remains the
transport foundation for the standalone host.

## Maintenance Checkpoint Archive

The dated entries below preserve completed experiments and verification evidence.
They are historical records; active work remains in the backlog tables above.

### Audit Verification Baseline

- `dotnet build Task_Flyout.csproj -c Debug -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet build Task_Flyout.csproj -c Release -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- Debug and Release unit runs: 800 passed, 0 failed, 0 skipped.
- Application and test dependency vulnerability scans: no known vulnerable packages from the configured source.
- `dotnet format Task_Flyout.slnx --verify-no-changes --no-restore`: failed on existing whitespace formatting; tracked by M2-07.
- Remote packaged smoke currently fails after publication rather than gating it; tracked by M0-01.

### 2026-08-07 Completion Verification

- `dotnet test Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj --no-restore`: 877 passed, 0 failed, 0 skipped.
- `dotnet build Task_Flyout.csproj -c Release -p:Platform=x64 --no-restore`: passed with 0 warnings and 0 errors.
- The worktree is clean, and `credentials.json`, `Secrets.cs`, `*.pfx`, `bin/`, and `obj/` are not tracked.

### 2026-08-07 Windhawk Dock Verification

- Read-only inspection found Windows 11 Taskbar Styler 1.8 enabled with
  `Luminosity_variant_Dock`; the preset defaults are `DockMargin=250`,
  `DockMarginFix=500`, `DockHeight=58`, `DockTopGap=5`, and `bcr=10`.
- The current source maps that preset to client slot `(0, 5) 250x48` on a
  1920x58 taskbar, and the new diagnostics expose
  the resolved surface and slot after packaging.
- The Release x64 MSIX under `AppPackages\WindhawkCheck` was signed with the
  existing local sideload certificate and independently verified by SignTool and
  PowerShell. The installed package was not replaced during this pass, so no
  running tray state or user Windhawk settings were changed; screenshot
  verification remains pending installation approval.

### 2026-08-07 Windhawk Slot Fix Verification

- The left-reserve, obstacle clipping, and independent glass-surface fixes passed
  the full suite: 882 tests passed, with 0 failures or skips; Release x64 build
  completed with 0 warnings and 0 errors.
- A fresh signed package is available under `AppPackages\WindhawkFix`; SignTool
  verification succeeded with the existing certificate. It has not been
  installed over the currently running package yet.

### 2026-08-07 Native Widgets Mode Verification

- Added a persisted `WeatherBarMode` selector with a safe migration default of
  `TaskFlyout`; invalid values are normalized and native requests fall back to
  the existing bar.
- Added Windows Web Experience Pack and taskbar-entry detection, reversible
  `TaskbarDa` ownership, Explorer-restart recovery, localized settings/status
  text, and Store/taskbar-settings launch actions.
- Debug x64 build completed with 0 warnings and 0 errors; the full unit suite
  passed 916/916 after native handoff and TaskbarDa snapshot hardening.
- The Task Flyout bar now remains visible while Explorer materializes the native
  entry. Verification polls quickly for 10 seconds, then backs off to 30-second
  recovery checks; it also retries a failed cross-process TaskbarDa restoration.
- Native bridge confirmation now requires a visible left-side taskbar surface
  with plausible geometry. A failed custom HWND close keeps its object reference
  and hides/retries it instead of creating a duplicate child window.
- Runtime inspection of installed `1.4.2.0` confirmed that finance content is
  Windows Widgets' rotating announcement surface, not a stale Task Flyout bar.
  The UI now labels this mode as Windows-managed dynamic content and opens the
  Widgets board with instructions to disable taskbar announcements and badges.
- A separate, disabled-by-default Windhawk companion now proves that custom
  content can occupy the existing `AugmentedEntryPointContentGrid` without a
  second HWND or a second XAML Diagnostics consumer. Compile/link validation is
  complete; it has not been installed or loaded into Explorer. Runtime theme,
  unload, multi-monitor, IPC, and click-activation checks remain M3-03/M3-04.
- The current sandbox resolves as Windows 10 Pro 25H2 and has no registered
  Web Experience Pack for the sandbox SID, so a real native
  `AugmentedEntryPointButton` screenshot cannot be claimed here. Manual
  verification on a Windows 11 profile with Widgets enabled remains the release
  check; the ordinary app path intentionally does not inject XAML into Explorer.

### 2026-08-09 Windhawk Companion Compile Verification

- The experimental companion is disabled by default and remains outside the
  MSIX. It was not copied into Windhawk, loaded into Explorer, or enabled.
- The current allowlist targets Windows build `26200` and the loaded
  `Taskbar.View.dll` profile `TimeDateStamp=0x6A3CD591`,
  `SizeOfImage=0x0098B000`, `CheckSum=0x00985653`; an unknown profile fails
  before the internal FrameworkElement slot is read.
- `windhawk\build-companion.ps1 -Mode Syntax` passed with the real Windhawk
  compiler definitions, and `-Mode Link` produced an x64 DLL of 184,832 bytes
  with SHA-256
  `BF190118F9EBF1CC12B38D0796F170DC42EA0D7D7EB75C2D799CD7DE9D501622`.
- Owner-thread restoration uses a self-unhooking temporary callback. A helper
  owns an extra DLL reference and callers wait at most three seconds; if the
  taskbar is unresponsive, the helper remains valid until it can finish rather
  than blocking settings/unload indefinitely or calling unloaded code.
- Runtime validation is intentionally still pending under M3-03. Weather data
  IPC and Task Flyout click activation remain M3-04.

### 2026-08-09 Weather Companion Protocol Verification

- M3-04 now has a versioned JSON request/response contract with 4 KiB request
  and 16 KiB response limits. Commands are restricted to `ping`,
  `get-snapshot`, and `open-weather`; duplicate critical fields, malformed UTF-8,
  excessive nesting, and trailing JSON are rejected.
- Weather text is scalar-bounded before it reaches Explorer. C0/C1 and Unicode
  line separators cannot create taskbar line breaks, bidirectional override and
  isolate controls are removed, and emoji joiners/variation selectors remain
  intact.
- The 20 protocol-specific tests and the full 936-test suite pass. The Debug x64
  app build also completes with 0 warnings and 0 errors. The app-side server is
  verified below; the non-blocking Windhawk client remains the next M3-04 phase.

### 2026-08-09 Weather Companion Server Verification

- The default-off app bridge now owns a single first-instance named pipe scoped
  by current-user SID and Windows session, with `CurrentUserOnly` access,
  4-byte little-endian framing, one request per connection, and a two-second
  transaction deadline. The same server handle is reused between clients.
- Native-mode weather refresh no longer depends on the self-drawn weather
  window remaining alive. A background coordinator refreshes an immutable
  snapshot immediately, every 30 minutes, and after location changes; pipe
  handlers never perform network requests or manipulate WinUI.
- Shutdown cancels both pending pipe I/O and weather refresh. Integration tests
  cover oversize/truncated frames, same-user round trips, slow-client shutdown,
  and recovery by a second client after a partial-frame timeout.
- The 7 server-specific tests and full 943-test suite pass. Debug x64 builds with
  0 warnings and 0 errors. The Windhawk-side client is verified below; the
  visible experimental opt-in remains pending, so no Explorer process changed.

### 2026-08-09 Weather Companion Client Verification

- Windhawk companion version `0.2.0` now derives the same SID-and-session pipe
  name as the app, uses bounded overlapped I/O only on a retained background
  worker, validates the 16 KiB response before allocation, and parses the JSON
  through `Windows.Data.Json` before publishing an immutable UI snapshot.
- A valid snapshot updates the injected icon and compact temperature/condition
  text on each taskbar owner thread. Missing, malformed, unavailable, older-than-
  two-hour, or implausibly future data restores the untouched Windows Adaptive
  Card. Static preview injection is now a separate diagnostics-only setting that
  defaults off.
- Windhawk syntax and real x64 link both pass. The compile-only DLL is 206,848
  bytes with SHA-256
  `D485554023F0D077338941E2C1D096D07C36DE9C1B0872AC5A61931ADBC541A7`.
  It was not installed, enabled, or loaded into Explorer.
- The full .NET suite now passes 949/949 and Debug x64 still builds with 0
  warnings and 0 errors. Task Flyout click activation, localized app opt-in,
  owner-thread unload hardening, and runtime visual validation remain pending.

### 2026-08-09 Standalone Native Taskbar Direction

- Runtime inspection separates the taskbar surface from the Widgets board:
  Explorer loads `Taskbar.View.dll` plus the UWP Adaptive Cards renderer, while
  `Widgets.exe` loads `WebView2Loader.dll` and `EmbeddedBrowserWebView.dll`.
- The supported Windows Widgets API only hosts providers in the Widgets Board;
  it does not expose a taskbar-entry extension point. A weather button that is
  independent of Web Experience therefore requires a private Explorer host.
- The target design is a signed, per-user broker plus a narrowly scoped x64 host
  DLL. The host renders standard UWP XAML only, performs no network or WebView
  work, and reads sanitized snapshots through the existing named pipe.
- The current Windhawk POC remains compile-only reference material. It will not
  be installed, loaded, or extended as the product runtime path.

### 2026-08-10 Pure XAML Weather View

- Added a WebView-free `Windows.UI.Xaml` weather view composed only from
  `Grid`, `Border`, `StackPanel`, and `TextBlock` controls. It is intentionally
  hit-test transparent so a taskbar-owned button can retain hover, pressed,
  focus, and accessibility behavior.
- Added a native text boundary that removes control, line-separator, and bidi
  override/isolate characters, collapses whitespace, bounds all fields, and
  never splits a UTF-16 surrogate pair before text enters Explorer.
- The x64 Release host and broker compile, both native test executables pass,
  and import inspection finds no WebView or network library. The taskbar hook
  remains a no-op, so this stage did not load or modify Explorer.

### 2026-08-10 Standalone Taskbar Hook Profile

- The development `Taskbar.View.dll` PDB resolves
  `TaskbarFrame::OnTaskbarLayoutChildBoundsChanged` to RVA `0x001F3540`.
  Symbol lookup is an offline development step; no PDB or symbol service is
  used by the shipped host.
- Compatibility now requires the Windows build, PE timestamp/image
  size/checksum, target RVA, and exact 20-byte x64 prologue to match. The
  Explorer-side probe additionally holds a module reference and requires the
  loaded PE fingerprint plus an in-bounds executable memory region with the
  same bytes, so an updated, replaced, or already-patched target fails closed
  before MinHook or XAML mutation is reached.
- Release x64 compilation and both native tests pass. The entry hook remains a
  no-op and this verification did not load the host into Explorer.

### 2026-08-10 Default-off Detour Lifecycle

- Extracted the Explorer-side profile verifier so the runtime owns a retained
  `Taskbar.View.dll` module lease, checks the taskbar owner PID/thread, requires
  `MEM_IMAGE` executable target memory, and rechecks the loaded PE fingerprint
  and prologue immediately before MinHook creation and enable.
- Added a default-off detour state machine. It calls the original function
  first, counts active trampoline calls, blocks custom callbacks during stop,
  disables the hook before restoring, drains before removing, and enters
  quarantine on any restore/disable/drain/remove/uninitialize failure. A
  successfully activated host is pinned until Explorer exits; no dynamic
  remote DLL unload is attempted.
- The MinHook backend is pinned to the upstream x64 sources and now has a
  no-op compatibility test plus a four-step in-process smoke test. Release
  x64 build, four native tests, and WebView/network import inspection pass.
  The exported thread hook remains inert, so Explorer was not changed.

### 2026-08-10 Detour Shutdown Hardening

- The stop path now leaves the lifecycle lock before invoking XAML restoration
  or MinHook operations, so restoration can safely query runtime diagnostics
  without a lock inversion.
- After a successful disable, the original trampoline pointer is atomically
  cleared before active detours are drained. A thread that reached the detour
  late therefore cannot call a trampoline after it has been removed.
- Custom XAML callbacks have a separate drain counter and a C++ exception
  boundary. Restoration is also exception-isolated; any failure quarantines
  the pinned runtime instead of unloading executable code still referenced by
  Explorer.
- Release x64 compilation and all four native tests pass. The exported entry
  hook is still inert, so no DLL was loaded into Explorer during verification.

### 2026-08-10 Taskbar XAML Tree Probe

- Added a read-only, bounded probe for the Explorer taskbar visual tree. It
  requires the expected `Taskbar.TaskbarFrame` identity, one direct
  `RootGrid`, one direct `Taskbar.TaskbarBackground#BackgroundControl`, one
  direct `TaskbarFrameRepeater`, compatible geometry, and a shared `XamlRoot`.
- Duplicate, missing, oversized, detached, or unavailable trees fail closed;
  the probe never appends controls or changes Windows-owned properties.
- `LandmarksMatched` is deliberately not a mutation permit. A build-specific
  primary/secondary direct-child fingerprint is still required before the
  append-only mount can consume this probe.
- Added pure signature tests and included the probe in the native Release test
  matrix. Five native tests pass; the interactive taskbar remains unavailable
  in the build sandbox, so no Explorer runtime probe or injection was run.

### 2026-08-10 Append-only XAML Mount Lease

- Added a default-deny mount lease that creates only standard
  `Windows.UI.Xaml.Controls.Button`/`Grid` content and appends it to the
  probed `RootGrid`; no WebView, Adaptive Card, Widgets, or network code is
  involved in the view.
- The lease has explicit gates for a future build-specific direct-child
  allowlist, left-slot collision result, and XAML dispatcher thread access.
  Until those gates are supplied it returns `structure-not-allowlisted` or
  `left-slot-unavailable` and performs no mutation.
- Restore removes only the exact button object it appended, keeps state on
  failed removal, verifies the owned name/automation/content identity, and
  keeps only weak XAML references between callbacks. Six native Release tests
  pass; the host entry hook remains inert and Explorer was not loaded or
  changed.

### 2026-08-10 Default-deny Left Slot Geometry Policy

- Added a pure DIP geometry strategy for a left-anchored, vertically centered
  weather-button candidate. It accepts only a caller-proven current structure
  and blocker snapshot; either unknown flag returns `unknown-structure` and no
  actionable candidate.
- Frame, `RootGrid`, requested size, gap, and every blocker rectangle are
  finite and bounded. NaN, infinity, endpoint overflow, undersized slots, a
  root outside its frame, and more than 64 interactive blockers fail closed.
- Collision uses the requested safety gap on all four sides and distinguishes
  a usable candidate from an occupied one without reading or mutating XAML.
  It is not yet connected to the mount lease or entry hook, so this stage does
  not inject into or otherwise modify Explorer.

### 2026-08-10 Geometry-bound Mount Layout

- The mount lease now consumes a `TaskbarSlotGeometryInput`, re-reads the
  current frame/root dimensions on the owner thread, and refuses all slot
  statuses except `candidate-available`.
- The created standard XAML button uses the proven candidate rectangle for its
  width, height, and top/left margin; the old independent fixed margin is no
  longer a second layout authority. The entry hook is still inert until a
  live owner-thread blocker scanner supplies the structure proof and blocker
  span.

### 2026-08-10 Private TaskbarFrame ABI Bridge

- Added a default-off bridge for the development profile's private
  `TaskbarFrame` IInspectable slot. It requires an active exact-profile detour
  on the captured owner thread before reading any private object memory.
- The slot, interface object, and vtable are bounded with current-process
  memory queries; QueryInterface/AddRef/Release targets must be executable
  image code before
  C++/WinRT projection. The projected object must then match
  `Taskbar.TaskbarFrame` and confirm Dispatcher thread access.
- The bridge has a pure fail-closed gate test matrix. It is not connected to
  the exported entry hook and was not called against Explorer in this stage.

### 2026-08-10 Live Left-Slot Probe

- Added an owner-thread-only probe that accepts the exact two-child
  `RootGrid` fingerprint (`BackgroundControl` + `TaskbarFrameRepeater`) or the
  same tree plus the exact previously owned button. It rejects unknown root
  children and bounded-count violations.
- Realized repeater children are converted to RootGrid-space rectangles only
  after loaded/visibility/shared-XamlRoot/transform checks. The result owns a
  bounded blocker array and exposes a short-lived span to the geometry gate;
  it retains no XAML references after return.
- The probe is now compile-tested but not called by the inert host hook. A
  different taskbar build or an unobserved child shape remains default-deny.

### 2026-08-10 Guarded Host Controller Wiring

- Connected the verified frame bridge, live tree/slot probe, and reversible
  mount lease behind the default-off taskbar detour controller. The mount API
  now consumes the owner-thread `TaskbarSlotProbeResult` directly, so callers
  cannot supply an independent structure or blocker proof.
- Corrected the private ABI interpretation: slot 3 is the embedded
  IInspectable interface address. Raw `QueryInterface(IFrameworkElement)` is
  isolated behind an SEH boundary, the returned vtable is checked, and the
  bridge requires a TLS token for the exact active detour callback.
- The exported `WH_CALLWNDPROC` entry hook now accepts only the private start /
  stop control message and keeps the host inert otherwise. Release x64 build,
  import inspection, and all ten native tests pass. The broker's control-message
  sender, named-pipe weather snapshot wiring, and disposable Explorer-session
  validation remain pending; no Explorer injection was attempted in the build
  sandbox.

### 2026-08-10 One-shot Broker Control Transport

- Added explicit `start` and `stop` broker commands. Each command requires a
  supported disk-side profile, rechecks that the `Shell_TrayWnd` PID/thread did
  not change, verifies the adjacent host API, installs a thread-specific
  `WH_CALLWNDPROC` hook, and sends the private registered message with a bounded
  timeout.
- The temporary hook is removed after a synchronous dispatch. On timeout or
  removal failure the broker avoids an eager `FreeLibrary`; a successful start
  still relies on the host's existing self-pin before the hook is removed.
- Release x64, forbidden-import inspection, and all eleven native tests pass.
  The sandbox returns a structured `probe-rejected` result because it cannot see
  the interactive taskbar, so no Explorer injection was attempted. A controller
  acknowledgement channel and live named-pipe weather snapshots remain pending.

### 2026-08-10 Standalone Host Weather Snapshot Pipe

- The Explorer host now derives the same current-user SID and session-scoped
  `TaskFlyout.Weather.v1` pipe as the app and requests `get-snapshot` on a
  retained MTA worker. The XAML owner thread performs no pipe or JSON work.
- Framing is fixed at a four-byte little-endian header and a 16 KiB response
  limit. Connect and overlapped read/write waits are bounded by one query
  deadline; the stop event interrupts pending I/O and cancellation is drained
  before stack state is released. Strict UTF-8, protocol version/status,
  required temperature, and UTC freshness are checked before the existing
  native text sanitizer creates the immutable view model. Alert text takes
  precedence over the ordinary description when the server supplies it.
- Snapshots older than two hours or more than five minutes in the future are
  rejected. Missing, malformed, unavailable, stale, or failed queries clear a
  previously published model and request relayout instead of displaying stale
  weather indefinitely.
- Controller stop signals and joins the worker with a two-second owner-thread
  bound before XAML restoration and detour removal. JSON response, alert,
  freshness, length, cancellation, and status policy tests now accompany the
  existing native matrix. Release x64 compilation, forbidden-import inspection,
  and all twelve native tests pass. The build sandbox still cannot access an
  interactive taskbar, so no Explorer injection or live pipe round trip was
  attempted.

### 2026-08-10 Standalone Host Weather Activation

- The injected standard XAML Button now owns an explicit Click event token.
  Its delegate captures only a stateless callback and signals an auto-reset
  event; no taskbar frame, lease, XAML object, HANDLE, pipe I/O, or JSON work is
  retained by the delegate or performed on Explorer's XAML thread.
- The existing MTA worker waits for stop, activation, or the 15-second snapshot
  interval. Repeated clicks are coalesced while one non-idempotent request is
  pending, and `open-weather` is never automatically retried after an ambiguous
  acknowledgement. `ok`, `unavailable`, malformed, wrong-version, and cancelled
  responses have distinct fail-closed policy results and do not disturb the
  current weather snapshot.
- Restore revokes the Click token on its owner thread before removing the exact
  Button identity. Failed revocation or removal retains the lease for retry and
  triggers the existing quarantine path. Stop disables producers before handle
  closure and still attempts detour/XAML cleanup when the worker misses its
  bounded join, preventing a live callback from being forgotten.
- Release x64 compilation, forbidden WebView/network import inspection, and all
  twelve native tests pass in an isolated build directory. The read-only probe
  returns `taskbar-window-missing` because the build sandbox cannot see the
  interactive taskbar, so no Explorer injection was attempted. Settings,
  diagnostics, acknowledgement, and disposable Explorer-session validation
  keep M3-07 in progress.

### 2026-08-10 Standalone Host Control Acknowledgement

- The broker/host ABI is now version 3 and uses a new control message identity.
  Each request packs a nonzero 32-bit correlation nonce with the start/stop
  command and supplies a message-only reply window owned by the broker. The host
  validates that window belongs to the same Windows session before changing
  controller state.
- Explorer posts only a fixed acknowledgement enum (`started`,
  `already-started`, `stopped`, `not-started`, or the matching rejection). It
  never dereferences broker memory or waits for the broker. After removing the
  temporary hook, the broker waits at most one second, validates the nonce and
  command/result family, and distinguishes controller rejection, timeout, wait
  failure, and malformed replies in structured JSON.
- Pure tests cover request packing, nonce preservation, command/result matching,
  idempotent results, rejection, and cross-command fail-closed behavior. A fresh
  Release x64 build passes all twelve native tests and forbidden WebView/network
  import inspection. Live acknowledgement remains part of the disposable
  Explorer-session validation; the build workflow does not inject the host.

### 2026-08-11 Standalone Taskbar Mode Policy

- Added a stable persisted `StandaloneTaskbar` mode without changing the safe
  migration default. Missing, malformed, and future values still normalize to
  the existing Task Flyout weather bar.
- The policy hands off the taskbar surface only after the standalone host is
  active and the Task Flyout weather provider is enabled. Until acknowledgement,
  the existing weather bar remains visible; without provider data the surface
  stays disabled rather than presenting a non-functional native button.
- Mode parsing, serialization, fallback, provider dependency, and active-host
  behavior pass all 26 focused policy/settings tests. Broker lifecycle control,
  localized UI, and privacy-safe runtime diagnostics remain pending under M3-07.

### 2026-08-11 Native Quarantine Cleanup Retry

- Quarantined detours now keep the host pinned but permit an owner-thread
  restore-only stop attempt. This retries XAML lease restoration and Click-token
  revocation without pretending that the private hook can be safely unloaded.
- Mount rollback tracks the Click token and exact child removal as separate
  obligations. If the child was already removed, a later successful token
  revocation can now release the retained lease instead of remaining stuck in
  `mounted` state.
- The native Release build, forbidden-import inspection, and all 12 tests pass;
  the read-only probe still reports `taskbar-window-missing`, so no Explorer
  injection was attempted.

### 2026-08-11 Broker Response Contract

- Added a bounded, strict JSON parser for the standalone broker's `probe`,
  `start`, and `stop` responses. Command/result families, acknowledgement
  values, exit codes, duplicate fields, trailing JSON, field types, and known
  probe/control statuses are checked before a result is accepted.
- The app-side result surface intentionally drops native paths, process/thread
  IDs, message IDs, and free-form detail text. It distinguishes unsupported,
  temporarily unavailable, rejected, ambiguous, invalid-response, and failed
  states; a successful start is named `ControllerActiveUnverified` because the
  current native acknowledgement does not prove that a weather button is
  visible yet.
- Focused parser tests pass 39/39. Process launching, binary packaging,
  generation-safe lifecycle control, mount-ready signalling, and localized UI
  remain pending under M3-07.

### 2026-08-11 Broker UTF-8 Pipe Output

- The native broker now explicitly selects UTF-8 CRT text modes for stdout and
  stderr before emitting JSON. Strict app-side decoding no longer depends on
  the machine ANSI code page when a supported taskbar profile contains
  non-ASCII paths or details.
- Release x64 compilation, forbidden-import inspection, all 12 native tests,
  and a redirected strict-UTF-8 `probe` smoke check pass. The probe remains
  read-only and reports `taskbar-window-missing` in the build sandbox.

### 2026-08-11 Bounded Broker Process Client

- Added an app-side client that resolves only the adjacent broker and host
  binaries, passes arguments through `ProcessStartInfo.ArgumentList`, disables
  shell execution/windows, and uses strict UTF-8 output handling.
- stdout and stderr are read concurrently with a 32 KiB per-stream cap; excess
  output is drained and rejected. Probe calls have a three-second bound, start
  and stop calls a ten-second bound, and timeout/cancellation kills the entire
  child process tree before bounded cleanup.
- A fake-runner test seam covers binary absence, argument construction,
  acknowledgement semantics, timeout ambiguity, cancellation, output failure,
  and launch failure. Full .NET tests pass 1004/1004 and the Debug x64 app
  build has zero warnings/errors. The client is not yet called by App startup.

### 2026-08-11 Standalone Lifecycle State Policy

- Added fixed, privacy-safe runtime states for startup, controller
  acknowledgement, shutdown, unsupported systems, missing binaries, transient
  failures, rejection, ambiguous cleanup, recovery, invalid output, and
  cancellation. UI diagnostics can localize these stable keys without exposing
  native paths, process identifiers, or broker detail text.
- A successful broker `start` maps only to `ControllerActiveUnverified`. The
  current acknowledgement proves that the controller accepted the request, but
  not that a later taskbar layout callback mounted a visible XAML button, so
  every requested standalone state continues to keep the Task Flyout weather
  bar as the safe fallback. The broker result no longer exposes a generic
  `IsSuccessful` shortcut that could conflate command acceptance with a visible
  native surface.
- Focused tests cover the complete 12-result mapping matrix and all 13 unique
  diagnostic keys; the full 1009/1009 .NET suite passes and the Debug x64 app
  build has zero warnings/errors. Generation-safe lifecycle coordination, App
  wiring, mount-ready signalling, native artifact packaging, and localized
  settings remain pending under M3-07.

### 2026-08-11 Generation-Safe Standalone Coordinator

- Added a pure app-side coordinator that serializes strict probe, start, and
  stop operations while generation tokens suppress stale results during rapid
  mode changes. Cancelling during probe never starts the controller; once a
  start call has begun, disable and shutdown retain an idempotent stop
  obligation until `ControllerInactive` is explicitly acknowledged.
- Repeating the same desired state reuses the current task instead of creating
  a process retry loop. `RefreshAsync` is the sole explicit recovery path for
  Explorer recreation or a user-requested retry, and controller acknowledgement
  still leaves the Task Flyout bar visible because it is not mount-ready proof.
- Final shutdown cancels an in-flight generation, attempts serialized cleanup,
  and has a hard bound even if a test client ignores cancellation. Status
  callbacks are privacy-safe, exception-isolated, generation-ordered, and are
  suppressed during disposal; the later App subscriber must marshal them to the
  UI dispatcher.
- All 14 focused coordinator tests and the full 1023/1023 .NET suite pass; the
  Debug x64 app build has zero warnings/errors. Tests use only a fake broker
  client, so no Explorer injection was attempted. App lifecycle wiring,
  mount-ready signalling, native artifact packaging, and localized settings
  remain pending under M3-07.

### 2026-08-11 Standalone App Lifecycle Wiring

- Wired the persisted standalone mode into the app lifecycle. The existing
  weather companion pipe is started before a broker `start` request, and
  disable, mode handoff, and app exit issue the required broker `stop` before
  the pipe can close. A failed or ambiguous stop keeps both the cleanup
  obligation and pipe alive instead of abandoning a possible Explorer host.
- Added a persistent cleanup lease before controller activation. A normal cold
  start adopts a lease left by a crash, starts the pipe, and sends an
  idempotent `stop` before allowing a Windows Widgets handoff. Packaged
  `action=smoke` launches and `TASKFLYOUT_DISABLE_STANDALONE_TASKBAR=1` suppress
  all broker control calls and deliberately preserve any pending lease.
- Decoupled `TaskbarCreated` recovery from weather-bar polling. The shared
  listener has an independent two-second registration retry until the tray
  message window is available, and an Explorer recreation explicitly refreshes
  requested or cleanup-pending controller state without creating an automatic
  retry loop for other failures.
- Controller acknowledgement remains `ControllerActiveUnverified`, so the Task
  Flyout weather bar stays visible until a future mount-ready acknowledgement
  can prove that the native XAML button exists. A currently visible Windows
  native Widgets entry is only a fail-closed gate: broker start is withheld and
  this stage does not overwrite `TaskbarDa`. Ownership-safe suppression and
  exact restoration of that entry remain the next M3-07 phase.
- The full .NET suite passes 1034/1034 and the Debug x64 app build has zero
  warnings/errors. Lifecycle tests use a fake broker client and the build does
  not inject into Explorer. Localized settings/diagnostics, mount-ready
  signalling, native artifact packaging, and live disposable-session
  validation remain pending.

### 2026-08-11 Ownership-Safe Native Widgets Suppression

- Added a standalone-only `TaskbarDa=0` lease that snapshots the exact original
  value and registry kind before mutation. It shares the Windows Widgets mode
  mutex, rejects the opposite owner, compares again before writing, restores
  only an exact owned DWORD zero, and preserves any later user or policy change.
- Snapshot metadata is marker-last and notification delivery uses a persisted
  generation token. An unconfirmed `Applied=false` snapshot is quarantined: it
  is never adopted from the current DWORD zero, never replays suppression, and
  mode exit releases only metadata rather than guessing across the crash
  boundary.
- App handoff now restores any old Windows Widgets enable lease, suppresses the
  native entry, waits until Explorer removes that entry, starts the companion
  pipe, and only then requests the standalone controller. Leaving the mode
  reverses the order: stop acknowledgement precedes exact TaskbarDa restoration
  and any Windows Widgets activation.
- Explorer recreation invalidates an in-flight start generation through the
  same disable/stop handoff, waits for suppression to converge, and then starts
  a fresh generation. Background verification monitors only an owned setting;
  an already-hidden unowned value is never rewritten. Packaged smoke launches
  and the standalone kill switch perform no TaskbarDa mutation.
- The Task Flyout weather bar remains the fallback because controller
  acknowledgement is not mount-ready proof. The full .NET suite passes
  1062/1062, the Debug x64 app build has zero warnings/errors, and an isolated
  native Release build passes all 12 tests plus forbidden WebView/network import
  inspection. The probe stayed read-only and no Explorer injection was
  attempted. Localized settings/diagnostics, mount-ready signalling, artifact
  packaging/signing, and disposable-session validation remain pending.

### 2026-08-11 Localized Standalone Controls and Diagnostics

- Added an explicit experimental pure-XAML Task Flyout taskbar switch beside
  the Windows-managed Widgets switch. The two modes are mutually exclusive and
  keep the persisted three-mode policy stable; turning either option off returns
  to the existing Task Flyout weather bar mode.
- English, Simplified Chinese, and Traditional Chinese now describe preparation,
  active-but-unverified, missing binary, unsupported profile, temporary outage,
  Widgets ownership conflict, safety suppression, stopping, failure, and
  fallback states. The user-facing mapper consumes only fixed enums, booleans,
  and recognized diagnostic tokens, never native paths or broker free text.
- Weather-page state refreshes on suppression and controller transitions without
  polling the UI. Read-only Settings diagnostics now expose controller request
  and cleanup state, companion-pipe activity, suppression readiness, snapshot
  transaction state, and pending taskbar notification state.
- All three resource files have matching keys and valid XML. The pure status
  matrix and full suite pass 1078/1078; the Debug x64 WinUI build has zero
  warnings/errors. Native artifact packaging/signing, mount-ready proof, and
  disposable Explorer-session validation remain pending under M3-07/M3-08.

### 2026-08-11 Standalone Native Artifact Staging and Sideload Signing

- Added an x64 MSBuild target that builds the broker and Explorer host in an
  isolated Ninja intermediate directory, runs the existing native test/import
  checks, and copies both binaries next to Debug/Release app output and publish
  artifacts. `BuildStandaloneTaskbarNative=false` remains an explicit opt-out
  for environments that only need managed tests; the target always passes
  `-SkipProbe`, so compilation never injects or loads Explorer code.
- Added an opt-in native PE signing step for explicit `SideloadOnly` builds.
  It selects the existing current-user certificate matching the manifest (or
  the supplied package thumbprint), signs both PE files with SHA-256, and
  verifies each signature before MSIX packaging. No certificate or private key
  is stored in the repository.
- A Release SideloadOnly package was produced and checked read-only: the MSIX
  signature is valid, both embedded native entries match their staged hashes,
  and the broker/host outputs carry the existing local signing certificate.
  No package was installed and no Explorer process was started. The isolated
  native matrix remains 12/12; mount-ready proof and disposable-session
  validation remain pending.

### 2026-08-11 Native Mount-Readiness Query

- Added an additive `status` control command while preserving every existing
  start/stop command value and acknowledgement. Older controller responses
  therefore remain unverified rather than being mistaken for mount proof.
- The Explorer host answers `mount-ready` only on the primary taskbar XAML
  owner thread and only after revalidating the exact owned Button, RootGrid
  child identity, retained click lease, loaded/visible state, and non-zero live
  layout intersection. A historical `mounted` bit, a pending layout pass, or a
  detached/replaced element reports pending or rejected instead.
- Pending queries request another asynchronous taskbar layout pass without
  blocking Explorer. The broker still removes its temporary hook before its
  bounded acknowledgement wait; no pointer or free-form XAML data crosses the
  process boundary.
- An isolated Release Ninja build passes all 12 native tests and the existing
  WebView/network import inspection. The build used `-SkipProbe`; no host was
  loaded into Explorer. Managed bounded polling and fallback handoff remain the
  next M3-07 step.

### 2026-08-11 Managed Mount-Status Contract

- Extended the strict broker parser and bounded process client with the
  additive `status` command. Only `mount-ready`, `mount-pending`, and
  `not-started` are accepted for an acknowledged status response; cross-command
  replies, unknown future states, wrong exit codes, and mismatched rejection
  families fail closed.
- `mount-ready` now has a distinct privacy-safe managed result and cannot be
  produced by the older `started`/`already-started` acknowledgements. The client
  still retains no native path, process identifier, or free-form detail.
- Focused broker protocol/client tests pass 52/52. The coordinator does not yet
  consume readiness in this slice, so the Task Flyout fallback remains visible
  until the next small lifecycle commit.

### 2026-08-11 Bounded Mount-Ready Handoff

- The coordinator now publishes controller startup as unverified first, then
  performs at most five read-only status requests 400 ms apart. Each status
  process has its own two-second bound. Only the fixed `mount-ready` result can
  enter the new `MountReady` runtime state; pending, timeout, an older broker,
  malformed output, or a thrown status implementation cannot close fallback.
- Disable, mode handoff, Explorer recovery, and disposal cancel the current
  readiness generation and still serialize the required idempotent stop behind
  the same operation gate. Status failures are not retried automatically;
  explicit refresh and `TaskbarCreated` remain the bounded recovery points.
- The app now feeds actual `MountReady` state into the pure presentation policy.
  Current status transitions re-run presentation without starting another
  broker generation, close the Task Flyout bar only after proof, and restore it
  for every later reported non-ready state. A failed fallback detach remains
  under the existing watchdog until it closes safely.
- English, Simplified Chinese, and Traditional Chinese expose the confirmed
  active state. All resource files remain schema-matched and valid XML. The
  complete .NET suite passes 1089/1089 and the Debug x64 app build succeeds with
  zero warnings/errors. The earlier isolated Native Release matrix remains
  12/12 with forbidden WebView/network imports absent. No package was installed
  and no Explorer status/start/injection command was executed.
- A same-Explorer visual-tree loss after a successful handoff still needs a
  low-overhead readiness-lease renewal path; `TaskbarCreated` and explicit
  refresh already revoke/recheck proof, but disposable validation must cover
  non-restart tree rebuilds before M3-07/M3-08 can be marked done.

### 2026-08-11 Authenticated Managed Mount Lease

- Extended the existing current-user/current-session weather pipe with a
  bounded `report-mount-readiness` request. The pipe server obtains the real
  client process identifier from the kernel; it never trusts a JSON-supplied
  identity.
- A controller start acknowledgement may now retain only the taskbar Explorer
  process identifier and the per-dispatch control nonce as an internal lease
  identity. Paths, thread identifiers, free-form native detail, and that
  identity remain absent from UI diagnostics.
- Ready/lost reports must match the current Explorer identity and carry a
  non-zero, monotonic mount generation. Old Explorer processes, old controller
  generations, reordered state changes, and ordinary same-user pipe clients
  cannot renew the current lease.
- Confirmed readiness now has a 12-second fail-closed lease. Authenticated
  ready heartbeats renew it; lost reports or missed heartbeats restore the Task
  Flyout fallback, and disable/refresh revokes the identity before old reports
  can arrive.
- Focused protocol/server/coordinator coverage and the complete .NET suite pass
  1102/1102. The Debug x64 app build succeeds with zero warnings/errors. This
  slice deliberately leaves fallback visible with the existing native binary;
  the next commit must emit the control nonce and live ready/lost reports from
  the persistent Host worker without starting another Broker process.

### 2026-08-11 Native Mount-Readiness Renewal

- The Broker now returns the exact start `controlNonce`, and Host API version 4
  rejects mixed old/new Broker and Host binaries. The Explorer Host publishes a
  reporting session only after its detour and persistent pipe worker are active;
  nonce replacement, observation reset, and snapshot capture share one lock so
  an in-flight worker cannot bind a previous mount observation to a new start.
- The existing MTA worker sends fixed-schema ready/lost reports on state changes
  and renews ready at five-second intervals. Only a fresh proof produced on the
  XAML owner thread can renew ready; the worker never dereferences a XAML object,
  and a proof older than twelve seconds is converted to lost fail-closed.
- A pending lost generation is latched until the app accepts it. A later ready
  observation cannot overtake that invalidation, and an acknowledgement from an
  older nonce cannot clear the current session's latch. Unresolved private frame
  bridges publish only a POD lost state without traversing apartment-affine
  leases.
- The managed coordinator also refuses to let a late Broker `mount-ready` result
  overwrite an authenticated lost report. The complete .NET suite passes
  1103/1103, the Debug x64 app build has zero warnings/errors, and the isolated
  Native Release matrix passes 12/12 plus the forbidden WebView/network import
  inspection.
- No package was installed and no Broker start/status command or Explorer
  injection was executed. M3-07/M3-08 remain in progress until a disposable
  Explorer session validates unload/rebuild latency, DPI, auto-hide, theme,
  multi-monitor, restart, unsupported-build rejection, and rollback.

### 2026-08-11 Mount-Readiness State Coverage

- Extracted the nonce, observation generation, proof sequence, stale-proof
  downgrade, and pending-lost latch into a deterministic header-only reducer.
  The Explorer Host still applies one SRW lock around the reducer and owns all
  event signalling, while tests can now exercise the protocol ordering without
  loading the Host or touching XAML.
- Native policy coverage now drives ready to lost to later ready, proves that
  lost remains visible until its exact acknowledgement, rejects a different
  controller nonce or generation, converts an expired proof to one stable lost
  generation, rejects an old ACK after a new session atomically replaces the
  nonce, and preserves non-zero values across theoretical sequence wrap.
- The isolated Native Release matrix passes 12/12 and the forbidden
  WebView/network import inspection remains clean. No Broker command was run
  and no Explorer process was loaded or modified.

### 2026-08-11 Disposable Explorer Validation Gate

- Added a standalone runtime harness whose normal path is closed unless the
  caller supplies both an Explorer-injection switch and an exact disposable-
  session confirmation phrase. Matching valid Authenticode signatures are the
  default; unsigned development binaries require a separate explicit override.
- The harness performs the exact-profile probe, one start, bounded mount-ready
  polling/hold checks, and a final inactive status check. It marks cleanup as
  required before invoking start, so timeouts, malformed output, rejected
  acknowledgements, and test assertions all enter up to three idempotent stop
  attempts. It never installs a package, changes Widgets registry state, kills
  Explorer, or restarts it.
- The manual matrix now covers authenticated fallback handoff and recovery,
  ready/lost ordering, click activation, tree rebuilds, DPI, auto-hide, theme,
  high contrast, multi-monitor, Explorer restart, unsupported fingerprints,
  exact registry restoration, and final pinned-module release.
- The PowerShell AST parser, missing-opt-in fail-closed gate, and `-DescribeOnly`
  path pass under PowerShell 7 and Windows PowerShell without resolving or
  launching a binary. The actual probe/start/status/stop path remains unrun
  until the user explicitly authorizes a disposable Explorer session, so M3-08
  remains in progress.

### 2026-08-12 Bounded Native Mount Diagnostics

- Host API version 5 extends the acknowledgement envelope without changing its
  low 32-bit controller result. The high 32 bits carry only a fixed diagnostic
  enum, so no pointer, path, exception text, private XAML name, or other
  free-form Explorer data crosses the process boundary.
- The owner-thread frame path now distinguishes an unobserved layout,
  unresolved private bridge, changed tree profile, exhausted lease slots,
  rejected slot structure or geometry, view creation, append/restore failure,
  mounted-but-not-ready, and confirmed mount-ready. The worker still consumes
  only atomics/POD and never dereferences apartment-affine XAML state.
- The Broker emits the additive `controllerDiagnostic` JSON property while the
  existing managed parser deliberately ignores it. The disposable-session
  harness requires the property and includes the last bounded reason in a
  mount-ready timeout, making the next live failure actionable without
  weakening its double opt-in or cleanup guarantees.
- The isolated Native Release matrix passes 12/12 with forbidden WebView and
  network imports absent. The complete .NET suite passes 1104/1104, the Debug
  x64 app build has zero warnings/errors, and both PowerShell implementations
  pass the harness's non-mutating path. The API v5 Broker and Host are now
  signed; a fresh disposable Explorer restart is still required before live
  diagnosis, so M3-07/M3-08 remain in progress.

### 2026-08-12 Local Signing Key Recovery

- Native signing now verifies that a matching certificate's RSA private key is
  actually accessible instead of trusting the store's `HasPrivateKey` marker.
  A broken or foreign-profile key association therefore remains fail-closed.
- The default behavior still refuses to replace a missing key. An explicit
  `-CreateAndTrustCertificateIfMissing` switch creates a new non-exportable
  code-signing certificate with the unchanged manifest Publisher and trusts it
  only in the current user's `TrustedPeople` and `Root` stores. It cannot be
  combined with a requested thumbprint, because a new key can never reproduce
  an old certificate thumbprint.
- PowerShell 7 and Windows PowerShell parse the script and reject the conflicting
  switch combination before reading binaries or changing a certificate store.
- On 2026-08-12, the interactive `vince` session created replacement
  current-user certificate `15303040F7CEECFDE9C023F15481D15C39CCDB46` and
  signed the API v5 Broker and Host. SignTool `/pa` verification completed for
  both binaries with zero warnings and zero errors.
- The next step is a fresh disposable Explorer restart followed by the gated
  probe/start/status/stop validation; M3-08 remains in progress.

### 2026-08-12 Signed API v5 Disposable Explorer Diagnosis

- After a fresh Explorer restart, strict probe accepted Windows build `26200`
  and the allowlisted `Taskbar.View.dll` fingerprint in Explorer process
  `12196` (taskbar thread `31500`). The newly signed API v5 Broker and Host
  passed the harness's signer gate and controller start was acknowledged.
- The bounded 20-second status poll remained `mount-pending` with the fixed
  diagnostic `awaiting-layout`; no mount-ready lease or successful native XAML
  mount was evidenced. This narrows the current evidence to the
  owner-thread layout callback not having been observed by the controller. It
  does not yet distinguish an uncalled detour target, a skipped custom callback,
  or a startup diagnostic ordering race.
- The harness's `finally` path acknowledged stop and its post-stop status check
  returned `not-started`; an independent status confirmation also returned
  `not-started` with diagnostic `none`. The Host remains pinned by design until
  Explorer exits, so any rebuilt diagnostic Host requires another fresh
  disposable Explorer restart before live use. M3-07/M3-08 remain in progress.

### 2026-08-12 Bounded Detour Activity Telemetry

- Host API version 6 adds only fixed, privacy-safe controller diagnostics for
  an unobserved detour target, an inactive detour, an unavailable callback, a
  reentrant callback, and a final callback-gate race. The Host records monotonic
  saturating detour-entry and custom-callback sequences plus one fixed skip
  reason; no pointer, XAML name, exception text, or free-form Explorer state is
  exposed.
- Start now captures its detour sequence baseline before installation and uses
  compare/exchange for the initial `awaiting-layout` sentinel, so a concrete
  callback diagnosis cannot be overwritten by a later startup write. A fresh
  `Dormant`/`Removed` lifecycle clears stale diagnostics, while an
  `AlreadyActive` retry preserves the current live diagnosis.
- Commit `e503fbd` contains the API v6 diagnostic changes. The API v6 Release
  build is isolated in
  `.testbuild/native-taskbar-weather-v6` because Explorer still pins the prior
  v5 Host path. Native tests pass 12/12 and the build script's WebView/browser/
  network import gate passes for both Broker and Host. Managed protocol tests
  pass 49/49, the complete .NET suite passes 1109/1109, the Debug x64 app build
  has zero warnings/errors, and PowerShell 7 plus Windows PowerShell pass the
  harness's AST/`DescribeOnly` checks. The new artifacts still require
  current-user signing and a fresh disposable Explorer restart before live
  diagnosis; M3-07/M3-08 remain in progress.

### 2026-08-12 Signed API v6 Detour Diagnosis

- The interactive user signed both API v6 artifacts with certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`, then restarted Explorer. The
  signed Host therefore loaded into fresh Explorer process `50720` on taskbar
  thread `25208`, rather than reusing the pinned v5 module.
- The gated 20-second harness remained `mount-pending` and ended with
  `detour-target-not-observed`. This proves the exact MinHook target did not
  execute after controller installation; the earlier `awaiting-layout` result
  was not a skipped-callback or startup-diagnostic race. Repeating or widening
  `WM_SETTINGCHANGE` relayout hints is therefore not an evidence-backed fix.
- The harness cleanup completed and an independent status returned
  `not-started` with diagnostic `none` in the same Explorer process. The next
  implementation step is a bounded, owner-thread bootstrap of the already-live
  TaskbarFrame/XAML tree which fails closed unless it finds one exact supported
  frame; M3-07/M3-08 remain in progress.

### 2026-08-13 Read-Only TaskbarFrame Bootstrap

- Commit `11ca45e` advances the native Host protocol to API version 7 and adds
  a diagnostic-only bootstrap for the already-live primary taskbar XAML tree.
  The actual `Shell_TrayWnd` from the control hook must belong to the current
  Explorer process and current owner thread before the public
  `VisualTreeHelper::FindElementsInHostCoordinates` query is allowed to run.
- The query converts the taskbar client bounds to DIPs, examines at most 1024
  projected elements with a 250 ms cooperative enumeration budget, accepts
  only the exact `Taskbar.TaskbarFrame` runtime class, deduplicates candidates
  by controlling `IUnknown` identity, and requires exactly one candidate to
  pass the existing dispatcher/XamlRoot/geometry/tree-landmark profile. All
  projected references die synchronously on the owner thread; exceptions,
  overflow, absence, ambiguity, and profile drift each fail closed with a
  fixed `bootstrap-*` diagnostic.
- This checkpoint cannot mutate Explorer XAML: it does not start the pipe
  worker, create a weather view, acquire a lease, append a child, or request a
  relayout. The installed detour uses a read-only callback that does not
  dereference its private frame argument. Bootstrap telemetry is stored
  separately and wins only during a successful active read-only lifecycle, so
  rejected Start/Stop/Status and cleanup faults cannot be hidden by a stale
  probe result.
- The disposable-session harness treats every `bootstrap-*` result as an
  immediate terminal observation. It accepts only a stable
  `bootstrap-frame-validated`, fails closed on the other eight reasons, and
  always proceeds through bounded stop plus a final `not-started` check instead
  of polling for a mount that v7 intentionally cannot produce.
- The isolated Release build in
  `.testbuild/native-taskbar-weather-v7` passes 13/13 native tests and both
  binaries pass the WebView/browser/network import gate. Managed protocol tests
  pass 58/58, the complete .NET suite passes 1118/1118, the Debug x64 app build
  has zero warnings/errors, and PowerShell 7 plus Windows PowerShell pass the
  harness AST and `DescribeOnly` paths.
- The downloaded `Taskbar.View.pdb` remains incomplete: its MSF header declares
  49,680,384 bytes while the available file is only 13,254,656 bytes, and a
  zero-padded copy is rejected by DIA. No symbol-derived private RVA or unsafe
  synthetic `TaskbarFrame` call was accepted from that artifact. The public
  bootstrap is therefore the next evidence-gathering step.
- Both API v7 binaries are signed with current-user certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`; SignTool verified both with zero
  warnings and zero errors. A read-only strict probe accepted Windows build
  `26200` and the allowlisted `Taskbar.View.dll` fingerprint in Explorer process
  `16420` on taskbar thread `20172`. The v7 Host has not been loaded yet:
  restart the disposable Explorer once to release its pinned v6 Host, then run
  the gated harness. If the result is `bootstrap-frame-not-observed`, investigate
  the XAML island host coordinate assumption before changing private detours or
  sending more layout notifications. M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v7 Bootstrap Diagnosis

- After the disposable Explorer restart, the signed API v7 Host loaded into
  fresh Explorer process `19900` on primary taskbar thread `32724`. The strict
  PE/profile probe remained supported and the harness's signer gate passed.
- The read-only public-XAML bootstrap returned the stable fixed diagnostic
  `bootstrap-query-failed`. This rules out accepting a unique validated frame;
  the current coarse result means an exception occurred during public query,
  iterator traversal, candidate projection/identity, or the final tree probe.
  It does not justify enabling the existing mount path.
- The harness failed closed and its `finally` cleanup completed. An independent
  strict probe still reported the same supported Explorer profile, Explorer
  remained responsive, and an independent status command returned
  `not-started` with diagnostic `none`.
- The next diagnostic slice should preserve the same read-only behavior while
  separating public-query acquisition, iterator creation/traversal, candidate
  identity projection, runtime-class inspection, and tree-profile exceptions
  into fixed privacy-safe stages. No HRESULT, pointer, class instance data, or
  exception text should cross the process boundary. M3-07/M3-08 remain in
  progress.

### 2026-08-13 Bootstrap Failure Stage Telemetry

- Commit `ebfd6c0` advances the Host protocol to API version 8 while preserving
  every API v7 diagnostic value. It appends fixed results for host-coordinate
  query failure, iterator failure, runtime-class inspection failure,
  controlling-`IUnknown` projection failure, and an unavailable final tree
  probe; the older `bootstrap-query-failed` remains the unknown fallback.
- The stage names describe only the code boundary where failure was observed.
  They do not expose or infer an HRESULT, pointer, exception text, runtime
  instance, candidate count, or private class data. C++/WinRT lazy evaluation
  can still surface an underlying query failure during iterator traversal.
- The live bootstrap keeps the API v7 safety boundary: exact taskbar window,
  current process/thread, bounded DIP rectangle, at most 1024 elements, 250 ms
  cooperative enumeration budget, exact class name, controlling identity
  deduplication, one full tree probe, synchronous owner-thread destruction, and
  no weather worker, view, mount lease, append, or relayout request.
- The isolated Release output is
  `.testbuild/native-taskbar-weather-v8`. Native tests and import gates pass
  13/13, managed protocol tests pass 63/63, the complete .NET suite passes
  1123/1123, the Debug x64 app build has zero warnings/errors, and PowerShell 7
  plus Windows PowerShell pass AST and `DescribeOnly` validation. The current
  Explorer still pins the API v7 Host. Both v8 binaries are now signed with
  current-user certificate `15303040F7CEECFDE9C023F15481D15C39CCDB46` and
  SignTool verified each with zero warnings/errors; load them only after another
  disposable Explorer restart. M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v8 Query Timeout Diagnosis

- After another Explorer restart, the signed API v8 Host was dispatched to the
  fresh primary taskbar owner. Start did not acknowledge within the Broker's
  bounded reply window, so no stage diagnostic was accepted. The same full
  `Shell_TrayWnd` client-rectangle query used by v7 can therefore block inside
  the public XAML query boundary long enough that cooperative post-call and
  enumeration budgets cannot protect the owner thread.
- The harness entered its ambiguous-start cleanup path and did not report a
  cleanup failure. After the delayed owner-thread work drained, an independent
  strict probe identified the supported primary taskbar in Explorer process
  `42396` on thread `42380`; an independent status acknowledgement returned
  `not-started` with diagnostic `none`. The taskbar Explorer remained
  responsive. Other Explorer processes were present and were not treated as
  the primary taskbar owner.
- A full-host rectangle is no longer an acceptable bootstrap input. The next
  slice should query a single bounded host-coordinate point (or the smallest
  practical rectangle) and keep the existing exact class/identity/tree gates.
  The diagnostic-only lifecycle should also avoid installing the private
  layout detour if Host pinning and Start/Status/Stop state can be preserved
  safely without it. M3-07/M3-08 remain in progress.

### 2026-08-13 Center-Point Bootstrap Query

- Commit `519e880` advances the Host protocol to API version 9 and replaces the
  v7/v8 full-client-rectangle `FindElementsInHostCoordinates` input with one
  deterministic center point derived from the validated taskbar client bounds.
  The public query still uses `subtree = null` and `includeAllElements = true`,
  then applies the same exact `Taskbar.TaskbarFrame`, controlling-identity,
  bounded-enumeration, and complete-tree-profile gates.
- The change is evidence-driven: API v7 produced a fixed
  `bootstrap-query-failed`, while API v8's stage split showed the synchronous
  query could outlive the Broker acknowledgement window. A center point is the
  smallest practical host-coordinate probe that may still hit the frame without
  asking the XAML runtime to process the entire taskbar rectangle. The timeout
  remains fail-closed if the runtime still blocks.
- Pure policy tests cover finite center-point conversion, translated origins,
  zero dimensions, and non-finite dimensions. Native 13/13, complete .NET
  1123/1123, Debug x64 (0 warnings/errors), and both PowerShell parser/
  `DescribeOnly` paths remain green. The v9 output is isolated in
  `.testbuild/native-taskbar-weather-v9`. Both v9 binaries are now signed with
  current-user certificate `15303040F7CEECFDE9C023F15481D15C39CCDB46` and
  SignTool verified each with zero warnings/errors; the current Explorer still
  pins the previous Host, so load v9 only after another disposable Explorer
  restart. M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v9 Host-Query Diagnosis

- After a fresh Explorer restart, the signed API v9 center-point bootstrap
  returned the stable fixed diagnostic `bootstrap-host-query-failed` within the
  control acknowledgement window. This removes the v8 full-rectangle workload
  as the immediate cause of the API failure: the public
  `FindElementsInHostCoordinates` host query itself rejects or cannot resolve
  the current taskbar XAML context when called with `subtree = null`.
- The harness failed closed and completed its bounded cleanup. Explorer process
  `49468` on primary taskbar thread `50096` remained responsive; an independent
  strict probe still matched the allowlisted profile and an independent status
  returned `not-started` with diagnostic `none`.
- Further coordinate shrinking is not evidence-backed. The next read-only slice
  should determine whether a public owner-thread XAML root can be acquired
  (for example `Window::Current().Content()` in a Window-hosted tree) and pass
  that exact `UIElement` as the query subtree. If no unique public root is
  available, the public-host-query bootstrap path must remain unsupported
  rather than reconstructing or guessing a private object. M3-07/M3-08 remain
  in progress.

### 2026-08-13 API v10 Public-Root Bootstrap

- Commit `19441c3` advances the diagnostic-only Host protocol to API version 10
  and makes the next route independent from the failed null-subtree query.
  On the validated taskbar owner thread it reads the public
  `Windows.UI.Xaml.Window::Current()` and `Content()` objects only; it never
  creates a `Window`, initializes a XAML island, attaches a source, sets
  `Content`, or keeps a XAML reference after the synchronous probe returns.
- The public root is accepted only when its `CoreWindow` interop HWND equals
  the exact `Shell_TrayWnd` supplied by the control hook. The root is then
  passed as the explicit `VisualTreeHelper::FindElementsInHostCoordinates`
  subtree at the deterministic taskbar center point. The existing bounded
  1024-element/250-ms budget, exact `Taskbar.TaskbarFrame` class, controlling
  identity deduplication, and complete tree profile remain in force.
- New fixed diagnostics are appended after historical values 0–30:
  `bootstrap-root-unavailable`, `bootstrap-root-query-failed`,
  `bootstrap-root-not-associated-with-taskbar`,
  `bootstrap-root-frame-not-observed`, `bootstrap-root-frame-ambiguous`,
  `bootstrap-root-tree-profile-mismatch`, `root-bootstrap-validated`, and
  `bootstrap-root-enumeration-overflow`. Only the validated value can pass
  the disposable harness; all other outcomes remain fail-closed and do not
  start the weather worker or mount a view.
- Native policy/control tests and the managed protocol allowlist cover the
  additive values. Release output is isolated in
  `.testbuild/native-taskbar-weather-v10`; the full Native suite is green
  (13/13). A disposable Explorer runtime check remains the only path that can
  promote this probe; no mount or worker is enabled by this checkpoint.
  M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v10 Artifacts

- The isolated v10 Broker and Host are signed with the existing current-user
  code-signing certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`. SignTool verified both chains
  with zero warnings and zero errors; neither file is timestamped.
- Signed SHA-256 hashes are
  `29BFC7E16C8ECB2FBEBC0E2DD2F4232C47BAB2EDC6DC51207A5D86377BCAE99B`
  for `TaskFlyout.TaskbarBroker.exe` and
  `87DB30CE1A5F7D922C4D281FC925688FF3EC92B99A6AB75D85675CBF32B4BD5D`
  for `TaskFlyout.TaskbarHost.dll`.
- The v10 controller was stopped by the disposable harness described below.
  The Explorer process can still pin the Host DLL by design until that
  disposable Explorer is restarted. M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v10 Public-Root Diagnosis

- After the strict preflight accepted Windows build `26200`, Explorer process
  `14636`, and primary taskbar thread `15476`, the signed v10 harness started
  against a `not-started` baseline and returned the stable fixed diagnostic
  `bootstrap-root-unavailable`. The taskbar owner thread therefore exposed no
  usable public `Window::Current()` root for this route; no inference about a
  private XAML object is made.
- The harness failed closed, ran its `finally` stop path, and an independent
  post-run strict probe still reported `supported`; an independent status
  acknowledgement reported `not-started` with diagnostic `none`. Explorer
  remained responsive. This is evidence that the public-root route is
  unsupported on the tested taskbar context, not evidence to relax the HWND
  association gate or construct a `DesktopWindowXamlSource`.
- M3-07/M3-08 remain in progress but the next experiment must be a separately
  scoped, explicitly documented child-host association study if one is still
  justified. The v10 path itself should remain disabled for mounting.

### 2026-08-13 Public Child-Host API Boundary

- Local Windows SDK `10.0.26100.0` inspection found no public
  `DesktopWindowXamlSource::GetForWindowId`, `GetForWindow`, or equivalent
  existing-source lookup. The projection exposes construction plus instance
  `Content`/focus members only. `GetWindowIdFromWindow` and
  `GetWindowFromWindowId` convert identifiers but do not return a XAML source,
  `XamlRoot`, or content root.
- Enumerating taskbar child HWNDs therefore cannot be promoted into a public
  XAML-root route without constructing/initializing/attaching a new source or
  guessing a private object. Both actions are outside the read-only bootstrap
  boundary. No API v11 public-child-host implementation will be added on this
  evidence.
- The only existing evidence-backed candidate is the already guarded private
  TaskbarFrame bridge: exact OS/PE/prologue allowlist, active detour, exact TLS
  callback token, owner thread, bounded slot/vtable reads, SEH-protected
  `QueryInterface`, exact runtime class, dispatcher access, and complete tree
  profile. Any next slice must remain diagnostic-only and validate that bridge
  inside a naturally occurring layout callback before mount logic is enabled.

### 2026-08-13 API v11 Private-Bridge Diagnosis

- Commit `ddd4a6e` advances the standalone Host protocol to API version 11 and
  connects the existing private `TaskbarFrame` bridge only to the read-only
  diagnostic callback. The path remains gated by the exact Windows/PE/prologue
  profile, active detour state, exact TLS callback token, captured owner thread,
  bounded slot/interface/vtable reads, executable method validation, and the
  SEH-protected `QueryInterface(IFrameworkElement)` projection.
- A resolved projection is not sufficient: the exact
  `Taskbar.TaskbarFrame` runtime class, dispatcher access, loaded/geometry/
  shared-XamlRoot checks, and unique `RootGrid`, `BackgroundControl`, and
  `TaskbarFrameRepeater` landmarks must all pass. All apartment-affine XAML
  references are released before the natural callback returns.
- Historical diagnostics 0–38 remain unchanged. API v11 appends 39–55 for
  `private-bridge-awaiting-callback`, each bridge gate stage,
  `private-bridge-tree-profile-mismatch`, and `private-bridge-validated`.
  The first natural callback result is latched for the diagnostic session; only
  the validated value succeeds.
- Commit `69d892d` updates the disposable harness to wait at most 15 seconds
  for a naturally occurring callback and explicitly does not request relayout.
  This checkpoint never starts the weather worker, creates a view, acquires a
  lease, appends/mounts a child, or enters the readiness hold interval. Its
  `finally` path still performs bounded idempotent stop and final
  `not-started` verification.
- Isolated Release output is in
  `.testbuild/native-taskbar-weather-v11`. The Native suite passes 13/13, the
  complete managed suite passes 1148/1148, Debug x64 builds with zero warnings
  and zero errors, and PowerShell 7 plus Windows PowerShell pass the AST/
  `DescribeOnly` gates. The native import gate remains free of WebView, browser,
  and network dependencies. Signing and a fresh disposable Explorer runtime
  diagnosis remain pending; M3-07/M3-08 stay in progress.

### 2026-08-13 Signed API v11 Artifacts

- The isolated v11 Broker and Host are signed with the existing current-user
  code-signing certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`. SignTool verified both chains
  with zero warnings and zero errors; neither file is timestamped.
- Signed SHA-256 hashes are
  `7C09AC3137558CFF792BE07FA30F5284545CE378EBD6A6189C02DC75BBFB48D5`
  for `TaskFlyout.TaskbarBroker.exe` and
  `136F649ECE0BBC3CE2D616E87F0522E6864F98DAAE77D1CD92D9FD7A60F32CF7`
  for `TaskFlyout.TaskbarHost.dll`.
- The current Explorer may still pin the previously loaded v10 Host. Restart
  only the disposable Explorer session before running the signed v11 harness;
  the harness itself will not restart Explorer or synthesize a relayout.
  M3-07/M3-08 remain in progress.

### 2026-08-13 Signed API v11 Natural-Callback Diagnosis

- After restarting the disposable Explorer, the strict profile probe accepted
  Windows build `26200`, Explorer process `14536`, and primary taskbar thread
  `53556`. Module inspection confirmed that the new process loaded the signed
  v11 Host from `.testbuild/native-taskbar-weather-v11`, not the previously
  pinned v10 binary.
- The signed harness started from a `not-started` baseline and remained at
  `private-bridge-awaiting-callback` for the full bounded 15-second window. No
  natural call to the allowlisted
  `TaskbarFrame::OnTaskbarLayoutChildBoundsChanged` target was observed, and
  the controller intentionally did not request relayout or call the private
  target synthetically.
- The harness failed closed and completed its `finally` cleanup. An independent
  status acknowledgement returned `not-started` with diagnostic `none`; the
  independent strict probe still returned `supported`, and Explorer process
  `14536` remained responsive.
- This result does not reject the guarded private-object bridge because the
  bridge callback never ran. It does show that the current low-frequency layout
  target cannot serve as a deterministic diagnostic bootstrap on an idle
  taskbar. The next experiment must identify an exact-profile, owner-thread
  callback that naturally supplies the same private object, or a separately
  justified shell-owned layout request; it must not invoke the private method
  directly, broaden the ABI allowlist, or enable mounting. M3-07/M3-08 remain
  in progress.

### 2026-08-13 Diagnostic-Only App Lifecycle Guard

- The managed broker protocol now reads the additive fixed
  `controllerDiagnostic` property and recognizes the exact API v6-v11
  `bootstrap-*`, `root-bootstrap-validated`, and `private-bridge-*` allowlist as
  diagnostic-only. Unknown future prefixes retain the older unverified behavior
  instead of silently expanding this security boundary.
- A diagnostic-only acknowledgement can arrive from either start or a later
  status query. In both cases the coordinator performs a bounded idempotent stop
  before publishing the terminal fallback state, clears the controller identity
  and mount lease, restores the cleanup decision atomically, and blocks an
  automatic repeat start. If cleanup is ambiguous or exceeds its bound, the
  state remains recovery with the cleanup obligation preserved.
- This prevents an API v11 diagnostic Host from being mistaken for a mountable
  weather controller, leaving its detour active, or retaining Windows Widgets
  suppression while the app waits for mount readiness that v11 cannot produce.
  The Task Flyout fallback remains visible. The managed suite passes 1157/1157
  and the Debug x64 app build completes with zero warnings and zero errors.

### 2026-08-14 Maintenance Checkpoint Archived

- Commit `abe66fa` archives the managed diagnostic-only lifecycle guard. It
  covers both immediate start acknowledgements and delayed status diagnostics,
  with bounded cleanup, fail-closed fallback, and no automatic retry loop.
- Commit `48bc91c` archives the native callback safety guard. Secondary
  taskbar-thread callbacks are ignored before they can consume the one-shot
  bridge result, and the read-only callback no longer uses `noexcept` across the
  detour exception boundary. The v11 native suite remains 13/13.
- The next PDB experiment was intentionally paused at this checkpoint. The
  existing DIA helper could validate the full PDB identity but did not enumerate
  symbols through its global `findChildren` query; no new RVA/profile was
  accepted or added. The only recorded candidate remains the offline note from
  the existing symbol tooling: `TaskbarFrame::OnTaskbarLayoutChildBoundsPending`
  at RVA `0x001DFE40`, which is unvalidated and must not be hooked or loaded.
- Working-tree changes in `Package.appxmanifest` and
  `windhawk/task-flyout-weather-companion.wh.cpp` remain user-owned and are not
  included in the maintenance commits.

### 2026-08-17 API v12 Pending-Layout Checkpoint

- Commit `bc8bdcb` advances the diagnostic-only Host protocol to API version
  12 and replaces the low-frequency changed-layout target with
  `TaskbarFrame::OnTaskbarLayoutChildBoundsPending` at RVA `0x001DFE40`. The
  exact Windows build, PE fingerprint, 20-byte prologue, owner thread, detour
  TLS token, private bridge gates, and complete tree profile remain mandatory.
  The Host still does not request relayout, start the weather worker, create a
  view or lease, modify the taskbar tree, or enter the mount path.
- A first strict probe rejected a manually mistranscribed prologue before any
  Explorer injection. Direct PE mapping of the allowlisted image identified
  the omitted displacement byte; the corrected exact bytes then passed a
  clean strict probe. This is evidence that the on-disk prologue gate remains
  fail-closed rather than silently accepting a near match.
- Clean Release output is isolated in
  `.testbuild/native-taskbar-weather-v12-verified`. The Native suite passes
  13/13, the import gate remains free of WebView, browser, and network
  dependencies, and the strict probe accepts Windows build `26200`, Explorer
  process `52884`, taskbar thread `56812`, and the existing allowlisted
  `Taskbar.View.dll` fingerprint.

### 2026-08-17 Signed API v12 Natural-Callback Diagnosis

- The verified Broker and Host are signed with the existing current-user
  code-signing certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`. SignTool verified both chains
  with zero warnings and zero errors; neither file is timestamped. Raw signed
  file SHA-256 hashes are
  `90CE38FAC68E49B892B02DD4186E3D20B364006BAC9B6CC75A24369BDABC9202`
  for `TaskFlyout.TaskbarBroker.exe` and
  `717D44387A9DA230EF8383E315DC4852509E557EEB008F57CF5DD9654C6A0DC8`
  for `TaskFlyout.TaskbarHost.dll`.
- Module inspection confirmed that Explorer process `52884` loaded the Host
  from `.testbuild/native-taskbar-weather-v12-verified`, not an earlier v11 or
  rejected v12 artifact. The signed harness started from a `not-started`
  baseline and remained at `private-bridge-awaiting-callback` for the full
  bounded 15-second window. No natural call to
  `TaskbarFrame::OnTaskbarLayoutChildBoundsPending` was observed, and the Host
  did not synthesize a relayout or call the private target directly.
- The harness failed closed and completed its `finally` cleanup. An independent
  status acknowledgement returned `not-started` with diagnostic `none`; the
  independent strict probe still returned `supported`, and Explorer remained
  responsive. The pending-layout target therefore is not a deterministic idle
  bootstrap on the tested taskbar either.
- This experiment is archived without enabling mount logic. Any future slice
  requires new evidence for a naturally occurring owner-thread callback or a
  separately justified shell-owned request; it must not broaden the ABI
  allowlist, invoke the private method synthetically, or enable worker/view/
  lease creation before the bridge is diagnosed successfully. M3-07/M3-08
  remain in progress.

### 2026-08-31 Background Agenda Refresh

- The app-level one-minute heartbeat now reads the current
  `SyncIntervalMinutes` value and force-refreshes the same 14-day-past through
  90-day-future range used by the Flyout whenever an agenda account is present.
  The attempt timestamp is recorded before remote I/O so a failed provider
  cannot create a one-minute retry loop.
- Upcoming-event checks run after the refresh attempt and explicitly invalidate
  their five-minute snapshot first. This also applies to a visible Flyout sync,
  so newly fetched events can produce reminders without waiting for the old
  snapshot to expire.
- `BackgroundRefreshSchedulePolicyTests` pass 9/9, the complete managed suite
  passes 1160/1160, and the Debug x64 app build completes with zero warnings
  and zero errors. Existing user-owned changes in `Package.appxmanifest` and
  `windhawk/task-flyout-weather-companion.wh.cpp` remain excluded.

### 2026-08-31 Mail Notification Target Navigation

- Background polling now persists a bounded, body-free unread metadata slice
  even when the Mail page has never been opened. It deliberately omits the
  pagination-completeness marker, so normal folder navigation still performs a
  provider fetch instead of treating the five-message poll result as complete.
- Toast activation matches the account, folder, and message identity exactly,
  with a narrow exception for explicit localized Inbox aliases. If a provider
  folder reference is stale, cached cross-folder identity is allowed only for
  configured Google and Outlook accounts; IMAP remains folder-scoped even when
  legacy cache metadata lacks UIDVALIDITY.
- `MailNotificationNavigationPolicyTests` pass 11/11, the complete managed
  suite passes 1171/1171, and the Debug x64 app build completes with zero
  warnings and zero errors. A live click on a newly generated mail toast remains
  the final packaged-runtime check. Existing user-owned changes in
  `Package.appxmanifest` and `windhawk/task-flyout-weather-companion.wh.cpp`
  remain excluded.

### 2026-09-01 Google OAuth Scope Minimization

- Google initial consent now requests three scopes instead of five: Calendar,
  Tasks, and Gmail Modify. The pinned Gmail SDK describes Gmail Modify as
  covering read, compose, and send, so the separate Gmail Readonly and Gmail
  Send scopes were redundant for Task Flyout's current feature set.
- Existing tokens containing the previous five-scope superset continue to
  restore silently. Tokens without Gmail Modify enter the existing explicit
  reconnect-required state; startup sync and background mail polling still
  cannot launch an authorization browser.
- This reduces the consent surface but does not remove Gmail's restricted-scope
  verification requirements or guarantee approval. Scope policy tests pass 4/4,
  the complete managed suite passes 1173/1173, and the Debug x64 app build
  completes with zero warnings and zero errors. Existing user-owned changes in
  `Package.appxmanifest` and `windhawk/task-flyout-weather-companion.wh.cpp`
  remain excluded.

### 2026-09-01 Google OAuth Verification Resubmission Packet

- The public README guidance no longer instructs users to bypass an
  unverified/blocked consent warning. The English and Chinese privacy policy
  now describe the exact Calendar, Tasks, and Gmail Modify data uses, local
  DPAPI protection, retention/deletion behavior, and Limited Use restrictions.
- `docs/google-oauth-verification.md` records the three-scope justification,
  direct-device data flow, paste-ready submission text, reviewer steps, English
  demo shot list, official references, and a console checklist. The packet
  intentionally contains no reviewer credentials, refresh tokens, or client
  secrets.
- The packet and local disclosures are complete and ready for review. Actual
  Cloud Console resubmission remains blocked on publishing the updated site,
  verifying the domain/branding, recording the unlisted demo, and securely
  preparing a synthetic reviewer account; Google approval is not implied.

### 2026-09-01 Google Auth Platform Live Audit

- A read-only console review confirmed that Audience is External/In production,
  with 3 of the unverified-app lifetime limit of 100 users, and that one active
  Desktop OAuth client named `Task Flyout` remains configured.
- Branding contains the expected app name, homepage, privacy link, logo,
  authorized domain, and contacts, but still displays the previous review
  finding that homepage ownership was not verified. A follow-up read-only
  Search Console audit confirmed that the exact URL-prefix property exists,
  HTML-tag verification is currently successful, and the signed-in project
  owner is a verified owner. The issue must be resubmitted as resolved after the
  updated site is published; the existing property must not be recreated.
- Data Access still contains the former Calendar, Tasks, Gmail Modify, Gmail
  Readonly, and Gmail Send set. The scope-justification and YouTube fields are
  empty. M0-06 therefore remains externally blocked in this order: publish the
  updated `master:/docs` site, resubmit and publish Branding, reduce Data Access
  to the three current scopes, add the justification/video/reviewer material,
  and then resubmit.

### 2026-09-01 OAuth Publication Checkpoint

- PR #2 was merged with merge commit `1837427`, preserving the maintenance
  branch's focused history. Master Quality run `33518068424` and Pages
  deployment `33518065281` both completed successfully.
- The live home page and privacy policy now return the published September 2026
  content. The home page visibly describes Google Calendar, Google Tasks, and
  Gmail; the privacy policy identifies Gmail Modify, excludes redundant Gmail
  Readonly/Gmail Send consent, and contains the data-flow, retention, deletion,
  and Limited Use disclosures.
- The publication prerequisite for M0-06 is complete. M0-06 remains `BLOCKED`
  on the external steps: resubmit the stale Branding ownership issue as
  resolved, publish the verified brand, reduce Data Access to the three current
  scopes, provide the unlisted English demo and synthetic reviewer account, and
  submit the new Verification Center request.

### 2026-09-02 Google Data Access Scope Reduction

- In the `mytaskflyout` project, `gmail.readonly` and `gmail.send` were removed
  and the change was saved. Data Access now contains exactly Calendar, Tasks,
  and Gmail Modify, matching the current build and public disclosures.
- Verification Center still reports that the brand is not displayed and keeps
  `Prepare for verification` disabled. Scope justifications, the YouTube demo,
  reviewer instructions, and synthetic credentials remain pending external
  submission work. M0-06 remains `BLOCKED` until Branding is published.

### 2026-09-02 Flyout Focus-Loss Dismissal

- The Flyout now rechecks the actual foreground host when its opening
  transition completes. If focus moved elsewhere while the dependency's open
  animation was suppressing `Hide()`, an unpinned Flyout closes instead of
  allowing the completion path to reclaim focus.
- The foreground probe recognizes only this process's DesktopFlyout host and
  fails open when Windows cannot provide a foreground handle. Pinned mode and
  an explicitly disabled `HideOnLostFocus` remain unaffected.
- `FlyoutDismissalPolicyTests` cover focused, unfocused, pinned, disabled, and
  unknown-focus cases. The Debug x64 app build completes with zero warnings and
  zero errors; packaged-runtime rapid-click validation remains in the final
  four-item test pass.

### 2026-09-02 Calendar Time And Refresh Consistency

- Existing event dialogs now hydrate their controls from structured start and
  end values instead of reconstructing them from display text. Legacy cache
  entries retain a narrow parser fallback, and optimistic updates persist both
  endpoints so reopening an edited event preserves its complete interval.
- The right-side agenda renders start and end times, the toolbar no longer
  exposes a horizontal scrollbar, and the cache content signature includes the
  event end time so end-only provider changes are persisted.
- Shared agenda-cache publications carry a monotonic version. A loaded Calendar
  page coalesces notifications onto its dispatcher and redraws the current
  month, week, year, and upcoming range without issuing another network fetch;
  stale or reordered callbacks cannot replace a newer view.
- Google mutations now target the source `CalendarId` for single events and all
  recurring deletion modes, falling back to `primary` only for legacy records
  without an ID. The managed suite passes 1193/1193 and the Debug x64 app build
  completes with zero warnings and zero errors. The final packaged test pass
  should still exercise an event from a non-primary Google calendar and
  background refresh while the page is open.

### 2026-09-03 Mail Unread Consistency (In Progress)

- The first M1-14 slice makes optimistic read and flag mutations converge by
  provider identity across every in-memory and persistent message window.
  Google and Outlook IDs are account-scoped across folders; IMAP identities
  remain scoped to account, folder, UIDVALIDITY, and UID.
- Read-state changes collect unique affected folder IDs before invalidating
  unread-only windows and adjusting each cached folder representation once.
  Open-page publications and notification withdrawal remain in the final
  M1-14 slice.
- The focused cache-policy tests pass 8/8, the complete managed suite passes
  1197/1197, and the Debug x64 app build completes with zero warnings and zero
  errors. Existing user-owned manifest and Windhawk changes remain excluded.

### 2026-09-03 Mail Authoritative Unread Reconciliation

- Provider pages now retain their continuation state through background polls.
  A complete unread snapshot replaces the cached unread set, including an
  empty result; a truncated five-message poll only merges its known prefix and
  cannot delete unknown older unread mail.
- Current queued read and flag intents are applied before provider results enter
  memory or persistent caches. Google and Outlook pending intents converge by
  account-scoped provider identity, while IMAP retry identity includes folder,
  UIDVALIDITY, and UID. Expired or permanently failed retries invalidate their
  optimistic message and folder windows so stale state must be refetched.
- Folder and message windows persist their actual provider-fetch timestamps.
  Missing, expired, future, or orphaned timestamps cannot grant a restored
  cache a fresh ten-minute lifetime, and partial background merges do not renew
  an existing window's age.
- The complete managed suite passes 1211/1211 and the Debug x64 app build
  completes with zero warnings and zero errors. M1-14 remains in progress for
  open-page cache publication, forced folder-count refresh, and deterministic
  mail-toast withdrawal.

### 2026-09-03 Mail Unread UI And Notification Convergence

- Mail folder and message cache commits now publish monotonic versions with an
  account/folder refresh scope. A loaded Mail page coalesces those callbacks onto
  its dispatcher, copies folder counts, and reconciles the selected cached
  message window in place without issuing another provider request or discarding
  the open message body and selection.
- Scheduled polling force-refreshes provider folder metadata before reading the
  Inbox unread slice, so cached badges no longer hide remote count changes for
  another ten-minute cache lifetime. The refresh button also requests folder
  counts and the selected message window together.
- Mail toasts use a stable 16-character account/provider identity tag in the
  `mail` group. Gmail and Outlook identities remain stable across folder copies;
  IMAP identities retain folder and UIDVALIDITY scope. Optimistic mark-read
  actions and authoritative unread removals withdraw the matching toast.
- Notification identity tests pass 5/5, the complete managed suite passes
  1216/1216, and the Debug x64 app build completes with zero warnings and zero
  errors. M1-14 is complete; the next packaged-runtime pass should still verify
  an external read-state change while Mail is open and a live toast withdrawal.

### 2026-09-03 Account-Scoped Provider Identity

- Connected agenda accounts now receive a stable account ID. Existing single
  Google, Microsoft, and iCloud records migrate to deterministic legacy IDs,
  while additional records receive opaque IDs without replacing the first
  account.
- Sync providers expose an account-scoped routing key. Agenda items, cache
  ranges, provider health, optimistic mutations, deduplication, and notification
  action targets retain that identity so equal remote item IDs from two accounts
  cannot read, overwrite, complete, or delete one another.
- Google credentials now use an account-specific protected SQLite scope. The
  former single Google token is copied once into the legacy account namespace,
  and clearing one namespaced provider no longer clears future sibling accounts.
- Account identity and notification-action tests pass 9/9, the complete managed
  suite passes 1224/1224, and the Debug x64 app build completes with zero
  warnings and zero errors. M3-09 remains in progress for provider hydration,
  interactive account selection, per-account Gmail linkage, and account-aware UI
  add/remove/reconnect flows.

### 2026-09-04 Google Provider Hydration And Account Selection

- Deferred account hydration now restores one `GoogleSyncProvider` for every
  saved Google account ID. Provider registration and removal use locked
  snapshots so account changes cannot invalidate an in-flight sync enumeration.
- New Google account flows can allocate an opaque provider identity and request
  Google's `select_account` prompt. A missing-scope retry explicitly requests
  consent while keeping the account-specific protected token store.
- Only `legacy-google` may migrate or clear the former shared Google token.
  Newly allocated accounts can no longer absorb an old credential and bypass
  the account picker. Namespace-policy tests pass 13/13 and the Debug x64 app
  build completes with zero warnings and zero errors.
- M3-09 remains in progress for connecting the new provider from both account
  entry points, linking each Gmail mailbox to its provider account ID, and
  making create/remove/reconnect UI actions account-aware.

### 2026-09-04 Multi-Google Account Routing

- Both Google add-account entry points now allocate an isolated provider,
  require explicit account selection, reject duplicate Gmail identities, and
  clean only the temporary authorization namespace after cancellation or
  failure.
- Gmail folders, messages, bodies, mutations, label moves, undo, and sending
  resolve the provider linked to the selected mailbox. Calendar, Tasks, and
  Flyout create/remove actions carry the selected account ID instead of routing
  through the first Google provider.
- Legacy Gmail and Outlook records acquire their deterministic provider account
  IDs, and a legacy Google agenda label is hydrated from its Gmail address.
  Per-account Flyout health no longer collapses sibling Google providers.
- Link-policy tests pass 15/15 and the Debug x64 app build completes with zero
  warnings and zero errors. M3-09 remains in progress for per-account reconnect
  controls, localized account actions, the complete managed suite, and packaged
  multi-account runtime validation.

### 2026-09-04 Per-Account Health And Reconnect

- The account health center now renders every connected identity separately,
  including its own cached/reconnect/last-success state and an account-scoped
  reconnect action. Google, Microsoft, and iCloud reconnects reuse the selected
  account ID and preserve visibility filters.
- Google reconnect temporarily removes the selected namespace token to force an
  interactive account choice. Cancellation, missing scopes, and an email
  mismatch roll back to the prior token; a mismatched identity is never linked
  to the selected Calendar, Tasks, or Gmail account.
- Account display-name bindings now update when a migrated or reconnected email
  address becomes available. New account, duplicate-account, reconnect, and
  mismatch text is localized in English, Simplified Chinese, and Traditional
  Chinese.
- Account-policy tests pass 20/20, the complete managed suite passes 1249/1249,
  and the Debug x64 app build completes with zero warnings and zero errors.
  M3-09 now only needs packaged runtime validation with two real Google test
  identities before it can be archived as complete.

### 2026-09-04 Flyout Activation-Handoff Regression

- Packaged runtime testing found that the post-transition focus check could
  mistake the taskbar or previously active window for a real focus loss and
  immediately close the Flyout after a tray click.
- Each open request now captures the foreground root-owner anchor. When the
  transition completes, the Flyout stays open if that same activation source is
  still foreground, but still closes if focus moved to a different window.
  The foreground probe recognizes the DesktopFlyouts host by both its window
  class and title and continues to fail open when Windows exposes no handle.
- The focused dismissal-policy tests pass 6/6, the complete managed suite passes
  1250/1250, and the Debug x64 app build completes with zero warnings and zero
  errors. The first packaged pass exposed the remaining host-activation gap
  recorded and resolved in the follow-up below.

### 2026-09-04 Flyout Host Activation Completion

- Packaged follow-up testing showed that the first tray open could remain owned
  by the previously foreground window. Light dismiss therefore did not become
  active until the user clicked inside the Flyout once.
- The opening handoff now observes the real DesktopFlyouts host throughout the
  transition, actively foregrounds its visible current-process host, and records
  whether that host ever owned the foreground. A later external click is still
  honored after the dependency finishes its animation, including a click back
  to the same window that was active before the tray gesture.
- Dynamic host classes such as `DesktopFlyoutHostClass.<guid>` are recognized by
  prefix. Tray double-click uses the same delayed Flyout toggle command as a
  single click, so a rapid gesture no longer opens the main Calendar window.
- Focused activation/dismissal tests pass 11/11, the complete managed suite
  passes 1255/1255, and the Debug x64 app build completes with zero warnings and
  zero errors.
- Signed package `1.4.4.3` was installed over `1.4.4.2` and manually validated:
  the first tray open dismisses when another window is clicked without requiring
  an initial click inside the Flyout, and rapid double-click no longer opens the
  main Calendar surface. The installed package reports healthy and the final
  MSIX SHA-256 is
  `8306D427FEDD4B93A76C4E937DBB652A0FAA78B486630EEFFBE175C433B37469`.
  M1-16 was archived as complete at that checkpoint; the corrected gesture
  requirement below reopens its packaged acceptance gate.

### 2026-09-05 Tray Double-Click Regression Correction

- The user confirmed the intended contract: single-click toggles Flyout;
  double-click opens the main application. The shared toggle binding added in
  `1f19378` changed that contract and was a regression, not a desired behavior.
- Separate commands now use a UI-dispatcher interaction coordinator. A main-window
  request invalidates single clicks still waiting for account hydration and
  suppresses toggles during the Flyout-to-main-window handoff.
- Explicit dismissal cancels a prepared-but-not-shown Flyout, stops activation
  retries, and waits for an in-progress open/close animation to close before
  opening the main window. Pinning does not block this explicit command;
  shutdown cancels the handoff. Existing external-click dismissal remains intact.
- Focused coordinator tests pass 9/9; the complete managed suite passes
  1264/1264; Debug x64 builds with zero warnings and zero errors.
- Runtime acceptance is still pending, not inferred from those tests: cold/warm
  single-click, double-click while hidden/open/opening/closing/pinned, double-click
  while hydration is delayed, outside click without first clicking inside Flyout,
  and a subsequent single-click after returning from the main window.

### 2026-09-05 Signed Tray Regression Candidate

- Generated Debug x64 candidate `1.4.4.4` from `b1fa8e6`, including the tray
  interaction fix `433c171`. The MSIX build passed with zero warnings/errors.
  This is a local test build, not a published release or a Release performance
  baseline.
- Artifact:
  `AppPackages/LocalLatest_1.4.4.4_20260905/Task_Flyout_1.4.4.4_x64_Debug_Test/Task_Flyout_1.4.4.4_x64_Debug.msix`.
  SHA-256:
  `3795F298CCCB5844692DC5D9C20BF37C207F3FE015D87DB679F4974D8B33452F`.
- Reused certificate `15303040F7CEECFDE9C023F15481D15C39CCDB46`. The normal
  sideload-build hook signed the bundled broker/host, and the MSIX was then
  signed with the same key. No certificate was created or exported and no
  trust-store changes were made. SignTool and Authenticode validation passed;
  this local signature has no timestamp.
- Read the packaged manifest as XML and verified version `1.4.4.4`, x64 and the
  expected publisher. The packaged application DLL and both native components
  are byte-identical to the verified build outputs.
- No installation, application restart, compatibility probe or Explorer
  injection was performed. M1-16 remains in progress for the runtime matrix;
  native taskbar experiments remain paused.
- Restored the user's manifest to `1.4.4.0`; its SHA-256 and the pre-existing
  Windhawk source SHA-256 exactly match the pre-build values. Neither file is
  staged or included in this checkpoint commit.

### 2026-09-05 User Acceptance And Responsiveness Planning

- After receiving candidate `1.4.4.4`, the user reported acceptance passed.
  M1-16 is complete for the requested single-click, double-click and outside-click
  flows. This is user-reported runtime evidence, not an agent-observed replay of
  every cold-start, pinned, animation or DPI scenario.
- This acceptance does not close multi-Google account, live unread/toast, OAuth,
  or native taskbar experiment gates. Those retain their individual statuses.
- The next pass prioritizes interaction latency and stable visible updates.
  Static inspection identified immediate working-set trimming and per-thread
  foreground transitions, synchronous pre-display search indexing, calendar
  tree rebuilds, and displayed-mail collection resets as measurement candidates.
  Their contribution to the user's perceived stutter is not yet measured.
- Added F0-01 and F1-01 through F1-04, with per-scenario measurement and regression
  gates in `performance-baseline.md`. Existing `MarkOnce` timestamps and the tray
  open-request span cannot stand in for repeated input-to-visible measurements
  or frame-pacing traces.
- This checkpoint changes documentation only: no runtime settings, power policy,
  startup/prewarm behavior, application code, package installation or diagnostics
  collection were changed. Existing automated results remain 1264/1264 and a
  clean Debug x64 build; they are historical, not rerun for this documentation.

### 2026-09-06 F0-01 Interaction Diagnostics, First Slice

- Implementation on `codex/interaction-performance`, based on the accepted
  behavior and planning checkpoint `fb3f464`. This is instrumentation only;
  no efficiency/trim, prewarm, synchronous-search or animation policy changes.
- New `performance-diagnostics-v2.csv` records monotonic elapsed durations,
  random operation IDs, first/subsequent command attempts, assembly MVID/version,
  configuration and architecture. Legacy logs are not rewritten. Process-start
  timing remains explicitly labeled `process_wall_estimate`.
- Flyout spans begin at single-click command dispatch, before efficiency restore,
  hydration and construction. Checkpoints mark cached-content application and
  open-state handling, followed by a guarded one-shot Rendering observation.
  Physical-input/gesture-recognition delay and actual frame presentation are
  not measured by this callback. Search open and user-input handling use the
  same approach without recording the query or indexed content.
- Closing, superseding or handing off to the main window cancels observations;
  disposed/finished operations cannot later report success. Started records
  expose operations without a terminal record. This does not prove packaged
  lifecycle wiring under every timing or DPI condition.
- Background writes have a 256-entry pending cap with cumulative loss/failure
  counters and best-effort flush status. Summaries isolate builds/configurations,
  cold/warm attempts and timing bases, default to separate runs, retain
  non-success outcomes, deduplicate log inputs, and suppress percentiles for
  incomplete groups. Cross-run combination requires explicit `-CombineRuns`.
- Verification: 1283/1283 unit tests passed; Debug x64 build passed with zero
  warnings/errors. The 19 added tests include monotonic-clock jumps, terminal
  outcome races, bounded writer backpressure/recovery, real CSV opt-in output,
  and the tray coordinator's per-command callback/hydration handoff.
  All 20 synthetic summary checks passed under both PowerShell 7 and Windows
  PowerShell. These fixtures are not application performance measurements.
- Remaining F0-01 work:
  signed Release control and reproducible runtime sampling, diagnostic overhead,
  short/long hidden intervals, power/prewarm context, dispatcher/allocation/GC
  observations and presentation traces. No performance gain is claimed.
- Commit: `diagnostics: measure correlated Flyout and search interactions`.
  The user's manifest and Windhawk edits remain outside this change. No package
  installation, trust-store mutation, OAuth action, Explorer injection, remote
  push or public release is part of this checkpoint.

### 2026-09-06 F0-01 Local Release Packaging Tooling

- Added `scripts/build-performance-control.ps1` to build an exact committed
  Release control with an existing certificate and a separate generated manifest.
  The opt-in project property leaves normal Debug/Release manifest selection
  unchanged; it does not override the SDK's existing BeforeCommon import chain.
- Read-only preflight checks require a valid four-component version and exact
  source commit; actual packaging rejects unrelated tracked/untracked source
  changes and existing output directories. It preserves the user-owned manifest
  and Windhawk file, never creates/exports keys or changes trust, and never
  installs the package or changes running application state.
- All 13 package-plan checks pass under PowerShell 7 and Windows PowerShell,
  including evaluated alternate/default manifest identity and SDK import-chain
  validation. Actual Release packaging/signature verification follows this
  tooling checkpoint so the artifact can refer to an exact source commit.
- Commit: `build: isolate local Release control manifest`. F0-01 remains active;
  neither a package build nor the planning checks establish performance results.

### 2026-09-06 F0-01 Signed Release Control Archive

- Built local Release x64 control `1.4.4.5` from
  `38ce24032c981a36f10cfa2fbf2b8d1ede8d0fd2`, with the diagnostics from `107a82d`.
  Exact artifact, package/payload hashes and pending runtime protocol are in
  [performance-control-1.4.4.5.md](performance-control-1.4.4.5.md).
- MSIX SHA-256:
  `A655EC4F2D6DFBF0E196E144BA99C3053F469F0CFD584AFE723DD36254B6D9C4`.
  Verified packaged manifest `1.4.4.5`/x64/publisher, Release assembly metadata,
  exact source informational version and build ID
  `0af25b29bbde4fb7932910b4f774e409`. Main DLL matches the ReadyToRun package-map
  input; native DLL/EXE match the signed Release outputs.
- Reused `15303040F7CEECFDE9C023F15481D15C39CCDB46`; MSIX and both native
  signatures are valid. No timestamp was applied. No certificate creation,
  key export, trust change, native execution or Explorer injection occurred.
- Release tests passed 1283/1283; Debug x64 build passed with zero warnings/errors.
  All 13 package-plan checks passed in both PowerShell implementations, and all
  20 synthetic summary checks passed. These are not runtime timing results.
- The user's manifest stays byte-identical at `1.4.4.0`; Windhawk changes are also
  untouched and unstaged. The installed app was not replaced, started, stopped
  or reconfigured. F0-01 remains IN PROGRESS for approved installation, packaged
  regression checks, comparable sampling and the remaining trace work.
- Commit: `docs: archive signed Release performance control`.

### 2026-09-06 F0-01 Installation Preflight

- The user approved installation/restart. Read-only checks found the healthy
  `1.4.4.4` package still running; the new `1.4.4.5` MSIX hash is unchanged.
- Computer Use did not expose a Task Flyout window or tray menu. A normal exit
  and inspection for unsaved editing could not be completed. Installation is
  paused pending the user's tray-menu Exit; no forced termination, duplicate
  launch, installation or runtime measurement occurred.
- F0-01 remains IN PROGRESS. The next action is to confirm the old process has
  exited and install the already-approved artifact, not to repeat packaging.

### 2026-09-06 F0-01 Deferred Update Accepted

- The old `1.4.4.4` process remained active on recheck. Submitted the verified
  `1.4.4.5` MSIX and bundled x64 runtime dependency using Windows' deferred
  registration option. The command succeeded without force-shutdown flags.
- Registered version and running process were still `1.4.4.4` immediately after
  submission. Update preparation has progressed, but new-version activation,
  UI regression and performance sampling have not occurred.
- Next: normal tray-menu Exit, reactivate the registered app, confirm version
  and process path `1.4.4.5`, then test. Do not repeat installation or claim the
  pending update is already running. No process was forcibly terminated.

### 2026-09-07 R1-03 Mail Notification Transport Regression

- The user reports that a Windows mail notification still opens Calendar.
  Read-only process inspection now confirms installed `1.4.4.5` is running;
  this is not dismissed as an unactivated old `1.4.4.4` process. F0-01 sampling
  is paused while the user-visible regression is addressed.
- Root cause: MailService uses SDK `AppNotificationBuilder.AddArgument`, whose
  payload separates arguments with semicolons. `NotificationActivationParser`
  explicitly rejected every semicolon and only split on ampersands. The service
  then interpreted rejected mail payloads as a generic main-window activation,
  causing the default Calendar page to appear. Prior tests used hand-written
  ampersand examples and did not exercise the SDK-generated wire format.
- Fixed SDK parsing and its limited percent encoding, preserving literal `+`
  in Outlook base64 IDs. Native warm/cold activation paths identify SDK input;
  legacy query-string callers remain supported. Validated routes now drive the
  dispatch switch; invalid mail/agenda/copy payloads are ignored instead of
  opening Calendar. Account/folder/message validation and agenda capability
  checks remain in place; no broad provider-ID character expansion was made.
- Shared startup suppression accepts only the exact `action=smoke` schema;
  extra arguments do not alter taskbar suppression after semicolon support.
  No native taskbar experiment was resumed and the manifest/Windhawk edits are
  preserved outside this work.
- Verification: 1307/1307 unit tests passed in both Debug and Release; the final
  Debug x64 application build passed with zero warnings/errors.
  The separate actual-SDK synthetic probe passed all 13 checks in the normal
  user runtime, without displaying notifications or accessing mailbox data.
  Signed candidate preparation follows; a real toast click
  remains a pending runtime check, not an inferred success from these tests.
- Commit: `fix: parse SDK notification arguments before routing`.

### 2026-09-07 R1-03 Signed Mail Notification Candidate

- Built and signed Release x64 `1.4.4.6` from
  `0d4dad8bc4c9223bcf25cde4cffa43df3909c490` using the existing certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46`. No new key, trust change or
  timestamp was introduced. The existing local Release builder was reused;
  the directory name does not make this a measured performance result.
- Artifact:
  `AppPackages/PerformanceControl_1.4.4.6_0d4dad8/Task_Flyout_1.4.4.6_x64_Test/Task_Flyout_1.4.4.6_x64.msix`.
  SHA-256:
  `2A31D6C4B2BED474F2940AE45A6E90F17678EA1882036203A676526133E740F7`.
- Verified package name/publisher/version/x64, valid signature, assembly
  configuration `Release` and informational version
  `1.0.0+0d4dad8bc4c9223bcf25cde4cffa43df3909c490`. Packaged DLL matches the
  current ReadyToRun output (SHA-256
  `3F97268AFB32168C7DE75483A9EE95508FB544D0BCDAC8119AEEE564E8B0D7BD`),
  MVID `baa8f3b20cd14ae2ab0518a1050732a3`.
- The actual app remains running `1.4.4.5`. This candidate was not installed,
  the real process was not stopped, and no real notification/clipboard/mailbox
  action was synthesized. Both user-owned files remain byte-identical.
- Pending acceptance after normal tray Exit and installation: click a newly
  received mail notification while the main window is hidden and while Calendar
  is visible; verify the original account/message opens. Also check a retained
  notification after fully exiting the app for cold activation, and two already
  configured accounts if available. Missing/deleted targets may show the existing
  mail-page error, but must not silently fall back to Calendar. Do not clear user
  data or create/send mail merely to manufacture a test result.
- Commit: `docs: archive mail notification fix candidate`. R1-03 stays IN PROGRESS
  until live acceptance; F0-01 stays paused.

### 2026-09-07 Mail Acceptance And Search Account Identity

- The user reports the original mail-notification problem looks fixed and asks
  to resume the prior work. Read-only checks confirm a healthy installed and
  running `1.4.4.6`. R1-03/M1-13 are complete for that user-reported symptom;
  no claim is made about an agent-observed full multi-account/cold-start matrix.
- F0-01 resumes, but the two standard diagnostic-log locations contain no
  performance files. Runtime sampling still requires a diagnostic-enabled
  process. While that setup remains pending, the independent R1-01 correctness
  prerequisite was completed; efficiency/trim policies were not changed.
- Agenda search deduplication and payload/result IDs now share one key from
  `GlobalSearchPolicy.CreateAgendaId`. It includes the existing normalized
  provider/account route, calendar/list ID, item ID, date and task/event kind.
  Structural encoding prevents punctuation in provider-owned IDs from shifting
  key boundaries. Calendar/item IDs remain case-sensitive, and missing account
  IDs use the established legacy-account rules.
- The snapshot still applies account visibility before grouping and keeps the
  original AgendaItem as its navigation payload. Search ranking, group caps,
  synchronous indexing and user data are unchanged. The tests cover duplicate
  remote IDs from two accounts, lookup of both original targets, legacy accounts,
  provider aliases, casing, dates/kinds, delimiter collisions and UI culture.
- Verification: 27 focused search/account tests and 1322 total tests passed;
  Debug x64 build passed with zero warnings/errors. R1-01 is a source-level
  correctness fix, not a measured performance gain or a new installed build.
- Commit: `fix(search): preserve account-scoped agenda identity`. The user's
  manifest and Windhawk changes remain outside the commit; no package, app
  restart, live provider mutation or remote push occurred.

### 2026-09-07 F0-01 Process-Local Diagnostic Launcher

- Added `scripts/start-performance-session.ps1`, pinned by default to the
  accepted installed `1.4.4.6` version, DLL SHA-256 and MVID. It verifies package
  health/publisher and payload, refuses to knowingly start another instance,
  and enables performance logging only in the child process environment.
- The archived standalone taskbar experiment is suppressed for this run via
  its existing environment switch. This temporary difference from an ordinary
  launch is explicit in the plan/result and must be constant for comparisons.
  No persistent preference, power policy, certificate or trust store is changed.
- Launch success requires a new, timestamp-checked v2 run with the matching
  Release build identity. Timeout/error does not count as a sample and does not
  trigger automatic force-stop or relaunch. The hash preflight warms the binary
  file cache; process-cold results must not be called disk-cold results.
- All 14 preflight/invalid-input/running-process/environment-preservation checks
  passed under PowerShell 7 and Windows PowerShell. The existing `1.4.4.6` process
  remained running; the actual launch/log-confirmation branch is untested pending
  normal exit. No benchmark results, package installation or new app launch are
  claimed. Instructions are in `performance-baseline.md`.
- Commit: `test: add guarded performance session launcher`. F0-01 remains active;
  the next step is an actual diagnostic session, not another baseline rebuild.

### 2026-09-07 F0-01 First Diagnostic Run Confirmed

- The user ran `start-performance-session.ps1` successfully. Read-only process
  and log inspection confirmed process `35240` executing installed `1.4.4.6`,
  run `807dd548099940499c2d3f5277409e19`, Release build
  `baa8f3b20cd14ae2ab0518a1050732a3`, and the packaged LocalCache log location.
  The user-run launcher reported `StandaloneTaskbarSuppressed=True`.
- Initial snapshot: sequences 1-2, no recorded drops/write failures and no
  incomplete operations. `startup/tray_interactive`: 475.120 ms, one observation,
  explicitly a process wall-clock estimate. `calendar/first_remote_sync_completion`:
  3559.615 ms, one monotonic remote-operation observation including network work.
  Neither is a repeated Flyout-open measurement. No interaction P95 is available.
- Environment snapshot: Intel Core Ultra 7 155H, 16 cores/22 logical processors;
  Windows build 26200; reported visible memory 33,215,864 KiB; active power scheme
  Balanced. The Windows power-mode slider, DPI/refresh, prewarm preference and
  fixture sizes are not yet recorded, so these are provisional observations,
  not a complete comparative benchmark or proof of a performance gain.
- No Flyout or search operations appeared in this initial run snapshot. Next
  manual slice: single-click open, dwell about one second, click outside to
  dismiss, wait two seconds, repeat 21 times to target one first attempt and
  twenty subsequent successful opens. Keep settings unchanged and avoid mixing
  main-window/search operations into this first slice. Actual valid counts and
  cancellation/failure outcomes must be checked afterward.
- The launch/log-confirmation branch is now verified through the user's run plus
  matching log reads. F0-01 remains IN PROGRESS for interaction samples and the
  outstanding frame/GC/allocation/overhead work. No app setting, process or file
  in the user's data store was changed by this inspection; no raw log was added
  to Git. Commit: `docs: record first verified diagnostic run`.

### 2026-09-07 F0-01 Incomplete Reopen Capture

- After the user reported the requested operations complete, two fresh reads of
  both known log locations still found only one Flyout command/operation in run
  `807dd548099940499c2d3f5277409e19`. The same `1.4.4.6` process remains active;
  the log is 4851 bytes and its last recorded timestamp is 14:20:57 local time.
  No warm attempt, cancellation or ignored command was recorded. Do not infer
  that the user did not perform the operations or that unlogged attempts passed.
- The one first-open operation reached efficiency-restored at 5.831 ms,
  accounts-ready at 6.198 ms, cached-content-applied at 221.392 ms, open-state
  handling at 612.766 ms and the Rendering callback at 616.138 ms. These are
  cumulative command-relative observations, not physical-input or frame-present
  timings. The 391.374 ms between cached-content and open-state markers includes
  the show/transition/dispatch path, not a proven CPU or animation-only cost.
- Last emitted loss/failure counters are zero, but an unchanged log cannot rule
  out later write failures. Source inspection found no once-only gate on
  StartInteraction and no code that disables logging after initialization.
  No concrete cause for the missing repeats has been established.
- Pause the 21-cycle request. Next verify one known tray invocation and whether
  its log grows, confirming the actual entry/window before further collection.
  Do not change efficiency/trim or animation policy based on this single sample.
- This checkpoint is observation only; no application code, runtime setting or
  user data was changed. Commit: `docs: flag incomplete Flyout capture`.

### 2026-09-07 F0-01 Reopen Capture And Endpoint Correction

- After the user identified the taskbar icon and opened again, the same log grew
  from 4851 to 10084 bytes and contains two successful warm attempts, with
  terminal durations 372.836 and 373.840 ms. Their efficiency-restored markers
  are 10.647/6.076 ms; cached-content-applied markers 12.254/7.484 ms; open-state
  markers 367.020/366.329 ms. Last emitted loss/failure counters remain zero.
  Current input and writing work; the earlier missing batch is still unexplained,
  not proof of user error or a permanently broken logger. Two samples cannot
  produce a meaningful P95.
- The Computer Use window list did not expose this popup. No window was activated,
  clicked or captured, no alternate UI-control path was used, and no duplicate
  app was launched. Analysis used the correlated log and exact dependency source.
- DesktopFlyouts.WinUI 1.4.0's nuspec pins
  `bf5f4cf1e6bb23aff4bf4893f0de05635153eb3a`. At that revision, Show starts the
  storyboard and CompleteOpen sets IsOpen after its completion. BottomToTop is
  configured as 267 ms. The old command_to_rendering point was post-animation;
  it cannot establish first-feedback latency or prove an efficiency/trim stall.
- Updated the source to observe a desired opening's Rendering callback only
  when its own XamlRoot reports the host visible, separately from the renamed
  command_to_open_completed_rendering endpoint. Added a show-request-returned
  checkpoint. Cancellation, timeout, probe failure and unobserved early renders
  are explicit; callbacks unsubscribe and have a five-second timeout. These are
  still pre-presentation observations, not actual on-screen pixels or FPS.
- Verification: 1331/1331 unit tests pass, including nine new callback/timeout/
  checkpoint cases; Debug x64 build has zero warnings/errors. The callback tests
  use manual framework-event stubs, not real WinUI rendering. No animation,
  tray gesture, activation handoff, efficiency or memory-trim policy was changed.
  The running `1.4.4.6` instance and user data were left untouched.
- Commit: `diagnostics: separate opening feedback from completion`. Bundle this
  with the already-tested R1-01 search identity fix, then validate one new early
  observation before asking the user for another repeated-open batch.

### 2026-09-07 F0-01 Split-Endpoint Candidate

- Built/signed Release x64 `1.4.4.7` from
  `f394bb01cb4355aa5d50044d1fda522b7fb7cbff`, including R1-01 search identity
  and the split opening observations. Existing certificate
  `15303040F7CEECFDE9C023F15481D15C39CCDB46` reused; no trust change or timestamp.
- Artifact:
  `AppPackages/PerformanceControl_1.4.4.7_f394bb0/Task_Flyout_1.4.4.7_x64_Test/Task_Flyout_1.4.4.7_x64.msix`.
  SHA-256:
  `5EBF601438FC076AF990AF6B4BFAFE7CD41ACB4218DE14F83A064C52426BC86B`.
  Package manifest/signature verified. The packaged DLL matches its R2R output,
  with Release informational version pointing to the exact source above.
- DLL SHA-256:
  `19188D26AEA4671152078FA3D46696C6AFDD219B51F25F4118C6BB0B7A11F98F`.
  Build ID: `194eb8db62d24a46a8332cef1212a47c`.
  `Start-Performance.ps1` beside the package delegates to the existing guarded
  launcher with these exact identifiers; the default launcher still pins 1.4.4.6.
  Its syntax and rejection of the still-installed old version were checked
  without starting or stopping an application.
- Debug/Release tests: 1331/1331 each; Debug build: zero warnings/errors; signed
  Release packaging passed. No installed app was replaced or stopped. The same
  `1.4.4.6` process remains running; manifest and Windhawk source hashes are
  unchanged. No UI input, screenshot or private-memory dump was taken.
- After normal tray Exit and installing the candidate, use its sidecar launcher
  to start diagnostics. Verify only one open/close first: the early visible-host
  callback may still report unavailable/not_observed on this XAML-island host.
  Do not request another full batch until that point and the retained tray/mail
  behaviors are checked. Actual frame presentation and observer overhead remain
  pending. Commit: `docs: archive split-endpoint measurement candidate`.

### 2026-09-07 F0-01 Early Endpoint Validated On The Installed App

- The user started the version-specific launcher. Process `10584` runs installed
  `1.4.4.7`, run `826586eb218b4de1ba182cca22d11fab`, build
  `194eb8db62d24a46a8332cef1212a47c`. Initial startup recorded 493.529 ms
  (process wall-clock estimate) and the first remote-sync operation 2868.881 ms
  (monotonic, includes remote work), one observation each.
- After the requested single open/close, operation
  `5708d109449744d78ad1974a2d48c3a2` has successful cumulative checkpoints:
  efficiency-restored 14.264 ms; accounts-ready 14.677 ms; cached-content-applied
  1010.061 ms; Show-returned 1018.875 ms; host-visible Rendering 1100.630 ms;
  open-state handling 1405.621 ms; open-completed Rendering 1408.427 ms.
- Both rendering observations fired on the real XAML-island host, in the intended
  order. No timeout/cancel/unobserved outcome, sequence gap or reported write
  loss is present in this snapshot. These remain pre-presentation observations,
  not a direct measurement of the first visible screen pixels.
- This first-open operation spent 995.384 ms between accounts-ready and content
  application, which includes window construction, XAML loading/layout, content
  work and dispatch waits. It is not a measurement of cache I/O alone. The new
  observer is not enough evidence to attribute that interval to any one cause.
- Do not compare this single first-open observation to the two earlier warm
  samples or conclude the new build is slower. Next collect three warm open/close
  operations with one-second dwell and two-second hidden intervals in this same
  process, before requesting a larger sample. No settings changes or additional
  installation are needed. P95 still requires at least twenty valid comparable
  samples; frame pacing and observer overhead remain unmeasured.
- No application code or user data was changed during this log inspection.
  Commit: `docs: verify early-render endpoint on installed candidate`.

### 2026-09-07 F0-01 Three Reopens And Prewarm Follow-Up

- All three requested reopen operations in the same `1.4.4.7` run produced
  complete successful checkpoints. No cancelled, superseded, timeout, missing
  early observation, sequence gap or reported loss appeared in this snapshot.
  Per-operation cumulative timings (milliseconds, in observed order):

| Checkpoint | Reopen 1 | Reopen 2 | Reopen 3 |
| --- | --- | --- | --- |
| Efficiency restored | 14.460 | 7.000 | 7.012 |
| Cached content applied | 22.606 | 8.366 | 8.393 |
| Show returned | 31.179 | 12.506 | 12.308 |
| Host-visible Rendering | 97.342 | 65.281 | 66.637 |
| Open-completed Rendering | 405.287 | 365.279 | 378.116 |

- The earlier first-use content/early-render checkpoints were 1010.061/1100.630 ms.
  This points to initial window/content setup as a useful investigation target,
  not proof of a particular expensive method or a measured optimization gain.
  Keep the efficiency/trim and animation policies unchanged for now.
- The first reopen's command was roughly 36 minutes after the previous command;
  subsequent command-to-command gaps were about 8.506 and 7.433 seconds. Exact
  hidden intervals are not logged. Do not treat all three as a precisely timed
  two-second-hidden cohort, calculate P95 from three samples, or equate Rendering
  with physical presentation/physical click latency.
- Read-only inspection confirms an existing opt-in prewarm path: startup waits
  eight seconds, checks memory pressure/512 MiB usage ceiling/768 MiB headroom,
  waits for account/cache hydration, then constructs/prepares the hidden Flyout.
  The settings toggle only saves the preference; it applies on the next startup.
  Its actual value and whether the gates allowed it in this run are unknown.
- Next confirm the current 'Preload tray flyout after startup' setting before
  proposing an on/off comparison using the same installed build and diagnostic
  launcher. No new package is required for that existing option. Record the
  preference per run and keep unlike sessions separate; verify memory costs and
  cold-first feedback before changing defaults. No setting or runtime behavior
  was changed during this inspection.
- F0-01 remains IN PROGRESS; frame tracing, measurement overhead and a larger
  comparable sample remain outstanding. Commit: `docs: record warm reopen observations`.

### 2026-09-07 F0-01 Prewarm Preference Report (Corrected)

- The initial message was interpreted as off during the three additional tests.
  The user then clarified that they enabled the toggle before those tests.
  Read-only recheck confirms the same process 10584, started at 15:36:38 local,
  remains active: no restart occurred. No setting change was made by the agent.
- The log contains four new completed reopens since the preceding read, seven
  total. The intervening extra operation's early callback is 66.674 ms; the
  latest three are 70.324, 68.499 and 67.800 ms. Their content-applied checkpoints
  are 8.610, 8.089 and 8.539 ms, and open-completed callbacks 379.843, 374.554
  and 382.698 ms. All have complete successful endpoints, with no reported
  failures, loss or cancellation. Exact hide times are still not recorded.
- Annotate the latest tests as 'toggle enabled without restart, existing Flyout
  already initialized'. Do not label them a clean off-state batch or an enabled
  startup-prewarm treatment. The log does not expose the captured startup
  preference. Seven samples still do not qualify for P95.
- Next controlled step uses the existing option, not a new package or default
  policy: keep prewarm enabled in Settings > System & General, exit normally, restart
  through the 1.4.4.7 sidecar launcher, wait at least fifteen seconds without
  opening Flyout or the main window, and provide the new StartedAndLogging run.
  Inspect its pre-first-open footprint before requesting one first open.
- Enabled preference does not prove prewarm ran: memory gates and cache hydration
  can skip/delay it. Record that uncertainty and verify the new observations.
  The memory snapshot will describe that state, not a precisely attributed
  increase over unmatched earlier snapshots. Preserve the original run and
  existing settings context; do not combine on/off runs for a percentile.
- The initial interpretation was committed as `docs: record confirmed prewarm-off sample`;
  this correction supersedes that configuration label. F0-01 remains IN PROGRESS.

### 2026-09-07 F0-01 Toggle Timing Clarification

- `FlyoutPrewarmToggle_Toggled` only saves the preference; prewarm is scheduled
  from OnLaunched. Changing the toggle does not construct/prewarm the current
  process again. The latest three measurements are still valid reopen data,
  but cannot show the benefit of prewarming before a fresh process's first use.
- Next: normal Exit with the preference left on, restart through the same 1.4.4.7
  sidecar, wait at least fifteen seconds without opening an app surface, then
  inspect the new run and first-open path. No retest/repackage is needed just
  to correct this annotation. Commit: `docs: correct prewarm toggle timing`.

### 2026-09-08 Benchmark Pass Closed By User

- The user explicitly ended baseline testing and asked to continue implementation.
  Preserve the corrected toggle-without-restart history and partial observations;
  do not request another warm/cold/prewarm run or claim a completed P95/trace.
- F0-01/F1-01 are paused; efficiency, working-set trimming, animations and the
  existing prewarm default remain unchanged. No diagnostic app was launched,
  stopped or reconfigured and no runtime log was collected during this change.
- Continue with the independent synchronous-search issue. The earlier audit's
  RSS description needed refinement: normal SQL paging already excluded HTML;
  synchronous pre-display queries/decryption and the broad article payload type
  were the remaining problems, not an unconditional SQL body read.

### 2026-09-08 M1-07 Asynchronous Global Search

- Open displays the search shell and local commands without first building a
  cache snapshot or loading an unrelated content page. Index work is queued only
  after the shell's initial Rendering callback; cache-source invalidations cannot
  bypass that ordering. This is not a physical frame-presentation measurement.
- Cache cloning, protected mail-cache reads, RSS metadata queries, candidate
  construction and nonempty query matching run on background workers. UI-owned
  account visibility is copied into a private detached filter using existing
  account rules; mail account IDs and localized labels are captured on the UI
  thread. Live ObservableCollections and UI controls are not read by workers.
- Each index/query worker admits at most one synchronous reader/matcher. New
  requests cancel older work; generation, session, snapshot and source-version
  checks reject stale publication. Text matching is cancellable and debounced;
  empty queries keep commands immediate. Closing/hiding/exiting cancels work,
  unsubscribes source events, clears payloads and restores navigation state.
- Agenda/mail publication, account changes and RSS metadata changes invalidate
  the snapshot. Activation checks current source/account validity and retains
  original account-scoped agenda/mail targets. RSS search holds typed metadata
  records with no HTML field; the existing reader loads only the selected body.
  Normal SQL paging remains body-free, now with cancellation between rows/pages.
  Existing one-time storage initialization/migration behavior was not rewritten.
- Added a restrained loading indicator and localized unavailable-cache feedback.
  A failed reader leaves commands usable; subsequent opens/source updates retry.
  Existing ranking, per-group limits, 1000-article cap and reader privacy rules
  are preserved. Blocking SQLite calls may still finish before cancellation is
  observed, but they no longer run in the UI opening path or publish stale data.
- Verification: 1340/1340 unit tests passed in Debug and Release; final Debug x64
  app build has zero warnings/errors. Nine new cases cover cancelled/superseded
  workers, queued-work bounds, failure recovery, search cancellation and SQL
  metadata queries with the body column removed. No benchmark was performed.
- Commit: `perf: build global search index asynchronously`. No package installation,
  live WinUI interaction, account mutation or remote push was performed. The
  original manifest and Windhawk source changes remain untouched/unstaged.
  M1-07 remains open for ordinary functional UI acceptance, not more timing runs.

### 2026-09-08 M1-07 Publication Boundary Follow-Up

- Pre-package review found that a query could capture a newer live source
  revision while still using an older snapshot. Queries now retain the revision
  assigned to their actual snapshot; index and query publication share the
  current-request/source-revision guard. Two regression tests cover mismatched
  source revisions and superseded/closed requests with matching revisions.
- A queued rebuild rechecks shell readiness when dispatched, so a late source
  callback cannot start cache preparation before a newly opened shell is ready.
- Debug and Release unit suites pass 1342/1342. This is functional race coverage,
  not a benchmark. No running app, power/prewarm setting, account or user cache
  was changed; original manifest and Windhawk source changes remain excluded.
- Commit: `fix(search): bind query results to snapshot revision`. A normal signed
  functional package follows; no diagnostic launcher or repeated timing request
  will be added to its acceptance steps.

### 2026-09-08 M1-07 Signed Functional Candidate

- The initial packaging request did not execute because the approval service
  returned 503. After the user explicitly requested another packaging attempt,
  the build and existing-certificate signing completed successfully.
- Release x64 `1.4.4.8` source:
  `b17f4294ab24319c9d60083db89918369e4d2704`, including asynchronous search
  `db56385` and the snapshot-revision follow-up.
- Artifact:
  `AppPackages/PerformanceControl_1.4.4.8_b17f429/Task_Flyout_1.4.4.8_x64_Test/Task_Flyout_1.4.4.8_x64.msix`.
  SHA-256:
  `ABC33DE83879D58D1E6E8D54104718DE127DB1A89CC475823D38B20F07B8DFE8`.
  The directory name comes from the reused build tool, not a renewed benchmark.
- Existing signer `15303040F7CEECFDE9C023F15481D15C39CCDB46` reused; SignTool and
  Authenticode validation passed. No certificate/trust change or timestamp.
  Verified manifest version/x64/publisher, Release assembly configuration and
  exact source informational version. DLL matches the current R2R output:
  SHA-256 `F8BC25621666867C5EAFE183483A6B143713A0BB0347E0E5126AA749EE14F900`,
  MVID `cbbcb2fe2c124f198966cc9cc197019c`.
- No app was installed, started or stopped; no diagnostic launcher, runtime log
  collection, timing loop or native taskbar experiment was performed. Original
  manifest and Windhawk hashes remain unchanged and outside this commit.
- Pending ordinary acceptance: normal Exit of the old version, install this
  exact package, launch normally, open Ctrl+K, type/change/clear a local query,
  close/reopen search, and open existing agenda/mail/RSS results. Verify correct
  account/target, responsive cancellation and preserved tray/mail-notification
  behavior. Do not run Start-Performance scripts or collect a new baseline.
- Commit: `docs: archive signed asynchronous search candidate`. M1-07 remains
  IN PROGRESS for functional UI acceptance.

### 2026-10-07 Outlook Explicit Authorization Candidate

- Implementation: `720017e860570156d4f5c25837ec25e37fa4ce37`. MS-AUTH-02 code is
  complete; live Microsoft and packaged UI acceptance are pending.
- 1352 Release tests pass; 9 new cases exercise production authorization
  fallback/cancellation/denial logic. Debug x64 builds with zero warnings/errors.
- Signed Release x64 candidate: `1.4.5.7`,
  `AppPackages/PerformanceControl_1.4.5.7_720017e/Task_Flyout_1.4.5.7_x64_Test/Task_Flyout_1.4.5.7_x64.msix`.
- SHA-256: `93958D7E4E0CD4ED57D20F8CEB3E30A459B7665BF1A702D5665786A66BD2F7E3`.
  Existing signer `15303040F7CEECFDE9C023F15481D15C39CCDB46`; package identity,
  SignTool, and Authenticode validation passed. Not installed or started.
- Pending manual checks: Add Outlook without an agenda account; reuse an
  existing Microsoft identity without calendar consent; cancel/deny/retry;
  restore after restart; runtime missing consent displays reconnection without
  a browser; first explicit Send can request Mail.Send; later explicit agenda
  connection can request its scopes. No outgoing test mail was sent.
- MS-AUTH-01 stays externally blocked. The school still requires admin approval;
  this build cannot override tenant policy. See [the unsent IT draft](microsoft-oauth-consent.md).
- Benchmarking and taskbar-weather experiments remain paused. Existing manifest
  and Windhawk modifications are preserved and excluded from the commits.

### 2026-10-07 Outlook IMAP OAuth2 Implementation

- Implementation: `dac3aa56796997e53c2b09ca9f37314393c7d898`.
  `MS-AUTH-03` code is complete; live protocol and packaged UI acceptance remain
  IN PROGRESS. Existing Outlook Graph and password IMAP entries are unchanged.
- New Mail account option: Outlook IMAP (OAuth2), with personal or work/school
  account selection and no password field. Read/unread convergence and mailbox
  operations reuse the existing account-scoped IMAP paths.
- Dedicated DPAPI MSAL cache for each mail ID; no use of the shared Graph cache.
  IMAP setup/reconnect and SMTP Send allow explicit incremental consent;
  runtime loading and polling only restore silently. Fixed TLS endpoints,
  home-account checks, setup cancellation/timeouts and account cleanup are wired.
- 1383/1383 Release tests pass, including 31 new endpoint/scope/identity/cache-name
  policy cases. Final Debug x64 application build: zero warnings and errors.
  The policy tests do not prove real MSAL, MailKit, DPAPI or packaged UI behavior.
  All three resource files parse without duplicate keys and contain the ten new
  localized entries. Staged diff whitespace check passes.
- Before live testing, check the existing Entra registration's account audience,
  desktop redirect and delegated Exchange `IMAP.AccessAsUser.All` / `SMTP.Send`
  permissions, plus mailbox IMAP / SMTP AUTH availability. No registration,
  tenant policy or consent grant was changed. `MS-AUTH-01` stays BLOCKED.
- [Connection instructions and acceptance matrix](outlook-imap-oauth.md) include
  cancel/deny/wrong account, restart, two-account isolation, unread convergence,
  silent missing consent, approved test send and removal/cleanup retry checks.
- Not packaged, installed or started; no actual Microsoft login or outgoing
  message was performed. Benchmark and native taskbar experiments remain paused.
  User manifest and Windhawk changes remain uncommitted and untouched.

### 2026-10-07 Outlook IMAP OAuth2 Signed Test Package

- User requested a local test package. Signed Release x64 candidate `1.4.5.8`
  uses source `96a1f3cf80b1514e23598109836e6e077c535da0`, including implementation
  `dac3aa56796997e53c2b09ca9f37314393c7d898`.
- Package:
  `AppPackages/PerformanceControl_1.4.5.8_96a1f3c/Task_Flyout_1.4.5.8_x64_Test/Task_Flyout_1.4.5.8_x64.msix`.
- SHA-256: `3A5F560323FA02757C07DCD7CB7480A38912DE1072ED77490D233C5A646EA463`.
  Existing signer: `15303040F7CEECFDE9C023F15481D15C39CCDB46`.
  Package identity/version/x64 architecture, SignTool and Authenticode checks
  passed. Local test package is signed without a timestamp.
- Re-ran Release tests: 1383 passed, zero failed/skipped. Packaging completed
  using the isolated manifest; pre-existing source manifest and Windhawk hashes
  stayed unchanged. Only this archive is committed after packaging.
- Not installed, launched or restarted. No login, outgoing test mail, tenant
  setting change, consent grant, diagnostic session or taskbar runtime attempt.
- For ordinary manual testing: exit the old app normally, install this exact
  MSIX, launch normally, then Mail → Add account → Outlook IMAP (OAuth2). Choose
  Work/school for the university account; personal accounts require their own
  selection and IMAP enabled. Follow [the acceptance checklist](outlook-imap-oauth.md).
- `MS-AUTH-03` remains IN PROGRESS for live/package acceptance. The owner still
  needs to check registration Exchange delegated permissions and protocol access;
  `MS-AUTH-01` remains externally BLOCKED. Packaging cannot bypass admin approval.
