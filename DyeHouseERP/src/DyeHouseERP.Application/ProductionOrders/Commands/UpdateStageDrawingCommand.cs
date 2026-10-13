using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

public record UpdateStageDrawingCommand(Guid StageExecutionId, string DrawingNumber, string Reason) : IRequest<ProductionOrderDto>;

public class UpdateStageDrawingCommandValidator : AbstractValidator<UpdateStageDrawingCommand>
{
    public UpdateStageDrawingCommandValidator()
    {
        RuleFor(x => x.StageExecutionId).NotEmpty();
        RuleFor(x => x.DrawingNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class UpdateStageDrawingCommandHandler : IRequestHandler<UpdateStageDrawingCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IDateTime _clock;
    public UpdateStageDrawingCommandHandler(IApplicationDbContext db, ICurrentUserService user, IDateTime clock)
        => (_db, _user, _clock) = (db, user, clock);

    public async Task<ProductionOrderDto> Handle(UpdateStageDrawingCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new Domain.Exceptions.DomainException("A reason is required to correct the drawing number.");
        var stage = await _db.ProductionOrderStageExecutions.SingleOrDefaultAsync(s => s.Id == request.StageExecutionId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrderStageExecution", request.StageExecutionId);
        var before = stage.DrawingNumber;
        // The reason is passed INTO the domain rule, so a closed (completed) print run can only be
        // corrected as an explicit, audited correction - never silently, and never without one.
        stage.SetDrawingNumber(request.DrawingNumber, request.Reason);
        _db.AuditLogEntries.Add(new AuditLogEntry(_clock.UtcNow, _user.UserName, "DrawingCorrection",
            "ProductionOrderStageExecution", stage.Id.ToString(),
            JsonSerializer.Serialize(new { drawingNumber = before }),
            JsonSerializer.Serialize(new { drawingNumber = stage.DrawingNumber }), null, request.Reason));
        await _db.SaveChangesAsync(cancellationToken);
        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, stage.ProductionOrderId, cancellationToken);
    }
}
