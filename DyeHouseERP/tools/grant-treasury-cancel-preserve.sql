/* ============================================================================
   Preserve previously-held treasury cancellation access after the permission
   split (treasury.create no longer implies treasury.cancel).

   BACKGROUND
   ----------
   Before the split, the three treasury reversal endpoints (receipt / payment /
   transfer cancel) were gated by `treasury.create`. Any user holding
   `treasury.create` could therefore cancel a posted treasury document. After
   the split those endpoints require the dedicated `treasury.cancel`
   permission, and `treasury.create` deliberately does NOT imply it at runtime.

   This script restores PARITY, not privilege: it adds `treasury.cancel` only to
   logins that ALREADY hold `treasury.create` and do NOT already hold
   `treasury.cancel`. It grants nothing to anyone who could not already cancel.
   It is NOT an EF migration and is NOT applied by the application on startup -
   run it deliberately, after reviewing the preview query, only if your
   deployment has non-admin users whose treasury cancellation ability must be
   preserved.

   NOTES
   -----
   * The seeded `admin` login carries the `admin` role, which is the documented
     catch-all and already passes every permission - it needs no change.
   * Permissions are a flat, comma-separated string on dbo.Users.Roles; there is
     no Role table. This script edits that column for the affected logins only.
   * Idempotent: re-running it changes nothing once everyone has the permission.
   * Take a database backup before running (see the release report).

   HOW TO USE
   ----------
   1. Run STEP 1 (preview) and review the affected logins.
   2. If correct, run STEP 2 inside a transaction.
   3. Confirm with STEP 3.
============================================================================ */

/* ---------------------------------------------------------------- STEP 1: PREVIEW (read-only)
   Logins that could cancel before the split and cannot yet: */
SELECT Id,
       Username,
       DisplayName,
       Roles
FROM dbo.Users
WHERE IsActive = 1
  AND ',' + Roles + ',' LIKE '%,treasury.create,%'
  AND ',' + Roles + ',' NOT LIKE '%,treasury.cancel,%'
ORDER BY Username;

/* ---------------------------------------------------------------- STEP 2: APPLY (review first!)
BEGIN TRANSACTION;

UPDATE dbo.Users
SET Roles = Roles + ',treasury.cancel',
    ModifiedBy = 'migration:treasury.cancel-preserve',
    ModifiedAtUtc = SYSUTCDATETIME()
WHERE IsActive = 1
  AND ',' + Roles + ',' LIKE '%,treasury.create,%'
  AND ',' + Roles + ',' NOT LIKE '%,treasury.cancel,%';

-- Expect the same row count as the STEP 1 preview.
PRINT CONCAT('Rows updated: ', @@ROWCOUNT);

-- COMMIT TRANSACTION;   -- uncomment only after the count matches STEP 1
-- ROLLBACK TRANSACTION; -- use this instead if anything looks wrong

/* ---------------------------------------------------------------- STEP 3: VERIFY
   Every active log in that holds treasury.create should now also hold
   treasury.cancel (no rows returned = parity restored): */
SELECT Id, Username, Roles
FROM dbo.Users
WHERE IsActive = 1
  AND ',' + Roles + ',' LIKE '%,treasury.create,%'
  AND ',' + Roles + ',' NOT LIKE '%,treasury.cancel,%';
