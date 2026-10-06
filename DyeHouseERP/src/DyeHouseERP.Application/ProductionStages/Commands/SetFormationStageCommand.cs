using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Commands;

/// <summary>
/// Designates the stage every new Job Order starts at - التشكيل (spec sections 12
/// and 54). Exactly one stage holds the flag: marking a stage moves it off any
/// other stage, so "which stage is التشكيل" is never ambiguous the way
/// "whichever has Sequence 1" would be.
/// </summary>
public record SetFormationStageCommand(Guid StageDefinitionId) : IRequest<ProductionStageDefinitionDto>;

public class SetFormationStageCommandHandler : IRequestHandler<SetFormationStageCommand, ProductionStageDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetFormationStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionStageDefinitionDto> Handle(SetFormationStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        if (stage.IsFormationStage)
            return CreateProductionStageDefinitionCommandHandler.Map(stage);

        // Move the flag rather than adding a second one.
        await CreateProductionStageDefinitionCommandHandler.ClearOtherFormationStagesAsync(
            _db, stage.Id, _currentUser.UserName, cancellationToken);

        stage.SetFormationStage(true, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CreateProductionStageDefinitionCommandHandler.Map(stage);
    }
}
