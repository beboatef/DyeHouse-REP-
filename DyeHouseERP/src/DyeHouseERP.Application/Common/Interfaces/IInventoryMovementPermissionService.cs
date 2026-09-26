namespace DyeHouseERP.Application.Common.Interfaces;

public interface IInventoryMovementPermissionService
{
    void EnsureCanEdit(Guid productionOrderId);
    void EnsureCanDelete(Guid productionOrderId);
}
