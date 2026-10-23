import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ProductionOrdersApi } from "@/api/client";
import PrintPreviewLayout from "@/components/PrintPreviewLayout";

export default function ProductionOrderPrintPage() {
  const { id } = useParams<{ id: string }>();
  const { data: order, isLoading } = useQuery({ queryKey: ["production-order-print", id], queryFn: () => ProductionOrdersApi.get(id!), enabled: !!id });

  if (isLoading || !order) return <div className="p-10 text-center text-ink-subtle">جارٍ التحميل...</div>;

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
        { header: "دخول (كجم)", render: (s) => s.inputKg ?? "—", align: "end" },
        { header: "دخول (متر)", render: (s) => s.inputMeter ?? "—", align: "end" },
        { header: "خروج (كجم)", render: (s) => s.outputKg ?? "—", align: "end" },
        { header: "خروج (متر)", render: (s) => s.outputMeter ?? "—", align: "end" },
        { header: "الدرجة الثانية (كجم)", render: (s) => s.secondGradeKg ?? "—", align: "end" },
        { header: "الدرجة الثانية (متر)", render: (s) => s.secondGradeMeter ?? "—", align: "end" },
        { header: "أتواب الدرجة الثانية", render: (s) => s.secondGradePieceCount ?? "—", align: "end" },
        { header: "المنفصلات (كجم)", render: (s) => s.separatesKg ?? "—", align: "end" },
        { header: "المنفصلات (متر)", render: (s) => s.separatesMeter ?? "—", align: "end" },
        { header: "الهالك (كجم)", render: (s) => s.lossKg ?? "—", align: "end" }
      ]}
      rows={order.stageExecutions}
      qrValue={`${window.location.origin}/scan/production-order/${order.id}`}
      notes={order.notes}
    />
  );
}
