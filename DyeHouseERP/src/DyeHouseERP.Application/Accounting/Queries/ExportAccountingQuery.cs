using System.Text.Json;
using DyeHouseERP.Application.Accounting.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Accounting.Queries;

/// <summary>
/// Accounting export (spec Part 2): read-only extraction of five export sets
/// (customer invoices with lines, supplier invoices and payments, treasury
/// transactions per account with SQL running balance, payroll runs per month,
/// cheque movements) as CSV (UTF-8 BOM, one set per file) or XLSX (one sheet
/// per set, ClosedXML, consistent with the existing exports). Every row
/// carries its source document number and the ledger row id for traceability.
/// Totals are appended from the same rows. The export never alters or locks
/// data; the only write is the required audit entry (range, format, row
/// counts - never file contents).
/// </summary>
public record ExportAccountingQuery(
    DateTime From,
    DateTime To,
    Guid? CustomerId = null,
    Guid? SupplierId = null,
    string Format = "xlsx",
    string? Set = null) : IRequest<AccountingExportFile>;

public class ExportAccountingQueryValidator : AbstractValidator<ExportAccountingQuery>
{
    public ExportAccountingQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("To must be on or after From.");
        RuleFor(x => x.Format).Must(f => string.Equals(f, "csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, "xlsx", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be csv or xlsx.");
        RuleFor(x => x.Set).Must(s => s is null || AccountingExportSets.All.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Set must be one of: {string.Join(", ", AccountingExportSets.All)}.");
        RuleFor(x => x.Set).NotEmpty().When(x => string.Equals(x.Format, "csv", StringComparison.OrdinalIgnoreCase))
            .WithMessage("CSV carries exactly one set per file: Set is required when Format is csv.");
        RuleFor(x => x).Must(x => x.CustomerId is null || x.SupplierId is null)
            .WithMessage("CustomerId and SupplierId cannot be combined: filter by one party at a time.");
        RuleFor(x => x.CustomerId).Null()
            .When(x => x.Set is not null && !string.Equals(x.Set, AccountingExportSets.CustomerInvoices, StringComparison.OrdinalIgnoreCase))
            .WithMessage("CustomerId only applies to the customer-invoices set.");
        RuleFor(x => x.SupplierId).Null()
            .When(x => x.Set is not null && !string.Equals(x.Set, AccountingExportSets.Supplier, StringComparison.OrdinalIgnoreCase))
            .WithMessage("SupplierId only applies to the supplier set.");
    }
}

public class ExportAccountingQueryHandler : IRequestHandler<ExportAccountingQuery, AccountingExportFile>
{
    private readonly IApplicationDbContext _db;
    private readonly IReportExportService _exports;
    private readonly IDateTime _clock;
    private readonly ICurrentUserService _currentUser;

    public ExportAccountingQueryHandler(
        IApplicationDbContext db, IReportExportService exports, IDateTime clock, ICurrentUserService currentUser)
    {
        _db = db; _exports = exports; _clock = clock; _currentUser = currentUser;
    }

    public async Task<AccountingExportFile> Handle(ExportAccountingQuery request, CancellationToken cancellationToken)
    {
        var format = request.Format.ToLowerInvariant();
        var wanted = request.Set is null
            ? AccountingExportSets.All.ToList()
            : new List<string> { AccountingExportSets.All.First(s => string.Equals(s, request.Set, StringComparison.OrdinalIgnoreCase)) };

        var sheets = new List<ExportWorksheet>();
        var rowsPerSet = new Dictionary<string, int>();

        if (wanted.Contains(AccountingExportSets.CustomerInvoices))
        {
            var sheet = await BuildCustomerInvoicesAsync(request, cancellationToken);
            sheets.Add(sheet); rowsPerSet[AccountingExportSets.CustomerInvoices] = CountDataRows(sheet);
        }
        if (wanted.Contains(AccountingExportSets.Supplier))
        {
            var sheet = await BuildSupplierAsync(request, cancellationToken);
            sheets.Add(sheet); rowsPerSet[AccountingExportSets.Supplier] = CountDataRows(sheet);
        }
        if (wanted.Contains(AccountingExportSets.Treasury))
        {
            var sheet = await BuildTreasuryAsync(request, cancellationToken);
            sheets.Add(sheet); rowsPerSet[AccountingExportSets.Treasury] = CountDataRows(sheet);
        }
        if (wanted.Contains(AccountingExportSets.Payroll))
        {
            var sheet = await BuildPayrollAsync(request, cancellationToken);
            sheets.Add(sheet); rowsPerSet[AccountingExportSets.Payroll] = CountDataRows(sheet);
        }
        if (wanted.Contains(AccountingExportSets.Cheques))
        {
            var sheet = await BuildChequesAsync(request, cancellationToken);
            sheets.Add(sheet); rowsPerSet[AccountingExportSets.Cheques] = CountDataRows(sheet);
        }

        byte[] content;
        string contentType, fileName;
        var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss");
        if (format == "csv")
        {
            content = _exports.GenerateCsv(sheets[0].Headers, sheets[0].Rows);
            contentType = "text/csv";
            fileName = $"accounting-{sheets[0].SheetName}-{stamp}.csv";
        }
        else
        {
            content = _exports.GenerateExcelWorkbook(sheets);
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            fileName = $"accounting-export-{stamp}.xlsx";
        }

        var totalRows = rowsPerSet.Values.Sum();
        _db.AuditLogEntries.Add(new AuditLogEntry(
            _clock.UtcNow, _currentUser.UserName, "AccountingExport", "AccountingExport",
            entityId: null, beforeDataJson: null,
            afterDataJson: JsonSerializer.Serialize(new
            {
                from = request.From, to = request.To, format,
                sets = wanted, rowCounts = rowsPerSet, totalRows
            }),
            ipAddress: _currentUser.IpAddress, reason: null));
        await _db.SaveChangesAsync(cancellationToken);

        return new AccountingExportFile
        {
            Content = content,
            ContentType = contentType,
            FileName = fileName,
            Format = format,
            SetsIncluded = wanted,
            From = request.From,
            To = request.To,
            TimeZone = KpiQueryHelpers.KpiTimeZone,
            TotalRows = totalRows,
            RowsPerSet = rowsPerSet
        };
    }

    private static int CountDataRows(ExportWorksheet sheet)
        => sheet.Rows.Count(r => !IsTotalsRow(r));

    private static bool IsTotalsRow(object?[] row)
        => row.Length > 0 && row[0] is string label && label == "TOTAL";

    private static object?[] TotalsRow(int width, params (int Index, object? Value)[] totals)
    {
        var row = new object?[width];
        row[0] = "TOTAL";
        foreach (var (index, value) in totals) row[index] = value;
        return row;
    }

    private async Task<ExportWorksheet> BuildCustomerInvoicesAsync(ExportAccountingQuery request, CancellationToken ct)
    {
        var invoicesQuery = _db.Invoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= request.From && i.InvoiceDate <= request.To);
        if (request.CustomerId.HasValue) invoicesQuery = invoicesQuery.Where(i => i.CustomerId == request.CustomerId.Value);
        var invoices = await invoicesQuery.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber).ToListAsync(ct);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var lines = invoiceIds.Count == 0 ? new List<InvoiceLine>()
            : await _db.InvoiceLines.AsNoTracking().Where(l => invoiceIds.Contains(l.InvoiceId))
                .OrderBy(l => l.InvoiceId).ToListAsync(ct);

        var ledgerIds = invoiceIds.Count == 0 ? new Dictionary<Guid, Guid>()
            : await _db.CustomerLedgerEntries.AsNoTracking()
                .Where(e => invoiceIds.Contains(e.SourceDocumentId)
                    && e.SourceDocumentType == DocumentType.Invoice && e.Debit > 0)
                .GroupBy(e => e.SourceDocumentId)
                .Select(g => new { InvoiceId = g.Key, LedgerId = g.Min(e => e.Id) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.LedgerId, ct);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await _db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.Code, c.Name }, ct);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = itemIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Code, ct);

        var headers = new[]
        {
            "Row", "InvoiceNumber", "InvoiceDate", "CustomerCode", "CustomerName",
            "ItemCode", "Quantity", "Unit", "UnitPrice", "LineTotal",
            "InvoiceSubtotal", "InvoiceDiscount", "InvoiceTax", "InvoiceTotal",
            "Status", "LedgerEntryId"
        };
        var rows = new List<object?[]>();
        foreach (var invoice in invoices)
        {
            var invoiceLines = lines.Where(l => l.InvoiceId == invoice.Id).ToList();
            var subTotal = invoiceLines.Sum(l => l.Quantity * l.ProcessingPrice);
            var total = subTotal - invoice.Discount + invoice.Tax;
            customers.TryGetValue(invoice.CustomerId, out var customer);
            ledgerIds.TryGetValue(invoice.Id, out var ledgerId);
            if (invoiceLines.Count == 0)
            {
                rows.Add(new object?[]
                {
                    rows.Count + 1, invoice.InvoiceNumber, invoice.InvoiceDate,
                    customer?.Code ?? "?", customer?.Name ?? "?",
                    null, null, null, null, 0m,
                    subTotal, invoice.Discount, invoice.Tax, total,
                    invoice.Status.ToString(), ledgerId == Guid.Empty ? null : (Guid?)ledgerId
                });
                continue;
            }
            foreach (var line in invoiceLines)
            {
                rows.Add(new object?[]
                {
                    rows.Count + 1, invoice.InvoiceNumber, invoice.InvoiceDate,
                    customer?.Code ?? "?", customer?.Name ?? "?",
                    items.GetValueOrDefault(line.ItemId, "?"),
                    line.Quantity, line.Unit.ToString(), line.ProcessingPrice, line.Quantity * line.ProcessingPrice,
                    subTotal, invoice.Discount, invoice.Tax, total,
                    invoice.Status.ToString(), ledgerId == Guid.Empty ? null : (Guid?)ledgerId
                });
            }
        }

        var distinctTotals = invoices
            .Select(i =>
            {
                var sub = lines.Where(l => l.InvoiceId == i.Id).Sum(l => l.Quantity * l.ProcessingPrice);
                return sub - i.Discount + i.Tax;
            })
            .Sum();
        rows.Add(TotalsRow(headers.Length,
            (9, rows.Where(r => !IsTotalsRow(r)).Sum(r => (decimal?)r[9] ?? 0m)),
            (13, distinctTotals)));
        return new ExportWorksheet(AccountingExportSets.CustomerInvoices, headers, rows);
    }

    private async Task<ExportWorksheet> BuildSupplierAsync(ExportAccountingQuery request, CancellationToken ct)
    {
        var invoicesQuery = _db.SupplierInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= request.From && i.InvoiceDate <= request.To);
        if (request.SupplierId.HasValue) invoicesQuery = invoicesQuery.Where(i => i.SupplierId == request.SupplierId.Value);
        var invoices = await invoicesQuery.OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber).ToListAsync(ct);

        var paymentsQuery = _db.SupplierPayments.AsNoTracking()
            .Where(p => p.PaymentDate >= request.From && p.PaymentDate <= request.To);
        if (request.SupplierId.HasValue) paymentsQuery = paymentsQuery.Where(p => p.SupplierId == request.SupplierId.Value);
        var payments = await paymentsQuery.OrderBy(p => p.PaymentDate).ThenBy(p => p.PaymentNumber).ToListAsync(ct);

        var supplierIds = invoices.Select(i => i.SupplierId).Concat(payments.Select(p => p.SupplierId)).Distinct().ToList();
        var suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var lineCounts = invoiceIds.Count == 0 ? new Dictionary<Guid, int>()
            : await _db.SupplierInvoiceLines.AsNoTracking().Where(l => invoiceIds.Contains(l.SupplierInvoiceId))
                .GroupBy(l => l.SupplierInvoiceId)
                .Select(g => new { InvoiceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.Count, ct);
        var invoiceLedgerIds = invoiceIds.Count == 0 ? new Dictionary<Guid, Guid>()
            : await _db.SupplierLedgerEntries.AsNoTracking()
                .Where(e => invoiceIds.Contains(e.SourceDocumentId)
                    && e.SourceDocumentType == DocumentType.SupplierInvoice && e.Debit > 0)
                .GroupBy(e => e.SourceDocumentId)
                .Select(g => new { InvoiceId = g.Key, LedgerId = g.Min(e => e.Id) })
                .ToDictionaryAsync(x => x.InvoiceId, x => x.LedgerId, ct);

        var paymentIds = payments.Select(p => p.Id).ToList();
        var paymentLedgerIds = paymentIds.Count == 0 ? new Dictionary<Guid, Guid>()
            : await _db.SupplierLedgerEntries.AsNoTracking()
                .Where(e => paymentIds.Contains(e.SourceDocumentId)
                    && e.SourceDocumentType == DocumentType.SupplierPayment && e.Credit > 0)
                .GroupBy(e => e.SourceDocumentId)
                .Select(g => new { PaymentId = g.Key, LedgerId = g.Min(e => e.Id) })
                .ToDictionaryAsync(x => x.PaymentId, x => x.LedgerId, ct);
        var linkedInvoiceNumbers = (await _db.SupplierInvoices.AsNoTracking()
                .Where(i => payments.Select(p => p.SupplierInvoiceId).Contains(i.Id))
                .Select(i => new { i.Id, i.InvoiceNumber }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.InvoiceNumber);

        var headers = new[]
        {
            "Row", "RowType", "DocumentNumber", "DocumentDate", "SupplierCode", "Details",
            "Subtotal", "Discount", "Tax", "InvoiceTotal", "PaidAmount",
            "Status", "LinkedDocument", "LedgerEntryId"
        };
        var rows = new List<object?[]>();
        foreach (var invoice in invoices)
        {
            invoiceLedgerIds.TryGetValue(invoice.Id, out var ledgerId);
            rows.Add(new object?[]
            {
                rows.Count + 1, "Invoice", invoice.InvoiceNumber, invoice.InvoiceDate,
                suppliers.GetValueOrDefault(invoice.SupplierId, "?"),
                $"Lines: {lineCounts.GetValueOrDefault(invoice.Id)}",
                invoice.SubTotal, invoice.Discount, invoice.Tax, invoice.Total, null,
                invoice.Status.ToString(), null, ledgerId == Guid.Empty ? null : (Guid?)ledgerId
            });
        }
        foreach (var payment in payments)
        {
            paymentLedgerIds.TryGetValue(payment.Id, out var ledgerId);
            linkedInvoiceNumbers.TryGetValue(payment.SupplierInvoiceId ?? Guid.Empty, out var linked);
            rows.Add(new object?[]
            {
                rows.Count + 1, "Payment", payment.PaymentNumber, payment.PaymentDate,
                suppliers.GetValueOrDefault(payment.SupplierId, "?"),
                payment.Description ?? payment.PaymentMethod,
                null, null, null, null, payment.Amount,
                payment.TreasuryAccountId.HasValue ? "Posted" : "Recorded",
                linked, ledgerId == Guid.Empty ? null : (Guid?)ledgerId
            });
        }

        var data = rows.Where(r => !IsTotalsRow(r)).ToList();
        rows.Add(TotalsRow(headers.Length,
            (6, data.Sum(r => (decimal?)r[6] ?? 0m)),
            (7, data.Sum(r => (decimal?)r[7] ?? 0m)),
            (8, data.Sum(r => (decimal?)r[8] ?? 0m)),
            (9, data.Sum(r => (decimal?)r[9] ?? 0m)),
            (10, data.Sum(r => (decimal?)r[10] ?? 0m))));
        return new ExportWorksheet(AccountingExportSets.Supplier, headers, rows);
    }

    private async Task<ExportWorksheet> BuildTreasuryAsync(ExportAccountingQuery request, CancellationToken ct)
    {
        var transactions = await _db.TreasuryTransactions.AsNoTracking()
            .Where(t => t.TransactionDate >= request.From && t.TransactionDate <= request.To)
            .Select(t => new
            {
                t.Id, t.TreasuryAccountId, t.TransactionDate, t.Direction, t.Amount,
                t.SourceDocumentNumber, t.Description,
                RunningBalance = _db.TreasuryTransactions
                    .Where(x => x.TreasuryAccountId == t.TreasuryAccountId
                        && (x.TransactionDate < t.TransactionDate
                            || (x.TransactionDate == t.TransactionDate && x.Id.CompareTo(t.Id) <= 0)))
                    .Sum(x => x.Direction == TreasuryDirection.In ? x.Amount : -x.Amount)
            })
            .OrderBy(t => t.TreasuryAccountId).ThenBy(t => t.TransactionDate).ThenBy(t => t.Id)
            .ToListAsync(ct);

        var accounts = await _db.TreasuryAccounts.AsNoTracking()
            .Where(a => transactions.Select(t => t.TreasuryAccountId).Distinct().Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.Code, a.Name }, ct);

        var headers = new[]
        {
            "Row", "AccountCode", "AccountName", "Date", "Direction",
            "InAmount", "OutAmount", "DocumentNumber", "Description", "RunningBalance", "LedgerRowId"
        };
        var rows = new List<object?[]>();
        foreach (var t in transactions)
        {
            accounts.TryGetValue(t.TreasuryAccountId, out var account);
            rows.Add(new object?[]
            {
                rows.Count + 1, account?.Code ?? "?", account?.Name ?? "?",
                t.TransactionDate, t.Direction.ToString(),
                t.Direction == TreasuryDirection.In ? t.Amount : null,
                t.Direction == TreasuryDirection.Out ? t.Amount : null,
                t.SourceDocumentNumber, t.Description, t.RunningBalance, t.Id
            });
        }

        var data = rows.Where(r => !IsTotalsRow(r)).ToList();
        rows.Add(TotalsRow(headers.Length,
            (5, data.Sum(r => (decimal?)r[5] ?? 0m)),
            (6, data.Sum(r => (decimal?)r[6] ?? 0m))));
        return new ExportWorksheet(AccountingExportSets.Treasury, headers, rows);
    }

    private async Task<ExportWorksheet> BuildPayrollAsync(ExportAccountingQuery request, CancellationToken ct)
    {
        var fromKey = request.From.Year * 100 + request.From.Month;
        var toKey = request.To.Year * 100 + request.To.Month;
        var runs = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.PeriodYear * 100 + r.PeriodMonth >= fromKey && r.PeriodYear * 100 + r.PeriodMonth <= toKey)
            .OrderBy(r => r.PeriodYear).ThenBy(r => r.PeriodMonth).ThenBy(r => r.RunNumber)
            .ToListAsync(ct);

        var runIds = runs.Select(r => r.Id).ToList();
        // GrossPay/NetPay are unmapped computed properties, so the sums expand
        // them here in SQL from the mapped pay components.
        var totals = runIds.Count == 0 ? new Dictionary<Guid, (decimal Gross, decimal Deductions, decimal Net)>()
            : await _db.PayrollRunLines.AsNoTracking().Where(l => runIds.Contains(l.PayrollRunId))
                .GroupBy(l => l.PayrollRunId)
                .Select(g => new
                {
                    RunId = g.Key,
                    Gross = g.Sum(l => l.BasicSalary + l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OvertimeAmount + l.OtherEarnings),
                    Deductions = g.Sum(l => l.Deductions),
                    Net = g.Sum(l => l.BasicSalary + l.Allowances + l.Incentives + l.Bonuses + l.Commissions + l.OvertimeAmount + l.OtherEarnings - l.Deductions - l.Tax - l.Insurance - l.Advances)
                })
                .ToDictionaryAsync(x => x.RunId, x => (x.Gross, x.Deductions, x.Net), ct);

        var headers = new[]
        {
            "Row", "RunNumber", "Year", "Month", "Gross", "Deductions", "Net",
            "Status", "PostedAtUtc", "LedgerRowId"
        };
        var rows = new List<object?[]>();
        foreach (var run in runs)
        {
            totals.TryGetValue(run.Id, out var t);
            rows.Add(new object?[]
            {
                rows.Count + 1, run.RunNumber, run.PeriodYear, run.PeriodMonth,
                t.Gross, t.Deductions, t.Net,
                // Payroll has no ledger in this system (no general ledger): the run id
                // itself is the trace key back to the payroll document.
                run.Status.ToString(), run.PostedAtUtc, run.Id
            });
        }

        var data = rows.Where(r => !IsTotalsRow(r)).ToList();
        rows.Add(TotalsRow(headers.Length,
            (4, data.Sum(r => (decimal?)r[4] ?? 0m)),
            (5, data.Sum(r => (decimal?)r[5] ?? 0m)),
            (6, data.Sum(r => (decimal?)r[6] ?? 0m))));
        return new ExportWorksheet(AccountingExportSets.Payroll, headers, rows);
    }

    private async Task<ExportWorksheet> BuildChequesAsync(ExportAccountingQuery request, CancellationToken ct)
    {
        var movements = await _db.CheckMovements.AsNoTracking()
            .Where(m => m.MovementDate >= request.From && m.MovementDate <= request.To)
            .OrderBy(m => m.MovementDate)
            .ToListAsync(ct);

        var checkIds = movements.Select(m => m.CheckId).Distinct().ToList();
        var checks = checkIds.Count == 0 ? new Dictionary<Guid, Check>()
            : await _db.Checks.AsNoTracking().Where(c => checkIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        var headers = new[]
        {
            "Row", "MovementDate", "CheckNumber", "Direction", "BankName", "MovementType",
            "FromHolder", "ToHolder", "Amount", "MovementId"
        };
        var rows = new List<object?[]>();
        foreach (var m in movements)
        {
            checks.TryGetValue(m.CheckId, out var check);
            rows.Add(new object?[]
            {
                rows.Count + 1, m.MovementDate, check?.CheckNumber ?? "?",
                check?.Direction.ToString() ?? "?", check?.BankName ?? "?",
                m.MovementType.ToString(), m.FromHolder, m.ToHolder, check?.Amount,
                // Cheque movements have no ledger row of their own: trace via the
                // movement id (CheckMovements) and the cheque number; a cleared
                // cheque's money row is the TreasuryTransaction whose
                // SourceDocumentId is the check id.
                m.Id
            });
        }

        var data = rows.Where(r => !IsTotalsRow(r)).ToList();
        rows.Add(TotalsRow(headers.Length, (8, data.Sum(r => (decimal?)r[8] ?? 0m))));
        return new ExportWorksheet(AccountingExportSets.Cheques, headers, rows);
    }
}
