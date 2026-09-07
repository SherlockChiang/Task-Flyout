# Windows App SDK Notification Contract

This console probe uses the actual `AppNotificationBuilder` and button builder
from Windows App SDK `2.1.3`, matching the application's pinned SDK. It builds
synthetic XML payloads, reads their launch/button attributes as Windows would,
and passes them through the application's activation parser/router.

It never registers or displays a notification, navigates the real application,
contacts a provider, reads application data, copies a code, or mutates a mailbox.
SDK bootstrap prompts and deployment-manager initialization are disabled.
Keep this SDK pin aligned with the app when upgrading dependencies.

```powershell
dotnet restore Tests/TaskFlyout.NotificationContract/TaskFlyout.NotificationContract.csproj --locked-mode
dotnet run --project Tests/TaskFlyout.NotificationContract/TaskFlyout.NotificationContract.csproj -c Debug --no-restore
```

Requirements: x64 Windows, .NET 10 SDK and the matching Windows App Runtime
already installed for the signed-in user. A sandbox user may not have access to
that runtime registration. The initial sandbox attempt in this pass did not
complete SDK initialization; its test process was stopped, and the probe then
passed in the normal user context. The user's Task Flyout process was not stopped.

Expected result: 13 checks pass, covering actual SDK mail payloads (including
Outlook `+`, `/`, `=` IDs), automatic/explicit transport selection and agenda/
copy button payloads. This is a separate manual contract check, not part of
`Task_Flyout.Tests` or a live Windows notification-click acceptance test.

The pure unit suite separately rejects malformed/duplicate/extra arguments and
verifies they cannot fall back to the calendar. SDK argument transport uses `;`
separators and decodes only `%25`, `%3B`, `%3D`; it is not a URL query string.
