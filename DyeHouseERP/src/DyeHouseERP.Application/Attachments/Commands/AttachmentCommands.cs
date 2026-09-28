using DyeHouseERP.Application.Attachments.DTOs;
using DyeHouseERP.Application.Attachments.Queries;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Attachments.Commands;

/// <summary>
/// Attaches a file to a business document (spec section 47). The owning record
/// is verified to exist first, so an attachment can never be orphaned by a
/// mistyped id.
/// </summary>
public record UploadAttachmentCommand(
    string EntityType, Guid EntityId, string FileName, string? ContentType, byte[] Content,
    string? Description = null) : IRequest<AttachmentDto>;

public class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    /// <summary>
    /// 25 MB. Contents live in the database, so this keeps a single upload from
    /// bloating the row store and the backup set.
    /// </summary>
    public const int MaxBytes = 25 * 1024 * 1024;

    public UploadAttachmentCommandValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty();
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(400);
        RuleFor(x => x.Content).NotNull().Must(c => c is { Length: > 0 }).WithMessage("The uploaded file is empty.");
        RuleFor(x => x.Content).Must(c => c is null || c.Length <= MaxBytes)
            .WithMessage($"Attachments are limited to {MaxBytes / (1024 * 1024)} MB.");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class UploadAttachmentCommandHandler : IRequestHandler<UploadAttachmentCommand, AttachmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UploadAttachmentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<AttachmentDto> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        if (!AttachmentEntityTypes.IsKnown(request.EntityType))
            throw new NotFoundException("AttachmentTarget", request.EntityType);

        if (!await OwnerExistsAsync(request.EntityType, request.EntityId, cancellationToken))
            throw new NotFoundException(request.EntityType, request.EntityId);

        var attachment = new Attachment(request.EntityType, request.EntityId, request.FileName,
            request.ContentType, request.Content, _currentUser.UserName, request.Description);

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);

        return new AttachmentDto
        {
            Id = attachment.Id, EntityType = attachment.EntityType, EntityId = attachment.EntityId,
            FileName = attachment.FileName, ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes, Description = attachment.Description,
            UploadedBy = attachment.UploadedBy, UploadedAtUtc = attachment.UploadedAtUtc
        };
    }

    private Task<bool> OwnerExistsAsync(string entityType, Guid id, CancellationToken ct) => entityType switch
    {
        AttachmentEntityTypes.Customer => _db.Customers.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.RawMessage => _db.RawMessages.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.ProductionOrder => _db.ProductionOrders.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.FormationRequest => _db.FormationRequests.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.Delivery => _db.Deliveries.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.Invoice => _db.Invoices.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.PurchaseOrder => _db.PurchaseOrders.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.PurchaseReceipt => _db.PurchaseReceipts.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.SupplierInvoice => _db.SupplierInvoices.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.SupplierPayment => _db.SupplierPayments.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.Check => _db.Checks.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.PayrollRun => _db.PayrollRuns.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.Employee => _db.Employees.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.MaterialSale => _db.MaterialSales.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.SupplyIssue => _db.SupplyIssues.AnyAsync(e => e.Id == id, ct),
        AttachmentEntityTypes.StockAdjustment => _db.StockAdjustments.AnyAsync(e => e.Id == id, ct),
        _ => Task.FromResult(false)
    };
}

public record UpdateAttachmentDescriptionCommand(Guid Id, string? Description) : IRequest<AttachmentDto>;

public class UpdateAttachmentDescriptionCommandHandler : IRequestHandler<UpdateAttachmentDescriptionCommand, AttachmentDto>
{
    private readonly IApplicationDbContext _db;
    public UpdateAttachmentDescriptionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<AttachmentDto> Handle(UpdateAttachmentDescriptionCommand request, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Attachment", request.Id);

        attachment.SetDescription(request.Description);
        await _db.SaveChangesAsync(cancellationToken);

        return new AttachmentDto
        {
            Id = attachment.Id, EntityType = attachment.EntityType, EntityId = attachment.EntityId,
            FileName = attachment.FileName, ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes, Description = attachment.Description,
            UploadedBy = attachment.UploadedBy, UploadedAtUtc = attachment.UploadedAtUtc
        };
    }
}

public record DeleteAttachmentCommand(Guid Id) : IRequest<bool>;

public class DeleteAttachmentCommandHandler : IRequestHandler<DeleteAttachmentCommand, bool>
{
    private readonly IApplicationDbContext _db;
    public DeleteAttachmentCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Attachment", request.Id);

        // Attachments are not financial/inventory documents, so removing one is
        // allowed - the audit interceptor still records who deleted it and what
        // the file was (spec section 46).
        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
