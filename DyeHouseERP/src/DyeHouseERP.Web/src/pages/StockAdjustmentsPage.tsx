import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AdjustmentType, ApprovalsApi, CustomersApi, ItemsApi, RawMessagesApi, StockAdjustmentsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { StockAdjustmentsExports } from "@/api/exports";

export default function StockAdjustmentsPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [rawMessageId, setRawMessageId] = useState("");
  const [type, setType] = useState<AdjustmentType>("Increase");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);
  const [approvalRequested, setApprovalRequested] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-adjustment", customerId],
    queryFn: () => RawMessagesApi.list({ customerId }),
    enabled: !!customerId
  });
  const { data: adjustments, isLoading } = useQuery({ queryKey: ["stock-adjustments"], queryFn: () => StockAdjustmentsApi.list() });

  const createMutation = useMutation({
    mutationFn: () =>
      StockAdjustmentsApi.create({
        customerId, itemId, rawMessageId, type, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        notes: notes || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["stock-adjustments"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setReason(""); setNotes(""); setError(null); setNegativeStockDetail(null);
      setApprovalRequested(null);
    },
    onError: (err: ApiErrorLike) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") setNegativeStockDetail({ shortage: err.response?.data?.shortage ?? 0, reason: "" });
      else setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ");
    }
  });

  /**
   * Spec section 24: the shortage is not "overridden and approved" by the
   * person who wants it. The screen submits the adjustment as an approval
   * request; a DIFFERENT user decides it, and only their approval posts it -
   * bound to this exact message, item, customer, direction and quantity.
   */
  const requestApprovalMutation = useMutation({
    mutationFn: (approvalReason: string) =>
      ApprovalsApi.submit({
        type: "StockAdjustment",
        reason: approvalReason,
        payloadJson: JSON.stringify({
          customerId, itemId, rawMessageId, type,
          quantityKg: qtyKg ? Number(qtyKg) : undefined,
          quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
          reason,
          notes: notes || undefined
        })
      }),
    onSuccess: (request) => {
      setNegativeStockDetail(null);
      setApprovalRequested(request.documentNumber);
      qc.invalidateQueries({ queryKey: ["approval-requests"] });
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "تعذر إرسال طلب الاعتماد")
  });

  return (
    <>
      <PageHeader
        title="تسويات المخزون"
        subtitle="كل تسوية تُسجَّل مع الرصيد قبل وبعد - ولا يمكن تعديلها بعد الحفظ، فقط تسوية جديدة"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: StockAdjustmentsExports.excel }}
              pdf={{ action: StockAdjustmentsExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تسوية جديدة"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="card-pad mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); setApprovalRequested(null); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="field-label">العميل</label>
                <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">نوع التسوية</label>
                <Select value={type} onChange={(e) => setType(e.target.value as AdjustmentType)}>
                  <option value="Increase">زيادة</option>
                  <option value="Decrease">نقص</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="field-label">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!customerId}>
                  <option value="">اختر...</option>
                  {messages?.map((m) => <option key={m.id} value={m.id}>{m.messageNumber}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">الكمية (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="field-label">الكمية (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
              </div>
              <div className="sm:col-span-2">
                <label className="field-label">السبب</label>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} required placeholder="فرق جرد فعلي / فرق وزن / تصحيح إدخال..." />
              </div>
            </div>
            <div>
              <label className="field-label">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}</Button>
            {error && <p className="form-error">{error}</p>}

            {negativeStockDetail && (
              <Card className="card-pad border-warning/30 bg-warning-soft">
                <p className="text-sm text-warning-ink">
                  النقص يتجاوز الرصيد المتاح بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
                  لا يتم تجاوز الرصيد السالب هنا: يُرسل الطلب إلى مركز الاعتمادات، ولا ينفّذه إلا مستخدم آخر يملك صلاحية اعتماد تجاوز الرصيد السالب.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب طلب الاعتماد..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary"
                    disabled={!negativeStockDetail.reason || requestApprovalMutation.isPending}
                    onClick={() => { setError(null); requestApprovalMutation.mutate(negativeStockDetail.reason); }}>
                    {requestApprovalMutation.isPending ? "جارٍ الإرسال..." : "طلب اعتماد"}
                  </Button>
                </div>
                <p className="mt-2 text-2xs text-warning-ink">
                  الاعتماد مرتبط بنفس التسوية ونفس النوع والكمية والوحدة والصنف والعميل - أي تغيير يتطلب طلبًا جديدًا.
                </p>
              </Card>
            )}

            {approvalRequested && (
              <p className="text-sm text-success-ink">
                تم إرسال طلب اعتماد على الرسالة <span className="ltr-nums font-bold">{approvalRequested}</span>.
                لن يتم الحفظ إلا بعد موافقة مستخدم آخر من مركز الاعتمادات.
              </p>
            )}
          </form>
        </Card>
      )}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>رقم التسوية</th>
              <th>العميل</th>
              <th>الصنف</th>
              <th>النوع</th>
              <th>قبل</th>
              <th>بعد</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {!isLoading && adjustments?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">لا توجد تسويات بعد</td></tr>}
            {adjustments?.map((a) => (
              <tr key={a.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{a.adjustmentNumber}</td>
                <td>{a.customerCode}</td>
                <td>{a.itemCode}</td>
                <td><Badge tone={a.type === "Increase" ? "green" : "red"}>{a.type === "Increase" ? "زيادة" : "نقص"}</Badge></td>
                <td className="ltr-nums">{a.quantityBeforeKg ?? a.quantityBeforeMeter ?? "-"}</td>
                <td className="ltr-nums font-medium">{a.quantityAfterKg ?? a.quantityAfterMeter ?? "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
