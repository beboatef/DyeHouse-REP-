namespace DyeHouseERP.Application.ReportBuilder.DTOs;

public class ReportableEntityDto
{
    public string EntityKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
}

public class ReportFilters
{
    public Guid? CustomerId { get; set; }
    public Guid? ItemId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Status { get; set; }
}

public class ReportResultDto
{
    public List<string> Headers { get; set; } = new();
    public List<List<string?>> Rows { get; set; } = new();
}

public class SavedReportTemplateDto
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
}
