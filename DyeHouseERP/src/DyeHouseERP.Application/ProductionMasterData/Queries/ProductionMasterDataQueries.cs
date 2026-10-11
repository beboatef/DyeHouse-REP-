using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionMasterData.Commands;
using DyeHouseERP.Application.ProductionMasterData.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionMasterData.Queries;

// ---------------------------------------------------------------------
// Machines
// ---------------------------------------------------------------------

public record GetMachinesQuery(bool? ActiveOnly = null, Guid? StageDefinitionId = null)
    : IRequest<List<MachineDto>>;

public class GetMachinesQueryHandler : IRequestHandler<GetMachinesQuery, List<MachineDto>>
{
    private readonly IApplicationDbContext _db;
    public GetMachinesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<MachineDto>> Handle(GetMachinesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Machines.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(m => m.IsActive);

        if (request.StageDefinitionId.HasValue)
        {
            var stageId = request.StageDefinitionId.Value;
            query = query.Where(m => _db.StageMachines.Any(l => l.MachineId == m.Id && l.StageDefinitionId == stageId));
        }

        var machines = await query.OrderBy(m => m.Code).ToListAsync(cancellationToken);

        var result = new List<MachineDto>(machines.Count);
        foreach (var machine in machines)
            result.Add(await MachineMapper.MapAsync(_db, machine, cancellationToken));

        return result;
    }
}

/// <summary>All stage→machine links, for the mapping matrix on the master-data screen.</summary>
public record GetStageMachineLinksQuery(Guid? StageDefinitionId = null) : IRequest<List<StageMachineLinkDto>>;

public class GetStageMachineLinksQueryHandler : IRequestHandler<GetStageMachineLinksQuery, List<StageMachineLinkDto>>
{
    private readonly IApplicationDbContext _db;
    public GetStageMachineLinksQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<StageMachineLinkDto>> Handle(GetStageMachineLinksQuery request, CancellationToken cancellationToken)
    {
        var query = from link in _db.StageMachines.AsNoTracking()
                    join stage in _db.ProductionStageDefinitions.AsNoTracking() on link.StageDefinitionId equals stage.Id
                    join machine in _db.Machines.AsNoTracking() on link.MachineId equals machine.Id
                    select new { link.StageDefinitionId, stage.Code, stage.Name, stage.Sequence, link.MachineId, MachineCode = machine.Code, MachineName = machine.Name, machine.IsActive };

        if (request.StageDefinitionId.HasValue)
            query = query.Where(x => x.StageDefinitionId == request.StageDefinitionId.Value);

        var rows = await query.OrderBy(x => x.Sequence).ThenBy(x => x.MachineCode).ToListAsync(cancellationToken);

        return rows.Select(x => new StageMachineLinkDto
        {
            StageDefinitionId = x.StageDefinitionId,
            StageCode = x.Code,
            StageName = x.Name,
            MachineId = x.MachineId,
            MachineCode = x.MachineCode,
            MachineName = x.MachineName,
            MachineIsActive = x.IsActive
        }).ToList();
    }
}

// ---------------------------------------------------------------------
// Production execution shifts
// ---------------------------------------------------------------------

public record GetProductionShiftsQuery(bool? ActiveOnly = null) : IRequest<List<ProductionShiftDto>>;

public class GetProductionShiftsQueryHandler : IRequestHandler<GetProductionShiftsQuery, List<ProductionShiftDto>>
{
    private readonly IApplicationDbContext _db;
    public GetProductionShiftsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<List<ProductionShiftDto>> Handle(GetProductionShiftsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductionShifts.AsNoTracking().AsQueryable();

        if (request.ActiveOnly == true)
            query = query.Where(s => s.IsActive);

        var shifts = await query.OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        return shifts.Select(ProductionShiftMapper.Map).ToList();
    }
}
