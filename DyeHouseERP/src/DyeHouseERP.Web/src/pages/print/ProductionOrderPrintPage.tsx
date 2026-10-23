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
        {
          header: "الدخول (كجم / متر)",
          render: (s) => [s.inputKg != null ? `${s.inputKg} كجم` : null, s.inputMeter != null ? `${s.inputMeter} متر` : null].filter(Boolean).join(" / ") || "—",
          align: "end"
        },
        {
          header: "الخروج (كجم / متر)",
          render: (s) => [s.outputKg != null ? `${s.outputKg} كجم` : null, s.outputMeter != null ? `${s.outputMeter} متر` : null].filter(Boolean).join(" / ") || "—",
          align: "end"
        },
        {
          header: "الدرجة الثانية (كجم / متر)",
          render: (s) => [s.secondGradeKg != null ? `${s.secondGradeKg} كجم` : null, s.secondGradeMeter != null ? `${s.secondGradeMeter} متر` : null].filter(Boolean).join(" / ") || "—",
          align: "end"
        },
        { header: "عدد أتواب الدرجة الثانية", render: (s) => s.secondGradePieceCount ?? "—", align: "end" },
        {
          header: "المنفصلات (كجم / متر)",
          render: (s) => [s.separatesKg != null ? `${s.separatesKg} كجم` : null, s.separatesMeter != null ? `${s.separatesMeter} متر` : null].filter(Boolean).join(" / ") || "—",
          align: "end"
        },
        { header: "الهالك (كجم)", render: (s) => s.wasteKg ?? "—", align: "end" }
      ]}
      rows={order.stageExecutions}
      qrValue={`${window.location.origin}/scan/production-order/${order.id}`}
      notes={order.notes}
    />
  );
}
