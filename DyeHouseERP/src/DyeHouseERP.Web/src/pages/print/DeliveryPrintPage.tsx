import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { DeliveriesApi } from "@/api/client";
import { useI18n } from "@/i18n";
import PrintPreviewLayout from "@/components/PrintPreviewLayout";

export default function DeliveryPrintPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();
  const { data: delivery, isLoading, isError } = useQuery({ queryKey: ["delivery-print", id], queryFn: () => DeliveriesApi.get(id!), enabled: !!id });

  if (isError) return <div role="alert" className="p-10 text-center">{t("common.error")}</div>;
  if (isLoading) return <div className="p-10 text-center">{t("common.loading")}</div>;
  if (!delivery) return <div className="p-10 text-center">{t("common.noData")}</div>;

  return (
    <PrintPreviewLayout
      title="إذن تسليم"
      documentNumber={delivery.deliveryNumber}
      subtitleLines={[
        { label: "العميل", value: `${delivery.customerCode} - ${delivery.customerName}` },
        { label: "التاريخ", value: new Date(delivery.deliveryDate).toLocaleDateString("en-GB") },
        { label: "الحالة", value: delivery.status }
      ]}
      columns={[
        { header: t("printing.drawingNumber"), render: (l) => l.drawingNumbers?.join(", ") || "—" },
        { header: "أمر التشغيل", render: (l) => l.productionOrderNumber },
        { header: "الصنف", render: (l) => `${l.itemCode}${l.color ? ` (${l.color})` : ""}` },
        { header: "كجم", render: (l) => l.quantityKg ?? "-", align: "end" },
        { header: "متر", render: (l) => l.quantityMeter ?? "-", align: "end" },
        { header: "قطع", render: (l) => l.pieceCount ?? "-", align: "end" }
      ]}
      rows={delivery.lines}
      qrValue={`${window.location.origin}/scan/delivery/${delivery.id}`}
      notes={delivery.notes}
    />
  );
}
