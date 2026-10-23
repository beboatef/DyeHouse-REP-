using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Warehouses.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Warehouses.Commands;

/// <summary>
/// Creates a warehouse (spec section 15). Warehouses are master data, never a fixed
/// list: an administrator adds as many, of whatever kind, as the factory needs - and
/// every document that lets the user pick a warehouse reads this table.
/// </summary>
public record CreateWarehouseCommand(string Code, string Name, WarehouseKind Kind) : IRequest<WarehouseMasterDto>;

public class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class CreateWarehouseCommandHandler : IRequestHandler<CreateWarehouseCommand, WarehouseMasterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateWarehouseCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<WarehouseMasterDto> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        WarehouseGuard.EnsureAllowedWarehouseKind(request.Kind);
        var code = (request.Code ?? string.Empty).Trim();

        // The code is the identity documents and reports carry, so it stays unique.
        var codeExists = await _db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken);
        if (codeExists)
            throw new DuplicateCodeException("Warehouse", code);

        var warehouse = new Warehouse(code, request.Name, request.Kind, _currentUser.UserName);

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(cancellationToken);

        return Map(warehouse);
    }

    internal static WarehouseMasterDto Map(Warehouse w) => new()
    {
        Id = w.Id,
        Code = w.Code,
        Name = w.Name,
        Kind = w.Kind,
        IsActive = w.IsActive
    };
}

/// <summary>
/// Renames a warehouse and/or re-classifies its kind. Never touches stock, ledger rows
/// or the documents that already reference it - the code stays immutable.
/// </summary>
public record UpdateWarehouseCommand(Guid Id, string Name, WarehouseKind Kind) : IRequest<WarehouseMasterDto>;

public class UpdateWarehouseCommandValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public class UpdateWarehouseCommandHandler : IRequestHandler<UpdateWarehouseCommand, WarehouseMasterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateWarehouseCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<WarehouseMasterDto> Handle(UpdateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Warehouse", request.Id);

        WarehouseGuard.EnsureAllowedWarehouseKind(request.Kind);
        if (warehouse.Kind != request.Kind)
            throw new DomainException("A warehouse's kind cannot be changed after creation. Create the correct warehouse and move stock through authorized documents instead.");

        warehouse.SetName(request.Name, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CreateWarehouseCommandHandler.Map(warehouse);
    }
}

/// <summary>
/// Activates or deactivates a warehouse. Deactivation only sets the flag: no row is
/// deleted, no ledger entry is touched and the previous documents stay fully traceable
/// (spec sections 15 and 42). New documents are refused an inactive warehouse where
/// they resolve one.
/// </summary>
public record SetWarehouseActiveCommand(Guid Id, bool IsActive) : IRequest<WarehouseMasterDto>;

public class SetWarehouseActiveCommandHandler : IRequestHandler<SetWarehouseActiveCommand, WarehouseMasterDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetWarehouseActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<WarehouseMasterDto> Handle(SetWarehouseActiveCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Warehouse", request.Id);

        if (request.IsActive)
            warehouse.Activate(_currentUser.UserName);
        else
            warehouse.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CreateWarehouseCommandHandler.Map(warehouse);
    }
}
