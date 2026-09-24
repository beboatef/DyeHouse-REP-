import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, DocumentPdfApi, ItemsApi, RawMessagesApi, WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

type LineDraft = { itemId: string; quantityKg: string; quantityMeter: string; pieceCount: string; notes: string };

const emptyLine = (): LineDraft => ({ itemId: "", quantityKg: "", quantityMeter: "", pieceCount: "", notes: "" });

const inspectionLabel: Record<string, string> = {
  PendingInspection: "بانتظار الفحص",
  Accepted: "مقبول",
  AcceptedWithNotes: "مقبول مع ملاحظات",
  Rejected: "مرفوض"
};
const inspectionTone: Record<string, "yellow" | "green" | "red"> = {
  PendingInspection: "yellow",
  Accepted: "green",
  AcceptedWithNotes: "green",
  Rejected: "red"
};

export default function RawMessagesPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [receiptDate, setReceiptDate] = useState(new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "raw"], queryFn: () => WarehousesApi.list({ kind: "RawMaterial" }) });
  const { data: messages, isLoading } = useQuery({ queryKey: ["raw-messages"], queryFn: () => RawMessagesApi.list() });

  const createMutation = useMutation({
    mutationFn: RawMessagesApi.create,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      setShowForm(false);
      setLines([emptyLine()]);
      setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const inspectionMutation = useMutation({
    mutationFn: ({ id, result }: { id: string; result: string }) => RawMessagesApi.recordInspection(id, { result }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["raw-messages"] })
  });

  const updateLine = (idx: number, patch: Partial<LineDraft>) =>
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      receiptDate,
      customerId,
      warehouseId,
      notes: notes || undefined,
      lines: lines
        .filter((l) => l.itemId && (l.quantityKg || l.quantityMeter))
        .map((l) => ({
          itemId: l.itemId,
          quantityKg: l.quantityKg ? Number(l.quantityKg) : undefined,
          quantityMeter: l.quantityMeter ? Number(l.quantityMeter) : undefined,
          pieceCount: l.pieceCount ? Number(l.pieceCount) : undefined,
          notes: l.notes || undefined
        }))
    });
  };

  return (
    <>
      <PageHeader
        title="استلام الخام / الرسائل"
        subtitle="كل رسالة تحصل على رقم تسلسلي تلقائي وتُعد المرجع الأساسي للتتبع الكامل حتى التسليم والفاتورة"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ رسالة استلام جديدة"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form onSubmit={submit} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                  <option value="">اختر العميل...</option>
                  {customers?.map((c) => (
                    <option key={c.id} value={c.id}>{c.code} - {c.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">المخزن</label>
                <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                  <option value="">اختر المخزن...</option>
                  {warehouses?.map((w) => (
                    <option key={w.id} value={w.id}>{w.code} - {w.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">تاريخ الاستلام</label>
                <Input type="date" value={receiptDate} onChange={(e) => setReceiptDate(e.target.value)} required />
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-xs font-medium text-gray-600">بنود الرسالة</label>
                <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
              </div>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-6 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="block text-[11px] text-gray-500 mb-1">الصنف</label>
                      <Select value={line.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} required>
                        <option value="">اختر...</option>
                        {items?.map((i) => (
                          <option key={i.id} value={i.id}>{i.code} - {i.name} ({i.baseUnit})</option>
                        ))}
                      </Select>
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">كمية (كجم)</label>
                      <Input type="number" step="0.001" min="0" value={line.quantityKg}
                        onChange={(e) => updateLine(idx, { quantityKg: e.target.value })} />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">كمية (متر)</label>
                      <Input type="number" step="0.001" min="0" value={line.quantityMeter}
                        onChange={(e) => updateLine(idx, { quantityMeter: e.target.value })} />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">عدد القطع/التوبات (وصفي)</label>
                      <Input type="number" min="0" value={line.pieceCount}
                        onChange={(e) => updateLine(idx, { pieceCount: e.target.value })} />
                    </div>
                    <div className="flex gap-2">
                      <Input placeholder="ملاحظات" value={line.notes} onChange={(e) => updateLine(idx, { notes: e.target.value })} />
                      {lines.length > 1 && (
                        <Button type="button" variant="secondary" onClick={() => setLines((p) => p.filter((_, i) => i !== idx))}>
                          حذف
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">ملاحظات عامة</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ الرسالة"}
            </Button>
            {error && <p className="text-sm text-red-600">{error}</p>}
          </form>
        </Card>
      )}

      <div className="space-y-4">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {!isLoading && messages?.length === 0 && <Card className="p-6 text-center text-gray-400">لا توجد رسائل استلام بعد</Card>}

        {messages?.map((m) => (
          <Card key={m.id} className="p-5">
            <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
              <div>
                <span className="font-bold text-lg ltr-nums">{m.messageNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{m.customerCode} - {m.customerName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{m.warehouseName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-500 ltr-nums">{new Date(m.receiptDate).toLocaleDateString("en-GB")}</span>
              </div>
              <div className="flex items-center gap-2">
                <Link to={`/print/raw-message/${m.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
                <Button variant="ghost" onClick={() => DocumentPdfApi.rawMessage(m.id, m.messageNumber)}>تنزيل PDF</Button>
                <Badge tone={inspectionTone[m.inspectionStatus]}>{inspectionLabel[m.inspectionStatus]}</Badge>
                {m.inspectionStatus === "PendingInspection" && (
                  <div className="flex gap-1">
                    <Button variant="secondary" onClick={() => inspectionMutation.mutate({ id: m.id, result: "Accepted" })}>قبول</Button>
                    <Button variant="secondary" onClick={() => inspectionMutation.mutate({ id: m.id, result: "Rejected" })}>رفض</Button>
                  </div>
                )}
              </div>
            </div>

            <table className="w-full text-sm">
              <thead>
                <tr className="text-gray-400 text-xs border-b border-gray-100">
                  <th className="text-start py-2 font-medium">الصنف</th>
                  <th className="text-start py-2 font-medium">الكمية المستلمة</th>
                  <th className="text-start py-2 font-medium">الرصيد المتبقي</th>
                  <th className="text-start py-2 font-medium">عدد القطع</th>
                </tr>
              </thead>
              <tbody>
                {m.lines.map((l) => (
                  <tr key={l.id} className="border-b border-gray-50 last:border-0">
                    <td className="py-2">{l.itemCode} - {l.itemName}</td>
                    <td className="py-2 ltr-nums">
                      {l.quantityKg != null && `${l.quantityKg} كجم`}
                      {l.quantityKg != null && l.quantityMeter != null && " / "}
                      {l.quantityMeter != null && `${l.quantityMeter} م`}
                    </td>
                    <td className="py-2 ltr-nums font-medium">
                      {l.remainingKg != null && `${l.remainingKg} كجم`}
                      {l.remainingKg != null && l.remainingMeter != null && " / "}
                      {l.remainingMeter != null && `${l.remainingMeter} م`}
                    </td>
                    <td className="py-2 ltr-nums">{l.pieceCount ?? "-"}</td>
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
