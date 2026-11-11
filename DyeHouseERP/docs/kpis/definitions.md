# Management KPI Definitions (Part 1)

Every KPI below is computed read-only from the append-only ledgers and the
existing documents. No KPI writes anything, and no KPI reads a stored balance:
balances are always derived by summation at query time.

Conventions shared by all seven KPIs:

- **Date range and timezone.** Every result carries `from`, `to` (echoed back
  exactly as applied; null means unbounded for that end) and `timeZone`.
  All ERP timestamps are stored in UTC and every filter compares UTC values,
  so the timezone is always `"UTC"`. There is no company timezone setting to
  consult; if one is ever introduced, this document and the constant
  `KpiQueryHelpers.KpiTimeZone` must be updated together.
- **Division by zero returns null**, never 0 and never Infinity. A null ratio
  means "no denominator", not "zero performance".
- **Source of truth.** The C# handler named under each KPI is the definition.
  If this document and the handler ever disagree, the handler wins and this
  document must be fixed.

## 1. Waste rate per job order and per period

- Handler: `GetWasteRateKpiQuery` (`Application/Kpis/Queries/WasteRateKpiQuery.cs`).
- Filters: `ProductionOrder.OrderDate` in `[from, to]`; optional `customerId`,
  optional `productionOrderId`.
- Per order:
  - `rawKg` = the order's `RequestedQuantityKg` (0 when the order recorded none).
  - `wasteKg` = the sum over the order's stage executions of
    `InputKg - OutputKg - SecondGradeKg - SeparatesKg`, skipping executions
    whose KG input or output is missing and executions whose difference would
    go negative. This replicates the `WasteKg` definition on
    `ProductionOrderStageExecution` in SQL (`WasteKg` itself is an unmapped
    computed property the database cannot evaluate).
  - `wasteRatePct` = `WasteMath.Percent(wasteKg, rawKg)` - the same function
    the waste report uses, so the two screens can never quote different
    figures for the same order. Null when `rawKg` is 0.
- Per period: `overallRatePct` = `WasteMath.Percent(SUM(wasteKg), SUM(rawKg))`
  over the orders in scope; null when the summed raw is 0.

## 2. Average lead time per job order

- Handler: `GetLeadTimeKpiQuery` (`Application/Kpis/Queries/LeadTimeKpiQuery.cs`).
- Filters: `ProductionOrder.OrderDate` in `[from, to]`; optional `customerId`.
- Per order:
  - `issueDate` = the earliest `InventoryTransaction.TransactionDate` with
    `ProductionOrderId` = the order, `RawMessageId` set and `Direction` = Out
    (the raw-issue OUT ledger row).
  - `completionDate` = the earliest posted (`Status` = Posted)
    `ReadyGoodsTransfer.TransferDate` for the order.
  - `leadTimeDays` = `completionDate - issueDate` in days (1 decimal).
- Only orders that have BOTH dates are counted; orders still in production are
  excluded, never estimated. The result reports the count, the average and the
  min/max (all null when no order finished in the window).

## 3. Machine utilisation per period

- Handler: `GetMachineUtilisationKpiQuery`
  (`Application/Kpis/Queries/MachineUtilisationKpiQuery.cs`).
- Window: `[from, to]` inclusive, defaulting to the current calendar month
  (UTC) when omitted. `daysInPeriod` = inclusive day count.
- Running time: completed stage executions (`Status` = Completed) with both
  `StartedAtUtc` and `CompletedAtUtc` set, clipped to the window; overlap
  hours are summed per machine and per shift. In-progress executions have no
  end yet and are excluded, never estimated.
- Available time: `SUM(HoursPerDay)` over the **active** production shifts
  times `daysInPeriod` (plant denominator, shared by every machine row).
  `HoursPerDay` is editable shift master data (`ProductionShift.HoursPerDay`,
  default 8) - the only available-time source; it was added with a migration
  instead of guessed. Per-shift rows use that shift's own
  `HoursPerDay x daysInPeriod` (0 for inactive shifts).
- `utilisationPct` = `running / available x 100` (2 decimals); null when the
  available time is 0.

## 4. Throughput per stage and per shift

- Handler: `GetThroughputKpiQuery` (`Application/Kpis/Queries/ThroughputKpiQuery.cs`).
- Filters: posted (`Status` = Posted) `ReadyGoodsTransfer` rows with
  `TransferDate` in `[from, to]`.
- The producing stage is resolved through the transfer's `StageExecutionId`
  link to the execution's `StageDefinitionId` (stage code/name from
  `ProductionStageDefinitions`); the shift is the execution's
  `ProductionShiftId`. Transfers whose historical link is null group under
  `"?"` - a link is never fabricated.
- Groups report transfer count and summed `QuantityKg` (null quantities count
  as 0), plus the grand total quantity and transfer count.

## 5. Customer exposure

- Handler: `GetCustomerExposureKpiQuery`
  (`Application/Kpis/Queries/CustomerExposureKpiQuery.cs`).
- Point-in-time (`asOfUtc`); the range is carried as null/null with the
  timezone, because exposure is "what is open now", not a period sum.
- Per customer:
  - `openBalance` = `SUM(Debit - Credit)` over the customer's
    `CustomerLedgerEntry` rows (live aggregation, never a stored balance).
  - Open invoices = `Issued`/`PartiallyPaid` invoices whose remaining balance
    (`lines - Discount + Tax - posted linked receipts`) is positive;
    `openInvoicesTotal` is the sum of those balances.
  - `oldestUnpaidInvoiceAgeDays` = whole days from the oldest such invoice's
    `InvoiceDate` to today. Invoices carry no due date in this system, so age
    is counted from the invoice date.
- Customers with neither a balance nor an open invoice are excluded; an
  optional `customerId` restricts the result to one customer.

## 6. Cheques due in the next N days

- Handler: `GetChequesDueKpiQuery` (`Application/Kpis/Queries/ChequesDueKpiQuery.cs`).
- Window: `[today, today + Days]`, `Days` configurable 1-90 (default 7),
  validated by `GetChequesDueKpiQueryValidator`.
- Rows: customer and supplier cheques with `DueDate` in the window whose
  `Status` is neither Cleared nor Cancelled. Bounced cheques ARE included - a
  bounced cheque is still outstanding until replaced or cancelled.
- Each row carries the kind (Customer/Supplier), the party code and name, the
  due date, the amount, the status and the whole days until due.

## 7. Negative stock overrides and period reopenings in the period

- Handler: `GetGovernanceKpiQuery` (`Application/Kpis/Queries/GovernanceKpiQuery.cs`).
- Overrides: `NegativeStockOverride` rows with `ApprovedAtUtc` in
  `[from, to]`, with the message number, customer and item codes resolved for
  display.
- Reopenings: audit-log entries with `EntityName` = `"PeriodClose"` and
  `Action` = `"Update"` in `[from, to]` (reopening a period is audited as an
  Update by the save-changes interceptor), joined back to the `PeriodClose`
  row for the period dates and the recorded `ReopenedBy`/`ReopenedAtUtc`.
- Both lists report their counts.
