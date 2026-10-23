import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, ReadyGoodsApi, WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, SearchInput } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ReadyGoodsExports } from "@/api/exports";
import StockTraceability from "@/components/StockTraceability";
import { useI18n } from "@/i18n";

export default function ReadyGoodsPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [productionOrderId, setProductionOrderId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [stageExecutionId, setStageExecutionId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [pieceCount, setPieceCount] = useState("");
  const [category, setCategory] = useState<"FirstGrade" | "SecondGrade" | "Separates">("FirstGrade");
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

  const [balanceSearch, setBalanceSearch] = useState("");
  const visibleBalance = useMemo(() => {
    const term = balanceSearch.trim().toLocaleLowerCase();
    return (balance ?? []).filter((row) => !term || [
      row.productionOrderNumber, row.customerCode, row.itemCode, row.color, row.category
    ].filter(Boolean).join(" ").toLocaleLowerCase().includes(term));
  }, [balance, balanceSearch]);
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
      productionOrderId, warehouseId, stageExecutionId, category,
      quantityKg: qtyKg ? Number(qtyKg) : undefined,
      quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
      pieceCount: pieceCount ? Number(pieceCount) : undefined
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["ready-goods-balance"] });
      qc.invalidateQueries({ queryKey: ["ready-goods-transfers"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setShowForm(false); setQtyKg(""); setQtyMeter(""); setPieceCount(""); setCategory("FirstGrade");
      setStageExecutionId(""); setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const rawReturnMutation = useMutation({
    mutationFn: (vars: { productionOrderId: string; itemId: string; category: "FirstGrade" | "SecondGrade" | "Separates" | null }) => ReadyGoodsApi.returnToRaw({
      productionOrderId: vars.productionOrderId,
      itemId: vars.itemId,
      category: vars.category,
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
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
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
        <Card className="card-pad mb-6">
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
                <p className="text-2xs text-warning-ink mt-1">
                  لا توجد مرحلة مغلقة لهذا الأمر التشغيلي - أغلق المرحلة أولًا ليتحرك ناتجها إلى الجاهز.
                </p>
              )}
            </div>
            <div>
              <label className="field-label">تصنيف القماش الجاهز</label>
              <Select value={category} onChange={(e) => setCategory(e.target.value as "FirstGrade" | "SecondGrade" | "Separates")} required>
                <option value="FirstGrade">درجة أولى</option>
                <option value="SecondGrade">درجة ثانية</option>
                <option value="Separates">منفصلات</option>
              </Select>
            </div>
            <div><label className="field-label">الكمية (كجم)</label><Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} /></div>
            <div><label className="field-label">الكمية (متر)</label><Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} /></div>
            <div><label className="field-label">عدد الأتواب (وصفي)</label><Input type="number" min="0" value={pieceCount} onChange={(e) => setPieceCount(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending || !stageExecutionId}>{createMutation.isPending ? "جارٍ الحفظ..." : "ترحيل"}</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}

      <h2 className="card-title mb-3">تتبع أرصدة وحركات الجاهز لكل أمر تشغيل</h2>
      <div className="mb-8">
        <StockTraceability scope="ready" />
      </div>

      <h2 className="card-title mb-3">الرصيد الجاهز الحالي</h2>
      <Card className="card-pad mb-3">
        <label className="field-label">بحث في أرصدة الجاهز</label>
        <SearchInput value={balanceSearch} onChange={(e) => setBalanceSearch(e.target.value)}
          placeholder="رقم أمر التشغيل أو العميل أو الصنف أو التصنيف" />
      </Card>
      <Card className="mb-8 overflow-hidden">
        <div className="table-wrap">
        <table className="table table-dense">
          <thead><tr>
            <th>أمر التشغيل</th><th>العميل</th>
            <th>الصنف</th><th>اللون</th><th>التصنيف</th>
            <th className="text-end">الرصيد (كجم)</th><th className="text-end">الرصيد (متر)</th>
            <th className="text-end">إجراء</th>
          </tr></thead>
          <tbody>
            {balanceLoading && <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {!balanceLoading && visibleBalance.length === 0 && <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">لا يوجد رصيد جاهز مطابق للبحث</td></tr>}
            {visibleBalance.map((b) => (
              <tr key={`${b.productionOrderId}-${b.itemId}-${b.category ?? "Unclassified"}`} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{b.productionOrderNumber}</td><td>{b.customerCode}</td>
                <td>{b.itemCode}</td><td>{b.color ?? "-"}</td>
                <td>{b.category === "FirstGrade" ? t("readyGoods.firstGrade", "درجة أولى") : b.category === "SecondGrade" ? t("readyGoods.secondGrade", "درجة ثانية") : b.category === "Separates" ? t("readyGoods.separates", "منفصلات") : t("readyGoods.unclassified", "غير مصنف")"}</td>
                <td className="text-end ltr-nums font-semibold">{b.remainingKg > 0 ? b.remainingKg.toLocaleString("en-US", { maximumFractionDigits: 3 }) : "—"}</td><td className="text-end ltr-nums font-semibold">{b.remainingMeter > 0 ? b.remainingMeter.toLocaleString("en-US", { maximumFractionDigits: 3 }) : "—"}</td>
                <td className="text-end">
                  <Button
                    variant="ghost"
                    onClick={() => {
                      const key = `${b.productionOrderId}-${b.itemId}-${b.category ?? "Unclassified"}`;
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
        </div>

        {/* Ready goods -> raw: an authorized movement of its own, with a mandatory reason. It is
            refused by the API when the quantity exceeds what is actually available on the lot. */}
        {rawFormFor && (() => {
          const row = balance?.find((b) => `${b.productionOrderId}-${b.itemId}-${b.category ?? "Unclassified"}` === rawFormFor);
          if (!row) return null;
          return (
            <div className="border-t border-line bg-surface-sunken p-4">
              <div className="text-sm font-semibold">
                إرجاع مخزون جاهز إلى الخام — <span className="ltr-nums">{row.productionOrderNumber}</span> — {row.category === "FirstGrade" ? t("readyGoods.firstGrade", "درجة أولى") : row.category === "SecondGrade" ? t("readyGoods.secondGrade", "درجة ثانية") : row.category === "Separates" ? t("readyGoods.separates", "منفصلات") : t("readyGoods.unclassified", "غير مصنف")"}
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
                  onClick={() => rawReturnMutation.mutate({ productionOrderId: row.productionOrderId, itemId: row.itemId, category: row.category as "FirstGrade" | "SecondGrade" | "Separates" | null })}
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

      <h2 className="card-title mb-3">سجل الإرجاع إلى الخام</h2>
      <Card className="mb-8">
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>أمر التشغيل</th><th>الرسالة المستقبلة</th><th>الكمية</th><th>السبب</th><th>المستخدم</th>
          </tr></thead>
          <tbody>
            {!rawReturns?.length && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">لا توجد حركات إرجاع إلى الخام</td></tr>}
            {rawReturns?.map((m) => (
              <tr key={m.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
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

      <h2 className="card-title mb-3">سجل الترحيلات</h2>
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>أمر التشغيل</th>
            <th>المرحلة المنتِجة</th><th>العميل</th><th>مخزن الجاهز</th><th>التصنيف</th>
            <th>الكمية المستلمة</th><th>المتاح الآن</th>
          </tr></thead>
          <tbody>
            {transfersLoading && <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{t.transferNumber}</td><td className="ltr-nums">{t.productionOrderNumber}</td>
                <td>{t.stageSequence != null ? `#${t.stageSequence} ${t.stageName ?? ""}` : "—"}</td>
                <td>{t.customerCode}</td>
                <td>{t.warehouseName}</td>
                <td>{t.category === "FirstGrade" ? t("readyGoods.firstGrade", "درجة أولى") : t.category === "SecondGrade" ? t("readyGoods.secondGrade", "درجة ثانية") : t.category === "Separates" ? t("readyGoods.separates", "منفصلات") : t("readyGoods.unclassified", "غير مصنف")"}</td>
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
