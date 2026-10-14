import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, ReadyGoodsApi, WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ReadyGoodsExports } from "@/api/exports";

export default function ReadyGoodsPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [productionOrderId, setProductionOrderId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [stageExecutionId, setStageExecutionId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [pieceCount, setPieceCount] = useState("");
  const [error, setError] = useState<string | null>(null);

  // ---- Ready goods back to RAW stock (spec section 29) ----
  // A separate, authorized, reasoned movement. Nothing does this implicitly: undelivered goods stay in
  // Ready Goods until someone explicitly moves them back for rework or reclassification.
  const [rawFormFor, setRawFormFor] = useState<string | null>(null);
  const [rawDraft, setRawDraft] = useState({ rawMessageId: "", quantityKg: "", quantityMeter: "", reason: "", notes: "" });
  const [rawError, setRawError] = useState<string | null>(null);

  const { data: orders } = useQuery({ queryKey: ["production-orders", "completed-eligible"], queryFn: () => ProductionOrdersApi.list() });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "ready"], queryFn: () => WarehousesApi.list({ kind: "ReadyGoods" }) });
  const { data: balance, isLoading: balanceLoading } = useQuery({ queryKey: ["ready-goods-balance"], queryFn: () => ReadyGoodsApi.balance() });
  const { data: transfers, isLoading: transfersLoading } = useQuery({ queryKey: ["ready-goods-transfers"], queryFn: () => ReadyGoodsApi.listTransfers() });
  const { data: rawReturns } = useQuery({ queryKey: ["ready-goods-raw-returns"], queryFn: () => ReadyGoodsApi.rawReturns() });
  // Only lots that hold a balance can receive ready stock back as raw material.
  const { data: rawLots } = useQuery({
    queryKey: ["raw-messages", "with-balance"],
    queryFn: () => RawMessagesApi.list({ onlyWithBalance: true })
  });

  // The stage link (spec section 23) is mandatory, so the form loads the chosen
  // order's closed stages and the quantity is capped by the stage's own output.
  const { data: selectedOrder } = useQuery({
    queryKey: ["production-order", productionOrderId],
    queryFn: () => ProductionOrdersApi.get(productionOrderId),
    enabled: !!productionOrderId
  });
  const closedStages = selectedOrder?.stageExecutions.filter((s) => s.status === "Completed") ?? [];

  const createMutation = useMutation({
    mutationFn: () => ReadyGoodsApi.createTransfer({
      productionOrderId, warehouseId, stageExecutionId,
      quantityKg: qtyKg ? Number(qtyKg) : undefined,
      quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
      pieceCount: pieceCount ? Number(pieceCount) : undefined
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["ready-goods-balance"] });
      qc.invalidateQueries({ queryKey: ["ready-goods-transfers"] });
      setShowForm(false); setQtyKg(""); setQtyMeter(""); setPieceCount("");
      setStageExecutionId(""); setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const rawReturnMutation = useMutation({
    mutationFn: (vars: { productionOrderId: string; itemId: string }) => ReadyGoodsApi.returnToRaw({
      productionOrderId: vars.productionOrderId,
      itemId: vars.itemId,
      rawMessageId: rawDraft.rawMessageId,
      quantityKg: rawDraft.quantityKg ? Number(rawDraft.quantityKg) : undefined,
      quantityMeter: rawDraft.quantityMeter ? Number(rawDraft.quantityMeter) : undefined,
      reason: rawDraft.reason.trim(),
      notes: rawDraft.notes.trim() || undefined
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["ready-goods-balance"] });
      qc.invalidateQueries({ queryKey: ["ready-goods-raw-returns"] });
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      setRawFormFor(null);
      setRawDraft({ rawMessageId: "", quantityKg: "", quantityMeter: "", reason: "", notes: "" });
      setRawError(null);
    },
    onError: (err: ApiErrorLike) => setRawError(err?.response?.data?.title ?? err?.response?.data?.detail ?? "تعذر النقل")
  });

  return (
    <>
      <PageHeader
        title="المخزون الجاهز"
        subtitle="ترحيل الإنتاج المكتمل إلى مخزن الجاهز، والرصيد المتاح لكل أمر تشغيل"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ label: "تصدير الأرصدة Excel", action: ReadyGoodsExports.balance.excel }}
              pdf={{ label: "تصدير الأرصدة PDF", action: ReadyGoodsExports.balance.pdf }}
              extra={
                <Button variant="ghost" onClick={() => ReadyGoodsExports.transfers.excel()}>
                  تصدير حركات الترحيل Excel
                </Button>
              }
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ ترحيل جديد"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="sm:col-span-2">
              <label className="field-label">أمر التشغيل</label>
              <Select value={productionOrderId} onChange={(e) => {
                setProductionOrderId(e.target.value); setStageExecutionId(""); setQtyKg(""); setQtyMeter("");
              }} required>
                <option value="">اختر...</option>
                {orders?.map((o) => <option key={o.id} value={o.id}>{o.orderNumber} - {o.customerCode} - {o.itemCode}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">مخزن الجاهز</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>
                {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">المرحلة المنتِجة</label>
              <Select value={stageExecutionId} onChange={(e) => {
                const stage = closedStages.find((s) => s.id === e.target.value);
                setStageExecutionId(e.target.value);
                setQtyKg(stage?.outputKg != null ? String(stage.outputKg) : "");
                setQtyMeter(stage?.outputMeter != null ? String(stage.outputMeter) : "");
              }} required disabled={!productionOrderId}>
                <option value="">اختر...</option>
                {closedStages.map((s) => (
                  <option key={s.id} value={s.id}>
                    #{s.sequence} {s.stageName}{s.outputKg != null ? ` - ${s.outputKg} كجم` : ""}{s.outputMeter != null ? ` ${s.outputMeter} م` : ""}
                  </option>
                ))}
              </Select>
              {productionOrderId && closedStages.length === 0 && (
                <p className="text-2xs text-amber-600 mt-1">
                  لا توجد مرحلة مغلقة لهذا الأمر التشغيلي - أغلق المرحلة أولًا ليتحرك ناتجها إلى الجاهز.
                </p>
              )}
            </div>
            <div><label className="field-label">الكمية (كجم)</label><Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} /></div>
            <div><label className="field-label">الكمية (متر)</label><Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} /></div>
            <div><label className="field-label">عدد القطع (وصفي)</label><Input type="number" min="0" value={pieceCount} onChange={(e) => setPieceCount(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending || !stageExecutionId}>{createMutation.isPending ? "جارٍ الحفظ..." : "ترحيل"}</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}

      <h2 className="font-semibold text-gray-800 mb-3">الرصيد الجاهز الحالي</h2>
      <Card className="mb-8">
        <table className="table">
          <thead><tr>
            <th>أمر التشغيل</th><th>العميل</th>
            <th>الصنف</th><th>اللون</th><th>الرصيد المتاح</th>
            <th className="text-end">إجراء</th>
          </tr></thead>
          <tbody>
            {balanceLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!balanceLoading && balance?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا يوجد رصيد جاهز حاليًا</td></tr>}
            {balance?.map((b) => (
              <tr key={`${b.productionOrderId}-${b.itemId}`} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{b.productionOrderNumber}</td><td>{b.customerCode}</td>
                <td>{b.itemCode}</td><td>{b.color ?? "-"}</td>
                <td className="ltr-nums font-medium">{b.remainingKg > 0 && `${b.remainingKg} كجم `}{b.remainingMeter > 0 && `${b.remainingMeter} م`}</td>
                <td className="text-end">
                  <Button
                    variant="ghost"
                    onClick={() => {
                      const key = `${b.productionOrderId}-${b.itemId}`;
                      setRawFormFor(rawFormFor === key ? null : key);
                      setRawDraft({ rawMessageId: "", quantityKg: "", quantityMeter: "", reason: "", notes: "" });
                      setRawError(null);
                    }}
                  >
                    إرجاع إلى الخام
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {/* Ready goods -> raw: an authorized movement of its own, with a mandatory reason. It is
            refused by the API when the quantity exceeds what is actually available on the lot. */}
        {rawFormFor && (() => {
          const row = balance?.find((b) => `${b.productionOrderId}-${b.itemId}` === rawFormFor);
          if (!row) return null;
          return (
            <div className="border-t border-line bg-surface-sunken p-4">
              <div className="text-sm font-semibold">
                إرجاع مخزون جاهز إلى الخام — <span className="ltr-nums">{row.productionOrderNumber}</span>
              </div>
              <p className="kpi-footnote mb-3">
                حركة منفصلة مصرح بها: لا يحدث هذا تلقائيًا أبدًا، ويُسجَّل السبب في المستند وفي سجل التدقيق.
              </p>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 lg:grid-cols-5">
                <div className="sm:col-span-2">
                  <label className="field-label">رقم رسالة الخام (اللوت المستلم)</label>
                  <Select value={rawDraft.rawMessageId}
                    onChange={(e) => setRawDraft((p) => ({ ...p, rawMessageId: e.target.value }))}>
                    <option value="">اختر...</option>
                    {rawLots?.filter((l) => l.customerId === row.customerId).map((l) => (
                      <option key={l.id} value={l.id}>{l.messageNumber}</option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">الكمية (كجم) — المتاح {row.remainingKg}</label>
                  <Input type="number" step="0.001" min="0" value={rawDraft.quantityKg}
                    onChange={(e) => setRawDraft((p) => ({ ...p, quantityKg: e.target.value }))} />
                </div>
                <div>
                  <label className="field-label">الكمية (متر) — المتاح {row.remainingMeter}</label>
                  <Input type="number" step="0.001" min="0" value={rawDraft.quantityMeter}
                    onChange={(e) => setRawDraft((p) => ({ ...p, quantityMeter: e.target.value }))} />
                </div>
                <div>
                  <label className="field-label">السبب *</label>
                  <Input value={rawDraft.reason}
                    onChange={(e) => setRawDraft((p) => ({ ...p, reason: e.target.value }))} />
                </div>
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-2">
                <Button
                  disabled={
                    rawReturnMutation.isPending || !rawDraft.rawMessageId ||
                    (!rawDraft.quantityKg && !rawDraft.quantityMeter) || rawDraft.reason.trim() === ""
                  }
                  onClick={() => rawReturnMutation.mutate({ productionOrderId: row.productionOrderId, itemId: row.itemId })}
                >
                  {rawReturnMutation.isPending ? "جارٍ الحفظ..." : "تنفيذ النقل إلى الخام"}
                </Button>
                <Button variant="ghost" onClick={() => setRawFormFor(null)}>إلغاء</Button>
                {rawError && <span className="form-error">{rawError}</span>}
              </div>
            </div>
          );
        })()}
      </Card>

      <h2 className="font-semibold text-gray-800 mb-3">سجل الإرجاع إلى الخام</h2>
      <Card className="mb-8">
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>أمر التشغيل</th><th>الرسالة المستقبلة</th><th>الكمية</th><th>السبب</th><th>المستخدم</th>
          </tr></thead>
          <tbody>
            {!rawReturns?.length && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد حركات إرجاع إلى الخام</td></tr>}
            {rawReturns?.map((m) => (
              <tr key={m.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{m.movementNumber}</td>
                <td className="ltr-nums">{m.productionOrderNumber}</td>
                <td className="ltr-nums">{m.destinationMessageNumber}</td>
                <td className="ltr-nums">{m.quantityKg != null && `${m.quantityKg} كجم `}{m.quantityMeter != null && `${m.quantityMeter} م`}</td>
                <td className="text-ink-muted">{m.reason}</td>
                <td className="text-ink-muted">{m.createdBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      <h2 className="font-semibold text-gray-800 mb-3">سجل الترحيلات</h2>
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>أمر التشغيل</th>
            <th>المرحلة المنتِجة</th><th>العميل</th><th>مخزن الجاهز</th>
            <th>الكمية المستلمة</th><th>المتاح الآن</th>
          </tr></thead>
          <tbody>
            {transfersLoading && <tr><td colSpan={7} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{t.transferNumber}</td><td className="ltr-nums">{t.productionOrderNumber}</td>
                <td>{t.stageSequence != null ? `#${t.stageSequence} ${t.stageName ?? ""}` : "—"}</td>
                <td>{t.customerCode}</td>
                <td>{t.warehouseName}</td>
                <td className="ltr-nums">{t.quantityKg != null && `${t.quantityKg} كجم `}{t.quantityMeter != null && `${t.quantityMeter} م`}</td>
                {/* Available is read live from the ledger - what is left after deliveries and any
                    movements back to raw stock, never a stored figure. */}
                <td className="ltr-nums">
                  {t.availableQuantityKg > 0 && `${t.availableQuantityKg} كجم `}
                  {t.availableQuantityMeter > 0 && `${t.availableQuantityMeter} م`}
                  {t.availableQuantityKg <= 0 && t.availableQuantityMeter <= 0 && "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
