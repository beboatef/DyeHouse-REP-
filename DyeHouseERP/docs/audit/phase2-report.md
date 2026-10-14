# Phase 2 — Master Data & Factory Settings

Baseline: `abb4426`, branch `freebuff/8c663d5dafcf97d023c3991e`. No commit, no push, no migration applied to any database.

---

## A. Scope actually touched

| Scope item | Verdict | What was done |
|---|---|---|
| 1. Items & unit configuration | **Extended** | Primary/secondary/RequireBothUnits already existed. Added the missing **explicit, item+operation-scoped conversion rule** (§C.1). Confirmed no global kg↔meter conversion. |
| 2. Customers & sub-customers | **Extended** | Added an explicit stored **primary → sub-customer** FK + cycle validation (§C.2). |
| 3. Suppliers, Warehouses | **Verified (no code change)** | Both already have complete CRUD, search/filter, activate/deactivate and audit (§D.3). |
| 4. Production stages (dynamic) | **Verified (no code change)** | Already dynamic with activation, route markers, machine association, search and audit; Ready Goods and inspection distinctions confirmed (§D.4). |
| 5. Machines | **Verified (no code change)** | Seed counts and management already correct (§D.5). |
| 6. Production execution shifts | **Verified (no code change)** | First/Second/Third seeded, independent of HR shifts (§D.5). |
| 7. Service pricing | **Verified (no code change)** | Key = PO+Stage+Unit; Ready Goods not billable; no prep-cost component; StageCostRate absent (§D.6). |
| 8. Factory settings | **Rebuilt the consumer side** | Added `ICompanyIdentityProvider`; rewrote `ReportExportService` to consume it; removed 28 hard-coded `"DyeHouse ERP"` subtitles; logo upload validation (§C.3). |

---

## B. Build, tests and migrations (actual results)

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build DyeHouseERP.sln -c Release --no-incremental` | **PASS** — 0 errors, **7 warnings** (identical to the Phase 1 baseline; no new warnings) |
| Unit tests | `dotnet test tests/DyeHouseERP.UnitTests -c Release --no-build` | **PASS** — **715/715** (Phase 1 had 696; +19 new tests) |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **PASS** — exit 0 |
| i18n key parity | `node tools/i18n-parity-check.cjs` | **PASS** — ar 887 / en 887, 0 failures |
| Migration model check | `dotnet ef migrations has-pending-model-changes` | **PASS** — "No changes have been made to the model since the last migration." |
| Migration SQL generation | `dotnet ef migrations script <prev> <new>` | **PASS** — emits valid T-SQL (proof the migration compiles; **not applied**) |
| Frontend lint | `npm run lint` | **Not run** — pre-existing: `eslint` is not installed and there is no config (Phase 1 finding F.9/F.B.4). |
| Integration tests | — | **Not run** — need a real SQL Server (Phase 1 limitation, unchanged). No DB is available here. |

---

## C. Code changes (new / modified), and why each is required by the phase

### C.1 Item-scoped explicit conversion rule (scope item 1)

The phase requires an *"optional item-specific conversion rule (explicit, scoped to item + operation)"* and *"explicit conversion only where configured"*. No such rule existed. Added:

- **`Domain/Entities/ItemUnitConversion.cs`** (new) — one rule = (ItemId, FromUnit, ToUnit, Factor, **Operation**). Invariants (pure, no DB): FromUnit ≠ ToUnit, Factor > 0, Operation required. `Convert(amount)` and `AppliesTo(operation, from, to)` are instance methods, so a conversion can only run through a rule that was deliberately configured. Nothing in the system calls it implicitly.
- **`Persistence/Configurations/ItemUnitConversionConfiguration.cs`** (new) — unique index **(ItemId, Operation)** (at most one rule per item+operation), check constraints `FromUnit <> ToUnit`, `Factor > 0`, FK to Items (Cascade).
- **`Items/Commands/ItemUnitConversionCommands.cs`** (new) — Set (upsert) + Remove, with validators; refuses a rule for a single-unit item and for a unit the item does not configure.
- **`Items/DTOs/ItemUnitConversionDto.cs`**, **`Items/Queries/GetItemUnitConversionsQuery.cs`** (new).
- **`IApplicationDbContext` / `ApplicationDbContext`** — added `DbSet<ItemUnitConversion>`.
- **`ItemsController`** — `GET/POST/DELETE api/Items/{id}/conversions`.
- **Frontend** — `ItemUnitConversion` type + `ItemsApi.conversions/setConversion/removeConversion` (`client.ts`); a conversions panel inside the item edit card (`ItemsPage.tsx`); 9 new AR/EN i18n keys.

`UnitOfMeasure` remains only KG/Meter; there is **no** global conversion constant anywhere. The rule table is empty by default.

### C.2 Primary → sub-customer relationship (scope item 2)

The phase requires an *"explicit stored, validated primary → sub-customer relationship (FK, no self-reference, no cycles)"*. None existed.

- **`Domain/Entities/Customer.cs`** — `ParentCustomerId` (nullable), `ParentCustomer` nav, `IsSubCustomer`, `SetParent` (rejects self-parenting at the entity level).
- **`Persistence/Configurations/CustomerConfiguration.cs`** — self-referencing FK (Restrict), index on `ParentCustomerId`, check constraint `CK_Customers_ParentNotSelf`.
- **`Customers/Commands/SetCustomerParentCommand.cs`** (new) — validates the parent exists and walks UP the parent chain to refuse any assignment that would create a cycle (with a visited set so it terminates even on pre-existing bad data).
- **`CustomerDto`** + **`GetCustomersQuery`** — expose `ParentCustomerId`, `ParentCustomerCode`, `ParentCustomerName`, `IsSubCustomer`.
- **`CustomersController`** — `POST api/Customers/{id}/parent`.
- **Frontend** — `Customer` type fields, `CustomersApi.setParent`, a parent `Select` in the customer form, and a "parent" column in the list; 3 new AR/EN i18n keys.

### C.3 Factory settings — single authoritative identity (scope item 8)

Phase 1 confirmed the defect: `ReportExportService` hard-coded `"DyeHouse ERP"` in its PDF footer and rendered no logo, and 28 controllers passed the literal `"DyeHouse ERP"` as a subtitle. Fixed:

- **`Application/Common/Interfaces/ICompanyIdentityProvider.cs`** (new) — `CompanyIdentity(NameAr, NameEn, LogoDataUrl)` with a safe `TryGetLogoBytes` (never throws; rejects missing/malformed/non-image data) and `ICompanyIdentityProvider.Get()`.
- **`Persistence/Services/CompanyIdentityProvider.cs`** (new) — reads the single `CompanySettings` row on **every call** (so an update is reflected immediately — no stale identity), falls back to a built-in default on a missing row or a read failure. Registered scoped in `Persistence/DependencyInjection.cs`.
- **`Infrastructure/Services/ReportExportService.cs`** — now takes `ICompanyIdentityProvider`. The PDF header renders the company name (EN + AR when they differ), an optional logo drawn inside a `try/catch` (a broken logo is skipped, never thrown), the document title, and an optional subtitle; the footer uses the configured name. Excel sets `RightToLeft` and puts the identity in the page header/footer so the header row stays row 1 (the import templates depend on that). **No hard-coded identity remains in the service.**
- **28 occurrences across 20 controller files** — the hard-coded `"DyeHouse ERP"` subtitle replaced with `null`; `ExportFileHelper.ToFile`/`ToPdf`, `PurchasesController.ExportFile` and `IReportExportService.GeneratePdf` now accept a nullable `subtitle` (identity comes from settings, so the subtitle is genuinely optional).
- **`UpdateCompanySettingsCommandValidator`** — logo upload validation: only `data:image/(png|jpeg|jpg|gif|webp);base64,…` with a valid base64 payload and under ~2 MB is accepted; a new public `IsValidImageDataUrl` helper is unit-tested.

The pre-existing anonymous `GET api/settings` (login page branding) is unchanged.

### C.4 The migration (created, **not applied**)

**`20261014015833_AddPhase2ItemConversionsAndSubCustomer`** — one migration, because both changes belong to this phase and EF emits them together:
1. `ALTER TABLE [Customers] ADD [ParentCustomerId] uniqueidentifier NULL` + index `IX_Customers_ParentCustomerId` + check `CK_Customers_ParentNotSelf` + self FK `FK_Customers_Customers_ParentCustomerId` (NO ACTION).
2. `CREATE TABLE [ItemUnitConversions]` with `PK_ItemUnitConversions`, checks `CK_ItemUnitConversions_DifferentUnits` / `CK_ItemUnitConversions_PositiveFactor`, FK to `Items` (CASCADE), indexes `IX_ItemUnitConversions_ItemId` and **unique `IX_ItemUnitConversions_ItemId_Operation`**.

Both additions are nullable/new-table, so the migration is safe on an existing database. It was generated and its SQL scripted for verification; it was **not** run against any database.

---

## D. Verification of the "already done" scope items (evidence)

### D.1 No global kg↔meter conversion
`UnitOfMeasure` is only KG/Meter (`SharedEnums.cs:5`); `ItemUnitRules.ConfiguredUnits` and `EnsureValidQuantities` validate, never convert; the only `ConversionFactor` in the system is on **purchase units** for chemicals (`PurchaseUnits/Commands`, unrelated to fabric). New test `WithoutAnyRule_ANonConfiguredUnitIsStillRefused_SoThereIsNoGlobalConversion` proves an unconfigured unit is refused outright.

### D.2 Item unit validation (existing, re-verified)
`ItemUnitConfigurationTests` (phase-1 baseline) already covers single / secondary-optional / both-required plus the real posting handlers (`CreateRawMessageCommand`, `AllocateRawCommand`, `CreateDeliveryCommand`, `CreateCustomerReturnCommand`, `CreateProductionOrderCommand`) refusing an unconfigured unit. No change needed.

### D.3 Suppliers & Warehouses — **complete CRUD with activate/deactivate, search/filter, audit**
- **Warehouses**: `GET` (kind/activeOnly/search), `POST`, `PUT {id}`, `POST {id}/active`, `import/*`, `export` — `WarehousesController.cs`.
- **Suppliers**: `GET` (activeOnly/search), `POST`, `PUT {id}` (which carries a nullable `IsActive` → `entity.Activate/Deactivate`, `SupplierCommands.cs:86-89`), `import/*`, `export` — `SuppliersController.cs`. Activation is done through the edit payload rather than a dedicated `/active` endpoint (a pre-existing, consistent-with-`UpdateSupplierCommand` choice — noted, not changed).
- Audit is automatic on every change via `AuditSaveChangesInterceptor` (Phase 1).

### D.4 Production stages (dynamic) — **verified**
`ProductionStageDefinition` carries only capability flags any workflow honours (`AllowSkip`, `RequiresDrawingNumber`, `IsFormationStage`, `IsReadyGoodsStage`); `AllowRepeat/Rework/Return` were removed in migration `20261013145555`. `SetProductionStageActiveCommand` deactivates without deleting and protects the formation/ready-goods markers; `GetProductionStageDefinitionsQuery` (ActiveOnly + Search) supports search/filter; `IssueRawMaterialCommand` builds the route from `.Where(s => s.IsActive)`, and `TransferToNextStageCommand` refuses a deactivated next stage. Covered by `ProductionStageAdministrationTests` (`AnInactiveStage_IsNeverChosenForANewTransfer`, `DeactivatingAStage_LeavesOldExecutionsAndTheirTraceabilityIntact`, `TheTwoRouteMarkers_CannotBeDeactivated`, `Search_MatchesPartialCodeAndPartialName`).
- **Ready Goods as a stock destination, not an ordinary stage**: `IsReadyGoodsStage` diverts to the `WarehouseKind.ReadyGoods` warehouse and completes the order (`TransferToNextStageCommand.cs:172-206`) — master-data distinction present.
- **Inspection is an optional configurable stage, not part of receiving**: the 21 seeded stages do **not** include inspection, and receiving inspection is recorded information only — `RawMessage.InspectionStatus` defaults to `PendingInspection` and is "recorded info, never an approval gate" (`RecordInspection`; `client.ts` `RawMessageLine` comment); `Printing` is a normal configurable stage seeded with `requiresDrawingNumber: true` (enforcement is Phase 4). No change needed.

### D.5 Machines & shifts — **verified**
`MachineSeeder` seeds exactly Jet 15, Stenter Fixation 4, Stenter Preparation 4, Water Fixation 2, Thermal Fixation 1 (no invented counts) with stage links; `ProductionShiftSeeder` seeds First/Second/Third, distinct from HR shifts. Covered by `ProductionMasterDataTests` (`InitialMachineMapping_MatchesTheApprovedNumbers`, `Seeder_CreatesMachinesAndStageLinks_AndIsIdempotent`, `Shift_HasNoHoursOrRestDayFields_ItIsExecutionDataOnly`).

### D.6 Service pricing — **verified**
`ProductionOrderServicePrices` unique index = `(ProductionOrderId, StageDefinitionId, Unit)`; `CustomerServicePrices` = `(StageDefinitionId, CustomerId, Unit)`. `ApplyServicePriceToJobOrderCommand` refuses the Ready Goods stage: *"Ready Goods is not a chargeable production service."* (`PriceListCommands.cs:189`). There is no preparation-cost component in the price; `StageCostRate` does not exist (removed). Covered by `PriceListTests`.

---

## E. Tests added (19 new, all passing)

`tests/DyeHouseERP.UnitTests/Phase2MasterDataTests.cs` — explicit conversion (create / upsert-not-duplicate / lookup-by-operation / refused for single-unit / entity invariants / applied only for the matching operation / no-rule-means-no-conversion / removal), sub-customer (assign+clear / self-parent refused / cycle refused / missing parent), and factory settings:
- `Pdf_UsesTheCompanyIdentity_NotAHardCodedName` — **real PDF generation**; two identities produce *different* bytes (proves the name is consumed and the hard-coded footer is gone) and the output starts with `%PDF-`.
- `Pdf_StillGenerates_WhenTheLogoIsMissing` / `_WhenTheLogoIsUnreadableOrCorrupt` / `_WithAValidLogo` — all produce a valid PDF (real QuestPDF generation; the corrupt-logo cases exercise the try/catch).
- `CompanyIdentity_DecodesAValidLogo_AndRefusesBrokenOnes`.
- `CompanyIdentityProvider_ReturnsTheConfiguredName_AndReflectsAnUpdate_WithNoStaleValue` — proves single-source + no stale identity, and the safe default when the row is absent.
- `Excel_StillGenerates_AndKeepsTheHeaderRowFirst` — parses the produced workbook, asserts `Code` is row 1 and the data row follows, and `RightToLeft` is set.
- `LogoUploadValidator_AcceptsOnlyRealImageDataUrls`.

`tests/DyeHouseERP.UnitTests/TestCompanyIdentityProvider.cs` — shared test double. `PrintingWorkflowTests` updated for the new constructor.

---

## F. Blocking questions / notes (not resolved by changing existing behaviour)

1. **Suppliers activate/deactivate shape** — Warehouses use `POST {id}/active`; Suppliers use a nullable `IsActive` on `PUT {id}`. Both satisfy the phase; I did **not** unify them (that would change existing client behaviour and tests). Flagged for your call.
2. **`production.scrap` permission** and the **PDF Arabic font embedding** questions from the Phase 1 report remain open and are unchanged by this phase. The export rewrite keeps the existing "host font" assumption (documented inline in `ReportExportService`); embedding Noto Naskh Arabic is still a Phase 8 decision.
3. **Frontend lint** remains non-runnable (pre-existing); no frontend test runner exists.

---

## G. Status

**`Verified`** — scope-locked to Phase 2; backend build clean (7 warnings, same as baseline), 715/715 unit tests pass, frontend build passes, i18n parity holds, the model matches the snapshot, and one migration was created (not applied). The integration suite and frontend lint are recorded **Not run** with reasons, never as passes.
