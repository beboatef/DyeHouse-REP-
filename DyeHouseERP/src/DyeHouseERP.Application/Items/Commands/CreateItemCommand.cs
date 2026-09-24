using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Items.Commands;

public record CreateItemCommand(string Code, string Name, UnitOfMeasure BaseUnit) : IRequest<ItemDto>;

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUnit).IsInEnum();
    }
}

public class CreateItemCommandHandler : IRequestHandler<CreateItemCommand, ItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateItemCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ItemDto> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var exists = await _db.Items.AnyAsync(i => i.Code == request.Code, cancellationToken);
        if (exists)
            throw new DuplicateCodeException("Item", request.Code);

        var item = new Item(request.Code, request.Name, request.BaseUnit, _currentUser.UserName);
        _db.Items.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return new ItemDto { Id = item.Id, Code = item.Code, Name = item.Name, BaseUnit = item.BaseUnit, IsActive = item.IsActive };
    }
}
