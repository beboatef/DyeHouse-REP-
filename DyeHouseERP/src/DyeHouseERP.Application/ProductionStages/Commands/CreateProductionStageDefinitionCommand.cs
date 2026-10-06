using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Commands;

public record CreateProductionStageDefinitionCommand(
    string Code, string Name, int Sequence,
    bool RequiresInputQuantity, bool RequiresOutputQuantity, bool RequiresApproval,
    bool AllowSkip, bool AllowRepeat, bool AllowRework, bool AllowReturn,
    string? Notes, bool IsFormationStage = false) : IRequest<ProductionStageDefinitionDto>;

public class CreateProductionStageDefinitionCommandValidator : AbstractValidator<CreateProductionStageDefinitionCommand>
{
    public CreateProductionStageDefinitionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sequence).GreaterThan(0);
    }
}

public class CreateProductionStageDefinitionCommandHandler
    : IRequestHandler<CreateProductionStageDefinitionCommand, ProductionStageDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateProductionStageDefinitionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionStageDefinitionDto> Handle(CreateProductionStageDefinitionCommand request, CancellationToken cancellationToken)
    {
        var codeExists = await _db.ProductionStageDefinitions.AnyAsync(s => s.Code == request.Code, cancellationToken);
        if (codeExists)
            throw new DuplicateCodeException("Production Stage", request.Code);

        var stage = new ProductionStageDefinition(
            request.Code, request.Name, request.Sequence, _currentUser.UserName,
            request.RequiresInputQuantity, request.RequiresOutputQuantity, request.RequiresApproval,
            request.AllowSkip, request.AllowRepeat, request.AllowRework, request.AllowReturn,
            request.Notes, request.IsFormationStage);

        // At most one stage may be the formation stage (التشكيل), so creating a new
        // formation stage moves the flag rather than adding a second one.
        if (request.IsFormationStage)
            await ClearOtherFormationStagesAsync(_db, null, _currentUser.UserName, cancellationToken);

        _db.ProductionStageDefinitions.Add(stage);
        await _db.SaveChangesAsync(cancellationToken);

        return Map(stage);
    }

    internal static ProductionStageDefinitionDto Map(ProductionStageDefinition s) => new()
    {
        Id = s.Id,
        Code = s.Code,
        Name = s.Name,
        Sequence = s.Sequence,
        IsActive = s.IsActive,
        RequiresInputQuantity = s.RequiresInputQuantity,
        RequiresOutputQuantity = s.RequiresOutputQuantity,
        RequiresApproval = s.RequiresApproval,
        AllowSkip = s.AllowSkip,
        AllowRepeat = s.AllowRepeat,
        AllowRework = s.AllowRework,
        AllowReturn = s.AllowReturn,
        IsFormationStage = s.IsFormationStage,
        IsReadyGoodsStage = s.IsReadyGoodsStage,
        Notes = s.Notes
    };

    /// <summary>
    /// Clears IsFormationStage on every stage other than <paramref name="exceptStageId"/>,
    /// so the "which stage is التشكيل" answer can never be ambiguous.
    /// </summary>
    internal static async Task ClearOtherFormationStagesAsync(
        IApplicationDbContext db, Guid? exceptStageId, string modifiedBy, CancellationToken cancellationToken)
    {
        var others = await db.ProductionStageDefinitions
            .Where(s => s.IsFormationStage && (exceptStageId == null || s.Id != exceptStageId.Value))
            .ToListAsync(cancellationToken);

        foreach (var other in others)
            other.SetFormationStage(false, modifiedBy);
    }

    /// <summary>
    /// Clears the ready-goods flag from every stage except one, so "الجاهز" is
    /// always exactly one stage - the same move-the-flag approach the formation
    /// stage uses. Keeping the two symmetric is what stops a route from having
    /// an ambiguous start or an ambiguous end.
    /// </summary>
    internal static async Task ClearOtherReadyGoodsStagesAsync(
        IApplicationDbContext db, Guid? exceptStageId, string modifiedBy, CancellationToken cancellationToken)
    {
        var others = await db.ProductionStageDefinitions
            .Where(s => s.IsReadyGoodsStage && (exceptStageId == null || s.Id != exceptStageId.Value))
            .ToListAsync(cancellationToken);

        foreach (var other in others)
            other.SetReadyGoodsStage(false, modifiedBy);
    }
}
