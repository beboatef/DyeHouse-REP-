# Phase 4 — Production floor, separates & reprocessing

> **Mode:** implementation completed in the working tree. **No commit, no push, no migration was applied**
> (and none was needed — see §E). **Phase status: `Verified` for the four items this phase had open; two
> checks remain `Not run` because the environment cannot run them (no SQL Server, no frontend lint).**
> Scope: the production floor — spec sections 13–23 (dynamic stages, the transfer that IS the end of an
> operation, actual input, separates, second grade, machines/shifts, reprocessing, scrap).

Branch: `freebuff/8c663d5dafcf97d023c3991e` · Baseline HEAD: `abb4426` · Date: 2026-10-14
Predecessors: `docs/audit/phase1-report.md` … `phase5-report.md`, `docs/audit/state-check.md`.

---

## A. What this phase had to deliver

Phase 4 has no brief stored on disk (`00-MAIN-PROMPT.md` is not in the workspace; see `state-check.md`
scope sources). Its item list was reconstructed there from `phase1-report.md` §E/§F, `progress.md` and the
tree, and every item was given a status before any work started. On entry the phase stood at **`جزئي`** with
four things open:

1. **The production-floor capture surface** — `phase5-report.md` §F recorded it explicitly as a pending
   Phase 4 dependency: the transfer form sent only `outputKg/outputMeter/drawingNumber/notes`, so second
   grade, separates and the *actual input* were accepted by the server but never entered from the floor.
2. **Test coverage** for second-grade recording and for the scrap/reprocess handlers — none existed (no test
   source in the repository mentioned second grade at all).
3. **`production.scrap`** — the decision Phase 1 §G.1 left open: the spec's checklist names a scrap right,
   while the code gated scrap with `production.edit`.
4. **A defect found while inventorying item 2** (see §C.1): the separates chain was **unreachable** — no code
   anywhere created a `Separate`, so the register, the reprocessing decision and the scrap decision had no
   data to act on, even though the transfer recorded a separates figure on the operation.

---

## B. State on entry

Recorded in `docs/audit/state-check.md` (§C, Phase 4 table): 11 items, of which 5 were `تم`, 3 `جزئي`, 3 `لم
يبدأ`. Build/tests at that moment: Release build **0 errors / 5 warnings**, unit tests **784 / 784**, the
integration suite blocked by the missing SQL Server. The one compile error in the tree (an expression-tree
`is not null` in the previous session's `Phase6FinanceTests.cs`) was fixed during the state check, so the
baseline for this phase is a **building, passing** tree.

---

## C. What this session produced

### C.1 The separates register is now actually reached (the real defect)

`Separate` is a first-class entity with its own lifecycle (`PendingReprocessing → Reprocessing →
Reprocessed | Scrapped`), RowVersion token, register endpoint, DTO and UI page. The transfer recorded
`separatesKg/Meter` **on the stage execution**, but `grep -rn "new Separate("` over the whole solution
returned **nothing**: no path ever created a register row, so the register was permanently empty and
"reprocess"/"scrap" had nothing to act on. Material the floor recorded was therefore recorded but untracked.

**Fix (not a new feature — it closes the existing lifecycle):** `TransferToNextStageCommand` now adds the
`Separate` row in the same transaction that closes the operation, and only when a quantity was actually
recorded (> 0):

```csharp
if (request.SeparatesKg is > 0 || request.SeparatesMeter is > 0)
    _db.Separates.Add(new Separate(
        order.Id, stage.Id, order.CustomerId, order.ItemId,
        request.SeparatesKg, request.SeparatesMeter, _currentUser.UserName,
        notes: request.Notes));
```

The row carries the original order, the operation it came off, the customer and the item — which is exactly
what the register, the traceability join and the reprocessing order all read. It is `Add`ed explicitly for
the same reason a new stage execution is: EF change detection would otherwise treat the client-generated key
as an existing row and try to UPDATE a row that was never inserted.

### C.2 The production-floor capture surface is wired

`ProductionOrderDetailPage`'s transfer form (the single "end of an operation" action) now captures the whole
record of the operation being closed, and `client.ts`'s `transferStage` body type declares the same fields:

| Field | KG | Meter |
|---|---|---|
| Actual input (المستهلك الفعلي) | ✓ | ✓ |
| Output (الناتج) | ✓ (existing) | ✓ (existing) |
| Separates (المنفصلات) | ✓ | ✓ |
| Second grade (الدرجة الثانية) | ✓ | ✓ |

Every field is optional — an operation may legitimately record none of them — and each is validated
server-side against the **item's own** unit configuration (`ItemUnitGuard` already ran on all four pairs), so
the front end is never the authority and a unit the item does not use is refused rather than converted. New
bilingual keys (`stage.inputKg`, `stage.inputMeter`, `stage.separatesKg`, `stage.separatesMeter`,
`stage.secondGradeKg`, `stage.secondGradeMeter`, `stage.floorCaptureHint`) were added to both dictionaries.

### C.3 `production.scrap` — the Phase 1 §G.1 decision, made and implemented

The spec's checklist names a scrap right; the code used `production.edit`. The decision taken:
**introduce the dedicated permission**, because writing material off as waste is a different decision from
editing a Job Order and must never be implied by the ability to edit one.

- `Permissions.ProductionScrap = "production.scrap"` added to `Permissions.All` (118 constants).
- `SeparatesController.Scrap` is now gated by it (`production.edit` no longer implies it).
- Mirrored in `Web/src/permissions.ts` so the Users screen can grant it and parity holds.

Nothing else about the endpoint changed: it still requires a reason, still refuses a second scrap, and still
writes no stock.

### C.4 Tests — `tests/DyeHouseERP.UnitTests/Phase4ProductionFloorTests.cs` (10 tests)

| # | Test | Pins |
|---|---|---|
| 1 | `AClosingOperation_RecordsItsActualInputSeparatesAndSecondGrade_AsEntered` | the actual input is stored as entered; separates and second grade are stored beside output; `LossKg = 1000 − 950 = 50` and `LossMeter = 1000 − 940 = 60` — **neither separates nor second grade is subtracted from the waste**, and KG/Meter are computed independently |
| 2 | `SecondGradeOrSeparates_InAUnitTheItemDoesNotUse_IsRefused_WithNothingWritten` | a Meter second-grade/separates figure on a KG-only item is refused with "*not configured to use Meter*", the operation stays open, nothing is written |
| 3 | `AnItemThatRequiresBothUnits_NeedsTheActualInputInBoth` | an item that requires both measurements does not accept a half-recorded operation |
| 4 | `ARecordedSeparatesQuantity_EntersTheSeparatesRegister_LinkedToItsOrderAndOperation` | the register row exists, links order/operation/customer/item, quantity 15 KG, status `PendingReprocessing` |
| 5 | `AnOperationThatRecordedNoSeparates_AddsNothingToTheRegister` | no separates recorded → no register row (second grade alone creates none) |
| 6 | `ScrappingASeparate_RequiresAReason_AndCanHappenOnlyOnce` | validator rejects a blank reason; the reason is kept; a second scrap throws |
| 7 | `ScrappingASeparate_IsItsOwnConfigurableRight_NotAnImpliedEditRight` | `production.scrap` exists in `Permissions.All`, differs from `production.edit`, the controller action carries exactly that policy and no role gate |
| 8 | `ReprocessingASeparate_CreatesANewLinkedOrder_AndLeavesTheOriginalUntouched` | the new order is raised on the separate's own 15 KG, points back via `ReprocessingOfProductionOrderId`, the separate becomes `Reprocessing` with the new order id, and the original order's number, requested quantity and operations are unchanged |
| 9 | `ReprocessingASeparate_WhoseUnitTheItemDoesNotUse_IsRefused` | a Meter separate on a KG-only item is refused (never converted); nothing is written, no order is created |
| 10 | `ScrappingOrReprocessingAnUnknownSeparate_IsRefused` | both handlers surface `NotFoundException` for an unknown id |

No production code outside C.1–C.3 needed a change: the transfer/completion transactions, the raw-allocation
gate, the drawing-number rule, the machine/shift suitability checks and pause/resume were already correct and
already covered (`ProductionStageAdministrationTests`, `MultiplePrintingExecutionsTests`, `PauseResumeLedgerTests`).

---

## D. Verification (commands actually run, with results)

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build DyeHouseERP.sln -c Release` | **PASS — 0 errors, 5 warnings** (the pre-existing `ExceptionHandlingMiddlewareTests`/`EditPermissionSweepTests` nullability set + the pre-existing `xUnit1031` pair). No new warning: an `async`-without-`await` test was corrected to a plain `void` test. |
| Unit tests | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **PASS — 794 / 794** (+10; was 784) |
| New suite only | `dotnet test ... --filter FullyQualifiedName~Phase4` | **PASS — 10 / 10** |
| Model vs snapshot | `dotnet ef migrations has-pending-model-changes` | **`No changes have been made to the model since the last migration.`** |
| Permission parity | `node tools/permissions-parity-check.cjs` | **PASS — 118 / 118, 0 failures** (was 117/117) |
| i18n parity | `node tools/i18n-parity-check.cjs` | **PASS — ar 903 / en 903, 0 failures** (was 896/896) |
| Duplicate/DbSet sweep | `node tools/duplicate-sweep.js` | **PASS** — 88 DbSets on the context = 88 on the interface, no duplicates |
| Wiring sweep | `node tools/wiring-sweep.js` | **PASS** — 76 UI calls checked, 0 unresolved; only login + public settings are policy-free (plus the documented approvals submit) |
| Frontend typecheck | `npx tsc -b` | **PASS — exit 0** |
| Frontend build | `npx vite build` | **PASS — exit 0**, chunk-size warning only |

**Not run (with reasons, never claimed as passes):**

- **Integration tests** (`tests/DyeHouseERP.IntegrationTests`): they need a real SQL Server via
  `DYEHOUSE_TEST_CONNECTION`; none is available (Phase 1 §B.3). So the SQL-level behaviour of the new unique
  filtered indexes and `sp_getapplock` paths remains verified **by construction**, and the Phase 1 §F request
  to "confirm ready-goods diversion and drawing-number enforcement in DB-backed tests" is still **open** —
  recorded as such in `state-check.md` (Phase 4 item 11), not silently closed.
- **Frontend lint** — `npm run lint` → `eslint: not found` (Phase 1 finding F.B.4).
- **Browser/floor entry** — the new form fields were not opened in a browser here (no browser tooling); the
  TypeScript build and the handler tests are the evidence.

---

## E. Migration

**None.** Phase 4 changed no entity, no relationship and no column: it wired an existing lifecycle in
application code, added UI fields and introduced a permission constant (permissions are JWT claims, not a
table). `has-pending-model-changes` is clean, which is the check that keeps that claim honest.

---

## F. Files touched by this session

**New:** `tests/DyeHouseERP.UnitTests/Phase4ProductionFloorTests.cs`, `docs/audit/state-check.md`,
`docs/audit/phase4-report.md`.

**Modified:** `src/DyeHouseERP.Application/ProductionOrders/Commands/TransferToNextStageCommand.cs` (register
entry point), `src/DyeHouseERP.Domain/Common/Permissions.cs` (`production.scrap`),
`src/DyeHouseERP.API/Controllers/SeparatesController.cs` (scrap re-gated),
`src/DyeHouseERP.Web/src/api/client.ts` (`transferStage` body), 
`src/DyeHouseERP.Web/src/pages/ProductionOrderDetailPage.tsx` (floor capture fields),
`src/DyeHouseERP.Web/src/permissions.ts`, `src/DyeHouseERP.Web/src/i18n/index.tsx`,
`tests/DyeHouseERP.UnitTests/Phase6FinanceTests.cs` (the one compile fix made during the state check).

---

## G. Phase 4 status

**`Verified`** for the four items that were open: the floor capture surface is wired end-to-end (server
contract + client type + form + i18n), the separates register is reachable and its scrap/reprocess lifecycle
is exercised by tests, and the `production.scrap` question from Phase 1 §G.1 is **decided and implemented**
rather than left implicit. Release build clean, **794/794** unit tests pass, permission parity 118/118, i18n
parity 903/903, duplicate and wiring sweeps clean, front end typechecks and builds, and the model is in sync
with the snapshot (no migration needed).

**Not run** (environment, not code): the SQL-Server integration suite — which is also what keeps the
"DB-backed confirmation of ready-goods diversion and drawing-number enforcement" request open — and frontend
lint.

**Next, in order:** Phase 5 has no open work of its own, so Phase 6's residual **web surface** is next
(invoice billing unit + chargeable-cost flag in the UI, the `settle-supplier` action, and their i18n keys),
after which Phase 7 may start. Phase 7 will not begin before Phases 4, 5 and 6 are closed.

No commit, no push, and no migration was applied.

---

## H. Continuation pass (2026-10-14, after the workspace was restored)

The phase was re-opened to finish it and to check what Phase 5 assumed from it. Everything in §A–§G above was
re-measured, not trusted; the four items §G calls closed stayed closed. One thing had been left half-done and is
now closed: **the front end still advertised the deleted `AllowSkip` capability.**

- `ProductionStagesPage.tsx` no longer edits/sends `allowSkip` and no longer renders the dead "Skippable"
  badge; its Properties column now shows the two real switches (`requiresInputQuantity` /
  `requiresOutputQuantity`).
- `client.ts`: `allowSkip` removed from `ProductionStageDefinition`, the `create` body and `StageExecution`;
  `secondGradeKg`/`secondGradeMeter` added to `StageExecution` (the DTO already exposed them).
- `StageRow.tsx` now shows the recorded **actual input, separates and second grade** — previously the floor
  recorded them but the operation history showed only baseline/output/loss.
- `SharedEnums.cs`: the misleading `Skipped = 4` comment corrected (value retained, unreachable by design).
- i18n: `stg.flag.allowSkip` and `stg.badgeSkip` removed from both dictionaries (**903 → 901**).

**Phase 5↔Phase 4 contract re-checked and confirmed** (drawing number in the delivery statement, the mandatory
return `CostBearer` enforced at the issue validator, and the five separate statuses) — no conflict found; one
statement-level test added (`TheStatement_CarriesEveryPrintRunsDrawingNumber_WithTheOperationsRecordedSeparatesAndSecondGrade`).

Re-verified after the change: Release build **0 errors / 5 warnings**, unit tests **795/795** (+1), Phase 4
**10/10**, Phase 5 **23/23**, permission parity **118/118**, i18n parity **901/901**, duplicate + wiring sweeps
clean, front end `tsc -b` **exit 0** and `vite build` **exit 0**, `has-pending-model-changes` clean (still no
migration). Integration tests (no SQL Server) and frontend lint remain **Not run**. No commit, no push, no
migration.
