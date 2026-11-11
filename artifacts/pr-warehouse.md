## Summary

Two themes, plus four smaller fixes that were already on the branch.

**1. Every warehouse keeps its own stock, item card and report** — a balance is scoped to the warehouse it physically sits in, not only to the receipt message or job order behind it.

**2. The web bundle is split per route** — opening the dashboard no longer downloads all fifty screens.

`feat/react-19-upgrade` is 7 commits ahead of `main` and 0 behind (fast-forwardable, no merge conflicts), 36 files changed, +2966/−404.

## 1. Warehouse independence (`7788605`)

A customer's raw material can be held in two warehouses, and until now the screen, its export and the printed lot card merged them into one balance with one running total.

- **Read-time key only.** The internal grouping key on the balance path (`GetStockTraceabilityQuery`) now includes `WarehouseId` for raw and ready goods (`R|msg|item|customer|warehouse`, `D|order|item|warehouse`). Nothing is stored, and no lot, roll or document numbering is introduced: it is a grouping key for aggregation, not lot tracking.
- **One movement classifier.** `StockMovementKinds` is now the single definition of raw / ready / material movements, so the balance screen, the export, the printed card and the item card cannot disagree.
- **Item card per warehouse** — `GET /api/inventory/item-card`: opening balance, period movements, closing balance, with KG and Meter kept independent. It rejects a foreign identifier, requires `inventory.view`, and blocks a unit that is not configured for the item instead of printing a misleading zero.
- **Balance report rebuilt** — `GET /api/reports/warehouse-balances`: one row set and one total per warehouse, followed by a grand total **per ledger and per unit**, so the report never merges the four ledgers into a single number.
- **One warehouse screen instead of four tabs** — `/warehouse/:warehouseId` with a dropdown entry point: overview, balances, movements, item card, documents and reports all read the selected warehouse. The old `?tab=` links still resolve. The sidebar now has a single "warehouses" entry.
- **Unchanged on purpose:** receiving, issuing, the production cycle, customer ownership, the four warehouses (`WH-RAW` / `WH-RDY` / `WH-MAT` / `WH-SUP`), the ready/waste/separates/defect classifications, and the delivery path, which still resolves the single Ready Goods warehouse. **No migration, no schema change.**

## 2. Bundle split per route (`40ed12d`)

- 48 routes are loaded with `React.lazy` behind `Suspense`; the shell keeps its own boundary around the outlet so the sidebar never unmounts during a page load, and the fallback is a translated status line, not a spinner that flashes.
- Framework packages every route needs (react, react-dom, router, React Query, axios) go to a named vendor chunk with a separator-anchored pattern — without it `react` would also match `react-smooth` and drag Recharts out of the dashboard chunk.
- **Measured:** entry `1.55 MB / 387 kB gzip` → `197 kB / 53 kB gzip` plus a `340 kB` vendor chunk, 68 chunks total; the build no longer warns about a chunk over 500 kB.

## Carried commits

- `e2eec19` — `appsettings.Example.json` and the mandatory `Jwt__Key` startup step documented; separates page moved onto the shared design tokens.
- `bd65a17` — React 19.3.1 with matching types, `qrcode.react` 4.x, `lucide-react` 1.x; no source changes required.
- `dadf250` — الطباعة modelled as a repeatable printing run with its own code, not as operation twenty-two of the approved twenty-one.
- `03f04b9` — waste report as a computed loss (`GET /api/reports/waste`) through a single `WasteMath` definition; posts no ledger row.
- `011c6cd` — `tools/run-warehouse-demo.sh` + `tools/warehouse-demo-seed.mjs`: starts the API on a real SQL Server, posts real stock through the public API and prints the figures the warehouse screens must show.

## Verification

| Check | Result |
|---|---|
| `dotnet build DyeHouseERP.sln` | 0 warnings, 0 errors |
| `dotnet test` | **999/999** passing (7 new warehouse-independence tests, plus waste-report and printing-run tests) |
| `npx tsc -b` | clean |
| `npm run build` | passes, no chunk over 500 kB |
| `npx eslint .` | 0 errors (9 pre-existing `react-refresh/only-export-components` warnings) |
| `node tools/i18n-parity-check.cjs` | 0 failures (ar 1090 / en 1090) |
| `node --check` / `sh -n` on the new tools | pass |

Not verified yet: the demo script has not been run against a real SQL Server instance in this environment (no database available), so no warehouse screenshots are attached. The figures it posts are printed by the script itself and should be checked on first run.

## Risks / reviewer notes

- Low risk to data: no migration, nothing stored, all changes are read-time projections plus one new read endpoint and one rebuilt report.
- Reports and exports change shape (per-warehouse rows and totals) — intended, and the reason for the PR.
- The bundle split changes code-splitting only; route URLs and the layout are unchanged.

Generated with Codebuff 🤖
Co-Authored-By: Codebuff <noreply@codebuff.com>
