namespace DyeHouseERP.Application.ProductionMasterData.DTOs;

/// <summary>Machine master data (S-Dynamic-Machines).</summary>
public class MachineDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Notes { get; set; }

    /// <summary>Stage definition ids this machine is mapped to (a machine may serve several stages).</summary>
    public List<Guid> StageDefinitionIds { get; set; } = new();

    /// <summary>Stage names, in sequence order, for display.</summary>
    public List<string> StageNames { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>A single stage→machine link, as returned to the stage-mapping UI.</summary>
public class StageMachineLinkDto
{
    public Guid StageDefinitionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public Guid MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public bool MachineIsActive { get; set; }
}

/// <summary>Production EXECUTION shift master data (S-Production-Execution-Shifts). Not an HR shift.</summary>
public class ProductionShiftDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
}
