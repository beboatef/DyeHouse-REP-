using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Separates.DTOs;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Commands;

/// <summary>Formally declares a Separate as waste rather than reprocessable material (spec section 22).</summary>
public record ScrapSeparateCommand(Guid SeparateId, string Reason) : IRequest<SeparateDto>;

public class ScrapSeparateCommandValidator : AbstractValidator<ScrapSeparateCommand>
{
    public ScrapSeparateCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public class ScrapSeparateCommandHandler : IRequestHandler<ScrapSeparateCommand, SeparateDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ScrapSeparateCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<SeparateDto> Handle(ScrapSeparateCommand request, CancellationToken cancellationToken)
    {
        var separate = await _db.Separates.FirstOrDefaultAsync(s => s.Id == request.SeparateId, cancellationToken)
            ?? throw new NotFoundException("Separate", request.SeparateId);

        separate.Scrap(request.Reason, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);

        return new SeparateDto
        {
            Id = separate.Id, OriginalProductionOrderId = separate.OriginalProductionOrderId,
            StageExecutionId = separate.StageExecutionId, CustomerId = separate.CustomerId, ItemId = separate.ItemId,
            QuantityKg = separate.QuantityKg, QuantityMeter = separate.QuantityMeter,
            Reason = separate.Reason, Notes = separate.Notes, Status = separate.Status,
            ReprocessingProductionOrderId = separate.ReprocessingProductionOrderId
        };
    }
}
