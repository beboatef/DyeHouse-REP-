import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, ItemsApi, RawExternalReleasesApi, RawMessagesApi, RawReleaseReason } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const reasonLabel: Record<RawReleaseReason, string> = {
  ReturnToCustomer: "إرجاع للعميل",
  ExternalProcessing: "تشغيل خارجي",
  Sale: "بيع خام"
};
const reasonTone: Record<RawReleaseReason, "blue" | "yellow" | "green"> = {
  ReturnToCustomer: "blue", ExternalProcessing: "yellow", Sale: "green"
};

export default function RawExternalReleasesPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [rawMessageId, setRawMessageId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [reason, setReason] = useState<RawReleaseReason>("ReturnToCustomer");
  const [externalParty, setExternalParty] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-release", customerId],
    queryFn: () => RawMessagesApi.list({ customerId, onlyWithBalance: true }),
    enabled: !!customerId
  });
  const { data: releases, isLoading } = useQuery({ queryKey: ["raw-external-releases"], queryFn: () => RawExternalReleasesApi.list() });

  const createMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      RawExternalReleasesApi.create({
        customerId, itemId, rawMessageId, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        externalParty: externalParty || undefined,
        notes: notes || undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["raw-external-releases"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setExternalParty(""); setNotes(""); setError(null); setNegativeStockDetail(null);
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
        title="إفراج الخام الخارجي"
        subtitle="إرجاع للعميل، تشغيل خارجي، أو بيع خام - كل حركة تُسحب من رسالة محددة يختارها المستخدم"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ حركة جديدة"}</Button>}
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
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر العميل...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر الصنف...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">السبب</label>
                <Select value={reason} onChange={(e) => setReason(e.target.value as RawReleaseReason)}>
                  <option value="ReturnToCustomer">إرجاع للعميل</option>
                  <option value="ExternalProcessing">تشغيل خارجي</option>
                  <option value="Sale">بيع خام</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-gray-600 mb-1">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!customerId}>
                  <option value="">اختر الرسالة...</option>
                  {messages?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الطرف الخارجي (اختياري)</label>
                <Input value={externalParty} onChange={(e) => setExternalParty(e.target.value)} placeholder="مصنع التشغيل الخارجي / المشتري" />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
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
                  الكمية تتجاوز الرصيد المتاح بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
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
              <th className="text-start px-4 py-3 font-medium">رقم الحركة</th>
              <th className="text-start px-4 py-3 font-medium">العميل</th>
              <th className="text-start px-4 py-3 font-medium">الصنف</th>
              <th className="text-start px-4 py-3 font-medium">الرسالة المصدر</th>
              <th className="text-start px-4 py-3 font-medium">الكمية</th>
              <th className="text-start px-4 py-3 font-medium">السبب</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && releases?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد حركات بعد</td></tr>}
            {releases?.map((r) => (
              <tr key={r.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{r.releaseNumber}</td>
                <td className="px-4 py-3">{r.customerCode} - {r.customerName}</td>
                <td className="px-4 py-3">{r.itemCode} - {r.itemName}</td>
                <td className="px-4 py-3 ltr-nums">{r.messageNumber}</td>
                <td className="px-4 py-3 ltr-nums">
                  {r.quantityKg != null && `${r.quantityKg} كجم `}
                  {r.quantityMeter != null && `${r.quantityMeter} م`}
                </td>
                <td className="px-4 py-3"><Badge tone={reasonTone[r.reason]}>{reasonLabel[r.reason]}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
