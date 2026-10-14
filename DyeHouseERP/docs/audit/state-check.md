# State check — Phases 4, 5 and 6 (inventory only, no new features)

> **Mode:** read-only inventory + the one compile fix needed to make the tree build. **No commit, no push,
> no migration applied.** Written before starting any further phase work, as instructed.
> Branch: `freebuff/8c663d5dafcf97d023c3991e` · Baseline HEAD: `abb4426` · Date: 2026-10-14.

**Scope sources used.** `00-MAIN-PROMPT.md` is **not present in this workspace** (this brief references it, but
only the per-phase briefs were supplied), and no Phase 4 brief text exists on disk either. The item lists below
are therefore reconstructed from the artefacts that *do* exist — `docs/audit/phase1-report.md` §E/§F (the
Keep/Modify/Rebuild classification and the "suggested scope per phase"), `docs/audit/progress.md`, and
`docs/audit/phase5-report.md` §F — plus what the working tree actually contains. Anything I could not evidence
from a file or a test is marked as such instead of guessed.

---

## A. Working-tree inventory

`git status`: **111 tracked files modified/deleted** (previously committed code), **31 untracked** files
(≈3 434 insertions / 726 deletions overall). `progress.md` documents Phases 1–3 as Verified and Phase 5 as
Verified; **Phase 4 has no report at all** and **Phase 6 was still in flight** when the previous session ended.

Untracked artefacts (all uncommitted):

- **Phase 2/3/5/6 migrations** (created, **not applied**):
  `20261014015833_AddPhase2ItemConversionsAndSubCustomer`, `20261014031119_Phase3ReceivingIdempotencyAndColor`,
  `20261014042210_Phase5ReadyGoodsDeliveryReturns`, `20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement`.
- **Phase 6 production code:** `Application/Checks/Commands/EndorsedCheckSettlementCommand.cs`,
  `Application/Common/Services/InvoiceBillingGuard.cs`, `Common/Interfaces/ICompanyIdentityProvider.cs`,
  `Persistence/Services/CompanyIdentityProvider.cs`.
- **Phase 2/5 code:** `ItemUnitConversion*` (entity/config/commands/query/DTO), `ReadyGoodsRawReturn*`,
  `ReturnReadyGoodsToRawStockCommand`, `GetDeliveryStatementQuery`, `DeliveryStatementPrintPage.tsx`,
  `SetCustomerParentCommand`, `CreateProductionOrderFromReturnCommand`.
- **Test files:** `Phase2MasterDataTests.cs`, `Phase3ReceivingAndAllocationTests.cs`,
  `Phase5ReadyGoodsDeliveryReturnsTests.cs`, `Phase6FinanceTests.cs`.
- The whole `DyeHouseERP/docs/` tree is itself untracked (the audit reports have never been committed).

---

## B. Build and tests (commands actually run)

| Check | Command | Result |
|---|---|---|
| Backend build (Release) | `dotnet build DyeHouseERP.sln -c Release` | **PASS** — 0 errors, 5 warnings (3 pre-existing nullability warnings in the unit-test project + 2 `xUnit1031` in the integration project when it is rebuilt). |
| Unit tests (Release) | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **PASS — 784 / 784**, 0 failed, 0 skipped (21 s). |
| Integration tests | `dotnet test tests/DyeHouseERP.IntegrationTests -c Release --no-build` | **BLOCKED — 0 passed / 34 failed.** The API host refuses to start: `System.InvalidOperationException: The integration tests need a real SQL Server test database. Set 'DYEHOUSE_TEST_CONNECTION' …` (`Program.cs:233`). No SQL Server exists in this environment — this is the Phase 1 §B.3 limitation, **not** a regression. |
| Permission parity | `node tools/permissions-parity-check.cjs` | **PASS — 117 / 117, 0 failures.** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **PASS — ar 896 / en 896, 0 failures.** |
| Duplicate / DbSet sweep | `node tools/duplicate-sweep.js` | **PASS** — 88 DbSets on the context = 88 on the interface; no duplicates. |
| Frontend build | `npx tsc -b && npx vite build` | see §B.1 below. |

**One compile error existed and was fixed (not a feature):** `Phase6FinanceTests.cs:762` used
`a.Roles is not null` inside a FluentAssertions lambda (an expression tree), which C# rejects with
`CS8122: An expression tree may not contain an 'is' pattern-matching operator`. Changed to `a.Roles != null`.
This was the only compile error in the tree; the interrupted Phase 6 test file had never been compiled before.

### B.1 Frontend build

`npx tsc -b` → **PASS (exit 0)**; `npx vite build` → **PASS (exit 0)** (chunk-size warning only). Recorded here
because the state check itself does not change front-end code; the same two commands were re-run after the
Phase 4 work (§H).

---

## C. Phase 4 — Production, separates & reprocessing

Phase 4 has **no report and no test file of its own**; its brief text is not on disk. Item list reconstructed
from `phase1-report.md` §F ("decide on `production.scrap`; confirm ready-goods diversion and drawing-number
enforcement in DB-backed tests") widened by the Phase 4-flavoured model work already sitting in the tree
(`progress.md` §Phase 4).

| # | Scope item | Status | Evidence |
|---|---|---|---|
| 1 | Stage transfer is the single "end stage" action: closes the stage, records output, derives loss % from the immutable baseline, locks it, activates only the chosen next stage | **تم** | `TransferToNextStageCommand.cs` (`stage.CloseWithOutput(...)`, `order.ActivateNextStage(...)`); `ProductionOrderStageExecution.cs`; tests `ProductionStageAdministrationTests.cs`, `ProductionOrderTests.cs` |
| 2 | Ready-goods diversion: the stage flagged `IsReadyGoodsStage` posts the ready document + IN movement and completes the order | **تم** (unit) / **محجوب** (DB) | `TransferToNextStageCommand.cs` ready-goods branch; covered by unit tests. The DB-backed confirmation Phase 1 §F asked for **cannot run** (no SQL Server, §B). |
| 3 | Drawing-number rule enforced server-side, plus additional print runs of the same stage | **تم** | `stage.EnsureDrawingNumber()`, `isAdditionalPrintRun` guard in `TransferToNextStageCommand.cs`; `MultiplePrintingExecutionsTests.cs` |
| 4 | Actual input recorded as entered (never inferred from output) | **جزئي** | Server stores `InputKg/InputMeter` (`CloseWithOutput`); **no unit-test source asserts it**, and the UI never sends it (item 8) |
| 5 | Second-grade material recorded as a quantity of its own (not output, not waste) | **جزئي** | Entity/command/DTO exist (`ProductionOrderStageExecution.SecondGradeKg/Meter`, `TransferToNextStageCommand`, `client.ts:1170`). **Zero test sources mention second grade**, and the UI never sends it. |
| 6 | Separates: own lifecycle entity + register + reprocess into a NEW order (genealogy preserved) + scrap with a reason | **جزئي** | `Separate.cs`, `ReprocessSeparateCommand.cs`, `ScrapSeparateCommand.cs`, `SeparatesController.cs`, `SeparatesPage.tsx` (reprocess + scrap buttons). **No dedicated handler test** (`ScrapSeparateCommand`/`ReprocessSeparateCommand` appear in no test); only `MultiplePrintingExecutionsTests` mentions `SeparatesKg`. |
| 7 | Machine (suitability + availability) and execution shift recorded on the closing stage | **تم** | `EnsureMachineAndShiftAreUsableAsync` in `TransferToNextStageCommand.cs`; `ProductionStageAdministrationTests.cs` |
| 8 | **Production-floor UI capture surface** for second grade / separates / actual input (the explicit pending dependency in `phase5-report.md` §F) | **لم يبدأ** | `ProductionOrderDetailPage.tsx:516` transfer form sends only `outputKg/outputMeter/drawingNumber/notes`; `client.ts:773` `transferStage` body type has `separatesKg/Meter` but **not** `secondGradeKg/Meter` nor `inputKg`. `ProductionFloorPage.tsx` only *displays* input/output (no transfer action). |
| 9 | Pause / resume with an explicit, lot-bound raw release | **تم** | `PauseResumeCommands.cs`; `PauseResumeLedgerTests.cs` |
| 10 | `production.scrap` decision (Phase 1 §G.1, open business question) | **لم يبدأ (قرار مطلوب)** | `Permissions.cs` has no `ProductionScrap`; `SeparatesController.Scrap` is gated by `production.edit`. Left as an explicit scope question, not silently changed. |
| 11 | DB-backed tests for ready-goods diversion + drawing number (Phase 1 §F) | **لم يبدأ / محجوب** | Integration suite blocked by the missing SQL Server (§B). |

**Phase 4 verdict: `جزئي (Incomplete)`.** The server-side production engine is implemented and unit-tested;
the missing pieces are (a) the floor capture of second grade / separates / actual input, (b) test coverage for
second-grade recording and for scrap/reprocess handlers, and (c) the `production.scrap` decision.

---

## D. Phase 5 — Ready goods, delivery & returns

`phase5-report.md` reports **`Verified`**; the state check confirms the artefacts are all present and the
suite still passes (22 tests inside `Phase5ReadyGoodsDeliveryReturnsTests.cs`).

| # | Scope item | Status | Evidence |
|---|---|---|---|
| 1 | Ready-goods warehouse as its own stock authority (+ ready-goods→raw return document) | **تم** | `ReadyGoodsTransfer`, `ReadyGoodsRawReturn`, `ReturnReadyGoodsToRawStockCommand`, `/api/ready-goods/raw-returns`, `ReadyGoodsPage` tab; 6 tests |
| 2 | Partial delivery settles only the Job Order's **fixed** original raw quantity | **تم** | `DeliveryLine.OriginalRawQuantity*`, `ProductionOrder.EnsureOriginalRawWithinLimit`; 2 tests |
| 3 | Delivery statement (بيان تسليم) per line, **per unit, never mixed** | **تم** | `GetDeliveryStatementQuery`, `/statement[/pdf]`, `DeliveryStatementPrintPage`; 7 tests (incl. per-unit waste 600−520−20−10 KG vs 100−85−5−2 M) |
| 4 | Sub-customer permit referencing a real primary permit, posting no stock | **تم** | `Delivery.PrimaryDeliveryId/PrimaryCustomerId`, `ResolvePrimaryPermitAsync`, `SetCustomerParentCommand`; tests |
| 5 | Customer returns (raw destination only, ownership + reference document, audited cancel, optional new Job Order stating the cost bearer) | **تم** | `CreateCustomerReturnCommand`, `CancelCustomerReturnCommand`, `CreateProductionOrderFromReturnCommand`; 9 tests |
| — | Delivery idempotency (`DeliverRequestId` + unique filtered index) | **تم (منشأ)** | Entity + migration; runtime uniqueness **محجوب** (no SQL Server) |
| — | Migration created, not applied | **تم** | `20261014042210_Phase5ReadyGoodsDeliveryReturns` |

**Phase 5 verdict: `تم (Verified)`** within the environment's limits; its only carried-over item is the
Phase 4 floor-entry dependency, which is recorded above as Phase 4 item 8 — not a Phase 5 defect.

---

## E. Phase 6 — Finance, invoicing, treasury & cheques

The backend was completed in the previous session (one compile error, fixed above); the **web layer is
unfinished**. Tests today: `Phase6FinanceTests.cs` compiles and its ~30 tests pass.

| # | Scope item | Status | Evidence |
|---|---|---|---|
| 1 | **Billing unit follows the service pricing** (billed by meter while production was by kg), using the **approved** quantity in that unit — never a manufactured conversion | **تم (backend) / جزئي (UI)** | `InvoiceBillingGuard.cs`, `InvoiceLine.Unit` (+ migration), `CreateInvoiceCommand`; tests `Billing_ByMeter_UsesTheApprovedMeterQuantity_AndNeverConvertsKgIntoMeter`, `..._IsRefused_WhenNoMeterQuantityWasEverApproved`, `..._InAUnitTheOrderHasNoServicePriceFor`. **UI gap:** no unit selector in `InvoicesPage.tsx`, no unit column in `InvoicePrintPage.tsx`. |
| 2 | Approved quantity cannot be billed twice; a typo can still be corrected | **تم** | `TheApprovedQuantity_CannotBeBilledTwice_AndATypoCanStillBeCorrected` |
| 3 | Distinguish execution / billable services / pricing / delivery quantities / chargeable amounts / company-borne costs | **تم (backend)** | `InvoiceBillingGuard`, `InvoiceLine.Unit`, `CostEntry.IsChargeableToCustomer` |
| 4 | Reprocessing & separate costs are **not** auto-added; included only when explicitly flagged chargeable; never counted twice | **جزئي** | `CostEntry.IsChargeableToCustomer` + unique filtered index `IX_InvoiceLines_CostEntryId`; tests `ACompanyBorneCost_IsNeverAutoAdded_AndCannotBeBilled`, `AChargeableCost_IsBilled_AndNeverCountedTwice`, `AReprocessingOrSeparateCost_StaysOutOfInvoicesEntirely_UnlessFlagged`. **UI gap:** no client/page field exposes the flag (no `chargeable` reference in `Web/src`). |
| 5 | Return registration never auto-modifies invoices or creates credit notes | **تم** | `RegisteringAReturn_LeavesTheInvoiceAndTheCustomerStatementUntouched` |
| 6 | Invoice PDF uses factory settings for name + logo; missing logo still generates; matches persisted data | **تم** | `ICompanyIdentityProvider`/`CompanyIdentityProvider`, `GetInvoicePdf`; `TheInvoicePdf_UsesTheConfiguredFactoryName_AndStillGeneratesWithoutALogo`, `..._StillGenerates_WithALogo` |
| 7 | Customer statements from authoritative financial records, units kept separate | **تم** | `GetCustomerStatementQuery.cs` (live running balance from `CustomerLedgerEntries`); isolation test above. *(Note: the statement is a money ledger — amounts, not KG/Meter — so "units kept separate" is satisfied by the invoice/statement never mixing a KG-priced line with a Meter-priced one; the per-line `Unit` is what carries that.)* |
| 8 | Treasury: receipts, payments, transfers, customer/supplier payments, reconciliation/reporting, document references, audit, permissions | **تم** | Existing treasury stack (Phase 1 §D.8 Keep) + Phase 6 test `APaymentMadeWithOurOwnCheque_PostsNoCashUntilThatChequeClears`; endpoints in `ReceiptsController`/`PaymentsController`/`TreasuryTransfersController` |
| 9 | Cheques: management + controlled status transitions + document references | **تم** | `ChecksController` (register/confirm/endorse/deposit/clear/bounce/return/cancel), `ChecksPage.tsx`, `Check.cs` guarded transitions |
| 10 | **Endorsed customer cheque settling a supplier payable** — no fictitious cash in/out, full document linkage, no duplicate settlement, correct balances for both parties | **تم (backend) / لم يبدأ (UI)** | `EndorsedCheckSettlementCommand.cs`, `POST /api/checks/{id}/settle-supplier`, `SupplierPayment.TreasuryAccountId` nullable + unique filtered index on `CheckId`; 6 tests (no cash rows; both balances move; second attempt rejected; clearing posts no cash; cancellation restores; only an incoming customer cheque; a plain supplier payment using a customer cheque is refused). **Gap:** neither `client.ts` nor `ChecksPage.tsx` calls `settle-supplier` (only `endorse`). |
| 11 | Period closing: authorized close, recorded status, validation, closed periods blocked from ordinary changes, controlled correction path, concurrent-close safe, authorized reopen | **تم** | `PeriodClosingCommands.cs`, `PeriodCloseService`, `/period-closing` route + `PeriodClosingPage.tsx`; tests `ClosingTheSameRangeTwice_LeavesExactlyOneClosedPeriod`, `ClosingAnOverlappingRange_IsRefused_...`, `ClosingTakesTheExclusiveGlobalPeriodLock_AndStaysPossibleAfterAReopen` |
| 12 | Financial consistency: transactions wrapped consistently; **no stock/finance movement created by exports or prints** | **تم** | `PrintingWorkflowTests.cs` + Phase 5 statement test (`printing twice moves no stock`); Phase 6 endorsement tests assert zero treasury rows |
| 13 | Permission checks on treasury / cheque / invoice endpoints | **تم** | `Phase6PermissionTests.EveryFinanceEndpoint_CarriesItsGranularPermissionPolicy` — a theory over 23 endpoint/action pairs (invoices, statement, receipts, payments, treasury accounts/transfers, checks, period closing) + the "no role gate" assertion; `Permissions.All` completeness test |
| — | Migration created, not applied | **تم** | `20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement` (5 schema changes, each explained in the file) |

**Phase 6 verdict: `جزئي (Incomplete)`.** All seven scope areas are implemented and tested server-side; what is
missing is the **web surface** (invoice billing-unit + chargeable flag + `settle-supplier` action + their i18n
keys) and the two DB-backed checks the environment cannot run.

---

## F. Cross-phase open items (unchanged, carried)

- **No SQL Server** → the integration suite (34 tests) cannot run; every unique filtered index and
  `sp_getapplock` path is verified **by construction only**.
- **Frontend lint** (`npm run lint`) → `eslint: not found` (Phase 1 F.B.4).
- **PDF Arabic font/RTL** → Phase 8 (`ReportExportService`; the Phase 5 statement PDF and the invoice PDF go
  through the same unbranded path).
- **Attachment content-type allowlist** (F.8) → Phase 8.
- **`production.scrap`** decision → Phase 4 (items 10 above).

---

## G. Next step (per the instruction: oldest incomplete phase first)

**Phase 4** is the oldest incomplete phase → work it first, then Phase 5's residual dependency is already
inside it, then close Phase 6's web surface. Phase 7 will not be started before Phases 4, 5 and 6 are closed.

Phase 4 completion plan (one phase at a time):

1. Wire the production-floor capture surface: send `separatesKg/Meter`, `secondGradeKg/Meter` and
   `inputKg/Meter` from the stage-transfer form, and add the corresponding fields to
   `client.ts`'s `transferStage` body type.
2. Add the missing handler tests: second-grade recording (stored, unit-validated, excluded from output/waste),
   separates recorded on transfer, `ScrapSeparateCommand` (reason mandatory, no double-scrap) and
   `ReprocessSeparateCommand` (genealogy, unit guard).
3. Record the `production.scrap` decision explicitly (introduce the permission or state why `production.edit`
   is the intended gate) rather than leaving it implicit.
4. Re-run build + the full unit suite + parity sweeps, then write `docs/audit/phase4-report.md`.

No commit, no push, and no migration has been applied.

---

## H. Update — after the Phase 4 work this state check ordered

The tables above are the **snapshot taken before any Phase 4 work** and are deliberately not rewritten; this
section records only what moved.

| Item (from §C/§E) | Was | Now |
|---|---|---|
| Phase 4 item 2 (register entry for recorded separates) | `جزئي` — *and actually unreachable*: no path created a `Separate` | **تم** — `TransferToNextStageCommand` adds the register row when a quantity is recorded; 10 new tests |
| Phase 4 items 4/5 (actual input, second grade) | `جزئي` (never sent by the UI, untested) | **تم** (floor capture wired + tested) |
| Phase 4 item 6 (separates register / reprocess / scrap) | `جزئي` (no handler test, unreachable register) | **تم** (register reached; scrap/reprocess tested) |
| Phase 4 item 8 (production-floor capture surface) | `لم يبدأ` | **تم** |
| Phase 4 item 10 (`production.scrap`) | `لم يبدأ (قرار مطلوب)` | **تم** — decided and implemented as its own right (118 constants) |
| Phase 4 item 11 (DB-backed tests) | `لم يبدأ / محجوب` | **still blocked** — no SQL Server; recorded as open, not closed |
| Phase 6 web surface (billing unit, chargeable flag, `settle-supplier`, i18n) | `جزئي` | **unchanged — next** |

Re-verified after the change: Release build **0 errors / 5 warnings**, unit tests **794 / 794** (+10),
permission parity **118/118**, i18n parity **903/903**, duplicate + wiring sweeps clean, frontend typecheck and
build **PASS**, `has-pending-model-changes` **clean** (Phase 4 needed no migration). Full detail:
`docs/audit/phase4-report.md`.

No commit, no push, and no migration has been applied.
