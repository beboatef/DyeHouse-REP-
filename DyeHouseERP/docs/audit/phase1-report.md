# Phase 1 — Baseline & Security Audit

> **Mode:** READ-ONLY. No source, migration or config file was created, modified or deleted by this phase.
> The only artefact written is this report (`docs/audit/phase1-report.md`).
> **Phase status: `Verified`** — the baseline was reproduced and every checklist item below is answered with
> file/line evidence (or explicitly recorded as *Not run* with the reason, never claimed as passed).

Repository: `beboatef/DyeHouse-REP-` (checkout `DyeHouseERP/`)
Audit date: 2026-10-14

---

## A. Baseline (recorded exactly)

| Item | Value |
|---|---|
| Branch | `freebuff/8c663d5dafcf97d023c3991e` |
| HEAD commit | `abb442655c63f7cfaa10811c7714a70e8d15ea57` |
| `git status --porcelain` | **empty** — working tree clean, no local modifications, no untracked files |
| Local `main` | `abb4426` (identical to HEAD's commit; `freebuff-base` branch also present) |
| Other branches | `freebuff-base`, `main`, `remotes/origin/main` |
| `origin/main` | `abb4426` (no divergence) |

Recent history (most recent first):

```
abb4426 feat: finalize raw issue workflow and item unit configuration
541ab8b feat: finalize DyeHouse ERP implementation and release-readiness fixes
589a1af fix: harden accounting period posting concurrency
7b718c4 feat(printing): enforce and trace the mandatory drawing number end to end
33d953a fix: finalize S1-S12 rules and reconcile EF model
d7ec9f1 fix(ui): remove duplicate login branding
fb19a42 checkpoint: current ERP implementation for cloud audit
e93a797 Fix InitialCreate so fresh databases migrate on SQL Server
1c82439 Fix raw message line loading, test JSON contract, and EF model drift
5e7b41b Delete .github/workflows/dotnet.yml
```

The working tree was clean **before and after** every command in this phase (no build/test command writes tracked files;
`bin/`, `obj/`, `node_modules/` and `dist/` are ignored).

---

## B. Build, tests and migrations (actual output)

### B.1 Environment

| Tool | Version available in this workspace |
|---|---|
| .NET SDK | `9.0.318` (matches `global.json` → `9.0.318`) at `$DOTNET_ROOT=/workspace/homes/…/.dotnet` |
| Node / npm | `node v24.21.0`, `npm 11.19.0` |
| `dotnet-ef` | `9.0.21` (global tool, installed into the workspace home) |

### B.2 Backend build — **PASSED**

Command: `dotnet build DyeHouseERP.sln -c Release`

```
Build succeeded.
    7 Warning(s)
    0 Error(s)
Time Elapsed 00:03:00.76        (exit code 0)
```

Warnings (non-blocking, evidence for Phase 8 cleanup):

| File | Warning |
|---|---|
| `src/DyeHouseERP.Persistence/Numbering/SqlAllocationLockService.cs:33` | `CS8618` + `CS0649` — field `_context` never assigned; a **real latent defect** (see §F.7), not just a warning |
| `tests/DyeHouseERP.IntegrationTests/ConcurrencyHardeningTests.cs:372-373` | `xUnit1031` blocking task ops |
| `tests/DyeHouseERP.UnitTests/EditPermissionSweepTests.cs:123` | `CS8602` possible null dereference |
| `tests/DyeHouseERP.UnitTests/ExceptionHandlingMiddlewareTests.cs:144` | `CS8600` / `CS8603` nullability |

### B.3 Backend tests

**Unit tests — PASSED.**
Command: `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build`

```
Passed!  - Failed: 0, Passed: 696, Skipped: 0, Total: 696, Duration: 21 s
```

55 unit-test files (~620 `[Fact]/[Theory]` methods; 696 executed cases). Files directly covering the Phase-1 checklist
areas include `RawAllocationGateHandlerTests`, `RawAllocationGateTests`, `NegativeStockApprovalGatingTests`,
`ConcurrencyAndSecurityHardeningTests`, `PrintingWorkflowTests`, `ReadyGoodsStageTraceabilityTests`,
`ProductionOrderCancellationTests`, `PauseResumeLedgerTests`, `EditPermissionSweepTests`,
`ItemUnitConfigurationTests`, `AccountingPeriodClosureTests`, `LoginRateLimiterTests`, `AuditRedactionTests`.

**Integration tests — `Not run` (blocked, NOT failed and NOT passed).**
`tests/DyeHouseERP.IntegrationTests` boot the real API against a **real SQL Server** via
`DYEHOUSE_TEST_CONNECTION` (`CustomWebApplicationFactory.cs:33-40`). In this workspace:
- `127.0.0.1:1433` is **closed**, `DYEHOUSE_TEST_CONNECTION` is **unset**;
- **no** local `sqlservr`/`sqlcmd`;
- **no** Docker daemon (`docker info` → unavailable), so a container cannot be started here.

The csproj has **no** Testcontainers package (`DyeHouseERP.IntegrationTests.csproj` references only
`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, `FluentAssertions`),
and the test files explicitly document "no Docker and no Testcontainers involved"
(`ChildInsertAndWarehouseGuardSqlTests.cs:31`, `ConcurrencyHardeningTests.cs:32`). Running them here would only
fail at fixture construction with the missing-connection exception, so they are recorded **Not run**.

### B.4 Frontend

| Step | Command | Result |
|---|---|---|
| Install | `npm install --no-package-lock --no-audit --no-fund` | **PASSED** (202 packages, 19 s). `npm ci` **fails** — see finding F.9. |
| Build | `npm run build` (`tsc -b && vite build`) | **PASSED** (exit 0; 2350 modules; `dist/assets/index-*.js` 1.35 MB → chunk-size warning only) |
| Lint | `npm run lint` (`eslint .`) | **FAILED / NOT RUNNABLE** — `sh: 1: eslint: not found` (exit 127). `eslint` is not a dependency and there is **no** eslint config in `src/DyeHouseERP.Web`. |
| Frontend tests | — | **Not run** — no test runner configured (no `vitest`/`jest` in `package.json`; no `*.test.*`/`*.spec.*` files) |

### B.5 Migrations and model drift — **PASSED**

Migrations present: **30** (`InitialCreate` … `20261013191146_AddProductionOrderIssueLink`).
`dotnet ef migrations has-pending-model-changes -p src/DyeHouseERP.Persistence -s src/DyeHouseERP.API --no-build`:

```
No changes have been made to the model since the last migration.   (exit code 0)
```

The EF model matches the latest snapshot; **nothing was applied**, no database was touched.

---

## C. System inventory

### C.1 Solution / layer structure (Clean Architecture)

```
src/DyeHouseERP.Domain/         55 entity files, 1 enum file (SharedEnums.cs), Permissions.cs   — no external deps
src/DyeHouseERP.Application/    202 .cs files — CQRS (MediatR) commands/queries, FluentValidation, DTOs
src/DyeHouseERP.Infrastructure/ auth/current-user, clock, JWT, ReportExportService (QuestPDF/ClosedXML)
src/DyeHouseERP.Persistence/    DbContext, 34 IEntityTypeConfiguration classes, 30 migrations,
                                numbering (sp_getapplock), ledger service, seeders, audit interceptor
src/DyeHouseERP.API/            Program.cs, 41 controllers, JWT auth, Swagger, Serilog, throttling, error middleware
src/DyeHouseERP.Web/            React 18 + TS + Vite + Tailwind, RTL Arabic-first
tests/                          55 unit-test files (696 cases) + 10 integration-test files (SQL Server gated)
tools/                          structural sweeps (edit-permission-audit.cjs, permissions-parity-check.cjs)
```

440 `.cs` files under `src/` (excl. `bin`/`obj`).

### C.2 Modules / features per layer (all live)

| Module | Domain | Application | API controllers |
|---|---|---|---|
| Master data (customers, suppliers, items, warehouses, materials, purchase units, machines, shifts, stages, price lists) | `Customer`, `Supplier`, `Item`, `Warehouse`, `Material`, `PurchaseUnit`, `Machine`, `ProductionShift`, `ProductionStageDefinition`, `CustomerServicePrice` | `Customers/`, `Suppliers/`, `Items/`, `Warehouses/`, `Materials/`, `PurchaseUnits/`, `ProductionMasterData/`, `ProductionStages/`, `PriceLists/` | Customers, Suppliers, Items, Warehouses, Materials, PurchaseUnits, Machines, ProductionShifts, ProductionStages, PriceLists |
| Raw receiving | `RawMessage`, `InventoryTransaction` | `RawReceipts/` | RawMessages |
| Raw allocation / Job Orders | `ProductionOrder`, `RawAllocation`, `ProductionOrderStageExecution`, `ReadyGoodsSource`, `Separate` | `ProductionOrders/` | ProductionOrders, ProductionFloor |
| Formation requests | `FormationRequest`, `FormationGroup`, `FormationBasin`, `FormationSpecTemplate` | `FormationRequests/` | FormationRequests, FormationSpecifications |
| External processing / transfers / adjustments / returns | `RawExternalRelease`, `CustomerTransfer`, `StockAdjustment`, `CustomerReturn`, `NegativeStockOverride` | `RawExternalReleases/`, `CustomerTransfers/`, `StockAdjustments/`, `CustomerReturns/` | RawExternalReleases, CustomerTransfers, StockAdjustments, CustomerReturns |
| Production → Ready goods → Delivery | `ReadyGoodsTransfer`, `Delivery`, `DeliveryLine` | `ReadyGoods/`, `Deliveries/` | ReadyGoods, Deliveries |
| Finance | `Invoice`, `CustomerLedgerEntry`, `Check`, `Treasury` (`TreasuryAccount`,`TreasuryTransaction`,`Receipt`,`Payment`,`TreasuryTransfer`) | `Invoices/`, `Checks/`, `Treasury/` | Invoices, Checks, Treasury (TreasuryAccounts, Receipts, Payments, TreasuryTransfers) |
| Cost accounting / pricing | `CostEntry`, `ProductionOrderServicePrice` | `CostAccounting/` | CostAccounting |
| Purchases | `PurchaseOrder(+Line)`, `PurchaseReceipt(+Line)`, `SupplierInvoice(+Line)`, `SupplierPayment`, `SupplierLedgerEntry` | `Purchases/` | Purchases |
| Payroll & HR | `Department`, `Employee`, `PayrollRun(+Line)`, `HrSection`, `JobPosition`, `CostCenter`, `HrShift`, `AttendanceRule`, `AttendanceEvent`, `LeaveType`, `LeaveRequest`, `OvertimeEntry`, `EmployeeAdvance`, `PayComponent`, `EmployeePayComponent`, `ContributionRule`, `PayrollAccountMapping` | `Payroll/`, (HR inside `Hr/` paths) | Payroll, Hr |
| Supplies & material sales | `SupplyIssue(+Line)`, `MaterialSale(+Line)` | `Supplies/`, `MaterialSales/` | Supplies, MaterialSales |
| Cross-cutting | `ApprovalRequest`, `Attachment`, `AuditLogEntry`, `CompanySettings`, `DocumentSequence`, `PeriodClose`, `SavedReportTemplate`, `User` | `Approvals/`, `Attachments/`, `Audit/`, `Reports/`, `ReportBuilder/`, `Settings/`, `Users/`, `Dashboard/`, `PeriodClosing/` | Approvals, Attachments, AuditLog, Dashboard, Reports, ReportBuilder, Settings, Users, PeriodClosing, Auth |

### C.3 DbSets and EF configuration

- **DbSets:** 80+ `DbSet<>` properties on `IApplicationDbContext` (`src/DyeHouseERP.Application/Common/Interfaces/IApplicationDbContext.cs:11-127`), mirrored by `ApplicationDbContext`.
- **Configurations:** 34 `IEntityTypeConfiguration<>` classes in `src/DyeHouseERP.Persistence/Configurations/`.
- **Decimal precision** (137 `HasPrecision` calls):

| Precision | Count | Typical use |
|---|---|---|
| `(18,3)` | 57 | fabric/quantity KG & Meter |
| `(18,4)` | 45 | prices, cost per unit |
| `(18,2)` | 15 | money |
| `(9,4)` | 10 | percentages |
| `(9,2)` | 9 | percentages |
| `(18,6)` | 1 | a high-precision rate |

- **Delete behaviors:** `Cascade` ×18, `Restrict` ×5, `NoAction` ×2.
- **Unique indexes:** 60 indexes marked `IsUnique` (e.g. `CustomerServicePrices (StageDefinitionId, CustomerId, Unit)`,
  `ProductionOrderServicePrices (ProductionOrderId, StageDefinitionId, Unit)`, `ProductionOrders.IssueRequestId`).
- **Optimistic concurrency:** `[Timestamp] RowVersion` on transactional entities (checks, deliveries, invoices,
  treasury, production order stages, etc.); `DbUpdateConcurrencyException` → HTTP 409 `CONCURRENT_UPDATE`
  (`ExceptionHandlingMiddleware.cs`).

### C.4 API endpoints

**380 endpoints across 41 controllers.** Route/verb/controller/permission table is in **Appendix A**.
Base controllers all carry a class-level `[Authorize]`; each action carries its own permission policy **except** three
(see §F.6). Highlights relevant to later phases:

- `POST api/production-orders/issue` — the canonical Job-Order creation path (raw issue driven; `RawConsume`).
  **There is no `POST api/production-orders` create endpoint anymore.**
- `POST api/production-orders/stage-executions/{id}/{start|complete|skip}` — still present but gated by `RawAllocationGuard` (§F.3).
- `GET api/settings` is `[AllowAnonymous]`; `PUT api/settings` requires `settings.manage`.
- `GET/POST api/attachments/{id}/content` require `attachments.view` (no public static path).

### C.5 Frontend routes and API calls

- **49 routes** in `src/DyeHouseERP.Web/src/App.tsx`. All business pages sit inside `<RequireAuth>`; the four
  `#/print/*` routes and `#/scan/:type/:id` are **also inside `<RequireAuth>`** (lines 103-108). `#/login` and the
  `*` NotFound route are the only unauthenticated routes.
- **40 page components** under `src/DyeHouseERP.Web/src/pages/` (+ `pages/print` ×4, `pages/scan` ×1,
  `pages/purchases/SupplierStatementPanel.tsx`).
- **API layer:** `src/DyeHouseERP.Web/src/api/client.ts` (50 exported API objects, 16 verb helpers) plus
  `documents.ts` and `exports.ts`. Every page calls `*.Api.*` helpers that map 1:1 onto the controllers above;
  server-side authorization is the enforcement point, the UI only mirrors permission checks via
  `src/DyeHouseERP.Web/src/permissions.ts`.
- **No customer-portal routes/pages remain** (removed by migration `20261013083344_RemoveCustomerPortal`).

### C.6 Permission inventory

- **116 permission constants** defined in `src/DyeHouseERP.Domain/Common/Permissions.cs` (the README's "95 constants"
  is stale — see §F.10). Unique strings = 116.
- **Seeded:** the single Development admin gets roles `admin, inventory.allow_negative_stock`
  (`src/DyeHouseERP.Persistence/Numbering/UserSeeder.cs:27`).
- **Enforced:** every controller action maps its own policy; negative-stock override is *additionally* re-checked in
  handlers via `ICurrentUserService.HasPermission(...)` (defence in depth) in `IssueRawMaterialCommand.cs:318`,
  `AllocateRawCommand.cs:191`, `PauseResumeCommands.cs:386`, `CreateRawExternalReleaseCommand.cs:123`,
  `CreateStockAdjustmentCommand.cs:112`, `CreateCustomerTransferCommand.cs:131`.
- **Defined but never enforced anywhere** (Phase 8 dead-permission candidates):

| Constant | Evidence |
|---|---|
| `production.create` | zero references in `src/` |
| `formation.manage_groups` | zero references in `src/` |
| `raw.return` | only `raw.return_cancel` is used (`CustomerReturnsController.cs:55`); `raw.return` itself is unused |

  (`inventory.allow_negative_stock` and `production.resume` are *used* — the former in handlers, the latter as the
  approval-type→permission map in `ApprovalRequestCommands.cs:53` — so they are **not** dead.)
- **Referenced but undefined:** none (the parity sweep `tools/permissions-parity-check.cjs` guards this).

### C.7 Seed data

| Seeder | Content |
|---|---|
| `ProductionStageSeeder` | 21 Arabic stage names (`استلام`, `التشكيل`, `الجاهز`, `الجيت`, `الرام التثبيت`, `الرام التجهيز`, `الفرادة`, `اللف`, `انكماش وتكسير`, `بيتش`, `روتاري`, `سوفلينا`, `غسيل سوفلينا`, `عصارة مفتوح`, `عصارة مقفول`, `كسترا`, `كلندر`, `كومباكتور`, `مثبت حراري`, `مثبت مائي`, `مجفف`) coded `PS001…`, plus `PRINTING / الطباعة` with `requiresDrawingNumber: true`. `التشكيل` → formation stage; `الجاهز` → ready-goods stage. |
| `MachineSeeder` | machines per stage mapping (`PS004` الجيت ×15, `PS005` ×4, `PS006` ×4, `PS020` ×2, `PS019` ×1), codes `MC001…` |
| `ProductionShiftSeeder` | `SHIFT-1` الوردية الأولى, `SHIFT-2`, `SHIFT-3` |
| `WarehouseSeeder` / `PurchaseUnitSeeder` | default warehouses and purchase units |
| `DocumentSequenceSeeder` | per-document-type sequence counters |
| `UserSeeder` | **Development only**: `admin / Admin@12345` (documented; no-op if any user exists) |

All seeders are idempotent and run only when `ASPNETCORE_ENVIRONMENT=Development` (`Program.cs`).

---

## D. Verified-defect hunt

Legend: **Confirmed** = reproduced in code with file+line; **Suspected** = plausible, needs a live DB/browser to confirm;
**Not found** = the checklist defect does not exist here (evidence given).

### D.1 Units

| Checklist item | Verdict | Evidence |
|---|---|---|
| Global kg↔meter conversion | **Not found** | `UnitOfMeasure` is only KG/Meter (`SharedEnums.cs:5`); `ItemUnitGuard` enforces per-item units; allocation never converts (`AllocateRawCommand.cs:24`). The only `ConversionFactor` is on **purchase** units for chemicals (`PurchaseUnits/Commands/PurchaseUnitCommands.cs:20-21`) and is explicitly optional/no-op when absent — it never bridges fabric KG↔Meter. |
| Waste computed across kg and meters | **Not found** | loss/separates are tracked per unit and never summed across dimensions (`CancelProductionOrderCommand.cs:179`, `TransferToNextStageCommand.cs:120`). |
| Billing quantity derived from weight | **Not found** | Delivery/Invoice lines carry explicit `Quantity`/`QuantityKg`/`QuantityMeter` (`CreateInvoiceCommand.cs:30`, `CreateDeliveryCommand.cs:29`); nothing derives a billed qty from weight. |
| "Top/توب" unit | **Not found** | Deliberately absent and documented (`SharedEnums.cs:5`, `ItemsController.cs:16`, `Item.cs:20`). |
| Mandatory bale count | **Not found** | `PieceCount` is optional descriptive data on delivery lines (`DeliveryConfiguration`), never an inventory unit. |

### D.2 Inventory

| Checklist item | Verdict | Evidence |
|---|---|---|
| Direct balance updates without ledger | **Not found** | The only balance source is `InventoryLedgerService` summing `InventoryTransactions` (`InventoryLedgerService.cs`). No mutable balance column exists. |
| Missing idempotency | **Not found** (for the riskiest path) | `IssueRawMaterialCommand` carries a client idempotency token (`RequestId`, line 54-56) checked before writing (line 123-130) with a DB backstop unique index on `ProductionOrders.IssueRequestId` (`ProductionOrderConfiguration.cs:53`). Other ledger paths rely on `sp_getapplock` serialization. |
| Missing concurrency protection | **Not found** | `[Timestamp] RowVersion` + 409 mapping; global stock lock `IAllocationLockService` (`sp_getapplock`) → 409 `LOCK_TIMEOUT`. |
| Negative stock allowed | **Not found** | Blocked and routed to the approval centre; override requires **two** permissions (`InventoryAllowNegativeStock` **and** `InventoryApproveNegativeStock`), e.g. `CreateStockAdjustmentCommand.cs:112`. |
| Same delivery deducting twice | **Not found** | Deliver posts once; cancellation posts a linked compensation row (`DeliveryWorkflowCommands.cs`); `DeliveryCorrectionTests` cover it. |
| Sub-customer permit creating a stock movement | **Not found** | Customer transfers create intentional ledger movements (`CreateCustomerTransferCommand`) but never mutate `RawMessage.CustomerId`; customer ownership is a ledger dimension. |
| Document reprint creating movements | **Not found** | All print/reprint endpoints are `GET …/pdf`/`…/export` that only read (`ProductionOrdersController.cs:36-54`, RawMessages/Invoices/Deliveries PDF handlers). |

### D.3 Production orders

| Checklist item | Verdict | Evidence |
|---|---|---|
| Formation Request required to create a PO | **Not found** (decoupled by design) | Migration `20261013122938_RemoveStageApprovalAndDecoupleFormationRequests`; a Job Order is created by the raw **issue** and may optionally link a Formation Request (`IssueRawMaterialCommand.cs:52,270-275`). Correct per the current spec. |
| PO not created from the issue transaction | **Not found** | No create-PO endpoint exists; `POST api/production-orders/issue` creates the order from the actual issued quantity in one atomic step (`ProductionOrdersController.cs:70-92`, `IssueRawMaterialCommand`). |
| Production starting before 100% allocation | **Not found** | `RawAllocationGuard.EnsureCompleteAsync` is called by start/complete/skip/transfer/ready-goods/complete/resume (`RawAllocationGuard.cs`, `StageExecutionCommands.cs:41,96,140`). |

### D.4 Production

| Checklist item | Verdict | Evidence |
|---|---|---|
| Fixed stage sequence | **Not found** | Stages are admin-configurable rows snapshotted per order; the user picks the next stage (`TransferToNextStageCommand`, `ProductionStageSeeder`). |
| Ready Goods treated as ordinary stage | **Not found** | The stage flagged `isReadyGoodsStage` diverts to the ready-goods warehouse and completes the order (`TransferToNextStageCommand.cs:172-206`). |
| Legacy start/complete/skip endpoints bypassing rules | **Not found** | All three enforce the 100% allocation gate and stage-type requirements (`StageExecutionCommands.cs:41,96,140`); skip additionally needs `definition.AllowSkip` + a reason. |
| `production.approve_stage` still present | **Not found** | Constant absent from `Permissions.cs`; zero references. |
| pause/resume inferring quantity from InputKg | **Not found** | Release quantity is explicitly supplied and bounded by the named lot (`PauseResumeCommands.cs`); the loss baseline reads `lastCompleted.BaselineKg ?? InputKg ?? OutputKg` (`:357`) as a documented fallback, not as an inference of the release quantity. |
| Printing without drawing number | **Not found** | `PRINTING` seeded with `requiresDrawingNumber: true`; server enforces it (`TransferToNextStageCommand.cs:128,139-143`, `stage.EnsureDrawingNumber()`), not just the frontend. |
| Stage flags AllowRepeat/AllowRework/AllowReturn blocking flows | **Not found** | All three removed by migration `20261013145555_RemoveInertStageCapabilityFlags`; no occurrences in live code. |

### D.5 Separates / reprocessing / scrap

| Checklist item | Verdict | Evidence |
|---|---|---|
| Separate treated as waste | **Not found** | `Separate` is its own lifecycle entity, explicitly not loss/waste (`Separate.cs:10-13`). |
| No lifecycle states | **Not found** | `SeparateStatus { …, Reprocessed, Scrapped }`; transitions guarded (`Separate.cs:75-81`). |
| Reprocessing not creating linked PO / deducting raw twice | **Not found** | `ReprocessSeparateCommand` creates a **new** order linked via `ReprocessingOfProductionOrderId` and draws from the separate, not a customer lot. |
| Reprocessing / separate costs auto-billed | **Not found** | Invoices contain only explicitly entered lines (`CreateInvoiceCommand`); no reprocess/cost-entry auto-add. |
| Scrap without `production.scrap` or reason | **Not found** (reason) / **scope** (permission) | Scrap requires a non-empty reason (`ScrapSeparateCommandValidator`, `Separate.Scrap(reason,…)`). Scrap is gated by `production.edit` (`SeparatesController.cs:35-38`), **not** a dedicated `production.scrap` permission — the constant does not exist. Flag as a scope question (§J.3). |
| Scrap + reprocess race | **Not found** | `Separate` guards "already Reprocessed/Scrapped" (`Separate.cs:77`) and carries a RowVersion token (`Separate.cs:32` comment). |

### D.6 Costing / pricing

| Checklist item | Verdict | Evidence |
|---|---|---|
| Pricing key not (PO + Stage + Unit) | **Not found** | `ProductionOrderServicePrices` unique index = `(ProductionOrderId, StageDefinitionId, Unit)` (`PriceListConfiguration.cs:55`). |
| StageCostRate as authoritative cost | **Not found** | Removed by migration `20261008215639_RemoveStageCostRate` (model entity gone). |
| Customer-owned raw counted as factory cost | **Not found** | Cost query only values factory-owned material issues (`GetProductionOrderCostQuery.cs:19`). `CostPerKg` divides by `outputKg` only (`:125`). |

### D.7 Delivery / returns

| Checklist item | Verdict | Evidence |
|---|---|---|
| Original PO quantity overwritten on partial delivery | **Not found** | `UpdateProductionOrderCommand` cannot change requested quantity (`ProductionOrdersController.cs:96-110`); delivery commands never touch `ProductionOrder.RequestedQuantity*`. |
| PO date replaced by delivery date | **Not found** | `Delivery` holds its own `DeliveryDate`; no write-back to the order date. |
| Returns merged with delivery / auto credit-notes / auto new-PO | **Not found** | `CreateCustomerReturnCommand` only links to an existing PO for validation; it creates **no** invoice, credit note, delivery or new PO. |
| Missing customer/company account choice | **Not found** | Treasury receipts/payments/transfers require an explicit account (`CreateTreasuryTransferCommand`, `ReceiptsController`/`PaymentsController`). |

### D.8 Finance

| Checklist item | Verdict | Evidence |
|---|---|---|
| Reprocessing costs auto-added to invoices | **Not found** | `CreateInvoiceCommand` adds only the supplied lines. |
| Endorsed cheque creating fake cash in/out | **Not found** | Endorsement only appends a `CheckMovement` and re-points the holder; the **only** transition that writes a `TreasuryTransaction` is `Clear` (`CheckCommands.cs:276`). |
| Duplicate settlement possible | **Not found** | `Check` carries `[Timestamp] RowVersion` (`Check.cs`) so two concurrent clears cannot both post money; clear/bounce are terminal. |

### D.9 Documents

| Checklist item | Verdict | Evidence |
|---|---|---|
| Hard-coded company name/logo in templates | **CONFIRMED** | `ReportExportService.GeneratePdf` writes the literal footer `"DyeHouse ERP - "` (`src/DyeHouseERP.Infrastructure/Services/ReportExportService.cs:73`) instead of reading `CompanySettings`, and renders **no logo**. Fix target for Phase 8. |
| Missing logo breaking PDF | **Not found** (no logo is referenced at all) | Same file — no logo path exists, so a missing logo cannot break generation; but the exploit the checklist worries about is the *inverse*: branding is simply absent. |
| Arabic/RTL broken in PDF | **Suspected / architectural gap** | `ReportExportService.GeneratePdf` registers **no** Arabic-capable font (only an inline warning comment, `:38-46`) and does not set document direction; on a bare Linux host Arabic will render as boxes. `GenerateExcel` likewise never sets `RightToLeft` sheet direction. Confirming visually needs a generated PDF — **Not run** here. |

### D.10 Other real defects found

| # | Severity | Finding | Evidence |
|---|---|---|---|
| F.7 | **Medium** | `SqlAllocationLockService._context` is declared but **never assigned** (`CS8618`/`CS0649`). If the class relies on it at runtime the stock lock is at risk — must be checked in Phase 3/8. | `src/DyeHouseERP.Persistence/Numbering/SqlAllocationLockService.cs:33` |
| F.8 | **Medium** | Attachment upload has **no content-type/extension allowlist**; any file (incl. HTML/SVG) is stored and later served back with the client-supplied `ContentType` (`File(content, contentType, fileName)`). Only size (25 MB) is validated. | `AttachmentCommands.cs:24-38`, `AttachmentsController.cs:37-40` |
| F.9 | **Low** | `npm ci` fails — `package-lock.json` is out of sync with `package.json` (missing `@rollup/rollup-*` optional deps). A clean CI install cannot use `npm ci`. | `npm ci` → `EUSAGE` (see §B.4) |
| F.10 | **Low** | README states "95 permission constants"; the code defines **116**. Documentation drift. | `Permissions.cs` vs `README.md` |
| F.11 | **Low** | Generated PDF/Excel chunk is 1.35 MB (single bundle) — Vite chunk-size warning; code-splitting deferred to Phase 8. | §B.4 build output |
| F.12 | **Low** | Duplicated/orphaned XML doc-comment above `Export` in `ProductionOrdersController` ("Printable production order PDF…" left over) — cosmetic. | `ProductionOrdersController.cs:37-42` |
| F.13 | **Info** | `appsettings.Testing.json` ships a real-looking test JWT key; harmless (Testing env only) but it *is* a committed key value. | `src/DyeHouseERP.API/appsettings.Testing.json` |
| F.14 | **Info** | README "Honest gaps"/doc text still says "no EF migrations exist yet" in `CustomWebApplicationFactory` comment, though migrations now exist — comment drift. | `CustomWebApplicationFactory.cs:24-26` |

### D.11 Security checklist

| Checklist item | Verdict | Evidence |
|---|---|---|
| Weak/fallback JWT secret | **Not found** | No fallback; `JwtKeyValidator` rejects null/blank/whitespace-padded, `CHANGE_ME…` prefix, known placeholder words, and, in Production, keys <16 chars (`JwtKeyValidator.cs`). `Program.cs` throws when unset (except EF design-time). |
| Hard-coded secrets | **Not found** (only test key, F.13) | Main config ships the `CHANGE_ME…` placeholder which the validator **refuses** to sign with (`appsettings.json`). |
| JWT lifetime ≠ 8h | **Not found** | `DateTime.UtcNow.AddHours(8)` (`JwtTokenService.cs:28`). |
| Refresh tokens present | **Not found** | zero occurrences of "refresh" in `src/`. |
| Token not invalidated on password change/deactivation/permission change | **Not found** | `OnTokenValidated` re-checks live user state, active flag **and** a role-claim diff on every request (`Program.cs`). |
| No login throttling | **Not found** | `LoginRateLimiter` + `LoginThrottleMiddleware`: per-(IP,username) and per-IP budgets, case-insensitive key, 4 KB body cap, `Retry-After`, no user enumeration. |
| Endpoints without permission checks | **Suspected (by design)** | 3 endpoints have no permission: `POST api/auth/login` (anonymous by design), `GET api/settings` (`[AllowAnonymous]`, documented: login page needs branding), `POST api/approvals` (submission; the **handler** enforces the owning module's permission per `ApprovalsController` doc). Recommend re-confirming `POST api/approvals` in Phase 8. |
| Privilege escalation (granting permissions you lack) | **Not found** | Self-permission change refused; last-admin cannot be demoted/deactivated; deactivation self-lockout refused (`UpdateUserCommand.cs:61,76`, `DeactivateUserCommand.cs:30,42`). |
| Last-admin race | **Partially addressed** | App-level guards exist (§ above). A DB-level uniqueness/serialization for the last-admin check is not evident — **Suspected**, verify in Phase 8 under concurrency. |
| Password change without current password | **Scope note** | Only an admin-driven `POST api/users/{id}/change-password` exists (`UsersManage`); there is **no** self-service endpoint, so no "current password" check is required — acceptable, but a self-service flow is a candidate for Phase 8. |
| Forwarded headers trusted blindly | **Not found** | Only explicit `ReverseProxy:KnownProxies`; default loopback; `RequireHeaderSymmetry = true`; no `KnownNetworks` (`Program.cs`). |
| CORS wildcard | **Not found** | `WithOrigins(config)` restricted to configured origins, default `http://localhost:5173`; never `AllowAnyOrigin` (`Program.cs`). |
| Swagger outside Development | **Not found** | `if (app.Environment.IsDevelopment())` (`Program.cs`). |
| Stack traces / SQL in responses | **Not found** | `ExceptionHandlingMiddleware` maps each exception to a generic envelope; default 500 = "An unexpected error occurred."; unique-violation branch strips SQL text. |
| Unsafe file upload | **CONFIRMED (F.8)** | no content-type/extension allowlist. |
| Attachments publicly reachable | **Not found** | stored in DB, served only via `GET api/attachments/{id}/content` behind `attachments.view`; no static path. |
| `/scan/:type/:id` without authorization | **Not found** | route is inside `<RequireAuth>` (`App.tsx:108`) and its data calls the authorized APIs (`ScanViewPage.tsx:45,77,91,105`). |
| Customer-portal routes/pages/controllers/permissions (frontend AND backend) | **Not found** | migration `20261013083344_RemoveCustomerPortal`; no portal routes/controllers/permissions remain. |
| Audit interceptor present? | **Yes** | `AuditSaveChangesInterceptor` writes an `AuditLogEntry` for every Added/Modified/Deleted entity. |
| Failures swallowed? | **Not found** | interceptor does not swallow; middleware logs unhandled exceptions. |
| Passwords logged? | **Not found** | `AuditValueRedactor` masks `*Password*`, `*Secret*`, `*Token*`, `*ApiKey*`; `byte[]` → `[REDACTED size N]`. Login success/failure are audited without password material (`LoginCommand.cs:54,63`). |
| `InvariantGlobalization` enabled | **Not found** | not set in any `.csproj`/`.json` — good (globalization/RTL supported). |
| Docker / Testcontainers referenced | **Not found (by design)** | no Dockerfile/compose; integration tests deliberately use an external SQL Server via env var; no Testcontainers package. |

---

## E. Proposed classification (Keep / Modify / Rebuild / Delete)

| Component | Classification | Evidence / reason |
|---|---|---|
| Domain entities + invariants (append-only ledgers, per-unit quantities, reversal-not-edit) | **Keep** | coherent, backed by 696 passing unit tests |
| Inventory ledger + `IAllocationLockService` (`sp_getapplock`) | **Keep (verify F.7)** | `_context` never assigned in `SqlAllocationLockService` must be checked |
| Permissions model + `PermissionAuthorizationHandler` + parity tool | **Keep** | server-enforced, 116 constants, parity sweep present |
| Auth pipeline (JWT, throttle, forwarded headers, session revalidation) | **Keep** | hardened, no fallback secret |
| Exception middleware + audit interceptor | **Keep** | consistent envelopes + redaction |
| Production stage engine (configurable stages, ready-goods diversion, drawing number) | **Keep** | matches spec |
| Production order creation via raw issue (+ idempotency token) | **Keep** | removes the old double-OUT risk |
| Formation requests (decoupled) | **Keep** | recent migration |
| Separates / reprocessing / scrap | **Modify** | add a dedicated `production.scrap` permission? (scope question §J.3) |
| Delivery / invoice / treasury / checks workflows | **Keep** | well-guarded; RowVersion + reversals |
| Attachments upload/download | **Modify** | add content-type/extension allowlist (F.8) |
| `ReportExportService` (PDF/Excel) | **Rebuild (Phase 8)** | hard-coded company name, no logo, no Arabic font/RTL (F.D.9) |
| `package-lock.json` + frontend tooling (eslint missing, no test runner) | **Modify** | fix lockfile for `npm ci`; add eslint config or drop the script; decide on a frontend test runner |
| Customer portal | **Delete (already done)** | migration present; verify no stragglers in Phase 8 |
| Dead permissions (`production.create`, `formation.manage_groups`, `raw.return`) | **Delete or wire** | Phase 8 decision |
| Integration tests | **Keep** | correct design (real SQL Server); need a DB in CI |

---

## F. Suggested scope per phase (what Phases 2–8 will actually touch)

- **Phase 2 — Master data & factory settings:** item unit config, warehouses, stages/machines/shifts, price lists,
  Company Settings (**must feed Phase 8 PDF branding**). No rebuild needed.
- **Phase 3 — Receiving, formation & raw allocation:** verify `RawAllocationGuard` coverage, `SqlAllocationLockService`
  (F.7), ownership dimension on transfers.
- **Phase 4 — Production, separates & reprocessing:** decide on `production.scrap`; confirm ready-goods diversion and
  drawing-number enforcement in DB-backed tests.
- **Phase 5 — Ready goods, delivery & returns:** confirm partial-delivery corrections + reversals against a real DB.
- **Phase 6 — Finance, invoicing, treasury & cheques:** confirm cheque clear/bounce concurrency (`RowVersion`) and
  reversal integrity against a real DB.
- **Phase 7 — Purchasing, materials, HR & payroll:** largest untouched surface; confirm purchase quantity correction
  (`purchases.edit_after_receipt`) and payroll posting periods.
- **Phase 8 — Reports, UI, exports & cleanup:** **fix `ReportExportService`** (company name from `CompanySettings`,
  logo, Arabic font + RTL), fix `npm ci`/eslint, prune dead permissions, remove README/comment drift, add ISO
  content-type allowlist for uploads.
- **Phase 9 — Final verification:** rerun builds + unit tests; run the integration suite against a provisioned SQL Server.

---

## G. Blocking business questions (genuine only)

1. **`production.scrap` permission** — the checklist names a `production.scrap` right, but the code gates scrap with
   `production.edit`. Should a dedicated scrap permission be introduced, or is `production.edit` the intended gate?
2. **Self-service password change** — should any user be allowed to change their own password (with a current-password
   check), or is admin-driven reset the only intended flow?
3. **Job order ↔ formation request coupling** — the code now creates a Job Order from the raw **issue** and only
   *optionally* links a Formation Request. Confirm this is the intended current workflow (the main prompt's checklist
   lists both "Formation Request required to create a PO" and "PO not created from the issue transaction" as defects —
   the two are mutually exclusive and the code chose the issue-driven model).
4. **PDF Arabic font** — should an Arabic font (e.g. Noto Naskh Arabic) be **embedded** in the deployment, or is a
   host-provided font acceptable? This decides the Phase 8 export rebuild.
5. **SQL Server for CI/verification** — should a verification database be provisioned for the integration suite, or is
   unit-test coverage sufficient for sign-off?

---

## H. Phase 1 status

**`Verified`.** Baseline reproduced on `abb4426` with a clean tree; backend build + 696/696 unit tests pass; frontend
build passes; model matches the latest migration snapshot (no pending changes). Every checklist item above is answered
with file/line evidence, and the integration suite + frontend lint/tests are recorded **Not run** with their reasons —
never as passes.

---

## Appendix A — Full API endpoint inventory (380 endpoints)

Generated from the controller source. `Permission` is the policy permission on the action (class-level `[Authorize]` always applies). `NONE` = action carries no permission check.

| Route | Verb | Controller | Permission | Handler |
|---|---|---|---|---|
| `api/approvals` | GET | ApprovalsController | ApprovalsView | ? |
| `api/approvals/requests` | GET | ApprovalsController | ApprovalsView | ? |
| `api/approvals` | POST | ApprovalsController | NONE | ? |
| `api/approvals/{id:guid}/approve` | POST | ApprovalsController | InventoryApproveNegativeStock | ? |
| `api/approvals/{id:guid}/reject` | POST | ApprovalsController | InventoryApproveNegativeStock | ? |
| `api/attachments` | GET | AttachmentsController | AttachmentsView | ? |
| `api/attachments/{id:guid}/content` | GET | AttachmentsController | AttachmentsView | Download |
| `api/attachments` | POST | AttachmentsController | AttachmentsManage | ? |
| `api/attachments/{id:guid}` | PUT | AttachmentsController | AttachmentsManage | ? |
| `api/attachments/{id:guid}` | DELETE | AttachmentsController | AttachmentsManage | Delete |
| `api/audit-log` | GET | AuditLogController | AuditView | ? |
| `api/auth/login` | POST | AuthController | NONE | ? |
| `api/checks` | GET | ChecksController | ChecksView | ? |
| `api/checks/{id:guid}` | GET | ChecksController | ChecksView | ? |
| `api/checks/summary` | GET | ChecksController | ChecksView | ? |
| `api/checks/customer-checks` | POST | ChecksController | ChecksCreate | ? |
| `api/checks/supplier-checks` | POST | ChecksController | ChecksCreate | ? |
| `api/checks/{id:guid}/confirm-receipt` | POST | ChecksController | ChecksEdit | ? |
| `api/checks/{id:guid}/endorse` | POST | ChecksController | ChecksEndorse | ? |
| `api/checks/{id:guid}/deposit` | POST | ChecksController | ChecksDeposit | ? |
| `api/checks/{id:guid}/clear` | POST | ChecksController | ChecksClear | ? |
| `api/checks/{id:guid}/bounce` | POST | ChecksController | ChecksBounce | ? |
| `api/checks/{id:guid}/return` | POST | ChecksController | ChecksEdit | ? |
| `api/checks/{id:guid}/cancel` | POST | ChecksController | ChecksCancel | ? |
| `api/checks/export/excel` | GET | ChecksController | ChecksExport | ExportExcel |
| `api/checks/export/pdf` | GET | ChecksController | ChecksExport | ExportPdf |
| `api/checks/{id:guid}/pdf` | GET | ChecksController | ChecksExport | GetPdf |
| `api/production-orders/{productionOrderId:guid}/cost` | GET | CostAccountingController | ProductionView | ? |
| `api/production-orders/{productionOrderId:guid}/cost/entries` | POST | CostAccountingController | ProductionEdit | ? |
| `api/production-orders/{productionOrderId:guid}/cost/estimate` | PUT | CostAccountingController | CostingEditEstimate | ? |
| `api/production-orders/{productionOrderId:guid}/cost/approve` | POST | CostAccountingController | CostingApprove | ? |
| `api/production-orders/{productionOrderId:guid}/cost/service-price` | POST | CostAccountingController | PricingManage | ? |
| `api/production-orders/{productionOrderId:guid}/cost/profitability` | GET | CostAccountingController | CostingView | ? |
| `api/production-orders/{productionOrderId:guid}/cost/export` | GET | CostAccountingController | CostingView | Export |
| `api/customer-returns` | GET | CustomerReturnsController | InventoryView | ? |
| `api/customer-returns/{id:guid}` | GET | CustomerReturnsController | InventoryView | ? |
| `api/customer-returns` | POST | CustomerReturnsController | InventoryEdit | ? |
| `api/customer-returns/{id:guid}/cancel` | POST | CustomerReturnsController | RawReturnCancel | ? |
| `api/customer-transfers/export` | GET | CustomerTransfersController | ReportsExport | Export |
| `api/customer-transfers` | GET | CustomerTransfersController | RawTransfer | ? |
| `api/customer-transfers` | POST | CustomerTransfersController | RawTransfer | ? |
| `api/[controller]` | GET | CustomersController | CustomersView | ? |
| `api/[controller]` | POST | CustomersController | CustomersCreate | ? |
| `api/[controller]/{id:guid}` | PUT | CustomersController | CustomersEdit | ? |
| `api/[controller]/{id:guid}/active` | POST | CustomersController | CustomersEdit | ? |
| `api/[controller]/export/excel` | GET | CustomersController | ReportsExport | ExportExcel |
| `api/[controller]/export` | GET | CustomersController | ReportsExport | Export |
| `api/[controller]/import/template` | GET | CustomersController | CustomersView | ImportTemplate |
| `api/[controller]/import/preview` | POST | CustomersController | CustomersCreate | ? |
| `api/[controller]/import/execute` | POST | CustomersController | CustomersCreate | ? |
| `api/[controller]/import/preview-update` | POST | CustomersController | CustomersEdit | ? |
| `api/[controller]/import/execute-update` | POST | CustomersController | CustomersEdit | ? |
| `api/dashboard/summary` | GET | DashboardController | ProductionView | ? |
| `api/deliveries` | GET | DeliveriesController | ReadyView | ? |
| `api/deliveries/{id:guid}` | GET | DeliveriesController | ReadyView | ? |
| `api/deliveries/export` | GET | DeliveriesController | ReportsExport | Export |
| `api/deliveries/{id:guid}/pdf` | GET | DeliveriesController | ReportsExport | GetDeliveryPdf |
| `api/deliveries` | POST | DeliveriesController | ReadyDeliver | ? |
| `api/deliveries/{id:guid}/prepare` | POST | DeliveriesController | ReadyDeliver | ? |
| `api/deliveries/{id:guid}/deliver` | POST | DeliveriesController | ReadyDeliver | ? |
| `api/deliveries/{id:guid}/cancel` | POST | DeliveriesController | ReadyDeliver | ? |
| `api/deliveries/{id:guid}/lines/corrections` | POST | DeliveriesController | ReadyEditPostApproval | ? |
| `api/formation-requests` | GET | FormationRequestsController | FormationView | ? |
| `api/formation-requests/{id:guid}` | GET | FormationRequestsController | FormationView | ? |
| `api/formation-requests/{id:guid}/traceability` | GET | FormationRequestsController | FormationViewTraceability | ? |
| `api/formation-requests` | POST | FormationRequestsController | FormationCreate | ? |
| `api/formation-requests/{id:guid}` | PUT | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/{id:guid}/submit` | POST | FormationRequestsController | FormationSubmit | ? |
| `api/formation-requests/{id:guid}/approve` | POST | FormationRequestsController | FormationApprove | ? |
| `api/formation-requests/{id:guid}/reject` | POST | FormationRequestsController | FormationReject | ? |
| `api/formation-requests/{id:guid}/cancel` | POST | FormationRequestsController | FormationCancel | ? |
| `api/formation-requests/{id:guid}/revise` | POST | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/{id:guid}` | DELETE | FormationRequestsController | FormationDelete | Delete |
| `api/formation-requests/{id:guid}/basins/{basinId:guid}/actual-quantity` | POST | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/{id:guid}/start-production` | POST | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/{id:guid}/groups/{groupId:guid}/production` | POST | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/{id:guid}/basins/{basinId:guid}/production` | POST | FormationRequestsController | FormationEdit | ? |
| `api/formation-requests/export/excel` | GET | FormationRequestsController | FormationExport | ExportExcel |
| `api/formation-requests/export` | GET | FormationRequestsController | FormationExport | Export |
| `api/formation-requests/{id:guid}/pdf` | GET | FormationRequestsController | FormationPrint | GetPdf |
| `api/formation-specifications` | GET | FormationSpecificationsController | FormationView | ? |
| `api/formation-specifications` | POST | FormationSpecificationsController | FormationManageSpecifications | ? |
| `api/formation-specifications/{id:guid}` | PUT | FormationSpecificationsController | FormationManageSpecifications | ? |
| `api/formation-specifications/{id:guid}/active` | POST | FormationSpecificationsController | FormationManageSpecifications | ? |
| `api/hr/sections` | GET | HrController | HrView | ? |
| `api/hr/sections` | POST | HrController | HrManage | ? |
| `api/hr/sections/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/jobs` | GET | HrController | HrView | ? |
| `api/hr/jobs` | POST | HrController | HrManage | ? |
| `api/hr/jobs/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/cost-centers` | GET | HrController | HrView | ? |
| `api/hr/cost-centers` | POST | HrController | HrManage | ? |
| `api/hr/cost-centers/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/shifts` | GET | HrController | HrView | ? |
| `api/hr/shifts` | POST | HrController | HrManage | ? |
| `api/hr/shifts/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/attendance-rules` | GET | HrController | HrView | ? |
| `api/hr/attendance-rules` | POST | HrController | HrManage | ? |
| `api/hr/attendance-rules/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/leave-types` | GET | HrController | HrView | ? |
| `api/hr/leave-types` | POST | HrController | HrManage | ? |
| `api/hr/leave-types/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/pay-components` | GET | HrController | HrView | ? |
| `api/hr/pay-components` | POST | HrController | HrManage | ? |
| `api/hr/pay-components/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/employee-pay-components` | GET | HrController | HrView | ? |
| `api/hr/employee-pay-components` | POST | HrController | HrManage | ? |
| `api/hr/employee-pay-components/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/employee-pay-components/{id:guid}` | DELETE | HrController | HrManage | RemoveEmployeePayComponent |
| `api/hr/contribution-rules` | GET | HrController | HrView | ? |
| `api/hr/contribution-rules` | POST | HrController | HrManage | ? |
| `api/hr/contribution-rules/{id:guid}` | PUT | HrController | HrManage | ? |
| `api/hr/account-mappings` | GET | HrController | PayrollAccounting | ? |
| `api/hr/account-mappings` | POST | HrController | PayrollAccounting | ? |
| `api/hr/employees/{id:guid}/profile` | PUT | HrController | PayrollManageEmployees | ? |
| `api/hr/attendance` | GET | HrController | HrView | ? |
| `api/hr/attendance/summary` | GET | HrController | HrView | ? |
| `api/hr/attendance` | POST | HrController | HrAttendance | ? |
| `api/hr/attendance/non-working` | POST | HrController | HrAttendance | ? |
| `api/hr/attendance/generate` | POST | HrController | HrAttendance | ? |
| `api/hr/attendance/{id:guid}` | DELETE | HrController | HrAttendance | RemoveAttendance |
| `api/hr/leave-requests` | GET | HrController | HrView | ? |
| `api/hr/leave-requests` | POST | HrController | HrLeaveRequest | ? |
| `api/hr/leave-requests/{id:guid}/approve` | POST | HrController | HrLeaveApprove | ? |
| `api/hr/leave-requests/{id:guid}/reject` | POST | HrController | HrLeaveApprove | ? |
| `api/hr/leave-requests/{id:guid}/cancel` | POST | HrController | HrLeaveRequest | ? |
| `api/hr/overtime` | GET | HrController | HrView | ? |
| `api/hr/overtime` | POST | HrController | HrOvertimeRequest | ? |
| `api/hr/overtime/{id:guid}/approve` | POST | HrController | HrOvertimeApprove | ? |
| `api/hr/overtime/{id:guid}/reject` | POST | HrController | HrOvertimeApprove | ? |
| `api/hr/advances` | GET | HrController | HrView | ? |
| `api/hr/advances` | POST | HrController | HrAdvancesManage | ? |
| `api/hr/advances/{id:guid}/cancel` | POST | HrController | HrAdvancesManage | ? |
| `api/hr/labor-cost` | GET | HrController | HrLaborCost | ? |
| `api/hr/labor-cost/export` | GET | HrController | HrLaborCost | ExportLaborCost |
| `api/invoices` | GET | InvoicesController | InvoicesView | ? |
| `api/invoices/{id:guid}` | GET | InvoicesController | InvoicesView | ? |
| `api/invoices/export` | GET | InvoicesController | ReportsExport | Export |
| `api/invoices/{id:guid}/pdf` | GET | InvoicesController | ReportsExport | GetInvoicePdf |
| `api/invoices` | POST | InvoicesController | InvoicesCreate | ? |
| `api/invoices/{id:guid}` | PUT | InvoicesController | InvoicesEdit | ? |
| `api/invoices/{id:guid}/lines` | POST | InvoicesController | InvoicesEdit | ? |
| `api/invoices/{id:guid}/lines/{lineId:guid}` | PUT | InvoicesController | InvoicesEdit | ? |
| `api/invoices/{id:guid}/lines/{lineId:guid}` | DELETE | InvoicesController | InvoicesEdit | ? |
| `api/invoices/{id:guid}/issue` | POST | InvoicesController | InvoicesIssue | ? |
| `api/invoices/{id:guid}/cancel` | POST | InvoicesController | InvoicesCancel | ? |
| `api/customers/{customerId:guid}/statement` | GET | CustomerStatementController | InvoicesView | ? |
| `api/customers/{customerId:guid}/statement/pdf` | GET | CustomerStatementController | ReportsExport | GetPdf |
| `api/customers/{customerId:guid}/statement/excel` | GET | CustomerStatementController | ReportsExport | GetExcel |
| `api/[controller]` | GET | ItemsController | ItemsView | ? |
| `api/[controller]` | POST | ItemsController | ItemsCreate | ? |
| `api/[controller]/{id:guid}` | PUT | ItemsController | ItemsEdit | ? |
| `api/[controller]/export/excel` | GET | ItemsController | ReportsExport | ExportExcel |
| `api/[controller]/export` | GET | ItemsController | ReportsExport | Export |
| `api/[controller]/import/template` | GET | ItemsController | ItemsView | ImportTemplate |
| `api/[controller]/import/preview` | POST | ItemsController | ItemsCreate | ? |
| `api/[controller]/import/execute` | POST | ItemsController | ItemsCreate | ? |
| `api/[controller]/import/execute-update` | POST | ItemsController | ItemsEdit | ? |
| `api/[controller]/import/preview-update` | POST | ItemsController | ItemsEdit | ? |
| `api/machines` | GET | MachinesController | ProductionView | ? |
| `api/machines/stage-links` | GET | MachinesController | ProductionView | ? |
| `api/machines` | POST | MachinesController | ProductionManageMachines | ? |
| `api/machines/{id:guid}` | PUT | MachinesController | ProductionManageMachines | ? |
| `api/machines/{id:guid}/activation` | POST | MachinesController | ProductionManageMachines | ? |
| `api/machines/{id:guid}/stages` | PUT | MachinesController | ProductionManageMachines | ? |
| `api/material-sales` | GET | MaterialSalesController | MaterialSalesView | ? |
| `api/material-sales/{id:guid}` | GET | MaterialSalesController | MaterialSalesView | ? |
| `api/material-sales/export` | GET | MaterialSalesController | ReportsExport | Export |
| `api/material-sales/{id:guid}/pdf` | GET | MaterialSalesController | ReportsExport | GetPdf |
| `api/material-sales` | POST | MaterialSalesController | MaterialSalesCreate | ? |
| `api/material-sales/{id:guid}/lines` | POST | MaterialSalesController | MaterialSalesCreate | ? |
| `api/material-sales/{id:guid}/lines/{lineId:guid}` | DELETE | MaterialSalesController | MaterialSalesCreate | ? |
| `api/material-sales/{id:guid}/terms` | PUT | MaterialSalesController | MaterialSalesCreate | ? |
| `api/material-sales/{id:guid}/post` | POST | MaterialSalesController | MaterialSalesPost | ? |
| `api/material-sales/{id:guid}/cancel` | POST | MaterialSalesController | MaterialSalesCancel | ? |
| `api/materials` | GET | MaterialsController | ItemsView | ? |
| `api/materials` | POST | MaterialsController | ItemsCreate | ? |
| `api/materials/export` | GET | MaterialsController | ReportsExport | Export |
| `api/materials/import/template` | GET | MaterialsController | ItemsView | ImportTemplate |
| `api/materials/import/preview` | POST | MaterialsController | ItemsCreate | ? |
| `api/materials/import/execute` | POST | MaterialsController | ItemsCreate | ? |
| `api/materials/import/preview-update` | POST | MaterialsController | ItemsEdit | ? |
| `api/materials/import/execute-update` | POST | MaterialsController | ItemsEdit | ? |
| `api/material-transfers` | GET | MaterialTransfersController | InventoryView | ? |
| `api/material-transfers` | POST | MaterialTransfersController | InventoryAdjust | ? |
| `api/material-transfers/export` | GET | MaterialTransfersController | ReportsExport | Export |
| `api/material-issues` | GET | MaterialIssuesController | ProductionView | ? |
| `api/material-issues` | POST | MaterialIssuesController | ProductionExecuteStage | ? |
| `api/material-issues/{id:guid}/cancel` | POST | MaterialIssuesController | ProductionIssueMaterialCancel | ? |
| `api/material-issues/export` | GET | MaterialIssuesController | ReportsExport | Export |
| `api/material-preparations` | GET | MaterialPreparationsController | ProductionView | ? |
| `api/material-preparations` | POST | MaterialPreparationsController | ProductionExecuteStage | ? |
| `api/material-preparations/export` | GET | MaterialPreparationsController | ReportsExport | Export |
| `api/[controller]/departments` | GET | PayrollController | PayrollView | ? |
| `api/[controller]/departments` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/departments/{id:guid}` | PUT | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees` | GET | PayrollController | PayrollView | ? |
| `api/[controller]/employees/{id:guid}` | GET | PayrollController | PayrollView | ? |
| `api/[controller]/employees` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees/{id:guid}` | PUT | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees/export` | GET | PayrollController | ReportsExport | ExportEmployees |
| `api/[controller]/employees/import/template` | GET | PayrollController | PayrollView | GetEmployeeImportTemplate |
| `api/[controller]/employees/import/preview` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees/import/execute` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees/import/preview-update` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/employees/import/execute-update` | POST | PayrollController | PayrollManageEmployees | ? |
| `api/[controller]/runs` | GET | PayrollController | PayrollView | ? |
| `api/[controller]/runs/{id:guid}` | GET | PayrollController | PayrollView | ? |
| `api/[controller]/runs` | POST | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/lines/{lineId:guid}` | PUT | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/lines/{lineId:guid}` | DELETE | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/account` | PUT | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/approve` | POST | PayrollController | PayrollApprove | ? |
| `api/[controller]/runs/{id:guid}/post` | POST | PayrollController | PayrollPost | ? |
| `api/[controller]/runs/{id:guid}/cancel` | POST | PayrollController | PayrollCancel | ? |
| `api/[controller]/runs/{id:guid}/calculate` | POST | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/submit` | POST | PayrollController | PayrollCreate | ? |
| `api/[controller]/runs/{id:guid}/return` | POST | PayrollController | PayrollApprove | ? |
| `api/[controller]/runs/{id:guid}/reverse` | POST | PayrollController | PayrollCancel | ? |
| `api/[controller]/runs/{id:guid}/payslips/{lineId:guid}/pdf` | GET | PayrollController | PayrollView | GetPayslipPdf |
| `api/[controller]/runs/{id:guid}/pdf` | GET | PayrollController | PayrollView | GetRunPdf |
| `api/[controller]/runs/{id:guid}/excel` | GET | PayrollController | PayrollView | GetRunExcel |
| `api/period-closes` | GET | PeriodClosingController | SettingsManage | ? |
| `api/period-closes` | POST | PeriodClosingController | SettingsManage | ? |
| `api/period-closes/{id:guid}/reopen` | POST | PeriodClosingController | SettingsManage | ? |
| `api/price-lists/service-prices` | GET | PriceListsController | PricingView | ? |
| `api/price-lists/service-prices` | POST | PriceListsController | PricingManage | ? |
| `api/price-lists/service-prices/{id:guid}/active` | POST | PriceListsController | PricingManage | ? |
| `api/production-floor` | GET | ProductionFloorController | ProductionView | ? |
| `api/production-orders` | GET | ProductionOrdersController | ProductionView | ? |
| `api/production-orders/{id:guid}` | GET | ProductionOrdersController | ProductionView | ? |
| `api/production-orders/export` | GET | ProductionOrdersController | ReportsExport | Export |
| `api/production-orders/{id:guid}/pdf` | GET | ProductionOrdersController | ReportsExport | GetPdf |
| `api/production-orders/issue` | POST | ProductionOrdersController | RawConsume | ? |
| `api/production-orders/{id:guid}` | PUT | ProductionOrdersController | ProductionEdit | ? |
| `api/production-orders/{id:guid}/raw-allocations` | POST | ProductionOrdersController | RawConsume | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/start` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/complete` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/skip` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/{id:guid}/complete` | POST | ProductionOrdersController | ProductionComplete | ? |
| `api/production-orders/{id:guid}/cancel` | POST | ProductionOrdersController | ProductionEdit | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/transfer` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/output` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/stage-executions/{stageExecutionId:guid}/drawing` | POST | ProductionOrdersController | ProductionExecuteStage | ? |
| `api/production-orders/{id:guid}/pause` | POST | ProductionOrdersController | ProductionEdit | ? |
| `api/production-orders/{id:guid}/resume` | POST | ProductionOrdersController | RawConsume | ? |
| `api/production-shifts` | GET | ProductionShiftsController | ProductionView | ? |
| `api/production-shifts` | POST | ProductionShiftsController | ProductionManageShifts | ? |
| `api/production-shifts/{id:guid}` | PUT | ProductionShiftsController | ProductionManageShifts | ? |
| `api/production-shifts/{id:guid}/activation` | POST | ProductionShiftsController | ProductionManageShifts | ? |
| `api/production-stages` | GET | ProductionStagesController | ProductionView | ? |
| `api/production-stages` | POST | ProductionStagesController | SettingsManage | ? |
| `api/production-stages/{id:guid}/formation-stage` | POST | ProductionStagesController | SettingsManage | ? |
| `api/production-stages/{id:guid}/ready-goods-stage` | POST | ProductionStagesController | SettingsManage | ? |
| `api/production-stages/{id:guid}` | PUT | ProductionStagesController | SettingsManage | ? |
| `api/production-stages/{id:guid}/active` | POST | ProductionStagesController | SettingsManage | ? |
| `api/purchase-units` | GET | PurchaseUnitsController | PurchasesView | ? |
| `api/purchase-units` | POST | PurchaseUnitsController | PurchasesEdit | ? |
| `api/purchase-units/{id:guid}` | PUT | PurchaseUnitsController | PurchasesEdit | ? |
| `api/purchase-units/{id:guid}/active` | POST | PurchaseUnitsController | PurchasesEdit | ? |
| `api/[controller]/orders` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/orders/{id:guid}` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/orders` | POST | PurchasesController | PurchasesCreate | ? |
| `api/[controller]/orders/{id:guid}` | PUT | PurchasesController | PurchasesEdit | ? |
| `api/[controller]/orders/{id:guid}/lines` | POST | PurchasesController | PurchasesEdit | ? |
| `api/[controller]/orders/{id:guid}/lines/{lineId:guid}` | PUT | PurchasesController | PurchasesEdit | ? |
| `api/[controller]/orders/{id:guid}/lines/{lineId:guid}` | DELETE | PurchasesController | PurchasesEdit | ? |
| `api/[controller]/orders/{id:guid}/lines/{lineId:guid}/correct-quantity` | POST | PurchasesController | PurchasesEditAfterReceipt | ? |
| `api/[controller]/orders/{id:guid}/submit` | POST | PurchasesController | PurchasesSubmit | ? |
| `api/[controller]/orders/{id:guid}/approve` | POST | PurchasesController | PurchasesApprove | ? |
| `api/[controller]/orders/{id:guid}/cancel` | POST | PurchasesController | PurchasesCancel | ? |
| `api/[controller]/receipts` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/receipts/{id:guid}` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/receipts` | POST | PurchasesController | PurchasesReceive | ? |
| `api/[controller]/supplier-invoices` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/supplier-invoices/{id:guid}` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/supplier-invoices` | POST | PurchasesController | PurchasesInvoice | ? |
| `api/[controller]/supplier-invoices/{id:guid}` | PUT | PurchasesController | PurchasesInvoice | ? |
| `api/[controller]/supplier-invoices/{id:guid}/post` | POST | PurchasesController | PurchasesInvoice | ? |
| `api/[controller]/supplier-invoices/{id:guid}/cancel` | POST | PurchasesController | PurchasesCancel | ? |
| `api/[controller]/supplier-payments` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/supplier-payments` | POST | PurchasesController | PurchasesPay | ? |
| `api/[controller]/supplier-payments/{id:guid}/cancel` | POST | PurchasesController | PurchasesPay | ? |
| `api/[controller]/suppliers/{supplierId:guid}/ledger` | GET | PurchasesController | PurchasesView | ? |
| `api/[controller]/orders/{id:guid}/pdf` | GET | PurchasesController | PurchasesView | GetOrderPdf |
| `api/[controller]/receipts/{id:guid}/pdf` | GET | PurchasesController | PurchasesView | GetReceiptPdf |
| `api/[controller]/supplier-invoices/{id:guid}/pdf` | GET | PurchasesController | PurchasesView | GetSupplierInvoicePdf |
| `api/[controller]/suppliers/{supplierId:guid}/ledger/export` | GET | PurchasesController | PurchasesExport | ExportSupplierLedger |
| `api/[controller]/orders/export` | GET | PurchasesController | PurchasesExport | ExportOrders |
| `api/[controller]/receipts/export` | GET | PurchasesController | PurchasesExport | ExportReceipts |
| `api/[controller]/supplier-invoices/export` | GET | PurchasesController | PurchasesExport | ExportInvoices |
| `api/[controller]/supplier-balances/export` | GET | PurchasesController | PurchasesExport | ExportSupplierBalances |
| `api/[controller]/supplier-payments/export` | GET | PurchasesController | PurchasesExport | ExportSupplierPayments |
| `api/[controller]/supplier-balances` | GET | PurchasesController | PurchasesView | ? |
| `api/raw-external-releases` | GET | RawExternalReleasesController | RawExternalRelease | ? |
| `api/raw-external-releases/export` | GET | RawExternalReleasesController | ReportsExport | Export |
| `api/raw-external-releases` | POST | RawExternalReleasesController | RawExternalRelease | ? |
| `api/raw-external-releases/{id:guid}/return` | POST | RawExternalReleasesController | RawExternalRelease | ? |
| `api/raw-external-releases/{id:guid}/external-processing` | PUT | RawExternalReleasesController | RawExternalRelease | ? |
| `api/raw-external-releases/{id:guid}/cancel` | POST | RawExternalReleasesController | RawExternalRelease | ? |
| `api/raw-messages` | GET | RawMessagesController | RawReceive | ? |
| `api/raw-messages/export` | GET | RawMessagesController | ReportsExport | Export |
| `api/raw-messages/{id:guid}` | GET | RawMessagesController | RawReceive | ? |
| `api/raw-messages` | POST | RawMessagesController | RawReceive | ? |
| `api/raw-messages/{id:guid}/inspection` | POST | RawMessagesController | RawInspect | RecordInspection |
| `api/raw-messages/{id:guid}/cancel` | POST | RawMessagesController | RawReceiptCancel | Cancel |
| `api/raw-messages/{id:guid}/pdf` | GET | RawMessagesController | ReportsExport | GetPdf |
| `api/ready-goods/transfers` | GET | ReadyGoodsController | ReadyView | ? |
| `api/ready-goods/transfers/export` | GET | ReadyGoodsController | ReportsExport | ExportTransfers |
| `api/ready-goods/balance/export` | GET | ReadyGoodsController | ReportsExport | ExportBalance |
| `api/ready-goods/balance` | GET | ReadyGoodsController | ReadyView | ? |
| `api/ready-goods/transfers` | POST | ReadyGoodsController | ReadyTransfer | ? |
| `api/ready-goods/transfers/{id:guid}` | PUT | ReadyGoodsController | InventoryEdit | ? |
| `api/ready-goods/transfers/{id:guid}` | DELETE | ReadyGoodsController | InventoryDelete | DeleteTransfer |
| `api/report-builder/entities` | GET | ReportBuilderController | ReportsView | ? |
| `api/report-builder/run` | POST | ReportBuilderController | ReportsView | ? |
| `api/report-builder/templates` | GET | ReportBuilderController | ReportsView | ? |
| `api/report-builder/templates` | POST | ReportBuilderController | ReportsExport | ? |
| `api/report-builder/run/pdf` | POST | ReportBuilderController | ReportsExport | RunPdf |
| `api/report-builder/run/excel` | POST | ReportBuilderController | ReportsExport | RunExcel |
| `api/reports/inventory-movements` | GET | ReportsController | InventoryView | ? |
| `api/reports/inventory-movements/excel` | GET | ReportsController | ReportsExport | GetInventoryMovementsExcel |
| `api/reports/inventory-movements/pdf` | GET | ReportsController | ReportsExport | GetInventoryMovementsPdf |
| `api/reports/warehouse-balances/export` | GET | ReportsController | ReportsExport | ExportWarehouseBalances |
| `api/reports/negative-stock-overrides` | GET | ReportsController | ReportsView | ? |
| `api/reports/negative-stock-overrides/pdf` | GET | ReportsController | ReportsExport | GetNegativeStockOverridesPdf |
| `api/reports/negative-stock-overrides/excel` | GET | ReportsController | ReportsExport | GetNegativeStockOverridesExcel |
| `api/separates` | GET | SeparatesController | ProductionView | ? |
| `api/separates/{id:guid}/reprocess` | POST | SeparatesController | ProductionReprocess | ? |
| `api/separates/{id:guid}/scrap` | POST | SeparatesController | ProductionEdit | ? |
| `api/settings` | GET | SettingsController | NONE | ? |
| `api/settings` | PUT | SettingsController | SettingsManage | ? |
| `api/stock-adjustments/export` | GET | StockAdjustmentsController | ReportsExport | Export |
| `api/stock-adjustments` | GET | StockAdjustmentsController | InventoryView | ? |
| `api/stock-adjustments` | POST | StockAdjustmentsController | InventoryAdjust | ? |
| `api/[controller]` | GET | SuppliersController | SuppliersView | ? |
| `api/[controller]` | POST | SuppliersController | SuppliersCreate | ? |
| `api/[controller]/{id:guid}` | PUT | SuppliersController | SuppliersEdit | ? |
| `api/[controller]/export` | GET | SuppliersController | ReportsExport | Export |
| `api/[controller]/import/template` | GET | SuppliersController | SuppliersView | ImportTemplate |
| `api/[controller]/import/preview` | POST | SuppliersController | SuppliersCreate | ? |
| `api/[controller]/import/execute` | POST | SuppliersController | SuppliersCreate | ? |
| `api/[controller]/import/preview-update` | POST | SuppliersController | SuppliersEdit | ? |
| `api/[controller]/import/execute-update` | POST | SuppliersController | SuppliersEdit | ? |
| `api/supplies` | GET | SuppliesController | SuppliesView | ? |
| `api/supplies/{id:guid}` | GET | SuppliesController | SuppliesView | ? |
| `api/supplies/export` | GET | SuppliesController | ReportsExport | Export |
| `api/supplies/{id:guid}/pdf` | GET | SuppliesController | ReportsExport | GetPdf |
| `api/supplies` | POST | SuppliesController | SuppliesIssue | ? |
| `api/supplies/{id:guid}/lines` | POST | SuppliesController | SuppliesIssue | ? |
| `api/supplies/{id:guid}/lines/{lineId:guid}` | DELETE | SuppliesController | SuppliesIssue | ? |
| `api/supplies/{id:guid}/post` | POST | SuppliesController | SuppliesIssue | ? |
| `api/supplies/{id:guid}/cancel` | POST | SuppliesController | SuppliesCancel | ? |
| `api/treasury-accounts` | GET | TreasuryAccountsController | TreasuryView | ? |
| `api/treasury-accounts` | POST | TreasuryAccountsController | TreasuryCreate | ? |
| `api/treasury-accounts/{id:guid}/statement` | GET | TreasuryAccountsController | TreasuryView | ? |
| `api/treasury-accounts/{id:guid}/statement/pdf` | GET | TreasuryAccountsController | ReportsExport | GetStatementPdf |
| `api/treasury-accounts/{id:guid}/statement/excel` | GET | TreasuryAccountsController | ReportsExport | GetStatementExcel |
| `api/receipts` | GET | ReceiptsController | TreasuryView | ? |
| `api/receipts` | POST | ReceiptsController | TreasuryCreate | ? |
| `api/receipts/{id:guid}/cancel` | POST | ReceiptsController | TreasuryCancel | ? |
| `api/payments` | GET | PaymentsController | TreasuryView | ? |
| `api/payments` | POST | PaymentsController | TreasuryCreate | ? |
| `api/payments/{id:guid}/cancel` | POST | PaymentsController | TreasuryCancel | ? |
| `api/treasury-transfers` | GET | TreasuryTransfersController | TreasuryView | ? |
| `api/treasury-transfers` | POST | TreasuryTransfersController | TreasuryCreate | ? |
| `api/treasury-transfers/{id:guid}/cancel` | POST | TreasuryTransfersController | TreasuryCancel | ? |
| `api/users` | GET | UsersController | UsersManage | ? |
| `api/users` | POST | UsersController | UsersManage | ? |
| `api/users/{id:guid}` | PUT | UsersController | RolesManage | ? |
| `api/users/{id:guid}/deactivate` | POST | UsersController | UsersManage | Deactivate |
| `api/users/{id:guid}/change-password` | POST | UsersController | UsersManage | ChangePassword |
| `api/[controller]` | GET | WarehousesController | InventoryView | ? |
| `api/[controller]` | POST | WarehousesController | SettingsManage | ? |
| `api/[controller]/{id:guid}` | PUT | WarehousesController | SettingsManage | ? |
| `api/[controller]/{id:guid}/active` | POST | WarehousesController | SettingsManage | ? |
| `api/[controller]/export` | GET | WarehousesController | ReportsExport | Export |
| `api/[controller]/import/template` | GET | WarehousesController | InventoryView | ImportTemplate |
| `api/[controller]/import/preview` | POST | WarehousesController | SettingsManage | ? |
| `api/[controller]/import/execute` | POST | WarehousesController | SettingsManage | ? |
