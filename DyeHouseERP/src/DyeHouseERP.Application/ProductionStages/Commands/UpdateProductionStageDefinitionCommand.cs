using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.DTOs;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Commands;

/// <summary>
/// Edits the administrative fields of a production stage (spec section 19): its display
/// name, its notes and its display order.
///
/// The CODE is not editable: it is the stable identity the starting data and the machine
/// map refer to, so a rename in the UI can never orphan a row. Reordering is a display
/// concern only - a Job Order's route is never inferred from Sequence (spec sections 13-14).
///
/// The number of stages is not fixed anywhere: the twenty-one seeded operations are
/// starting data, and this command administers any stage, whether it came from the seeder
/// or was added later from inside the ERP.
/// </summary>
public record UpdateProductionStageDefinitionCommand(
    Guid StageDefinitionId, string Name, int Sequence, string? Notes = null)
    : IRequest<ProductionStageDefinitionDto>;

public class UpdateProductionStageDefinitionCommandValidator : AbstractValidator<UpdateProductionStageDefinitionCommand>
{
    public UpdateProductionStageDefinitionCommandValidator()
    {
        RuleFor(x => x.StageDefinitionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Sequence).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdateProductionStageDefinitionCommandHandler
    : IRequestHandler<UpdateProductionStageDefinitionCommand, ProductionStageDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateProductionStageDefinitionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionStageDefinitionDto> Handle(
        UpdateProductionStageDefinitionCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        // Historical executions are untouched on purpose: they reference the stage by Id, so
        // renaming or reordering a stage can never rewrite what an old Job Order recorded.
        stage.UpdateDetails(request.Name, request.Sequence, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CreateProductionStageDefinitionCommandHandler.Map(stage);
    }
}
