import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { DeliveriesApi, type DeliveryLine } from "@/api/client";
import { useI18n } from "@/i18n";
import PrintPreviewLayout, { type PrintColumn } from "@/components/PrintPreviewLayout";
import {
  ALL_DELIVERY_QUANTITY_COLUMNS, deliveryColumnsStorageKey, readDeliveryQuantityColumns,
  saveDeliveryQuantityColumns, type DeliveryQuantityColumns
} from "@/deliveryPrintColumns";

/**
 * PRINTABLE DELIVERY PERMIT (إذن تسليم) - spec sections 31 and 39.
 *
 * The document a customer receives. On a sub-customer permit it names BOTH numbers: the sub-permit's
 * own number (from its own SUB- sequence, never shared with the primary permits) and the primary
 * permit it was issued against, plus that permit's customer by code and name only.
 *
 * The three QUANTITY columns are choosable - KG, Meter and the roll count - because a sub-customer
 * who receives goods by the metre only does not want an empty KG column on their paper. The choice
 * lives in one place (see src/deliveryPrintColumns.ts) because the register's PDF download makes the
 * same choice: it affects the print-out ONLY, deletes no recorded quantity, and never hides the
 * identifying data (company, recipient, job order, item, colour).
 */

export default function DeliveryPrintPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();
  const { data: delivery, isLoading, isError } = useQuery({ queryKey: ["delivery-print", id], queryFn: () => DeliveriesApi.get(id!), enabled: !!id });

  const [quantityColumns, setQuantityColumns] = useState<DeliveryQuantityColumns>(ALL_DELIVERY_QUANTITY_COLUMNS);
  const [loadedKey, setLoadedKey] = useState<string | null>(null);

  // The permit's own kind decides WHICH stored choice applies, and that is only known once it has
  // loaded - so the remembered choice is read as soon as the key is known.
  const storageKey = delivery ? deliveryColumnsStorageKey(delivery.isSubCustomerPermit) : null;

  useEffect(() => {
    if (!storageKey || loadedKey === storageKey) return;
    setQuantityColumns(readDeliveryQuantityColumns(storageKey));
    setLoadedKey(storageKey);
  }, [storageKey, loadedKey]);

  const toggleColumn = (key: keyof DeliveryQuantityColumns) =>
    setQuantityColumns((prev) => {
      const next = { ...prev, [key]: !prev[key] };
      if (storageKey) saveDeliveryQuantityColumns(storageKey, next);
      return next;
    });

  if (isError) return <div role="alert" className="p-10 text-center">{t("common.error")}</div>;
  if (isLoading) return <div className="p-10 text-center">{t("common.loading")}</div>;
  if (!delivery) return <div className="p-10 text-center">{t("common.noData")}</div>;

  // Identifying columns first and always: they are what makes the document the document.
  const columns: PrintColumn<DeliveryLine>[] = [
    { header: t("printing.drawingNumber"), render: (l) => l.drawingNumbers?.join(", ") || "—" },
    { header: t("prod.jobOrder", "أمر التشغيل"), render: (l) => l.productionOrderNumber },
    { header: t("common.item"), render: (l) => `${l.itemCode}${l.color ? ` (${l.color})` : ""}` }
  ];

  if (quantityColumns.kg) {
    columns.push({ header: t("del.printColumnKg"), render: (l) => l.quantityKg ?? "—", align: "end" });
  }
  if (quantityColumns.meter) {
    columns.push({ header: t("del.printColumnMeter"), render: (l) => l.quantityMeter ?? "—", align: "end" });
  }
  if (quantityColumns.rolls) {
    columns.push({ header: t("del.printColumnRolls"), render: (l) => l.pieceCount ?? "—", align: "end" });
  }

  return (
    <>
      {/* Column chooser. Deliberately part of the screen rather than of the document - it is hidden
          by the print stylesheet, so a printed permit never carries the controls. */}
      <div className="no-print mx-auto mt-4 max-w-[210mm] rounded-2xl border border-line bg-surface p-4" dir="rtl">
        <div className="mb-2 text-sm font-semibold text-ink">{t("del.printColumns")}</div>
        <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm text-ink-muted">
          {(
            [
              ["kg", t("del.printColumnKg")],
              ["meter", t("del.printColumnMeter")],
              ["rolls", t("del.printColumnRolls")]
            ] as const
          ).map(([key, label]) => (
            <label key={key} className="flex items-center gap-1.5">
              <input type="checkbox" checked={quantityColumns[key]} onChange={() => toggleColumn(key)} />
              {label}
            </label>
          ))}
        </div>
        <p className="mt-2 text-2xs text-ink-muted">{t("del.printColumnsHint")}</p>
      </div>

      <PrintPreviewLayout
        title="إذن تسليم"
        documentNumber={delivery.deliveryNumber}
        subtitleLines={[
          { label: t("del.recipient"), value: `${delivery.customerCode} - ${delivery.customerName}` },
          { label: "التاريخ", value: new Date(delivery.deliveryDate).toLocaleDateString("en-GB") },
          { label: "الحالة", value: delivery.status },
          // A sub-customer permit carries the primary permit's number and its customer's code/name -
          // nothing else of the primary customer's record travels onto this document.
          ...(delivery.primaryDeliveryNumber
            ? [
                { label: t("del.primaryPermit"), value: delivery.primaryDeliveryNumber },
                {
                  label: t("del.primaryCustomer"),
                  value: `${delivery.primaryCustomerCode ?? ""} ${delivery.primaryCustomerName ?? ""}`.trim()
                }
              ]
            : [])
        ]}
        columns={columns}
        rows={delivery.lines}
        qrValue={`${window.location.origin}/scan/delivery/${delivery.id}`}
        notes={delivery.notes}
      />
    </>
  );
}
