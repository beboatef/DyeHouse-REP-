# Error envelope and retry guidance

Every failure comes back as JSON with the same shape, produced in one place
(`ExceptionHandlingMiddleware`; the forced-password-change gate in
`MustChangePasswordMiddleware` uses the same fields):

```json
{ "title": "Human-readable sentence.", "status": 409, "code": "LOCK_TIMEOUT" }
```

- `title` is always present. Quote it in bug reports — it names the exact rule.
- `status` mirrors the HTTP status.
- `code` is present on the retryable conflicts and the password gate, and is what
  a client should switch on. Never parse the title.
- `errors` appears only on `400` validation failures (field → message list).
- `currentBalance`, `requestedQuantity`, `shortage` appear only on
  `NEGATIVE_STOCK` so the UI can show the numbers without a second call.

## The codes a client must handle

| HTTP | `code` | `title` (exact) | Meaning and retry guidance |
|---|---|---|---|
| 400 | — | `Validation failed` | A field value is rejected (`errors` lists them). Fix the input; retrying the same payload will fail the same way. |
| 401 | — | `Invalid username or password.` | Login/refresh credentials are wrong (or the refresh token was revoked). Send the user back to `/login`. |
| 403 | — | `You do not have permission to perform this action.` | Authenticated but lacking the permission. Do not retry; ask an administrator for the right. |
| 403 | `PASSWORD_CHANGE_REQUIRED` | `You must change your password before using the system.` | The account is flagged (first login, admin reset, expired password). Route to `/change-password`; every business call answers this until the change succeeds. |
| 404 | — | Entity-specific (`Warehouse`, `RawMessage`, … plus the id) | The record does not exist (or was never committed). Reload the list; do not retry the same id blindly. |
| 409 | `CONCURRENT_UPDATE` | `This record was changed by someone else while you were working on it. Reload and try again.` | Optimistic-concurrency conflict. Reload the document, re-apply the user's intent on the fresh copy, then save. Safe to retry after reload. |
| 409 | `LOCK_TIMEOUT` | `This stock is locked by another operation in progress. Please retry in a moment.` | Another posting holds the stock lock (`sp_getapplock`). Wait briefly and retry the identical request — no reload needed, nothing is lost. |
| 409 | `NEGATIVE_STOCK` | `Operation would result in negative stock. Current balance: {x}, requested: {y}, shortage: {z}.` | The posting would drive a lot negative. Either reduce the quantity or file a negative-stock override approval (`inventory.allow_negative_stock` to request, `inventory.approve_negative_stock` to decide — never the same user). |
| 409 | `DUPLICATE_RECORD` | `This record duplicates an existing entry. Adjust the value and try again.` | A unique index (code, number) already has the value — usually a double submit. Check the list before re-creating. |
| 409 | — | Domain message (e.g. document-locked) | `DuplicateCodeException` / `DocumentLockedException`: the document is locked or the code exists. Same guidance as `DUPLICATE_RECORD`. |
| 422 | — | Domain message | A business rule refused the operation: closed period (`The period … is closed. …`), deactivated warehouse (`Warehouse '…' (…) is deactivated and cannot be used …`), wrong warehouse kind, already-cancelled document, non-positive quantity, and 150+ siblings. Read the message — it names the fix. Retry only after changing the business facts. |
| 500 | — | `An unexpected error occurred.` | A real server fault. Safe to retry once; if it repeats, report it with the request id and timestamp. No stock was posted (the unit of work rolls back). |

## Retry rules of thumb

1. Retry as-is, after a short wait: `LOCK_TIMEOUT` only.
2. Reload, re-apply, then save: `CONCURRENT_UPDATE`.
3. Change something, then retry: `400`, `404`, `409 NEGATIVE_STOCK` / `DUPLICATE_RECORD`, `422`.
4. Never retry — redirect the user instead: `401` (→ login), `403` (→ permissions admin or `/change-password` for the password gate).
5. POSTs are safe to retry on `LOCK_TIMEOUT` because the lock serializes postings: either the first attempt committed (the retry then hits `DUPLICATE_RECORD` / idempotency) or it never posted. `CONCURRENT_UPDATE` after a retry means someone else committed first — reload.
