import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, ItemsApi, ProductionOrdersApi, ProductionPriority } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const statusLabel: Record<string, string> = {
  Draft: "مسودة", RawAllocated: "تم تخصيص الخام", InProduction: "قيد التشغيل", Completed: "مكتمل", Cancelled: "ملغي"
};
const statusTone: Record<string, "gray" | "blue" | "yellow" | "green" | "red"> = {
  Draft: "gray", RawAllocated: "blue", InProduction: "yellow", Completed: "green", Cancelled: "red"
};
const priorityLabel: Record<string, string> = { Low: "منخفضة", Normal: "عادية", High: "عالية", Urgent: "عاجلة" };

export default function ProductionOrdersPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [color, setColor] = useState("");
  const [orderDate, setOrderDate] = useState(new Date().toISOString().slice(0, 10));
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [priority, setPriority] = useState<ProductionPriority>("Normal");
  const [customerReference, setCustomerReference] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: orders, isLoading } = useQuery({ queryKey: ["production-orders"], queryFn: () => ProductionOrdersApi.list() });

  const createMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.create({
        customerId, itemId, orderDate, priority,
        color: color || undefined,
        requestedQuantityKg: qtyKg ? Number(qtyKg) : undefined,
        requestedQuantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        customerReference: customerReference || undefined,
        notes: notes || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["production-orders"] });
      setShowForm(false);
      setColor(""); setQtyKg(""); setQtyMeter(""); setCustomerReference(""); setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  return (
    <>
      <PageHeader
        title="أوامر التشغيل"
        subtitle="المرجع المركزي لدورة الإنتاج بالكامل - من تخصيص الخام حتى اكتمال جميع المراحل"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ أمر تشغيل جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
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
                <label className="block text-xs font-medium text-gray-600 mb-1">اللون</label>
                <Input value={color} onChange={(e) => setColor(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية المطلوبة (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية المطلوبة (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الأولوية</label>
                <Select value={priority} onChange={(e) => setPriority(e.target.value as ProductionPriority)}>
                  <option value="Low">منخفضة</option>
                  <option value="Normal">عادية</option>
                  <option value="High">عالية</option>
                  <option value="Urgent">عاجلة</option>
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">تاريخ الأمر</label>
                <Input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} required />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">مرجع العميل</label>
                <Input value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} />
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
          </form>
        </Card>
      )}

      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {!isLoading && orders?.length === 0 && <Card className="p-6 text-center text-gray-400">لا توجد أوامر تشغيل بعد</Card>}

        {orders?.map((o) => (
          <Link key={o.id} to={`/production-orders/${o.id}`}>
            <Card className="p-4 hover:border-brand-300 transition-colors">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <span className="font-bold ltr-nums">{o.orderNumber}</span>
                  <span className="text-gray-400 mx-2">·</span>
                  <span className="text-sm text-gray-600">{o.customerCode} - {o.customerName}</span>
                  <span className="text-gray-400 mx-2">·</span>
                  <span className="text-sm text-gray-600">{o.itemCode} - {o.itemName}</span>
                  {o.color && <><span className="text-gray-400 mx-2">·</span><span className="text-sm text-gray-600">{o.color}</span></>}
                </div>
                <div className="flex items-center gap-2">
                  <Badge tone="gray">{priorityLabel[o.priority]}</Badge>
                  <Badge tone={statusTone[o.status]}>{statusLabel[o.status]}</Badge>
                </div>
              </div>
              <div className="mt-2 text-xs text-gray-400">
                {o.stageExecutions.filter((s) => s.status === "Completed").length} / {o.stageExecutions.length} مراحل مكتملة
              </div>
            </Card>
          </Link>
        ))}
      </div>
    </>
  );
}
