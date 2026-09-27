import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MaterialsApi,
  PurchasesApi,
  SuppliersApi,
  TreasuryAccountsApi,
  WarehousesApi
} from "@/api/client";
import type { MaterialUnit, PurchaseOrder, PurchaseOrderStatus, SupplierInvoiceStatus } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Purchases (spec section 35) inside the existing UI patterns: one page with tabs
 * for orders, receiving, supplier invoices, payments and supplier balances.
 *
 * The flows follow the ledger rules already used everywhere else in the ERP:
 *   - a purchase order has no stock or accounting effect at all;
 *   - a goods receipt is the only action that moves material stock;
 *   - posting a supplier invoice is the only action that creates a payable;
 *   - a supplier payment moves cash and reduces the payable, and cancelling a
 *     posted invoice posts an equal-and-opposite ledger row instead of deleting.
 */

type TabKey = "orders" | "receipts" | "invoices" | "payments" | "balances";

const tabs: { key: TabKey; labelKey: string }[] = [
  { key: "orders", labelKey: "pur.tab.orders" },
  { key: "receipts", labelKey: "pur.tab.receipts" },
  { key: "invoices", labelKey: "pur.tab.invoices" },
  { key: "payments", labelKey: "pur.tab.payments" },
  { key: "balances", labelKey: "pur.tab.balances" }
];

const units: MaterialUnit[] = ["KG", "Gram", "Liter"];

const orderTone: Record<PurchaseOrderStatus, "gray" | "yellow" | "green" | "blue" | "red"> = {
  Draft: "gray",
  Submitted: "yellow",
  Approved: "blue",
  PartiallyReceived: "yellow",
  Received: "green",
  Cancelled: "red"
};

const invoiceTone: Record<SupplierInvoiceStatus, "gray" | "green" | "red"> = {
  Draft: "gray",
  Posted: "green",
  Cancelled: "red"
};

const money = (v: number) => v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function PurchasesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const initial = (params.get("tab") as TabKey) ?? "orders";
  const [tab, setTab] = useState<TabKey>(tabs.some((x) => x.key === initial) ? initial : "orders");

  const select = (key: TabKey) => {
    setTab(key);
    setParams({ tab: key }, { replace: true });
  };

  return (
    <>
      <PageHeader title={t("pur.title")} subtitle={t("pur.subtitle")} />

      <div className="flex flex-wrap gap-2 mb-5">
        {tabs.map((x) => (
          <Button key={x.key} variant={tab === x.key ? "primary" : "secondary"} onClick={() => select(x.key)}>
            {t(x.labelKey)}
          </Button>
        ))}
      </div>

      {tab === "orders" && <OrdersTab onChanged={() => qc.invalidateQueries()} />}
      {tab === "receipts" && <ReceiptsTab />}
      {tab === "invoices" && <InvoicesTab />}
      {tab === "payments" && <PaymentsTab />}
      {tab === "balances" && <BalancesTab />}
    </>
  );
}

// ------------------------------------------------------------------- orders

function OrdersTab({ onChanged }: { onChanged: () => void }) {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"" | PurchaseOrderStatus>("");

  const [supplierId, setSupplierId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [orderDate, setOrderDate] = useState(new Date().toISOString().slice(0, 10));
  const [expectedDeliveryDate, setExpectedDeliveryDate] = useState("");
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<{ materialId: string; quantity: string; unit: MaterialUnit; unitPrice: string }[]>([
    { materialId: "", quantity: "", unit: "KG", unitPrice: "" }
  ]);

  const { data: suppliers } = useQuery({ queryKey: ["suppliers", "active"], queryFn: () => SuppliersApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses"], queryFn: () => WarehousesApi.list() });
  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: orders, isLoading } = useQuery({
    queryKey: ["purchase-orders", statusFilter],
    queryFn: () => PurchasesApi.orders({ status: statusFilter || undefined })
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["purchase-orders"] });
    qc.invalidateQueries({ queryKey: ["purchase-receipts"] });
    onChanged();
  };

  const createMutation = useMutation({
    mutationFn: () =>
      PurchasesApi.createOrder({
        orderDate,
        supplierId,
        warehouseId,
        expectedDeliveryDate: expectedDeliveryDate || undefined,
        notes: notes || undefined,
        lines: lines
          .filter((l) => l.materialId && Number(l.quantity) > 0)
          .map((l) => ({
            materialId: l.materialId,
            quantity: Number(l.quantity),
            unit: l.unit,
            unitPrice: Number(l.unitPrice || 0)
          }))
      }),
    onSuccess: () => {
      invalidate();
      setShowForm(false);
      setLines([{ materialId: "", quantity: "", unit: "KG", unitPrice: "" }]);
      setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const action = useMutation({
    mutationFn: (vars: { id: string; action: "submit" | "approve" }) =>
      vars.action === "submit" ? PurchasesApi.submitOrder(vars.id) : PurchasesApi.approveOrder(vars.id),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => PurchasesApi.cancelOrder(vars.id, vars.reason),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const removeLine = useMutation({
    mutationFn: (vars: { orderId: string; lineId: string }) => PurchasesApi.removeOrderLine(vars.orderId, vars.lineId),
    onSuccess: invalidate
  });

  const cancelOrder = (order: PurchaseOrder) => {
    const reason = window.prompt(t("pur.cancelReason"));
    if (!reason) return;
    cancelMutation.mutate({ id: order.id, reason });
  };

  return (
    <>
      <Card className="p-4 mb-4 flex flex-wrap items-end gap-3">
        <div className="w-52">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.status")}</label>
          <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as "" | PurchaseOrderStatus)}>
            <option value="">{t("common.all")}</option>
            {(["Draft", "Submitted", "Approved", "PartiallyReceived", "Received", "Cancelled"] as PurchaseOrderStatus[]).map((s) => (
              <option key={s} value={s}>{t(`pur.status.${s}`)}</option>
            ))}
          </Select>
        </div>
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pur.newOrder")}</Button>
      </Card>

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.supplier")}</label>
                <Select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} required>
                  <option value="">{t("common.select")}</option>
                  {suppliers?.map((s) => (
                    <option key={s.id} value={s.id}>{s.code} - {pick(s.nameAr, s.nameEn, s.name)}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.warehouse")}</label>
                <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                  <option value="">{t("common.selectWarehouse")}</option>
                  {warehouses?.map((w) => (
                    <option key={w.id} value={w.id}>{w.code} - {w.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.orderDate")}</label>
                <Input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} required />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.expectedDelivery")}</label>
                <Input type="date" value={expectedDeliveryDate} onChange={(e) => setExpectedDeliveryDate(e.target.value)} />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600 mb-2">{t("pur.tab.orders")}</label>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-5 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="block text-[11px] text-gray-500 mb-1">{t("pur.material")}</label>
                      <Select
                        value={line.materialId}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, materialId: e.target.value } : l)))}
                        required
                      >
                        <option value="">{t("common.select")}</option>
                        {materials?.map((m) => (
                          <option key={m.id} value={m.id}>{m.code} - {m.name} ({m.unit})</option>
                        ))}
                      </Select>
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("pur.quantity")}</label>
                      <Input
                        type="number" step="0.001" min="0" value={line.quantity}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, quantity: e.target.value } : l)))}
                      />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("common.unit")}</label>
                      <Select
                        value={line.unit}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, unit: e.target.value as MaterialUnit } : l)))}
                      >
                        {units.map((u) => (
                          <option key={u} value={u}>{u}</option>
                        ))}
                      </Select>
                    </div>
                    <div className="flex gap-2">
                      <div className="flex-1">
                        <label className="block text-[11px] text-gray-500 mb-1">{t("pur.unitPrice")}</label>
                        <Input
                          type="number" step="0.0001" min="0" value={line.unitPrice}
                          onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, unitPrice: e.target.value } : l)))}
                        />
                      </div>
                      {lines.length > 1 && (
                        <Button type="button" variant="secondary" onClick={() => setLines((p) => p.filter((_, i) => i !== idx))}>
                          {t("common.remove")}
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
              <Button
                type="button"
                variant="ghost"
                className="mt-2"
                onClick={() => setLines((p) => [...p, { materialId: "", quantity: "", unit: "KG", unitPrice: "" }])}
              >
                {t("pur.addLine")}
              </Button>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <div className="space-y-4">
        {isLoading && <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>}
        {!isLoading && orders?.length === 0 && <Card className="p-6 text-center text-gray-400">{t("common.empty")}</Card>}

        {orders?.map((o) => (
          <Card key={o.id} className="p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-bold ltr-nums">{o.orderNumber}</span>
                <Badge tone={orderTone[o.status]}>{t(`pur.status.${o.status}`)}</Badge>
                <span className="text-sm text-gray-600">{o.supplierCode} - {o.supplierName}</span>
                <span className="text-gray-400">·</span>
                <span className="text-sm text-gray-600">{o.warehouseName}</span>
                <span className="text-gray-400">·</span>
                <span className="text-sm text-gray-500 ltr-nums">{new Date(o.orderDate).toLocaleDateString("en-GB")}</span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold ltr-nums">{money(o.totalValue)}</span>
                <Button variant="ghost" onClick={() => setExpanded(expanded === o.id ? null : o.id)}>
                  {t("common.details")}
                </Button>
                {o.status === "Draft" && (
                  <Button variant="secondary" onClick={() => action.mutate({ id: o.id, action: "submit" })}>
                    {t("pur.submit")}
                  </Button>
                )}
                {o.status === "Submitted" && (
                  <Button variant="secondary" onClick={() => action.mutate({ id: o.id, action: "approve" })}>
                    {t("pur.approve")}
                  </Button>
                )}
                {o.status !== "Cancelled" && o.status !== "Received" && (
                  <Button variant="ghost" onClick={() => cancelOrder(o)}>{t("pur.cancelOrder")}</Button>
                )}
              </div>
            </div>

            {expanded === o.id && (
              <div className="mt-4">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-gray-400 text-xs border-b border-gray-100">
                      <th className="text-start py-2 font-medium">{t("pur.material")}</th>
                      <th className="text-start py-2 font-medium">{t("pur.quantity")}</th>
                      <th className="text-start py-2 font-medium">{t("pur.unitPrice")}</th>
                      <th className="text-start py-2 font-medium">{t("pur.lineValue")}</th>
                      <th className="text-start py-2 font-medium">{t("pur.receivedQty")}</th>
                      <th className="text-start py-2 font-medium">{t("pur.outstandingQty")}</th>
                      <th className="text-start py-2 font-medium" />
                    </tr>
                  </thead>
                  <tbody>
                    {o.lines.map((l) => (
                      <tr key={l.id} className="border-b border-gray-50 last:border-0">
                        <td className="py-2">{l.materialCode} - {l.materialName}</td>
                        <td className="py-2 ltr-nums">{l.quantity} {l.unit}</td>
                        <td className="py-2 ltr-nums">{money(l.unitPrice)}</td>
                        <td className="py-2 ltr-nums">{money(l.lineValue)}</td>
                        <td className="py-2 ltr-nums">{l.receivedQuantity}</td>
                        <td className="py-2 ltr-nums font-medium">{l.outstandingQuantity}</td>
                        <td className="py-2">
                          {(o.status === "Draft" || o.status === "Submitted") && l.receivedQuantity === 0 && (
                            <button
                              className="text-red-600 hover:underline text-xs font-semibold"
                              onClick={() => removeLine.mutate({ orderId: o.id, lineId: l.id })}
                            >
                              {t("pur.removeLine")}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}

// ----------------------------------------------------------------- receipts

function ReceiptsTab() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [supplierId, setSupplierId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [orderId, setOrderId] = useState("");
  const [receiptDate, setReceiptDate] = useState(new Date().toISOString().slice(0, 10));
  const [supplierDocumentNumber, setSupplierDocumentNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<{ materialId: string; quantity: string; unit: MaterialUnit; unitCost: string; orderLineId: string }[]>([
    { materialId: "", quantity: "", unit: "KG", unitCost: "", orderLineId: "" }
  ]);

  const { data: suppliers } = useQuery({ queryKey: ["suppliers", "active"], queryFn: () => SuppliersApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses"], queryFn: () => WarehousesApi.list() });
  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: approvedOrders } = useQuery({
    queryKey: ["purchase-orders", "approved"],
    queryFn: () => PurchasesApi.orders({ status: "Approved" })
  });
  const { data: partialOrders } = useQuery({
    queryKey: ["purchase-orders", "partial"],
    queryFn: () => PurchasesApi.orders({ status: "PartiallyReceived" })
  });
  const receivableOrders = [...(approvedOrders ?? []), ...(partialOrders ?? [])].filter(
    (o) => !supplierId || o.supplierId === supplierId
  );

  const { data: receipts, isLoading } = useQuery({ queryKey: ["purchase-receipts"], queryFn: () => PurchasesApi.receipts() });

  const selectedOrder = receivableOrders.find((o) => o.id === orderId);

  const createMutation = useMutation({
    mutationFn: () =>
      PurchasesApi.createReceipt({
        receiptDate,
        supplierId,
        warehouseId,
        purchaseOrderId: orderId || undefined,
        supplierDocumentNumber: supplierDocumentNumber || undefined,
        notes: notes || undefined,
        lines: lines
          .filter((l) => l.materialId && Number(l.quantity) > 0)
          .map((l) => ({
            materialId: l.materialId,
            quantity: Number(l.quantity),
            unit: l.unit,
            unitCost: Number(l.unitCost || 0),
            purchaseOrderLineId: l.orderLineId || undefined
          }))
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["purchase-receipts"] });
      qc.invalidateQueries({ queryKey: ["purchase-orders"] });
      qc.invalidateQueries({ queryKey: ["material-balances"] });
      setShowForm(false);
      setLines([{ materialId: "", quantity: "", unit: "KG", unitCost: "", orderLineId: "" }]);
      setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  // Picking an order line pre-fills the material, unit and remaining quantity so
  // the user never has to re-enter what the approved order already says.
  const applyOrder = (id: string) => {
    setOrderId(id);
    const order = receivableOrders.find((o) => o.id === id);
    if (!order) return;
    setSupplierId(order.supplierId);
    setWarehouseId(order.warehouseId);
    setLines(
      order.lines
        .filter((l) => l.outstandingQuantity > 0)
        .map((l) => ({
          materialId: l.materialId,
          quantity: String(l.outstandingQuantity),
          unit: l.unit,
          unitCost: String(l.unitPrice),
          orderLineId: l.id
        }))
    );
  };

  return (
    <>
      <Card className="p-4 mb-4">
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pur.newReceipt")}</Button>
        <p className="text-xs text-gray-500 mt-2">{t("pur.receiptHint")}</p>
      </Card>

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.linkToOrder")}</label>
                <Select value={orderId} onChange={(e) => applyOrder(e.target.value)}>
                  <option value="">{t("pur.noOrderLink")}</option>
                  {receivableOrders.map((o) => (
                    <option key={o.id} value={o.id}>{o.orderNumber} - {o.supplierName}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.supplier")}</label>
                <Select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} required>
                  <option value="">{t("common.select")}</option>
                  {suppliers?.map((s) => (
                    <option key={s.id} value={s.id}>{s.code} - {pick(s.nameAr, s.nameEn, s.name)}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.warehouse")}</label>
                <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                  <option value="">{t("common.selectWarehouse")}</option>
                  {warehouses?.map((w) => (
                    <option key={w.id} value={w.id}>{w.code} - {w.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.receiptDate")}</label>
                <Input type="date" value={receiptDate} onChange={(e) => setReceiptDate(e.target.value)} required />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.supplierDoc")}</label>
                <Input value={supplierDocumentNumber} onChange={(e) => setSupplierDocumentNumber(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
                <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600 mb-2">{t("common.item")}</label>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-6 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="block text-[11px] text-gray-500 mb-1">{t("pur.material")}</label>
                      <Select
                        value={line.materialId}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, materialId: e.target.value } : l)))}
                        required
                      >
                        <option value="">{t("common.select")}</option>
                        {materials?.map((m) => (
                          <option key={m.id} value={m.id}>{m.code} - {m.name} ({m.unit})</option>
                        ))}
                      </Select>
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("pur.receivedQty")}</label>
                      <Input
                        type="number" step="0.001" min="0" value={line.quantity}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, quantity: e.target.value } : l)))}
                      />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("common.unit")}</label>
                      <Select
                        value={line.unit}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, unit: e.target.value as MaterialUnit } : l)))}
                      >
                        {units.map((u) => (
                          <option key={u} value={u}>{u}</option>
                        ))}
                      </Select>
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("pur.unitCost")}</label>
                      <Input
                        type="number" step="0.0001" min="0" value={line.unitCost}
                        onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, unitCost: e.target.value } : l)))}
                      />
                    </div>
                    <div className="flex gap-2">
                      {lines.length > 1 && (
                        <Button type="button" variant="secondary" onClick={() => setLines((p) => p.filter((_, i) => i !== idx))}>
                          {t("common.remove")}
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
              <Button
                type="button"
                variant="ghost"
                className="mt-2"
                onClick={() => setLines((p) => [...p, { materialId: "", quantity: "", unit: "KG", unitCost: "", orderLineId: "" }])}
              >
                {t("pur.addLine")}
              </Button>
              {selectedOrder && (
                <p className="text-xs text-gray-500 mt-2">
                  {t("pur.orderNumber")}: <span className="ltr-nums">{selectedOrder.orderNumber}</span>
                </p>
              )}
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <div className="space-y-4">
        {isLoading && <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>}
        {!isLoading && receipts?.length === 0 && <Card className="p-6 text-center text-gray-400">{t("common.empty")}</Card>}

        {receipts?.map((r) => (
          <Card key={r.id} className="p-5">
            <div className="flex flex-wrap items-center gap-2 mb-3">
              <span className="font-bold ltr-nums">{r.receiptNumber}</span>
              <span className="text-sm text-gray-600">{r.supplierCode} - {r.supplierName}</span>
              <span className="text-gray-400">·</span>
              <span className="text-sm text-gray-600">{r.warehouseName}</span>
              {r.orderNumber && (
                <>
                  <span className="text-gray-400">·</span>
                  <span className="text-sm text-gray-500 ltr-nums">{r.orderNumber}</span>
                </>
              )}
              <span className="text-gray-400">·</span>
              <span className="text-sm text-gray-500 ltr-nums">{new Date(r.receiptDate).toLocaleDateString("en-GB")}</span>
              <span className="ms-auto text-sm font-semibold ltr-nums">{money(r.totalValue)}</span>
            </div>
            <table className="w-full text-sm">
              <thead>
                <tr className="text-gray-400 text-xs border-b border-gray-100">
                  <th className="text-start py-2 font-medium">{t("pur.material")}</th>
                  <th className="text-start py-2 font-medium">{t("pur.receivedQty")}</th>
                  <th className="text-start py-2 font-medium">{t("pur.unitCost")}</th>
                  <th className="text-start py-2 font-medium">{t("pur.lineValue")}</th>
                </tr>
              </thead>
              <tbody>
                {r.lines.map((l) => (
                  <tr key={l.id} className="border-b border-gray-50 last:border-0">
                    <td className="py-2">{l.materialCode} - {l.materialName}</td>
                    <td className="py-2 ltr-nums">{l.quantity} {l.unit}</td>
                    <td className="py-2 ltr-nums">{money(l.unitCost)}</td>
                    <td className="py-2 ltr-nums">{money(l.lineValue)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        ))}
      </div>
    </>
  );
}

// ----------------------------------------------------------------- invoices

function InvoicesTab() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [overdueOnly, setOverdueOnly] = useState(false);

  const [supplierId, setSupplierId] = useState("");
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(new Date().toISOString().slice(0, 10));
  const [dueDate, setDueDate] = useState(new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");
  const [discount, setDiscount] = useState("");
  const [tax, setTax] = useState("");
  const [lines, setLines] = useState<{ materialId: string; description: string; quantity: string; unit: MaterialUnit; unitPrice: string }[]>([
    { materialId: "", description: "", quantity: "", unit: "KG", unitPrice: "" }
  ]);

  const { data: suppliers } = useQuery({ queryKey: ["suppliers", "active"], queryFn: () => SuppliersApi.list({ activeOnly: true }) });
  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: invoices, isLoading } = useQuery({
    queryKey: ["supplier-invoices", overdueOnly],
    queryFn: () => PurchasesApi.invoices({ overdueOnly: overdueOnly || undefined })
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["supplier-invoices"] });
    qc.invalidateQueries({ queryKey: ["supplier-balances"] });
  };

  const createMutation = useMutation({
    mutationFn: () =>
      PurchasesApi.createInvoice({
        invoiceNumber,
        invoiceDate,
        dueDate,
        supplierId,
        notes: notes || undefined,
        discount: Number(discount || 0),
        tax: Number(tax || 0),
        lines: lines
          .filter((l) => l.description && Number(l.quantity) > 0)
          .map((l) => ({
            materialId: l.materialId || undefined,
            description: l.description,
            quantity: Number(l.quantity),
            unit: l.unit,
            unitPrice: Number(l.unitPrice || 0)
          }))
      }),
    onSuccess: () => {
      invalidate();
      setShowForm(false);
      setInvoiceNumber("");
      setLines([{ materialId: "", description: "", quantity: "", unit: "KG", unitPrice: "" }]);
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const postMutation = useMutation({
    mutationFn: (id: string) => PurchasesApi.postInvoice(id),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => PurchasesApi.cancelInvoice(vars.id, vars.reason),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  return (
    <>
      <Card className="p-4 mb-4 flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
          {t("pur.overdueOnly")}
        </label>
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pur.newInvoice")}</Button>
        <span className="text-xs text-gray-500">{t("pur.invoiceHint")}</span>
      </Card>

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.supplier")}</label>
                <Select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} required>
                  <option value="">{t("common.select")}</option>
                  {suppliers?.map((s) => (
                    <option key={s.id} value={s.id}>{s.code} - {pick(s.nameAr, s.nameEn, s.name)}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.invoiceNumber")}</label>
                <Input value={invoiceNumber} onChange={(e) => setInvoiceNumber(e.target.value)} required maxLength={60} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.invoiceDate")}</label>
                <Input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} required />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.dueDate")}</label>
                <Input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} required />
              </div>
            </div>

            <div className="space-y-3">
              {lines.map((line, idx) => (
                <div key={idx} className="grid grid-cols-1 sm:grid-cols-6 gap-3 items-end bg-gray-50 rounded-lg p-3">
                  <div className="sm:col-span-2">
                    <label className="block text-[11px] text-gray-500 mb-1">{t("pur.material")}</label>
                    <Select
                      value={line.materialId}
                      onChange={(e) => {
                        const material = materials?.find((m) => m.id === e.target.value);
                        setLines((p) =>
                          p.map((l, i) =>
                            i === idx
                              ? {
                                  ...l,
                                  materialId: e.target.value,
                                  description: l.description || (material ? `${material.code} - ${material.name}` : "")
                                }
                              : l
                          )
                        );
                      }}
                    >
                      <option value="">{t("common.select")}</option>
                      {materials?.map((m) => (
                        <option key={m.id} value={m.id}>{m.code} - {m.name}</option>
                      ))}
                    </Select>
                  </div>
                  <div>
                    <label className="block text-[11px] text-gray-500 mb-1">{t("pur.description")}</label>
                    <Input
                      value={line.description}
                      onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, description: e.target.value } : l)))}
                      required
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-gray-500 mb-1">{t("pur.quantity")}</label>
                    <Input
                      type="number" step="0.001" min="0" value={line.quantity}
                      onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, quantity: e.target.value } : l)))}
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] text-gray-500 mb-1">{t("pur.unitPrice")}</label>
                    <Input
                      type="number" step="0.0001" min="0" value={line.unitPrice}
                      onChange={(e) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, unitPrice: e.target.value } : l)))}
                    />
                  </div>
                  <div className="flex gap-2">
                    {lines.length > 1 && (
                      <Button type="button" variant="secondary" onClick={() => setLines((p) => p.filter((_, i) => i !== idx))}>
                        {t("common.remove")}
                      </Button>
                    )}
                  </div>
                </div>
              ))}
            </div>

            <Button
              type="button"
              variant="ghost"
              onClick={() => setLines((p) => [...p, { materialId: "", description: "", quantity: "", unit: "KG", unitPrice: "" }])}
            >
              {t("pur.addLine")}
            </Button>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
                <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.discount")}</label>
                <Input type="number" step="0.01" min="0" value={discount} onChange={(e) => setDiscount(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.tax")}</label>
                <Input type="number" step="0.01" min="0" value={tax} onChange={(e) => setTax(e.target.value)} />
              </div>
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("pur.invoiceNumber")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.supplier")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.invoiceDate")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.dueDate")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.totalValue")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && invoices?.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {invoices?.map((i) => (
              <tr key={i.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{i.invoiceNumber}</td>
                <td className="px-4 py-3">{i.supplierCode} - {i.supplierName}</td>
                <td className="px-4 py-3 ltr-nums">{new Date(i.invoiceDate).toLocaleDateString("en-GB")}</td>
                <td className="px-4 py-3 ltr-nums">{new Date(i.dueDate).toLocaleDateString("en-GB")}</td>
                <td className="px-4 py-3 ltr-nums">{money(i.total)} {i.currency}</td>
                <td className="px-4 py-3">
                  <Badge tone={invoiceTone[i.status]}>{t(`pur.invStatus.${i.status}`)}</Badge>
                </td>
                <td className="px-4 py-3">
                  {i.status === "Draft" && (
                    <button
                      className="text-brand-600 hover:underline text-xs font-semibold"
                      onClick={() => postMutation.mutate(i.id)}
                    >
                      {t("pur.postInvoice")}
                    </button>
                  )}
                  {i.status !== "Cancelled" && (
                    <button
                      className="text-red-600 hover:underline text-xs font-semibold ms-3"
                      onClick={() => {
                        const reason = window.prompt(t("pur.cancelReason"));
                        if (reason) cancelMutation.mutate({ id: i.id, reason });
                      }}
                    >
                      {t("pur.cancelInvoice")}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

// ----------------------------------------------------------------- payments

function PaymentsTab() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [supplierId, setSupplierId] = useState("");
  const [accountId, setAccountId] = useState("");
  const [paymentDate, setPaymentDate] = useState(new Date().toISOString().slice(0, 10));
  const [amount, setAmount] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("");
  const [description, setDescription] = useState("");

  const { data: suppliers } = useQuery({ queryKey: ["suppliers", "active"], queryFn: () => SuppliersApi.list({ activeOnly: true }) });
  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const { data: payments, isLoading } = useQuery({ queryKey: ["supplier-payments"], queryFn: () => PurchasesApi.payments() });

  const createMutation = useMutation({
    mutationFn: () =>
      PurchasesApi.paySupplier({
        paymentDate,
        supplierId,
        treasuryAccountId: accountId,
        amount: Number(amount),
        paymentMethod: paymentMethod || undefined,
        description: description || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["supplier-payments"] });
      qc.invalidateQueries({ queryKey: ["supplier-balances"] });
      qc.invalidateQueries({ queryKey: ["treasury-accounts"] });
      setShowForm(false);
      setAmount("");
      setDescription("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  return (
    <>
      <Card className="p-4 mb-4">
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pur.newPayment")}</Button>
      </Card>

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.supplier")}</label>
              <Select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} required>
                <option value="">{t("common.select")}</option>
                {suppliers?.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} - {pick(s.nameAr, s.nameEn, s.name)}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.account")}</label>
              <Select value={accountId} onChange={(e) => setAccountId(e.target.value)} required>
                <option value="">{t("pay.selectAccount")}</option>
                {accounts?.map((a) => (
                  <option key={a.id} value={a.id}>{a.code} - {a.name}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.paymentDate")}</label>
              <Input type="date" value={paymentDate} onChange={(e) => setPaymentDate(e.target.value)} required />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.amount")}</label>
              <Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} required />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.paymentMethod")}</label>
              <Input value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)} maxLength={60} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pur.description")}</label>
              <Input value={description} onChange={(e) => setDescription(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("pur.paySupplier")}
            </Button>
          </form>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("pur.paymentNumber")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.supplier")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.paymentDate")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.account")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.amount")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.paymentMethod")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && payments?.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {payments?.map((p) => (
              <tr key={p.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{p.paymentNumber}</td>
                <td className="px-4 py-3">{p.supplierCode} - {p.supplierName}</td>
                <td className="px-4 py-3 ltr-nums">{new Date(p.paymentDate).toLocaleDateString("en-GB")}</td>
                <td className="px-4 py-3">{p.treasuryAccountName}</td>
                <td className="px-4 py-3 ltr-nums">{money(p.amount)} {p.currency}</td>
                <td className="px-4 py-3 text-gray-600">{p.paymentMethod ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

// ----------------------------------------------------------------- balances

function BalancesTab() {
  const { t } = useI18n();
  const [withBalanceOnly, setWithBalanceOnly] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);

  const { data: balances, isLoading } = useQuery({
    queryKey: ["supplier-balances", withBalanceOnly],
    queryFn: () => PurchasesApi.balances({ withBalanceOnly: withBalanceOnly || undefined })
  });

  const { data: ledger } = useQuery({
    queryKey: ["supplier-ledger", selected],
    queryFn: () => PurchasesApi.ledger(selected as string),
    enabled: !!selected
  });

  return (
    <>
      <Card className="p-4 mb-4">
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={withBalanceOnly} onChange={(e) => setWithBalanceOnly(e.target.checked)} />
          {t("pur.withBalanceOnly")}
        </label>
      </Card>

      <Card className="mb-6">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("common.code")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.supplier")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.totalInvoiced")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.totalPaid")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.outstanding")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.openInvoices")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pur.overdue")}</th>
              <th className="text-start px-4 py-3 font-medium" />
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && balances?.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {balances?.map((b) => (
              <tr key={b.supplierId} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{b.supplierCode}</td>
                <td className="px-4 py-3">{b.supplierName}</td>
                <td className="px-4 py-3 ltr-nums">{money(b.totalInvoiced)}</td>
                <td className="px-4 py-3 ltr-nums">{money(b.totalPaid)}</td>
                <td className="px-4 py-3 ltr-nums font-semibold">{money(b.outstanding)}</td>
                <td className="px-4 py-3 ltr-nums">{b.openInvoiceCount}</td>
                <td className="px-4 py-3 ltr-nums text-red-600">{b.overdueAmount > 0 ? money(b.overdueAmount) : "—"}</td>
                <td className="px-4 py-3">
                  <button
                    className="text-brand-600 hover:underline text-xs font-semibold"
                    onClick={() => setSelected(selected === b.supplierId ? null : b.supplierId)}
                  >
                    {t("pur.ledger")}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {selected && (
        <Card className="p-5">
          <h3 className="font-semibold mb-3">{t("pur.ledger")}</h3>
          <table className="w-full text-sm">
            <thead>
              <tr className="text-gray-400 text-xs border-b border-gray-100">
                <th className="text-start py-2 font-medium">{t("common.date")}</th>
                <th className="text-start py-2 font-medium">{t("common.reference")}</th>
                <th className="text-start py-2 font-medium">{t("pur.debit")}</th>
                <th className="text-start py-2 font-medium">{t("pur.credit")}</th>
                <th className="text-start py-2 font-medium">{t("pur.runningBalance")}</th>
              </tr>
            </thead>
            <tbody>
              {ledger?.length === 0 && (
                <tr>
                  <td colSpan={5} className="py-6 text-center text-gray-400">{t("common.empty")}</td>
                </tr>
              )}
              {ledger?.map((e) => (
                <tr key={e.id} className="border-b border-gray-50 last:border-0">
                  <td className="py-2 ltr-nums">{new Date(e.entryDate).toLocaleDateString("en-GB")}</td>
                  <td className="py-2">
                    <span className="ltr-nums">{e.sourceDocumentNumber}</span>
                    <span className="text-gray-400 text-xs ms-2">{e.description}</span>
                  </td>
                  <td className="py-2 ltr-nums">{e.debit > 0 ? money(e.debit) : "—"}</td>
                  <td className="py-2 ltr-nums">{e.credit > 0 ? money(e.credit) : "—"}</td>
                  <td className="py-2 ltr-nums font-medium">{money(e.runningBalance)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </>
  );
}
