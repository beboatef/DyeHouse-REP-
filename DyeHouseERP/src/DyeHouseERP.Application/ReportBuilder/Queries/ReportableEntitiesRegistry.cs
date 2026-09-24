namespace DyeHouseERP.Application.ReportBuilder.Queries;

/// <summary>
/// The whitelist the whole report builder is built around (spec section 36:
/// "Do not expose unsafe arbitrary SQL execution to normal users"). Every
/// entity key and column name a user can request MUST appear here -
/// RunReportCommand rejects anything else before touching the database.
/// Adding a new reportable entity means adding one entry here plus one
/// case in RunReportCommandHandler's switch - no dynamic SQL involved.
/// </summary>
public static class ReportableEntitiesRegistry
{
    public static readonly Dictionary<string, (string Label, string[] Columns)> Entities = new()
    {
        ["ProductionOrders"] = ("أوامر التشغيل", new[]
        {
            "OrderNumber", "CustomerCode", "ItemCode", "Color", "Status", "Priority", "OrderDate", "RequestedQuantityKg", "RequestedQuantityMeter"
        }),
        ["Invoices"] = ("الفواتير", new[]
        {
            "InvoiceNumber", "CustomerCode", "InvoiceDate", "Status", "SubTotal", "Discount", "Tax", "Total"
        }),
        ["Customers"] = ("العملاء", new[]
        {
            "Code", "Name", "IsActive"
        })
    };
}
