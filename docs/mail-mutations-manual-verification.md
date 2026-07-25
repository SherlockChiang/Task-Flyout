# Mail mutation manual verification

## Included

- Outlook, Gmail, and IMAP: mark read/unread and flag/unflag for one or multiple messages.
- Offline/transient failures: optimistic state remains visible and the latest intent for each message and mutation kind is retried.
- Undo: restores each message's prior state and replaces any queued forward intent.
- Outlook: archive, move, and move-to-Deleted-Items operate on exactly one selected message online. Gmail: archive removes `INBOX`, move adds a user label and removes the current movable label, and trash uses Gmail's safe trash endpoint. These operations are never retried or queued offline.

## Manual checks

1. Open one unread message with auto-mark enabled and confirm it becomes read. Select two messages and confirm selection alone does not open or auto-mark either message.
2. Run each command on mixed-state selections and verify the provider web UI reflects the final state.
3. Disconnect networking, issue opposite commands for the same message, reconnect, and verify only the last value wins. Restart before reconnecting to verify metadata-only persistence.
4. Use Undo after a mixed-state bulk operation and verify each item returns to its own prior state.
5. In an unread-only window, mark read and undo; verify removal/reinsertion and non-negative folder unread counts.
6. Repeat for Outlook, Gmail, and an IMAP mailbox with stable UIDVALIDITY. Change the IMAP mailbox generation and verify stale mutations are rejected rather than applied to another message.
7. In Outlook, select exactly one message and verify Archive, Move to folder, and Move to Deleted Items are enabled. In Gmail Inbox, verify Archive, Move to label, and Move to trash are enabled; verify Archive is disabled outside Inbox, Move is disabled for non-movable system labels, and Trash is disabled in Trash. Verify all are hidden for IMAP and disabled for zero or multiple selections.
8. In Gmail, verify the move picker contains custom user labels only. Move a message whose destination label is already attached, then undo and verify the pre-existing destination label remains attached.
9. Archive, move, and trash Gmail messages, then use Undo. Verify the message identity remains stable and the original list is refreshed from Gmail rather than locally fabricated.
10. Interrupt a Gmail action after submission. Verify the app does not retry or queue it, refreshes labels/messages, and reports an unknown outcome. Confirm Gmail permanent delete and batch delete are never called.
11. Open Outlook Move and verify all nested folders are paged recursively, breadcrumbs identify duplicate names, and the current folder is absent.
12. Complete each Outlook operation and verify the item remains visible until Graph confirms success, then disappears and the detail pane clears. Verify source and destination unread counts change only for unread messages.
13. Select Undo and verify the returned Graph message ID is moved online to the original folder. Confirm the source folder force-refreshes and the restored message receives Graph's newest returned identity.
14. Interrupt the network after submitting a move. Verify the operation is not retried or queued, both folder caches/cursors are invalidated, and the source folder is force-refreshed with an outcome-unknown status.
15. Force Outlook draft send failure and verify cleanup moves the draft to canonical Deleted Items when that folder resolves; verify no message DELETE request is sent.

## Limitations

IMAP archive/move/trash remain unsupported. Permanent delete is unsupported for every provider. Outlook and Gmail membership-changing operations require a live connection and provider mail-write authorization. A timeout or transport break can occur after the provider accepted an action, so the app invalidates and reconciles without retrying; it cannot promise whether that individual operation completed.
