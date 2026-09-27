namespace DyeHouseERP.Application.Items.DTOs;

/// <summary>One row of an item import: exactly what will happen to it, and why it was rejected if it was.</summary>
public class ItemImportRowResult
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string BaseUnit { get; set; } = string.Empty;

    /// <summary>True when the row is importable (new item, or an existing item being updated with permission).</summary>
    public bool IsValid { get; set; }

    /// <summary>True when the row matches an item code that already exists.</summary>
    public bool IsExisting { get; set; }

    /// <summary>Create / Update / Skip - what the execute step will do with this row.</summary>
    public string Action { get; set; } = "Create";

    public List<string> Errors { get; set; } = new();
}

public class ItemImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int NewRows { get; set; }
    public int ExistingRows { get; set; }

    /// <summary>False when existing item codes were found but the caller is not allowed to update them (spec section 7: existing items are never overwritten automatically).</summary>
    public bool UpdateExistingAllowed { get; set; }

    public List<ItemImportRowResult> Rows { get; set; } = new();
}

public class ItemImportExecuteResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();

    /// <summary>Full per-row outcome - this IS the import result/report the spec asks for.</summary>
    public List<ItemImportRowResult> Rows { get; set; } = new();
}
