# Phase 7 — Purchasing, chemicals, operating supplies, HR & payroll

> **Mode:** verification-first. **No commit, no push, no migration was applied.**
> **Phase status: `Verified` for everything covered by the unit suite and the static checks below.**
> Scope: supplier procurement (section 35) · factory-owned chemicals and operating supplies (section 27) ·
> HR, attendance and payroll, **fully configurable** (sections 28–39).

Branch: `freebuff/8c663d5dafcf97d023c3991e` · Baseline HEAD: `abb4426` · Date: 2026-10-14
Predecessors: `docs/audit/phase1-report.md` … `phase5-report.md`, `progress.md`.

---

## A. What this phase had to deliver

| # | Requirement | Where it lives |
|---|---|---|
| 1 | **Purchasing & suppliers** — supplier master, purchase orders, receipts, supplier invoices, payments, outstanding balances, statements, audit, permissions, validation | `SuppliersController`, `PurchasesController`, `Purchases/Commands/*`, `Purchases/Queries/*`, `PurchasesPage` |
| 2 | **Drafts move nothing; posting moves once, atomically** | `PurchaseOrder`, `PurchaseReceipt`, `SupplierInvoice` status guards; `PostSupplierInvoiceCommand` |
| 3 | **Chemicals (factory-owned)** — items/units, ledger movements, actual consumption, stage (Job Order) association, cost, no negative stock outside the authorised exception | `MaterialIssue`/`MaterialTransaction`, `CreateMaterialIssueCommand`, `MaterialLedgerService` |
| 4 | **Operating supplies (factory-owned)** — items, units, movements, consumption, **department** association, cost **derived from transactions**, not a stage workflow | `SupplyIssue`/`SupplyIssueLine`, `SupplyIssueCommands`, `SupplyIssueDtoBuilder` |
| 5 | **HR, attendance & payroll — fully configurable** (no hard-coded shift counts, rules, rates, allowances, deductions) | `HrMasterCommands`, `AttendanceRule`, `PayrollCalculator`, `PayrollLifecycleCommands` |
| 6 | **HR shifts are separate from the three production execution shifts** | `HrShift` vs `ProductionShift` (distinct entities) |
| 7 | **Sensitive payroll data gated server-side; granular permission on every endpoint** | `Permissions`, `HrController`, `PayrollController` |

---

## B. Approach: verify first, change only what is genuinely wrong

The phase brief asks for five specified behaviours and a scope inventory. Every one of the five was
**implemented already and correct** on inspection; the phase therefore needed **test coverage, not code
changes**. This was the deliberate reading of "touch only what this phase needs": a missing test is a real
gap, but rewriting working, wired, permission-checked code to "make the phase look done" is not.

Findings from the scope inventory (read the code, then confirmed with the checks in §E):

- **Purchasing.** Supplier master (`SuppliersController`, `suppliers.view/create/edit`); purchase orders
  with the full draft→submit→approve→receive lifecycle and its own permissions
  (`purchases.create/submit/approve/receive/edit_after_receipt/cancel`); receipts; supplier invoices
  (`purchases.invoice`); supplier payments (`purchases.pay`); **outstanding balances**
  (`GetSupplierBalancesQuery`, `GET api/purchases/supplier-balances`) and the **supplier statement/ledger**
  (`GET api/purchases/suppliers/{id}/ledger`, `…/ledger/export`), all with Excel exports
  (`purchases.export`). Supplier liabilities are the `SupplierLedgerEntry` rows.
- **Chemicals.** `Material` of `MaterialKind.Chemical`; the append-only `MaterialTransaction` ledger is the
  balance; `CreateMaterialIssueCommand` refuses any issue that would overdraw the balance
  (`NegativeStockException`) under the stock lock and outside closed periods; the issue is tied to a Job
  Order (the "stage" association) and priced from the material/transaction for cost tracking.
- **Operating supplies.** `MaterialKind.OperatingSupply`; `SupplyIssue` draft → `AddSupplyIssueLineCommand`
  → `PostSupplyIssueCommand`, with an optional **department** association; the cost is the `UnitCost`
  recorded on the posted `MaterialTransaction` rows. A chemical can never be booked as an internal supply
  (the line handler refuses a non-`OperatingSupply` material), which is the correct asymmetry: consumables
  may be charged to a Job Order, but a dye is never routed through the supplies store.
- **HR/payroll.** Every rate, factor and rule is data: `AttendanceRule` (daily hours, working days/month,
  grace, per-minute penalties, absence-day factor, overtime grace + multipliers, working week, paid-leave
  and overtime-approval switches), `HrShift` (its own timing, break, working days), `PayComponent`,
  `ContributionRule`, `LeaveType`. `PayrollCalculator` is a **pure function of those masters** — no factory
  amount or rate is coded. `HrShift` and `ProductionShift` are distinct entities and the payroll path never
  reads a production shift.
- **The negative-stock exception is deliberately fabric-only.** `NegativeStockOverride` is bound to a
  `RawMessageId` + `CustomerId` (customer-owned raw lots), and the only override mechanism is
  `ApprovalRequestGuard`, which enforces separation of duties (a requester can never approve their own
  request). Factory-owned chemical stock therefore has **no self-service override path at all** — the
  required "exception needs a separate approver" rule is satisfied by that guard.

**No production code needed a fix.** The only artefact added is the test file in §C.

---

## C. What this session produced

**`tests/DyeHouseERP.UnitTests/Phase7PurchasingMaterialsHrTests.cs`** — 11 tests across five suites, each
pinning one brief requirement against the **real** handlers or the **real** domain rules:

- `Phase7PurchasePostingTests` (2)
  - a **draft** supplier invoice writes **zero** `SupplierLedgerEntry` rows; **posting** writes **exactly
    one** debit equal to the invoice total (never one per line); **re-posting is refused**
    (`DomainException`) and writes nothing a second time;
  - a **draft** purchase order creates **no** stock movement and **no** financial entry.
- `Phase7ChemicalStockTests` (3)
  - issuing **more than the balance** is refused with `NegativeStockException` and leaves **no** document and
    **no** OUT movement;
  - factory-owned material stock has **no override/approval property** on the command (so it can never be
    waved through), and the system's only override record is raw/customer-scoped;
  - the exception workflow **requires a stored approval** (no approval id ⇒ refused) and **refuses
    self-approval** even for a user holding *both* negative-stock permissions.
- `Phase7OperatingSupplyTests` (2)
  - a draft supply issue moves no stock; **posting writes exactly one OUT row per line** with the unit cost
    taken from the material/transaction (the **consumption cost comes from the recorded transaction**, not a
    value typed at report time); the issue total is derived from those rows; re-posting is refused;
  - a production chemical **cannot be booked as an internal operating supply**.
- `Phase7HrPayrollConfigurationTests` (3)
  - **changing the active attendance policy changes the calculated payroll with no code change** (same
    employee, same salary, one absence: 6000/25 = 240/day, so the strict policy deducts exactly one day more
    than the lenient one);
  - `HrShift` and `ProductionShift` are separate entity types (an HR shift carries its own planned minutes;
    a production shift carries no HR attendance rules);
  - adding a **production execution shift does not change HR attendance or payroll**.
- `Phase7HrPayrollPermissionTests` (1)
  - **every** HTTP action on `HrController` and `PayrollController` carries its **own** granular permission
    policy, none is gated by role, each policy names a real `Permissions` constant, and the sensitive
    `payroll.view/approve/post` + `hr.view/manage` rights are all present.

---

## D. Migrations

**None created, and none required.** This phase adds tests only; it changes no entity, no mapping and no
`DbSet`. Confirmed clean: `dotnet ef migrations has-pending-model-changes` returned *"No changes have been
made to the model since the last migration."* (see §E). The last migration is still
`20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement`.

---

## E. Verification (commands actually run, with results)

| Check | Command | Result |
|---|---|---|
| Backend build (test project) | `dotnet build tests/DyeHouseERP.UnitTests` | **PASS** — 0 errors, 3 warnings (the pre-existing `ExceptionHandlingMiddlewareTests`/`EditPermissionSweepTests` nullability set) |
| New suite only | `dotnet test … --filter FullyQualifiedName~Phase7` | **PASS — 11 / 11** |
| Full unit suite | `dotnet test tests/DyeHouseERP.UnitTests` | **PASS — 806 / 806** (+11 this session) |
| Model vs snapshot | `dotnet ef migrations has-pending-model-changes` | **`No changes have been made to the model since the last migration.`** |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **PASS** — 2351 modules, chunk-size warning only |
| Permission parity | `node tools/permissions-parity-check.cjs` | **PASS — 118 / 118, 0 failures** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **PASS — ar 901 / en 901, 0 failures** |
| Duplicate/DbSet sweep | `node tools/duplicate-sweep.js` | **PASS** — 88 DbSets on the context = 88 on the interface |
| Wiring sweep | `node tools/wiring-sweep.js` | only login + public settings policy-free (unchanged) |
| Edit-permission audit | `node tools/edit-permission-audit.cjs` | informational, no failures |

**Not run (with reasons, never claimed as passes):**

- **Integration tests** (`tests/DyeHouseERP.IntegrationTests`) need a real **SQL Server** via
  `DYEHOUSE_TEST_CONNECTION`; none is available here (no Docker, no instance) — unchanged from Phase 1
  §B.3. So the SQL-level behaviour of the supplier-ledger unique indexes, `sp_getapplock` serialisation of
  the material/supply locks, and the `rowversion` concurrency tokens is verified by construction, not at
  runtime.
- **Frontend lint** — `npm run lint` → `eslint: not found` (Phase 1 finding F.B.4, still open, Phase 8).
- **Browser/PDF rendering** — no browser tooling here; the HR/payroll and supplies screens were not opened.
- **Migrating a live database** — migrations are created, never applied, per the brief.

---

## F. Phase 7 status

**`Verified`.** All five briefed behaviours are implemented, wired end-to-end (server contract + UI +
exports + permissions) and now covered by **11 dedicated tests**; the full unit suite (**806/806**),
permission parity, i18n parity, duplicate and wiring sweeps, the model/snapshot check and the frontend build
all pass.

**Not run** (and therefore not claimed): the SQL-Server integration suite and frontend lint — both blocked by
the environment (Phase 1 §B.3/§B.4) — plus browser rendering (Phase 8 Arabic-font item).

No commit, no push, and no migration was applied.
