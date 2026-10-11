using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>
/// The single derivation of a Job Order's OUTSTANDING raw-material issue per
/// source lot, read straight from the append-only inventory ledger:
///
///   issued   (OUT rows, DocumentType.ProductionOrder)
///   minus returned (IN rows, DocumentType.ProductionOrderPause)
///   minus returned (IN rows posted by a cancellation, also DocumentType.ProductionOrder)
///
/// No balance column is read or written anywhere - only ledger rows.
///
/// Shared by pause (which decides how much MAY be released back) and by
/// cancellation (which decides how much MUST be returned) so the two can never
/// drift: a cancellation returns exactly what a release would, to the penny.
/// Keeping one formula here is what makes "cancellation after a partial pause"
/// reverse the NET claim instead of double-crediting the lots the pause already
/// gave back.
/// </summary>
internal static class ProductionOrderIssueLedger
{
    public static async Task<Dictionary<(Guid MessageId, Guid ItemId), (decimal Kg, decimal Meter)>>
        GetOutstandingIssuePerLotAsync(
            IApplicationDbContext db, Guid productionOrderId, CancellationToken cancellationToken)
    {
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductionOrderId == productionOrderId
                && t.RawMessageId != null
                && (t.SourceDocumentType == DocumentType.ProductionOrder
                    || t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Select(t => new { t.RawMessageId, t.ItemId, t.QuantityKg, t.QuantityMeter, t.Direction })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<(Guid, Guid), (decimal Kg, decimal Meter)>();

        foreach (var row in rows)
        {
            var key = (row.RawMessageId!.Value, row.ItemId);
            var sign = row.Direction == TransactionDirection.Out ? 1m : -1m;
            var current = result.TryGetValue(key, out var c) ? c : (Kg: 0m, Meter: 0m);
            result[key] = (current.Kg + sign * (row.QuantityKg ?? 0), current.Meter + sign * (row.QuantityMeter ?? 0));
        }

        // Never offer a negative outstanding figure (a data anomaly must not turn
        // into a negative release, nor into a negative reversal).
        foreach (var key in result.Keys.ToList())
        {
            var v = result[key];
            result[key] = (Math.Max(0m, v.Kg), Math.Max(0m, v.Meter));
        }

        return result;
    }
}
