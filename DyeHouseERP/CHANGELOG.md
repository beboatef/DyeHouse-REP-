# DyeHouseERP Changelog

## Part 1-3 — Phone floor view, scan flow, PWA (frontend only, no API change)

- `src/DyeHouseERP.Web/src/pages/FloorMobilePage.tsx` (new, route `/floor/m`
  in `App.tsx`): phone-first floor screen - one card per live order (order
  number, customer/item, current stage, time in stage, in/out quantities),
  stage filter + search, no admin tables. Next action per state: Start/Resume
  link to the order, inline record-output form (KG/Meter inputs with
  `inputmode="decimal"`, never converted), inline pause with reason, and a
  Transfer/details link. Output posts through the existing
  `updateStageOutput` endpoint with a confirm step that repeats the quantity
  and a pending-state submit lock, so a double tap cannot post twice (the
  transfer/output endpoints expose no idempotency token, and the API contract
  is frozen - the guard is client-side). On a network failure the form keeps
  every entered value and shows a bilingual no-blame retry message.
- Scan flow: `src/DyeHouseERP.Web/src/pages/scan/ScanViewPage.tsx` reworked
  into a one-screen summary (order number, current stage, next action) with a
  full-width next-action button to the order (two taps from the camera), plus
  load-failure state with retry. Signed-out scans redirect to login and return
  to the same scan URL after sign-in: new `src/DyeHouseERP.Web/src/utils/returnUrl.ts`
  (`resolveReturnUrl` accepts only same-origin app paths, rejects absolute,
  protocol-relative, `javascript:` and backslash URLs) wired into
  `RequireAuth.tsx` and `LoginPage.tsx`; new
  `src/DyeHouseERP.Web/src/utils/returnUrl.test.ts` (10 tests, incl. external
  return-URL rejection).
- PWA, no offline data: `public/manifest.webmanifest` + `public/icon-192.png`
  (generated from `favicon-512.png`) + `index.html` manifest/theme-color/
  apple-touch links; new `public/sw.js` caching only the static app shell
  (JS/CSS/fonts/icons) with a top-of-file rule comment - `/api`, auth cookies
  and ledger data are network-only and never cached; `main.tsx` registers the
  worker in production builds only; new
  `src/DyeHouseERP.Web/src/components/ServiceWorkerUpdatePrompt.tsx` shows a
  bilingual refresh prompt when a new deployment is waiting (activated only on
  user tap, never silently); new `src/DyeHouseERP.Web/src/sw.test.ts` failing
  the build if the worker's never-cache list stops matching `/api`.
- Shared: `.btn-floor` (min-height 48px) in `index.css`; 47 new
  `floor.m.*`/`scan.*`/`pwa.*` keys in both dictionaries; `src/vite-env.d.ts`
  (standard Vite client types, needed by the SW registration);
  `tools/ui-smoke.js` covers `/floor/m` + two `/scan/*` routes and accepts
  `SMOKE_VIEWPORT=mobile` (390x844 touch) in addition to desktop.
- Checks: `tsc -b` clean; `lint` 0 errors (10 pre-existing fast-refresh
  warnings); `build` green with manifest/SW/icons in `dist`; `vitest` 21/21;
  `i18n-parity-check` 0 failures (1219/1219); `hooks-check` clean;
  `ui-consistency-check` 2 failures, both pre-existing in `UsersPage.tsx` /
  `ReportsPage.tsx` (untouched by this change). `ui-smoke.js` could NOT run
  here (Chrome cannot be downloaded in this environment) - production-build
  smoke incl. `/floor/m`, `/scan` at 390x844 + desktop must run in CI; PWA
  assets and SPA fallback for the new routes verified over `vite preview`
  with curl instead. No API contract change (no backend file touched).

## Part 1 — Management KPIs (7 metrics, read-only, `kpis.view`)

- New query set `Application/Kpis/` (DTOs + 7 queries + `KpiSummaryQuery`, all
  behind `GET api/kpis/*` in `KpisController.cs`, permission `kpis.view` added
  to `Permissions.cs` + `All` and mirrored in `permissions.ts`): waste rate per
  order/period (reuses `WasteMath.Percent`, replicated from mapped stream
  columns in SQL), lead time (issue OUT ledger row to first posted ready
  transfer), machine utilisation per machine/shift (completed executions
  clipped to the window over active-shift hours), throughput per stage/shift
  (posted ready transfers via `StageExecutionId`), customer exposure (live
  ledger sum + open invoices + oldest-unpaid age from invoice date), cheques
  due in N days (default 7, 1–90), governance (overrides + audit-log period
  reopenings). Every result carries date range + `UTC` timezone; division by
  zero returns null; nothing writes.
- `ProductionShift.HoursPerDay` (decimal, default 8, validated 0–24) added
  with migration `AddProductionShiftHours` (existing rows backfilled 8);
  `ProductionShiftDto`/mapper, create/update commands + validators,
  `ProductionShiftsController` update request, seeder updated; no UI manages
  execution shifts (API-only master data, unchanged).
- `docs/kpis/definitions.md` (English): one formula per KPI with source
  entities and exact filters.
- Frontend: KPI section on `DashboardPage.tsx` (current-month summary, hidden
  on 403) and a `مؤشرات` tab on `ReportsPage.tsx` (filters + summary cards +
  detail tables); `KpisApi` in `client.ts`; 62 `kpi.*`/`reports.kpisTab` keys
  in both dictionaries.
- Tests: `KpiQueryTests` (8 facts, one per KPI with hand-computed values, incl.
  a null-on-zero-denominator case), `KpiSqlTranslationTests` (3 `ToQueryString`
  proofs that the waste/treasury-running-balance/payroll sums translate to
  SQL Server, incl. the `Guid.CompareTo` tiebreak), summary-vs-detail
  consistency test.
- Checks: solution Release build 0 CS warnings (only the known NU1902 package
  advisory); unit tests 1081/1081; `tsc -b`, `lint` (0 errors), `build`,
  `vitest` (7/7, incl. the route contract covering the new endpoints);
  `i18n-parity-check` 0 failures (1171/1171);
  `permissions-parity-check` 121/121; `duplicate-sweep`, `wiring-sweep`
  (406 routes, 0 duplicates, 0 unresolved UI calls), `hooks-check` clean;
  `has-pending-model-changes` clean. Integration tests NOT executed here (no
  SQL Server in this environment) — they run in CI.

## Part 2 — Accounting export (`accounting.export`, CSV + XLSX)

- New `GET api/accounting/export` in `AccountingController.cs` (permission
  `accounting.export` added to `Permissions.cs` + `All` and mirrored in
  `permissions.ts`): date range (required), optional customer OR supplier
  filter, `format=csv|xlsx` (CSV carries exactly one set, UTF-8 with BOM;
  XLSX one sheet per set via ClosedXML). Five sets — customer invoices with
  lines, supplier invoices + payments, treasury per account (running balance
  computed in SQL over full history via a correlated subquery), payroll runs
  per month, cheque movements — each row with source document number + ledger
  row id (payroll/cheque exceptions documented), totals row appended from the
  same rows; one audit entry (range/format/row counts, never file contents);
  export never locks data.
- `IReportExportService` gained `GenerateCsv` + `GenerateExcelWorkbook`
  (implemented in `ReportExportService`; single-sheet `GenerateExcel`
  refactored onto the shared filler, output unchanged); test fakes updated.
- `docs/accounting/export-mapping.md` (Arabic): column-to-field mapping per
  set with the confirm-with-accountant note; no product-compatibility claims.
- Frontend: `AccountingExports` in `exports.ts` + export card on the
  `مؤشرات` tab (range, set, customer/supplier dropdowns, CSV/XLSX buttons);
  12 `acct.*` keys in both dictionaries.
- Tests: `AccountingExportTests` (CSV BOM + line/invoice totals vs independent
  recomputation, treasury running-balance compounding incl. opening, XLSX
  five-sheet + audit row counts, audit entry without file contents).
- Checks: same as Part 1 (shared run above). Integration tests NOT executed
  here (no SQL Server in this environment) — they run in CI.

## Docs & CI — user guide (Arabic), API documentation, integration job in CI

- New `docs/user-guide/` (Arabic, one file per module, numbered steps, screenshot
  placeholder per screen, required `Permissions.*` constant quoted in each):
  `01-login-and-profile.md`, `02-receiving-raw-messages.md`, `03-job-orders.md`,
  `04-stage-transfer-and-ready-goods.md`, `05-separates-and-reprocessing.md`,
  `06-external-processing.md`, `07-deliveries-and-invoices.md`,
  `08-treasury-and-cheques.md`, `09-purchases-and-materials.md`,
  `10-payroll-and-hr.md`, `11-period-closing.md`, `12-approvals.md` (separation of
  duties + the two-permission negative-stock override),
  `13-reports-and-exports.md`, `14-troubleshooting.md` (exact titles of
  `LOCK_TIMEOUT`, `CONCURRENT_UPDATE`, `NEGATIVE_STOCK`, the closed-period and
  deactivated-warehouse refusals, with the user action for each).
- New `docs/api/workflows.md` (Mermaid sequences for the five core workflows with
  endpoint routes and ledger rows written) and `docs/api/errors.md` (envelope
  shape, exact code/title table, retry rules).
- API documentation in code (comments + attributes only, no behaviour change):
  `GenerateDocumentationFile` on + `IncludeXmlComments` in Swagger (`Program.cs`,
  `DyeHouseERP.API.csproj`); every action in all 40 controller files now carries a
  summary (route + required permission) and the error-code `ProducesResponseType`
  attributes it can return (Approvals, Attachments, AuditLog, Auth, Checks,
  CostAccounting, CustomerReturns, CustomerTransfers, Customers, Dashboard,
  Deliveries, FormationRequests, FormationSpecifications, Hr, InventoryTraceability,
  Invoices, Items, Machines, MaterialSales, Materials, Payroll, PeriodClosing,
  PriceLists, ProductionFloor, ProductionOrders, ProductionShifts, ProductionStages,
  PurchaseUnits, Purchases, RawExternalReleases, RawMessages, ReadyGoods,
  ReportBuilder, Reports, Separates, Settings, StockAdjustments, Suppliers,
  Supplies, Treasury, Users, Warehouses); request records documented with
  `<param>`; remaining public members documented in `Auth/AuthCookies.cs`,
  `Auth/CsrfProtectionMiddleware.cs`, `Auth/LoginRateLimiter.cs`,
  `Auth/LoginThrottleMiddleware.cs`, `Authorization/PermissionAuthorization.cs`,
  `Common/ExportFileHelper.cs`, `Health/DatabaseHealthCheck.cs`,
  `Health/ReadinessHealthChecks.cs`, `Logging/RequestLogEnricher.cs`,
  `Middleware/ExceptionHandlingMiddleware.cs`,
  `Middleware/MustChangePasswordMiddleware.cs`,
  `Middleware/RequestMetricsMiddleware.cs`, `Middleware/SecurityHeadersMiddleware.cs`,
  `Startup/DatabaseBootstrap.cs`, `Program.cs`.
- CI (`.github/workflows/ci.yml`): integration job now runs on every pull request
  (no branch filter), keeps pushes to main, and gains a nightly `schedule` run;
  SQL Server service pinned to `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04`
  (both jobs); no `continue-on-error` anywhere; readiness wait is bounded and fails
  when the port never opens; tests run in Release with a TRX logger, results
  published as the `integration-trx` artifact, and a summary step fails the job
  when zero tests executed.
- `README.md`: local Docker reproduction of the integration job, CI paragraph
  updated (schedule + TRX + zero-test guard), new Documentation section linking
  the user guide and API docs.
- Checks: solution Release build 0 CS warnings (only the known NU1902 package
  advisory); unit tests 1066/1066; `wiring-sweep` 0 duplicate routes;
  `i18n-parity-check` 0 failures (no web strings added); workflow YAML parses.
  Integration tests were NOT executed here (no SQL Server in this environment) —
  they compile and run in CI.

## Part 1 — Backup, restore and verification

- Added `tools/backup/backup-dyehouse.sql` + `.sh` (FULL backup, COMPRESSION + CHECKSUM,
  `COPY_ONLY=0`, UTC-timestamped name; sqlcmd variables, no credentials in files) and
  `tools/backup/restore-verify.sql` + `.sh` (restores into a `*_restoretest` scratch database only,
  refuses any other target; RESTORE VERIFYONLY, DBCC CHECKDB, ledger-sum reconciliation per
  warehouse/item, row counts to a report file, guarded drop).
- Added `docs/operations/backup-and-restore.md` (nightly full / 15-minute log schedule,
  retention, off-machine copy, encryption, drill usage, RTO/RPO fields, restore checklist).
- Added CI job `backup-drill` (seeds the drill DB via `FullHappyPathTests`, backs up, restores,
  verifies) and `BackupRestoreVerificationTests` (recomputes ledger balances through the
  application's own services against hand-written SQL on the restored copy; skips without
  `DYEHOUSE_RESTOREREVERIFY_CONNECTION`).
- README: removed the "backups are the SQL Server team's responsibility alone" sentence;
  linked the three operations runbooks.
- Checks: `bash -n` on both wrappers; all 8 guard paths exercised (allow-list, suffix, name
  refusal); CI YAML parses. SQL execution and the drill itself run in CI (no SQL Server here).

## Part 2 — Password and session policy

- Domain: `User.MustChangePassword` + `PasswordChangedAtUtc`; new `PasswordHistory` entity
  (PBKDF2 hashes only); `UserRefreshToken.LastActivityAtUtc`; `CompanySettings.PasswordMaxAgeDays`
  (default 0 = disabled) + `SessionIdleMinutes` (default 30). Migration `AddPasswordPolicy`
  with backfills (token activity = creation, idle default 30, legacy password age = rollout date);
  `has-pending-model-changes` clean.
- Application: `PasswordPolicy` (12/200/username/history, messages mirrored verbatim in the
  frontend ar+en dicts) + `PasswordChangeService` (single set-point: policy, history prune,
  revoke-all-sessions `PasswordChanged`, null-payload audit, stable event); creation and admin
  reset flag the account; self-change clears it; login flags (never locks) on expiry; refresh
  refuses idle sessions (including the reuse-grace path). New permission `users.reset_password`
  now gates the admin reset endpoint (admin bypass unchanged); new endpoints `GET /api/auth/me`,
  `POST /api/auth/change-password`, `PUT /api/settings/password-policy` (all additive).
- API: `MustChangePasswordMiddleware` (live DB flag check; 403 + `PASSWORD_CHANGE_REQUIRED`;
  escape hatches change-password/logout/me/login/refresh/health). Bootstrap and `UserSeeder`
  admins start flagged.
- Web: `users.reset_password` mirrored in `permissions.ts`; `/change-password` page with the same
  policy messages; login routes flagged sessions there; 403 gate redirects there; Users page gates
  reset controls and shows the must-change badge; Settings page edits the two knobs.
- Tests: 22 unit (`PasswordPolicyTests`: policy, create/change/reset, expiry, idle, seeder, gate)
  + 4 integration (`PasswordPolicyApiTests`: bootstrap cannot skip, reuse refused, expiry flags
  without lockout, change kills sessions). Note: deployments with non-admin `users.manage`
  holders who reset passwords must also grant them `users.reset_password`.

## Part 3 — Monitoring and alerting

- Serilog: single `RequestLogEnricher` (`RequestId`, authenticated `UserName`, route template,
  `StatusCode`; secrets never read — pinned by `MonitoringTests`); elapsed via request logging.
- Stable `LogEvents` constants emitted for LOCK_TIMEOUT, CONCURRENT_UPDATE,
  NegativeStockOverrideCreated, LoginThrottleTripped, PeriodClosed/Reopened, ApprovalConsumed,
  RefreshTokenRevokedForPasswordChange, DatabaseConnectionFailed (+ `EventNames` stability test).
- Health: `/health` is anonymous liveness with no checks; `/health/ready` requires
  `users.manage` and checks database reachability, pending migrations and seed data (formation
  stage + `WH-RAW/RDY/MAT/SUP`); choice documented in `docs/operations/monitoring.md`.
- Metrics: in-box `DyeHouseERP` meter (request duration/5xx by route, lock wait/timeouts);
  OTLP export only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (new `OpenTelemetry.Exporter.
  OpenTelemetryProtocol` 1.12.0 reference; no backend SDK). Note: NuGet audit reports one
  moderate advisory (GHSA-4625-4j76-fww9) against the exporter package — package-level, not
  project code; the exporter is inert unless the endpoint variable is set.
- Added `docs/operations/monitoring.md` (event table + thresholds, Seq/Elasticsearch queries).
- Tests: `MonitoringTests` (4 unit) + `HealthProbeTests` (3 integration, run in CI).

## Portal — Phase 1: Security Design (design only, not approved for build)

- Added `docs/portal/design.md` (English): full security design for the read-only
  customer portal. Covers the endpoint inventory (P1–P12 portal, A1–A3 internal
  admin), per-group STRIDE threat model (spoofing, IDOR/customer-to-customer leakage
  via shared IDs, order/invoice number enumeration, session fixation), exact
  field-level data scope per document type with an explicit never-exposed list
  (other customers' prices, internal costs, cost-bearer decisions, employee data,
  treasury accounts, any party's cheque details, audit entries), the `PortalAccount`
  identity model (staff-created under a new `portal.manage` permission, exactly one
  immutable `CustomerId`, no shared logins, instant lockout on disable), the
  authentication decision (password + mandatory TOTP; magic links rejected with
  justification; separate audience, cookies and refresh table from staff JWTs),
  server-side customer scoping with a query-shape example and 404-not-403 rule,
  rate limiting / audit / monitoring requirements, data residency and retention,
  and 12 open questions the business must answer.
- No application code was written or changed in this phase. No staff-side guard
  was touched. No new user-visible strings were added (i18n parity unaffected).
- Next: Phase 2 build starts only after a human writes "design approved".
