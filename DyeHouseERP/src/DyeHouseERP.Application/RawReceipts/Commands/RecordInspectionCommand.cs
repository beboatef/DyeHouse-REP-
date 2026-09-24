using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;

namespace DyeHouseERP.Application.RawReceipts.Commands;

public record RecordInspectionCommand(
    Guid RawMessageId,
    InspectionStatus Result,
    string? Notes) : IRequest<Unit>;

public class RecordInspectionCommandValidator : AbstractValidator<RecordInspectionCommand>
{
    public RecordInspectionCommandValidator()
    {
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.Result).IsInEnum().NotEqual(InspectionStatus.PendingInspection);
    }
}

public class RecordInspectionCommandHandler : IRequestHandler<RecordInspectionCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;

    public RecordInspectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(RecordInspectionCommand request, CancellationToken cancellationToken)
    {
        var message = await _db.RawMessages.FindAsync(new object[] { request.RawMessageId }, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        message.RecordInspection(request.Result, _currentUser.UserName, request.Notes, _clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
