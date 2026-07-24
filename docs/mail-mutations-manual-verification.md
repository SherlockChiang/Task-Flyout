# Mail mutation manual verification

## Included

- Outlook, Gmail, and IMAP: mark read/unread and flag/unflag for one or multiple messages.
- Offline/transient failures: optimistic state remains visible and the latest intent for each message and mutation kind is retried.
- Undo: restores each message's prior state and replaces any queued forward intent.
- Outlook only: archive, move, and delete-to-Deleted-Items move exactly one selected message online. These operations are never retried or queued offline because Microsoft Graph returns a replacement message identity.

## Manual checks

1. Open one unread message with auto-mark enabled and confirm it becomes read. Select two messages and confirm selection alone does not open or auto-mark either message.
2. Run each command on mixed-state selections and verify the provider web UI reflects the final state.
3. Disconnect networking, issue opposite commands for the same message, reconnect, and verify only the last value wins. Restart before reconnecting to verify metadata-only persistence.
4. Use Undo after a mixed-state bulk operation and verify each item returns to its own prior state.
5. In an unread-only window, mark read and undo; verify removal/reinsertion and non-negative folder unread counts.
6. Repeat for Outlook, Gmail, and an IMAP mailbox with stable UIDVALIDITY. Change the IMAP mailbox generation and verify stale mutations are rejected rather than applied to another message.
7. In Outlook, select exactly one message and verify Archive, Move, and Delete are enabled. Verify they are hidden for Gmail/IMAP and disabled for zero or multiple selections.
8. Open Move and verify all nested folders are paged recursively, breadcrumbs identify duplicate names, and the current folder is absent.
9. Complete each Outlook operation and verify the item remains visible until Graph confirms success, then disappears and the detail pane clears. Verify source and destination unread counts change only for unread messages.
10. Select Undo and verify the returned Graph message ID is moved online to the original folder. Confirm the source folder force-refreshes and the restored message receives Graph's newest returned identity.
11. Interrupt the network after submitting a move. Verify the operation is not retried or queued, both folder caches/cursors are invalidated, and the source folder is force-refreshed with an outcome-unknown status.
12. Force Outlook draft send failure and verify cleanup moves the draft to canonical Deleted Items when that folder resolves; verify no message DELETE request is sent.

## Limitations

Gmail and IMAP archive/move/trash remain unsupported. Permanent delete is unsupported for every provider. Outlook move operations require a live connection and Microsoft Graph mail-write authorization. A timeout or transport break can occur after Graph accepted the move, so the app can only invalidate and reconcile; it cannot promise whether that individual operation completed.
