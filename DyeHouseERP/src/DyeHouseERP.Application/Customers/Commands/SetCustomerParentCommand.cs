using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Customers.Commands;

/// <summary>
/// Sets or clears a customer's direct PARENT - i.e. makes it a sub-customer of another
/// customer (spec section 7). The relationship is explicit and stored (a real self-referencing
/// FK), never inferred from a code prefix or a note.
///
/// Validation (the entity cannot see the whole tree, so it happens here):
///   * the parent must exist and must not be the customer itself;
///   * the parent must not already be a DESCENDANT of the customer - otherwise assigning it
///     would close a cycle (A -&gt; B -&gt; C -&gt; A), which would make the hierarchy unusable and
///     any upward walk infinite.
/// </summary>
public record SetCustomerParentCommand(Guid CustomerId, Guid? ParentCustomerId) : IRequest<CustomerDto>;

public class SetCustomerParentCommandHandler : IRequestHandler<SetCustomerParentCommand, CustomerDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerParentCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<CustomerDto> Handle(SetCustomerParentCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        if (request.ParentCustomerId is null)
        {
            customer.SetParent(null, _currentUser.UserName);
            await _db.SaveChangesAsync(cancellationToken);
            return await LoadDtoAsync(customer, cancellationToken);
        }

        var parentId = request.ParentCustomerId.Value;

        if (parentId == customer.Id)
            throw new DomainException("A customer cannot be its own parent (sub-customer of itself).");

        var parent = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken)
            ?? throw new NotFoundException("Customer", parentId);

        // Walk UP from the proposed parent. If we ever reach this customer, the proposed
        // link would create a cycle. The parent map is read once; the visited set makes the
        // walk terminate even on pre-existing bad data.
        var parentOf = await _db.Customers.AsNoTracking()
            .Where(c => c.ParentCustomerId != null)
            .Select(c => new { c.Id, ParentId = c.ParentCustomerId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.ParentId, cancellationToken);

        var visited = new HashSet<Guid>();
        var cursor = parentId;
        while (visited.Add(cursor))
        {
            if (cursor == customer.Id)
                throw new DomainException(
                    $"'{parent.Code}' is already a sub-customer of '{customer.Code}'. " +
                    "Assigning it as the parent would create a circular relationship.");

            if (!parentOf.TryGetValue(cursor, out var next))
                break; // reached a top-level customer: no cycle

            cursor = next;
        }

        customer.SetParent(parentId, _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);
        return await LoadDtoAsync(customer, cancellationToken);
    }

    private async Task<CustomerDto> LoadDtoAsync(Domain.Entities.Customer customer, CancellationToken cancellationToken)
    {
        var dto = CustomerMapper.ToDto(customer);
        if (customer.ParentCustomerId.HasValue)
        {
            var parent = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customer.ParentCustomerId.Value, cancellationToken);
            dto.ParentCustomerCode = parent?.Code;
            dto.ParentCustomerName = parent?.Name;
        }
        return dto;
    }
}
