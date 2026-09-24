using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.Application.Customers.DTOs;

public class CustomerImportPreviewDto
{
    public List<ImportRowResult> Rows { get; set; } = new();
    public int ValidCount { get; set; }
    public int InvalidCount { get; set; }
}

public class CustomerImportExecuteResultDto
{
    public int CreatedCount { get; set; }
    public int SkippedInvalidCount { get; set; }
    public List<ImportRowResult> Errors { get; set; } = new();
}
