import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, DeliveriesApi, DocumentPdfApi, ReadyGoodsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const statusLabel: Record<string, string> = { Draft: "مسودة", Prepared: "مجهزة", Delivered: "تم التسليم", Cancelled: "ملغاة" };
const statusTone: Record<string, "gray" | "blue" | "green" | "red"> = { Draft: "gray", Prepared: "blue", Delivered: "green", Cancelled: "red" };

type LineDraft = { productionOrderId: string; itemId: string; color: string; quantityKg: string; quantityMeter: string };
const emptyLine = (): LineDraft => ({ productionOrderId: "", itemId: "", color: "", quantityKg: "", quantityMeter: "" });

export default function DeliveriesPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [deliveryDate, setDeliveryDate] = useState(new Date().toISOString().slice(0, 10));
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);
  const [cancelReasonById, setCancelReasonById] = useState<Record<string, string>>({});

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

  const updateLine = (idx: number, patch: Partial<LineDraft>) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  return (
    <>
      <PageHeader
        title="التسليمات"
        subtitle="مسودة ← مجهزة ← تم التسليم ← ملغاة - التسليم يخصم من المخزون الجاهز، والإلغاء ينشئ حركة عكسية"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تسليم جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                  <option value="">اختر...</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">تاريخ التسليم</label>
                <Input type="date" value={deliveryDate} onChange={(e) => setDeliveryDate(e.target.value)} required />
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-xs font-medium text-gray-600">بنود التسليم (من الرصيد الجاهز)</label>
                <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
              </div>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="block text-[11px] text-gray-500 mb-1">أمر التشغيل (الرصيد الجاهز)</label>
                      <Select value={line.productionOrderId} onChange={(e) => updateLine(idx, { productionOrderId: e.target.value })} required disabled={!customerId}>
                        <option value="">اختر...</option>
                        {readyBalance?.map((b) => (
                          <option key={b.productionOrderId} value={b.productionOrderId}>
                            {b.productionOrderNumber} - {b.itemCode} {b.color ? `(${b.color})` : ""} - متاح {b.remainingKg || b.remainingMeter}
                          </option>
                        ))}
                      </Select>
                    </div>
                    <div><label className="block text-[11px] text-gray-500 mb-1">كمية (كجم)</label><Input type="number" step="0.001" min="0" value={line.quantityKg} onChange={(e) => updateLine(idx, { quantityKg: e.target.value })} /></div>
                    <div><label className="block text-[11px] text-gray-500 mb-1">كمية (متر)</label><Input type="number" step="0.001" min="0" value={line.quantityMeter} onChange={(e) => updateLine(idx, { quantityMeter: e.target.value })} /></div>
                  </div>
                ))}
              </div>
            </div>

            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ كمسودة"}</Button>
            {error && <p className="text-sm text-red-600">{error}</p>}
          </form>
        </Card>
      )}

      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {deliveries?.map((d) => (
          <Card key={d.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="font-bold ltr-nums">{d.deliveryNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{d.customerCode} - {d.customerName}</span>
              </div>
              <Badge tone={statusTone[d.status]}>{statusLabel[d.status]}</Badge>
            </div>

            <table className="w-full text-sm mt-3">
              <tbody>
                {d.lines.map((l) => (
                  <tr key={l.id} className="border-b border-gray-50 last:border-0">
                    <td className="py-1.5 ltr-nums">{l.productionOrderNumber}</td>
                    <td className="py-1.5">{l.itemCode}{l.color ? ` (${l.color})` : ""}</td>
                    <td className="py-1.5 ltr-nums">{l.quantityKg != null && `${l.quantityKg} كجم `}{l.quantityMeter != null && `${l.quantityMeter} م`}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="flex items-center gap-2 mt-3">
              {d.status === "Draft" && <Button variant="secondary" onClick={() => prepareMutation.mutate(d.id)}>تجهيز</Button>}
              <Link to={`/print/delivery/${d.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
              <Button variant="ghost" onClick={() => DocumentPdfApi.delivery(d.id, d.deliveryNumber)}>تنزيل PDF</Button>
              {d.status === "Prepared" && <Button variant="secondary" onClick={() => deliverMutation.mutate(d.id)}>تسليم</Button>}
              {(d.status === "Draft" || d.status === "Prepared" || d.status === "Delivered") && (
                <>
                  <Input
                    placeholder="سبب الإلغاء..."
                    value={cancelReasonById[d.id] ?? ""}
                    onChange={(e) => setCancelReasonById((p) => ({ ...p, [d.id]: e.target.value }))}
                    className="max-w-xs"
                  />
                  <Button variant="ghost" disabled={!cancelReasonById[d.id]} onClick={() => cancelMutation.mutate({ id: d.id, reason: cancelReasonById[d.id] })}>إلغاء</Button>
                </>
              )}
            </div>
          </Card>
        ))}
      </div>
    </>
  );
}
