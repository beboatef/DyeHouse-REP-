import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  CustomersApi, InventoryLedgerApi, ItemsApi, KpisApi, ReadyGoodsApi, ReportsApi, SuppliersApi, WarehousesApi
} from "@/api/client";
import { AccountingExports, ReportsExports } from "@/api/exports";
import { useI18n } from "@/i18n";
import { PageHeader, Card, KpiCard, Button, Input, Select } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";

/**
 * The Reports module (spec sections 17, 18, 39).
 *
 * Every tab here is a *real* backend report with a live data source - there is
 * no static or hard-coded table. Each one can be read on screen, printed
 * (browser print of the styled report, same A4 rules as the document previews)
 * and downloaded as Excel or PDF, all through the same filters the table is
 * showing, so the file always matches the screen.
 */
type TabKey = "movements" | "balances" | "overrides" | "kpis";

export default function ReportsPage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabKey>("movements");
  const tabs: { key: TabKey; label: string }[] = [
    { key: "movements", label: "حركات المخزون" },
    { key: "balances", label: "أرصدة المخازن" },
    { key: "overrides", label: "تجاوز الرصيد السالب" },
    { key: "kpis", label: t("reports.kpisTab") }
  ];

  return (
    <>
      <PageHeader
        title="التقارير"
        subtitle="تقارير حقيقية من سجل الحركات وسجل الأرصدة - كل تقرير قابل للطباعة والتنزيل Excel و PDF"
      />
      <div className="tabs mb-6">
        {tabs.map((x) => (
          <button
            key={x.key}
            onClick={() => setTab(x.key)}
            className={`tab ${tab === x.key ? "tab-active" : ""}`}
          >
            {x.label}
          </button>
        ))}
      </div>

      {tab === "movements" && <MovementsReport />}
      {tab === "balances" && <BalancesReport />}
      {tab === "overrides" && <OverridesReport />}
      {tab === "kpis" && <KpisTab />}
    </>
  );
}

/** Common filter bar + the shared print stylesheet for report screens. */
function ReportFrame({
  children, toolbar
}: {
  children: React.ReactNode;
  toolbar: React.ReactNode;
}) {
  return (
    <>
      {toolbar}
      <Card className="print-page">{children}</Card>
      <style>{`
        @media print {
          .no-print { display: none !important; }
          body { background: white !important; }
          .print-page { box-shadow: none !important; border: none !important; }
          @page { size: A4 landscape; margin: 10mm; }
        }
      `}</style>
    </>
  );
}

const num = (value: number | null | undefined) => (value == null ? "" : value.toLocaleString("en-GB"));

// ---------------------------------------------------------------- movements

function MovementsReport() {
  const [filters, setFilters] = useState({ customerId: "", itemId: "", warehouseId: "", from: "", to: "" });

  const { data: customers } = useQuery({ queryKey: ["customers", ""], queryFn: () => CustomersApi.list() });
  const { data: items } = useQuery({ queryKey: ["items", ""], queryFn: () => ItemsApi.list() });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses"], queryFn: () => WarehousesApi.list() });

  const params = {
    customerId: filters.customerId || undefined,
    itemId: filters.itemId || undefined,
    warehouseId: filters.warehouseId || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined
  };

  const { data, isLoading } = useQuery({
    queryKey: ["inventory-movements", filters],
    queryFn: () => InventoryLedgerApi.movements({ ...params, limit: 5000 })
  });

  return (
    <ReportFrame
      toolbar={
        <div className="no-print flex flex-wrap items-end gap-3 mb-4">
          <div className="w-48">
            <label className="field-label">العميل</label>
            <Select value={filters.customerId} onChange={(e) => setFilters({ ...filters, customerId: e.target.value })}>
              <option value="">الكل</option>
              {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
            </Select>
          </div>
          <div className="w-48">
            <label className="field-label">الصنف</label>
            <Select value={filters.itemId} onChange={(e) => setFilters({ ...filters, itemId: e.target.value })}>
              <option value="">الكل</option>
              {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
            </Select>
          </div>
          <div className="w-48">
            <label className="field-label">المخزن</label>
            <Select value={filters.warehouseId} onChange={(e) => setFilters({ ...filters, warehouseId: e.target.value })}>
              <option value="">الكل</option>
              {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.code} - {w.name}</option>)}
            </Select>
          </div>
          <div><label className="field-label">من</label>
            <Input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} /></div>
          <div><label className="field-label">إلى</label>
            <Input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} /></div>
          <ExportButtons
            excel={{ action: () => ReportsExports.inventoryMovements.excel(params) }}
            pdf={{ action: () => ReportsExports.inventoryMovements.pdf(params) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>}
          />
        </div>
      }
    >
      <table className="table">
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>المستند</th>
            <th>النوع</th>
            <th>المخزن</th>
            <th>العميل</th>
            <th>الصنف</th>
            <th>الرسالة</th>
            <th>أمر التشغيل</th>
            <th>كجم</th>
            <th>متر</th>
            <th>المستخدم</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && <tr><td colSpan={11} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
          {!isLoading && data?.length === 0 && (
            <tr><td colSpan={11} className="px-4 py-6 text-center text-ink-subtle">لا توجد حركات مطابقة</td></tr>
          )}
          {data?.map((m) => (
            <tr key={m.id} className="border-b border-line last:border-0">
              <td className="ltr-nums">{new Date(m.transactionDate).toLocaleDateString("en-GB")}</td>
              <td className="ltr-nums">{m.sourceDocumentNumber}</td>
              <td>{m.sourceDocumentType}</td>
              <td>{m.warehouseName}</td>
              <td>{m.customerCode}</td>
              <td>{m.itemCode}</td>
              <td className="ltr-nums">{m.messageNumber ?? "-"}</td>
              <td className="ltr-nums">{m.orderNumber ?? "-"}</td>
              <td className="ltr-nums">{num(m.quantityKg)}</td>
              <td className="ltr-nums">{num(m.quantityMeter)}</td>
              <td>{m.createdBy}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ReportFrame>
  );
}

// ----------------------------------------------------------------- balances

function BalancesReport() {
  const [customerId, setCustomerId] = useState("");
  const { data: customers } = useQuery({ queryKey: ["customers", ""], queryFn: () => CustomersApi.list() });

  const { data: ready, isLoading } = useQuery({
    queryKey: ["ready-goods-balance", customerId],
    queryFn: () => ReadyGoodsApi.balance({ customerId: customerId || undefined })
  });

  return (
    <ReportFrame
      toolbar={
        <div className="no-print flex flex-wrap items-end gap-3 mb-4">
          <div className="w-56">
            <label className="field-label">العميل</label>
            <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
              <option value="">الكل</option>
              {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
            </Select>
          </div>
          <ExportButtons
            excel={{ action: () => ReportsExports.warehouseBalances("excel")({ customerId: customerId || undefined }) }}
            pdf={{ action: () => ReportsExports.warehouseBalances("pdf")({ customerId: customerId || undefined }) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>}
          />
        </div>
      }
    >
      <div className="p-4 text-sm text-ink-muted border-b border-line">
        رصيد الخام لكل رسالة استلام + رصيد الجاهز لكل أمر تشغيل. الأرصدة مُحتسبة من دفتر الحركات
        وليست حقولًا مخزَّنة، فلا يمكن أن تنحرف عن Reality.
      </div>
      <table className="table">
        <thead>
          <tr>
            <th>أمر التشغيل</th>
            <th>العميل</th>
            <th>الصنف</th>
            <th>اللون</th>
            <th>كجم</th>
            <th>متر</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
          {!isLoading && ready?.length === 0 && (
            <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">لا توجد أرصدة جاهزة</td></tr>
          )}
          {ready?.map((r) => (
            <tr key={r.productionOrderId} className="border-b border-line last:border-0">
              <td className="ltr-nums">{r.productionOrderNumber}</td>
              <td>{r.customerCode} - {r.customerName}</td>
              <td>{r.itemCode} - {r.itemName}</td>
              <td>{r.color ?? "-"}</td>
              <td className="ltr-nums">{num(r.remainingKg)}</td>
              <td className="ltr-nums">{num(r.remainingMeter)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ReportFrame>
  );
}

// ---------------------------------------------------------------- overrides

function OverridesReport() {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const params = { from: from || undefined, to: to || undefined };

  const { data: overrides, isLoading } = useQuery({
    queryKey: ["negative-stock-overrides", from, to],
    queryFn: ReportsApi.negativeStockOverrides
  });

  return (
    <ReportFrame
      toolbar={
        <div className="no-print flex flex-wrap items-end gap-3 mb-4">
          <div><label className="field-label">من</label>
            <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
          <div><label className="field-label">إلى</label>
            <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
          <ExportButtons
            excel={{ action: () => ReportsExports.negativeStockOverrides.excel(params) }}
            pdf={{ action: () => ReportsExports.negativeStockOverrides.pdf(params) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>}
          />
        </div>
      }
    >
      <table className="table">
        <thead><tr>
          <th>التاريخ</th><th>الرسالة</th>
          <th>العميل</th><th>الصنف</th>
          <th>المطلوب</th><th>قبل</th>
          <th>بعد</th><th>السبب</th>
          <th>طلبه</th><th>اعتمده</th>
        </tr></thead>
        <tbody>
          {isLoading && <tr><td colSpan={10} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
          {!isLoading && overrides?.length === 0 && (
            <tr><td colSpan={10} className="px-4 py-6 text-center text-ink-subtle">لا توجد تجاوزات مسجلة</td></tr>
          )}
          {overrides?.map((o) => (
            <tr key={o.id} className="border-b border-line last:border-0">
              <td className="ltr-nums">{new Date(o.approvedAtUtc).toLocaleString("en-GB")}</td>
              <td className="ltr-nums">{o.messageNumber}</td>
              <td>{o.customerCode}</td>
              <td>{o.itemCode}</td>
              <td className="ltr-nums">{num(o.requestedQuantity)}</td>
              <td className="ltr-nums">{num(o.balanceBefore)}</td>
              <td className="ltr-nums">{num(o.resultingBalance)}</td>
              <td>{o.reason}</td>
              <td>{o.requestedBy}</td>
              <td>{o.approvedBy}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ReportFrame>
  );
}
// ---------------------------------------------------------------- KPIs (spec Part 1)

function monthStart(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-01`;
}

function todayStr(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

function fmtDate(value: string | null): string {
  if (!value) return "";
  return new Date(value).toLocaleDateString("en-GB");
}

function KpisTab() {
  const { t } = useI18n();
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(todayStr());
  const params = { from: from || undefined, to: to || undefined };

  const summary = useQuery({ queryKey: ["kpis-summary", from, to], queryFn: () => KpisApi.summary(params), retry: false });
  const waste = useQuery({ queryKey: ["kpis-waste", from, to], queryFn: () => KpisApi.wasteRate(params), retry: false });
  const lead = useQuery({ queryKey: ["kpis-lead", from, to], queryFn: () => KpisApi.leadTime(params), retry: false });
  const util = useQuery({ queryKey: ["kpis-util", from, to], queryFn: () => KpisApi.machineUtilisation(params), retry: false });
  const throughput = useQuery({ queryKey: ["kpis-throughput", from, to], queryFn: () => KpisApi.throughput(params), retry: false });
  const exposure = useQuery({ queryKey: ["kpis-exposure"], queryFn: () => KpisApi.customerExposure(), retry: false });
  const cheques = useQuery({ queryKey: ["kpis-cheques"], queryFn: () => KpisApi.chequesDue(7), retry: false });
  const gov = useQuery({ queryKey: ["kpis-gov", from, to], queryFn: () => KpisApi.governance(params), retry: false });

  const anyError = [summary, waste, lead, util, throughput, exposure, cheques, gov].some((q) => q.isError);
  if (anyError) return null;

  return (
    <div className="flex flex-col gap-6">
      <ReportFrame
        toolbar={
          <div className="no-print flex flex-wrap items-end gap-3 mb-4">
            <div><label className="field-label">{t("kpi.from")}</label>
              <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
            <div><label className="field-label">{t("kpi.to")}</label>
              <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
            <ExportButtons extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>} />
          </div>
        }
      >
        {summary.data && (
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <KpiCard label={t("kpi.wasteRate")} value={summary.data.overallWasteRatePct == null ? "—" : `${summary.data.overallWasteRatePct}%`} />
            <KpiCard label={t("kpi.leadTime")} value={summary.data.averageLeadTimeDays ?? "—"} footnote={`${t("kpi.finishedOrders")}: ${summary.data.finishedOrders}`} />
            <KpiCard label={t("kpi.utilisation")} value={summary.data.overallUtilisationPct == null ? "—" : `${summary.data.overallUtilisationPct}%`} />
            <KpiCard label={t("kpi.throughput")} value={Math.round(summary.data.totalThroughputKg)} />
            <KpiCard label={t("kpi.exposure")} value={Math.round(summary.data.totalCustomerExposure)} />
            <KpiCard label={t("kpi.chequesDue")} value={summary.data.chequesDueCount} footnote={`${Math.round(summary.data.chequesDueTotal)}`} />
            <KpiCard label={t("kpi.overrides")} value={summary.data.overridesCount} />
            <KpiCard label={t("kpi.reopenings")} value={summary.data.reopeningsCount} />
          </div>
        )}

        {waste.data && waste.data.orders.length > 0 && (
          <KpiTable title={t("kpi.wasteRate")} headers={[t("kpi.orderNo"), t("kpi.rawKg"), t("kpi.wasteKg"), t("kpi.ratePct")]}>
            {waste.data.orders.map((o) => (
              <tr key={o.productionOrderId} className="border-b border-line last:border-0">
                <td className="ltr-nums font-medium">{o.orderNumber}</td>
                <td className="ltr-nums">{num(o.rawKg)}</td>
                <td className="ltr-nums">{num(o.wasteKg)}</td>
                <td className="ltr-nums">{o.wasteRatePct == null ? "—" : `${o.wasteRatePct}%`}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {lead.data && lead.data.orders.length > 0 && (
          <KpiTable title={t("kpi.leadTime")} headers={[t("kpi.orderNo"), t("kpi.issueDate"), t("kpi.completionDate"), t("kpi.leadDays")]}>
            {lead.data.orders.map((o) => (
              <tr key={o.productionOrderId} className="border-b border-line last:border-0">
                <td className="ltr-nums font-medium">{o.orderNumber}</td>
                <td className="ltr-nums">{fmtDate(o.issueDate)}</td>
                <td className="ltr-nums">{fmtDate(o.completionDate)}</td>
                <td className="ltr-nums">{o.leadTimeDays}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {util.data && util.data.machines.length > 0 && (
          <KpiTable title={t("kpi.utilisation")} headers={[t("kpi.machine"), t("kpi.runningHours"), t("kpi.availableHours"), t("kpi.ratePct")]}>
            {util.data.machines.map((m) => (
              <tr key={m.machineId ?? m.machineCode} className="border-b border-line last:border-0">
                <td>{m.machineCode} - {m.machineName}</td>
                <td className="ltr-nums">{m.runningHours}</td>
                <td className="ltr-nums">{m.availableHours}</td>
                <td className="ltr-nums">{m.utilisationPct == null ? "—" : `${m.utilisationPct}%`}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {throughput.data && throughput.data.totalTransfers > 0 && (
          <KpiTable title={t("kpi.throughput")} headers={[t("kpi.stage"), t("kpi.transfers"), t("kpi.quantityKg")]}>
            {throughput.data.byStage.map((g) => (
              <tr key={g.code} className="border-b border-line last:border-0">
                <td>{g.code} - {g.name}</td>
                <td className="ltr-nums">{num(g.transfersCount)}</td>
                <td className="ltr-nums">{num(g.quantityKg)}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {exposure.data && exposure.data.customers.length > 0 && (
          <KpiTable title={t("kpi.exposure")} headers={[t("kpi.customer"), t("kpi.openBalance"), t("kpi.openInvoices"), t("kpi.oldestUnpaid"), t("kpi.ageDays")]}>
            {exposure.data.customers.map((c) => (
              <tr key={c.customerId} className="border-b border-line last:border-0">
                <td>{c.customerCode} - {c.customerName}</td>
                <td className="ltr-nums">{num(c.openBalance)}</td>
                <td className="ltr-nums">{num(c.openInvoicesTotal)} ({c.openInvoicesCount})</td>
                <td className="ltr-nums">{c.oldestUnpaidInvoiceNumber ?? "—"}</td>
                <td className="ltr-nums">{c.oldestUnpaidInvoiceAgeDays ?? "—"}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {cheques.data && cheques.data.cheques.length > 0 && (
          <KpiTable title={`${t("kpi.chequesDue")} (7 ${t("kpi.days")})`} headers={[t("kpi.chequeNo"), t("kpi.kind"), t("kpi.party"), t("kpi.dueDate"), t("kpi.amount"), t("kpi.status")]}>
            {cheques.data.cheques.map((c) => (
              <tr key={c.checkId} className="border-b border-line last:border-0">
                <td className="ltr-nums font-medium">{c.checkNumber}</td>
                <td>{c.kind}</td>
                <td>{c.partyCode} - {c.partyName}</td>
                <td className="ltr-nums">{fmtDate(c.dueDate)}</td>
                <td className="ltr-nums">{num(c.amount)}</td>
                <td>{c.status}</td>
              </tr>
            ))}
          </KpiTable>
        )}

        {gov.data && (gov.data.overrides.length > 0 || gov.data.reopenings.length > 0) && (
          <>
            {gov.data.overrides.length > 0 && (
              <KpiTable title={t("kpi.overrides")} headers={[t("kpi.messageNo"), t("kpi.customer"), t("kpi.item"), t("kpi.reason"), t("kpi.approvedBy")]}>
                {gov.data.overrides.map((o) => (
                  <tr key={o.id} className="border-b border-line last:border-0">
                    <td className="ltr-nums font-medium">{o.messageNumber}</td>
                    <td>{o.customerCode}</td>
                    <td>{o.itemCode}</td>
                    <td>{o.reason}</td>
                    <td>{o.approvedBy}</td>
                  </tr>
                ))}
              </KpiTable>
            )}
            {gov.data.reopenings.length > 0 && (
              <KpiTable title={t("kpi.reopenings")} headers={[t("kpi.period"), t("kpi.reopenedBy"), t("kpi.approvedBy")]}>
                {gov.data.reopenings.map((r) => (
                  <tr key={r.periodCloseId} className="border-b border-line last:border-0">
                    <td className="ltr-nums">{fmtDate(r.periodStart)} - {fmtDate(r.periodEnd)}</td>
                    <td>{r.reopenedBy ?? r.auditUserName}</td>
                    <td>{r.auditUserName}</td>
                  </tr>
                ))}
              </KpiTable>
            )}
          </>
        )}
      </ReportFrame>

      <AccountingExportCard />
    </div>
  );
}

function KpiTable({ title, headers, children }: { title: string; headers: string[]; children: React.ReactNode }) {
  return (
    <div className="mb-6">
      <h3 className="mb-2 text-sm font-bold text-ink">{title}</h3>
      <table className="table">
        <thead><tr>{headers.map((h) => <th key={h}>{h}</th>)}</tr></thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

// ---------------------------------------------------------------- accounting export (spec Part 2)

function AccountingExportCard() {
  const { t } = useI18n();
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(todayStr());
  const [set, setSet] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [supplierId, setSupplierId] = useState("");

  const { data: customers } = useQuery({ queryKey: ["customers-all"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: suppliers } = useQuery({ queryKey: ["suppliers-all"], queryFn: () => SuppliersApi.list({ activeOnly: true }) });

  const params = {
    from: from || undefined,
    to: to || undefined,
    set: set || undefined,
    customerId: customerId || undefined,
    supplierId: supplierId || undefined
  };
  const setOptions = [
    { value: "", label: t("acct.setAll") },
    { value: "customer-invoices", label: t("acct.setCustomerInvoices") },
    { value: "supplier", label: t("acct.setSupplier") },
    { value: "treasury", label: t("acct.setTreasury") },
    { value: "payroll", label: t("acct.setPayroll") },
    { value: "cheques", label: t("acct.setCheques") }
  ];

  return (
    <Card className="print-page">
      <div className="card-title mb-1">{t("acct.title")}</div>
      <p className="mb-4 text-sm text-ink-muted">{t("acct.subtitle")}</p>
      <div className="no-print flex flex-wrap items-end gap-3">
        <div><label className="field-label">{t("kpi.from")}</label>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
        <div><label className="field-label">{t("kpi.to")}</label>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
        <div className="w-48"><label className="field-label">{t("acct.set")}</label>
          <Select value={set} onChange={(e) => setSet(e.target.value)}>
            {setOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </Select>
        </div>
        <div className="w-48"><label className="field-label">{t("acct.customer")}</label>
          <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); if (e.target.value) setSupplierId(""); }}>
            <option value="">الكل</option>
            {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
          </Select>
        </div>
        <div className="w-48"><label className="field-label">{t("acct.supplier")}</label>
          <Select value={supplierId} onChange={(e) => { setSupplierId(e.target.value); if (e.target.value) setCustomerId(""); }}>
            <option value="">الكل</option>
            {suppliers?.map((s) => <option key={s.id} value={s.id}>{s.code} - {s.name}</option>)}
          </Select>
        </div>
        <Button variant="secondary" onClick={() => AccountingExports.csv(params)}>{t("acct.downloadCsv")}</Button>
        <Button variant="secondary" onClick={() => AccountingExports.xlsx(params)}>{t("acct.downloadXlsx")}</Button>
      </div>
    </Card>
  );
}
