# DyeHouse ERP

A production-oriented ERP for a fabric dyeing/finishing job-work factory (لحساب الغير), built with .NET Clean Architecture + React. This implements the original spec end to end, across all of its functional sections: master data, raw receiving, production with a configurable stage engine, customer formation requests and specification cells, separates/reprocessing, materials/chemicals, operating supplies, material sales, ready goods, delivery, invoicing, customer accounts, treasury, cheques, purchases with supplier statements, payroll, cost accounting (including job-order costing), reporting with PDF/Excel export, attachments, an approval center, and a customer-portal + production-floor surface - all behind granular, server-enforced permissions, an automatic audit log, and a bilingual Arabic/English (RTL/LTR) UI.

## Important: what "tested" actually means here

What has genuinely been run in this repo's development environment:

- `dotnet build DyeHouseERP.sln` - **0 warnings, 0 errors** across all six projects.
- `dotnet test tests/DyeHouseERP.UnitTests` - **59/59 passing** (domain rules: no-FIFO allocation, stage rules, transfers, ledger direction, numbering, negative-stock override, check lifecycle, permission-list normalisation).
- `dotnet ef migrations add` - migrations are generated in `src/DyeHouseERP.Persistence/Migrations` and the model snapshot is up to date. Generating a migration builds the whole EF model, so the entity configurations, indexes, FK cascade paths and column mappings are all verified by it - not just by eye.
- Frontend type checking - `bunx tsc --noEmit -p tsconfig.json` exits 0, with `noUnusedLocals`/`noUnusedParameters` on.
- Static structural verification across the codebase, run as scripts rather than by eye: `node tools/wiring-sweep.js` (271 controller actions, 0 duplicate routes, 0 actions without a permission policy beyond the two intentional `[AllowAnonymous]` ones, 74 UI download/import calls with 0 unresolved) and `node tools/duplicate-sweep.js` (0 duplicate permission values, i18n keys, entity names or indexes; 63/63 `DbSet`s matching in both directions).

What has **not** been run, and cannot be in this environment:
- The integration tests in `tests/DyeHouseERP.IntegrationTests` need a real SQL Server (`Server=localhost,1433`) - there is none here, and the numbering engine's `sp_getapplock` has no in-memory equivalent. They are written and compiling, but unexecuted. **No SQL Server integration testing has been performed.**
- The API therefore cannot start (`Program.cs` runs `Database.MigrateAsync()` on boot and there is no database to migrate), so the UI was exercised **without a backend**. `tools/ui-smoke.js` and `tools/ui-smoke-authed.js` drive a headless Chromium over the real routes: every module screen mounts, no page throws, the language switch flips RTL↔LTR, and clicking each export/import control was observed issuing the correct request. What could **not** be observed is the other half of those calls - a real `.xlsx`/`.pdf` body, a saved file, a rendered statement with data - because every response is a connection error. Claims about *what a button does* below are therefore backed by a captured request; claims about *the file it produces* are not.

## Headless UI smoke test

The preview is a static Vite dev server, so the browser tests are run against it with a stub auth token (the guard only checks for a token's presence, and there is no backend to log in against):

```bash
freebuff-preview start
mkdir -p /tmp/uitest && cd /tmp/uitest && npm i puppeteer@23   # kept out of package.json on purpose
cd - >/dev/null
node tools/smoke-modules.sh        # every module transforms through the running dev server
node tools/ui-smoke.js             # 27 routes, signed out
node tools/ui-smoke-authed.js      # 33 module screens, token stubbed
```

Chromium needs system libraries that are not present in a bare image:
`apt-get install -y --no-install-recommends libglib2.0-0 libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2 libatspi2.0-0 fonts-liberation`

## Getting started

### 1. Backend

Requires .NET 9 SDK and a SQL Server instance you already have access to (a local install, SQL Server Express/LocalDB, or a remote instance) - no Docker required.

First, point the app at your SQL Server - edit `src/DyeHouseERP.API/appsettings.Development.json` and replace the connection string with your own, e.g.:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=DyeHouseERP;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

(`Trusted_Connection=True` for Windows/LocalDB integrated auth, or `User Id=...;Password=...;` if your server uses SQL login instead.)

```bash
dotnet restore
dotnet build

dotnet tool install --global dotnet-ef   # once

dotnet ef migrations add InitialCreate \
  --project src/DyeHouseERP.Persistence \
  --startup-project src/DyeHouseERP.API

dotnet run --project src/DyeHouseERP.API
```

Swagger UI opens at `https://localhost:7100/swagger`. In Development, migrations apply and the document-numbering sequences + a default admin login (`admin` / `Admin@12345`) seed automatically on startup.

### 2. Frontend

Requires Node 20+.

```bash
cd src/DyeHouseERP.Web
npm install
npm run dev
```

Opens at `http://localhost:5173`, proxying `/api` to the backend.

> **Hosted-preview note.** The repository root is the folder containing `src/`,
> but `package.json` lives in `src/DyeHouseERP.Web/`. A preview tool that
> assumes it sits at the repository root will fail with
> `ENOENT: .../package.json`. Point it at the real directory instead of moving
> the manifest:
>
> ```bash
> freebuff-preview set-install "cd DyeHouseERP/src/DyeHouseERP.Web && npm install"
> freebuff-preview set "cd DyeHouseERP/src/DyeHouseERP.Web && npx vite --host 0.0.0.0 --port ${PORT:-5173}" 5173
> freebuff-preview set-build  "cd DyeHouseERP/src/DyeHouseERP.Web && npx vite build"
> freebuff-preview start
> ```
>
> The preview is also served under a generated external hostname
> (`<port>-<workspace>.e2b.app`). Vite rejects requests whose `Host` header is
> not in `server.allowedHosts`, so without that list the external URL returns
> *"Blocked request. This host ... is not allowed."* instead of the app. It is
> set to `[".e2b.app", ".e2b.dev"]` in `vite.config.ts` - **not**
> `allowedHosts: true`, which would turn the DNS-rebinding protection off
> entirely. A leading dot matches the domain and every subdomain, and these are
> plain strings because Vite compares entries with `===` (a `RegExp` entry can
> never match). Verify with:
>
> ```bash
> node tools/ext-preview-check.js https://<port>-<workspace>.e2b.app
> ```
>
> which loads the real external hostname in a headless browser and fails if Vite
> answers with its host-check page. Hitting `127.0.0.1` would pass even with the
> check misconfigured, so the external URL is the only meaningful test.

### 3. Running the integration tests

Needs the same SQL Server as above. They create/drop their own database (`DyeHouseERP_IntegrationTests`) on every run - never point them at a database with real data. By default they connect using the same style of connection string as above; override it if needed:

```bash
DYEHOUSE_TEST_CONNECTION="Server=localhost;Database=DyeHouseERP_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True;" dotnet test tests/DyeHouseERP.IntegrationTests
```

Override the target with an environment variable if needed:

```bash
DYEHOUSE_TEST_CONNECTION="Server=localhost,1433;Database=DyeHouseERP_IntegrationTests;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;" dotnet test tests/DyeHouseERP.IntegrationTests
```

Domain unit tests (no database needed) run separately: `dotnet test tests/DyeHouseERP.UnitTests`.

## Solution structure

```
src/
  DyeHouseERP.Domain/         entities, enums, domain exceptions - zero external dependencies
  DyeHouseERP.Application/    CQRS commands/queries (MediatR), validators (FluentValidation), DTOs
  DyeHouseERP.Infrastructure/ auth/current-user, clock - no EF Core here
  DyeHouseERP.Persistence/    EF Core DbContext, entity configs, the numbering engine, ledger services
  DyeHouseERP.API/            controllers, JWT auth, Swagger, Serilog, global exception middleware
  DyeHouseERP.Web/            React + TypeScript + Tailwind, RTL Arabic-first
tests/
  DyeHouseERP.UnitTests/      domain rule tests (no-FIFO, inspection gating, stage rules, transfers, ...)
```

## What's implemented, by spec section

| Sections | Module | Key files |
|---|---|---|
| 6, 7, 8, 9 | Customers, Items, Warehouses, numbering engine | `Domain/Entities/{Customer,Item,Warehouse,DocumentSequence}.cs`, `Persistence/Numbering/SqlDocumentNumberGenerator.cs` |
| 10, 11 | Raw Receipt / Message + receiving inspection | `Domain/Entities/RawMessage.cs`, `Application/RawReceipts/*` |
| 18 | Append-only inventory ledger, per-customer balances | `Domain/Entities/InventoryTransaction.cs`, `Persistence/Services/InventoryLedgerService.cs` |
| 12, 19-21 | Production Order + configurable stage engine | `Domain/Entities/{ProductionOrder,ProductionStageDefinition,ProductionOrderStageExecution}.cs` |
| 4, 13 | Manual raw allocation to Production Order (NO FIFO) | `Application/ProductionOrders/Commands/AllocateRawCommand.cs` |
| 14, 15 | External raw release + customer-to-customer transfer | `Domain/Entities/{RawExternalRelease,CustomerTransfer}.cs` |
| 16, 17 | Stock adjustments + negative-stock override & audit | `Domain/Entities/{StockAdjustment,NegativeStockOverride}.cs` |
| 22, 23 | Separates + reprocessing (new linked Production Order) | `Domain/Entities/Separate.cs`, `Application/Separates/*` |
| 24-28 | Materials/chemicals, warehouses, transfer, issue, "الإحلال" | `Domain/Entities/Material*.cs`, `Persistence/Services/MaterialLedgerService.cs` |
| 29, 30 | Ready goods transfer ("ترحيل") + live balance | `Domain/Entities/ReadyGoodsTransfer.cs`, `Application/ReadyGoods/*` |
| 31 | Delivery (Draft->Prepared->Delivered->Cancelled + reversal) | `Domain/Entities/Delivery.cs`, `Application/Deliveries/*` |
| 32, 33 | Invoices + customer statement | `Domain/Entities/{Invoice,CustomerLedgerEntry}.cs`, `Application/Invoices/*` |
| 34 | Treasury: accounts, receipts, payments, transfers | `Domain/Entities/Treasury.cs`, `Application/Treasury/*` |
| 35 | Cost accounting rollup per Production Order | `Domain/Entities/CostEntry.cs`, `Application/CostAccounting/*` |
| 36, 37 | Customer portal requests + production floor dashboard | `Domain/Entities/ProductionRequest.cs`, `Application/CustomerPortal/*` |
| — | Auth (login, JWT, user management) | `Domain/Entities/User.cs`, `Infrastructure/Services/{PasswordHasher,JwtTokenService}.cs`, `Application/{Auth,Users}/*` |
| 17, 33 | PDF/Excel report export | `Infrastructure/Services/ReportExportService.cs`, `Application/Reports/*`, `API/Controllers/{ReportsController,CustomerStatementController}` |
| 41 | Granular, server-enforced permissions | `Domain/Common/Permissions.cs`, `API/Authorization/PermissionAuthorization.cs` - every controller action carries its own `[Authorize(Policy = PermissionPolicy.For(Permissions.X))]`, not just a class-level role check |
| 42 | Audit log (automatic, every Create/Update/Delete + Login/LoginFailed) | `Domain/Entities/AuditLogEntry.cs`, `Persistence/Interceptors/AuditSaveChangesInterceptor.cs`, `Application/Audit/*` |
| 1, 47 | Company branding: logo upload + name, shown on login/sidebar | `Domain/Entities/CompanySettings.cs`, `Application/Settings/*`, `API/Controllers/SettingsController.cs`, `Web/src/pages/SettingsPage.tsx` |
| 19, 33 | Real-data dashboard (no hardcoded KPIs - spec rule #19) | `Application/Dashboard/Queries/GetDashboardSummaryQuery.cs`, `Web/src/pages/DashboardPage.tsx` |
| 37, 38 | Excel export + Excel import across every master-data and operational list. One shared contract (`ImportPreviewDto` / `ImportExecuteResultDto`) and one shared wizard (`Web/src/components/ImportPanel.tsx`); endpoint declarations live in `Web/src/api/exports.ts`, file generation in `Infrastructure/Services/{ReportExportService,ExcelImportReader}.cs` |
| 39 | Printable single-document PDFs + in-app print-preview screens | `API/Controllers/{InvoicesController,DeliveriesController,ProductionOrdersController,RawMessagesController}.cs` (`/pdf` actions), `Web/src/components/PrintPreviewLayout.tsx`, `Web/src/pages/print/*` |
| 40 | QR codes + scan quick-view | `Web/src/pages/scan/ScanViewPage.tsx` (embedded via `qrcode.react` in the print-preview pages) |
| 43 | Period closing | `Domain/Entities/PeriodClose.cs`, `Persistence/Services/PeriodCloseService.cs`, `Application/PeriodClosing/*`, `Web/src/pages/PeriodClosingPage.tsx` |
| 36 | Custom report builder (whitelisted entities/columns only) | `Application/ReportBuilder/*`, `API/Controllers/ReportBuilderController.cs`, `Web/src/pages/ReportBuilderPage.tsx` |
| 5, 12 | Customer Formation Requests: reusable specification cells, snapshot on approval, group output, linkage to job orders and raw messages | `Domain/Entities/{FormationRequest,FormationSpecTemplate}.cs`, `Application/FormationRequests/*`, `API/Controllers/{FormationRequestsController,FormationSpecificationsController}.cs`, `Web/src/pages/{FormationRequestsPage,FormationRequestDetailPage,FormationSpecificationsPage}.tsx` |
| 34 | Job Order costing: estimated / live actual / approved cost, cost per KG and per Meter, costing notes, Excel/PDF export | `Application/CostAccounting/Commands/ProductionOrderCostingCommands.cs`, `Application/CostAccounting/Queries/GetProductionOrderCostQuery.cs`, `API/Controllers/CostAccountingController.cs`, `Web/src/pages/ProductionOrderDetailPage.tsx` (cost panel) |
| 21 | External Processing: release to a third party, expected return, cost, return posting, cancellation | `Domain/Entities/RawExternalRelease.cs`, `Application/RawExternalReleases/Commands/ExternalProcessingCommands.cs`, `Web/src/pages/RawExternalReleasesPage.tsx` |
| 26 | Material sales to customers (draft -> approved -> posted), ledger-backed, never a stock edit | `Domain/Entities/MaterialSale.cs`, `Application/MaterialSales/*`, `API/Controllers/MaterialSalesController.cs`, `Web/src/pages/MaterialSalesPage.tsx` |
| 27 | Operating supplies as their own store workflow (issue, timeline, acknowledgment) | `Domain/Entities/SupplyIssue.cs`, `Application/Supplies/*`, `API/Controllers/SuppliesController.cs`, `Web/src/pages/SuppliesPage.tsx` |
| 44 | Approval Center - one read-only inbox over every pending approval in the system | `Application/Approvals/*`, `API/Controllers/ApprovalsController.cs`, `Web/src/pages/ApprovalCenterPage.tsx` |
| 47 | Private attachments on any business document (stored in-DB, permission-checked downloads) | `Domain/Entities/Attachment.cs`, `Application/Attachments/*`, `API/Controllers/AttachmentsController.cs`, `Web/src/components/AttachmentsPanel.tsx` |
| 6, 7 | Bilingual items (AR/EN names + category) and full Excel import for items: template, preview, validation, duplicate handling, permission-gated update of existing rows | `Domain/Entities/Item.cs`, `Application/Items/{Commands,Queries}/*`, `API/Controllers/ItemsController.cs`, `Web/src/pages/ItemsPage.tsx` |
| 37, 38, 39 | Print/PDF/Excel across documents and reports: every list endpoint that is worth printing accepts `?format=excel\|pdf`, every single document has a `/pdf`, and the three account statements (customer, supplier, treasury) are documents with opening balance, running balance and closing total. UI: `Web/src/api/exports.ts` + `Web/src/components/ExportButtons.tsx` |
| 2, 3 | Arabic/English UI with a language switcher and automatic RTL/LTR | `Web/src/i18n/index.tsx` (`useI18n`), `Web/src/components/Layout.tsx` |
| 35 | Purchases: requests/orders/receiving/supplier invoices/payments, balances and statements, draft editing, details, Excel + PDF export | `Domain/Entities/Purchase*.cs`, `Application/Purchases/*`, `API/Controllers/PurchasesController.cs`, `Web/src/pages/PurchasesPage.tsx`, `Web/src/api/documents.ts` |
| 49 | Payroll: departments, employees, monthly runs, allowances/deductions, approve/post/cancel, payslip PDF and run Excel/PDF | `Domain/Entities/{Employee,PayrollRun}.cs`, `Application/Payroll/*`, `API/Controllers/PayrollController.cs`, `Web/src/pages/PayrollPage.tsx` |
| 33 | Cheques: incoming/outgoing, received, deposited, cleared, bounced/returned, endorsed, cancelled, with a movement ledger, status audit and Excel/PDF export | `Domain/Entities/Check.cs`, `Application/Checks/*`, `API/Controllers/ChecksController.cs`, `Web/src/pages/ChecksPage.tsx` |
| 14 | Suppliers master data + supplier statement | `Domain/Entities/Supplier.cs`, `Application/Suppliers/*`, `API/Controllers/SuppliersController.cs`, `Web/src/pages/SuppliersPage.tsx` |
| 18 | Inventory movements report over the append-only ledger (+ Excel/PDF) | `Application/Reports/Queries/GetInventoryMovementsQuery.cs`, `API/Controllers/ReportsController.cs` |
| 14 | Warehouse as ONE sidebar module: balances, receipts, issues, transfers, adjustments, movements, master data | `Web/src/pages/WarehouseHubPage.tsx` |

Every module above has a matching RTL Arabic React page reachable from the sidebar.

## Key design decisions

- No FIFO, anywhere. Every consumption (production allocation, external release, transfer, adjustment, delivery) requires the user to name a specific source (message, ready-goods lot). Nothing is ever auto-picked oldest-first.
- No "Top/توب" unit. `UnitOfMeasure` is only `KG` or `Meter`; piece/tob counts are plain descriptive integers, never their own inventory entity.
- Balances are never stored fields. Two append-only ledgers back the whole system: `InventoryTransaction` (fabric, per customer+message+item) and `MaterialTransaction` (chemicals, per warehouse+material). A third, `CustomerLedgerEntry`, backs the financial statement, and `TreasuryTransaction` backs cash/bank balances. Every "current balance" anywhere in the UI is a live sum over one of these - there is no `Balance` column anywhere that could drift out of sync.
- Customer ownership is a ledger dimension, not a message field. `CustomerTransfer` moves stock between customers without ever editing `RawMessage.CustomerId` - see `IInventoryLedgerService.GetCustomerBalanceAsync` vs `GetMessageBalanceAsync`.
- Stage names are never hard-coded. `ProductionStageDefinition` is fully admin-configurable; a new order's route is a snapshot of whatever's Active, in Sequence order, at creation time.
- Negative stock requires an explicit, permissioned, audited override (`inventory.allow_negative_stock`) - reused identically across raw allocation, external release, transfer, and stock adjustments, each writing a `NegativeStockOverride` row for the audit report.
- Reversals, not edits. Cancelling a Delivered delivery or an Issued invoice posts an equal-and-opposite ledger row referencing the original - nothing is ever mutated or deleted after being finalized (`AuditableEntity.Lock()`).
- Genealogy is preserved, never overwritten. Reprocessing a Separate always creates a brand-new Production Order (`ReprocessingOfProductionOrderId`); the original order's history is immutable.
- Never divide by zero. Cost-per-KG/Meter and profit margin in `GetProductionOrderCostQuery` are only computed when their denominator is actually positive, otherwise `null`.
- Enums serialize as their string names over the API (`JsonStringEnumConverter` registered globally in `Program.cs`), not raw integers - every status/kind/priority field in the React types and every string comparison in the frontend (`status === "Pending"`) depends on this. Caught while writing the integration tests below; it's an easy thing to silently get wrong.

## Honest gaps / what I'd do next with more time

- Build, unit tests, migrations and frontend type checking all pass (see above). What is still unverified is runtime behaviour against a real SQL Server and a real browser - the integration tests exist but need a database this environment does not have.
- Bilingual AR/EN with RTL/LTR is implemented in `Web/src/i18n/index.tsx` (`useI18n`: `t`, `pick`, `lang`, `dir`), with both dictionaries at key parity (547 keys each, no duplicates). Some older screens still carry inline Arabic literals; the shared shell, dashboard, purchases, payroll, checks, items, formation, supplies, material-sales, warehouse and approvals screens are on translation keys. Finishing the extraction on the remaining legacy pages is mechanical work, not new architecture.
- **Excel import** follows one contract everywhere, for six master-data lists: Customers, Items, Materials/Chemicals, Suppliers, Warehouses and Employees. Upload → read (`ExcelImportReader`, ClosedXML) → validate every row (missing fields, duplicate keys, in-file duplicates, existing-record conflicts) → preview with per-row errors and the exact action each row will take → explicit confirm → execute (re-validates from scratch, never trusts a stale preview) → results. Overwriting an existing record is a *separate* pair of endpoints guarded by the module's `*.edit` permission and only reachable when the user ticks a box - that is what makes "no silent overwrite" true. Warehouses are the deliberate exception (`supportsUpdate={false}`): changing a warehouse's `Kind` would silently reinterpret every historical balance it already holds, so an import can only ever create. Arabic header aliases are accepted by every validator, so one template works for an Arabic-speaking clerk and for an export from another system.
- **Document PDFs** (spec section 39) exist for every single document that gets handed to someone: raw message, production order, delivery, invoice, formation request, check, purchase order, purchase receipt, supplier invoice, material sale, supply issue, job-order costing, plus the customer/supplier/treasury statements. Each has a "PDF" button on its own screen.
- **In-app print preview** (spec section 39) exists for the four core operational documents: `/print/invoice/:id`, `/print/delivery/:id`, `/print/production-order/:id`, `/print/raw-message/:id` render styled A4 HTML (company logo, header, line items, totals, signature lines, QR) and hand off to the browser's own print dialog. Statements and reports use a lighter `@media print` stylesheet on the report screen itself. The PDF endpoints remain the way to *save* a file.
- **Verification tooling.** `node tools/wiring-sweep.js` proves every download/import path the React app calls resolves to a real controller action, that no action lacks a permission policy (other than login and the public company settings), and that no verb+route pair is declared twice. `node tools/duplicate-sweep.js` proves there are no duplicate permission values, i18n keys, entity class names or `HasIndex` calls, and that the 63 `DbSet`s on `ApplicationDbContext` match the interface the Application layer depends on in both directions. `node tools/hooks-check.js` walks the TypeScript AST for the React failure modes a compiler passes straight through - a hook called inside a branch, a loop or a callback, and a local binding that shadows a hook name. `bash tools/smoke-modules.sh` asks a *running* dev server to transform every application module, which catches a page that cannot compile. `node tools/ui-smoke.js` and `node tools/ui-smoke-authed.js` drive a headless browser over the real routes (see below). None of them replace a compiler; each closes a gap the compiler cannot see.
- **QR codes** (spec section 40) are embedded in every print-preview page, encoding a link to a matching `/scan/:type/:id` quick-view screen. Scanning a Production Order shows exactly what the spec asks for: customer, item, color, requested/completed quantity, current stage, status, and the full stage history - not the full edit screen.
- **Period closing** (spec section 43) now blocks every posting path, not a sample of them. `IPeriodCloseService.EnsureOpenAsync` is called by **all 29 handlers that insert a ledger row** (verified by scanning every `new InventoryTransaction / MaterialTransaction / CustomerLedgerEntry / SupplierLedgerEntry / TreasuryTransaction` site): raw receipt, inspection rejection, raw allocation, customer-to-customer transfer, external release and external-processing return, ready-goods transfer, delivery and its cancellation, material issue/transfer/preparation, supply issue and its cancellation, material-sale posting and cancellation, purchase receipt, supplier invoice post/cancel, supplier payment, invoice issue/cancel, treasury receipt/payment/transfer, stock adjustment, cheque clearing, payroll post/cancel. The date used is exactly the date stamped on the ledger row: reversal/cancellation handlers are dated today, so a closed historical period never blocks a correction - only a period that covers *today* does. That is the documented intent of the feature, not an oversight.
- **Custom report builder** (spec section 36) is real but intentionally narrow: `ReportableEntitiesRegistry` whitelists exactly which entities (`ProductionOrders`, `Invoices`, `Customers`) and which columns can be queried - there is no dynamic/raw SQL anywhere, matching the spec's explicit warning against exposing that to normal users. Users pick an entity + columns, run it, save it as a named template, and export PDF/Excel. Adding a new reportable entity means one registry entry + one case in `RunReportCommandHandler` - the pattern is proven, just not yet applied to every entity in the system.
- **QR/barcode: no hardware scanner integration, deliberately.** No current requirement asks the factory to operate a handheld scanner, so none was added: QR labels are generated on the print-preview documents and the `/scan/:type/:id` quick-view screens are reached by opening the URL the code encodes (any phone camera or handheld reader in keyboard-wedge mode that types a URL works). Keyboard-wedge scanners that emit a plain document number instead of a URL would need a small "lookup by number" screen - explicitly **optional**, not built, and listed here so the decision is visible rather than forgotten.
- **Permissions: no Role entity, on purpose.** Access is a flat, per-user list of permission names (`User.Roles`, checked server-side by `PermissionAuthorizationHandler`, with `admin` as the catch-all) rather than a Roles table with role-permission mappings. Introducing a Role aggregate now would mean a new table, a join table, claim-derivation changes and a migration - i.e. a redesign of a working permission system, which is exactly what was asked not to happen. What *was* missing and is now closed: the picker on `/users` exposed only **3 of the 95** permissions, so most granular permissions could not be granted to anyone through the app. `Web/src/permissions.ts` now mirrors all 95 backend permissions (grouped, Arabic-labelled, searchable - verified to match `Permissions.All` exactly), and a login's permissions can finally be **edited** after creation (`PUT /api/users/{id}`, guarded by `roles.manage`, which until now was a dead constant). Two guard rails make that safe: you cannot edit your own permissions, and the last active administrator cannot lose `admin`.
- **PDF/Arabic font caveat**: `ReportExportService` (QuestPDF) renders Arabic titles/labels, but correct shaping needs an Arabic-capable font actually present on the host. Dev machines usually have one; a bare Linux container often doesn't - if PDF text comes out as boxes on your server, embed a font (e.g. Noto Naskh Arabic) via `QuestPDF.Drawing.FontManager.RegisterFont` at startup. Excel export (ClosedXML) has no such issue - Arabic renders correctly since it's just cell text, not typeset PDF content.
- List queries that hydrate full DTOs per row (e.g. `GetProductionOrdersQuery`) are simple but not optimized for large datasets - noted inline where this matters most.
- Real integration tests now exist in `tests/DyeHouseERP.IntegrationTests` (`WebApplicationFactory<Program>`, boots the real API in-process against a real SQL Server) covering: customer CRUD + 401/409 handling, manual raw allocation with the negative-stock block, and a full happy path from raw receipt through to a zero customer-statement balance. They need SQL Server reachable (see "Running the tests" below) - they don't run against an in-memory fake because the numbering engine's `sp_getapplock` call has no in-memory equivalent.
- Reporting (the various "reports" mentioned throughout the spec) now has real PDF/Excel export, not just JSON: `IReportExportService` (QuestPDF for PDF, ClosedXML for Excel) backs the customer statement (`GET /api/customers/{id}/statement/pdf|excel`, spec section 33) and the Negative Stock / Balance Override Report (`GET /api/reports/negative-stock-overrides/pdf|excel`, spec section 17). Both are one shared service, so adding export to any other list (deliveries, invoices, ...) is a ~10-line controller action, not a new subsystem.
- The seeded admin account (see below) is the only way to log in until you create more via the Users screen (admin-only, `/users`) - it covers create, deactivate, and change-password, but not self-service password reset or email verification.

## Authentication

A minimal but real JWT auth flow is wired up:

- `POST /api/auth/login` with `{ "username": "...", "password": "..." }` returns a signed JWT (`Jwt:Key`/`Issuer`/`Audience` from `appsettings.json` - **change the key before any real deployment**).
- Passwords are hashed with PBKDF2/SHA-256, 100,000 iterations, random per-user salt (`Infrastructure/Services/PasswordHasher.cs`).
- In Development, `UserSeeder` creates one admin login automatically on first run if no users exist yet: **`admin` / `Admin@12345`**. The React login page pre-fills the username and shows this hint.
- The frontend stores the token in `localStorage`, attaches it as a Bearer header on every request, and redirects to `/login` automatically on a 401.
- There's no user-management UI yet (see gaps above) - add/rotate users directly via the `Users` table or a quick script until one exists.

Want me to continue with the EF migrations attempt, a user-management screen, or the integration tests next?
