import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AdjustmentType, CustomersApi, ItemsApi, RawMessagesApi, StockAdjustmentsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

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

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-adjustment", customerId],
    queryFn: () => RawMessagesApi.list({ customerId }),
    enabled: !!customerId
  });
  const { data: adjustments, isLoading } = useQuery({ queryKey: ["stock-adjustments"], queryFn: () => StockAdjustmentsApi.list() });

  const createMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      StockAdjustmentsApi.create({
        customerId, itemId, rawMessageId, type, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        notes: notes || undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["stock-adjustments"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setReason(""); setNotes(""); setError(null); setNegativeStockDetail(null);
    },
    onError: (err: any) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") setNegativeStockDetail({ shortage: err.response.data.shortage, reason: "" });
      else setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ");
    }
  });

  return (
    <>
      <PageHeader
        title="تسويات المخزون"
        subtitle="كل تسوية تُسجَّل مع الرصيد قبل وبعد - ولا يمكن تعديلها بعد الحفظ، فقط تسوية جديدة"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تسوية جديدة"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(false); }}>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">نوع التسوية</label>
                <Select value={type} onChange={(e) => setType(e.target.value as AdjustmentType)}>
                  <option value="Increase">زيادة</option>
                  <option value="Decrease">نقص</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-gray-600 mb-1">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!customerId}>
                  <option value="">اختر...</option>
                  {messages?.map((m) => <option key={m.id} value={m.id}>{m.messageNumber}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
              </div>
              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-gray-600 mb-1">السبب</label>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} required placeholder="فرق جرد فعلي / فرق وزن / تصحيح إدخال..." />
              </div>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}</Button>
            {error && <p className="text-sm text-red-600">{error}</p>}

            {negativeStockDetail && (
              <Card className="p-4 border-yellow-300 bg-yellow-50">
                <p className="text-sm text-yellow-800">
                  النقص يتجاوز الرصيد المتاح بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>. يتطلب صلاحية تجاوز وسببًا موثقًا.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب التجاوز..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary" disabled={!negativeStockDetail.reason} onClick={() => createMutation.mutate(true)}>تجاوز واعتماد</Button>
                </div>
              </Card>
            )}
          </form>
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">رقم التسوية</th>
              <th className="text-start px-4 py-3 font-medium">العميل</th>
              <th className="text-start px-4 py-3 font-medium">الصنف</th>
              <th className="text-start px-4 py-3 font-medium">النوع</th>
              <th className="text-start px-4 py-3 font-medium">قبل</th>
              <th className="text-start px-4 py-3 font-medium">بعد</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && adjustments?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد تسويات بعد</td></tr>}
            {adjustments?.map((a) => (
              <tr key={a.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{a.adjustmentNumber}</td>
                <td className="px-4 py-3">{a.customerCode}</td>
                <td className="px-4 py-3">{a.itemCode}</td>
                <td className="px-4 py-3"><Badge tone={a.type === "Increase" ? "green" : "red"}>{a.type === "Increase" ? "زيادة" : "نقص"}</Badge></td>
                <td className="px-4 py-3 ltr-nums">{a.quantityBeforeKg ?? a.quantityBeforeMeter ?? "-"}</td>
                <td className="px-4 py-3 ltr-nums font-medium">{a.quantityAfterKg ?? a.quantityAfterMeter ?? "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
