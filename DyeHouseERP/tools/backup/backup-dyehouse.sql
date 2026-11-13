/*
 * DyeHouseERP full backup (sqlcmd script, no credentials in this file).
 *
 * Usage (via backup-dyehouse.sh, which validates the database name first):
 *   sqlcmd -S <server> -U <user> -P <pw> -d master -b -V16 \
 *     -v DbName="DyeHouseERP" BackupDir="D:\Backups" PathSeparator="\" \
 *     -i backup-dyehouse.sql
 *
 * sqlcmd variables:
 *   DbName         - the database to back up. Must already exist; the .sh
 *                    wrapper additionally requires it to be on an explicit
 *                    allow-list before sqlcmd is even started.
 *   BackupDir      - server-side directory the backup file is written to.
 *                    NOTE: SQL Server writes this path itself, so it must exist
 *                    on the DATABASE SERVER (not on the machine running sqlcmd)
 *                    and be writable by the SQL Server service account.
 *   PathSeparator  - "\" on Windows hosts, "/" on Linux hosts. Required:
 *                    this script must not default it (see the note below).
 *
 * The backup is a regular (COPY_ONLY=0) FULL backup with COMPRESSION and
 * CHECKSUM, named <DbName>_YYYYMMDD_HHMMSS.bak. It is the base of the log
 * chain when the database uses the FULL recovery model: never take it with
 * COPY_ONLY=1 from this script or later log backups cannot be applied.
 *
 * Exit behaviour: run sqlcmd with -b (ABORT_ON_ERROR) and -V16 so any
 * RAISERROR or T-SQL error returns a non-zero exit code to the caller.
 */
:on error exit

-- Deliberately no :setvar defaults for BackupName or PathSeparator: a :setvar
-- in the script takes precedence over the same variable passed with -v, so a
-- "default" here silently replaces what the caller asked for (it turned a
-- named backup into a timestamped one written with the wrong separator).
-- Every variable this script uses must come from -v.

IF DB_ID(N'$(DbName)') IS NULL
BEGIN;
    RAISERROR(N'Backup refused: database ''$(DbName)'' does not exist on this server.', 16, 1);
END;

DECLARE @utcNow DATETIME2 = SYSUTCDATETIME();
DECLARE @stamp NVARCHAR(20) =
    CONVERT(NVARCHAR(8), @utcNow, 112) + N'_' + REPLACE(CONVERT(NVARCHAR(8), @utcNow, 108), N':', N'');
-- BackupName overrides the timestamped name when the operator passes one
-- (deterministic names are easier to pick up in automation); empty = default.
DECLARE @backupFile NVARCHAR(4000) =
    N'$(BackupDir)$(PathSeparator)' +
    CASE WHEN N'$(BackupName)' = N'' THEN N'$(DbName)_' + @stamp + N'.bak' ELSE N'$(BackupName)' END;

BACKUP DATABASE [$(DbName)]
    TO DISK = @backupFile
    WITH COMPRESSION, CHECKSUM,
         NAME = N'DyeHouseERP full backup $(DbName)',
         DESCRIPTION = N'Nightly full backup taken by tools/backup/backup-dyehouse.sql';

PRINT N'BACKUP_FILE=' + @backupFile;
PRINT N'BACKUP_DB=$(DbName)';
