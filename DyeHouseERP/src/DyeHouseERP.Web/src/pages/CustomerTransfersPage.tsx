import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, CustomerTransfersApi, ItemsApi, RawMessagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";

export default function CustomerTransfersPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [fromCustomerId, setFromCustomerId] = useState("");
  const [toCustomerId, setToCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [rawMessageId, setRawMessageId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-transfer", fromCustomerId],
    queryFn: () => RawMessagesApi.list({ customerId: fromCustomerId, onlyWithBalance: true }),
    enabled: !!fromCustomerId
  });
  const { data: transfers, isLoading } = useQuery({ queryKey: ["customer-transfers"], queryFn: () => CustomerTransfersApi.list() });

  const createMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      CustomerTransfersApi.create({
        fromCustomerId, toCustomerId, rawMessageId, itemId, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        notes: notes || undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-transfers"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setReason(""); setNotes(""); setError(null); setNegativeStockDetail(null);
    },
    onError: (err: any) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") {
        setNegativeStockDetail({ shortage: err.response.data.shortage, reason: "" });
      } else {
        setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ");
      }
    }
  });

  return (
    <>
      <PageHeader
        title="تحويل الخام بين العملاء"
        subtitle="لا يتم تعديل بيانات الرسالة الأصلية أبدًا - كل تحويل يُسجَّل كحركة مستقلة وقابلة للتتبع بالكامل"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحويل جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate(false);
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">من عميل</label>
                <Select value={fromCustomerId} onChange={(e) => { setFromCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">إلى عميل</label>
                <Select value={toCustomerId} onChange={(e) => setToCustomerId(e.target.value)} required>
                  <option value="">اختر...</option>
                  {customers?.filter((c) => c.id !== fromCustomerId).map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر الصنف...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-gray-600 mb-1">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!fromCustomerId}>
                  <option value="">اختر الرسالة...</option>
                  {messages?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
                    </option>
                  ))}
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
                <label className="block text-xs font-medium text-gray-600 mb-1">سبب التحويل</label>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} required />
              </div>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
            {error && <p className="text-sm text-red-600">{error}</p>}

            {negativeStockDetail && (
              <Card className="p-4 border-yellow-300 bg-yellow-50">
                <p className="text-sm text-yellow-800">
                  الكمية تتجاوز رصيد العميل المرسل بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
                  يتطلب صلاحية تجاوز الرصيد السالب وسببًا موثقًا.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب التجاوز..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary" disabled={!negativeStockDetail.reason} onClick={() => createMutation.mutate(true)}>
                    تجاوز واعتماد
                  </Button>
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
              <th className="text-start px-4 py-3 font-medium">رقم التحويل</th>
              <th className="text-start px-4 py-3 font-medium">من</th>
              <th className="text-start px-4 py-3 font-medium">إلى</th>
              <th className="text-start px-4 py-3 font-medium">الصنف</th>
              <th className="text-start px-4 py-3 font-medium">الرسالة</th>
              <th className="text-start px-4 py-3 font-medium">الكمية</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && transfers?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد تحويلات بعد</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{t.transferNumber}</td>
                <td className="px-4 py-3">{t.fromCustomerCode}</td>
                <td className="px-4 py-3">{t.toCustomerCode}</td>
                <td className="px-4 py-3">{t.itemCode}</td>
                <td className="px-4 py-3 ltr-nums">{t.messageNumber}</td>
                <td className="px-4 py-3 ltr-nums">
                  {t.quantityKg != null && `${t.quantityKg} كجم `}
                  {t.quantityMeter != null && `${t.quantityMeter} م`}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
