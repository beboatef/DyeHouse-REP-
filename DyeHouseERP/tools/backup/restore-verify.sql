/*
 * DyeHouseERP restore drill + verification (sqlcmd script, no credentials here).
 *
 * Restores the latest backup into a SCRATCH database and proves the restore is
 * faithful BEFORE anyone would ever need it in anger:
 *   1. RESTORE VERIFYONLY on the backup file,
 *   2. RESTORE into the scratch database (relocated files),
 *   3. DBCC CHECKDB on the scratch database,
 *   4. reconciliation: grouped ledger sums (per warehouse/item/direction, per
 *      account, per customer/supplier) must be IDENTICAL between source and
 *      scratch; row counts for the key tables must match,
 *   5. row counts are SELECTed (the .sh wrapper captures them into the report),
 *   6. the scratch database is dropped (unless KeepScratch="1") - but only
 *      after re-checking its name.
 *
 * Usage (via restore-verify.sh, which re-checks every guard first):
 *   sqlcmd -S <server> -U <user> -P <pw> -d master -b -V16 \
 *     -v SourceDb="DyeHouseERP" BackupFile="D:\Backups\DyeHouseERP_....bak" \
 *        ScratchDb="DyeHouseERP_restoretest" DataDir="" PathSeparator="\" \
 *        KeepScratch="0" \
 *     -i restore-verify.sql -o drill-report.txt
 *
 * sqlcmd variables:
 *   SourceDb       - the live database the backup was taken from (reconciliation
 *                    baseline). Must exist.
 *   BackupFile     - server-side path of the backup file to verify.
 *   ScratchDb      - restore target. MUST end in _restoretest or the script
 *                    refuses to touch it. Must not equal SourceDb.
 *   DataDir        - directory for the relocated scratch files. Empty means
 *                    "ask the server" (InstanceDefaultDataPath); if the server
 *                    reports none, the script fails and tells you to pass one.
 *   PathSeparator  - "\" (Windows) or "/" (Linux). Default "\".
 *   KeepScratch    - "1" leaves the scratch database in place after a
 *                    successful drill (follow-up checks); anything else drops
 *                    it. Passed by the .sh wrapper (--keep-scratch).
 *
 * Exit behaviour: run sqlcmd with -b (ABORT_ON_ERROR) and -V16. Every guard
 * and every mismatch raises severity 16, so a failed drill exits non-zero
 * and - crucially - a failed RESTORE or a failed CHECKDB aborts the script
 * BEFORE the drop step, leaving the scratch database in place for forensics.
 */
:on error exit

:setvar PathSeparator "\"

-- ---------------------------------------------------------------- guards ---
IF RIGHT(N'$(ScratchDb)', 12) <> N'_restoretest'
BEGIN;
    RAISERROR(N'Restore drill refused: scratch database ''$(ScratchDb)'' does not end in _restoretest. This script never touches any other database name.', 16, 1);
END;

IF N'$(ScratchDb)' = N'$(SourceDb)'
BEGIN;
    RAISERROR(N'Restore drill refused: scratch database must differ from the source database.', 16, 1);
END;

IF DB_ID(N'$(SourceDb)') IS NULL
BEGIN;
    RAISERROR(N'Restore drill refused: source database ''$(SourceDb)'' does not exist on this server.', 16, 1);
END;

-- ------------------------------------------------------- 1. VERIFYONLY ---
PRINT N'== [1/6] RESTORE VERIFYONLY ' + N'$(BackupFile)';
RESTORE VERIFYONLY FROM DISK = N'$(BackupFile)';
PRINT N'VERIFYONLY passed.';

-- ------------------------------------------------- 2. RESTORE (relocated) -
PRINT N'== [2/6] RESTORE DATABASE ' + N'$(ScratchDb)';

DECLARE @dataDir NVARCHAR(260) = N'$(DataDir)';
IF @dataDir = N''
    SET @dataDir = CONVERT(NVARCHAR(260), SERVERPROPERTY('InstanceDefaultDataPath'));
IF @dataDir IS NULL OR @dataDir = N''
BEGIN;
    RAISERROR(N'Restore drill refused: no data directory known. Pass -v DataDir="<server-side directory>".', 16, 1);
END;
IF RIGHT(@dataDir, 1) NOT IN (N'\', N'/')
    SET @dataDir = @dataDir + N'$(PathSeparator)';

CREATE TABLE #filelist
(
    LogicalName NVARCHAR(128),
    PhysicalName NVARCHAR(260),
    [Type] CHAR(1),
    FileGroupName NVARCHAR(128),
    Size NUMERIC(20, 0),
    MaxSize NUMERIC(20, 0),
    FileId BIGINT,
    CreateLSN NUMERIC(25, 0),
    DropLSN NUMERIC(25, 0),
    UniqueId UNIQUEIDENTIFIER,
    ReadOnlyLSN NUMERIC(25, 0),
    ReadWriteLSN NUMERIC(25, 0),
    BackupSizeInBytes BIGINT,
    SourceBlockSize INT,
    FileGroupId INT,
    LogGroupGUID UNIQUEIDENTIFIER,
    DifferentialBaseLSN NUMERIC(25, 0),
    DifferentialBaseGUID UNIQUEIDENTIFIER,
    IsReadOnly BIT,
    IsPresent BIT,
    TDEThumbprint VARBINARY(32),
    SnapshotUrl NVARCHAR(360)
);

DECLARE @filelistSql NVARCHAR(MAX) =
    N'RESTORE FILELISTONLY FROM DISK = N''' + REPLACE(N'$(BackupFile)', N'''', N'''''''') + N''';';
INSERT INTO #filelist EXEC sp_executesql @filelistSql;

IF EXISTS (SELECT 1 FROM #filelist WHERE [Type] NOT IN (N'D', N'L'))
BEGIN;
    RAISERROR(N'Restore drill refused: backup contains a file type this script cannot relocate (expected data + log only).', 16, 1);
END;

DECLARE @moves NVARCHAR(MAX) =
(
    SELECT STRING_AGG(
        N'MOVE N''' + REPLACE(LogicalName, N'''', N'''''''') + N''' TO N''' +
        @dataDir + REPLACE(LogicalName, N'''', N'''''''') + N'_restoretest' +
        CASE WHEN [Type] = N'D' THEN N'.mdf' ELSE N'.ldf' END + N'''', N', ')
    FROM #filelist
);

DECLARE @restoreSql NVARCHAR(MAX) =
    N'RESTORE DATABASE ' + QUOTENAME(N'$(ScratchDb)') +
    N' FROM DISK = N''' + REPLACE(N'$(BackupFile)', N'''', N'''''''') + N''' WITH REPLACE, ' + @moves + N';';
EXEC sp_executesql @restoreSql;
PRINT N'RESTORE completed.';

-- ------------------------------------------------------------ 3. CHECKDB --
PRINT N'== [3/6] DBCC CHECKDB ' + N'$(ScratchDb)';
DECLARE @checkSql NVARCHAR(MAX) =
    N'DBCC CHECKDB (' + QUOTENAME(N'$(ScratchDb)', '''') + N') WITH NO_INFOMSGS;';
EXEC sp_executesql @checkSql;
PRINT N'CHECKDB passed with no errors.';

-- ----------------------------------------------------- 4. reconcile sums --
PRINT N'== [4/6] reconciling ledger sums source vs scratch';

DECLARE @src NVARCHAR(258) = QUOTENAME(N'$(SourceDb)');
DECLARE @scr NVARCHAR(258) = QUOTENAME(N'$(ScratchDb)');

-- 4a. Fabric ledger: per warehouse / item / direction.
DECLARE @reconcileSql NVARCHAR(MAX) = N'
SELECT WarehouseId, ItemId, Direction,
       SUM(QuantityKg) AS Kg, SUM(QuantityMeter) AS Meter, COUNT_BIG(*) AS Rows
INTO #a FROM ' + @src + N'.dbo.InventoryTransactions GROUP BY WarehouseId, ItemId, Direction;
SELECT WarehouseId, ItemId, Direction,
       SUM(QuantityKg) AS Kg, SUM(QuantityMeter) AS Meter, COUNT_BIG(*) AS Rows
INTO #b FROM ' + @scr + N'.dbo.InventoryTransactions GROUP BY WarehouseId, ItemId, Direction;
IF EXISTS ((SELECT WarehouseId, ItemId, Direction, Kg, Meter, Rows FROM #a
            EXCEPT
            SELECT WarehouseId, ItemId, Direction, Kg, Meter, Rows FROM #b)
           UNION ALL
           (SELECT WarehouseId, ItemId, Direction, Kg, Meter, Rows FROM #b
            EXCEPT
            SELECT WarehouseId, ItemId, Direction, Kg, Meter, Rows FROM #a))
    RAISERROR(N''Restore verification FAILED: InventoryTransactions sums differ between source and scratch.'', 16, 1);
DROP TABLE #a; DROP TABLE #b;';
EXEC sp_executesql @reconcileSql;
PRINT N'InventoryTransactions: sums match.';

-- 4b. Chemicals ledger: per warehouse / material / direction.
SET @reconcileSql = N'
SELECT WarehouseId, MaterialId, Direction,
       SUM(Quantity) AS Qty, COUNT_BIG(*) AS Rows
INTO #a FROM ' + @src + N'.dbo.MaterialTransactions GROUP BY WarehouseId, MaterialId, Direction;
SELECT WarehouseId, MaterialId, Direction,
       SUM(Quantity) AS Qty, COUNT_BIG(*) AS Rows
INTO #b FROM ' + @scr + N'.dbo.MaterialTransactions GROUP BY WarehouseId, MaterialId, Direction;
IF EXISTS ((SELECT WarehouseId, MaterialId, Direction, Qty, Rows FROM #a
            EXCEPT
            SELECT WarehouseId, MaterialId, Direction, Qty, Rows FROM #b)
           UNION ALL
           (SELECT WarehouseId, MaterialId, Direction, Qty, Rows FROM #b
            EXCEPT
            SELECT WarehouseId, MaterialId, Direction, Qty, Rows FROM #a))
    RAISERROR(N''Restore verification FAILED: MaterialTransactions sums differ between source and scratch.'', 16, 1);
DROP TABLE #a; DROP TABLE #b;';
EXEC sp_executesql @reconcileSql;
PRINT N'MaterialTransactions: sums match.';

-- 4c. Cash ledger: per account / direction.
SET @reconcileSql = N'
SELECT TreasuryAccountId, Direction, SUM(Amount) AS Amount, COUNT_BIG(*) AS Rows
INTO #a FROM ' + @src + N'.dbo.TreasuryTransactions GROUP BY TreasuryAccountId, Direction;
SELECT TreasuryAccountId, Direction, SUM(Amount) AS Amount, COUNT_BIG(*) AS Rows
INTO #b FROM ' + @scr + N'.dbo.TreasuryTransactions GROUP BY TreasuryAccountId, Direction;
IF EXISTS ((SELECT TreasuryAccountId, Direction, Amount, Rows FROM #a
            EXCEPT
            SELECT TreasuryAccountId, Direction, Amount, Rows FROM #b)
           UNION ALL
           (SELECT TreasuryAccountId, Direction, Amount, Rows FROM #b
            EXCEPT
            SELECT TreasuryAccountId, Direction, Amount, Rows FROM #a))
    RAISERROR(N''Restore verification FAILED: TreasuryTransactions sums differ between source and scratch.'', 16, 1);
DROP TABLE #a; DROP TABLE #b;';
EXEC sp_executesql @reconcileSql;
PRINT N'TreasuryTransactions: sums match.';

-- 4d. Customer ledger: per customer.
SET @reconcileSql = N'
SELECT CustomerId, SUM(Debit) AS Debit, SUM(Credit) AS Credit, COUNT_BIG(*) AS Rows
INTO #a FROM ' + @src + N'.dbo.CustomerLedgerEntries GROUP BY CustomerId;
SELECT CustomerId, SUM(Debit) AS Debit, SUM(Credit) AS Credit, COUNT_BIG(*) AS Rows
INTO #b FROM ' + @scr + N'.dbo.CustomerLedgerEntries GROUP BY CustomerId;
IF EXISTS ((SELECT CustomerId, Debit, Credit, Rows FROM #a
            EXCEPT
            SELECT CustomerId, Debit, Credit, Rows FROM #b)
           UNION ALL
           (SELECT CustomerId, Debit, Credit, Rows FROM #b
            EXCEPT
            SELECT CustomerId, Debit, Credit, Rows FROM #a))
    RAISERROR(N''Restore verification FAILED: CustomerLedgerEntries sums differ between source and scratch.'', 16, 1);
DROP TABLE #a; DROP TABLE #b;';
EXEC sp_executesql @reconcileSql;
PRINT N'CustomerLedgerEntries: sums match.';

-- 4e. Supplier ledger: per supplier.
SET @reconcileSql = N'
SELECT SupplierId, SUM(Debit) AS Debit, SUM(Credit) AS Credit, COUNT_BIG(*) AS Rows
INTO #a FROM ' + @src + N'.dbo.SupplierLedgerEntries GROUP BY SupplierId;
SELECT SupplierId, SUM(Debit) AS Debit, SUM(Credit) AS Credit, COUNT_BIG(*) AS Rows
INTO #b FROM ' + @scr + N'.dbo.SupplierLedgerEntries GROUP BY SupplierId;
IF EXISTS ((SELECT SupplierId, Debit, Credit, Rows FROM #a
            EXCEPT
            SELECT SupplierId, Debit, Credit, Rows FROM #b)
           UNION ALL
           (SELECT SupplierId, Debit, Credit, Rows FROM #b
            EXCEPT
            SELECT SupplierId, Debit, Credit, Rows FROM #a))
    RAISERROR(N''Restore verification FAILED: SupplierLedgerEntries sums differ between source and scratch.'', 16, 1);
DROP TABLE #a; DROP TABLE #b;';
EXEC sp_executesql @reconcileSql;
PRINT N'SupplierLedgerEntries: sums match.';

-- ------------------------------------------------- 5. row counts (report) --
PRINT N'== [5/6] key-table row counts (source vs scratch)';

CREATE TABLE #counts (TableName NVARCHAR(64), SourceRows BIGINT, ScratchRows BIGINT);

DECLARE @countSql NVARCHAR(MAX);
DECLARE @tables TABLE (Name NVARCHAR(64));
INSERT INTO @tables (Name) VALUES
    (N'RawMessages'), (N'ProductionOrders'), (N'Deliveries'),
    (N'Invoices'), (N'TreasuryTransactions'), (N'InventoryTransactions');

DECLARE @t NVARCHAR(64);
DECLARE tableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @tables;
OPEN tableCursor;
FETCH NEXT FROM tableCursor INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN;
    SET @countSql = N'INSERT INTO #counts (TableName, SourceRows, ScratchRows) SELECT N''' + @t + N''', ' +
        N'(SELECT COUNT_BIG(*) FROM ' + @src + N'.dbo.' + QUOTENAME(@t) + N'), ' +
        N'(SELECT COUNT_BIG(*) FROM ' + @scr + N'.dbo.' + QUOTENAME(@t) + N');';
    EXEC sp_executesql @countSql;
    FETCH NEXT FROM tableCursor INTO @t;
END;
CLOSE tableCursor;
DEALLOCATE tableCursor;

IF EXISTS (SELECT 1 FROM #counts WHERE SourceRows <> ScratchRows)
BEGIN;
    SELECT TableName, SourceRows, ScratchRows FROM #counts;
    RAISERROR(N'Restore verification FAILED: row counts differ between source and scratch (see report).', 16, 1);
END;

SELECT TableName, SourceRows AS [Rows] FROM #counts ORDER BY TableName;
DROP TABLE #counts;

-- ------------------------------------------------------- 6. drop scratch --
-- The CI drill passes KeepScratch=1 so a follow-up step can recompute balances
-- through the application against the restored copy; interactive drills keep
-- the historical behaviour and drop it.
IF N'$(KeepScratch)' = N'1'
BEGIN;
    PRINT N'== [6/6] keeping scratch database ' + N'$(ScratchDb)' + N' (--keep-scratch)';
END;
ELSE
BEGIN;
    PRINT N'== [6/6] dropping scratch database ' + N'$(ScratchDb)';
    IF RIGHT(N'$(ScratchDb)', 12) <> N'_restoretest'
    BEGIN;
        RAISERROR(N'Refusing to drop: name no longer ends in _restoretest. Dropping nothing; clean up manually.', 16, 1);
    END;
    DECLARE @dropSql NVARCHAR(MAX) = N'DROP DATABASE ' + QUOTENAME(N'$(ScratchDb)') + N';';
    EXEC sp_executesql @dropSql;
END;

IF N'$(KeepScratch)' = N'1'
    PRINT N'RESTORE DRILL PASSED: backup file verified, scratch CHECKDB clean, ledger sums and row counts identical, scratch retained.';
ELSE
    PRINT N'RESTORE DRILL PASSED: backup file verified, scratch CHECKDB clean, ledger sums and row counts identical, scratch dropped.';
