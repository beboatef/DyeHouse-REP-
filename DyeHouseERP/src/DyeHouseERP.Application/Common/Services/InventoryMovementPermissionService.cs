using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Application.Common.Services;

public sealed class InventoryMovementPermissionService : IInventoryMovementPermissionService
{
    private readonly ICurrentUserService _currentUser;
    private readonly IApplicationDbContext _context;

    public InventoryMovementPermissionService(
        ICurrentUserService currentUser,
        IApplicationDbContext context)
    {
        _currentUser = currentUser;
        _context = context;
    }

    public void EnsureCanEdit(Guid productionOrderId)
    {
        if (!_currentUser.HasPermission(Permissions.InventoryEdit))
            throw new DomainException("You do not have permission to edit inventory movements.");

        EnsureNotLocked(productionOrderId);
    }

    public void EnsureCanDelete(Guid productionOrderId)
    {
        if (!_currentUser.HasPermission(Permissions.InventoryDelete))
            throw new DomainException("You do not have permission to delete inventory movements.");

        EnsureNotLocked(productionOrderId);
    }

    private void EnsureNotLocked(Guid productionOrderId)
    {
        var deliveryLocked = _context.DeliveryLines
            .Where(x => x.ProductionOrderId == productionOrderId)
            .Join(
                _context.Deliveries,
                line => line.DeliveryId,
                delivery => delivery.Id,
                (line, delivery) => delivery.Status)
            .Any(x => x == DeliveryStatus.Delivered);

        if (deliveryLocked)
            throw new DocumentLockedException(
                "Inventory movement is locked because the related delivery has been delivered.",
                productionOrderId.ToString());

        var invoiceLocked = _context.InvoiceLines
            .Where(x => x.ProductionOrderId == productionOrderId)
            .Join(
                _context.Invoices,
                line => line.InvoiceId,
                invoice => invoice.Id,
                (line, invoice) => invoice.Status)
            .Any(x => x != InvoiceStatus.Draft);

        if (invoiceLocked)
            throw new DocumentLockedException(
                "Inventory movement is locked because the related invoice has been issued.",
                productionOrderId.ToString());
    }
}
