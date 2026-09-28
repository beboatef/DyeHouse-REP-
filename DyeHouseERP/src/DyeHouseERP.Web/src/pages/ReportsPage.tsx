import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  CustomersApi, InventoryLedgerApi, ItemsApi, ReadyGoodsApi, ReportsApi, WarehousesApi
} from "@/api/client";
import { ReportsExports } from "@/api/exports";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";
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
type TabKey = "movements" | "balances" | "overrides";

const tabs: { key: TabKey; label: string }[] = [
  { key: "movements", label: "حركات المخزون" },
  { key: "balances", label: "أرصدة المخازن" },
  { key: "overrides", label: "تجاوز الرصيد السالب" }
];

export default function ReportsPage() {
  const [tab, setTab] = useState<TabKey>("movements");

  return (
    <>
      <PageHeader
        title="التقارير"
        subtitle="تقارير حقيقية من سجل الحركات وسجل الأرصدة - كل تقرير قابل للطباعة والتنزيل Excel و PDF"
      />
      <div className="flex gap-2 mb-6 border-b border-gray-200">
        {tabs.map((x) => (
          <button
            key={x.key}
            onClick={() => setTab(x.key)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
              tab === x.key ? "border-brand-600 text-brand-700" : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            {x.label}
          </button>
        ))}
      </div>

      {tab === "movements" && <MovementsReport />}
      {tab === "balances" && <BalancesReport />}
      {tab === "overrides" && <OverridesReport />}
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
            <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
            <Select value={filters.customerId} onChange={(e) => setFilters({ ...filters, customerId: e.target.value })}>
              <option value="">الكل</option>
              {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
            </Select>
          </div>
          <div className="w-48">
            <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
            <Select value={filters.itemId} onChange={(e) => setFilters({ ...filters, itemId: e.target.value })}>
              <option value="">الكل</option>
              {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
            </Select>
          </div>
          <div className="w-48">
            <label className="block text-xs font-medium text-gray-600 mb-1">المخزن</label>
            <Select value={filters.warehouseId} onChange={(e) => setFilters({ ...filters, warehouseId: e.target.value })}>
              <option value="">الكل</option>
              {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.code} - {w.name}</option>)}
            </Select>
          </div>
          <div><label className="block text-xs font-medium text-gray-600 mb-1">من</label>
            <Input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} /></div>
          <div><label className="block text-xs font-medium text-gray-600 mb-1">إلى</label>
            <Input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} /></div>
          <ExportButtons
            excel={{ action: () => ReportsExports.inventoryMovements.excel(params) }}
            pdf={{ action: () => ReportsExports.inventoryMovements.pdf(params) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>}
          />
        </div>
      }
    >
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">التاريخ</th>
            <th className="text-start px-4 py-3 font-medium">المستند</th>
            <th className="text-start px-4 py-3 font-medium">النوع</th>
            <th className="text-start px-4 py-3 font-medium">المخزن</th>
            <th className="text-start px-4 py-3 font-medium">العميل</th>
            <th className="text-start px-4 py-3 font-medium">الصنف</th>
            <th className="text-start px-4 py-3 font-medium">الرسالة</th>
            <th className="text-start px-4 py-3 font-medium">أمر التشغيل</th>
            <th className="text-start px-4 py-3 font-medium">كجم</th>
            <th className="text-start px-4 py-3 font-medium">متر</th>
            <th className="text-start px-4 py-3 font-medium">المستخدم</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && <tr><td colSpan={11} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
          {!isLoading && data?.length === 0 && (
            <tr><td colSpan={11} className="px-4 py-6 text-center text-gray-400">لا توجد حركات مطابقة</td></tr>
          )}
          {data?.map((m) => (
            <tr key={m.id} className="border-b border-gray-100 last:border-0">
              <td className="px-4 py-2 ltr-nums">{new Date(m.transactionDate).toLocaleDateString("en-GB")}</td>
              <td className="px-4 py-2 ltr-nums">{m.sourceDocumentNumber}</td>
              <td className="px-4 py-2">{m.sourceDocumentType}</td>
              <td className="px-4 py-2">{m.warehouseName}</td>
              <td className="px-4 py-2">{m.customerCode}</td>
              <td className="px-4 py-2">{m.itemCode}</td>
              <td className="px-4 py-2 ltr-nums">{m.messageNumber ?? "-"}</td>
              <td className="px-4 py-2 ltr-nums">{m.orderNumber ?? "-"}</td>
              <td className="px-4 py-2 ltr-nums">{num(m.quantityKg)}</td>
              <td className="px-4 py-2 ltr-nums">{num(m.quantityMeter)}</td>
              <td className="px-4 py-2">{m.createdBy}</td>
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
            <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
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
      <div className="p-4 text-sm text-gray-600 border-b border-gray-100">
        رصيد الخام لكل رسالة استلام + رصيد الجاهز لكل أمر تشغيل. الأرصدة مُحتسبة من دفتر الحركات
        وليست حقولًا مخزَّنة، فلا يمكن أن تنحرف عن Reality.
      </div>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">أمر التشغيل</th>
            <th className="text-start px-4 py-3 font-medium">العميل</th>
            <th className="text-start px-4 py-3 font-medium">الصنف</th>
            <th className="text-start px-4 py-3 font-medium">اللون</th>
            <th className="text-start px-4 py-3 font-medium">كجم</th>
            <th className="text-start px-4 py-3 font-medium">متر</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
          {!isLoading && ready?.length === 0 && (
            <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد أرصدة جاهزة</td></tr>
          )}
          {ready?.map((r) => (
            <tr key={r.productionOrderId} className="border-b border-gray-100 last:border-0">
              <td className="px-4 py-2 ltr-nums">{r.productionOrderNumber}</td>
              <td className="px-4 py-2">{r.customerCode} - {r.customerName}</td>
              <td className="px-4 py-2">{r.itemCode} - {r.itemName}</td>
              <td className="px-4 py-2">{r.color ?? "-"}</td>
              <td className="px-4 py-2 ltr-nums">{num(r.remainingKg)}</td>
              <td className="px-4 py-2 ltr-nums">{num(r.remainingMeter)}</td>
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
          <div><label className="block text-xs font-medium text-gray-600 mb-1">من</label>
            <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
          <div><label className="block text-xs font-medium text-gray-600 mb-1">إلى</label>
            <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
          <ExportButtons
            excel={{ action: () => ReportsExports.negativeStockOverrides.excel(params) }}
            pdf={{ action: () => ReportsExports.negativeStockOverrides.pdf(params) }}
            extra={<Button variant="secondary" onClick={() => window.print()}>طباعة</Button>}
          />
        </div>
      }
    >
      <table className="w-full text-sm">
        <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
          <th className="text-start px-4 py-3 font-medium">التاريخ</th><th className="text-start px-4 py-3 font-medium">الرسالة</th>
          <th className="text-start px-4 py-3 font-medium">العميل</th><th className="text-start px-4 py-3 font-medium">الصنف</th>
          <th className="text-start px-4 py-3 font-medium">المطلوب</th><th className="text-start px-4 py-3 font-medium">قبل</th>
          <th className="text-start px-4 py-3 font-medium">بعد</th><th className="text-start px-4 py-3 font-medium">السبب</th>
          <th className="text-start px-4 py-3 font-medium">طلبه</th><th className="text-start px-4 py-3 font-medium">اعتمده</th>
        </tr></thead>
        <tbody>
          {isLoading && <tr><td colSpan={10} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
          {!isLoading && overrides?.length === 0 && (
            <tr><td colSpan={10} className="px-4 py-6 text-center text-gray-400">لا توجد تجاوزات مسجلة</td></tr>
          )}
          {overrides?.map((o) => (
            <tr key={o.id} className="border-b border-gray-100 last:border-0">
              <td className="px-4 py-2 ltr-nums">{new Date(o.approvedAtUtc).toLocaleString("en-GB")}</td>
              <td className="px-4 py-2 ltr-nums">{o.messageNumber}</td>
              <td className="px-4 py-2">{o.customerCode}</td>
              <td className="px-4 py-2">{o.itemCode}</td>
              <td className="px-4 py-2 ltr-nums">{num(o.requestedQuantity)}</td>
              <td className="px-4 py-2 ltr-nums">{num(o.balanceBefore)}</td>
              <td className="px-4 py-2 ltr-nums">{num(o.resultingBalance)}</td>
              <td className="px-4 py-2">{o.reason}</td>
              <td className="px-4 py-2">{o.requestedBy}</td>
              <td className="px-4 py-2">{o.approvedBy}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ReportFrame>
  );
}
