# Phase 6 — Finance, invoicing, treasury, cheques & period closing

> **Provenance.** This report was written in the audit pass that ran on branch `fix/audit-findings`
> (2026-10-14) from **the code, the test suite in the tree, and the item list recorded in
> `docs/audit/state-check.md §E`**. Phase 6's own session produced no report, so nothing here is copied
> from a claim that cannot be pointed at a file. The checks listed in §D were **actually executed in this
> pass**; everything else is a description of the implementation with a file/test pointer, not a re-run.
>
> **Base commit for this audit: `7c68042`** (`main`). This report describes `7c68042` + the audit-branch
> changes (see `progress.md` for the audit findings).
> **No migration was applied. No commit or push was performed.**

Latest status: [`docs/audit/progress.md`](progress.md).

---

## A. Scope and deliverables (state-check §E, items 1–13)

| # | Scope item | State | Evidence |
|---|---|---|---|
| 1 | **Billing unit follows the service pricing** (bill by meter while production ran by kg), using the order's **approved** quantity in that unit — never a manufactured conversion | **Implemented (server + UI)** | `Application/Common/Services/InvoiceBillingGuard.cs`; `InvoiceLine.Unit` (`Domain/Entities/Invoice.cs`, config `Persistence/Configurations/InvoiceConfiguration.cs`); `CreateInvoiceCommand`, `EditInvoiceCommands`; UI unit selector `Web/src/pages/InvoicesPage.tsx:115` (create row, draft-editor row, add-line row) and `Web/src/api/client.ts` (`InvoiceLine.unit`, all three line bodies). Tests: `Phase6InvoiceBillingUnitTests` — `Billing_ByMeter_UsesTheApprovedMeterQuantity_AndNeverConvertsKgIntoMeter`, `Billing_ByMeter_IsRefused_WhenNoMeterQuantityWasEverApproved`, `Billing_IsRefused_InAUnitTheOrderHasNoServicePriceFor`, `TheBillingUnit_MustBeARealUnit` |
| 2 | The approved quantity cannot be billed twice; a typo can still be corrected | **Implemented** | `Phase6InvoiceBillingUnitTests.TheApprovedQuantity_CannotBeBilledTwice_AndATypoCanStillBeCorrected`; draft editing in `EditInvoiceCommands` |
| 3 | Distinguish execution / billable services / pricing / delivery quantities / chargeable amounts / company-borne costs | **Implemented** | `InvoiceBillingGuard` (which quantity is billable), `InvoiceLine.Unit` (which unit), `CostEntry.IsChargeableToCustomer` + `CreateCostEntryCommand` (company-borne vs chargeable) |
| 4 | Reprocessing/separate costs are **not** auto-added; billed only when explicitly flagged chargeable, never twice | **Implemented (server + UI)** | `CostEntry.IsChargeableToCustomer`, unique filtered index `IX_InvoiceLines_CostEntryId` (`Persistence/Configurations/CostEntryConfiguration.cs`); UI checkbox `Web/src/pages/ProductionOrderDetailPage.tsx:1063` via `CostAccountingApi.addEntry` (`client.ts`). Tests: `Phase6ChargeableCostTests` — `ACompanyBorneCost_IsNeverAutoAdded_AndCannotBeBilled`, `AChargeableCost_IsBilled_AndNeverCountedTwice`, `AReprocessingOrSeparateCost_StaysOutOfInvoicesEntirely_UnlessFlagged` |
| 5 | Registering a return never auto-modifies an invoice or creates a credit note | **Implemented** | `Phase6CustomerReturnInvoiceIsolationTests.RegisteringAReturn_LeavesTheInvoiceAndTheCustomerStatementUntouched`; `CustomerReturns` commands touch no invoice/ledger-charge path |
| 6 | Invoice PDF uses factory settings (name + logo); a missing logo still generates; matches persisted data | **Implemented** | `ICompanyIdentityProvider`/`CompanyIdentityProvider`; `InvoicesController.GetInvoicePdf` → `ReportExportService`; tests `Phase6InvoicePdfTests.TheInvoicePdf_UsesTheConfiguredFactoryName_AndStillGeneratesWithoutALogo`, `TheInvoicePdf_StillGenerates_WithALogo` |
| 7 | Customer statements from authoritative financial records, units kept separate | **Implemented** | `Invoices/Queries/GetCustomerStatementQuery.cs` — a live running balance over `CustomerLedgerEntries`; the per-line `Unit` is what keeps a KG-priced line from being mixed with a Meter-priced one |
| 8 | Treasury: receipts, payments, transfers, customer/supplier payments, reporting, document references, audit, permissions | **Implemented** | `Treasury/Commands/*` (`CreateReceiptCommand`, `CreatePaymentCommand`, `CreateTreasuryTransferCommand`, `CancelTreasuryDocumentCommands`), `TreasuryController`, `Phase6TreasuryChequeCashTests.APaymentMadeWithOurOwnCheque_PostsNoCashUntilThatChequeClears` |
| 9 | Cheques: management + controlled status transitions + document references | **Implemented** | `Domain/Entities/Check.cs` (guarded transitions), `Checks/Commands/CheckCommands.cs`, `ChecksController`, `Web/src/pages/ChecksPage.tsx` |
| 10 | **Endorsed customer cheque settling a supplier payable** — no fictitious cash in/out, full document linkage, no duplicate settlement, correct balances | **Implemented (server + UI)** | `Checks/Commands/EndorsedCheckSettlementCommand.cs`; `POST api/checks/{id}/settle-supplier` (`ChecksController.cs:86`); `SupplierPayment.TreasuryAccountId` nullable + unique filtered index on `CheckId`; UI `ChecksApi.settleSupplier` (`client.ts:2327`) used from `ChecksPage.tsx`. Tests: `Phase6EndorsedCheckSettlementTests` — `EndorsingACustomerCheque_PostsNoCash_AndMovesBothBalances`, `TheSameCheque_CanNeverSettleASecondTime`, `ClearingAnEndorsedCustomerCheque_PostsNoCashEither`, `CancellingTheSettlement_RestoresBothBalances_AndStillPostsNoCash`, `OnlyAnIncomingCustomerCheck_CanSettleASupplierPayable`, `PayingASupplierWithACustomerCheck_IsRefused_BecauseTheEndorsementWorkflowOwnsIt` |
| 11 | Period closing: authorized close, recorded status, validation, closed periods blocked, controlled correction, concurrent-close safe, authorized reopen | **Implemented** | `PeriodClosing/Commands/PeriodClosingCommands.cs`, `Persistence/Services/PeriodCloseService.cs` (SHARED guard for postings, EXCLUSIVE for close/reopen), `PeriodClosingController` + `Web/src/pages/PeriodClosingPage.tsx`; tests `Phase6PeriodClosingTests` — `ClosingTheSameRangeTwice_LeavesExactlyOneClosedPeriod`, `ClosingAnOverlappingRange_IsRefused_SoNoDateSitsInsideTwoClosures`, `ClosingTakesTheExclusiveGlobalPeriodLock_AndStaysPossibleAfterAReopen` |
| 12 | Financial consistency: exports/prints create no stock or financial movement | **Implemented** | `PrintingWorkflowTests`; Phase 5 `Phase5DeliveryStatementTests.PrintingTheStatement_IsReadOnly_AndPostsNoStock`; the endorsement tests assert zero treasury rows |
| 13 | Granular permission policy on every finance endpoint | **Implemented** | `Phase6PermissionTests` — a theory over 23 controller/action/permission pairs (invoices, statement, receipts, payments, treasury accounts/transfers, checks, period closing) asserting each carries `Permission:<right>`, plus `TheFinanceRights_AreSeparatePermissions` for `Permissions.All` completeness |

**Web surface:** the three items `state-check.md §185` listed as `جزئي` (invoice billing unit, the
"chargeable to customer" flag, the `settle-supplier` action) were closed in an earlier session and are
recorded in `progress.md` ("Phase 6 — web-surface closure"); the client/page references in the table above
are the standing evidence.

---

## B. Migration (created, **not applied**)

`src/DyeHouseERP.Persistence/Migrations/20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement.cs` —
five schema changes, each explained inline in the file:

1. `SupplierPayments.TreasuryAccountId` → **nullable** (an endorsed-cheque settlement leaves no account).
2. `IX_SupplierPayments_CheckId` → **unique filtered** index (one cheque settles at most one live payment).
3. `InvoiceLines.CostEntryId` (nullable) — the billed-cost link that makes double billing detectable.
4. `InvoiceLines.Unit` (`nvarchar(20)`, default `'KG'`) — the unit the service is priced in; the default
   matches the domain default and the unit pre-existing rows were already priced in, so no stored figure is
   reinterpreted.
5. `CostEntries.IsChargeableToCustomer` (default `false`) — no historical cost becomes billable by upgrade.

---

## C. Checks actually run in this audit pass (real output)

| Check | Command | Result |
|---|---|---|
| Phase 6 suite (Debug) | `dotnet test tests/DyeHouseERP.UnitTests -c Debug --no-build --filter FullyQualifiedName~Phase6` | **exit 0 — 45 / 45 passed**, 0 failed, 0 skipped |
| Full unit suite (Release) | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **exit 0 — 865 / 865 passed** |
| Backend build | `dotnet build DyeHouseERP.sln -c Release` | **exit 0 — 0 errors, 5 warnings** (pre-existing nullability set) |
| TypeScript/Vite build | `npm run build` (web) | **exit 0** (chunk-size advisory only) |
| Frontend lint | `npm run lint` (web) | **exit 0 — 0 errors, 9 `react-refresh` advisories** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **909 / 909, 0 failures** |
| Permission parity | `node tools/permissions-parity-check.cjs` | **118 / 118, 0 failures** |
| Model vs snapshot | `dotnet ef migrations has-pending-model-changes` | **"No changes have been made to the model since the last migration."** |

## D. Not run (environment), and why

1. **SQL Server integration tests — Not Run.** `DYEHOUSE_TEST_CONNECTION` is unset and no SQL Server exists
   in this environment (`tests/DyeHouseERP.IntegrationTests/CustomWebApplicationFactory.cs`). This means the
   *database-level* halves of Phase 6 are unverified here: the unique filtered index on
   `SupplierPayments.CheckId`, the `IX_InvoiceLines_CostEntryId` unique index, and `sp_getapplock`
   behaviour behind the period-close guard. They are asserted by migration SQL/metadata and by the handler
   tests that substitute the lock, and remain open until a database is available.
2. **Interactive browser pass — Not Run.** No browser can be launched in this environment, so the billing-unit
   selector, the chargeable checkbox and the `settle-supplier` action are verified by the TypeScript build,
   ESLint and source review — not by clicking.
3. This report does **not** re-claim the earlier session's command output for Phase 6; the numbers in §C are
   the ones produced in this pass.
