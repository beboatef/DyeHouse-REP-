using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Commands;

/// <summary>
/// Creates a brand-new Production Order to reprocess a Separate (spec
/// section 23). The original order's history is NEVER edited or
/// overwritten - genealogy is preserved purely through
/// ProductionOrder.ReprocessingOfProductionOrderId pointing back to it.
///
/// <paramref name="StartingStageId"/> is optional and must name an EXISTING, ACTIVE stage:
/// reprocessed material is already past formation (it came out of a later operation), so the
/// job may legitimately start where the rework actually begins. Empty means the ordinary
/// route start (التشكيل). Either way the order starts at exactly ONE stage and the rest of the
/// route stays dynamic, and no inventory movement is posted by this command - the raw issue
/// remains the single deduction.
/// </summary>
public record ReprocessSeparateCommand(
    Guid SeparateId, DateTime OrderDate, string? Notes, Guid? StartingStageId = null) : IRequest<ProductionOrderDto>;

public class ReprocessSeparateCommandValidator : AbstractValidator<ReprocessSeparateCommand>
{
    public ReprocessSeparateCommandValidator()
    {
        RuleFor(x => x.SeparateId).NotEmpty();
    }
}

public class ReprocessSeparateCommandHandler : IRequestHandler<ReprocessSeparateCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public ReprocessSeparateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<ProductionOrderDto> Handle(ReprocessSeparateCommand request, CancellationToken cancellationToken)
    {
        var separate = await _db.Separates.FirstOrDefaultAsync(s => s.Id == request.SeparateId, cancellationToken)
            ?? throw new NotFoundException("Separate", request.SeparateId);

        // Spec sections 5-6: the reprocessing order's requested quantity may only use the
        // item's configured units, and both when the item requires both.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, separate.ItemId, separate.QuantityKg, separate.QuantityMeter,
            cancellationToken, "on this reprocessing order");

        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);

        var newOrder = new ProductionOrder(
            orderNumber, separate.CustomerId, separate.ItemId, request.OrderDate, _currentUser.UserName,
            requestedQuantityKg: separate.QuantityKg, requestedQuantityMeter: separate.QuantityMeter,
            notes: request.Notes, reprocessingOfProductionOrderId: separate.OriginalProductionOrderId);

        // Only ACTIVE stages are offered, so a stage that does not exist - or that has been
        // deactivated - can never become the starting stage (the domain lookup refuses it).
        var activeStages = await _db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        newOrder.BuildStageRoute(activeStages, _currentUser.UserName, request.StartingStageId);

        _db.ProductionOrders.Add(newOrder);

        separate.StartReprocessing(newOrder.Id);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, newOrder.Id, cancellationToken);
    }
}
