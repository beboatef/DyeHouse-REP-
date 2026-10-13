using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>
/// Edits an open Job Order's descriptive and scheduling fields (spec sections 12 + 16): colour, raw
/// origin, customer reference, notes, priority, order date and the closed-line / open-line type.
///
/// Deliberately NOT part of this command:
///   * CustomerId / ItemId     - the order's identity, already referenced by allocations, stage
///                               executions, ready-goods transfers, deliveries and invoices;
///   * RequestedQuantityKg/Mtr - the formation stage's immutable baseline and the denominator of
///                               the H1 100%-allocation rule.
/// A wrong customer, item or quantity is corrected by CANCELLING the order (CancelProductionOrderCommand,
/// which reverses the raw material it still holds) and creating a new one - so the correction is an
/// explicit, audited workflow rather than a silent rewrite of the order's identity or baseline.
/// </summary>
public record UpdateProductionOrderCommand(
    Guid Id,
    DateTime OrderDate,
    string? Color,
    string? RawOrigin,
    string? CustomerReference,
    string? Notes,
    ProductionPriority Priority,
    JobOrderType JobOrderType) : IRequest<ProductionOrderDto>;

public class UpdateProductionOrderCommandValidator : AbstractValidator<UpdateProductionOrderCommand>
{
    public UpdateProductionOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.JobOrderType).IsInEnum();
        // The persisted column widths - refused with a clear message rather than a database truncation error.
        RuleFor(x => x.Color).MaximumLength(100);
        RuleFor(x => x.RawOrigin).MaximumLength(500);
        RuleFor(x => x.CustomerReference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdateProductionOrderCommandHandler : IRequestHandler<UpdateProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateProductionOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionOrderDto> Handle(UpdateProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.Id);

        order.UpdateDetails(
            request.Color, request.RawOrigin, request.CustomerReference, request.Notes,
            request.Priority, request.OrderDate, _currentUser.UserName);

        // The line type keeps its own existing rule (refused once the order is actually in production),
        // so it is routed through the domain method that already owns that invariant rather than
        // re-implemented here. It is only invoked when the caller actually CHANGES it: re-sending the
        // current value (as any edit form does) must never fail an unrelated notes/colour edit on an
        // order that is already in production.
        if (order.JobOrderType != request.JobOrderType)
            order.SetJobOrderType(request.JobOrderType, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}
