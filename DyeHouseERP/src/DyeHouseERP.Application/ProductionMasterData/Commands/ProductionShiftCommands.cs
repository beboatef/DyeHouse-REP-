using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionMasterData.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionMasterData.Commands;

/// <summary>
/// Production EXECUTION shift master data (S-Production-Execution-Shifts).
/// Entirely user-configurable and deliberately separate from the HR employee
/// shift model: no attendance, no hours, no rest days, no payroll link.
/// </summary>
public static class ProductionShiftMapper
{
    public static ProductionShiftDto Map(ProductionShift s) => new()
    {
        Id = s.Id,
        Code = s.Code,
        Name = s.Name,
        Sequence = s.Sequence,
        IsActive = s.IsActive,
        CreatedAtUtc = s.CreatedAtUtc,
        CreatedBy = s.CreatedBy,
        ModifiedAtUtc = s.ModifiedAtUtc,
        ModifiedBy = s.ModifiedBy
    };
}

public record CreateProductionShiftCommand(string Code, string Name, int Sequence) : IRequest<ProductionShiftDto>;

public class CreateProductionShiftCommandValidator : AbstractValidator<CreateProductionShiftCommand>
{
    public CreateProductionShiftCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sequence).GreaterThan(0);
    }
}

public class CreateProductionShiftCommandHandler : IRequestHandler<CreateProductionShiftCommand, ProductionShiftDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateProductionShiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<ProductionShiftDto> Handle(CreateProductionShiftCommand request, CancellationToken cancellationToken)
    {
        if (await _db.ProductionShifts.AnyAsync(s => s.Code == request.Code, cancellationToken))
            throw new DuplicateCodeException("Production Shift", request.Code);

        var shift = new ProductionShift(request.Code, request.Name, request.Sequence, _currentUser.UserName);
        _db.ProductionShifts.Add(shift);

        await _db.SaveChangesAsync(cancellationToken);
        return ProductionShiftMapper.Map(shift);
    }
}

public record UpdateProductionShiftCommand(Guid Id, string Name, int Sequence) : IRequest<ProductionShiftDto>;

public class UpdateProductionShiftCommandValidator : AbstractValidator<UpdateProductionShiftCommand>
{
    public UpdateProductionShiftCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sequence).GreaterThan(0);
    }
}

public class UpdateProductionShiftCommandHandler : IRequestHandler<UpdateProductionShiftCommand, ProductionShiftDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateProductionShiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<ProductionShiftDto> Handle(UpdateProductionShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await _db.ProductionShifts.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ProductionShift", request.Id);

        shift.Update(request.Name, request.Sequence, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return ProductionShiftMapper.Map(shift);
    }
}

public record SetProductionShiftActivationCommand(Guid Id, bool IsActive) : IRequest<ProductionShiftDto>;

public class SetProductionShiftActivationCommandHandler : IRequestHandler<SetProductionShiftActivationCommand, ProductionShiftDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetProductionShiftActivationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<ProductionShiftDto> Handle(SetProductionShiftActivationCommand request, CancellationToken cancellationToken)
    {
        var shift = await _db.ProductionShifts.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ProductionShift", request.Id);

        if (request.IsActive) shift.Activate(_currentUser.UserName);
        else shift.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return ProductionShiftMapper.Map(shift);
    }
}
