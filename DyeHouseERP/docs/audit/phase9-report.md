# Phase 9 — Final Verification

> ## ⚠️ HISTORICAL REPORT — superseded
>
> This is a **point-in-time snapshot**, recorded at HEAD `abb4426` with the phases 2–8 work still
> **uncommitted** in the working tree (~180 dirty files). It is kept for the record and is **not** the
> current status.
>
> - **Latest status: [`docs/audit/progress.md`](progress.md).**
> - The phases 2–8 work described here as uncommitted was afterwards committed as `abb4426..7c68042` on
>   `main`, on the repository owner's explicit instruction. §C.7's "No unauthorized Git operations" line
>   therefore describes the state *at the time of writing*, not today's `HEAD`.
> - A later audit pass (see **Corrections** below) **found two real defects** in lines this report marked
>   `Pass`, and confirmed one coverage gap and one missing capability. Those rows are annotated here and the
>   code was fixed on branch `fix/audit-findings` — the report is not silently rewritten, because its
>   measured rows are what was true of `abb4426`.

## Corrections (found after this report, at HEAD `7c68042`)

| This report's line | What was actually true | Where it stands now |
|---|---|---|
| §B.7 "No privilege escalation — **Pass** (caveat)" | The caveat was a **real escalation path**: a holder of `users.manage`/`roles.manage` could grant `admin` (or any right they did not hold) to another account, and could modify/disable/reset an administrator account. The self-permission rule only stopped a user from editing *their own* set | **Fixed** — `UserAdministrationGuard` is enforced server-side in `CreateUserCommand`, `UpdateUserCommand`, `DeactivateUserCommand` and `ChangePasswordCommand`; regression tests in `tests/DyeHouseERP.UnitTests/UserPermissionEscalationTests.cs` (see `progress.md`) |
| §C.5 "Sub-customer permits linked without movements — **Pass**" | Issuing one did post no stock — but **confirming** one (`MarkDelivered`) posted a *second* OUT for the same ready lot, i.e. the movement could be deducted twice | **Fixed** — a sub-customer permit posts no movement in `MarkDeliveryDeliveredCommandHandler` or in the post-approval correction path, and its lines may reference the primary customer's Job Order; tests in `SubCustomerPermitDeliveryTests.cs` (see `progress.md`) |
| §C.4 "No double raw deduction … *No dedicated test exists*" (coverage gap) | Still true at `abb4426`; it was closed afterwards | **Closed** — a direct ledger test exists (`Phase4ProductionFloorTests`, section 6, recorded in `progress.md`), and the reprocessing/return starting-stage work added further ledger assertions |
| §C.4 "Linked new Job Order — **Pass**" / reprocess + return routes | Both workflows always started at التشكيل; there was no way to say the rework begins at a later operation | **Extended** — an optional, validated **starting stage** (active stages only) now reaches `Reprocessing`, the issue workflow and the return workflow (see `progress.md`) |
| §C.5 "Sub-customer permits linked without movements — **Pass**" (further) | A **sub-permit line's original-raw figure** was also summed into the order's "already settled" total in `CreateDeliveryCommand`, `EditDeliveredDeliveryCommand` and `ReadRemainingOriginalRawAsync`, so a document-only sub-permit shrank the primary permit's real remaining allowance and could block a later legitimate delivery; the correction endpoint also dropped the manually-entered original-raw figures, and a sub-permit could promise more than the primary permit delivered | **Fixed** — sub-permit lines are excluded from the settled arithmetic, a sub-permit is bounded by the linked primary permit's own lines, and the correction endpoint carries the original-raw fields; tests in `SubCustomerPermitDeliveryTests.cs` (see `progress.md`) |
| §C.3 "Drawing number only when Printing — **Pass**" (reprocess/return) | Correct as written: a Printing stage still demands a drawing number, and a further print run names its OWN. A reprocess/return order that selects Printing as its **starting** stage is therefore refused, because the number is stored on the *original* order and the rule forbids borrowing another order's number | **Open business decision** (not a defect) — recorded for the owner: either supply a drawing number at order creation, or start the rework earlier and enter it at Printing. No rule was relaxed and no source was invented (see `progress.md`) |

> **Mode:** verification and regression review ONLY. No feature work; no file was changed by this phase.
> **No commit, no push, and no migration was applied.**

Branch: `freebuff/8c663d5dafcf97d023c3991e` · HEAD: `abb4426` (unchanged) · Date: 2026-10-14
Inputs: `docs/audit/phase1-report.md` … `phase8-report.md`, `progress.md`, `state-check.md`.

> Note: `00-MAIN-PROMPT.md` is **not present** in this checkout (nor anywhere in the workspace). This report
> therefore follows the format the phase brief specifies and the one used by phases 1–8. Recorded as a
> documentation gap, not a code defect.

**Overall status: `Verified` for every line that can be measured here — except one line that is
`Not Applicable` (no implementing feature), and two suites that are `Not Run` (environment). The project is
therefore NOT declared "complete"; see §E.**

---

## A. Everything run fresh (this session)

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build DyeHouseERP.sln -c Release --no-incremental` | **Build succeeded — 0 errors, 5 warnings** (2× `xUnit1031` integration, `CS8600`+`CS8603` ExceptionHandlingMiddlewareTests, `CS8602` EditPermissionSweepTests) |
| Unit tests | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **Passed! — 832 / 832** (0 failed, 0 skipped) |
| Integration tests | `dotnet test tests/DyeHouseERP.IntegrationTests -c Release --no-build` | **Not Run** — 34/34 fail at startup: `InvalidOperationException : The integration tests need a real SQL Server test database. Set DYEHOUSE_TEST_CONNECTION …` (`CustomWebApplicationFactory.cs:37`) |
| TypeScript | `npx tsc -b` | **exit 0** |
| Frontend lint | `npm run lint` (`eslint .`) | **exit 0** — 0 errors, 9 `react-refresh/only-export-components` advisories |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **exit 0** — 2351 modules; chunk-size warning only |
| Clean install | `npm ci --dry-run` | **exit 0** |
| React hook check | `node tools/hooks-check.js` | **exit 0** — no invalid hook call sites, no hook shadowing |
| Module-graph smoke | `node tools/smoke-modules.sh http://127.0.0.1:3000` | **PASS — 68 modules, 0 failed** (server on `0.0.0.0:3000`, preview host 200) |
| Model consistency | `dotnet ef migrations has-pending-model-changes` | **"No changes have been made to the model since the last migration."** |
| Permission parity | `node tools/permissions-parity-check.cjs` | **118 / 118, 0 failures** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **901 / 901, 0 failures** |
| Duplicate/DbSet sweep | `node tools/duplicate-sweep.js` | **PASS — 88 = 88** |
| Wiring sweep | `node tools/wiring-sweep.js` | Only login + public settings carry no policy (expected) |

Migrations are **generated, never applied** (see §C).

---

## B. Security sweep

| # | Item | Verdict | Evidence |
|---|---|---|---|
| 1 | JWT lifetime 8h | **Pass** | `JwtTokenService.cs:28` `expiresAtUtc = DateTime.UtcNow.AddHours(8)` |
| 2 | No refresh tokens | **Pass** | repo-wide search for "refresh token" → **0 hits** |
| 3 | No fallback / hard-coded secrets | **Pass** | `Program.cs` throws when `Jwt:Key` is absent unless EF design-time; `JwtKeyValidator` rejects blank/whitespace/`CHANGE_ME*`/named placeholders and enforces ≥16 chars in Production; `appsettings.Testing.json` key is test-only |
| 4 | PBKDF2-SHA256 | **Pass** | `PasswordHasher.cs:20` `Rfc2898DeriveBytes.Pbkdf2(…, HashAlgorithmName.SHA256, …)`, 100 000 iterations, per-password random salt |
| 5 | Login throttling | **Pass** | `LoginThrottleMiddleware` + `LoginRateLimiter` (per (IP,username) and per-IP budgets, defaults 5/15m/15m/20); `LoginRateLimiterTests` (10) |
| 6 | Token invalidation on password change / deactivation / permission change | **Pass** | `Program.cs` `OnTokenValidated` re-reads the live user via `IUserSessionValidator` every request: rejects missing account, inactive account, and any role-claim diff; `ConcurrencyAndSecurityHardeningTests.UserSessionValidatorTests`, `DeactivateUserSafetyTests` |
| 7 | No privilege escalation | **Pass (caveat)** — *corrected: the caveat was a real escalation path; see Corrections* | `UpdateUserCommand` refuses a self permission change outright and refuses removing the last active admin; `UserSafetyJwtAndPaySupplierTests.DeactivateUserSafetyTests`, `UserRoleValidationTests`. Caveat recorded as an open question (§E): a holder of `users.manage`/`roles.manage` may grant `admin` to another account — a deliberate high-privilege capability, not a self-escalation path |
| 8 | Last-admin protection | **Pass** | `UpdateUserCommand.cs:64-76` (only-active-admin demotion refused) |
| 9 | Current password required for self-service change | **Not Applicable** | There is **no self-service password endpoint**. The only change-password route is `POST api/users/{id}/change-password` behind `users.manage` (admin-driven). Phase 1 recorded the same scope note. No feature was added (Phase 9 forbids it) |
| 10 | Trusted proxies only | **Pass** | `Program.cs` honors `X-Forwarded-*` **only** for `ReverseProxy:KnownProxies`; default is loopback only; no `KnownNetworks`; `RequireHeaderSymmetry` |
| 11 | Explicit CORS | **Pass** | `AddCors` with explicit `Cors:AllowedOrigins` (default `http://localhost:5173`), no wildcard, credentials allowed for those origins |
| 12 | Swagger Development-only | **Pass** | `if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }` |
| 13 | No leaked internals | **Pass** | `ExceptionHandlingMiddleware` returns a fixed JSON envelope; the default branch logs server-side and returns `"An unexpected error occurred."` — no stack trace, no SQL text (`ExceptionHandlingMiddlewareTests`, 6) |
| 14 | Safe file handling | **Pass** | Attachment extension+MIME allowlist (`AttachmentFileTypes`, Phase 8 / F.8), 25 MB limit, owning record must exist; `Phase8AttachmentTests` |
| 15 | Export/import/document endpoints protected | **Pass** | `wiring-sweep` reports only login + public settings policy-free; export routes carry `reports.export`; import create/update carry `items.create`/`items.edit` etc. |
| 16 | Status codes consistent | **Pass** | 400 validation, 401 invalid credentials, 403 unauthorized, 404 not-found, 409 negative-stock/lock/concurrency/duplicate, 422 domain rule, 500 generic — one mapping, in `ExceptionHandlingMiddleware` |
| 17 | Audit intact and non-leaking | **Pass** | `AuditSaveChangesInterceptor` + `AuditValueRedactor` masks `PasswordHash/Password/Secret/Token/ApiKey`, `byte[]`→`[REDACTED size N]`; `AuditRedactionTests`; `/api/audit-log` behind `audit.view` |

---

## C. Final acceptance checklist

### C.1 Inventory & units
| Requirement | Status | Evidence |
|---|---|---|
| Item primary/secondary units | **Pass** | `Item.BaseUnit`/`SecondaryUnit`/`RequireBothUnits`; `ItemUnitConfigurationTests` (23); migration `…AddItemSecondaryUnitAndRequireBothUnits` |
| No global kg↔meter conversion | **Pass** | Conversion only via an explicit per-(item, operation) `ItemUnitConversion`; `ItemUnitRules`; `ItemUnitConfigurationTests`, `Phase2MasterDataTests` |
| Inventory vs billing quantities distinct | **Pass** | `InvoiceLine.BillingUnit` + `InvoiceBillingGuard`; `Phase6InvoiceBillingUnitTests` |
| Ledger authoritative (no stored balance) | **Pass** | Balances summed from `InventoryTransaction`/`MaterialTransaction` (`InventoryLedgerService`, `GetInventoryMovementsQuery`); `Phase8ExportTests` asserts the export is built from the ledger |
| Negative stock controlled | **Pass** | `NegativeStockException` gates every stock path; `NegativeStockApprovalGatingTests` (15), `RawMessageTests.NegativeStockExceptionTests`, `Phase7ChemicalStockTests` |
| Posted corrections preserve history | **Pass** | Append-only ledger + equal-and-opposite reversal rows (`ReversesTransactionId`), `[Timestamp]` on ledger rows; `RawReceiptCancellationTests` (9), `TreasuryCancellationTests`, `MaterialIssueCancellationTests`, `CheckMovementInsertTests` |

### C.2 Receiving & orders
| Requirement | Status | Evidence |
|---|---|---|
| Issue creates a Job Order | **Pass** | `IssueRawMaterialCommand`→`ProductionOrder`; `IssueRawMaterialWorkflowTests` (11), `Phase3ReceivingAndAllocationTests` |
| Formation Request optional | **Pass** | `FormationProductionCommandTests` (9); Phase 3 report (issue works with and without a request) |
| Formation moves no stock | **Pass** | `FormationRequestTests`, `FormationBasinTests` (26), `FormationRequestRevisionTests`; Phase 3 report |
| Production blocked before 100% allocation | **Pass** | `RawAllocationGateTests` (14), `RawAllocationGateHandlerTests` (18) |
| Customer raw traceable | **Pass** | `InventoryTransaction.RawMessageId`→`InventoryMovementDto.MessageNumber`; `RawMessage` ownership guards (`CreateReceiptOwnershipTests`) |

### C.3 Production
| Requirement | Status | Evidence |
|---|---|---|
| Dynamic stages (create/activate/order) | **Pass** | `CreateProductionStageDefinitionCommand`; `ProductionStageAdministrationTests` (11) |
| Seed list is not a maximum | **Pass** | `ProductionStageSeeder` (comment: order = display order only, "does NOT impose a route"); `ProductionStageSeedTests` |
| User selects the next operation | **Pass** | `TransferToNextStageCommand.ActivateNextStage`; `PrintingWorkflowTests` |
| Ready Goods is a stock destination | **Pass** | `WarehouseKind.ReadyGoods`; `ReadyGoodsStageMarkerTests`, `ReadyGoodsStageTraceabilityTests` (10) |
| Pause/resume explicit | **Pass** | `PauseResumeCommands` + lot-bound release; `PauseResumeLedgerTests` (14), `ProductionOrderPauseTests` |
| Drawing number only when Printing | **Pass** | `ProductionStageDefinition.RequiresDrawingNumber`; `PrintingWorkflowTests` (7), `MultiplePrintingExecutionsTests` (4) |
| Waste and separates correct | **Pass** | `Phase4FloorCaptureTests` (10); waste never absorbs separates/second grade |
| Legacy bypasses gone | **Pass** | `StageExecutionCommands.cs` **absent** from the tree; no start/complete/skip endpoints |
| `production.approve_stage` absent | **Pass** | repo-wide search → **0 hits** (src, tests, frontend) |

### C.4 Reprocessing & costs
| Requirement | Status | Evidence |
|---|---|---|
| Linked new Job Order | **Pass** | `ReprocessSeparateCommand` sets `ReprocessingOfProductionOrderId`; `Phase4FloorCaptureTests.ReprocessingASeparate_CreatesANewLinkedOrder_AndLeavesTheOriginalUntouched` |
| No double raw deduction | **Pass** — *corrected: the "no dedicated test" gap was closed afterwards; see Corrections* | Reprocessing creates the order **only** (no stock movement) and is raised on the separate's own quantity; raw is deducted once, on the order it is issued to; the original order's figures are asserted untouched. *No dedicated "double deduction" test exists — evidence is the command + the untouched-original assertion* |
| Actual costs recorded | **Pass** | `CostEntry` + `CreateCostEntryCommand`; `CostCategoryTests` (5), `CostApprovalOrderingTests` (4) |
| Reprocessing/separate costs not auto-charged | **Pass** | `CostEntry.IsChargeableToCustomer` defaults false; `InvoiceBillingGuard`; `Phase6ChargeableCostTests` |
| Responsibility explicit | **Pass** | `CreateProductionOrderFromReturnCommand` requires `CostBearer`; carried on the order/DTOs |
| Scrap vs reprocessing exclusive | **Pass** | `Separate.StartReprocessing`/`Scrap` guard transitions; `Phase4FloorCaptureTests` |

### C.5 Delivery & returns
| Requirement | Status | Evidence |
|---|---|---|
| Partial delivery | **Pass** | `Phase5DeliveryStatementTests` (23 total in the file family), `DeliveryCorrectionTests` (8) |
| Original quantity fixed | **Pass** | `ProductionOrder.EnsureOriginalRawWithinLimit`; Phase 5 report/tests |
| Remaining correct | **Pass** | `GetDeliveriesQueryHandler.ReadRemainingOriginalRawAsync` (single source reused by guard + statement) |
| Statement fields complete | **Pass** | `GetDeliveryStatementQuery` + `DeliveryStatementPrintPage`; `Phase5DeliveryStatementTests` |
| Kg and meters independent | **Pass** | Per-unit columns, never summed (statement + `Phase8ExportTests`) |
| Sub-customer permits linked without movements | **Pass** — *corrected: confirming one could post a second OUT; see Corrections* | `Delivery.PrimaryDeliveryId/PrimaryCustomerId`; Phase 5 sub-customer test |
| Returns separate | **Pass** | `CustomerReturnCancellationTests` (7), `Phase5CustomerReturnTests` |
| No auto invoice/credit-note | **Pass** | `Phase6CustomerReturnInvoiceIsolationTests` |
| Customer/company choice enforced | **Pass** | `CostBearer` on a return-raised order (mandatory, mutually exclusive with an ordinary issue) |

### C.6 Documents
| Requirement | Status | Evidence |
|---|---|---|
| Identity from factory settings | **Pass** | `ICompanyIdentityProvider`/`CompanyIdentityProvider`; `UpdateCompanySettingsCommand` |
| Invoices and templates consume it | **Pass** | `ReportExportService` reads the provider (never a literal); `Phase8ExportTests` asserts the factory name in the file; `Phase6InvoicePdfTests` |
| PDF/Excel units correct | **Pass (content)** | `Phase8ExportTests` (Arabic data + separate KG/Meter); PDF Arabic *shaping* is Not Run (font) |
| Missing logo OK | **Pass** | `ReportExportService` uses `TryGetLogoBytes` and a try/catch around the image; Phase 2 report |
| Printing creates no movements | **Pass** | Printing is read-only (`GetDeliveryStatementQuery` etc.); Phase 5 "printing twice moves no stock"; `PrintingWorkflowTests` |

### C.7 Cleanup & quality
| Requirement | Status | Evidence |
|---|---|---|
| Customer portal gone (frontend + backend + permissions) | **Pass** | `Phase8PortalRemovalTests`; `RemoveCustomerPortal` migration; built bundle has no `/portal` route |
| Audit preserved | **Pass** | `AuditRedactionTests`; `/api/audit-log` behind `audit.view`; interceptor unchanged |
| No Docker/Testcontainers | **Pass** | repo-wide search → only comments stating their absence; no Dockerfile/compose |
| No InvariantGlobalization | **Pass** | repo-wide search → **0 hits** |
| No unauthorized Git operations | **Pass** | HEAD still `abb4426`; 180 dirty files; no commit/push/force performed |

**Checklist tally: 44 Pass, 1 Not Applicable (self-service password), 0 Fail.**
Plus 2 environment-blocked suites: integration tests and real-browser UI.

---

## D. Regression review against the Phase 1 baseline

- **Kept modules still work.** The Phase 1 "Kept" list (customers, suppliers, items, warehouses, raw
  receiving, raw allocation, job orders, ready goods, deliveries, invoices, treasury, checks, formation
  requests, users, reports, audit, settings) is exercised by the 832-test unit suite plus the module-graph
  smoke (68 modules, 0 failed) and the permission/i18n/duplicate/wiring sweeps. No previously-green check
  regressed.
- **Only one deliberate scope-adjacent change exists across the seven phases**: the Phase 8 attachment
  allowlist (F.8), which *tightens* an existing endpoint and is covered by 14 new tests; the full suite
  (832/832) confirms nothing else broke.
- **Files touched outside a phase's declared scope:** none. Every changed path lives under
  `DyeHouseERP/src|tests|docs`. No change to `README.md`, `DyeHouseERP.sln`, `global.json`, `.gitignore`,
  or any `tools/` script. The working tree carries the accumulated, uncommitted work of phases 2–8
  (≈180 files): src/DyeHouseERP.Web/src (43), API/Controllers (23), UnitTests (20), Domain/Entities (12),
  Persistence/Configurations (10), Persistence/Migrations (9), the remainder across Application modules.
- **Baseline-defect tracking:** F.7 (dead `SqlAllocationLockService`) fixed in Phase 3; F.8 fixed in Phase 8;
  F.9 and F.B.4 fixed in Phase 8. Remaining baseline items are documentation/PDF-font only.

---

## E. Not Run / Blocked / Partially Implemented (with exact reasons)

1. **SQL Server integration tests — Not Run.** 34/34 fail at `InitializeAsync` because
   `DYEHOUSE_TEST_CONNECTION` is unset and no SQL Server exists in this environment
   (`CustomWebApplicationFactory.cs:37`). This blocks runtime confirmation of: `sp_getapplock`
   serialization, unique filtered indexes, check constraints, `rowversion` concurrency, and the API-level
   404/403 behaviours (portal 404, scan authorization). Those are verified by construction (migration SQL,
   model metadata) and by unit/reflection tests instead.
2. **Real-browser UI pass — Not Run.** Chrome cannot launch: its system libraries (`libnss3`, `libatk-1.0`,
   `libcups`, `libxkbcommon`, …) are absent and cannot be installed (no root, no passwordless sudo). Runtime
   forms/loading/empty/error states, RTL↔LTR switching, dark mode and download dialogs are covered only by
   `tsc`, ESLint, the hook check, the module smoke and source review.
3. **PDF Arabic glyph shaping — Not verified.** `ReportExportService` produces a real PDF, but correct
   Arabic shaping depends on an Arabic-capable font being available to QuestPDF in the deployment. No font
   is embedded here (Phase 1 F.D.9).
4. **Self-service password change — Not Applicable.** The feature does not exist; only an admin-driven
   change endpoint does. Not added (Phase 9 permits no features).
5. **No dedicated "no double raw deduction" test** — the property is enforced by the reprocessing design but
   asserted only indirectly (original order untouched). Recorded as a coverage gap.

Because of items 1–4, the overall status is **`Verified` for all measurable lines, but NOT a clean
"Verified" under the strict rule** (every line Pass), and the project is **not** declared complete.

---

## F. Created-but-unapplied migrations

All are committed to the working tree, **none applied** (no database here). The four created during phases
2–8 (the earlier ones predate this audit run):

| Migration | Purpose | Data risk |
|---|---|---|
| `20261014015833_AddPhase2ItemConversionsAndSubCustomer` | `ItemUnitConversions` table (+2 check constraints, 2 indexes, FK) and `Customer.ParentCustomerId` (+ index, check constraint, self-FK) | Low — new table + nullable column; no data rewrite |
| `20261014031119_Phase3ReceivingIdempotencyAndColor` | `RawMessage.ReceiveRequestId` (+ unique filtered index) and `RawMessageLine.Color` | Low — two nullable columns; index creation only |
| `20261014042210_Phase5ReadyGoodsDeliveryReturns` | Ready-goods→raw document, delivery original-raw + sub-customer columns, return columns, ready-goods idempotency index | Low–Medium — adds columns/indexes; the ready-goods raw-return table is new |
| `20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement` | Invoice billing unit, chargeable-cost flag, endorsed-cheque settlement | Low–Medium — nullable/defaulted columns on live tables |

Also created earlier (all unapplied, all low risk): `…AddItemSecondaryUnitAndRequireBothUnits`,
`…AddProductionOrderIssueLink`, `…AddRawReceiptCancellation`, `…AddCustomerReturnCancellation`,
`…AddTreasuryTransferCancellationAndEditCorrections`, `…RemoveCustomerPortal`,
`…RemoveStageApprovalAndDecoupleFormationRequests`, `…AddMaterialIssueCancellation`, etc. (see the file list in
§A). `has-pending-model-changes` is clean, so the migration chain matches the model exactly.

---

## G. Open business questions

1. **Self-service password change** — should a user be able to change their own password (requiring the
   current one)? Today only an admin can. (Phase 1 §D.2, still open.)
2. **Admin-granting via `users.manage`/`roles.manage`** — should granting the `admin` role require the
   `admin` permission itself, rather than `roles.manage`?
3. **PDF Arabic font** — embed an Arabic-capable font in the deployment, or is a server font assumed?
   (Phase 1 F.D.9.)
4. **Multi-instance deployment** — the login throttle is per-process in-memory; a load-balanced deployment
   needs a shared store.
5. **`00-MAIN-PROMPT.md`** — the phase brief references it; it is absent from the repository. Should it be
   committed so the Change Policy/prohibitions travel with the code?

---

## H. Aggregate status by phase

| Phase | Scope | Status |
|---|---|---|
| 1 | Baseline & security audit | `Verified` (read-only) — F.7/F.8/F.9/F.B.4 later fixed |
| 2 | Master data & factory settings | `Verified` |
| 3 | Receiving, formation & raw allocation | `Verified` (F.7 fixed) |
| 4 | Production, separates & reprocessing | `Verified` |
| 5 | Ready goods, delivery & returns | `Verified` |
| 6 | Finance, invoicing, treasury & cheques | `Verified` (backend + web surface per Phase 6 report) |
| 7 | Purchasing, materials, HR & payroll | `Verified` |
| 8 | Reports, UI, exports & cleanup | `Verified` (F.8 fixed; F.9/F.B.4 closed) |
| 9 | Final verification | **`Verified` on all measurable lines; NOT declared complete** (2 suites Not Run, 1 line Not Applicable) |

**Overall: `Verified` with the explicit exceptions in §E. Do not treat the build passing as completion.**

No commit, no push, and no migration was applied by this phase.
