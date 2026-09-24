import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { DeliveriesApi, InvoicesApi, ProductionOrdersApi, RawMessagesApi } from "@/api/client";
import { Card, Badge } from "@/components/ui";

/// <summary>
/// What a QR code scan opens (spec section 40): a compact, read-only
/// summary - not the full editing screen. Scanning a Production Order shows
/// exactly the fields the spec calls out: Customer, Item, Color, Requested
/// quantity, Current stage, Status, history (stage list).
/// </summary>
export default function ScanViewPage() {
  const { type, id } = useParams<{ type: string; id: string }>();

  if (type === "production-order") return <ProductionOrderScan id={id!} />;
  if (type === "invoice") return <InvoiceScan id={id!} />;
  if (type === "delivery") return <DeliveryScan id={id!} />;
  if (type === "raw-message") return <RawMessageScan id={id!} />;

  return <div className="p-8 text-center text-gray-400">نوع مستند غير معروف</div>;
}

function ScanShell({ title, number, children }: { title: string; number: string; children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-gray-50 p-4 flex items-start justify-center">
      <Card className="w-full max-w-md p-5 mt-6">
        <div className="text-xs text-gray-400 mb-1">{title}</div>
        <div className="text-lg font-bold ltr-nums mb-4">{number}</div>
        {children}
      </Card>
    </div>
  );
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex justify-between py-2 border-b border-gray-50 text-sm">
      <span className="text-gray-500">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

function ProductionOrderScan({ id }: { id: string }) {
  const { data: order, isLoading } = useQuery({ queryKey: ["scan-order", id], queryFn: () => ProductionOrdersApi.get(id) });
  if (isLoading || !order) return <div className="p-8 text-center text-gray-400">جارٍ التحميل...</div>;

  const currentStage = order.stageExecutions.find((s) => s.status === "InProgress") ?? order.stageExecutions.find((s) => s.status === "Pending");
  const completedKg = order.stageExecutions.filter((s) => s.status === "Completed").reduce((sum, s) => sum + (s.outputKg ?? 0), 0);

  return (
    <ScanShell title="أمر تشغيل" number={order.orderNumber}>
      <Row label="العميل" value={`${order.customerCode} - ${order.customerName}`} />
      <Row label="الصنف" value={`${order.itemCode} - ${order.itemName}`} />
      <Row label="اللون" value={order.color ?? "-"} />
      <Row label="الكمية المطلوبة" value={`${order.requestedQuantityKg ?? ""} ${order.requestedQuantityKg ? "كجم" : ""} ${order.requestedQuantityMeter ?? ""} ${order.requestedQuantityMeter ? "م" : ""}`} />
      <Row label="الكمية المكتملة" value={`${completedKg} كجم`} />
      <Row label="المرحلة الحالية" value={currentStage?.stageName ?? "-"} />
      <Row label="الحالة" value={<Badge tone="blue">{order.status}</Badge>} />

      <div className="mt-4">
        <div className="text-xs text-gray-400 mb-2">سجل المراحل</div>
        <div className="space-y-1.5">
          {order.stageExecutions.map((s) => (
            <div key={s.id} className="flex justify-between text-xs">
              <span>{s.stageName}</span>
              <span className="text-gray-400">{s.status}</span>
            </div>
          ))}
        </div>
      </div>
    </ScanShell>
  );
}

function InvoiceScan({ id }: { id: string }) {
  const { data: invoice, isLoading } = useQuery({ queryKey: ["scan-invoice", id], queryFn: () => InvoicesApi.get(id) });
  if (isLoading || !invoice) return <div className="p-8 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <ScanShell title="فاتورة" number={invoice.invoiceNumber}>
      <Row label="العميل" value={`${invoice.customerCode} - ${invoice.customerName}`} />
      <Row label="التاريخ" value={new Date(invoice.invoiceDate).toLocaleDateString("en-GB")} />
      <Row label="الإجمالي" value={invoice.total} />
      <Row label="الحالة" value={<Badge tone="blue">{invoice.status}</Badge>} />
    </ScanShell>
  );
}

function DeliveryScan({ id }: { id: string }) {
  const { data: delivery, isLoading } = useQuery({ queryKey: ["scan-delivery", id], queryFn: () => DeliveriesApi.get(id) });
  if (isLoading || !delivery) return <div className="p-8 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <ScanShell title="إذن تسليم" number={delivery.deliveryNumber}>
      <Row label="العميل" value={`${delivery.customerCode} - ${delivery.customerName}`} />
      <Row label="التاريخ" value={new Date(delivery.deliveryDate).toLocaleDateString("en-GB")} />
      <Row label="عدد البنود" value={delivery.lines.length} />
      <Row label="الحالة" value={<Badge tone="blue">{delivery.status}</Badge>} />
    </ScanShell>
  );
}

function RawMessageScan({ id }: { id: string }) {
  const { data: message, isLoading } = useQuery({ queryKey: ["scan-raw-message", id], queryFn: () => RawMessagesApi.get(id) });
  if (isLoading || !message) return <div className="p-8 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <ScanShell title="إذن استلام خام" number={message.messageNumber}>
      <Row label="العميل" value={`${message.customerCode} - ${message.customerName}`} />
      <Row label="المخزن" value={message.warehouseName} />
      <Row label="التاريخ" value={new Date(message.receiptDate).toLocaleDateString("en-GB")} />
      <Row label="حالة الفحص" value={<Badge tone="blue">{message.inspectionStatus}</Badge>} />
    </ScanShell>
  );
}
