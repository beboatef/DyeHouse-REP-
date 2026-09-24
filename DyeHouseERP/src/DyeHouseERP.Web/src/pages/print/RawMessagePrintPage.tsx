import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { RawMessagesApi } from "@/api/client";
import PrintPreviewLayout from "@/components/PrintPreviewLayout";

export default function RawMessagePrintPage() {
  const { id } = useParams<{ id: string }>();
  const { data: message, isLoading } = useQuery({ queryKey: ["raw-message-print", id], queryFn: () => RawMessagesApi.get(id!), enabled: !!id });

  if (isLoading || !message) return <div className="p-10 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <PrintPreviewLayout
      title="إذن استلام خام"
      documentNumber={message.messageNumber}
      subtitleLines={[
        { label: "العميل", value: `${message.customerCode} - ${message.customerName}` },
        { label: "المخزن", value: message.warehouseName },
        { label: "التاريخ", value: new Date(message.receiptDate).toLocaleDateString("en-GB") },
        { label: "حالة الفحص", value: message.inspectionStatus }
      ]}
      columns={[
        { header: "الصنف", render: (l) => `${l.itemCode} - ${l.itemName}` },
        { header: "كجم", render: (l) => l.quantityKg ?? "-", align: "end" },
        { header: "متر", render: (l) => l.quantityMeter ?? "-", align: "end" },
        { header: "قطع", render: (l) => l.pieceCount ?? "-", align: "end" }
      ]}
      rows={message.lines}
      qrValue={`${window.location.origin}/scan/raw-message/${message.id}`}
      notes={message.notes}
    />
  );
}
