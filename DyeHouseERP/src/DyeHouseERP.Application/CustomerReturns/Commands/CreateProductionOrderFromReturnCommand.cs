using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerReturns.Commands;

/// <summary>
/// Raises a NEW, INDEPENDENT Job Order from a customer return (spec section 33).
///
/// What this deliberately is NOT: it is not a conversion of the return. The return stays exactly as
/// it was recorded - no invoice is touched, no credit note is created, no charge is reversed, and no
/// approval is implied. The order is a job of its own that merely references the return it came from.
///
/// What it deliberately IS: the SAME canonical workflow every other order comes from (spec section 5)
/// - issuing raw material creates the Job Order, posts the OUT ledger row and writes the matching
/// allocation in one transaction - so a return-based job is real production with real stock
/// transactions, not a parallel bookkeeping path. This command only resolves the return's customer and
/// forwards to that workflow with the return reference attached.
///
/// The one decision the caller MUST make is who carries the cost: <see cref="CostBearer.Customer"/>
/// (على حساب العميل) or <see cref="CostBearer.Company"/> (على حساب الشركة). There is no default. The
/// same physical rework can legitimately be billed to the customer or absorbed by the factory as a
/// quality claim, and guessing would move money silently.
/// </summary>
public record CreateProductionOrderFromReturnCommand(
    Guid CustomerReturnId,
    Guid ItemId,
    Guid RawMessageId,
    DateTime OrderDate,
    decimal? ActualQuantityKg,
    decimal? ActualQuantityMeter,
    /// <summary>Mandatory: no default assumption is made about who pays for the rework.</summary>
    CostBearer? CostBearer,
    string? Color = null,
    string? Notes = null,
    // Client idempotency token, forwarded to the issue workflow: a retried confirmation with the same
    // token returns the SAME order instead of raising a second job and a second stock issue.
    Guid? RequestId = null) : IRequest<ProductionOrderDto>;

public class CreateProductionOrderFromReturnCommandValidator : AbstractValidator<CreateProductionOrderFromReturnCommand>
{
    public CreateProductionOrderFromReturnCommandValidator()
    {
        RuleFor(x => x.CustomerReturnId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty().WithMessage("Choose the item being reprocessed from the return.");
        RuleFor(x => x.RawMessageId).NotEmpty()
            .WithMessage("Choose the raw material lot the returned material is issued from.");
        RuleFor(x => x).Must(x => x.ActualQuantityKg is > 0 || x.ActualQuantityMeter is > 0)
            .WithMessage("Enter the actual issued quantity in KG and/or Meter - it becomes the new Job Order's opening quantity.");

        RuleFor(x => x.CostBearer)
            .NotNull()
            .WithMessage("Choose who carries the cost: on the customer's account (على حساب العميل) or on the company's account (على حساب الشركة). There is no default.")
            .DependentRules(() => RuleFor(x => x.CostBearer!.Value).IsInEnum());
    }
}

public class CreateProductionOrderFromReturnCommandHandler
    : IRequestHandler<CreateProductionOrderFromReturnCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISender _mediator;

    public CreateProductionOrderFromReturnCommandHandler(IApplicationDbContext db, ISender mediator)
    {
        _db = db; _mediator = mediator;
    }

    public async Task<ProductionOrderDto> Handle(
        CreateProductionOrderFromReturnCommand request, CancellationToken cancellationToken)
    {
        var customerReturn = await _db.CustomerReturns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.CustomerReturnId, cancellationToken)
            ?? throw new NotFoundException("CustomerReturn", request.CustomerReturnId);

        // A cancelled return restored nothing (its stock effect was reversed), so it cannot justify a
        // new job order.
        if (customerReturn.IsCancelled)
            throw new DomainException(
                $"Customer return {customerReturn.ReturnNumber} is cancelled, so no Job Order can be raised from it.");

        // The order belongs to the return's own customer: the material being issued is theirs, and the
        // issue workflow would refuse a lot that belongs to anyone else anyway.
        var issue = new IssueRawMaterialCommand(
            customerReturn.CustomerId, request.ItemId, request.RawMessageId, request.OrderDate,
            request.ActualQuantityKg, request.ActualQuantityMeter,
            Color: request.Color,
            Notes: request.Notes,
            RequestId: request.RequestId,
            CustomerReturnId: customerReturn.Id,
            CostBearer: request.CostBearer!.Value);

        return await _mediator.Send(issue, cancellationToken);
    }
}
