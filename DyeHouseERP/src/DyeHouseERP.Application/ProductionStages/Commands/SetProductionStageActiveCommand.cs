using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionStages.Commands;

/// <summary>
/// Activates or deactivates a production stage (spec section 19).
///
/// Deactivation is a FLAG, never a delete: the row stays in master data, every existing
/// stage execution keeps its StageDefinitionId, and reports, costs and traceability for
/// old Job Orders keep resolving it. What deactivation stops is the stage being picked
/// for anything NEW - it drops out of the active route, and the transfer command refuses
/// it explicitly.
///
/// The two route markers are protected: the stage every Job Order STARTS at (التشكيل) and
/// the stage where output becomes Ready Goods (الجاهز) cannot be deactivated, because that
/// would silently break every new Job Order instead of parking an operation. Another stage
/// has to take the role first - those flags are moved by SetFormationStageCommand and
/// SetReadyGoodsStageCommand.
/// </summary>
public record SetProductionStageActiveCommand(Guid StageDefinitionId, bool IsActive)
    : IRequest<ProductionStageDefinitionDto>;

public class SetProductionStageActiveCommandHandler
    : IRequestHandler<SetProductionStageActiveCommand, ProductionStageDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetProductionStageActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ProductionStageDefinitionDto> Handle(
        SetProductionStageActiveCommand request, CancellationToken cancellationToken)
    {
        var stage = await _db.ProductionStageDefinitions
            .FirstOrDefaultAsync(s => s.Id == request.StageDefinitionId, cancellationToken)
            ?? throw new NotFoundException("ProductionStageDefinition", request.StageDefinitionId);

        if (request.IsActive)
        {
            stage.Activate(_currentUser.UserName);
        }
        else
        {
            if (stage.IsFormationStage)
                throw new DomainException(
                    $"Stage '{stage.Name}' is where every Job Order starts (التشكيل). Move that role to another stage before deactivating it.");

            if (stage.IsReadyGoodsStage)
                throw new DomainException(
                    $"Stage '{stage.Name}' is where output becomes Ready Goods (الجاهز). Move that role to another stage before deactivating it.");

            stage.Deactivate(_currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return CreateProductionStageDefinitionCommandHandler.Map(stage);
    }
}
