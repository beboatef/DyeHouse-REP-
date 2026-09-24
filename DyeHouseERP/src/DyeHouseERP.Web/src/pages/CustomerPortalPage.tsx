import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, ItemsApi, ProductionRequestsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const statusLabel: Record<string, string> = { Pending: "بانتظار المراجعة", Approved: "معتمد", Rejected: "مرفوض", ConvertedToOrder: "تم التحويل لأمر تشغيل" };
const statusTone: Record<string, "yellow" | "green" | "red" | "blue"> = { Pending: "yellow", Approved: "green", Rejected: "red", ConvertedToOrder: "blue" };

export default function CustomerPortalPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [color, setColor] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [notes, setNotes] = useState("");
  const [reasonById, setReasonById] = useState<Record<string, string>>({});

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: requests, isLoading } = useQuery({ queryKey: ["production-requests"], queryFn: () => ProductionRequestsApi.list() });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["production-requests"] });

  const createMutation = useMutation({
    mutationFn: () => ProductionRequestsApi.create({
      customerId, itemId, requestDate: new Date().toISOString().slice(0, 10), color: color || undefined,
      requestedQuantityKg: qtyKg ? Number(qtyKg) : undefined, requestedQuantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
      notes: notes || undefined
    }),
    onSuccess: () => { invalidate(); setShowForm(false); setQtyKg(""); setQtyMeter(""); setNotes(""); }
  });

  const approveMutation = useMutation({ mutationFn: (id: string) => ProductionRequestsApi.approve(id), onSuccess: invalidate });
  const rejectMutation = useMutation({ mutationFn: (vars: { id: string; reason: string }) => ProductionRequestsApi.reject(vars.id, vars.reason), onSuccess: invalidate });
  const convertMutation = useMutation({
    mutationFn: (id: string) => ProductionRequestsApi.convert(id, new Date().toISOString().slice(0, 10)), onSuccess: invalidate
  });

  return (
    <>
      <PageHeader
        title="بوابة العملاء - طلبات التشغيل"
        subtitle="العميل يقدّم طلبًا جديدًا هنا فقط - لا يصل إلى أي شاشة إدارية داخلية. الطلب يدخل ضمن سير العمل الداخلي ليُعتمد أو يُرفض ثم يتحول لأمر تشغيل"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ طلب تشغيل جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
              <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required><option value="">اختر...</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}</Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
              <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required><option value="">اختر...</option>{items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">اللون</label><Input value={color} onChange={(e) => setColor(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية (كجم)</label><Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية (متر)</label><Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">ملاحظات</label><Input value={notes} onChange={(e) => setNotes(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>إرسال الطلب</Button>
          </form>
        </Card>
      )}

      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {requests?.map((r) => (
          <Card key={r.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="font-bold ltr-nums">{r.requestNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{r.customerCode}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{r.itemCode} - {r.itemName}{r.color ? ` (${r.color})` : ""}</span>
              </div>
              <Badge tone={statusTone[r.status]}>{statusLabel[r.status]}</Badge>
            </div>
            {r.convertedOrderNumber && <p className="text-xs text-gray-500 mt-1">أمر التشغيل: <span className="ltr-nums font-medium">{r.convertedOrderNumber}</span></p>}

            {r.status === "Pending" && (
              <div className="flex gap-2 mt-3 items-center">
                <Button variant="secondary" onClick={() => approveMutation.mutate(r.id)}>اعتماد</Button>
                <Input placeholder="سبب الرفض..." value={reasonById[r.id] ?? ""} onChange={(e) => setReasonById((p) => ({ ...p, [r.id]: e.target.value }))} className="max-w-xs" />
                <Button variant="ghost" disabled={!reasonById[r.id]} onClick={() => rejectMutation.mutate({ id: r.id, reason: reasonById[r.id] })}>رفض</Button>
              </div>
            )}
            {r.status === "Approved" && (
              <div className="mt-3"><Button variant="secondary" onClick={() => convertMutation.mutate(r.id)}>تحويل إلى أمر تشغيل</Button></div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
