using System.Data;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Numbering;

/// <summary>
/// Implements the automatic, concurrency-safe document numbering engine
/// required by spec section 6. Deliberately does NOT use
/// "SELECT MAX(number) + 1", which race-conditions under concurrent writers
/// and can silently issue duplicate document numbers.
///
/// Strategy: sp_getapplock takes an exclusive, transaction-scoped lock keyed
/// by (DocumentType, Year, WarehouseId), so two concurrent requests for the
/// same sequence are fully serialized at the database level - safe even
/// across multiple API instances behind a load balancer. Once the lock is
/// held, the counter row is read, incremented, and written back inside the
/// same transaction.
/// </summary>
public class SqlDocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly ApplicationDbContext _context;

    public SqlDocumentNumberGenerator(ApplicationDbContext context) => _context = context;

    public async Task<string> NextAsync(DocumentType documentType, Guid? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var definition = await _context.DocumentSequenceDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentType == documentType, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No DocumentSequenceDefinition is configured for '{documentType}'. Seed one via DocumentSequenceSeeder before use.");

        int? year = definition.YearlyReset ? DateTime.UtcNow.Year : null;
        Guid? scopedWarehouseId = definition.WarehouseScoped ? warehouseId : null;

        var lockResource = $"DocSeq:{documentType}:{year?.ToString() ?? "-"}:{scopedWarehouseId?.ToString() ?? "-"}";

        var connection = (SqlConnection)_context.Database.GetDbConnection();
        var ownsConnection = connection.State != ConnectionState.Open;
        if (ownsConnection) await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var lockCmd = connection.CreateCommand())
            {
                lockCmd.Transaction = (SqlTransaction)transaction;
                lockCmd.CommandText = "sp_getapplock";
                lockCmd.CommandType = CommandType.StoredProcedure;
                lockCmd.Parameters.Add(new SqlParameter("@Resource", lockResource));
                lockCmd.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
                lockCmd.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
                lockCmd.Parameters.Add(new SqlParameter("@LockTimeout", 15000));
                var returnParam = new SqlParameter("@ReturnValue", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
                lockCmd.Parameters.Add(returnParam);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);

                var lockResult = (int)returnParam.Value;
                if (lockResult < 0)
                    throw new TimeoutException($"Could not acquire numbering lock for '{lockResource}' (sp_getapplock returned {lockResult}).");
            }

            // Now that we hold the exclusive lock, read-modify-write the counter safely.
            long nextValue;
            await using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = (SqlTransaction)transaction;
                selectCmd.CommandText = @"
SELECT CurrentValue FROM DocumentSequenceCounters
WHERE DocumentType = @DocumentType
  AND (Year = @Year OR (@Year IS NULL AND Year IS NULL))
  AND (WarehouseId = @WarehouseId OR (@WarehouseId IS NULL AND WarehouseId IS NULL));";
                selectCmd.Parameters.Add(new SqlParameter("@DocumentType", documentType.ToString()));
                selectCmd.Parameters.Add(new SqlParameter("@Year", (object?)year ?? DBNull.Value));
                selectCmd.Parameters.Add(new SqlParameter("@WarehouseId", (object?)scopedWarehouseId ?? DBNull.Value));

                var existing = await selectCmd.ExecuteScalarAsync(cancellationToken);

                if (existing is null)
                {
                    nextValue = definition.StartingNumber;
                    await using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = (SqlTransaction)transaction;
                    insertCmd.CommandText = @"
INSERT INTO DocumentSequenceCounters (Id, DocumentType, Year, WarehouseId, CurrentValue)
VALUES (@Id, @DocumentType, @Year, @WarehouseId, @CurrentValue);";
                    insertCmd.Parameters.Add(new SqlParameter("@Id", Guid.NewGuid()));
                    insertCmd.Parameters.Add(new SqlParameter("@DocumentType", documentType.ToString()));
                    insertCmd.Parameters.Add(new SqlParameter("@Year", (object?)year ?? DBNull.Value));
                    insertCmd.Parameters.Add(new SqlParameter("@WarehouseId", (object?)scopedWarehouseId ?? DBNull.Value));
                    insertCmd.Parameters.Add(new SqlParameter("@CurrentValue", nextValue));
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    nextValue = (long)existing + 1;
                    await using var updateCmd = connection.CreateCommand();
                    updateCmd.Transaction = (SqlTransaction)transaction;
                    updateCmd.CommandText = @"
UPDATE DocumentSequenceCounters SET CurrentValue = @CurrentValue
WHERE DocumentType = @DocumentType
  AND (Year = @Year OR (@Year IS NULL AND Year IS NULL))
  AND (WarehouseId = @WarehouseId OR (@WarehouseId IS NULL AND WarehouseId IS NULL));";
                    updateCmd.Parameters.Add(new SqlParameter("@CurrentValue", nextValue));
                    updateCmd.Parameters.Add(new SqlParameter("@DocumentType", documentType.ToString()));
                    updateCmd.Parameters.Add(new SqlParameter("@Year", (object?)year ?? DBNull.Value));
                    updateCmd.Parameters.Add(new SqlParameter("@WarehouseId", (object?)scopedWarehouseId ?? DBNull.Value));
                    await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return definition.Format(nextValue, year);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownsConnection) await connection.CloseAsync();
        }
    }
}
