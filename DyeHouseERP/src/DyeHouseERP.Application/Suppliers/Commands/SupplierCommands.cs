using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Suppliers.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Suppliers.Commands;

/// <summary>Creates a supplier. Code is manually entered and unique; the account number is linked to it (spec section 5/35).</summary>
public record CreateSupplierCommand(
    string Code, string Name, string? NameAr = null, string? NameEn = null,
    string? AccountNumber = null, string? Phone = null, string? Address = null,
    string? ContactPerson = null, string? TaxNumber = null) : IRequest<SupplierDto>;

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.AccountNumber).MaximumLength(30);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.NameAr) || !string.IsNullOrWhiteSpace(x.NameEn))
            .WithMessage("A supplier name is required (Arabic and/or English).");
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, SupplierDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateSupplierCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        if (await _db.Suppliers.AnyAsync(s => s.Code == code, cancellationToken))
            throw new DuplicateCodeException("Supplier", code);

        var entity = new Supplier(code, request.Name, _currentUser.UserName,
            request.NameAr, request.NameEn, request.Phone, request.Address, request.ContactPerson, request.TaxNumber);

        if (!string.IsNullOrWhiteSpace(request.AccountNumber))
            entity.SetAccountNumber(request.AccountNumber);

        _db.Suppliers.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return SupplierMapper.Map(entity);
    }
}

public record UpdateSupplierCommand(
    Guid Id, string? NameAr = null, string? NameEn = null, string? AccountNumber = null,
    string? Phone = null, string? Address = null, string? ContactPerson = null,
    string? TaxNumber = null, bool? IsActive = null) : IRequest<SupplierDto>;

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, SupplierDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public UpdateSupplierCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<SupplierDto> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.Id);

        if (!string.IsNullOrWhiteSpace(request.NameAr) || !string.IsNullOrWhiteSpace(request.NameEn))
            entity.SetNames(request.NameAr, request.NameEn);

        if (!string.IsNullOrWhiteSpace(request.AccountNumber))
            entity.SetAccountNumber(request.AccountNumber);

        entity.SetContact(
            request.Phone ?? entity.Phone,
            request.Address ?? entity.Address,
            request.ContactPerson ?? entity.ContactPerson,
            request.TaxNumber ?? entity.TaxNumber);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) entity.Activate(_currentUser.UserName);
            else entity.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return SupplierMapper.Map(entity);
    }
}

internal static class SupplierMapper
{
    public static SupplierDto Map(Supplier s) => new()
    {
        Id = s.Id,
        Code = s.Code,
        Name = s.Name,
        NameAr = s.NameAr,
        NameEn = s.NameEn,
        AccountNumber = s.AccountNumber,
        Phone = s.Phone,
        Address = s.Address,
        ContactPerson = s.ContactPerson,
        TaxNumber = s.TaxNumber,
        IsActive = s.IsActive
    };
}
