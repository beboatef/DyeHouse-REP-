# DyeHouse ERP

A production-oriented ERP for a fabric **dyeing / finishing / processing** factory working on a **job-work (customer-owned material / لحساب الغير)** model, built with ASP.NET Core (.NET 9) + Clean Architecture on the backend and React + TypeScript + Vite on the frontend. The UI is bilingual **Arabic (RTL, default) / English (LTR)**.

The customer owns the fabric; the factory receives it, inspects it, processes it (dyeing/finishing, closed line or open line), and returns or delivers it. Every balance in the system is derived from **append-only inventory ledgers** — nothing is ever stored as an editable balance column. Raw fabric is tracked in **KG and Meter as fully separate units** (no automatic conversion, no FIFO, no automatic source selection). Customers submit **Job Orders** (`ProductionOrder`, closed line / open line), optionally via **Formation Requests**; completed production is transferred to **Ready Goods**, then delivered and invoiced. Chemicals/materials, purchases, treasury, cheques, payroll, cost accounting, reports, permissions, and audit complete the picture.

## What the system is built on

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core, .NET 9, CQRS via MediatR, FluentValidation |
| Architecture | Clean Architecture: Domain → Application → Infrastructure / Persistence → API |
| Data | EF Core 9 + SQL Server (append-only ledger tables, no stored balance columns) |
| Frontend | React 19 + TypeScript + Vite + Tailwind, TanStack Query/Router |
| i18n | Arabic (RTL, default) + English (LTR) — `Web/src/i18n/index.tsx`, 557/557 key parity |
| Reports | PDF (QuestPDF) + Excel (ClosedXML) export on every list/report; printable single documents |
| Auth | JWT, per-user granular permissions (95 constants, server-enforced on every action), audit log |

## Terminology

User-facing terminology (UI, reports, messages): **تشغيل خارجي / External Processing** — sending customer-owned material to a third-party processor and receiving it back. Internal technical identifiers (`RawExternalRelease` entity, `RawExternalReleases` table, `RawIssueExternalRelease` document type, `/api/raw-external-releases` routes, `/raw-external-releases` page route) intentionally retain the legacy "release" naming; they are not user-visible.

## Solution structure

```
src/
  DyeHouseERP.Domain/         entities, enums, domain exceptions - zero external dependencies
  DyeHouseERP.Application/    CQRS commands/queries (MediatR), validators (FluentValidation), DTOs
  DyeHouseERP.Infrastructure/ auth/current-user, clock - no EF Core here
  DyeHouseERP.Persistence/    EF Core DbContext, entity configs, numbering engine, ledger services, migrations
  DyeHouseERP.API/            controllers, JWT auth, Swagger, Serilog, global exception middleware
  DyeHouseERP.Web/            React + TypeScript + Tailwind, RTL Arabic-first
tests/
  DyeHouseERP.UnitTests/          domain rule tests (no DB needed)
  DyeHouseERP.IntegrationTests/   API tests over a real SQL Server (WebApplicationFactory)
tools/                            structural verification sweeps + headless UI smoke tests
```

## Modules (all live in the sidebar, Arabic-first)

- **Master data** — Customers, Suppliers, Items (KG or Meter as the item's own unit), Warehouses, Materials & Chemicals, Operating Supplies, Users & Permissions, Company Settings.
- **Raw receiving (الرسائل / messages)** — each customer delivery becomes a numbered Raw Message; inspection gate (accepted/rejected); every line carries its own live remaining KG/Meter derived from the ledger.
- **Job Orders (أوامر التشغيل)** — `ClosedLine` (الخط المقفول) or `OpenLine` (على المفتوح); a fully admin-configurable stage engine (stage definitions snapshotted onto each order); execution, stage approval, reprocessing, separates.
- **Manual raw allocation** — when consuming raw material for a Job Order the user explicitly picks the message(s) and quantities. **There is no FIFO and no automatic picking**, and allocation may span several messages.
- **External Processing (تشغيل خارجي)** — material leaving the store for a reason other than production consumption: return to customer, external processing (with party, stage, cost, expected return, partial/full return tracking, cancellation), or raw sale. Every movement draws from one user-chosen message.
- **Customer-to-customer transfers** — move stock between customers on the ledger without ever editing the original message.
- **Stock adjustments** — with the permissioned, audited negative-stock override.
- **Production → Ready Goods (المخزون الجاهز)** — completed quantities are transferred into the ready-goods store; live balance per job order.
- **Deliveries** — Draft → Prepared → Delivered, cancellation posts a linked reversal; deducts from Ready Goods.
- **Invoices & customer statement** — approve → posts to the customer ledger; cancellation posts a reversal.
- **Treasury & Cheques** — accounts, receipts/payments/transfers; incoming/outgoing cheques with a full status lifecycle (deposited, cleared, bounced, endorsed, cancelled).
- **Purchases** — supplier requests/orders/receiving, supplier invoices and payments, balances and statements.
- **Payroll** — departments, employees, monthly runs, allowances/deductions, approve/post/cancel, payslip PDF.
- **Cost accounting** — per Job Order: estimated vs. live-actual cost (material + preparation + external processing + operating cost), cost per KG and per Meter, approval.
- **Reports** — inventory movements, negative-stock overrides, report builder (whitelisted entities only), Excel/PDF everywhere, in-app print previews with QR codes.
- **Period closing** — blocks every ledger-posting path inside a closed period.
- **Approval center** — one read-only inbox over every pending approval in the system.

## Core invariants (enforced in code)

- **Append-only ledgers.** `InventoryTransaction` (fabric), `MaterialTransaction` (chemicals), `CustomerLedgerEntry`, `SupplierLedgerEntry`, `TreasuryTransaction`. A "balance" is always a live sum — never a mutable column. Corrections are linked reversal rows, never edits or deletes.
- **KG and Meter are separate units.** `UnitOfMeasure` is only KG or Meter; both quantity columns can coexist on a movement and are never reconciled against each other. **No automatic KG↔Meter conversion exists.**
- **No FIFO, anywhere.** Every consumption (production allocation, external processing, transfer, adjustment, delivery) requires the user to name the exact source (message / ready-goods lot). Nothing is auto-picked oldest-first.
- **Customer ownership is a ledger dimension**, not a field edited on the message — transfers and external movements never mutate `RawMessage.CustomerId`.
- **Reversals, not edits.** Cancelling a delivered delivery or issued invoice posts an equal-and-opposite row referencing the original; finalized documents are locked (`AuditableEntity.Lock()`).
- **Optimistic concurrency.** Transactional entities carry EF `[Timestamp]` `RowVersion` tokens (16 tokens across 15 entity files); `DbUpdateConcurrencyException` maps to HTTP 409 CONCURRENT_UPDATE.
- **Global stock lock.** Allocation/issue flows serialize through `IAllocationLockService` (`sp_getapplock`, key `Stock:{warehouse}:{item}:{customer}:{product}:{meter}`) instead of relying on row locks; lock timeouts map to HTTP 409 LOCK_TIMEOUT.
- **Server-enforced permissions.** 95 permission constants (`Domain/Common/Permissions.cs`); every controller action carries its own `[Authorize]` policy; the Users screen mirrors the full list. Deactivated users / changed permissions are rejected on already-issued tokens by a live session check.
- **Everything is audited.** `AuditSaveChangesInterceptor` records every Create/Update/Delete plus login events.
- **Numbering engine.** Sequential document numbers generated through `sp_getapplock` (`SqlDocumentNumberGenerator`) — safe under concurrency.
- **Hardened auth.** JWT with no development fallback key (`JwtKeyValidator` rejects placeholder keys), login throttling middleware (case-insensitive, 4 KB body cap), forwarded headers only from trusted proxies.

## Getting started

### 1. Backend

Requires the **.NET 9 SDK** and a **SQL Server** instance (local install, SQL Server Express/LocalDB, or remote).

Point the API at your server — edit `src/DyeHouseERP.API/appsettings.Development.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=DyeHouseERP;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

(`Trusted_Connection=True` for Windows/LocalDB integrated auth, or `User Id=...;Password=...;` for SQL login.)

```bash
dotnet build

dotnet tool install --global dotnet-ef   # once

dotnet ef migrations add InitialCreate \
  --project src/DyeHouseERP.Persistence \
  --startup-project src/DyeHouseERP.API

dotnet run --project src/DyeHouseERP.API
```

Swagger UI opens at `https://localhost:7100/swagger`. In Development the API applies migrations and seeds document-numbering sequences, default warehouses, and a default admin login (`admin` / `Admin@12345`, created by `UserSeeder` only when no user exists — change the password immediately after first login).

> **Production note.** `Program.cs` applies migrations on startup for development convenience; in production prefer running migrations as an explicit deploy step and do not rely on the Development seeder.

### 2. Frontend

Requires Node 20+.

```bash
cd src/DyeHouseERP.Web
npm install
npm run dev
```

Opens at `http://localhost:5173`, proxying `/api` to the backend.

> **Hosted-preview note.** The repository root is the folder containing `DyeHouseERP.sln`, but `package.json` lives in `src/DyeHouseERP.Web/`. Point a preview tool at the real directory instead of moving the manifest:
>
> ```bash
> freebuff-preview set-install "cd DyeHouseERP/src/DyeHouseERP.Web && npm install"
> freebuff-preview set "cd DyeHouseERP/src/DyeHouseERP.Web && npx vite --host 0.0.0.0 --port ${PORT:-5173}" 5173
> freebuff-preview set-build  "cd DyeHouseERP/src/DyeHouseERP.Web && npx vite build"
> freebuff-preview start
> ```
>
> Vite's host allow-list is configured in `vite.config.ts` for the external preview hostname; verify with `node tools/ext-preview-check.js https://<preview-host>`.

### 3. Tests

Domain unit tests need no database:

```bash
dotnet test tests/DyeHouseERP.UnitTests
```

Integration tests need a real SQL Server (the numbering engine's `sp_getapplock` has no in-memory equivalent). They create/drop their own database on every run — never point them at real data:

```bash
DYEHOUSE_TEST_CONNECTION="Server=localhost,1433;Database=DyeHouseERP_IntegrationTests;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;" \
  dotnet test tests/DyeHouseERP.IntegrationTests
```

## Verification performed in this repository

- `dotnet build --configuration Release` — 0 errors / 0 warnings from project code.
- `dotnet test tests/DyeHouseERP.UnitTests` — **133/133 passing**.
- Integration tests compile; they require a reachable SQL Server and cannot run in this repo's dev environment. **No SQL Server integration testing has been performed here.**
- `dotnet ef migrations has-pending-model-changes` — clean (model snapshot in sync).
- Frontend: `npx tsc -b --noEmit` — clean.
- Structural sweeps (run as scripts, not by eye):
  - `node tools/wiring-sweep.js` — every UI download/import call resolves to a real controller action; no action lacks a permission policy (only login and public company settings are intentionally anonymous); no duplicate routes.
  - `node tools/duplicate-sweep.js` — no duplicate permission values, i18n keys, entity names, or indexes; `DbSet`s match the persistence interface in both directions.
  - `node tools/hooks-check.js` — TypeScript-AST check for React rule violations a compiler misses (hooks in branches/loops/callbacks, hook-name shadowing).
  - `node tools/ui-smoke.js` / `ui-smoke-tabs.js` — headless-Chromium pass over the real routes (mount, RTL/LTR flip, export/import controls), counting backend-less API failures separately.
- i18n: both dictionaries in `Web/src/i18n/index.tsx` at **557/557 key parity**. Some pages still carry inline Arabic literals alongside the dictionary; completing that extraction is mechanical work, not new architecture.

## Known operational notes

- **PDF Arabic shaping**: QuestPDF renders Arabic text but needs an Arabic-capable font on the host. On bare Linux containers, register one (e.g. Noto Naskh Arabic) via `QuestPDF.Drawing.FontManager.RegisterFont` at startup; Excel export is unaffected.
- **Backups / database maintenance** are SQL Server-side responsibilities; the application ships none.
- List queries that hydrate full DTOs per row are simple and correct, not tuned for very large datasets (noted inline where it matters most).
- No hardware barcode-scanner integration by design; QR codes on print documents encode `/scan/:type/:id` quick-view URLs that any phone camera or keyboard-wedge reader can open.
