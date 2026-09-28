using DyeHouseERP.Application.Attachments.DTOs;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Attachments.Queries;

/// <summary>Attachments of one business document (spec section 47) - metadata only.</summary>
public record GetAttachmentsQuery(string EntityType, Guid EntityId) : IRequest<List<AttachmentDto>>;

public class GetAttachmentsQueryHandler : IRequestHandler<GetAttachmentsQuery, List<AttachmentDto>>
{
    private readonly IApplicationDbContext _db;
    public GetAttachmentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<AttachmentDto>> Handle(GetAttachmentsQuery request, CancellationToken cancellationToken)
    {
        if (!AttachmentEntityTypes.IsKnown(request.EntityType)) return new List<AttachmentDto>();

        return await _db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == request.EntityType && a.EntityId == request.EntityId)
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new AttachmentDto
            {
                Id = a.Id, EntityType = a.EntityType, EntityId = a.EntityId,
                FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes,
                Description = a.Description, UploadedBy = a.UploadedBy, UploadedAtUtc = a.UploadedAtUtc
            })
            .ToListAsync(cancellationToken);
    }
}

/// <summary>The attachment bytes, fetched through the API only so permissions are always enforced.</summary>
public record GetAttachmentContentQuery(Guid Id) : IRequest<AttachmentContentDto>;

public class GetAttachmentContentQueryHandler : IRequestHandler<GetAttachmentContentQuery, AttachmentContentDto>
{
    private readonly IApplicationDbContext _db;
    public GetAttachmentContentQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<AttachmentContentDto> Handle(GetAttachmentContentQuery request, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Attachment", request.Id);

        return new AttachmentContentDto
        {
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            Content = attachment.Content
        };
    }
}
