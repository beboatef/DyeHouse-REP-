using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.DTOs;
using DyeHouseERP.Application.FormationRequests.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.FormationRequests.Commands;

/// <summary>
/// Optional explicit specification values for one group. Anything left null when a
/// SpecificationTemplateId is also supplied keeps the template's value.
/// </summary>
public record FormationSpecificationInput(
    decimal? WidthCm = null, decimal? MetersPerKg = null, decimal? Gsm = null,
    string? TubFormat = null, string? WindingTapeFormat = null,
    string? QualityInstructions = null, string? LabInstructions = null,
    string? InternalInstructions = null, string? CustomerInstructions = null);

/// <summary>
/// One group/cell on a request (spec section 30). <paramref name="Id"/> is only
/// sent when editing an existing group; omitted means "create a new group".
/// </summary>
public record FormationGroupInput(
    decimal PlannedQuantity,
    UnitOfMeasure Unit,
    int? TubCount = null,
    string? Name = null,
    string? Color = null,
    Guid? SpecificationTemplateId = null,
    FormationSpecificationInput? Specification = null,
    string? Notes = null,
    Guid? Id = null);

/// <summary>
/// Creates a Formation Request (طلب تشكيل) with as many groups/cells as the customer needs (spec sections 28-30).
/// No stock moves here - the request only plans and authorises; material is issued later through the ledger.
/// </summary>
public record CreateFormationRequestCommand(
    Guid CustomerId, Guid ItemId, DateTime RequestDate, UnitOfMeasure Unit,
    List<FormationGroupInput> Groups, Guid? RawMessageId = null, string? Notes = null)
    : IRequest<FormationRequestDto>;

public class CreateFormationRequestCommandValidator : AbstractValidator<CreateFormationRequestCommand>
{
    public CreateFormationRequestCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.Groups).NotEmpty().WithMessage("A formation request needs at least one group/cell.");
        RuleForEach(x => x.Groups).ChildRules(group =>
        {
            group.RuleFor(g => g.PlannedQuantity).GreaterThan(0);
            group.RuleFor(g => g.Unit).IsInEnum();
            group.RuleFor(g => g.TubCount).GreaterThanOrEqualTo(0).When(g => g.TubCount.HasValue);
        });
    }
}

public class CreateFormationRequestCommandHandler : IRequestHandler<CreateFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateFormationRequestCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<FormationRequestDto> Handle(CreateFormationRequestCommand request, CancellationToken cancellationToken)
    {
        await FormationRequestRules.EnsureCustomerAndItemAsync(_db, request.CustomerId, request.ItemId, cancellationToken);
        await FormationRequestRules.EnsureMessageOwnershipAsync(_db, request.RawMessageId, request.CustomerId, cancellationToken);

        var number = await _numberGenerator.NextAsync(DocumentType.FormationRequest, cancellationToken: cancellationToken);
        var total = request.Groups.Sum(g => g.PlannedQuantity);

        var entity = new FormationRequest(
            number, request.RequestDate, request.CustomerId, request.ItemId, request.Unit,
            _currentUser.UserName, request.RawMessageId, total, request.Notes);

        await FormationRequestGroupWriter.ApplyAsync(_db, entity, request.Groups, _currentUser.UserName, cancellationToken);

        // The header total always mirrors the groups, so the two can never disagree.
        entity.UpdateHeader(request.RequestDate, request.RawMessageId, request.Notes,
            entity.Groups.Sum(g => g.PlannedQuantity), _currentUser.UserName);

        _db.FormationRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
    }
}

/// <summary>
/// Replaces the header and the full group set of a request that is still editable (Draft/Rejected).
/// Groups that are gone are removed, groups that remain are updated in place (keeping their Id and history),
/// and new groups are appended - so group identities stay stable for traceability while the request is being prepared.
/// </summary>
public record UpdateFormationRequestCommand(
    Guid Id, DateTime RequestDate, UnitOfMeasure Unit, List<FormationGroupInput> Groups,
    Guid? RawMessageId = null, string? Notes = null)
    : IRequest<FormationRequestDto>;

public class UpdateFormationRequestCommandValidator : AbstractValidator<UpdateFormationRequestCommand>
{
    public UpdateFormationRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.Groups).NotEmpty().WithMessage("A formation request needs at least one group/cell.");
        RuleForEach(x => x.Groups).ChildRules(group =>
        {
            group.RuleFor(g => g.PlannedQuantity).GreaterThan(0);
            group.RuleFor(g => g.Unit).IsInEnum();
        });
    }
}

public class UpdateFormationRequestCommandHandler : IRequestHandler<UpdateFormationRequestCommand, FormationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateFormationRequestCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<FormationRequestDto> Handle(UpdateFormationRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.FormationRequests.Include(r => r.Groups)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Formation Request", request.Id);

        if (!entity.IsEditable)
            throw new DocumentLockedException("Formation Request", entity.RequestNumber);

        await FormationRequestRules.EnsureMessageOwnershipAsync(_db, request.RawMessageId ?? entity.RawMessageId, entity.CustomerId, cancellationToken);

        var keptIds = request.Groups.Where(g => g.Id.HasValue).Select(g => g.Id!.Value).ToHashSet();
        foreach (var group in entity.Groups.Where(g => !keptIds.Contains(g.Id)).ToList())
            entity.RemoveGroup(group.Id, _currentUser.UserName);

        // Existing groups are updated in place (stable Ids, stable traceability); new groups are appended.
        var ordered = request.Groups
            .Select(input => new { input, isNew = !input.Id.HasValue })
            .OrderBy(x => x.isNew ? 1 : 0)
            .Select(x => x.input)
            .ToList();

        await FormationRequestGroupWriter.ApplyAsync(_db, entity, ordered, _currentUser.UserName, cancellationToken, skipExistingIds: keptIds);

        entity.UpdateHeader(request.RequestDate, request.RawMessageId, request.Notes,
            entity.Groups.Sum(g => g.PlannedQuantity), _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await FormationRequestDtoBuilder.BuildAsync(_db, entity, cancellationToken);
    }
}

/// <summary>Shared group writing logic: template snapshot first, then any explicit values the user typed over it.</summary>
internal static class FormationRequestGroupWriter
{
    public static async Task ApplyAsync(
        IApplicationDbContext db, FormationRequest request, List<FormationGroupInput> groups,
        string user, CancellationToken cancellationToken, HashSet<Guid>? skipExistingIds = null)
    {
        var templateIds = groups.Where(g => g.SpecificationTemplateId.HasValue)
            .Select(g => g.SpecificationTemplateId!.Value).Distinct().ToList();

        var templates = templateIds.Count == 0
            ? new Dictionary<Guid, FormationSpecTemplate>()
            : await db.FormationSpecTemplates.Where(t => templateIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        foreach (var input in groups)
        {
            FormationGroup group;

            if (skipExistingIds is not null && input.Id.HasValue && skipExistingIds.Contains(input.Id.Value))
            {
                group = request.UpdateGroup(
                    input.Id.Value, input.Name, input.PlannedQuantity, input.Unit, input.TubCount, input.Color,
                    input.Specification?.WidthCm, input.Specification?.MetersPerKg, input.Specification?.Gsm,
                    input.Specification?.TubFormat, input.Specification?.WindingTapeFormat,
                    input.Specification?.QualityInstructions, input.Specification?.LabInstructions,
                    input.Specification?.InternalInstructions, input.Specification?.CustomerInstructions,
                    input.Notes, user);
            }
            else
            {
                group = request.AddGroup(input.Name, input.PlannedQuantity, input.Unit, input.TubCount, input.Color, input.Notes, user);
            }

            if (input.SpecificationTemplateId.HasValue && templates.TryGetValue(input.SpecificationTemplateId.Value, out var template))
                request.ApplySpecificationTemplate(group.Id, template, user);

            if (input.Specification is not null &&
                (input.Specification.WidthCm.HasValue || input.Specification.MetersPerKg.HasValue ||
                 input.Specification.Gsm.HasValue || input.Specification.TubFormat is not null ||
                 input.Specification.WindingTapeFormat is not null || input.Specification.QualityInstructions is not null ||
                 input.Specification.LabInstructions is not null || input.Specification.InternalInstructions is not null ||
                 input.Specification.CustomerInstructions is not null))
            {
                // Explicit values win over the template snapshot, but nothing is ever converted between KG and Meter.
                var current = group;
                request.UpdateGroup(
                    current.Id, input.Name, input.PlannedQuantity, input.Unit, input.TubCount, input.Color,
                    input.Specification.WidthCm ?? current.WidthCm,
                    input.Specification.MetersPerKg ?? current.MetersPerKg,
                    input.Specification.Gsm ?? current.Gsm,
                    input.Specification.TubFormat ?? current.TubFormat,
                    input.Specification.WindingTapeFormat ?? current.WindingTapeFormat,
                    input.Specification.QualityInstructions ?? current.QualityInstructions,
                    input.Specification.LabInstructions ?? current.LabInstructions,
                    input.Specification.InternalInstructions ?? current.InternalInstructions,
                    input.Specification.CustomerInstructions ?? current.CustomerInstructions,
                    input.Notes, user);
            }
        }

        request.RenumberGroups(user);
        await Task.CompletedTask;
    }
}

/// <summary>Cross-entity rules shared by the formation request commands (spec section 55).</summary>
internal static class FormationRequestRules
{
    public static async Task EnsureCustomerAndItemAsync(
        IApplicationDbContext db, Guid customerId, Guid itemId, CancellationToken cancellationToken)
    {
        var customerExists = await db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists) throw new NotFoundException("Customer", customerId);

        var itemExists = await db.Items.AsNoTracking().AnyAsync(i => i.Id == itemId, cancellationToken);
        if (!itemExists) throw new NotFoundException("Item", itemId);
    }

    /// <summary>
    /// A formation request may only draw from a raw material message that actually belongs to the
    /// customer on the request - customer-owned material must stay traceable to its owner (spec sections 6 and 31).
    /// </summary>
    public static async Task EnsureMessageOwnershipAsync(
        IApplicationDbContext db, Guid? rawMessageId, Guid customerId, CancellationToken cancellationToken)
    {
        if (!rawMessageId.HasValue) return;

        var message = await db.RawMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == rawMessageId.Value, cancellationToken)
            ?? throw new NotFoundException("Raw Message", rawMessageId.Value);

        if (message.CustomerId != customerId)
            throw new DomainException(
                $"Raw material message '{message.MessageNumber}' belongs to a different customer. " +
                "A formation request can only reference the requesting customer's own raw material.");
    }
}
