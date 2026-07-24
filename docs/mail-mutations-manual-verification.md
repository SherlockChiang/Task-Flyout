# Mail mutation manual verification

## Included

- Outlook, Gmail, and IMAP: mark read/unread and flag/unflag for one or multiple messages.
- Offline/transient failures: optimistic state remains visible and the latest intent for each message and mutation kind is retried.
- Undo: restores each message's prior state and replaces any queued forward intent.

## Manual checks

1. Open one unread message with auto-mark enabled and confirm it becomes read. Select two messages and confirm selection alone does not open or auto-mark either message.
2. Run each command on mixed-state selections and verify the provider web UI reflects the final state.
3. Disconnect networking, issue opposite commands for the same message, reconnect, and verify only the last value wins. Restart before reconnecting to verify metadata-only persistence.
4. Use Undo after a mixed-state bulk operation and verify each item returns to its own prior state.
5. In an unread-only window, mark read and undo; verify removal/reinsertion and non-negative folder unread counts.
6. Repeat for Outlook, Gmail, and an IMAP mailbox with stable UIDVALIDITY. Change the IMAP mailbox generation and verify stale mutations are rejected rather than applied to another message.

## Deferred provider-specific operations

No archive, move, trash, or delete buttons are exposed. Outlook move returns a replacement message identity; Gmail archive/trash are label/system operations with folder-dependent semantics; IMAP move/trash support and expunge behavior vary by server and can invalidate UIDs. Safe offline replay therefore needs destination identity mapping and reconciliation that are outside this increment. Permanent delete is explicitly unsupported for every provider.
