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

`docs/audit/phase6-report.md` **does not exist** in the checkout (only `phase7/8/9` report on Phase 6), so the
briefed UI items were checked directly against the source instead of a report. Phase 6's **web surface** was
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
