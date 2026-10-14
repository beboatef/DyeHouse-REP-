# Phase 3 — Receiving, Formation Requests & Raw Allocation

Baseline: `abb4426`, branch `freebuff/8c663d5dafcf97d023c3991e`. No commit, no push, no migration applied to any database.

---

## A. Scope actually touched

| Scope item | Verdict | What was done |
|---|---|---|
| 1. Raw-material receiving | **Fixed a real defect + closed two gaps** | The receipt already posted exactly one IN row per measured unit, with a system-generated number and no mandatory inspection. Added **(a) the receiving idempotency token** (§C.2), **(b) the optional colour field** (§C.3). Colour was the one field in the phase's list that the receipt did not carry. |
| 2. Formation Requests (planning) | **Verified (no code change)** | Customer, item, colour/specs, planned quantities + units, multiple basins with planned **and actual** quantities, notes, status workflow, revision and audit all already exist and are correct: the module writes **zero** stock (§D.2). |
| 3. Stock issue → Production Order | **Verified (no code change)** | `IssueRawMaterialCommand` already posts the OUT row, creates the Job Order from the ACTUAL issued quantity and links issue + optional request/basin in **one** transaction; there is no POST create-order endpoint at all (§D.3). |
| 4. Raw-material allocation | **Backend verified; the DISPLAY was missing and was added** | The 100% gate was already enforced on every production-start path from 8 call sites. Required / allocated / remaining / status were not shown anywhere — added, derived from the **same** domain rule the guard uses (§C.4). |
| 5. Traceability | **Verified (no code change)** | Receipt → issue → Job Order and receipt → Formation Request are stored references, not conventions (§D.5). |
| 6. Permissions | **Verified (no code change)** | Receiving, inspection, cancellation, issue and adjustment each carry their own server-side policy; the negative-stock exception additionally needs **two** permissions plus a stored approval (§D.6). |
| 7. Edit / cancel rules | **Verified (no code change)** | A posted receipt is cancelled (never edited/deleted) by a controlled, reason-bearing reversal; a draft Formation Request is editable and a submitted/approved one is revised; the open Job Order edits only descriptive fields (§D.7). |
| — Phase-1 finding **F.7** (deferred *to this phase*) | **CONFIRMED DEFECT — FIXED** | `SqlAllocationLockService` had no constructor: every stock lock threw a `NullReferenceException` before reaching `sp_getapplock` (§C.1). |

---

## B. Build, tests and migrations (actual results)

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build DyeHouseERP.sln -c Release --no-incremental` | **PASS** — 0 errors, **5 warnings** (baseline was 7; see note) |
| Unit tests | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **PASS** — **725/725** (Phase 2 had 715; **+10 new**) |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **PASS** — exit 0 |
| i18n key parity | `node tools/i18n-parity-check.cjs` | **PASS** — ar 896 / en 896, 0 failures (+9 new keys) |
| Permission parity | `node tools/permissions-parity-check.cjs` | **PASS** — backend 116 / frontend 116, 0 failures |
| Migration model check | `dotnet ef migrations has-pending-model-changes -…--configuration Release --no-build` | **PASS** — "No changes have been made to the model since the last migration." |
| Migration SQL generation | `dotnet ef migrations script 20261014015833… 20261014031119…` | **PASS** — emits valid T-SQL (proof the migration compiles; **not applied**) |
| Frontend lint | `npm run lint` | **Not run** — pre-existing: `eslint` is not installed and there is no config (Phase 1 finding F.B.4). |
| Integration tests | — | **Not run** — need a real SQL Server (Phase 1 limitation, unchanged). |

**Warning count went DOWN from 7 to 5.** The two warnings that disappeared are exactly `CS8618` + `CS0649` on `SqlAllocationLockService._context` — the compiler was already reporting the F.7 defect. The remaining 5 are the pre-existing ones in test files (`xUnit1031` ×2, `CS8602`, `CS8600`, `CS8603`) and are untouched by this phase.

---

## C. Code changes (new / modified), and why each is required by the phase

### C.1 THE defect: the stock lock could never actually lock (F.7)

**`src/DyeHouseERP.Persistence/Numbering/SqlAllocationLockService.cs`**

```csharp
private readonly ApplicationDbContext _context;      // declared …
// … with NO constructor, so DI built the service with a null context.
public SqlAllocationLockService(ApplicationDbContext context) => _context = context;   // ← added (line 42)
```

`AcquireAsync` / `AcquireManyAsync` / the period lock helpers all start with
`(SqlConnection)_context.Database.GetDbConnection()`. With a null context that throws a
`NullReferenceException` **before** `sp_getapplock` is ever called — on the real SQL Server, for
**every** stock posting: receiving, raw issue, allocation, transfers, adjustments, ready goods,
deliveries, the period Close/Reopen. The "concurrent postings serialise on the same lot" guarantee
therefore held only in the unit tests, which substitute a recording lock double for this class
(`RecordingStockLock`), while the integration suite — the only place the real service runs — is
`Not run` because no SQL Server exists in this workspace.

It is registered `services.AddScoped<IAllocationLockService, SqlAllocationLockService>()`
(`Persistence/DependencyInjection.cs:36`), and this phase's "concurrent issues cannot drive stock
negative" requirement depends on it, so it is fixed here rather than deferred. The fix mirrors the
sibling `SqlDocumentNumberGenerator`, which has always had its constructor
(`SqlDocumentNumberGenerator.cs:27`). Regression test:
`The_Stock_Lock_Service_Is_Constructed_With_Its_DbContext` (§E).

### C.2 Receiving idempotency (scope item 1: "once, idempotent against retries")

The canonical issue path already carried a client idempotency token with a unique-index backstop;
receiving — also a stock posting — had none: a double-submitted or retried confirmation wrote the
physical delivery into stock **twice** (two receipts, two IN rows).

- **`Domain/Entities/RawMessage.cs`** — `ReceiveRequestId` (`:38`) set once in the constructor (`:69`).
- **`Persistence/Configurations/RawMessageConfiguration.cs`** — unique **filtered** index
  `IX_RawMessages_ReceiveRequestId … WHERE [ReceiveRequestId] IS NOT NULL` (`:41-42`), so every
  historical row keeps its `NULL` and is unaffected.
- **`Application/RawReceipts/Commands/CreateRawMessageCommand.cs`** — optional `Guid? RequestId`
  (`:32`); the handler looks the token up **before** the period guard (`:89-98`) and returns the
  receipt that was already posted instead of posting a second movement. A retry of an
  already-posted receipt therefore still succeeds after the period is closed.
- The DTO is now produced by **one** shared loader (`LoadDtoAsync`, `:167`) used by both the normal
  and the retry path, so a retry answers with the lot's **live ledger balance** rather than a copy
  of the originally received quantity.
- **Frontend** — a per-form token (`RawMessagesPage.tsx:52`, minted with `crypto.randomUUID()` and
  refreshed after a successful post), sent as `requestId` (`client.ts` `RawMessagesApi.create`).

### C.3 Receiving colour, where applicable (scope item 1's field list)

The only field of the phase's receiving list that the receipt did not carry. The screenshot of the
existing screen (`erp-screenshots/07-raw-messages.png`) shows the factory receives by **item** (100%
cotton, polyester 67%, blend, rib, jersey…) with no colour column, and the item master has no colour
attribute — so this is deliberately **optional**, descriptive traceability data, never a stock
dimension (the ledger still keys on message + item + customer).

- **`Domain/Entities/RawMessage.cs`** — `RawMessageLine.Color` (`:186`), trimmed, `null` when blank;
  `AddLine(..., string? color = null)` (`:74`).
- **`Persistence/Configurations/RawMessageConfiguration.cs`** — `nvarchar(100)`, nullable (`:69`).
- **`DTOs/RawMessageDto.cs`** — `RawMessageLineInput.Color` (validated ≤100) and
  `RawMessageLineDto.Color`; `GetRawMessagesQuery` projects it.
- **`API/Controllers/RawMessagesController.cs`** — the printable receipt (`:112`) and the
  register/warehouse-balances export (`:47`) now carry a `Color / اللون` column.
- **Frontend** — a colour input per line (`RawMessagesPage.tsx:264`), a colour column in the receipt
  table (`:337`) and in the in-app print preview (`print/RawMessagePrintPage.tsx`).

### C.4 Required / allocated / remaining / allocation status (scope item 4's "display")

The backend refusal existed; nothing ever showed the user **why** it refuses.

- **`Application/ProductionOrders/DTOs/ProductionOrderDto.cs`** — `AllocatedQuantityKg/Meter`,
  `RemainingToAllocateKg/Meter`, `IsRawAllocationComplete` (`:110-129`).
- **`Application/ProductionOrders/Queries/GetProductionOrderByIdQuery.cs`** — computed from the
  allocation rows that were just read back, with completeness decided by **the domain's own rule**
  (`order.IsRawAllocationComplete(...)`, `:89`) — the very call `RawAllocationGuard` makes before it
  lets production start, so the screen and the server can never disagree. Remaining is clamped at 0
  (an over-allocated order is complete, never "−50 remaining"). Both the list and the detail query
  share this loader, so the figure is consistent everywhere.
- **`Web` `ProductionOrderDetailPage.tsx:275-312`** — four summary tiles (required / allocated /
  remaining / allocation status) above the existing manual lot picker, plus 6 new AR/EN i18n keys.
- The DTO additions also give "source + unit" a place to be read next to the totals: the allocation
  table below lists each source message with its KG / Meter amounts (pre-existing).

### C.5 The migration (created, **not applied**)

**`20261014031119_Phase3ReceivingIdempotencyAndColor`** — one migration, because both additions
belong to the receiving document and EF emits them together:

1. `ALTER TABLE [RawMessages] ADD [ReceiveRequestId] uniqueidentifier NULL`
   + unique filtered index `IX_RawMessages_ReceiveRequestId` (`WHERE [ReceiveRequestId] IS NOT NULL`).
2. `ALTER TABLE [RawMessageLines] ADD [Color] nvarchar(100) NULL`.

Both columns are **nullable**, so the migration is safe on an existing database and changes no
existing row. No other migration is required by this phase: everything else in phases 1–3 was already
in the model (the last schema-affecting change for allocation was
`20261013191146_AddProductionOrderIssueLink`, already applied to the model snapshot).

---

## D. Verification of the "already done" scope items (evidence)

### D.1 Receiving (scope item 1)
`RawMessage` carries customer, warehouse, receipt date, receiving user, notes, the system-generated
`MessageNumber`, and per line: item, `QuantityKg` and/or `QuantityMeter`, optional `PieceCount`
(bale/roll count), optional notes and now colour. The number is **always** produced by
`IDocumentNumberGenerator` (`CreateRawMessageCommand.cs:80`), implemented by
`SqlDocumentNumberGenerator` with a transaction-scoped `sp_getapplock` on
`(DocumentType, Year, WarehouseId)` (`:53-58`) — concurrency-safe across API instances.
**Inspection is not mandatory**: `InspectionStatus` defaults to `PendingInspection`, the receipt is
`Open` and allocatable immediately (`RawMessage.IsAvailableForAllocation`), and the domain comment
says so explicitly ("recorded information only, NEVER an approval step"). The handler acquires the
per-lot stock lock (`StockLockKey.RawLot`), reads the balance inside it and posts one `IN` row per
measured unit in the same `SaveChanges` — a ledger movement through the standard workflow, never a
balance edit.

### D.2 Formation Requests write no stock (scope item 2)
- Every listed field exists: `FormationRequest` (customer, item, unit, total, notes, raw message,
  status + submit/approve/reject/cancel stamps) and `FormationGroup` / `FormationBasin` (planned
  quantity, unit, tub count, **colour**, width/m-per-kg/gsm/tub format, four instruction blocks,
  specification snapshot) — and a basin additionally carries **`ActualQuantity`** with
  `EffectiveQuantity`/`RemainingQuantity`, so planned ≠ actual is modelled, not assumed.
- `grep -rn "InventoryTransaction|RawAllocations|NegativeStockOverride"` over
  `Application/FormationRequests/` and `FormationRequestsController.cs` → **NONE**.
- Statuses are `Draft → Submitted → Approved/Rejected/Cancelled`, editable only while draft/rejected
  (`UpdateFormationRequestCommand` refuses otherwise with `DocumentLockedException`), corrections via
  a new revision (`ReviseFormationRequestCommand`, `RevisionOfFormationRequestId`), and every change
  is captured by `AuditSaveChangesInterceptor`.
- Decoupling is data, not convention: `ProductionOrder.FormationRequestId` is nullable
  (`ProductionOrderConfiguration.cs:63`, asserted by `ModelLinkageAndPayrollConfigTests`), and the
  controller's own documentation states the request "never creates, links to, or converts into a Job
  Order". `CreateFormationRequestCommand` never names a Job Order.

### D.3 Issue creates the Job Order, atomically, with or without a request (scope item 3)
`IssueRawMaterialCommand` (one lock on `StockLockKey.RawLot`, one `SaveChangesAsync`) posts the OUT
row, creates the order with the **actual** issued quantities as its opening quantities, snapshots the
stage route, writes the matching `RawAllocation`, links the order to the issue row and to the
optional Formation Request/basin, and writes an audit entry. `ValidateFormationReferenceAsync`
refuses a basin without its request, a request of another customer and a basin that belongs to
another request; nothing anywhere requires a request. There is **no** endpoint that creates an
ordinary Job Order without an issue (`CreateProductionOrderCommand` is documented LEGACY/INTERNAL ONLY
and unreachable from a controller), so a PO can never be produced from a planned quantity alone.
Warehouse, customer, item, date, unit and responsible user all travel on the issue row/order, so the
"without a Formation Request" link set is complete.

### D.4 The 100% allocation gate (scope item 4, enforcement)
The rule itself is `ProductionOrder.IsRawAllocationComplete(kg, meter)` — per **unit**, never
converted between KG and Meter, with the same 0.001 tolerance the ready-goods cap uses, and an order
whose requested quantity was never recorded still needs a genuinely positive allocation.
`RawAllocationGuard.EnsureCompleteAsync` supplies the committed totals and is called from **8** sites:
`StageExecutionCommands` (start / complete / skip-closure, `:40, :98, :168`),
`TransferToNextStageCommand` (`:116, :266`), `CompleteProductionOrderCommand` (`:46`),
`CreateReadyGoodsTransferCommand` (`:73`) and `PauseResumeCommands` (`:305`, counting the resume
re-issue). Allocation rows are append-only, so the check is monotonic — a race can cause a false
refusal, never a bypass. Partial allocation is allowed and recorded; only production start and every
later production transition are refused.

### D.5 Traceability (scope item 5)
`InventoryTransaction` carries `RawMessageId`, `SourceDocumentType`/`SourceDocumentId`,
`ProductionOrderId`, customer, item, warehouse, both quantities and direction, so a movement is
traceable to its document and its lot; `ProductionOrder.SourceIssueTransactionId` links the order
back to the exact issue row that created it, and `FormationRequestId`/`FormationBasinId` link the
planning. `GET api/formation-requests/{id}/traceability` returns the customer → message → request
chain. The receipt PDF is the printable traceability document for the lot.

### D.6 Permissions enforced server-side (scope item 6)
`api/raw-messages` `POST` = `raw.receive`, `{id}/inspection` = `raw.inspect`, `{id}/cancel` =
`raw.receipt_cancel`; `api/production-orders/issue` and `{id}/raw-allocations` = `raw.consume`;
`api/stock-adjustments` `POST` = `inventory.adjust`. The negative-stock exception is enforced twice:
the action's permission, **and** in the handler both `inventory.allow_negative_stock` **and**
`inventory.approve_negative_stock`, **and** a stored, single-use, operation-bound approval raised by
a different user (`ApprovalRequestGuard.ConsumeAsync`: pending, right type, right document, matching
operation signature, requester ≠ approver taken from the authenticated account + rowversion). Every
ledger-affecting action is reachable only through one of these policies.

### D.7 Edit / cancel rules (scope item 7)
- **Raw receipt** — posted immediately, no draft state; correction is
  `POST api/raw-messages/{id}/cancel` with a mandatory reason, which appends an equal-and-opposite OUT
  row naming the movement it reverses and preserves the original document and its IN row. It is
  refused while material is allocated to a Job Order or has already left the lot (so a posted
  document is never edited, deleted or silently re-balanced).
- **Formation Request** — editable while `Draft`/`Rejected`; once submitted/approved it is corrected
  by a **revision** that leaves the original and its approval history intact.
- **Job Order** — descriptive/scheduling fields editable while open, identity and requested quantity
  not (they are referenced by allocations, goods, deliveries and invoices); a completed or cancelled
  order is locked; cancellation reverses the outstanding raw claim per lot as new compensating
  movements.

---

## E. Tests added (10 new, all passing)

`tests/DyeHouseERP.UnitTests/Phase3ReceivingAndAllocationTests.cs`

| Test | Required scenario it covers |
|---|---|
| `Receiving_With_No_Inspection_Data_At_All_Posts_The_Receipt_And_Its_In_Movement` | **Receiving without inspection fields succeeds** — plus the field set (customer, item, colour, quantities + units, warehouse, system number, bale count, notes) and "one ledger movement, through the standard workflow" |
| `A_Retried_Receiving_Confirmation_With_The_Same_Token_Posts_Exactly_One_Receipt_And_One_Movement` | **Idempotent receiving** (new behaviour); a different token is a different delivery |
| `A_Retried_Receipt_Reports_The_Live_Balance_Not_The_Originally_Received_Quantity` | The retry path answers from the ledger, not a stale copy |
| `Receiving_Stores_No_Colour_When_None_Was_Given` | The new field is genuinely optional |
| `Creating_And_Editing_A_Formation_Request_Moves_No_Stock_At_All` | **Formation Request create/edit produces zero stock movements** (handler level: no ledger row, allocation, adjustment or override; balance unchanged; no Job Order) |
| `The_Order_Reports_Required_Allocated_Remaining_And_Status_By_The_Rule_The_Server_Enforces` | **Display = enforcement**: partial → incomplete + `DomainException` from the same rule; 100% → complete + allowed |
| `An_Over_Allocated_Order_Reports_Zero_Remaining_And_Not_A_Negative_Figure` | Remaining is clamped |
| `The_Requester_Of_A_Negative_Stock_Exception_Cannot_Approve_Their_Own_Issue` | **Negative-stock exception: self-approval rejected** on the issue path (no order, no movement, approval stays pending) |
| `The_Stock_Lock_Service_Is_Constructed_With_Its_DbContext` | Regression guard for §C.1 (the constructor exists and the field is the context) |
| `Acquiring_No_Stock_Dimensions_Is_A_NoOp_And_Needs_No_Lock_At_All` | The no-op path still behaves |

Required scenarios already covered by existing suites (re-run in the 725, not duplicated):
issue with/without a Formation Request (`IssueRawMaterialWorkflowTests`:
`DirectIssue_CreatesTheLedgerMovement_AndTheOrderLinkedToIt_WithNoFormationLinks`,
`Issue_From_A_Formation_Request_Links_Request_Basin_And_Issue`), planned ≠ issued
(`The_Order_Opening_Quantity_Is_The_ACTUAL_Issued_Quantity_Not_The_Planned`), duplicate request → one
issue + one order (`A_Retried_Confirmation_With_The_Same_RequestId_Creates_No_Duplicate_Issue_Or_Order`),
concurrent issues cannot overspend (`Concurrent_Issues_Cannot_Overspend_The_Lot`), start blocked below
100% and allowed at 100% (`RawAllocationGateHandlerTests`, 26 cases), self-approval rejected on the
other override paths (`NegativeStockApprovalGatingTests`).

`ItemUnitConfigurationTests` and `WarehouseDeactivationTests` were updated for the receiving handler's
new ledger dependency (3 + 1 call sites); no assertion was weakened.

---

## F. Not run / limitations / open questions

1. **Integration tests — Not run.** No SQL Server is available (no `sqlservr`, no Docker; Phase 1
   B.3). Consequently the *runtime* behaviour of the two things this phase depends on at the database
   level is **not** verified here: `sp_getapplock` actually serialising two concurrent postings
   (§C.1), and the unique filtered index rejecting a genuinely racing duplicate receipt token. Both
   are verified only by construction, code reading and the migration SQL. Deliberately recorded as
   **Not run**, never as a pass. The existing SQL-gated integration suites
   (`RawAllocationAndNegativeStockTests`, `ConcurrencyHardeningTests`,
   `ChildInsertAndWarehouseGuardSqlTests`, `FullHappyPathTests`, …) should be executed against a real
   database before this phase is signed off.
2. **Frontend lint — Not run** (pre-existing: no `eslint` install and no config). The frontend build
   (`tsc -b`) is the type gate that did run, and it passes.
3. **Receiving has no draft/"unposted" state.** Scope item 7's "unposted → editable, posted →
   controlled correction" is realised as: the receipt posts on confirm and is corrected by the
   cancel-with-reversal workflow. If a genuine draft stage is wanted for receiving, that is a
   workflow change, not a fix — flagged, not invented.
4. **Interpretation recorded for review:** the phase's receiving field "colour (where applicable)"
   was implemented as an optional per-line colour (§C.3), because the field was absent everywhere on
   the receipt. If the intended reading was "the article carries the colour in master data", the
   change is additive (nullable column, optional input) and can be reverted without touching any
   other behaviour.
5. Still open from earlier phases and untouched here: a dedicated `production.scrap` permission
   (§J.3 of the Phase 1 report), PDF Arabic-font embedding (Phase 8), the duplicated XML doc-comment
   above `ProductionOrdersController.Export` (Phase-1 F.12), and the suppliers' activate/deactivate
   shape.

---

## G. Status

**`Verified with one real defect fixed`** — scope-locked to Phase 3. Backend build clean (0 errors,
5 warnings, two **fewer** than the baseline because the compiler was already reporting F.7), **725/725**
unit tests pass, frontend build passes, i18n parity holds (896/896), permission parity holds
(116/116), the model matches the snapshot, and one migration was created (**not applied**) covering
the two new nullable columns. The integration suite and frontend lint are recorded **Not run** with
their reasons, never as passes.
