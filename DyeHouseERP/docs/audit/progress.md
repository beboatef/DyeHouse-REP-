# Audit progress log

Baseline: HEAD `abb4426`, branch `freebuff/8c663d5dafcf97d023c3991e`. No commit, no push, no migration applied.

---

## Phase 1 — Baseline & security audit — `Verified`
Read-only. See `docs/audit/phase1-report.md`. Key deferred findings: **F.7** (`SqlAllocationLockService._context`
never assigned → deferred to Phase 3/8), F.8 (attachment content-type allowlist → Phase 8), F.9 (`npm ci` broken),
F.B.4 (frontend lint not runnable), no SQL Server for the integration suite.

## Phase 2 — Master data & factory settings — `Verified`
See `docs/audit/phase2-report.md`.
- Items: item+operation-scoped explicit `ItemUnitConversion` rule (entity, config, commands, DTO, query, endpoints, UI, tests).
- Customers: stored primary → sub-customer FK with app-level cycle detection.
- Factory settings: `ICompanyIdentityProvider`, `ReportExportService` rebuilt (name + logo, RTL), logo upload validation.
- One migration created (not applied): `20261014015833_AddPhase2ItemConversionsAndSubCustomer`.
- Verified without change: suppliers/warehouses CRUD, dynamic stages, machines/shifts, pricing, optional inspection.
- Results: build PASS (0 errors, 7 warnings), 715/715 unit tests, frontend build PASS, i18n 887/887,
  model clean, migration SQL valid. Integration tests + frontend lint: **Not run**.

## Phase 3 — Receiving, formation requests & raw allocation — `Verified with one real defect fixed (FINAL)`
See `docs/audit/phase3-report.md`.
- **F.7 FIXED (the phase's headline defect):** `SqlAllocationLockService` had no constructor, so DI built it
  with a **null** `DbContext` and every stock lock threw `NullReferenceException` before `sp_getapplock` — the
  serialization guarantee existed only in unit tests (which substitute a lock double). Added the constructor;
  the two compiler warnings that signalled it (`CS8618`/`CS0649`) are gone.
- **Receiving idempotency added** (scope item 1 "once, idempotent against retries"): optional `RequestId` token on
  `CreateRawMessageCommand`, `RawMessage.ReceiveRequestId`, unique **filtered** index, retry short-circuit; one
  shared DTO loader so a retry answers with the live ledger balance; frontend token per form.
- **Receiving colour added** (scope item 1's field list, the only field the receipt lacked): optional per-line
  `RawMessageLine.Color`, DTO/command/UI/print/PDF columns, i18n.
- **Allocation display added** (scope item 4 "display required / allocated / remaining / status"):
  `ProductionOrderDto` now carries allocated/remaining/complete computed by the **same** domain rule the
  `RawAllocationGuard` enforces; four summary tiles on the job-order detail page.
- One migration created (not applied): `20261014031119_Phase3ReceivingIdempotencyAndColor` (two nullable columns +
  one unique filtered index).
- Verified without change: Formation Requests write **zero** stock and are decoupled from Job Orders; issue →
  PO is atomic (with/without a Formation Request, actual ≠ planned, idempotent); 100% allocation gate on 8 call
  sites; traceability receipt → issue → PO; server-side permissions for receiving/issue/exception/adjustments;
  edit-vs-cancel rules.
- Results: build PASS (0 errors, **5** warnings — two fewer than baseline), **725/725** unit tests (+10 new),
  frontend build PASS, i18n 896/896, permission parity 116/116, model clean, migration SQL valid.
  Integration tests (no SQL Server) and frontend lint: **Not run** — so the SQL-level lock behaviour and the
  unique-index race backstop are verified by construction only.

## Phase 4 — Production, separates & reprocessing — `Verified` (see `docs/audit/phase4-report.md`)
A state check first (`docs/audit/state-check.md`) put the phase at `جزئي` with four open items; all four are
now closed:
- **The real defect found while inventorying:** no code anywhere created a `Separate`, so the separates
  register (and its reprocess/scrap decisions) could never receive data even though the transfer recorded a
  separates figure. `TransferToNextStageCommand` now adds the register row in the same closing transaction.
- **Production-floor capture surface wired** (`phase5-report.md` §F's pending dependency): the transfer form
  and `client.ts` now carry the actual input, separates and second-grade quantities (KG/Meter, all optional,
  validated server-side against the item's own units), with bilingual keys.
- **`production.scrap` decided and implemented** (Phase 1 §G.1): a dedicated right, no longer implied by
  `production.edit` (`Permissions.ProductionScrap`; `SeparatesController.Scrap`; mirrored in `permissions.ts`).
- **Tests added:** `tests/…/Phase4ProductionFloorTests.cs` — 10 tests (as-entered capture, waste never absorbs
  separates/second grade, unit refusals, register entry, scrap once with a reason, reprocess genealogy,
  reprocess unit refusal, unknown id).
- No migration needed (no model change); `has-pending-model-changes` clean.
- Results: build PASS (0 errors, 5 warnings), **794/794** unit tests (+10), permission parity 118/118,
  i18n 903/903, duplicate + wiring sweeps clean, frontend typecheck + build PASS. Integration tests (no SQL
  Server) and frontend lint: **Not run** — so the DB-backed confirmation of ready-goods diversion / drawing
  number is still open. Carried to Phase 8: PDF Arabic font, attachment allowlist (F.8), `npm ci`/eslint.

## Phase 5 — Ready goods, delivery & returns — `Verified` (see `docs/audit/phase5-report.md`)
Completed the interrupted implementation. Ready-goods→raw document (`ReadyGoodsRawReturn`, own permission +
sequence + audit, two ledger rows, availability checked under the stock lock); partial delivery against the
Job Order's FIXED original raw quantity; the DELIVERY STATEMENT (بيان تسليم) — per line original/settled/
remaining/ready/bales/scraps/2nd grade/calculated waste, **per unit, never mixed**, read-only — as screen
(configurable columns), PDF and Excel; the sub-customer permit (real primary-permit + stored parent link,
posts no stock); and customer returns (raw-material destination only, ownership + reference document, audited
cancel, and an optional new Job Order that MUST state the cost bearer).
- **Added this session:** `tests/…/Phase5ReadyGoodsDeliveryReturnsTests.cs` — 22 tests. No production code
  needed a fix.
- **Incident repaired:** an errant `dotnet ef migrations remove` (run with a stale Debug build) had deleted the
  untracked Phase 2 migration and reverted the model snapshot; both were restored and the chain re-proved
  (see `phase5-report.md` §C).
- Results: build PASS (0 errors, 5 warnings), **739/739** unit tests (+22), frontend build PASS,
  permission parity 117/117, i18n 896/896, duplicate sweep OK, model clean (`has-pending-model-changes`),
  migration SQL chain valid. One migration created (not applied):
  `20261014042210_Phase5ReadyGoodsDeliveryReturns`. Integration tests (no SQL Server) and frontend lint:
  **Not run**.
- Pending Phase 4 dependency: **closed in Phase 4** — the floor now sends second grade / separates / actual input.

## Phase 4 — continuation pass (this session) — `Verified`, gaps closed

Run after the workspace was restored, so **every number below was re-measured here** rather than trusted from the
reports. No commit, no push, no migration. Order of work, exactly as instructed:

### 1. Release build (requested first)

`dotnet build -c Release` → **PASS**. The SDK is not on `PATH` after a restore; it lives at `$HOME/.dotnet`
(and `dotnet ef` at `$HOME/.dotnet/tools`), so the commands need `PATH`/`DOTNET_ROOT` set. Incremental run: 0
errors / 2 warnings; full `--no-incremental` run: **0 errors, 5 warnings** (`CS8600`+`CS8603` in
`ExceptionHandlingMiddlewareTests`, `CS8602` in `EditPermissionSweepTests`, 2× `xUnit1031` in the integration
project) — the same 5 the reports recorded, none new.

### 2. The nine Phase 4 items, each with its status and its evidence

| # | Item (as briefed) | Status | Evidence re-checked here |
|---|---|---|---|
| 1 | **تنفيذ المراحل** — the transfer IS the end of an operation (closes, records output, derives loss % from the immutable baseline, locks, activates only the chosen next stage; machine + execution shift recorded) | **تم** | `TransferToNextStageCommand.cs` (`CloseWithOutput`, `ActivateNextStage`, `EnsureMachineAndShiftAreUsableAsync`), `ProductionOrderStageExecution.cs`; tests `ProductionStageAdministrationTests`, `ProductionOrderTests`, `Phase4ProductionFloorTests` |
| 2 | **حذف الـ legacy** — the old start/complete/skip workflow and its endpoints gone; `AllowSkip` column dropped | **تم** | `StageExecutionCommands.cs` deleted (absent from the tree); `AllowSkip` exists only in migrations/comments; `ProductionStagesController`/`ProductionOrderStageExecution` expose no start/complete/skip. **This pass closed the last frontend residue** (see §3) |
| 3 | **الإيقاف والاستئناف** — pause + resume with an explicit, lot-bound raw release | **تم** | `PauseResumeCommands.cs`, endpoints `POST …/pause`, `POST …/resume`; `PauseResumeLedgerTests`, `StageMarkerAndPauseTests` |
| 4 | **رقم الرسمة** — required server-side before printing; a further print run carries its OWN number | **تم** | `stage.EnsureDrawingNumber()`, the `isAdditionalPrintRun` rule in `TransferToNextStageCommand.cs`; `MultiplePrintingExecutionsTests` (4 tests) |
| 5 | **الفصل** — separates as their own lifecycle + register, never waste | **تم** | `Separate.cs`, register row added on close (`TransferToNextStageCommand` — the previously-unreachable link), `SeparatesController`, `SeparatesPage`; `Phase4ProductionFloorTests` (4 tests) |
| 6 | **إعادة التشغيل** — reprocess into a NEW linked order, original history untouched | **تم** | `ReprocessSeparateCommand.cs` (+ unit guard, genealogy via `ReprocessingOfProductionOrderId`); 3 tests |
| 7 | **التكاليف** — the order's costing reads only factory-owned issues and the recorded output denominator | **تم** | `GetProductionOrderCostQuery.cs`, `ProductionOrderCostingCommands.cs`; `CostApprovalOrderingTests`, `CostCategoryTests` (untouched this pass — no defect) |
| 8 | **التخريد** — scrap with a mandatory reason, once only, its own right | **تم** | `ScrapSeparateCommand` (+ validator), `Separate.Scrap`; 3 tests incl. the permission test |
| 9 | **الصلاحيات** — `production.scrap` is real and never implied by `production.edit`; parity holds | **تم** | `Permissions.ProductionScrap`, `SeparatesController.Scrap` policy, `permissions.ts`; `permissions-parity-check` **118/118** |

Item 11 of the older state check (DB-backed confirmation of ready-goods diversion + drawing number) stays
**blocked**, not failed: the integration suite cannot start without SQL Server (`Program.cs:233`).

### 3. The gaps that were actually open, and what was done

Only the front end was incomplete — the backend legacy deletion had not been mirrored in the UI/TS contract:

- **`ProductionStagesPage.tsx` still edited and displayed `allowSkip`** (form switch, create body, and a
  "Skippable" badge that could never be true) while the backend had already removed `AllowSkip` from the
  entity, the DTO and `CreateProductionStageDefinitionCommand`. Removed the switch, the field and the badge;
  the list's "Properties" column now shows the two capability switches that DO exist
  (`requiresInputQuantity` / `requiresOutputQuantity`).
- **`client.ts` still declared `allowSkip`** on `ProductionStageDefinition`, on the `ProductionStagesApi.create`
  body and on `StageExecution` — sending/stating a capability the API no longer has. All three removed.
- **`StageExecution` omitted `secondGradeKg` / `secondGradeMeter`** although the DTO exposes them — added, so the
  front end can read the figure the floor records.
- **`StageRow.tsx` never showed the Phase 4 figures back.** The floor could record the actual input, the
  separates and the second grade, but the operation history displayed only baseline/output/loss. Those three
  are now shown on the row (reusing the existing bilingual keys).
- **`SharedEnums.cs`** — the `Skipped = 4` comment still claimed "only allowed if the stage definition has
  AllowSkip = true". Corrected: the value is retained (never renumbered) for persistence compatibility and is
  unreachable by any code path; the remaining mentions are defensive guards.
- **i18n** — the two now-unused keys removed from **both** dictionaries (`stg.flag.allowSkip`, `stg.badgeSkip`).

### 4. What Phase 5 assumed from Phase 4 — re-checked against the final implementation

| Assumption | Verdict |
|---|---|
| **Drawing number in the delivery statement** | **Works.** `GetDeliveryStatementQuery` selects `RequiresDrawingNumber`/`DrawingNumber` from the order's operations and returns every distinct run's number; `DeliveryStatementRowDto.DrawingNumbers`, the TS type and the printable "رقم الرسمة" column all match. |
| **Chargeable-to-customer marker on returns** | **Works and is enforced.** `CreateProductionOrderFromReturnCommand` requires `CostBearer`; `IssueRawMaterialCommandValidator` refuses a return-based issue with no bearer **and** refuses a bearer on an ordinary issue, so it cannot be set by accident either way; `ProductionOrder.LinkIssue` stores it; both `ProductionOrderDto` and `CustomerReturnOrderDto` expose it. Inclusion in billing stays explicit (`CostEntry.IsChargeableToCustomer` + `InvoiceBillingGuard`), which is Phase 6's rule, not a conflict. |
| **Separate statuses** | **Works.** `SeparateStatus` has all five states (`PendingReprocessing/Reprocessing/Reprocessed/Ready/Scrapped`), matching the TS union and the page; transitions are guarded (`StartReprocessing`, `MarkReprocessed`, `Scrap`), and the register row is created by the transfer. No conflict found. |

**One test added** so the statement↔floor contract is pinned rather than only read:
`TheStatement_CarriesEveryPrintRunsDrawingNumber_WithTheOperationsRecordedSeparatesAndSecondGrade`
(`Phase5ReadyGoodsDeliveryReturnsTests`) — a printing run with its own drawing number, plus recorded separates
and second grade, must reach the statement per unit. Suite went **22 → 23**.

### 5. Re-verification after every edit (nothing below is carried over from an earlier run)

| Check | Result |
|---|---|
| `dotnet build DyeHouseERP.sln -c Release --no-incremental` | **PASS — 0 errors, 5 warnings** |
| `dotnet test … UnitTests -c Release --no-build` | **PASS — 795 / 795** (+1) |
| `… --filter FullyQualifiedName~Phase4` / `~Phase5` | **10 / 10** · **23 / 23** |
| `dotnet ef migrations has-pending-model-changes` | **No changes…** (still no migration needed) |
| `node tools/permissions-parity-check.cjs` | **118 / 118, 0 failures** |
| `node tools/i18n-parity-check.cjs` | **ar 901 / en 901, 0 failures** (was 903; 2 dead keys removed) |
| `node tools/duplicate-sweep.js` | **PASS** — 88 DbSets = 88 |
| `node tools/wiring-sweep.js` | **PASS** — only login + public settings policy-free (unchanged) |
| `npx tsc -b` then `npx vite build` | **exit 0 / exit 0** (chunk-size warning only) |

**Still not run (environment, not code):** the SQL-Server integration suite (34 tests — no `DYEHOUSE_TEST_CONNECTION`)
and `npm run lint` (`eslint: not found`, F.B.4). Browser entry of the new stage-row fields was not opened here
(no browser tooling); the TypeScript build and the handler/statement tests are the evidence.

No commit, no push, and no migration was applied.

## Phase 7 — Purchasing, chemicals, operating supplies, HR & payroll — `Verified` (see `docs/audit/phase7-report.md`)
Verification-first: every briefed behaviour was already implemented and correct, so the phase added **test
coverage, not code**. Scope inventory confirmed supplier statement/balances, materials vs supplies with
distinct workflows and costs, config-driven HR/payroll, and granular permissions on every HR/payroll action.
The only negative-stock exception is the fabric-only `ApprovalRequestGuard` (separation of duties), so
factory-owned chemical stock has no self-service override — the required rule is met by that guard.
- **Added this session:** `tests/…/Phase7PurchasingMaterialsHrTests.cs` — **11 tests**: draft purchase posts
  zero / posting posts exactly one / re-post refused (supplier invoice) and a draft PO moves nothing;
  chemical over-consumption refused with nothing written; the exception needs a stored approval and refuses
  self-approval; operating-supply consumption cost comes from the posted ledger rows; a production chemical
  cannot be booked as an internal supply; changing the active attendance policy changes payroll with no code
  change; production execution shifts do not affect HR; and every HR/payroll action carries its own granular
  policy.
- **No production code needed a fix. No migration needed** (model unchanged).
- Results: build PASS (0 errors, 3 warnings), **806/806** unit tests (+11), frontend build PASS,
  permission parity 118/118, i18n 901/901, duplicate + wiring sweeps clean, model clean. Integration tests
  (no SQL Server) and frontend lint: **Not run**.

## Phase 8 — Reports, UI, exports & cleanup — `Verified` (see `docs/audit/phase8-report.md`)
Verify-first. One real defect fixed and two tooling items closed; 26 tests added.
- **F.8 FIXED (the phase's headline defect):** attachment upload had **no content-type/extension allowlist** -
  the bytes are served from the API origin, so HTML/SVG/JS would be executed by the browser (stored XSS).
  Added `AttachmentFileTypes` (extension + MIME allowlist, charset-stripped) and enforced it in
  `UploadAttachmentCommandValidator`.
- **F.9 FIXED:** `npm ci` could not run (lockfile out of sync); regenerated it - `npm ci --dry-run` exits 0.
- **F.B.4 FIXED:** added a real flat ESLint setup (`eslint.config.js` + deps); `eslint .` exits 0. The first
  run exposed 98 `no-explicit-any` errors, all fixed at the cause: a shared ambient `ApiErrorLike` type
  (94 callbacks across 30 files), a generic `PrintPreviewLayout<T>`, a memoised floor list, an unused `TABS`
  binding, and five negative-stock handlers that read `shortage` without optional chaining.
- **Verified without change:** reports are real-data and units are never mixed; the Excel item
  template/import (row validation, reported errors, idempotent retry, permission-gated update); the customer
  portal is absent from the API, the route table and the build; `/scan/:type/:id` resolves only known types
  and every scan type is behind its own view policy with unknown ids throwing `NotFoundException`.
- **Added this session:** `tests/…/Phase8ReportsExportsCleanupTests.cs` — **26 tests** (exports carry Arabic
  data + identity + RTL and move nothing; template round-trips; import integrity/idempotency; portal absence;
  scan authorization/invalid id; attachment allowlist/association/authorization).
- **No migration needed** (no model change).
- Results: solution build 0 errors (2 warnings), **832/832** unit tests (+26), `tsc -b` exit 0, `eslint .`
  exit 0, `npm run build` PASS, `npm ci --dry-run` exit 0, hooks-check PASS, module smoke 68/68, dev server up
  (preview host 200), permission parity 118/118, i18n 901/901, duplicate + wiring sweeps clean, model clean.
- **Not run:** the interactive browser UI pass - Chrome cannot launch (missing system libs, no root/sudo); and
  the SQL-Server integration suite (no database). PDF Arabic glyph shaping depends on a deployment font.

## Phase 9 — Final verification — `Verified` on all measurable lines; NOT declared complete (see `docs/audit/phase9-report.md`)
Verification only — **no file was changed by this phase**.
- Fresh runs: Release build **0 errors / 5 warnings**; unit tests **832/832**; `tsc -b` exit 0; `eslint .`
  exit 0; `npm run build` exit 0; `npm ci --dry-run` exit 0; hooks-check exit 0; module smoke **68/68**;
  permission parity **118/118**; i18n **901/901**; duplicate + wiring sweeps clean; `has-pending-model-changes`
  **clean**.
- Security sweep: **16 / 17 Pass**, 1 **Not Applicable** (no self-service password endpoint exists — admin-only
  change). JWT 8h + no refresh tokens, no fallback/placeholder secret, PBKDF2-SHA256 (100k), login throttling,
  per-request live re-check of active/permissions, self-elevation + last-admin guards, trusted proxies only,
  explicit CORS, Swagger Development-only, generic 500 envelope, attachment allowlist, protected exports,
  redacting audit.
- Acceptance checklist: **44 Pass, 1 Not Applicable, 0 Fail** across inventory/units, receiving/orders,
  production, reprocessing/costs, delivery/returns, documents, cleanup.
- Regression: no previously-green check regressed; **no file changed outside `src/`, `tests/`, `docs/`** (no
  README/sln/global.json/.gitignore/tools edits). HEAD still `abb4426`.
- **Not Run:** the 34 SQL-Server integration tests (no `DYEHOUSE_TEST_CONNECTION`) and the real-browser UI pass
  (Chrome cannot launch — missing system libraries, no root). PDF Arabic glyph shaping unverified (deployment
  font). One coverage gap: no dedicated "no double raw deduction" test.
- Open questions: self-service password change; admin-granting via `users.manage`/`roles.manage`; embed an
  Arabic PDF font; shared throttle store for multi-instance; commit `00-MAIN-PROMPT.md` (absent from the repo).

---

## Phase 6 — web-surface closure (this session) — `Verified`

`docs/audit/phase6-report.md` **did not exist** when this section was written (only `phase7/8/9` reported on
Phase 6), so the briefed UI items were checked directly against the source instead of a report. *(That report
was added later, in the `7c68042` audit pass — see the audit section below.)* Phase 6's **web surface** was
`جزئي` per `state-check.md §185`. Three gaps were found and closed; the backend needed only one field exposed.

| Briefed item | Evidence before | Change | Evidence after |
|---|---|---|---|
| **وحدة سطر الفاتورة** | Backend complete (`InvoiceLineDto.Unit`, `InvoiceLineInput.Unit`, `Add/Update/UpdateLine` commands + `InvoiceBillingGuard`); the client dropped it — `InvoiceLine` had no `unit`, and `create`/`addLine`/`updateLine` bodies omitted it, so every line silently defaulted to KG | `client.ts` now types `unit` on `InvoiceLine` and on all three line bodies; `InvoicesPage.tsx` gained a unit selector on the create row, the draft-editor row and the add-line row | `tsc -b` exit 0 |
| **علامة "يُحمَّل على العميل"** | `CostEntry.IsChargeableToCustomer` + `CreateCostEntryCommand` supported it, but `AddCostEntryRequest` dropped it and no `Web/src` file referenced a chargeable flag | `AddCostEntryRequest(..., bool IsChargeableToCustomer = false)` passed into the command; `CostAccountingApi.addEntry` types it; the `CostPanel` form has an off-by-default checkbox | `dotnet build` 0 errors; `tsc -b` exit 0 |
| **إجراء تظهير الشيك لمورد** (`settle-supplier`) | `ChecksController.SettleSupplier` + 6 passing tests existed; neither `client.ts` nor `ChecksPage.tsx` called it (only `endorse`) | `ChecksApi.settleSupplier(...)` added; `ChecksPage` gained a `settle` action (settlement-date input + button) for Received/InHand/Bounced and a button for an already-Endorsed check, reusing the supplier selection | `tsc -b` exit 0; endpoint unchanged (already tested) |
| **مفاتيح i18n الخاصة بيهم** | `checks.action.settleSupplier`, `checks.settlementDate`, `checks.settleNote`, `cost.chargeableToCustomer`, `cost.chargeableNote` were absent | Added to **both** dictionaries | i18n parity **906/906**, 0 failures (was 901) |

**Checks run after the edits (real output):** Release build **0 errors / 5 warnings** (same 5 as baseline); unit
tests **832/832**; `npm run build` (`tsc -b && vite build`) **exit 0**; `npm run lint` **exit 0** (0 errors, 9
`react-refresh` advisories, unchanged); `node tools/i18n-parity-check.cjs` **906/906**; `node
tools/permissions-parity-check.cjs` **118/118**.

**Not Run:** the interactive browser pass — Chrome cannot launch here, so the new selector/checkbox/button are
verified by the TypeScript build and source, not by clicking. No migration, no commit.

## Coverage gap closed — "no double raw deduction on reprocessing" (this session) — `Verified`

Phase 9 listed exactly one coverage gap: *"no dedicated 'no double raw deduction' test"*. It is now closed.

**Test added:** `ReprocessingASeparate_PostsNoSecondRawDeduction_AndLeavesTheOriginalIssueIntact`
(`tests/DyeHouseERP.UnitTests/Phase4ProductionFloorTests.cs`, section 6). It is a **direct** test, not an
indirect one:

1. A REAL raw issue (`IssueRawMaterialCommand`) creates the original order — one OUT ledger row of 1000 kg
   against the customer's lot. The test first asserts that row exists (so the scene is non-vacuous).
2. The formation operation is closed recording 15 kg of separates, so a `Separate` enters the register.
3. **Before/after comparison of the ledger itself:** the original order's OUT rows (id, quantities, source
   document) are snapshotted, the whole ledger row count is snapshotted, the separate is reprocessed, and
   then: the ledger gained **no** row at all; the original order's OUT rows are `BeEquivalentTo` the snapshot
   row-for-row; the reprocessing order owns **zero** ledger rows; its claim is the separate's own 15 kg (never
   the original 1000); and the lot balance is exactly `100000 - 1000` — i.e. the original issue is the only
   deduction that ever touched the lot.

The assertion is non-vacuous: any ledger write during reprocessing fails the row-count equality, and any
re-deduction of the original fails the `BeEquivalentTo`.

**Checks run after the edit (real output):** Release build **0 errors / 3 warnings** (incremental; full run
0 errors / 5 warnings); filtered run of the new test **1/1 passed**; full unit suite **833/833** (was 832,
+1); `npm run build` **exit 0**; `npm run lint` **exit 0** (0 errors, 9 advisories — no frontend file changed
by this item). No migration, no commit.

**Not Run:** the SQL-Server integration suite (no `DYEHOUSE_TEST_CONNECTION`) — the ledger comparison runs on
the InMemory provider, which executes the same application code but not the database constraints.

## PDF Arabic text — embedded open-licence font (this session) — `Verified`

Phases 4/8/9 all carried *"PDF Arabic glyph shaping unverified — depends on a deployment font"*. It is now
implemented in-code and verified on the produced bytes.

**What was added:**

- **Font file in the repo:** `src/DyeHouseERP.Infrastructure/Fonts/NotoNaskhArabic-Regular.ttf` (TrueType,
  family `Noto Naskh Arabic`, downloaded from `notofonts.github.io`), with its licence
  `NotoNaskhArabic-OFL.txt` — **SIL Open Font License 1.1** (from `google/fonts` `ofl/notonaskharabic`). Both
  are declared `<EmbeddedResource>` in `DyeHouseERP.Infrastructure.csproj`, so the font travels inside the
  assembly and the licence travels with the binary.
- **Registered in QuestPDF:** `ReportExportService`'s static constructor registers the embedded font via
  `QuestPDF.Drawing.FontManager.RegisterFont(stream)` (once per process, can't be skipped before a PDF is
  generated), and `page.DefaultTextStyle(... .FontFamily("Noto Naskh Arabic"))` makes every PDF use it. The
  old inline "deployment font needed" warning comment is gone.
- **README** operational note corrected (it previously told operators to register a font at startup).

**Verification — the produced PDF really contains Arabic text (both approaches the brief allowed):**

1. **Embedded fonts inspected** (raw bytes): `/FontFile2` is present (a TrueType **font program** is embedded,
   not merely referenced) and `/BaseFont /BAAAAA+NotoNaskhArabic-Regular` — the registered Arabic font, subset
   and embedded. This is pinned permanently by the new test.
2. **Text extracted** (one-off, out of repo, `pypdf` via `pip --target`): extracting the generated PDF yields
   real Arabic code points — the title `كشف حساب العميل`, the company name and the item value all come back as
   Arabic characters, and the `/ToUnicode` CMaps resolve to U+0600–U+06FF / Arabic presentation forms.

**Test added (committed):** `APdfExport_EmbedsTheArabicFont_AndCarriesRealArabicText`
(`Phase8ReportsExportsCleanupTests.cs`) — asserts `/FontFile2`, the embedded `NotoNaskhArabic` name, and that a
`/ToUnicode` CMap resolves to an Arabic code point (it decodes the flate-compressed CMap streams with
`ZLibStream`, so the check needs no extra package). It is non-vacuous: before this change the service
registered no font, so neither the Arabic font name nor an Arabic `/ToUnicode` mapping could appear.

**Checks run after the edits (real output):** Release build **0 errors / 5 warnings** (full solution, after the
`.csproj` change); unit tests **834/834** (was 833, +1) with the two PDF tests passing; `npm run build`
**exit 0**; `npm run lint` **exit 0** (0 errors, 9 advisories — no frontend file changed by this item);
`dotnet ef migrations has-pending-model-changes` → **"No changes have been made to the model"** (no migration
needed — the change is a resource, not a model change); i18n **906/906**; permissions parity **118/118**.

**Not Run / limitations:** the visual in-browser rendering of the PDF was not opened (no browser tooling);
proof of Arabic is the embedded-font + `/ToUnicode` inspection above, not a screenshot. The 500 KB+ font adds
~250 KB to the Infrastructure assembly. Excel export was already RTL and is unaffected.

## Audit pass on `7c68042` — 3 findings confirmed & fixed, 1 documentation finding closed

Branch `fix/audit-findings` (created from `main` @ `7c68042` **without** touching `main`).
**No commit, no push, no force push, no branch deletion, and no migration applied.**

Three of the four audited findings were **confirmed against the code** and fixed; finding 4 was documentation
only. Each fixed finding has regression tests that were **run and observed to fail before the fix**, then
re-run green afterwards.

| Finding | Verdict | Cause (file) | Fix | Regression tests (before → after) |
|---|---|---|---|---|
| **1 — permission escalation (critical)** | **Confirmed** | `Application/Users/Commands/CreateUserCommand.cs`, `UpdateUserCommand.cs` (+ `DeactivateUserCommand.cs`, `ChangePasswordCommand.cs`) never checked the caller's own rights: a `users.manage`/`roles.manage` holder could grant `admin` (or any right they lacked), and could edit/disable/reset an administrator account | New `Application/Common/Services/UserAdministrationGuard.cs` enforces, server-side: you may only grant permissions you hold; only an administrator may grant/revoke `admin`; only an administrator may modify an administrator account. Existing self-lockout and last-active-admin rules unchanged | `UserPermissionEscalationTests` (**7 failed / 5 passed → 12 / 12 passed**) |
| **2 — sub-customer permit double deduction (high)** | **Confirmed** | `Deliveries/Commands/DeliveryWorkflowCommands.cs` posted an OUT for **every** permit, including a sub-customer one, so confirming it deducted the same ready lot a second time; `CreateDeliveryCommand.cs` also refused a line that named the (correct) primary customer's Job Order; the correction path could draw stock out for a sub-permit too | A sub-customer permit now posts no movement in `MarkDeliveryDeliveredCommandHandler` (document state only) and none in `EditDeliveredDeliveryCommandHandler`; `CreateDeliveryCommand` validates a sub-customer permit's line against **its own customer OR the linked primary permit's customer**, and still refuses any third customer | `SubCustomerPermitDeliveryTests` (**5 failed / 4 passed → 9 / 9 passed**) |
| **3 — selectable starting stage (high)** | **Confirmed** | `ProductionOrder.BuildStageRoute` always started at the formation stage, so reprocessing (`ReprocessSeparateCommand`) and return-raised orders could not begin at the operation the rework is actually at | `BuildStageRoute(…, Guid? startingStageId)` + optional `StartingStageId` through `ReprocessSeparateCommand`, `IssueRawMaterialCommand`, `CreateProductionOrderFromReturnCommand`, both controllers (`api/separates/{id}/reprocess`, `api/customer-returns/{id}/production-order`) and both pages. Validated against the **active** stages (unknown/deactivated refused); exactly one stage is still activated and the route stays dynamic; no new stock movement (the raw issue remains the single deduction) | `SelectableStartingStageTests` (**6 failed / 4 passed → 10 / 10 passed**). *One sub-case is reported as blocked, not invented: a starting stage that `RequiresDrawingNumber` is refused by the existing domain rule — where the drawing number would come from at reprocess time is an open business decision* |
| **4 — audit documentation consistency** | **Confirmed (docs)** | `docs/audit/phase9-report.md` read as if it were the current status, and `docs/audit/phase6-report.md` did not exist | Phase 9 is now explicitly labelled a **historical** snapshot at `abb4426`, with a **Corrections** section for the three lines this audit disproved and a pointer to this log; `phase6-report.md` added with the Phase 6 scope, file/test evidence, the created-but-unapplied migration and the checks actually run | n/a (documentation) |

**Audit base HEAD:** `7c68042` (`main`). `git ls-remote origin refs/heads/main` = `7c680424392afb3b99f4027132bb9d2d37e55f3d`.

**Checks run in this pass (real output):** Release build **exit 0 — 0 errors / 5 warnings**; unit suite
**865 / 865 passed** (was 834, **+31** new tests); `npm run build` **exit 0**; `npm run lint` **exit 0**
(0 errors, 9 pre-existing advisories); i18n parity **909 / 909**; permission parity **118 / 118**;
`hooks-check` clean; duplicate sweep **88 = 88**; wiring sweep unchanged (login + public settings only);
`dotnet ef migrations has-pending-model-changes` → **"No changes have been made to the model since the last
migration."** (no migration is needed by any fix — all three are logic/API changes).

**Not Run (unchanged environment limits):** the SQL Server integration suite (`DYEHOUSE_TEST_CONNECTION`
unset; `DyeHouseERP.IntegrationTests`) and any interactive browser pass — so the DB-level halves of these fixes
(unique filtered indexes, `sp_getapplock` serialization, end-to-end HTTP authorization) are unverified here.

**Not done by design:** no migration applied, no unrelated refactor, no change to the established business
rules (ledger as source of truth, no duplicate raw deduction, independent KG/Meter units, dynamic route,
existing permissions/audit trails).

## Next
- Phase 4 — `Verified` (see `phase4-report.md`). The two Phase 4/5 items it could not close are environmental:
  the DB-backed integration suite (no SQL Server) and frontend lint.
- Phase 6 (Finance, invoicing, treasury & cheques) — **web surface CLOSED this session** (see the Phase 6
  section above): invoice billing unit, the "chargeable to customer" flag, the endorsed-cheque
  `settle-supplier` action and their i18n keys are now wired into `client.ts` + the pages.
- Phase 7 — `Verified` (see `phase7-report.md`). Purchasing/chemicals/supplies/HR & payroll were already
  implemented; the phase added 11 tests, no code and no migration. `Phase7PurchasingMaterialsHrTests.cs` is
  the only new file.
- **All phases 1–9 are now worked.** Phase 9 (`phase9-report.md`) is the final verification: everything
  measurable passes, but the project is **not declared complete** — the SQL-Server integration suite and the
  real-browser UI pass are Not Run (environment) and self-service password change is Not Applicable (no
  feature).
- **Three follow-ups closed in this session (see the sections above):** the Phase 6 web surface; the single
  Phase 9 coverage gap (a direct "no double raw deduction on reprocessing" ledger test); and the PDF Arabic
  font (embedded in-code, no longer a deployment dependency). Nothing is committed or pushed, and no migration
  is applied.

## Continuation pass on `fix/audit-findings` — findings 1–3 re-opened and closed (this session)

Reviewed the actual code and tests rather than trusting the earlier report. No commit, no push, no force
push, no branch deletion, **no migration applied**, and `main` untouched. This pass adds **8** unit tests
(865 → **873**).

| Item | What was actually still open | What was done | Tests |
|---|---|---|---|
| **1 — permissions (UI half)** | `UserAdministrationGuard` was already enforced server-side in all four user commands, but `UsersPage` still offered every permission to every delegated administrator, and offered edit / deactivate / reset controls even on an **administrator** account | `permissions.ts` gained `readSignedInActor()` (mirrors the guard: you may only grant what you hold, only an admin may grant `admin`, only an admin may touch an admin account). `UsersPage` filters the picker to the permissions the caller may grant, filters the account list and hides the account controls for admin accounts, and strips any non-grantable value from the request body. The server is unchanged and still the enforcement point - hiding a box is a convenience, never the control | No UI test harness exists; verified by `tsc -b` + `vite build` (exit 0) and `eslint .` (0 errors). `UserPermissionEscalationTests` 12/12 unchanged |
| **2 — sub-customer permit** | **Confirmed defect:** a sub-permit line carrying an **original-raw** figure was summed into the order's "already settled" total in three places (`CreateDeliveryCommand`, `EditDeliveredDeliveryCommand`, `GetDeliveriesQueryHandler.ReadRemainingOriginalRawAsync`) even though a sub-permit moves no stock - so a document-only sub-permit shrank the primary permit's real remaining allowance. Also confirmed: `POST /api/deliveries/{id}/lines/corrections` silently dropped the manually-entered original-raw figures; a correction changing ONLY the original-raw figure was skipped by the quantity-delta guard; and a sub-permit could promise more than the primary permit delivered | Sub-permit lines are excluded from "settled" in all three places; the order-level original-raw check is skipped for a sub-permit (its figures are a share of the primary's, never a second settlement); new `EnsureSubPermitWithinPrimaryAsync` bounds a sub-permit cumulatively by the linked primary permit's own lines; the correction endpoint now carries the two original-raw fields into `EditDeliveredDeliveryCommand`; an original-raw-only correction is now recorded and audited (no stock movement) | `SubCustomerPermitDeliveryTests` **9 → 16** (+7): primary-alone completion, fully manual line entry, nonexistent primary, wrong-parent link, quantity-limit, the original-raw double-count proof, and an original-raw correction. The double-count test is **non-vacuous**: with the exclusion reverted it reads `0` instead of `200` |
| **3 — starting stage / drawing number** | The selectable starting stage was already complete and tested. The one open point was the drawing number a Printing stage demands | **Business decision, not invented:** the number IS stored and linked - on the **original** order's own printing execution - but the project rule is that each print run names its OWN number and never borrows another order's (`TransferToNextStageCommand`, `ProductionOrderStageExecution.SetDrawingNumber`). Auto-carrying it into the reprocess/return order would break that rule, and making it optional would weaken the mandatory-drawing rule. The starting stage that requires a drawing number is therefore still refused, loudly, and the decision is left to the owner | `SelectableStartingStageTests` **10 → 11** (+1: the original order's drawing number is stored and is NOT borrowed by the new order) |

**Manual entry (finding 2b), verified rather than re-implemented:** a sub-customer permit's details are
entered by hand through the same `CreateDeliveryCommand` / `DeliveryLineInput` as any delivery line (colour,
KG, Meter, piece count, raw origin, original-raw figures). Nothing on either page copies lines from the
primary permit, and the ownership link (primary permit + stored parent customer) is validated server-side.

**Checks run in this pass (real output, exit statuses preserved):**

| Check | Result |
|---|---|
| `dotnet build DyeHouseERP.sln -c Release` | **exit 0 - 0 errors, 5 warnings** (the same pre-existing 5) |
| `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **Passed - 873 / 873** (was 865, +8) |
| `npm run build` (`tsc -b && vite build`) | **exit 0** |
| `npm run lint` (`eslint .`) | **exit 0** - 0 errors, 9 pre-existing advisories |
| `node tools/i18n-parity-check.cjs` | **ar 909 / en 909, 0 failures** |
| `node tools/permissions-parity-check.cjs` | **118 / 118, 0 failures** |
| `node tools/hooks-check.js` | clean - no invalid hook call sites |
| `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` | **"No changes have been made to the model since the last migration."** (no migration needed - every change is logic/API/UI) |
| `git diff --check` | clean (exit 0) |

**Not Run (environment, unchanged):** the SQL Server integration suite (no `DYEHOUSE_TEST_CONNECTION`) and any
interactive browser pass (no browser tooling here). The Debug configuration of the solution build also hit a
host **out-of-memory** in Roslyn on the Persistence project; the Release build is clean and the model check was
run against it, so this is a resource limit, not a code result.

**Remaining limitation (unchanged, deliberate):** the API still answers the new authorization refusals with the
middleware's generic 403 body, so a delegated user who bypasses the screen sees "You do not have permission to
perform this action." rather than the guard's specific message. Fixing that would change the error envelope for
every `UnauthorizedAccessException` in the app, which is outside this finding's scope; recorded as an option.

**Follow-up found while wiring the manual entry:** the "ready stock" line picker on the deliveries screen was
filtered to the permit's own customer, so the exact scenario the brief requires - a sub-permit naming the
PRIMARY customer's Job Order - could not be selected from the screen even though the API accepts it. While a
primary permit is chosen the picker now lists every ready lot (each labelled with its customer); the server
still refuses any line that is not the sub-customer's own or the linked primary customer's. Re-verified after
the change: `npm run build` exit 0, `npm run lint` exit 0 (0 errors, 9 pre-existing advisories). No `.cs` file
changed after the 873/873 run, so the backend results above still hold.

## Focused pass — multiple sub-customer permits + independent numbering (this session)

Scope limited to two points: (1) confirm that several sub-customer permits can hang off ONE primary
permit and that quantities/deductions stay correct, and (2) make the sub-permit number sequence
independent of the primary permit sequence. Inspection first, then the smallest change that closes a
proven gap. Branch `fix/audit-findings`, no commit, no push, **no migration applied**, `main` untouched.

### Inspection result (before any edit)

| Question | Verified behaviour | Evidence |
|---|---|---|
| Does the system already allow more than one sub-permit per primary? | **Yes.** Nothing caps the count. A sub-permit is defined purely by `Delivery.PrimaryDeliveryId`, and the create path validates the primary, the stored parent-customer link, and the ownership of every line - then the cumulative quantity bound below. | `Delivery.cs:29/40`; `CreateDeliveryCommand.cs` `ResolvePrimaryPermitAsync` + `EnsureSubPermitWithinPrimaryAsync:189` (sibling sub-permits are summed per (Job Order, Item)) |
| Can a sub-permit be created with no primary? | **No** - an "independent sub-permit" is unrepresentable: without `PrimaryDeliveryId` the document is simply an ordinary permit, and a supplied-but-unknown id raises `NotFoundException`. | `ResolvePrimaryPermitAsync` |
| Quantity over-run across several subs? | **Prevented**, cumulatively: a new sub-permit may only claim what the primary delivered minus what every non-cancelled sibling already claimed, per unit. | `EnsureSubPermitWithinPrimaryAsync:189-211` |
| Duplicate stock deduction? | **Not possible**: a sub-permit posts no movement on create, on confirm (`DeliveryWorkflowCommands.cs:121`) and on correction (`EditDeliveredDeliveryCommand.cs:119`); only the primary permit writes the OUT rows. | those lines + `Delivery.IsSubCustomerPermit` |
| Unique number per document? | **Enforced in EF Core**, not by convention: unique index on `Deliveries.DeliveryNumber`, plus a unique *filtered* index on `DeliverRequestId` that makes a retried confirmation idempotent. | `DeliveryConfiguration.cs:14-15, 34-36` |
| Numbering engine | `SqlDocumentNumberGenerator`: `sp_getapplock` keyed by `(DocumentType, Year, WarehouseId)` + a counter row - never MAX+1. Sequence parameters are data (`DocumentSequenceDefinition`), seeded per type. | `SqlDocumentNumberGenerator.cs`, `DocumentSequence.cs`, `DocumentSequenceSeeder.cs` |

### Gap found and fixed

**Only one real defect existed - the numbering sequence.** `CreateDeliveryCommand` minted EVERY permit's
number from `DocumentType.Delivery`, so a sub-permit consumed the primary `DEL` counter (and two sub-permits
of one primary shared that same sequence). Multiple sub-permits, the cumulative quantity bound, the
independent KG/Meter comparison and the no-double-deduction behaviour were already correct.

- `SharedEnums.cs:100` - new `DocumentType.SubDelivery = 29`, used ONLY to key the numbering engine (a
  sub-permit writes no ledger/treasury row, so it never appears as a source document type).
- `DocumentSequenceSeeder.cs:27` - seeds `(SubDelivery, "SUB", 6, yearly, not warehouse-scoped)`, i.e. the
  same configurable shape as `DEL`. **No migration is needed**: `DocumentType` is stored as a *string* in
  both sequence tables (`DocumentSequenceConfiguration` `HasConversion<string>()`), so this is a seed row,
  not a schema change - confirmed by `has-pending-model-changes` → "No changes…".
- `CreateDeliveryCommand.cs:121-122` - the number is taken from `SubDelivery` when the permit is issued
  against a primary one, and from `Delivery` otherwise. Same engine, different sequence key, so the
  concurrency guarantee (`sp_getapplock`) is identical.

### Tests added (`SubCustomerPermitDeliveryTests`, 16 → 22)

Five handler-level tests:

* Three sub-permits (400 + 350 + 250 Metre) against one 1000-Metre primary all succeed and a fourth is
  refused; metre-only sub-permits never invent a KG figure; KG and Metre allowances are bounded independently.
* A sub-permit draws from `SubDelivery` while the primary draws from `Delivery`, and two sub-permits never
  share a number. **Non-vacuous**: with the old `DocumentType.Delivery` restored it fails with
  `{Delivery, Delivery, Delivery}`.
* The seeder produces an independent `SUB` definition (`SUB-2026-000041`).

One **model-level** test (`Model_PinsPermitUniquenessAndTheIndependentSubSequence`), because a handler test
sees only a stub generator and an in-memory provider that ignores unique indexes and locks. It pins, from
the EF model: the unique index on `Deliveries.DeliveryNumber`; the *filtered* unique index on
`DeliverRequestId` (idempotent retry, NULLs preserved); the optional, indexed, `Restrict` FK on
`PrimaryDeliveryId`; the unique counter key `(DocumentType, Year, WarehouseId)` - the key the generator
locks - which is why the `DEL` and `SUB` sequences can never share a counter; and that `DocumentType` is
persisted as **text** in both sequence tables, which is the concrete reason this change needs no migration.
It earned its keep immediately: it failed first because `IEntityType.Name` is the full CLR name, and the
assertion - not the production code - was corrected.

**Checks (real output):** Release build **0 errors** · unit suite **879 / 879** (was 873, +6) ·
`has-pending-model-changes` → **"No changes…"** (no migration) · i18n **909/909** · permissions **118/118** ·
`git diff --check` clean.

**Concurrency, precisely:** the guarantee lives in `sp_getapplock` on `(DocumentType, Year, WarehouseId)`
plus that unique counter key. The unit suite cannot exercise it - every handler test injects
`FixedNumberGenerator`, and the in-memory provider ignores both locks and unique indexes. So it is verified
by construction (generator SQL + pinned model metadata), **not** by a passing concurrent test; the
SQL Server integration suite requires `DYEHOUSE_TEST_CONNECTION` and was not run.

### Left untouched (deliberate)

`ItemUnitRules` still requires BOTH measurements on every line of an item flagged **"require both units"**,
for primary and sub-permits alike. A metre-only sub-permit therefore needs an item that uses both units
without *requiring* both (the case the brief's example describes); exempting document-only sub-permits from
that per-item setting is a unit-rule change, not confirmed by the brief, so it is reported, not made.

**Not Run:** the SQL Server integration suite (no `DYEHOUSE_TEST_CONNECTION`) - so the `sp_getapplock`
serialization and the unique indexes are verified by construction (migration SQL + EF configuration), not by
a live database.

---

## Pass 3 - sub-permit recipient, item/colour source and printable column choice (spec section 31)

**Supersedes** the "Left untouched (deliberate)" note above: the brief now explicitly authorises the
`RequireBothUnits` exemption for sub-customer permits, so it is implemented (below) instead of parked.

### Findings before the change (code, with lines)

| Question | Evidence | Verdict |
| --- | --- | --- |
| Is the recipient chosen by hand? | `DeliveriesPage.tsx` recipient `Select` bound to `customerId`; `CreateDeliveryCommand.CustomerId` | **Already manual** - it was never inherited from the primary permit. The picker was not narrowed to the linked customer's sub-customers, so an invalid choice only failed server-side. |
| Is the item taken from the primary permit? | `DeliveriesPage.tsx` (old) took `itemId` from the ready-goods balance row; `CreateDeliveryCommandHandler` never checked it against `PrimaryDeliveryId` | **Defect** - a sub-permit line could name an item the primary permit never carried. Item and colour were free input. |
| Is the colour taken from the primary permit? | Old line 2 of `SubCustomerPermitDeliveryTests.SubCustomerPermit_CarriesTheFullyManuallyEnteredLineData` asserted `Color = "Green"` against a primary that recorded `"Red"` | **Defect** - the colour was invented by the sub-permit; one colour could become two spellings in the data. |
| Roll count (`PieceCount`) | `DeliveryLine.PieceCount` + `DeliveryLineInput.PieceCount` existed, but the create form had no field and `createMutation` never sent it | **Gap** - the manual roll count could not be entered at all. |
| Are KG / Metre / rolls forced together? | `AddLine` requires KG and/or Metre; `Validator` requires the same; the two are independent | **Correct** - nothing forced all three, and no conversion existed. |
| Does `RequireBothUnits` block a sub-permit? | `ItemUnitGuard` applied `item.RequireBothUnits` to every line | **Defect for a document-only slice** - a sub-permit handing over one unit was refused for an item flagged "both". |
| Print columns | `DeliveryPrintPage.tsx` had three fixed quantity columns | **Gap** - no way to print a metre-only document without an empty KG column. |
| Several sub-permits, independent `SUB` numbering, no extra stock movement | previous pass, unchanged | **Still correct** - re-verified by the suite below. |

### Changes made

**Backend**

* `CreateDeliveryCommand.cs:104,283` - `ResolveSubPermitLineSourcesAsync` resolves every sub-permit line's
  item and colour from the primary permit **before** anything is created: the item must be one the primary
  permit carried for that Job Order, the colour must be one it recorded for that item and is stored in the
  primary's own spelling (`"red"` → `"Red"`), and a line naming a Job Order the primary never delivered
  (the sub-customer's own job) must carry that Job Order's own item. An ordinary permit is untouched.
* `CreateDeliveryCommand.cs:128` - `enforceRequireBothUnits: !request.PrimaryDeliveryId.HasValue`.
* `ItemUnitRules.cs:61,73,96` and `ItemUnitGuard.cs:35,54` - the exemption parameter (default `true`, so
  every movement-recording path is unchanged). Unit selection is still enforced and nothing is converted.

**Frontend**

* `DeliveriesPage.tsx:77-186,275-385` - the recipient picker narrows to the primary customer's
  sub-customers and clears an invalid recipient; the line's source is the primary permit's own lines
  (its items and colours) with the sub-customer's own lots kept in a second group; a colour chooser appears
  when the primary recorded more than one; **KG, Metre and roll count are three separate manual inputs**;
  and each primary line shows what is already distributed to other sub-permits and what remains - per unit,
  never combined (e.g. `400 + 350 + 250 = 1000 م`).
* `DeliveryPrintPage.tsx` - a print column chooser for KG / Metre / rolls. Print-only (no recorded value
  is touched), the identifying columns always print, the choice is remembered per document format in
  `localStorage` (the same mechanism as the language/theme switches) and a sub-customer permit keeps its own
  key, so its format is independent of an ordinary permit's. The sub-permit print also names the primary
  permit and its customer.
* `deliveryPrintColumns.ts` (new) - that choice in **one** place, because two surfaces must agree on it:
  the print preview and the PDF. The single source of truth is why the key can never drift between them.
* `DeliveriesController.cs` (`GetDeliveryPdf`) + `api/exports.ts` - the **PDF** honours the same choice
  (`?includeKg/includeMeter/includeRolls`, default all), so the downloaded permit shows the columns the
  user chose instead of a fixed set. Only the choice of columns travels: no quantity is edited, converted
  or dropped from the data, and the identifying columns always print. `PrintingWorkflowTests` was updated
  for the new signature (it now passes the export service by name and keeps the default of all columns).
* `i18n/index.tsx` - 18 new keys in **both** dictionaries.

### Tests added (`SubCustomerPermitDeliveryTests`, 22 → 27)

Item the primary never carried → refused · colour the primary never recorded → refused (and the refusal
names the colours it does carry) · `"red"` is stored as the primary's `"Red"` · the sub-customer's own Job
Order with someone else's item → refused · a sub-permit line may be **metre-only or KG-only** even when the
item requires both units, while the same item on an ordinary permit still fails with `requires BOTH` (the
rule was narrowed, not removed). The one existing test whose rule changed (the arbitrary colour) was
corrected, not weakened.

One controller-level suite (`DeliveryPermitPdfColumnsTests`, 5 tests) for the PDF half of the column
choice, because that is the half reachable without a browser: it drives the real `GetDeliveryPdf` action
and captures exactly what it hands the renderer. It pins that a hidden column is absent from the header
**and** from every data row (so the table can never shift out of alignment), that all **eight** combinations
stay well-formed with each quantity under its own column and its own value, that hiding a column changes no
other printed figure, and - with the production `ReportExportService` - that the narrowed metre-only table
renders as real bytes (`%PDF-`, correct file name).

### Checks (real output)

Release build **0 errors** (5 pre-existing warnings, all in test files not touched) · unit suite
**889 / 889** (was 879, +10) · `has-pending-model-changes` → **"No changes have been made to the model…"**
(**no migration** added and none applied) · i18n **927 / 927** · permissions **118 / 118** · duplicate
sweep clean · `wiring-sweep.js` output unchanged (login + public settings only) · frontend `tsc -b &&
vite build` **PASS** · `eslint` **0 errors** (9 pre-existing warnings) · `git diff --check` **clean**.

Note on the run itself: `dotnet test` **with** an inline build aborted twice with
`Failed to launch testhost … System.OutOfMemoryException` on this shared VM (6.5 GB free, 119 processes -
a resource ceiling, not a test failure). Building first and running with `--no-build` passes every time; all
numbers above come from that form.

### Observed but deliberately NOT changed

`ItemUnitRules.EnsureValidQuantities` enforces only the **secondary** unit when `RequireBothUnits` is on
(`ItemUnitRules.cs:96-105`: `secondaryProvided`), so a quantity in the secondary unit alone passes while the
message says both must be supplied. That asymmetry is pre-existing and unrelated to the two points in this
brief; changing it would alter unit rules for the whole system, so it is **recorded here, not fixed**. The
test above therefore proves the exemption with the **KG-only** line (the case the exemption actually
unblocks) and the ordinary-permit refusal with the **KG-only** line too.

### Not run

The SQL Server integration suite (no `DYEHOUSE_TEST_CONNECTION`) · the Puppeteer UI smoke tools
(`tools/printing-ui-check.cjs`, `ui-smoke*.js`) - they require puppeteer at `/tmp/uitest` and a browser,
neither of which exists in this workspace, and no browser automation is available here. The frontend
evidence is the type-check, the production build and the lint run; **no browser/print interaction was
verified**.

---

## Pass 4 - RequireBothUnits means BOTH units, and the permit print columns verified in a browser

### Root cause (one expression)

`ItemUnitRules.EnsureValidQuantities` (Domain) is the single gate every quantity write goes through -
`ItemUnitGuard` wraps it and every posting handler calls that. Its `RequireBothUnits` branch computed
**`secondaryProvided` and nothing else**:

```csharp
var secondaryProvided = secondaryUnit.Value == UnitOfMeasure.KG ? kgProvided : meterProvided;
if (!secondaryProvided) throw ...
```

So the setting was enforced as *"the secondary unit is mandatory"* rather than the rule the flag, the DTO
("every movement for this item must record BOTH units rather than one") and the refusal's own wording
("requires BOTH of its units: a KG quantity AND a Meter quantity must be supplied") all describe. Because
`requireAnyQuantity` had already guaranteed at least one figure, the practical effect was: **KG-only was
refused while Meter-only - also missing one of the two units - was accepted and posted.** The same document
passed or failed depending on which unit the operator happened to type.

### The fix

The branch now resolves **both** sides (`baseProvided` and `secondaryProvided`) and refuses when either is
missing, naming the missing unit(s) in the message. Nothing else changed: unit selection, the no-conversion
guarantee, the `requireAnyQuantity` precedence and the sub-customer permit exemption
(`enforceRequireBothUnits: false`, a document that posts no movement of its own) are all untouched, and the
message still contains `requires BOTH of its units` so existing assertions keep their meaning.

### Affected operations reviewed

All 26 call sites of `ItemUnitGuard`/`ItemUnitRules` were read. They divide into two groups and the fix
applies identically to both because the parameter it depends on has one default (`true`):

* **19 handlers that post a movement or record a production figure** - raw receipt, inspection rejections,
  Job Order requested quantity, raw allocation, raw issue, pause release, resume re-issue, stage transfer
  (output / input / separates / second grade), external release, ready-goods transfer, ready-goods-to-raw,
  stock adjustment, customer transfer, customer return, delivery, reprocess-separate, plus their validators.
  For these the old behaviour was already refusing the base-only case, so the change adds exactly one new
  refusal: a secondary-only quantity.
* **1 deliberate exemption** - `CreateDeliveryCommand` passes `enforceRequireBothUnits: false` for a
  sub-customer permit (it is a slice of a permit that already recorded both units), which stays as is.
* The `requireAnyQuantity: false` paths (pause release, separates, actual input, second grade) still allow an
  empty figure; when the item demands both units they now require both in both directions, consistently.

No other place enforces this rule - there is no duplicate check in a validator, an import reader or the UI.

### Tests

`ItemUnitConfigurationTests` +4: the missing-base direction on the pure rule **in both unit orderings**
(`Missing: KG` / `Missing: Meter`), both-supplied passes in either ordering, the empty case keeps the
pre-existing "quantity is required" precedence (and names both units when `requireAnyQuantity: false`), an
item with no secondary unit is never asked for one, and a **handler-level** raw receipt that types only the
Meter for a both-units item is refused and posts nothing.
`SubCustomerPermitDeliveryTests` extended: on an ordinary permit **both** single-unit directions are now
refused (the metre-only one being the case that used to slip through) while the sub-permit still accepts
both single-unit lines - the exemption is proven, not assumed.

**Non-vacuous:** restoring the old one-sided check makes exactly these four tests fail
(`RequireBothUnits_MissingTheBaseUnit_IsRejected`, `RequireBothUnits_IsSatisfiedOnlyBySupplyingBothUnits`,
`RawReceipt_RequiresBothUnits_EvenWhenOnlyTheSecondaryUnitWasTyped`,
`SubCustomerPermit_MayCarryMetreAlone_EvenWhenTheItemRequiresBothUnits`) and nothing else.

### Browser verification of the permit print columns (run for real this time)

`tools/delivery-print-columns-check.cjs` (new) drives the real page in headless Chrome against the vite dev
server with the API stubbed by request interception (there is still no SQL Server). It **passed with
`failures: 0`**: all eight column combinations print exactly the chosen columns; every row keeps one cell per
header; a hidden column is absent rather than zero-filled while a visible one keeps its recorded figure;
drawing number, job order and item print in every format including "no quantity column at all"; the choice
persists for the sub-customer permit and never writes the ordinary permit's key; the register's PDF download
is requested with the same three flags (`includeKg=false&includeMeter=false&includeRolls=false` after hiding
all three); and printing issued **no** write request and no console error. **Non-vacuous:** forcing the KG
column to always print makes it fail eight times.

Environment notes, so the next run does not rediscover them (they are also in the tool's header): the image
has no root and no browser, so puppeteer came from npm into `/tmp/uitest` and Chrome's shared libraries from
`.deb` files extracted to `/tmp/chromedeps` (`LD_LIBRARY_PATH`). **Full Chrome cannot start here** -
`pthread_create: Resource temporarily unavailable`, only ~58 extra threads are grantable - which is the same
ceiling that aborts `dotnet test` when it builds and tests in one command; `headless: "shell"`
(chrome-headless-shell) starts and is what the tool uses.

### Checks (real output)

Release build of the API **0 errors** · unit suite **893 / 893** (was 889, +4) · frontend `tsc -b && vite
build` **PASS** · eslint **0 errors** (9 pre-existing warnings) · browser print check **failures: 0** ·
`git diff --check` **clean**.

### Not verified

The end-to-end HTTP + SQL Server path of the PDF (no database here): the column flags are verified at the
request level in the browser and the server's projection is verified by
`DeliveryPermitPdfColumnsTests` with the production export service rendering real bytes, but no request has
been served by a live API in this environment.
