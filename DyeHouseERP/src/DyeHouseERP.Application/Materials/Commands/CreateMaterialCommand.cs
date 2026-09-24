using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Commands;

public record CreateMaterialCommand(string Code, string Name, MaterialUnit Unit, decimal PurchasePrice) : IRequest<MaterialDto>;

public class CreateMaterialCommandValidator : AbstractValidator<CreateMaterialCommand>
{
    public CreateMaterialCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
    }
}

public class CreateMaterialCommandHandler : IRequestHandler<CreateMaterialCommand, MaterialDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateMaterialCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<MaterialDto> Handle(CreateMaterialCommand request, CancellationToken cancellationToken)
    {
        if (await _db.Materials.AnyAsync(m => m.Code == request.Code, cancellationToken))
            throw new DuplicateCodeException("Material", request.Code);

        var material = new Material(request.Code, request.Name, request.Unit, request.PurchasePrice, _currentUser.UserName);
        _db.Materials.Add(material);
        await _db.SaveChangesAsync(cancellationToken);

        return new MaterialDto { Id = material.Id, Code = material.Code, Name = material.Name, Unit = material.Unit, PurchasePrice = material.PurchasePrice, IsActive = material.IsActive };
    }
}
