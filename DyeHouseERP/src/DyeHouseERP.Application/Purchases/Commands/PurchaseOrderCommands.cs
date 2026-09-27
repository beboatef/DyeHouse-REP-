using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Application.Purchases.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Purchases.Commands;

public class PurchaseOrderLineInput
{
    public Guid MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Creates a purchase order (spec section 35). The order starts as a draft; it has
/// no stock or accounting effect until it is approved and received.
/// </summary>
public record CreatePurchaseOrderCommand(
    DateTime OrderDate,
    Guid SupplierId,
    Guid WarehouseId,
    DateTime? ExpectedDeliveryDate = null,
    string? Notes = null,
    List<PurchaseOrderLineInput>? Lines = null) : IRequest<PurchaseOrderDto>;

public class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreatePurchaseOrderCommandHandler : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly ISender _mediator;

    public CreatePurchaseOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, ISender mediator)
    { _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        await PurchaseRules.EnsureSupplierExists(_db, request.SupplierId, cancellationToken);
        await PurchaseRules.EnsureWarehouseExists(_db, request.WarehouseId, cancellationToken);

        var orderNumber = await _numberGenerator.NextAsync(DocumentType.PurchaseOrder, cancellationToken: cancellationToken);

        var order = new PurchaseOrder(orderNumber, request.OrderDate, request.SupplierId, request.WarehouseId,
            _currentUser.UserName, request.ExpectedDeliveryDate, request.Notes);

        foreach (var line in request.Lines ?? new List<PurchaseOrderLineInput>())
        {
            await PurchaseRules.EnsureMaterialExists(_db, line.MaterialId, cancellationToken);
            order.AddLine(line.MaterialId, line.Quantity, line.Unit, line.UnitPrice, line.Notes, _currentUser.UserName);
        }

        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record UpdatePurchaseOrderCommand(
    Guid Id, DateTime OrderDate, Guid WarehouseId,
    DateTime? ExpectedDeliveryDate = null, string? Notes = null) : IRequest<PurchaseOrderDto>;

public class UpdatePurchaseOrderCommandHandler : IRequestHandler<UpdatePurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public UpdatePurchaseOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(UpdatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.Id);

        await PurchaseRules.EnsureWarehouseExists(_db, request.WarehouseId, cancellationToken);
        order.UpdateHeader(request.OrderDate, request.WarehouseId, request.ExpectedDeliveryDate, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record AddPurchaseOrderLineCommand(
    Guid OrderId, Guid MaterialId, decimal Quantity, MaterialUnit Unit, decimal UnitPrice, string? Notes = null)
    : IRequest<PurchaseOrderDto>;

public class AddPurchaseOrderLineCommandHandler : IRequestHandler<AddPurchaseOrderLineCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public AddPurchaseOrderLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(AddPurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.OrderId);

        await PurchaseRules.EnsureMaterialExists(_db, request.MaterialId, cancellationToken);
        order.AddLine(request.MaterialId, request.Quantity, request.Unit, request.UnitPrice, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record UpdatePurchaseOrderLineCommand(
    Guid OrderId, Guid LineId, decimal Quantity, MaterialUnit Unit, decimal UnitPrice, string? Notes = null)
    : IRequest<PurchaseOrderDto>;

public class UpdatePurchaseOrderLineCommandHandler : IRequestHandler<UpdatePurchaseOrderLineCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public UpdatePurchaseOrderLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(UpdatePurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.OrderId);

        order.UpdateLine(request.LineId, request.Quantity, request.Unit, request.UnitPrice, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record RemovePurchaseOrderLineCommand(Guid OrderId, Guid LineId) : IRequest<PurchaseOrderDto>;

public class RemovePurchaseOrderLineCommandHandler : IRequestHandler<RemovePurchaseOrderLineCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public RemovePurchaseOrderLineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(RemovePurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.OrderId);

        order.RemoveLine(request.LineId, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

// ------------------------------------------------------------- workflow

public record SubmitPurchaseOrderCommand(Guid Id) : IRequest<PurchaseOrderDto>;

public class SubmitPurchaseOrderCommandHandler : IRequestHandler<SubmitPurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public SubmitPurchaseOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(SubmitPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.Id);

        order.Submit(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record ApprovePurchaseOrderCommand(Guid Id) : IRequest<PurchaseOrderDto>;

public class ApprovePurchaseOrderCommandHandler : IRequestHandler<ApprovePurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public ApprovePurchaseOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(ApprovePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.Id);

        order.Approve(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

public record CancelPurchaseOrderCommand(Guid Id, string Reason) : IRequest<PurchaseOrderDto>;

public class CancelPurchaseOrderCommandValidator : AbstractValidator<CancelPurchaseOrderCommand>
{
    public CancelPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CancelPurchaseOrderCommandHandler : IRequestHandler<CancelPurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _mediator;

    public CancelPurchaseOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, ISender mediator)
    { _db = db; _currentUser = currentUser; _mediator = mediator; }

    public async Task<PurchaseOrderDto> Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("PurchaseOrder", request.Id);

        order.Cancel(_currentUser.UserName, request.Reason);

        await _db.SaveChangesAsync(cancellationToken);
        return await _mediator.Send(new GetPurchaseOrderByIdQuery(order.Id), cancellationToken);
    }
}

/// <summary>Shared existence checks so no command can post against a missing supplier/material/warehouse.</summary>
internal static class PurchaseRules
{
    public static async Task EnsureSupplierExists(IApplicationDbContext db, Guid supplierId, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            throw new NotFoundException("Supplier", supplierId);
    }

    public static async Task EnsureWarehouseExists(IApplicationDbContext db, Guid warehouseId, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId, ct))
            throw new NotFoundException("Warehouse", warehouseId);
    }

    public static async Task EnsureMaterialExists(IApplicationDbContext db, Guid materialId, CancellationToken ct)
    {
        if (!await db.Materials.AnyAsync(m => m.Id == materialId, ct))
            throw new NotFoundException("Material", materialId);
    }

    public static async Task EnsureTreasuryAccountExists(IApplicationDbContext db, Guid accountId, CancellationToken ct)
    {
        if (!await db.TreasuryAccounts.AnyAsync(a => a.Id == accountId, ct))
            throw new NotFoundException("TreasuryAccount", accountId);
    }
}
