# Phase 8 — Reports, UI, Exports & Cleanup

> **Mode:** verify first, fix what is genuinely wrong. **No commit, no push, no migration was applied.**
> **Phase status: `Verified` for everything covered by the unit suite, the static checks and the module-level
> frontend smoke test. Two checks remain `Not run` for environmental reasons (a real browser, and PDF Arabic
> glyph shaping on this host).**

Branch: `freebuff/8c663d5dafcf97d023c3991e` · Baseline HEAD: `abb4426` · Date: 2026-10-14
Predecessors: `docs/audit/phase1-report.md` … `phase7-report.md`, `progress.md`.

---

## A. What this phase had to deliver

| # | Requirement | Outcome |
|---|---|---|
| 1 | Reports on real data only, units never mixed | **Verified** (see §C.1) |
| 2 | PDF/Excel exports with Arabic/English, RTL, identity, correct formatting | **Verified** for content/structure; PDF Arabic *shaping* is a deployment-font matter (§E) |
| 3 | Excel item template + import (validate, row errors, idempotent, authorized) | **Verified** (see §C.2) |
| 4 | Frontend/UI, design system, states, remove dead controls, end-to-end modules | **Verified at build/static/module level**; interactive browser pass blocked (§E) |
| 5 | Customer portal removal (a delete) | **Verified absent** (see §C.3) |
| 6 | Attachments — association, type/size validation, authorization, no public URLs | **Fixed + verified** (§B.1) |
| 7 | QR `/scan/:type/:id` — types, id validation, authorization, no leakage | **Verified** (see §C.5) |

---

## B. What this session changed

### B.1 Backend — one genuine defect fixed (finding F.8)

**Attachment upload had no content-type/extension allowlist.** The bytes are served back from the API origin
with `File(content, contentType, fileName)`, so an uploaded **HTML or SVG file would be rendered by the browser
as active content on that origin** — a stored-XSS vector. Only the 25 MB size was validated.

Fix (an allowlist, not a denylist — a denylist can never enumerate every dangerous type):

- `Attachments/DTOs/AttachmentDtos.cs` — added `AttachmentFileTypes` with `AllowedExtensions`
  (pdf/png/jpg/jpeg/webp/gif/bmp/doc/docx/xls/xlsx/csv/txt/rtf/odt/ods/zip) and `AllowedContentTypes`
  (the matching MIME set), a `NormalizeContentType` helper (strips `; charset=…`), and `IsAllowed(fileName,
  contentType)` which requires **both** an allowed extension and a matching declared type.
- `Attachments/Commands/AttachmentCommands.cs` — `UploadAttachmentCommandValidator` now rejects any file that
  fails `AttachmentFileTypes.IsAllowed`.

`html`, `svg`, `xml`, `js` and unknown extensions are therefore never stored. The download path is unchanged
and still only reachable through the permission-guarded `AttachmentsController`.

### B.2 Frontend tooling — two Phase 1/8 items closed

- **F.9 — `npm ci` was broken** (`package-lock.json` out of sync with `package.json`). Regenerated the lockfile
  by installing the new dev dependencies through npm; **`npm ci --dry-run` now exits 0.**
- **F.B.4 — `npm run lint` could not run** (`eslint: not found`, no config). Added a real flat ESLint setup
  (`eslint.config.js` + `@eslint/js`, `typescript-eslint`, `eslint-plugin-react-hooks`,
  `eslint-plugin-react-refresh`, `globals`) wired to the existing `lint` script. **`eslint .` now exits 0.**

### B.3 Frontend — typing cleanup the new linter exposed (no behaviour change)

The first lint run reported **98 `no-explicit-any` errors, 1 unused binding, 11 advisories**. Each was fixed
at the cause rather than by relaxing a rule:

- Added an ambient **`src/types/api-globals.d.ts`** declaring the shared API error shape
  (`ApiErrorBody`/`ApiErrorLike`, covering `title`/`detail`/`message`/`code`/`shortage`). The ~94
  `onError`/`catch` callbacks across **30 files** that read the API error body are now typed
  `(err: ApiErrorLike)` instead of `(err: any)`, so a misspelled field is a compile error.
- `StockAdjustmentsPage`, `ProductionOrdersPage`, `ProductionOrderDetailPage`, `RawExternalReleasesPage`,
  `CustomerTransfersPage` — the negative-stock handler read `err.response.data.shortage`
  *without* optional chaining (it would have stored `undefined` into a `number` field); now
  `err.response?.data?.shortage ?? 0`.
- `PrintPreviewLayout` — `PrintColumn`/`rows` were `any`; the component is now **generic
  (`PrintPreviewLayout<T>`)** so a column's `render(row)` is typed from the rows passed in.
  `DeliveryStatementPrintPage` supplies its row type explicitly.
- `ImportPanel` — the upload helper returned `any`; it now returns `unknown` and the caller casts to the
  declared preview/execute shape; the `catch` uses the shared `errorMessage` helper.
- `ProductionFloorPage` — `const list = rows ?? []` allocated a new array each render, defeating the memoised
  derivations; now `useMemo(() => rows ?? [], [rows])` (clears both `exhaustive-deps` advisories).
- `HrPage` — the `TABS` value was used only as a type; replaced by an explicit `Tab` union.

Result: **0 lint errors** (9 remaining `react-refresh/only-export-components` advisories, which are warnings
and do not fail the run), `tsc -b` clean, and the production build clean.

---

## C. What was verified, and how

### C.1 Reports — real data, units never mixed

- `ReportsController` (inventory movements + warehouse balances + negative-stock overrides) builds every row
  from live queries (`GetInventoryMovementsQuery`, `GetRawMessagesQuery`, `GetReadyGoodsBalanceQuery`,
  `GetNegativeStockOverridesQuery`) — no hard-coded totals, no fabricated rows.
- Units are **separate columns/fields**, never added together: `QuantityKg` / `QuantityMeter`,
  `Balance KG` / `Balance M`, `SignedQuantityKg` / `SignedQuantityMeter`.
- A new test drives the real query over a seeded ledger row and asserts the two measurements stay distinct.

### C.2 Excel item template + import

- `ItemImportTemplate.Headers` = `Code, NameAr, NameEn, Category, BaseUnit, SecondaryUnit, RequireBothUnits`
  — exactly the item model's fields, with `BaseUnit` limited to KG/Meter and no invented unit.
- `ItemsController` serves the template (`import/template`), and preview/execute/update behind
  `items.create` / `items.edit` respectively.
- New tests: the downloaded template is a non-empty workbook whose headers round-trip through
  `ExcelImportReader`; the template's own sample rows re-import with **zero** invalid rows; a mixed file
  **creates only the valid row** and **reports the rejected one** (never silently dropped); a **retry is
  idempotent** (no duplicate); an existing code is **refused without the update flag** and updated with it; a
  **duplicate code inside one file is rejected**.

### C.3 Customer portal removal (a delete)

- **API:** a reflection sweep over every controller asserts no controller name, no `[Route]` template, no
  HTTP action template and no member name contains "portal"; `Permissions.All` contains no portal permission.
- **Route table:** a test reads `src/DyeHouseERP.Web/src/App.tsx` from the checkout and asserts it contains no
  "portal" reference; the `*` catch-all renders `NotFoundPage` for any unknown path.
- **Build:** the freshly built bundle contains no `/portal` route (the only "portal" tokens are React DOM's
  `createPortal`).
- The `20261013083344_RemoveCustomerPortal` migration is the (immutable) record of the tables being dropped;
  no portal code survives.

### C.4 QR / `/scan/:type/:id`

- The route is inside `<RequireAuth>` and resolves four types: `production-order`, `invoice`, `delivery`,
  `raw-message` (anything else renders a "unknown document type" message rather than a blank screen).
- Each type is rendered from the **existing, permission-guarded read endpoint**. A test asserts every one of
  those `{id:guid}` GET actions carries its own view policy (`production.view`, `invoices.view`,
  `ready.view`, `raw.receive`), so a scan cannot reveal a record the user may not open.
- A test asserts an unknown id throws `NotFoundException` for all four types — no invented data.

### C.5 Attachments

- Association is enforced (fixed entity-type list + the owning record must exist), and the new allowlist
  (B.1) plus the 25 MB limit gate the upload. A test asserts a valid PDF is stored against its record and that
  the download action carries `attachments.view`.
- Attachments are stored in the database and served only through `AttachmentsController`; there is no public
  static path.

### C.6 RTL/LTR, dark mode, design system (source-level)

- `i18n/index.tsx` sets `<html dir>` to `rtl` for Arabic (default) and `ltr` for English, plus `<html lang>`;
  `.ltr-nums` and an SVG guard in `index.css` keep numbers/logo correct inside an RTL page.
- `ThemeProvider` toggles the `dark` class on `<html>` (`darkMode: "class"`), with light/dark/system.
- Interactive verification of these in a real browser is **Not run** (§E).

---

## D. Tests added

`tests/DyeHouseERP.UnitTests/Phase8ReportsExportsCleanupTests.cs` — **26 tests** across five suites:

- `Phase8ExportTests` (3): an Excel export of the real inventory ledger carries the source record's **Arabic**
  data and keeps KG/Meter in separate fields; the worksheet is **right-to-left**; the **factory name from the
  identity provider** is present in the file; before/after counts of `InventoryTransactions`,
  `MaterialTransactions` and `CustomerLedgerEntries` are **identical** (an export moves nothing); a PDF export
  is a real `%PDF`.
- `Phase8ItemImportTests` (5): template columns + non-empty download + header round-trip; template re-imports
  cleanly; mixed file creates/skips/retries idempotently; existing code refused without permission and updated
  with it; duplicate code rejected.
- `Phase8PortalRemovalTests` (2): no controller/route/permission mentions the portal; the route table has no
  portal route.
- `Phase8ScanTests` (2): unknown ids → `NotFoundException` for all four scan types; every scan type is behind
  its permission policy.
- `Phase8AttachmentTests` (14 including the theories): allowed types accepted; HTML/SVG/JS/XML/EXE/no-extension
  refused; over-limit refused; unknown entity type and missing owner refused with nothing written; a valid
  attachment is stored and the download action is permission-guarded.

---

## E. Verification (commands actually run, with results)

| Check | Command | Result |
|---|---|---|
| Solution build | `dotnet build DyeHouseERP.sln` | **0 errors**, 2 warnings |
| New suite | `dotnet test … --filter FullyQualifiedName~Phase8` | **PASS — 26 / 26** |
| Full unit suite | `dotnet test tests/DyeHouseERP.UnitTests` | **PASS — 832 / 832** (+26) |
| Model vs snapshot | `dotnet ef migrations has-pending-model-changes` | **No changes…** → no migration needed |
| TypeScript | `npx tsc -b` | **exit 0** |
| Lint | `npm run lint` (`eslint .`) | **exit 0** (0 errors, 9 react-refresh advisories) |
| Production build | `npm run build` (`tsc -b && vite build`) | **PASS** (chunk-size warning only) |
| Clean install | `npm ci --dry-run` | **exit 0** (F.9 fixed) |
| React hook defects | `node tools/hooks-check.js` | **exit 0** — "No invalid hook call sites, no hook shadowing." |
| Module graph smoke | `node tools/smoke-modules.sh http://127.0.0.1:3000` | **PASS** — 68 modules, 0 failed |
| Dev server + preview host | `vite --host 0.0.0.0 --port 3000` | listening on `0.0.0.0:3000`; `/` and the preview hostname both **200** |
| Portal absent from build | `grep -i portal dist/assets/*.js` | only React DOM `createPortal`; **no `/portal` route** |
| Permission parity | `node tools/permissions-parity-check.cjs` | **118 / 118, 0 failures** |
| i18n parity | `node tools/i18n-parity-check.cjs` | **901 / 901, 0 failures** |
| Duplicate/DbSet sweep | `node tools/duplicate-sweep.js` | **PASS** — 88 = 88 |
| Wiring sweep | `node tools/wiring-sweep.js` | only login + public settings policy-free (unchanged) |

**Not run (with reasons, never claimed as passes):**

1. **Real-browser UI smoke** (`tools/ui-smoke.js`, `ui-smoke-authed.js`, `ui-smoke-tabs.js`,
   `printing-ui-check.cjs`). Puppeteer and Chrome were installed, but Chrome **cannot launch on this host**:
   it needs system libraries (`libnss3`, `libatk-1.0`, `libcups`, `libxkbcommon`, …) that are absent and cannot
   be installed — there is **no root / no passwordless sudo**. So forms, loading/empty/error states, RTL/LTR
   *at runtime*, dark mode and download dialogs are verified only by the compiler, the linter, the hook check,
   the module-graph smoke and the source (C.6). They are **not** browser-verified here.
2. **SQL-Server integration suite** — no `DYEHOUSE_TEST_CONNECTION` in this environment (Phase 1 §B.3), so the
   API could not be booted; the portal-returns-404 and scan-authorization checks are proved by the reflection
   tests and the route-table test rather than by live HTTP against the API.
3. **PDF Arabic text shaping** — `ReportExportService` renders a real PDF, but correct Arabic glyph shaping
   depends on an Arabic-capable font being available to QuestPDF in the deployment (documented in the service
   itself). No font was embedded here, so the PDF's Arabic *rendering* is unverified (the process did not
   regress; this is the Phase 1 F.D.9 item).

---

## F. Migrations

**None created, none required.** This phase changed no entity or mapping (the F.8 fix lives in the Application
layer) and `has-pending-model-changes` is clean. The last migration remains
`20261014054727_Phase6InvoiceBillingUnitAndCheckSettlement`.

---

## G. Files touched by Phase 8 (working tree, uncommitted)

**Backend:** `Application/Attachments/DTOs/AttachmentDtos.cs`,
`Application/Attachments/Commands/AttachmentCommands.cs`.

**Tests:** `tests/DyeHouseERP.UnitTests/Phase8ReportsExportsCleanupTests.cs` (new).

**Web — tooling:** `eslint.config.js` (new), `package.json`, `package-lock.json`.
**Web — types:** `src/types/api-globals.d.ts` (new).
**Web — components/pages:** `components/ImportPanel.tsx`, `components/PrintPreviewLayout.tsx`,
`pages/print/DeliveryStatementPrintPage.tsx`, `pages/HrPage.tsx`, `pages/ProductionFloorPage.tsx`,
`pages/StockAdjustmentsPage.tsx`, `pages/ProductionOrdersPage.tsx`,
`pages/ProductionOrderDetailPage.tsx`, `pages/RawExternalReleasesPage.tsx`,
`pages/CustomerTransfersPage.tsx`, plus the ~25 further pages whose error callbacks were re-typed.

---

## H. Phase 8 status

**`Verified`** for the exports/reports content, the item template and import, the portal removal, the scan
authorization and invalid-id behaviour, the attachment security fix, and the whole frontend toolchain
(tsc, lint, clean install, hook check, module graph, production build). One production defect was found and
fixed (**F.8**), and two long-standing Phase 1/8 tooling items were closed (**F.9**, **F.B.4**).

**Not run** (and therefore not claimed): the interactive browser pass (blocked by missing system libraries and
no root) and the SQL-Server integration suite (no database); PDF Arabic shaping depends on a deployment font.

No commit, no push, and no migration was applied.
