import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { InvoicesApi } from "@/api/client";
import PrintPreviewLayout from "@/components/PrintPreviewLayout";

export default function InvoicePrintPage() {
  const { id } = useParams<{ id: string }>();
  const { data: invoice, isLoading } = useQuery({ queryKey: ["invoice-print", id], queryFn: () => InvoicesApi.get(id!), enabled: !!id });

  if (isLoading || !invoice) return <div className="p-10 text-center text-gray-400">جارٍ التحميل...</div>;

  return (
    <PrintPreviewLayout
      title="فاتورة"
      documentNumber={invoice.invoiceNumber}
      subtitleLines={[
        { label: "العميل", value: `${invoice.customerCode} - ${invoice.customerName}` },
        { label: "التاريخ", value: new Date(invoice.invoiceDate).toLocaleDateString("en-GB") },
        { label: "الحالة", value: invoice.status }
      ]}
      columns={[
        { header: "الصنف", render: (l) => `${l.itemCode}${l.color ? ` (${l.color})` : ""}` },
        { header: "أمر التشغيل", render: (l) => l.productionOrderNumber ?? "-" },
        { header: "الكمية", render: (l) => l.quantity, align: "end" },
        { header: "السعر", render: (l) => l.processingPrice, align: "end" },
        { header: "القيمة", render: (l) => l.value, align: "end" }
      ]}
      rows={invoice.lines}
      totals={[
        { label: "الإجمالي الفرعي", value: String(invoice.subTotal) },
        { label: "الخصم", value: String(invoice.discount) },
        { label: "الضريبة", value: String(invoice.tax) },
        { label: "الصافي", value: String(invoice.total) }
      ]}
      qrValue={`${window.location.origin}/scan/invoice/${invoice.id}`}
      notes={invoice.notes}
    />
  );
}
