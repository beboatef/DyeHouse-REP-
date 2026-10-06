namespace DyeHouseERP.Application.ProductionStages.DTOs;

public class ProductionStageDefinitionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresInputQuantity { get; set; }
    public bool RequiresOutputQuantity { get; set; }
    public bool RequiresApproval { get; set; }
    public bool AllowSkip { get; set; }
    public bool AllowRepeat { get; set; }
    public bool AllowRework { get; set; }
    public bool AllowReturn { get; set; }

    /// <summary>The stage every new Job Order starts at - التشكيل. At most one stage has this set.</summary>
    public bool IsFormationStage { get; set; }

    /// <summary>The FINAL stage - الجاهز (Ready Goods). At most one stage has this set, and never the same one as the formation stage.</summary>
    public bool IsReadyGoodsStage { get; set; }

    public string? Notes { get; set; }
}
