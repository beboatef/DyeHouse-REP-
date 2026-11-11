# Backup and Restore — Runbook

Losing ledger data is the largest operational risk this system carries: every balance
is a live sum over append-only ledger rows, so a lost ledger cannot be rebuilt from
anywhere else. The application ships the tooling (`tools/backup/`); running it on a
schedule, keeping copies off the machine, and drilling the restore are operator
duties. This page is the whole contract.

## 1. What to back up, how often

| What | How | Schedule |
|---|---|---|
| Full database backup | `tools/backup/backup-dyehouse.sh` (COMPRESSION + CHECKSUM, `COPY_ONLY=0`, UTC-timestamped name) | **Nightly**, off-peak |
| Transaction-log backups | SQL Server Agent job or the host scheduler calling `BACKUP LOG` | **Every 15 minutes** if point-in-time recovery is required (FULL recovery model); otherwise hourly, or SIMPLE model with nightly fulls only — business decision, default recommended: FULL + 15-minute logs |
| Off-machine copy | Copy each finished `.bak` (+ log chain) to separate storage (different host / object storage) **before** the next backup runs | Same schedule as the backup itself |
| Encryption at rest | Backup encryption (certificate) or encrypted target volume | Always — backups contain customer prices, payroll and credentials hashes |

Retention default: nightly fulls **30 days**, log backups **7 days**, one monthly full
**12 months**. Adjust to legal requirements; never keep fewer than 2 full cycles.

## 2. Taking a backup

```bash
export DYEHOUSE_BACKUP_PASSWORD='<sa-or-agent-password>'
./tools/backup/backup-dyehouse.sh \
  --database DyeHouseERP \
  --backup-dir /var/backups/dyehouse \
  --server db01.example.local \
  --user backup_agent \
  --password-env DYEHOUSE_BACKUP_PASSWORD
# Windows host example: --backup-dir 'D:\Backups' --path-separator '\'
```

Rules the wrapper enforces (and the runbook repeats because they matter):

- The database name must match `^[A-Za-z0-9_]+$` **and** be on the explicit
  allow-list (`DyeHouseERP` built in; extend with `--allow-db` or
  `DYEHOUSE_BACKUP_ALLOW_DBS`). Anything else is refused before `sqlcmd` starts.
- Credentials never live in the scripts: the password travels in an environment
  variable (or omit `--user` for trusted/integrated auth).
- `--backup-dir` is a **server-side** path: SQL Server writes it, so it must exist
  on the database server and be writable by the SQL Server service account.
- The script exits non-zero on any failure (`sqlcmd -b -V16`); a failed backup is
  never reported as success. Alert on non-zero exit.

## 3. The restore drill (do this before you need it)

```bash
export DYEHOUSE_BACKUP_PASSWORD='<sa-or-agent-password>'
./tools/backup/restore-verify.sh \
  --source-db DyeHouseERP \
  --backup-file /var/backups/dyehouse/DyeHouseERP_20260201_020000.bak \
  --scratch-db DyeHouseERP_restoretest \
  --report /var/log/dyehouse/restore-drill.txt \
  --server db01.example.local \
  --user backup_agent \
  --password-env DYEHOUSE_BACKUP_PASSWORD \
  --allow-db DyeHouseERP
```

What the drill proves, in order (any failure exits non-zero and aborts **before**
the drop step, leaving the scratch database in place for forensics):

1. `RESTORE VERIFYONLY` — the file is a readable backup.
2. `RESTORE DATABASE` into the scratch name with relocated files.
3. `DBCC CHECKDB WITH NO_INFOMSGS` — the restored database is physically sound.
4. Reconciliation — grouped ledger sums are **identical** between source and
   scratch: `InventoryTransactions` per warehouse/item/direction,
   `MaterialTransactions` per warehouse/material/direction, `TreasuryTransactions`
   per account/direction, `CustomerLedgerEntries` per customer,
   `SupplierLedgerEntries` per supplier.
5. Row counts match for `RawMessages`, `ProductionOrders`, `Deliveries`, `Invoices`,
   `TreasuryTransactions`, `InventoryTransactions` (written to the report file).
6. The scratch database is dropped — only after re-checking the `_restoretest`
   suffix inside the script itself.

Safety rails (both wrapper and SQL re-check, so no single bug can drop production):

- The scratch name **must** end in `_restoretest`; anything else is refused twice.
- Scratch and source must differ.
- The source database must be on an explicit allow-list passed with `--allow-db`
  (there is deliberately no built-in default for restore sources).

Run the drill **monthly** and after every infrastructure change (new host, new SQL
version, moved backup target). Keep every report; auditors will ask for them.

## 4. CI drill

The `backup-drill` CI job runs the same toolchain against the integration SQL
Server container on every push to `main`/`codex/*`/`feat/*`: it backs up the
integration database after a data-populating test class has run, restores it into
`*_restoretest`, runs the full `restore-verify.sql` (VERIFYONLY, CHECKDB,
reconciliation, counts, drop), and executes `BackupRestoreVerificationTests`,
which recompute balances through the application's own ledger services against the
restored copy. A red drill fails the build like any test failure.

## 5. Real restore checklist (production incident)

1. Stop the application (no new writes mid-restore).
2. Take a tail-log backup of the damaged database if the engine still runs
   (`BACKUP LOG ... WITH NORECOVERY`) — you may need "up to the minute", not
   "up to last night".
3. Pick the newest verified full backup **whose drill report is green**; never
   restore an unverified file in production.
4. Restore full (+ differentials, + log chain in order) with `RECOVERY` on the
   last file only.
5. Run `DBCC CHECKDB WITH NO_INFOMSGS`.
6. Re-run the reconciliation queries from `restore-verify.sql` §4–5 against the
   restored database and eyeball the counts report.
7. Point the application at it, smoke-test login + one read per module, then open
   traffic. Record the measured RTO/RPO below.

## 6. Measured RTO/RPO (fill in after the first drill)

| Metric | Target | Measured (date) |
|---|---|---|
| RTO — backup file to serving traffic | ≤ 2 h | ___ (___) |
| RPO — maximum data loss (log cadence) | ≤ 15 min | ___ (___) |
| Full backup duration (production size) | fits the nightly window | ___ (___) |
| Restore + CHECKDB duration | fits the RTO budget | ___ (___) |
