import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, DeliveriesApi, ReadyGoodsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";
import { ExportButtons } from "@/components/ExportButtons";
import { DeliveriesExports } from "@/api/exports";

const statusLabel: Record<string, string> = { Draft: "مسودة", Prepared: "مجهزة", Delivered: "تم التسليم", Cancelled: "ملغاة" };
const statusTone: Record<string, "gray" | "blue" | "green" | "red"> = { Draft: "gray", Prepared: "blue", Delivered: "green", Cancelled: "red" };

type LineDraft = { productionOrderId: string; itemId: string; color: string; quantityKg: string; quantityMeter: string };
const emptyLine = (): LineDraft => ({ productionOrderId: "", itemId: "", color: "", quantityKg: "", quantityMeter: "" });

export default function DeliveriesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [deliveryDate, setDeliveryDate] = useState(new Date().toISOString().slice(0, 10));
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);
  const [cancelReasonById, setCancelReasonById] = useState<Record<string, string>>({});
  // Post-approval correction (spec section 30): which delivery's editor is open,
  // and the new quantity typed for each of its existing lines.
  const [correctingId, setCorrectingId] = useState<string | null>(null);
  const [correctQty, setCorrectQty] = useState<Record<string, string>>({});
  const [correctReason, setCorrectReason] = useState("");
  const [correctError, setCorrectError] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: readyBalance } = useQuery({
    queryKey: ["ready-goods-balance", customerId], queryFn: () => ReadyGoodsApi.balance({ customerId }), enabled: !!customerId
  });
  const { data: deliveries, isLoading } = useQuery({ queryKey: ["deliveries"], queryFn: () => DeliveriesApi.list() });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["deliveries"] });

  const createMutation = useMutation({
    mutationFn: () => DeliveriesApi.create({
      customerId, deliveryDate,
      lines: lines.filter((l) => l.productionOrderId && (l.quantityKg || l.quantityMeter)).map((l) => {
        const balanceRow = readyBalance?.find((b) => b.productionOrderId === l.productionOrderId);
        return {
          productionOrderId: l.productionOrderId, itemId: balanceRow?.itemId ?? "", color: balanceRow?.color ?? undefined,
          quantityKg: l.quantityKg ? Number(l.quantityKg) : undefined, quantityMeter: l.quantityMeter ? Number(l.quantityMeter) : undefined
        };
      })
    }),
    onSuccess: () => { invalidate(); setShowForm(false); setLines([emptyLine()]); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const prepareMutation = useMutation({ mutationFn: DeliveriesApi.prepare, onSuccess: invalidate });
  const deliverMutation = useMutation({
    mutationFn: DeliveriesApi.deliver, onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذر التسليم")
  });
  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => DeliveriesApi.cancel(vars.id, vars.reason), onSuccess: invalidate
  });

  /**
   * Corrects an approved delivery (spec section 30). Only quantities are sent -
   * the API decides the compensating movement, so the screen cannot accidentally
   * imply that editing a line "just changes a number" without touching stock.
   */
  const correctMutation = useMutation({
    mutationFn: (vars: { id: string; lines: { lineId: string; quantityKg?: number; quantityMeter?: number }[]; reason: string }) =>
      DeliveriesApi.correctApprovedLines(vars.id, vars),
    onSuccess: () => {
      invalidate();
      setCorrectingId(null);
      setCorrectQty({});
      setCorrectReason("");
      setCorrectError(null);
    },
    onError: (err: any) => setCorrectError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "تعذر حفظ التصحيح")
  });

  const openCorrection = (id: string) => {
    setCorrectingId(id);
    setCorrectQty({});
    setCorrectReason("");
    setCorrectError(null);
  };

  const updateLine = (idx: number, patch: Partial<LineDraft>) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  return (
    <>
      <PageHeader
        title="التسليمات"
        subtitle="مسودة ← مجهزة ← تم التسليم ← ملغاة - التسليم يخصم من المخزون الجاهز، والإلغاء ينشئ حركة عكسية"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: DeliveriesExports.excel }}
              pdf={{ action: DeliveriesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تسليم جديد"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="field-label">العميل</label>
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                  <option value="">اختر...</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">تاريخ التسليم</label>
                <Input type="date" value={deliveryDate} onChange={(e) => setDeliveryDate(e.target.value)} required />
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="field-label">بنود التسليم (من الرصيد الجاهز)</label>
                <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
              </div>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="field-label">أمر التشغيل (الرصيد الجاهز)</label>
                      <Select value={line.productionOrderId} onChange={(e) => updateLine(idx, { productionOrderId: e.target.value })} required disabled={!customerId}>
                        <option value="">اختر...</option>
                        {readyBalance?.map((b) => (
                          <option key={b.productionOrderId} value={b.productionOrderId}>
                            {b.productionOrderNumber} - {b.itemCode} {b.color ? `(${b.color})` : ""} - متاح {b.remainingKg || b.remainingMeter}
                          </option>
                        ))}
                      </Select>
                    </div>
                    <div><label className="field-label">كمية (كجم)</label><Input type="number" step="0.001" min="0" value={line.quantityKg} onChange={(e) => updateLine(idx, { quantityKg: e.target.value })} /></div>
                    <div><label className="field-label">كمية (متر)</label><Input type="number" step="0.001" min="0" value={line.quantityMeter} onChange={(e) => updateLine(idx, { quantityMeter: e.target.value })} /></div>
                  </div>
                ))}
              </div>
            </div>

            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ كمسودة"}</Button>
            {error && <p className="form-error">{error}</p>}
          </form>
        </Card>
      )}

      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {deliveries?.map((d) => (
          <Card key={d.id} className="card-pad">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="font-bold ltr-nums">{d.deliveryNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{d.customerCode} - {d.customerName}</span>
              </div>
              <Badge tone={statusTone[d.status]}>{statusLabel[d.status]}</Badge>
            </div>

            <div className="table-wrap mt-3">
              <table className="table table-dense">
                <thead>
                  <tr>
                    <th>{t("prod.jobOrder", "أمر التشغيل")}</th>
                    <th>{t("common.item", "الصنف")}</th>
                    <th>{t("common.quantity", "الكمية")}</th>
                  </tr>
                </thead>
                <tbody>
                  {d.lines.map((l) => (
                    <tr key={l.id}>
                      <td className="ltr-nums">{l.productionOrderNumber}</td>
                      <td>{l.itemCode}{l.color ? ` (${l.color})` : ""}</td>
                      <td className="ltr-nums">{l.quantityKg != null && `${l.quantityKg} كجم `}{l.quantityMeter != null && `${l.quantityMeter} م`}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-3 flex flex-wrap items-center gap-2">
              {d.status === "Draft" && <Button variant="secondary" onClick={() => prepareMutation.mutate(d.id)}>تجهيز</Button>}
              <Link to={`/print/delivery/${d.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
              <Button variant="ghost" onClick={DeliveriesExports.documentPdf(d.id)}>تنزيل PDF</Button>
              {d.status === "Prepared" && <Button variant="secondary" onClick={() => deliverMutation.mutate(d.id)}>تسليم</Button>}
              {d.status === "Delivered" && correctingId !== d.id && (
                <Button variant="secondary" onClick={() => openCorrection(d.id)}>
                  {t("del.correctApproved", "تصحيح بعد الاعتماد")}
                </Button>
              )}
              {(d.status === "Draft" || d.status === "Prepared" || d.status === "Delivered") && (
                <>
                  <Input
                    placeholder="سبب الإلغاء..."
                    value={cancelReasonById[d.id] ?? ""}
                    onChange={(e) => setCancelReasonById((p) => ({ ...p, [d.id]: e.target.value }))}
                    className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                  />
                  <Button variant="ghost" disabled={!cancelReasonById[d.id]} onClick={() => cancelMutation.mutate({ id: d.id, reason: cancelReasonById[d.id] })}>إلغاء</Button>
                </>
              )}
            </div>

            {/* Post-approval correction editor. Each line keeps its delivered
                quantity until the user changes it, and the difference is shown
                per line so the effect on Ready Goods is never a surprise. */}
            {d.status === "Delivered" && correctingId === d.id && (
              <div className="mt-3 rounded-lg border border-line bg-surface-sunken p-3">
                <p className="mb-2 text-2xs text-ink-subtle">{t("del.correctHint")}</p>
                <div className="table-wrap">
                  <table className="table table-dense">
                    <thead>
                      <tr>
                        <th>{t("prod.jobOrder", "أمر التشغيل")}</th>
                        <th>{t("common.item", "الصنف")}</th>
                        <th>{t("common.quantity", "الكمية المعتمدة")}</th>
                        <th>{t("del.newQuantity", "الكمية الصحيحة")}</th>
                        <th>{t("del.difference", "الفرق")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {d.lines.map((l) => {
                        const isKg = l.quantityKg != null;
                        const original = Number(isKg ? l.quantityKg : l.quantityMeter);
                        const typed = correctQty[l.id];
                        const next = typed === undefined ? original : Number(typed);
                        const diff = Number.isFinite(next) ? next - original : 0;
                        return (
                          <tr key={l.id}>
                            <td className="ltr-nums">{l.productionOrderNumber}</td>
                            <td>{l.itemCode}{l.color ? ` (${l.color})` : ""}</td>
                            <td className="ltr-nums">
                              {isKg ? `${l.quantityKg} كجم` : `${l.quantityMeter} م`}
                            </td>
                            <td>
                              <Input
                                type="number"
                                min={0}
                                step="0.01"
                                className="max-w-[9rem]"
                                value={typed ?? String(original)}
                                onChange={(e) => setCorrectQty((p) => ({ ...p, [l.id]: e.target.value }))}
                              />
                            </td>
                            <td className="ltr-nums">
                              {diff === 0 ? (
                                <span className="text-ink-subtle">—</span>
                              ) : (
                                <span className={diff < 0 ? "text-success-ink" : "text-warning-ink"}>
                                  {diff > 0 ? "+" : ""}
                                  {diff.toLocaleString("en-US")}
                                </span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>

                {correctError && <p className="mt-2 text-sm text-danger-ink">{correctError}</p>}

                <div className="mt-3 flex flex-wrap items-end gap-2">
                  <div className="min-w-[14rem] flex-1">
                    <label className="field-label">
                      {t("del.correctReason", "سبب التصحيح")}
                    </label>
                    <Input value={correctReason} onChange={(e) => setCorrectReason(e.target.value)} />
                  </div>
                  <Button
                    disabled={correctReason.trim() === "" || correctMutation.isPending}
                    onClick={() =>
                      correctMutation.mutate({
                        id: d.id,
                        reason: correctReason.trim(),
                        lines: d.lines.map((l) => {
                          const isKg = l.quantityKg != null;
                          const original = Number(isKg ? l.quantityKg : l.quantityMeter);
                          const typed = correctQty[l.id];
                          return {
                            lineId: l.id,
                            quantityKg: isKg ? (typed === undefined ? original : Number(typed)) : undefined,
                            quantityMeter: isKg ? undefined : (typed === undefined ? original : Number(typed))
                          };
                        })
                      })
                    }
                  >
                    {t("common.save", "حفظ")}
                  </Button>
                  <Button variant="secondary" onClick={() => setCorrectingId(null)}>
                    {t("common.close", "إغلاق")}
                  </Button>
                </div>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
