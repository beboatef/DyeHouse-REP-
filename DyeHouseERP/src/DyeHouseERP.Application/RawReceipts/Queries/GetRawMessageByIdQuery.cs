using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.RawReceipts.DTOs;
using MediatR;

namespace DyeHouseERP.Application.RawReceipts.Queries;

/// <summary>Single-message lookup - used by the print-preview screen and the QR scan view (spec sections 39-40).</summary>
public record GetRawMessageByIdQuery(Guid Id) : IRequest<RawMessageDto>;

public class GetRawMessageByIdQueryHandler : IRequestHandler<GetRawMessageByIdQuery, RawMessageDto>
{
    private readonly MediatR.ISender _mediator;
    public GetRawMessageByIdQueryHandler(MediatR.ISender mediator) => _mediator = mediator;

    public async Task<RawMessageDto> Handle(GetRawMessageByIdQuery request, CancellationToken cancellationToken)
    {
        var messages = await _mediator.Send(new GetRawMessagesQuery(), cancellationToken);
        return messages.FirstOrDefault(m => m.Id == request.Id)
            ?? throw new NotFoundException("RawMessage", request.Id);
    }
}
