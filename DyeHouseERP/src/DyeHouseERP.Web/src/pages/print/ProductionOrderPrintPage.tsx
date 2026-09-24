import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ProductionOrdersApi } from "@/api/client";
import PrintPreviewLayout from "@/components/PrintPreviewLayout";

export default function ProductionOrderPrintPage() {
  const { id } = useParams<{ id: string }>();
  const { data: order, isLoading } = useQuery({ queryKey: ["production-order-print", id], queryFn: () => ProductionOrdersApi.get(id!), enabled: !!id });

  if (isLoading || !order) return <div className="p-10 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <PrintPreviewLayout
      title="أمر تشغيل"
      documentNumber={order.orderNumber}
      subtitleLines={[
        { label: "العميل", value: `${order.customerCode} - ${order.customerName}` },
        { label: "الصنف", value: `${order.itemCode} - ${order.itemName}${order.color ? ` (${order.color})` : ""}` },
        { label: "الحالة", value: order.status }
      ]}
      columns={[
        { header: "المرحلة", render: (s) => s.stageName },
        { header: "الحالة", render: (s) => s.status },
        { header: "دخول", render: (s) => s.inputKg ?? "-", align: "end" },
        { header: "خروج", render: (s) => s.outputKg ?? "-", align: "end" },
        { header: "فاقد", render: (s) => s.lossKg ?? "-", align: "end" }
      ]}
      rows={order.stageExecutions}
      qrValue={`${window.location.origin}/scan/production-order/${order.id}`}
      notes={order.notes}
    />
  );
}
