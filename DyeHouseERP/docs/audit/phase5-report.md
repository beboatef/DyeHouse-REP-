# Phase 5 — Ready Goods, partial delivery, delivery statement, sub-customer permits & returns

> **Mode:** implementation completed in the working tree. **No commit, no push, no migration was applied.**
> **Phase status: `Verified` for the units tested here; two checks are `Not run` (no SQL Server, no frontend lint).**
> Scope: spec sections **29–33** (Ready Goods warehouse movements, partial delivery + بيان تسليم,
> the sub-customer permit, and customer returns / a Job Order raised from a return).

Branch: `freebuff/8c663d5dafcf97d023c3991e` · Baseline HEAD: `abb4426` · Date: 2026-10-14
Predecessors: `docs/audit/phase1-report.md` … `phase3-report.md`, `progress.md`.

---

## A. What this phase had to deliver

Per the Phase 1 scope split (§F) and the user's brief:

| # | Requirement | Where it lives |
|---|---|---|
| 1 | **مخزن الجاهز** — the Ready Goods warehouse as a stock authority of its own | `ReadyGoodsTransfer`, ready balance derived from the ledger, `/ready-goods/*` |
| 2 | **التسليم الجزئي** — a partial delivery settles only what the Job Order's *fixed* original raw quantity still owes | `Delivery`/`DeliveryLine.OriginalRawQuantity*`, `ProductionOrder.EnsureOriginalRawWithinLimit` |
| 3 | **بيان التسليم** — the delivery statement: per line, original raw / settled / remaining / ready / bales / scraps / 2nd grade / calculated waste, **per unit, never mixed** | `GetDeliveryStatementQuery`, `/deliveries/{id}/statement[/pdf]`, `DeliveryStatementPrintPage` |
| 4 | **تصاريح العميل الفرعي** — a sub-customer permit that references a real primary permit and posts no stock | `Delivery.PrimaryDeliveryId/PrimaryCustomerId`, `CreateDeliveryCommand.ResolvePrimaryPermitAsync`, `SetCustomerParentCommand` |
| 5 | **المرتجعات** — returns land only in raw material stock, record ownership + reference document, and may optionally raise a new Job Order that states who pays | `CreateCustomerReturnCommand`, `CancelCustomerReturnCommand`, `CreateProductionOrderFromReturnCommand`, `CustomerReturnsPage` |

Cross-cutting rules the phase had to respect: the ledger stays append-only (corrections are compensating
movements), quantities are never converted between KG and Meter, stock-posting paths take the stock lock
and fall outside closed periods, and every new stock document gets its own permission + audit entry.

---

## B. State on entry (the phase was interrupted mid-implementation)

`git status` showed a large body of **uncommitted** work spanning Phases 2–5 (≈118 files), and
`progress.md` documented only through Phase 3. The interrupted action was the creation of the Phase 5 test
file (`Phase5ReadyGoodsDeliveryReturnsTests.cs`), and the in-flight work was "updating the returns query
and the controllers".

Inventory taken from the tree:

**Already implemented and wired (verified by reading the code and by build/route checks):**

- Ready-goods **transfers/balance** and the **ready-goods→raw** document
  (`ReadyGoodsRawReturn` entity + `ReadyGoodsRawReturnConfiguration` + DbSet + `DocumentType.ReadyGoodsRawReturn`
  + `DocumentSequenceSeeder` row `RGR`), `ReturnReadyGoodsToRawStockCommand`, `GetReadyGoodsRawReturnsQuery`,
  `Permissions.ReadyReturnToRaw`, `/api/ready-goods/raw-returns` (+ register), `ReadyGoodsPage` tab.
- **Partial delivery**: `DeliveryLine.OriginalRawQuantityKg/Meter`, the fixed original on the order,
  `ProductionOrder.EnsureOriginalRawWithinLimit`, remaining computed once in
  `GetDeliveriesQueryHandler.ReadRemainingOriginalRawAsync` and reused by the create-time guard **and** the
  statement; `DeliveriesPage` entry + remaining column.
- **Delivery statement**: `GetDeliveryStatementQuery` + `DeliveryStatementDto`, controller endpoints
  `GET /api/deliveries/{id}/statement` and `.../statement/pdf`, `exports.ts` `statementPdf`,
  `App.tsx` route `/print/delivery-statement/:id`, `DeliveryStatementPrintPage` with **configurable columns**.
- **Sub-customer permits**: `Delivery.PrimaryDeliveryId/PrimaryCustomerId`, migration-partial-index +
  self-FK, `CreateDeliveryCommand` primary-permit resolution (must exist, must be non-cancelled, must be
  the customer's stored parent), `Customer.ParentCustomerId` + `CK_Customers_ParentNotSelf`,
  `SetCustomerParentCommand` (app-level cycle detection), `CustomersPage` parent picker,
  `DeliveriesPage` primary-permit picker + display.
- **Returns**: `CreateCustomerReturnCommand` (+ mandatory raw-warehouse destination, ownership, reference
  document), `CancelCustomerReturnCommand`, `CreateProductionOrderFromReturnCommand` (mandatory
  `CostBearer`, delegates to the canonical issue workflow), the returns query/DTO/controller (`referenceDocument`,
  `ownership`, raised orders), `CustomerReturnsPage` UI, `Permissions.RawReturn`.
- **Delivery idempotency**: `Delivery.DeliverRequestId` + unique filtered index + `MarkDeliveryDeliveredCommand`
  retry short-circuit.
- Migration `20261014042210_Phase5ReadyGoodsDeliveryReturns` created (**not applied**).

**Missing / unfinished:**

1. **The Phase 5 unit tests did not exist** — `Phase5ReadyGoodsDeliveryReturnsTests.cs` was never written
   (the interrupted action). This session created it (see §D).
2. The model→snapshot state had to be reconciled (see §C — an incident, not a pre-existing defect).

---

## C. Incident during this session, and its repair (recorded honestly)

While checking for model drift I ran `dotnet ef migrations has-pending-model-changes` and then
`dotnet ef migrations add`/`remove` **with `--no-build` against a stale `bin/Debug` assembly**. EF therefore
believed the *last* migration was `20261014015833_AddPhase2ItemConversionsAndSubCustomer`, and
`migrations remove` **deleted that (untracked) Phase 2 migration and reverted `ApplicationDbContextModelSnapshot.cs`**
to a pre-Phase-2 model. The "drift" that triggered this was itself a false positive caused by the stale
Debug assembly (its model predated Phase 5).

**Repair (all in the working tree):**

1. Removed the probe migration `__DriftProbe` that the same command had created.
2. Rebuilt `ApplicationDbContextModelSnapshot.cs` from the authoritative final model in
   `20261014042210_Phase5ReadyGoodsDeliveryReturns.Designer.cs` (the model that migration was generated from).
3. Recreated `20261014015833_AddPhase2ItemConversionsAndSubCustomer.cs` (Up/Down) and its `.Designer.cs`.
   The Up/Down were **derived, not remembered**: the model delta was computed by diffing the baseline
   snapshot against the final model and subtracting the known Phase 3 and Phase 5 changes, leaving exactly
   *Customer.ParentCustomerId (+ index, + check constraint, + self FK)* and *the `ItemUnitConversions` table
   (+ two check constraints, two indexes, FK to Items)* — which is what the Phase 2 report describes.
4. Proved the chain is coherent by generating the SQL for the three tail migrations and confirming each
   object is created exactly once, then rebuilding and re-running the whole suite.

Post-repair state: `has-pending-model-changes` → **clean**; migration script from
`20261013191146_AddProductionOrderIssueLink` applies Phase 2 → Phase 3 → Phase 5 with **no duplicate column
or table**.

> Note for reviewers: the per-migration `.Designer.cs` files in this tree (Phase 2/3/5) each carry the same
> final model, which is the state the previous session left them in. The executable `Up`/`Down` of each
> migration are distinct and correct; only the latest snapshot drives EF diffs.

---

## D. What this session produced

- **`tests/DyeHouseERP.UnitTests/Phase5ReadyGoodsDeliveryReturnsTests.cs`** — 22 tests across three suites:
  - `Phase5ReadyGoodsRawReturnTests` (6): validator requires a reason and a quantity; the move posts exactly
    two ledger rows (OUT of Ready Goods, IN to the raw lot) + one immutable document + one audit entry;
    it can never overdraw the ready lot (`NegativeStockException`, nothing written); it refuses another
    customer's lot; it refuses a destination that is not raw material stock; the register reads each row back
    with order/customer/lot/warehouse identity.
  - `Phase5DeliveryStatementTests` (7): original raw / settled / remaining are read correctly; a unit that was
    not recorded reports **null** and the waste is **null** (never a fabricated zero); waste is computed
    **per unit** from the operations (600−520−20−10 = 50 KG; 100−85−5−2 = 8 M) and never across units;
    printing twice moves no stock; `EnsureOriginalRawWithinLimit` accepts exactly the original, refuses more,
    and treats KG/Meter independently; a **second partial delivery may only settle the outstanding** original;
    a sub-customer permit names the primary permit + primary customer and nothing else of theirs; and it is
    refused when the customer is not actually a sub-customer or when the primary permit is cancelled.
  - `Phase5CustomerReturnTests` (9): a return posts an IN row into raw material stock and records ownership +
    reference document, touching no invoice/delivery; it refuses a non-raw destination and another customer's
    lot; a cancelled return requires a reason and cannot be cancelled twice; a Job Order raised from a return
    must state the cost bearer and is refused for a cancelled return (with the mediator asserted **not**
    called).
- Restored the Phase 2 migration + model snapshot described in §C.

No production code needed a fix — the interrupted implementation was already complete and correct on the
Build/tests; the only missing artefact was its test coverage.

---

## E. Verification (commands actually run, with results)

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build DyeHouseERP.sln -c Release` | **PASS** — 0 errors, **5 warnings** (the pre-existing `ExceptionHandlingMiddlewareTests`/`EditPermissionSweepTests` nullability set; unchanged from Phase 3) |
| Unit tests | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **PASS — 739 / 739** (+22 new; was 717) |
| New suite only | `... --filter FullyQualifiedName~Phase5` | **PASS — 22 / 22** |
| Model vs snapshot | `dotnet ef migrations has-pending-model-changes` (Debug build) | **`No changes have been made to the model since the last migration.`** |
| Migration SQL chain | `dotnet ef migrations script 20261013191146_AddProductionOrderIssueLink` | **PASS** — Phase 2/3/5 objects each created once (see §C) |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **PASS** — 2351 modules, chunk-size warning only |
| Permission parity | `node tools/permissions-parity-check.cjs` | **PASS — 117 / 117, 0 failures** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **PASS — ar 896 / en 896, 0 failures** |
| Duplicate/DbSet sweep | `node tools/duplicate-sweep.js` | **PASS** — 88 DbSets on context = 88 on interface, no duplicates |
| Edit-permission audit | `node tools/edit-permission-audit.cjs` | informational, no failures |

**Not run (with reasons, never claimed as passes):**

- **Integration tests** (`tests/DyeHouseERP.IntegrationTests`): they boot the API against a **real SQL Server**
  via `DYEHOUSE_TEST_CONNECTION`; none is available here (no Docker, no local instance) — unchanged from
  Phase 1 §B.3. So the SQL-level behaviour of the new unique filtered indexes
  (`Deliveries.DeliverRequestId`, `ReadyGoodsRawReturns.MovementNumber`), `sp_getapplock` serialization of the
  ready/raw lock pair, and the `CK_*` check constraints is verified by construction (script + model), not at runtime.
- **Frontend lint** — `npm run lint` → `eslint: not found` (Phase 1 finding F.B.4, still open, Phase 8).
- **Browser/PDF rendering** — the Arabic/RTL PDF gap (Phase 1 §D.9 / Phase 8) means the statement *PDF* is
  generated through the same unbranded `ReportExportService`; the on-screen print page renders normally but
  was not opened in a browser here.

---

## F. Pending Phase 4 dependency (recorded, not worked around)

The delivery **statement** reads second-grade and scraps/separates from the order's stage executions
(`ProductionOrderStageExecution.SecondGradeKg/Meter`, `SeparatesKg/Meter`). The server contract to *record*
them exists — `TransferToNextStageCommand` and `ProductionOrdersController` accept
`separatesKg/Meter` and `secondGradeKg/Meter`, `CloseWithOutput` stores them, and the DTOs expose them.
What is **not** wired is the **production-floor UI**: `ProductionOrderDetailPage`'s transfer form sends only
`outputKg/Meter`, `drawingNumber` and `notes`, and `client.ts`'s `transferStage` body type omits
`separatesKg/Meter` and `secondGradeKg/Meter`.

That capture surface is Phase 4 (production floor) work, so per the brief it is left as a **pending
dependency** rather than patched here. Until Phase 4 wires it, a delivery statement will legitimately show
"—" for second grade / scraps on orders whose floor entry never recorded them — the columns are present and
correct, they are simply empty.

Also carried over and **not** touched (Phase 4 open items from the Phase 1 report): the `production.scrap`
permission question and the removal of the legacy `StageExecutionCommands.cs` start/complete/skip endpoints
(already deleted in this tree; `AllowSkip` dropped by the Phase 5 migration).

---

## G. Files touched by Phase 5 (working tree, uncommitted)

**New:** `Domain/Entities/ReadyGoodsRawReturn.cs`, `Persistence/Configurations/ReadyGoodsRawReturnConfiguration.cs`,
`Application/ReadyGoods/Commands/ReturnReadyGoodsToRawStockCommand.cs`, `Application/ReadyGoods/DTOs/ReadyGoodsRawReturnDtos.cs`,
`Application/Deliveries/Queries/GetDeliveryStatementQuery.cs`, `Application/Customers/Commands/SetCustomerParentCommand.cs`,
`Application/CustomerReturns/Commands/CreateProductionOrderFromReturnCommand.cs`,
`Web/src/pages/print/DeliveryStatementPrintPage.tsx`,
`Persistence/Migrations/20261014015833_AddPhase2ItemConversionsAndSubCustomer*.cs` (restored, §C),
`Persistence/Migrations/20261014031119_Phase3*`, `Persistence/Migrations/20261014042210_Phase5ReadyGoodsDeliveryReturns*.cs`,
`tests/DyeHouseERP.UnitTests/Phase5ReadyGoodsDeliveryReturnsTests.cs` (this session).

**Modified (highlights):** `Delivery.cs`, `CustomerReturn.cs`, `ProductionOrder.cs`,
`ProductionOrderStageExecution.cs`, `ProductionStageDefinition.cs`, `SharedEnums.cs`, `Customer.cs`,
`Permissions.cs`, `ApplicationDbContext.cs` + `IApplicationDbContext.cs`, `CustomerReturnConfiguration.cs`,
`DeliveryConfiguration.cs`, `ProductionOrderConfiguration.cs`, the Deliveries/ReadyGoods/CustomerReturns
commands, queries, DTOs and controllers, `DocumentSequenceSeeder.cs`, `permissions.ts`, `api/client.ts`,
`api/exports.ts`, `i18n/index.tsx`, `App.tsx`, `DeliveriesPage.tsx`, `CustomerReturnsPage.tsx`,
`ReadyGoodsPage.tsx`, `CustomersPage.tsx`.

---

## H. Phase 5 status

**`Verified`** for everything covered by the unit suite and the static checks above. The five Phase 5
requirements are implemented, wired end-to-end (server contract + UI + exports) and now covered by 22
dedicated tests; the Release build is clean, **739/739** unit tests pass, permission parity, i18n parity,
duplicate sweep and the model/snapshot all pass, and the migration chain is coherent.

**Not run** (and therefore not claimed): the SQL-Server integration suite and frontend lint — both blocked
by the environment (Phase 1 §B.3/§B.4), plus browser/PDF rendering (Phase 8 Arabic-font item). One explicit
**pending Phase 4 dependency**: recording second grade / separates from the production-floor UI.

No commit, no push, and no migration was applied.
