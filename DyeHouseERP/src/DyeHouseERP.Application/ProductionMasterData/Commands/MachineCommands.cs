using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.ProductionMasterData.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionMasterData.Commands;

/// <summary>
/// Machine master data CRUD (S-Dynamic-Machines). Machines are ordinary,
/// user-maintained master data: there is no fixed maximum, adding a machine
/// never needs a source-code change, and a machine can be mapped to any number
/// of stages.
/// </summary>
public static class MachineMapper
{
    public static async Task<MachineDto> MapAsync(
        IApplicationDbContext db, Machine machine, CancellationToken cancellationToken)
    {
        var links = await db.StageMachines.AsNoTracking()
            .Where(l => l.MachineId == machine.Id)
            .ToListAsync(cancellationToken);

        var stageIds = links.Select(l => l.StageDefinitionId).ToList();

        var stages = await db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => stageIds.Contains(s.Id))
            .OrderBy(s => s.Sequence)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        return new MachineDto
        {
            Id = machine.Id,
            Code = machine.Code,
            Name = machine.Name,
            IsActive = machine.IsActive,
            Notes = machine.Notes,
            StageDefinitionIds = stages.Select(s => s.Id).ToList(),
            StageNames = stages.Select(s => s.Name).ToList(),
            CreatedAtUtc = machine.CreatedAtUtc,
            CreatedBy = machine.CreatedBy,
            ModifiedAtUtc = machine.ModifiedAtUtc,
            ModifiedBy = machine.ModifiedBy
        };
    }
}

public record CreateMachineCommand(
    string Code, string Name, string? Notes, List<Guid>? StageDefinitionIds = null) : IRequest<MachineDto>;

public class CreateMachineCommandValidator : AbstractValidator<CreateMachineCommand>
{
    public CreateMachineCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CreateMachineCommandHandler : IRequestHandler<CreateMachineCommand, MachineDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateMachineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<MachineDto> Handle(CreateMachineCommand request, CancellationToken cancellationToken)
    {
        if (await _db.Machines.AnyAsync(m => m.Code == request.Code, cancellationToken))
            throw new DuplicateCodeException("Machine", request.Code);

        var machine = new Machine(request.Code, request.Name, _currentUser.UserName, request.Notes);
        _db.Machines.Add(machine);

        await MachineStageMapping.ApplyAsync(_db, machine.Id, request.StageDefinitionIds, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await MachineMapper.MapAsync(_db, machine, cancellationToken);
    }
}

public record UpdateMachineCommand(Guid Id, string Name, string? Notes) : IRequest<MachineDto>;

public class UpdateMachineCommandValidator : AbstractValidator<UpdateMachineCommand>
{
    public UpdateMachineCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class UpdateMachineCommandHandler : IRequestHandler<UpdateMachineCommand, MachineDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateMachineCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<MachineDto> Handle(UpdateMachineCommand request, CancellationToken cancellationToken)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Machine", request.Id);

        machine.Update(request.Name, request.Notes, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await MachineMapper.MapAsync(_db, machine, cancellationToken);
    }
}

/// <summary>
/// Activates/deactivates a machine. A deactivated machine is not offered for new
/// execution, but it is never deleted, so the history of the stages it already
/// ran stays intact.
/// </summary>
public record SetMachineActivationCommand(Guid Id, bool IsActive) : IRequest<MachineDto>;

public class SetMachineActivationCommandHandler : IRequestHandler<SetMachineActivationCommand, MachineDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetMachineActivationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<MachineDto> Handle(SetMachineActivationCommand request, CancellationToken cancellationToken)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Machine", request.Id);

        if (request.IsActive) machine.Activate(_currentUser.UserName);
        else machine.Deactivate(_currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await MachineMapper.MapAsync(_db, machine, cancellationToken);
    }
}

/// <summary>
/// Replaces the set of stages a machine is mapped to. Removing a link never
/// touches completed executions - their MachineId reference is historical.
/// </summary>
public record SetMachineStagesCommand(Guid Id, List<Guid> StageDefinitionIds) : IRequest<MachineDto>;

public class SetMachineStagesCommandHandler : IRequestHandler<SetMachineStagesCommand, MachineDto>
{
    private readonly IApplicationDbContext _db;

    public SetMachineStagesCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<MachineDto> Handle(SetMachineStagesCommand request, CancellationToken cancellationToken)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Machine", request.Id);

        await MachineStageMapping.ApplyAsync(_db, machine.Id, request.StageDefinitionIds, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await MachineMapper.MapAsync(_db, machine, cancellationToken);
    }
}

internal static class MachineStageMapping
{
    /// <summary>
    /// Makes the machine's stage links exactly <paramref name="stageDefinitionIds"/>:
    /// validates every stage exists, removes links that are gone and adds links
    /// that are new. A (stage, machine) pair can never be recorded twice.
    /// </summary>
    public static async Task ApplyAsync(
        IApplicationDbContext db, Guid machineId, List<Guid>? stageDefinitionIds, CancellationToken cancellationToken)
    {
        var desired = (stageDefinitionIds ?? new List<Guid>()).Distinct().ToList();

        if (desired.Count > 0)
        {
            var existingStageIds = await db.ProductionStageDefinitions.AsNoTracking()
                .Where(s => desired.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var missing = desired.Except(existingStageIds).ToList();
            if (missing.Count > 0)
                throw new NotFoundException("ProductionStageDefinition", missing[0]);
        }

        var current = await db.StageMachines
            .Where(l => l.MachineId == machineId)
            .ToListAsync(cancellationToken);

        foreach (var link in current.Where(l => !desired.Contains(l.StageDefinitionId)).ToList())
            db.StageMachines.Remove(link);

        var currentStageIds = current.Select(l => l.StageDefinitionId).ToHashSet();
        foreach (var stageId in desired.Where(id => !currentStageIds.Contains(id)))
            db.StageMachines.Add(new StageMachine(stageId, machineId));
    }
}
