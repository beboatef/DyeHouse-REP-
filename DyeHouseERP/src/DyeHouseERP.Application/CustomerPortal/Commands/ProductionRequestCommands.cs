using DyeHouseERP.Application.CustomerPortal.Queries;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerPortal.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerPortal.Commands;

// -------------------- Submit (customer) --------------------
public record CreateProductionRequestCommand(
    Guid CustomerId, Guid ItemId, DateTime RequestDate, string? Color,
    decimal? RequestedQuantityKg, decimal? RequestedQuantityMeter, string? Notes) : IRequest<ProductionRequestDto>;

public class CreateProductionRequestCommandValidator : AbstractValidator<CreateProductionRequestCommand>
{
    public CreateProductionRequestCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x).Must(x => x.RequestedQuantityKg is > 0 || x.RequestedQuantityMeter is > 0);
    }
}

public class CreateProductionRequestCommandHandler : IRequestHandler<CreateProductionRequestCommand, ProductionRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateProductionRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<ProductionRequestDto> Handle(CreateProductionRequestCommand request, CancellationToken cancellationToken)
    {
        var requestNumber = await _numberGenerator.NextAsync(DocumentType.ProductionRequest, cancellationToken: cancellationToken);

        var pr = new ProductionRequest(requestNumber, request.RequestDate, request.CustomerId, request.ItemId,
            _currentUser.UserName, request.Color, request.RequestedQuantityKg, request.RequestedQuantityMeter, request.Notes);

        _db.ProductionRequests.Add(pr);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetProductionRequestsQueryHandlerHelper.MapAsync(_db, pr, cancellationToken);
    }
}

// -------------------- Approve / Reject (staff) --------------------
public record ApproveProductionRequestCommand(Guid RequestId, string? StaffNotes) : IRequest<ProductionRequestDto>;

public class ApproveProductionRequestCommandHandler : IRequestHandler<ApproveProductionRequestCommand, ProductionRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public ApproveProductionRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<ProductionRequestDto> Handle(ApproveProductionRequestCommand request, CancellationToken cancellationToken)
    {
        var pr = await _db.ProductionRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("ProductionRequest", request.RequestId);

        pr.Approve(request.StaffNotes ?? string.Empty, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionRequestsQueryHandlerHelper.MapAsync(_db, pr, cancellationToken);
    }
}

public record RejectProductionRequestCommand(Guid RequestId, string Reason) : IRequest<ProductionRequestDto>;

public class RejectProductionRequestCommandValidator : AbstractValidator<RejectProductionRequestCommand>
{
    public RejectProductionRequestCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

public class RejectProductionRequestCommandHandler : IRequestHandler<RejectProductionRequestCommand, ProductionRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RejectProductionRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<ProductionRequestDto> Handle(RejectProductionRequestCommand request, CancellationToken cancellationToken)
    {
        var pr = await _db.ProductionRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("ProductionRequest", request.RequestId);

        pr.Reject(request.Reason, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionRequestsQueryHandlerHelper.MapAsync(_db, pr, cancellationToken);
    }
}

// -------------------- Convert to Production Order (staff) --------------------
public record ConvertProductionRequestCommand(Guid RequestId, DateTime OrderDate) : IRequest<ProductionRequestDto>;

public class ConvertProductionRequestCommandHandler : IRequestHandler<ConvertProductionRequestCommand, ProductionRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public ConvertProductionRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<ProductionRequestDto> Handle(ConvertProductionRequestCommand request, CancellationToken cancellationToken)
    {
        var pr = await _db.ProductionRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("ProductionRequest", request.RequestId);

        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);
        var order = new ProductionOrder(orderNumber, pr.CustomerId, pr.ItemId, request.OrderDate, _currentUser.UserName,
            pr.Color, pr.RequestedQuantityKg, pr.RequestedQuantityMeter, notes: $"From customer request {pr.RequestNumber}");

        var activeStages = await _db.ProductionStageDefinitions.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        order.BuildStageRoute(activeStages);
        _db.ProductionOrders.Add(order);

        pr.MarkConverted(order.Id, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionRequestsQueryHandlerHelper.MapAsync(_db, pr, cancellationToken);
    }
}
