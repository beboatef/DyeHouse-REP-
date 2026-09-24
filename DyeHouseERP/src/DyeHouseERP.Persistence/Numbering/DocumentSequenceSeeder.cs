using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Numbering;

/// <summary>
/// Seeds the default numbering rules for every document type (spec section
/// 6). Safe to call on every startup - only inserts definitions that don't
/// already exist, so an admin's later customization (different prefix,
/// padding, etc.) via the settings screen is never overwritten.
/// </summary>
public static class DocumentSequenceSeeder
{
    private static readonly (DocumentType Type, string Prefix, int Padding, bool Yearly, bool WarehouseScoped)[] Defaults =
    {
        (DocumentType.RawReceiptMessage,        "MSG", 6, true,  false),
        (DocumentType.ProductionOrder,          "PRD", 6, true,  false),
        (DocumentType.RawIssueExternalRelease,  "REL", 6, true,  false),
        (DocumentType.RawReturn,                "RET", 6, true,  false),
        (DocumentType.CustomerTransfer,         "TRF", 6, true,  false),
        (DocumentType.StockAdjustment,          "ADJ", 6, true,  false),
        (DocumentType.Delivery,                 "DEL", 6, true,  false),
        (DocumentType.Invoice,                  "INV", 6, true,  false),
        (DocumentType.Receipt,                  "RCP", 6, true,  false),
        (DocumentType.Payment,                  "PAY", 6, true,  false),
        (DocumentType.TreasuryTransfer,         "TTR", 6, true,  false),
        (DocumentType.MaterialTransfer,         "MTR", 6, true,  false),
        (DocumentType.MaterialIssue,            "MIS", 6, true,  false),
        (DocumentType.PreparationDilution,      "PRP", 6, true,  false),
        (DocumentType.ReadyGoodsTransfer,       "RGT", 6, true,  false),
        (DocumentType.ProductionRequest,        "REQ", 6, true,  false),
    };

    public static async Task SeedAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        var existing = await context.DocumentSequenceDefinitions
            .Select(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        foreach (var d in Defaults)
        {
            if (existing.Contains(d.Type)) continue;
            context.DocumentSequenceDefinitions.Add(
                new DocumentSequenceDefinition(d.Type, d.Prefix, d.Padding, d.Yearly, d.WarehouseScoped));
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
