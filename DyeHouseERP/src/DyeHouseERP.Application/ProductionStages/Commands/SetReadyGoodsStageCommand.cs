using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Commands;

/// <summary>
/// Designates the FINAL stage - الجاهز / Ready Goods (spec section 17).
///
/// Symmetric with <see cref="SetFormationStageCommand"/>: the stage is marked
/// explicitly and never inferred from Sequence, marking a stage MOVES the flag off
/// any other stage, and a stage that is already the formation stage is refused so
/// a Job Order can never start and finish at the same stop.
/// </summary>
public record SetReadyGoodsStageCommand(Guid StageDefinitionId) : IRequest<ProductionStageDefinitionDto>;

public class SetReadyGoodsStageCommandHandler : IRequestHandler<SetReadyGoodsStageCommand, ProductionStageDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetReadyGoodsStageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionStageDefinitionDto> Handle(SetReadyGoodsStageCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        // Checked here as well as on the entity so the caller gets a business
        // rejection (422) rather than a bare ArgumentException.
        if (stage.IsFormationStage)
            throw new DomainException(
                $"Stage '{stage.Name}' is already the formation stage (التشكيل). A stage cannot be both where a Job Order starts and where it finishes - choose a different stage for الجاهز.");

        if (stage.IsReadyGoodsStage)
            return CreateProductionStageDefinitionCommandHandler.Map(stage);

        // Move the flag rather than creating a second ready-goods stage.
        await CreateProductionStageDefinitionCommandHandler.ClearOtherReadyGoodsStagesAsync(
            _db, stage.Id, _currentUser.UserName, cancellationToken);

        stage.SetReadyGoodsStage(true, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return CreateProductionStageDefinitionCommandHandler.Map(stage);
    }
}