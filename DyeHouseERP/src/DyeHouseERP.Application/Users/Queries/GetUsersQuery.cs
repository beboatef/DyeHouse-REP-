using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Users.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Queries;

public record GetUsersQuery : IRequest<List<UserDto>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, List<UserDto>>
{
    private readonly IApplicationDbContext _db;
    public GetUsersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken);

        return users.Select(u => new UserDto
        {
            Id = u.Id, Username = u.Username, DisplayName = u.DisplayName,
            Roles = u.RoleList.ToList(), IsActive = u.IsActive
        }).ToList();
    }
}
