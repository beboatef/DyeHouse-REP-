using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// Warehouse master data is dynamic (spec section 15): an administrator adds, renames,
/// re-classifies, activates and deactivates warehouses from inside the ERP.
///
/// Deactivation is a FLAG, never a delete. The row, its ledger rows, its balances and the
/// documents that already refer to it all stay exactly as they were, and old movements stay
/// readable and traceable. What deactivation stops is the warehouse taking part in anything
/// NEW - which is why the rule is expressed as two SEPARATE checks rather than one:
///
///   * <see cref="EnsureUsableForNewDocumentAsync"/> - the warehouse a caller picks while
///     CREATING a document must exist and be active.
///
///   * <see cref="EnsureUsableForNewMovementAsync"/> - any operation that is about to WRITE A
///     NEW STOCK MOVEMENT (an IN row, an OUT row, or either leg of a transfer) must target an
///     existing, active warehouse. This covers far more than "create": posting an existing
///     draft, appending a line or a quantity to a persisted document, resuming a paused order,
///     editing a delivered document or reversing a posted one all produce new ledger rows, so
///     they all go through this check - a document that is merely old is never a way around
///     the rule.
///
/// A path that only READS a document, or that changes its status without writing any stock
/// row, deliberately calls neither method: retiring a warehouse must not block reading
/// history, and must not block closing a document that has no stock effect (a draft that was
/// never posted, for example). Handlers are not given a blanket exemption - each one was
/// judged by what it actually writes.
///
/// A missing warehouse is a not-found (HTTP 404) and a deactivated one is a business refusal
/// (HTTP 400), matching how the rest of the application layer reports them.
/// </summary>
public static class WarehouseGuard
{
    /// <summary>
    /// A NEW document may only reference a warehouse that exists and is still active.
    /// Returns the entity so a caller that needs the code, name or kind does not have to
    /// query it a second time.
    /// </summary>
    public static Task<Warehouse> EnsureUsableForNewDocumentAsync(
        IApplicationDbContext db, Guid warehouseId, CancellationToken cancellationToken = default)
        => LoadAsync(db, warehouseId, cancellationToken,
            "on a new document");

    /// <summary>
    /// Any operation about to write a NEW stock movement into (or out of, or between) this
    /// warehouse may only do so while it exists and is still active. Deactivated warehouses
    /// keep their history, but they never receive a new ledger row - not from a new document,
    /// not from posting or extending an old one. Use <paramref name="operation"/> to name the
    /// specific step in the refusal message.
    /// </summary>
    public static Task<Warehouse> EnsureUsableForNewMovementAsync(
        IApplicationDbContext db, Guid warehouseId, CancellationToken cancellationToken = default,
        string operation = "to post a new stock movement")
        => LoadAsync(db, warehouseId, cancellationToken, operation);


    /// <summary>Ensures factory-owned materials are kept only in their designated warehouse.</summary>
    public static void EnsureMaterialWarehouseCompatibility(MaterialKind materialKind, Warehouse warehouse)
    {
        var expectedKind = materialKind switch
        {
            MaterialKind.Chemical => WarehouseKind.Materials,
            MaterialKind.OperatingSupply => WarehouseKind.OperatingSupplies,
            _ => throw new DomainException($"Unknown material kind '{materialKind}' cannot be moved through inventory.")
        };

        if (warehouse.Kind != expectedKind)
            throw new DomainException(
                $"Material kind '{materialKind}' cannot be moved through warehouse '{warehouse.Name}' ({warehouse.Code}) of kind '{warehouse.Kind}'. Expected warehouse kind '{expectedKind}'.");
    }

    /// <summary>The WIP warehouse is not a user-manageable fifth warehouse in this ERP.</summary>
    public static void EnsureAllowedWarehouseKind(WarehouseKind kind)
    {
        if (!Enum.IsDefined(typeof(WarehouseKind), kind) || kind == WarehouseKind.ProductionWip)
            throw new DomainException("Only RawMaterial, ReadyGoods, Materials, and OperatingSupplies warehouses can be created or selected.");
    }

    private static async Task<Warehouse> LoadAsync(
        IApplicationDbContext db, Guid warehouseId, CancellationToken cancellationToken, string operation)
    {
        var warehouse = await db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", warehouseId);

        if (!warehouse.IsActive)
            throw new DomainException(
                $"Warehouse '{warehouse.Name}' ({warehouse.Code}) is deactivated and cannot be used {operation}. " +
                "Its existing movements and documents remain readable and unchanged; reactivate the warehouse to " +
                "use it again.");

        return warehouse;
    }
}
