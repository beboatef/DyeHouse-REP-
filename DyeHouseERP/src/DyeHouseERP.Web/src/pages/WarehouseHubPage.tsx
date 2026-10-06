import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { CustomersApi, InventoryLedgerApi, RawMessagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ReportsExports } from "@/api/exports";
import { useI18n } from "@/i18n";
import RawMessagesPage from "@/pages/RawMessagesPage";
import RawExternalReleasesPage from "@/pages/RawExternalReleasesPage";
import CustomerTransfersPage from "@/pages/CustomerTransfersPage";
import StockAdjustmentsPage from "@/pages/StockAdjustmentsPage";
import WarehousesPage from "@/pages/WarehousesPage";

/**
 * The single Warehouse module (spec section 14). Receipts, issues, returns,
 * transfers, adjustments, external processing, movements, balances and reports
 * are TABS here instead of separate top-level sidebar entries. Each tab reuses
 * the existing, already-working screen rather than duplicating it.
 */

type TabKey = "balances" | "receipts" | "issues" | "transfers" | "adjustments" | "movements" | "master";

const tabs: { key: TabKey; labelKey: string }[] = [
  { key: "balances", labelKey: "wh.tab.balances" },
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

  const select = (key: TabKey) => {
    setTab(key);
    setParams({ tab: key }, { replace: true });
  };

  return (
    <>
      <PageHeader title={t("nav.warehouseHub")} subtitle={t("wh.subtitle")} />
      <div className="flex flex-wrap gap-2 mb-5">
        {tabs.map((item) => (
          <button
            key={item.key}
            onClick={() => select(item.key)}
            className={`rounded-lg px-3.5 py-2 text-[13px] font-semibold transition-colors ${
              tab === item.key
                ? "bg-slate-900 text-white"
                : "bg-white text-slate-600 border border-slate-200 hover:bg-slate-100"
            }`}
          >
            {t(item.labelKey)}
          </button>
        ))}
      </div>

      {tab === "balances" && <BalancesTab />}
      {tab === "receipts" && <RawMessagesPage />}
      {tab === "issues" && <RawExternalReleasesPage />}
      {tab === "transfers" && <CustomerTransfersPage />}
      {tab === "adjustments" && <StockAdjustmentsPage />}
      {tab === "movements" && <MovementsTab />}
      {tab === "master" && <WarehousesPage />}
    </>
  );
}

/** Customer-owned raw material balances, drill-down ready: filtered by customer/message/item. */
function BalancesTab() {
  const { t } = useI18n();
  const [customerId, setCustomerId] = useState("");
  const [search, setSearch] = useState("");
  const [negativesOnly, setNegativesOnly] = useState(false);

  const { data: customers } = useQuery({ queryKey: ["customers"], queryFn: () => CustomersApi.list() });

  const { data, isLoading } = useQuery({
    queryKey: ["warehouse-balances", customerId],
    queryFn: () => RawMessagesApi.list({ customerId: customerId || undefined, onlyWithBalance: true })
  });

  // A balance is negative when the ledger has posted more out than in. This screen only
  // REPORTS that state - it never clamps, corrects or hides it, because the ledger is the
  // source of truth and a negative balance is a real exception the factory has to see.
  const allRows = (data ?? []).flatMap((message) => message.lines.map((line) => ({ message, line })));

  const isNegative = (line: { remainingKg?: number | null; remainingMeter?: number | null }) =>
    (line.remainingKg ?? 0) < 0 || (line.remainingMeter ?? 0) < 0;

  const negativeCount = allRows.filter(({ line }) => isNegative(line)).length;

  const rows = allRows.filter(({ message, line }) => {
    if (negativesOnly && !isNegative(line)) return false;
    if (!search) return true;
    const term = search.toLowerCase();
    return (
      message.messageNumber.toLowerCase().includes(term) ||
      message.customerCode.toLowerCase().includes(term) ||
      line.itemCode.toLowerCase().includes(term)
    );
  });

  return (
    <>
      <Card className="p-4 mb-4 grid grid-cols-1 sm:grid-cols-3 gap-3 items-end">
        <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
          <option value="">{t("common.all")} - {t("common.customer")}</option>
          {customers?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.code} - {c.name}
            </option>
          ))}
        </Select>
        <Input
          placeholder={t("common.searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <label className="flex items-center gap-2 text-xs text-slate-600">
          <input
            type="checkbox"
            checked={negativesOnly}
            onChange={(e) => setNegativesOnly(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300"
          />
          {t("wh.negativesOnly")}
        </label>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-xs text-slate-500">{t("wh.balanceNote")}</span>
          {negativeCount > 0 && (
            <span className="text-xs font-semibold text-danger">
              {negativeCount} {t("wh.negativeBalances")}
            </span>
          )}
          {/* Same live balances the table shows: raw remaining per message line
              plus ready-goods per job order, all summed from the ledgers. */}
          <ExportButtons
            excel={{ label: t("common.export"), action: () => ReportsExports.warehouseBalances("excel")({ customerId: customerId || undefined }) }}
            pdf={{ label: t("common.pdf"), action: () => ReportsExports.warehouseBalances("pdf")({ customerId: customerId || undefined }) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>{t("common.print")}</Button>}
          />
        </div>
      </Card>

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>{t("fr.rawMessage")}</th>
              <th>{t("common.customer")}</th>
              <th>{t("common.item")}</th>
              <th>{t("common.quantity")} KG</th>
              <th>{t("common.quantity")} M</th>
              <th>{t("common.status")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {!isLoading && rows.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {rows.map(({ message, line }) => {
              const negative = isNegative(line);
              return (
                <tr
                  key={line.id}
                  className={`border-b border-gray-100 last:border-0 hover:bg-gray-50 ${negative ? "bg-danger/5" : ""}`}
                >
                  <td className="font-medium ltr-nums">{message.messageNumber}</td>
                  <td>
                    <span className="ltr-nums font-medium">{message.customerCode}</span>
                    <span className="text-gray-500"> · {message.customerName}</span>
                  </td>
                  <td>
                    <span className="ltr-nums font-medium">{line.itemCode}</span>
                    <span className="text-gray-500"> · {line.itemName}</span>
                  </td>
                  <td className={`px-4 py-3 ltr-nums ${(line.remainingKg ?? 0) < 0 ? "font-bold text-danger" : ""}`}>
                    {line.remainingKg ?? "—"}
                  </td>
                  <td className={`px-4 py-3 ltr-nums ${(line.remainingMeter ?? 0) < 0 ? "font-bold text-danger" : ""}`}>
                    {line.remainingMeter ?? "—"}
                  </td>
                  <td>
                    {negative ? (
                      <Badge tone="red">{t("wh.negative")}</Badge>
                    ) : (
                      <Badge tone={message.status === "Depleted" ? "gray" : "green"}>{message.status}</Badge>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>
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
      <Card className="p-4 mb-4 grid grid-cols-1 sm:grid-cols-4 gap-3 items-end">
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
        <div className="text-xs text-slate-500">
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
              <tr><td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {!isLoading && (data?.length ?? 0) === 0 && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {data?.map((m) => (
              <tr key={m.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="ltr-nums text-gray-600">{m.transactionDate.slice(0, 10)}</td>
                <td>
                  <div className="ltr-nums font-medium">{m.sourceDocumentNumber}</div>
                  <div className="text-2xs text-gray-400">{m.sourceDocumentType}</div>
                </td>
                <td className="ltr-nums">{m.customerCode}</td>
                <td className="ltr-nums">{m.itemCode}</td>
                <td className={`px-4 py-3 ltr-nums font-medium ${(m.signedQuantityKg ?? 0) < 0 ? "text-red-600" : "text-green-700"}`}>
                  {m.signedQuantityKg ?? "—"}
                </td>
                <td className={`px-4 py-3 ltr-nums font-medium ${(m.signedQuantityMeter ?? 0) < 0 ? "text-red-600" : "text-green-700"}`}>
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
