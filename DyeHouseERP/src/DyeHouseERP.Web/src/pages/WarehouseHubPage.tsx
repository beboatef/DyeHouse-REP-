import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { InventoryLedgerApi } from "@/api/client";
import { PageHeader, Card, Button, Input } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ReportsExports } from "@/api/exports";
import StockTraceability from "@/components/StockTraceability";
import { useI18n } from "@/i18n";
import RawMessagesPage from "@/pages/RawMessagesPage";
import RawExternalReleasesPage from "@/pages/RawExternalReleasesPage";
import CustomerTransfersPage from "@/pages/CustomerTransfersPage";
import StockAdjustmentsPage from "@/pages/StockAdjustmentsPage";
import WarehousesPage from "@/pages/WarehousesPage";
import SeparatesPage from "@/pages/SeparatesPage";

/**
 * The single Warehouse module (spec section 14): every screen that reads or moves stock lives here as a
 * TAB, and each tab reuses the existing, already-working screen rather than duplicating it.
 *
 * The four balance tabs are the point of this module: raw material, ready goods, materials/chemicals and
 * the separations register. Each one shows a balance PER LOT - a receipt message, a job order or a
 * material in a warehouse - together with the LEDGER ROWS that balance is made of, so "what is in stock?"
 * and "why?" are answered on the same screen. Nothing is stored or cached: every figure is summed from
 * the append-only ledger when the screen asks, which is what makes it current by construction.
 */

type TabKey =
  | "balances" | "ready" | "materials" | "separates"
  | "receipts" | "issues" | "transfers" | "adjustments" | "movements" | "master";

const tabs: { key: TabKey; labelKey: string }[] = [
  { key: "balances", labelKey: "wh.tab.balances" },
  { key: "ready", labelKey: "wh.tab.readyBalances" },
  { key: "materials", labelKey: "wh.tab.materialBalances" },
  { key: "separates", labelKey: "nav.separates" },
  { key: "receipts", labelKey: "wh.tab.receipts" },
  { key: "issues", labelKey: "wh.tab.issues" },
  { key: "transfers", labelKey: "wh.tab.transfersFrom" },
  { key: "adjustments", labelKey: "wh.tab.adjustments" },
  { key: "movements", labelKey: "wh.tab.movements" },
  { key: "master", labelKey: "wh.tab.master" }
];

export default function WarehouseHubPage() {
  const { t } = useI18n();
  const [params, setParams] = useSearchParams();
  const initial = (params.get("tab") as TabKey) ?? "balances";
  const [tab, setTab] = useState<TabKey>(tabs.some((x) => x.key === initial) ? initial : "balances");

  useEffect(() => {
    const requested = (params.get("tab") as TabKey) ?? "balances";
    if (tabs.some((x) => x.key === requested)) {
      setTab(requested);
    }
  }, [params]);

  const select = (key: TabKey) => {
    setTab(key);
    setParams({ tab: key }, { replace: true });
  };

  return (
    <>
      <PageHeader title={t("nav.warehouseHub")} subtitle={t("wh.subtitle")} />
      <div className="tabs mb-5 flex-wrap">
        {tabs.map((item) => (
          <button
            key={item.key}
            onClick={() => select(item.key)}
            className={`tab ${tab === item.key ? "tab-active" : ""}`}
          >
            {t(item.labelKey)}
          </button>
        ))}
      </div>

      {/* Balances: one tab per warehouse, all three on the same principles - a balance per lot, the
          movements behind it, KG and Meter in their own columns, and nothing merged or converted. */}
      {tab === "balances" && (
        <>
          <p className="mb-3 text-xs text-ink-muted">{t("wh.balanceNote")}</p>
          <StockTraceability scope="raw" />
        </>
      )}
      {tab === "ready" && <StockTraceability scope="ready" />}
      {tab === "materials" && <StockTraceability scope="materials" />}
      {tab === "separates" && <SeparatesPage />}
      {tab === "receipts" && <RawMessagesPage />}
      {tab === "issues" && <RawExternalReleasesPage />}
      {tab === "transfers" && <CustomerTransfersPage />}
      {tab === "adjustments" && <StockAdjustmentsPage />}
      {tab === "movements" && <MovementsTab />}
      {tab === "master" && <WarehousesPage />}
    </>
  );
}

/** The inventory ledger itself: every posted stock movement with its source document and user. */
function MovementsTab() {
  const { t } = useI18n();
  const [filters, setFilters] = useState({ from: "", to: "", warehouseId: "" });

  const { data, isLoading } = useQuery({
    queryKey: ["inventory-movements", filters],
    queryFn: () =>
      InventoryLedgerApi.movements({
        from: filters.from || undefined,
        to: filters.to || undefined,
        warehouseId: filters.warehouseId || undefined,
        limit: 300
      })
  });

  return (
    <>
      <Card className="card-pad mb-4 grid grid-cols-1 sm:grid-cols-4 gap-3 items-end">
        <div>
          <label className="field-label">{t("common.from")}</label>
          <Input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} />
        </div>
        <div>
          <label className="field-label">{t("common.to")}</label>
          <Input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} />
        </div>
        <div className="flex flex-wrap gap-2">
          <ExportButtons
            excel={{ label: t("common.export"), action: () => ReportsExports.inventoryMovements.excel({
              from: filters.from || undefined,
              to: filters.to || undefined,
              warehouseId: filters.warehouseId || undefined
            }) }}
            pdf={{ label: t("common.pdf"), action: () => ReportsExports.inventoryMovements.pdf({
              from: filters.from || undefined,
              to: filters.to || undefined,
              warehouseId: filters.warehouseId || undefined
            }) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>{t("common.print")}</Button>}
          />
        </div>
        <div className="text-xs text-ink-muted">
          {t("wh.tab.movements")}: {data?.length ?? 0}
        </div>
      </Card>

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>{t("common.date")}</th>
              <th>{t("fr.rawMessage")} / {t("fr.jobOrder")}</th>
              <th>{t("common.customer")}</th>
              <th>{t("common.item")}</th>
              <th>KG (±)</th>
              <th>M (±)</th>
              <th>{t("common.user")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-ink-subtle">{t("common.loading")}</td></tr>
            )}
            {!isLoading && (data?.length ?? 0) === 0 && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty")}</td></tr>
            )}
            {data?.map((m) => (
              <tr key={m.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="ltr-nums text-ink-muted">{m.transactionDate.slice(0, 10)}</td>
                <td>
                  <div className="ltr-nums font-medium">{m.sourceDocumentNumber}</div>
                  <div className="text-2xs text-ink-subtle">{m.sourceDocumentType}</div>
                </td>
                <td className="ltr-nums">{m.customerCode}</td>
                <td className="ltr-nums">{m.itemCode}</td>
                <td className={`px-4 py-3 ltr-nums font-medium ${(m.signedQuantityKg ?? 0) < 0 ? "text-danger-ink" : "text-success-ink"}`}>
                  {m.signedQuantityKg ?? "—"}
                </td>
                <td className={`px-4 py-3 ltr-nums font-medium ${(m.signedQuantityMeter ?? 0) < 0 ? "text-danger-ink" : "text-success-ink"}`}>
                  {m.signedQuantityMeter ?? "—"}
                </td>
                <td className="text-ink-muted">{m.createdBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
