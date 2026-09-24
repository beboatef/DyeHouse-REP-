# DyeHouse ERP

A production-oriented ERP for a fabric dyeing/finishing job-work factory (لحساب الغير), built with .NET Clean Architecture + React. This implements every functional section of the original spec (sections 1-37) end to end: master data, raw receiving, production with a configurable stage engine, separates/reprocessing, materials/chemicals, ready goods, delivery, invoicing, customer accounts, treasury, cost accounting, and a customer-portal + production-floor surface.

## Important: what "tested" actually means here

I do not have the ability to run this application in the environment that generated this code - no internet access, no Docker, no ability to launch `dotnet run` or `npm run dev` and click through the UI. I was asked directly to "test everything," so I want to be precise about what I could and could not do, rather than let that instruction quietly go unaddressed:

**What I actually did (real, but limited):**
- Static structural verification via `grep`/`diff` across the whole codebase: every `DbSet<T>` in `IApplicationDbContext` has a matching one in `ApplicationDbContext` (and vice versa); every `[HttpGet/Post/Put/Delete]` action has exactly one matching `[Authorize(Policy = ...)]` permission attribute (checked file-by-file, counts must match); no duplicate `using` statements were introduced by scripted edits; no stray/malformed directories from earlier shell mistakes.
- Careful, consistent hand-written code following one pattern throughout, so errors are more likely to be small and mechanical (a missing `using`, an EF precision nuance) than structural.

**What I did NOT do:**
- Never ran `dotnet build`. Never ran `dotnet ef migrations add` against a real database. Never ran `npm install` or `npm run build`. Never opened the app in a browser. Never ran the integration tests I wrote earlier in this conversation.
- So I cannot promise it compiles cleanly on the first try, and you should treat your own first `dotnet build` + `npm install` as the actual test - not a formality.

If you run it and hit errors, paste them back to me and I'll fix them directly - that's a real, fast feedback loop, unlike me guessing at problems I can't reproduce.

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
| 37, 38 | Excel export (customers, reports) + Excel import (customers, with preview/validate/confirm workflow) | `Infrastructure/Services/{ReportExportService,ExcelImportReader}.cs`, `Application/Customers/Commands/ImportCustomersCommands.cs`, `Web/src/pages/CustomersPage.tsx` |
| 39 | Printable single-document PDFs + in-app print-preview screens | `API/Controllers/{InvoicesController,DeliveriesController,ProductionOrdersController,RawMessagesController}.cs` (`/pdf` actions), `Web/src/components/PrintPreviewLayout.tsx`, `Web/src/pages/print/*` |
| 40 | QR codes + scan quick-view | `Web/src/pages/scan/ScanViewPage.tsx` (embedded via `qrcode.react` in the print-preview pages) |
| 43 | Period closing | `Domain/Entities/PeriodClose.cs`, `Persistence/Services/PeriodCloseService.cs`, `Application/PeriodClosing/*`, `Web/src/pages/PeriodClosingPage.tsx` |
| 36 | Custom report builder (whitelisted entities/columns only) | `Application/ReportBuilder/*`, `API/Controllers/ReportBuilderController.cs`, `Web/src/pages/ReportBuilderPage.tsx` |

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

- Not compiled. As above - first build will surface small issues.
- No EF migrations generated yet - `dotnet ef migrations add InitialCreate` needs to be run against a real SQL Server to produce the actual migration files and confirm the model builds cleanly (check constraints, cascade paths on `CustomerTransfer`'s two customer FKs, etc.).
- **No bilingual Arabic/English localization system.** The UI is still hardcoded Arabic strings inside React components - exactly what the review prompt calls out as unacceptable ("Do not hardcode Arabic text directly throughout React components"). There is no `ar.json`/`en.json`, no language switcher, no automatic LTR mode. This is the single largest remaining gap from that review and hasn't been started - it needs a real i18n library (e.g. `react-i18next`), translation-key extraction across ~30 pages, and backend error-message localization. I did not attempt it this session because it's a large, separable piece of work better done as its own focused pass than squeezed in alongside everything else.
- **Excel import** now has one real, complete implementation for Customers (`/api/customers/import/preview` then `/import/execute`) following the exact safe workflow the spec requires: upload → read (`ExcelImportReader`, ClosedXML) → validate every row (missing fields, duplicate codes, in-file duplicates) → preview with per-row errors → explicit confirm → execute (re-validates before committing, never trusts a stale preview) → results. Items/Materials/opening-balance imports aren't built yet, but they're the same pattern (see `CustomerImportValidator` as the template).
- **Document PDFs** (spec section 39) now exist for single Invoice, Delivery, Production Order, and Raw Message documents (`GET /api/{resource}/{id}/pdf`), not just the two list-style reports from before - each has a "طباعة PDF" button in its page. Printing itself is "download the PDF and use the browser's print dialog" rather than a dedicated in-app print-preview screen.
- **In-app print preview** (spec section 39) now exists for real: `/print/invoice/:id`, `/print/delivery/:id`, `/print/production-order/:id`, `/print/raw-message/:id` render the document as styled A4 HTML (company logo, header, line items, totals, signature lines) with a "طباعة" button that calls the browser's own print dialog - no server round-trip needed just to preview, and `@media print` rules hide everything but the document itself. The old PDF-download endpoints still exist too, for actually saving a file.
- **QR codes** (spec section 40) are embedded in every print-preview page, encoding a link to a matching `/scan/:type/:id` quick-view screen. Scanning a Production Order shows exactly what the spec asks for: customer, item, color, requested/completed quantity, current stage, status, and the full stage history - not the full edit screen.
- **Period closing** (spec section 43) is implemented: `PeriodClose` entity, close/reopen commands (reopen is itself audited via the existing audit interceptor), and `IPeriodCloseService.EnsureOpenAsync` wired into four representative posting handlers (issuing an invoice, creating a stock adjustment, creating a treasury receipt, creating a treasury payment) - each now rejects a date inside a closed period. Extending the same check to every other posting command (raw receipt, delivery, etc.) is the identical one-line addition; it hasn't been done for all of them yet.
- **Custom report builder** (spec section 36) is real but intentionally narrow: `ReportableEntitiesRegistry` whitelists exactly which entities (`ProductionOrders`, `Invoices`, `Customers`) and which columns can be queried - there is no dynamic/raw SQL anywhere, matching the spec's explicit warning against exposing that to normal users. Users pick an entity + columns, run it, save it as a named template, and export PDF/Excel. Adding a new reportable entity means one registry entry + one case in `RunReportCommandHandler` - the pattern is proven, just not yet applied to every entity in the system.
- No QR/barcode scanning hardware integration (the QR codes are generated and the scan-view page exists, but there's no dedicated barcode-scanner input workflow beyond "open the URL the QR code encodes").
- Permissions are now genuinely per-endpoint (`[Authorize(Policy = PermissionPolicy.For(Permissions.X))]` on every controller action, checked server-side by `PermissionAuthorizationHandler`) rather than a class-level role check. What's still simplified: permissions are delivered as JWT role claims per user (`User.Roles`, comma-separated) rather than a separate Roles table with its own CRUD screen - `roles.manage` exists as a permission constant but there's no "create a custom role with this permission set" UI yet, only per-user permission checkboxes on `/users`.
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
