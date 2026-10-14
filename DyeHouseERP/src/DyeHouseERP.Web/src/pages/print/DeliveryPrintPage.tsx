import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { DeliveriesApi, type DeliveryLine } from "@/api/client";
import { useI18n } from "@/i18n";
import PrintPreviewLayout, { type PrintColumn } from "@/components/PrintPreviewLayout";

/**
 * PRINTABLE DELIVERY PERMIT (إذن تسليم) - spec sections 31 and 39.
 *
 * The document a customer receives. On a sub-customer permit it names BOTH numbers: the sub-permit's
 * own number (from its own SUB- sequence, never shared with the primary permits) and the primary
 * permit it was issued against, plus that permit's customer by code and name only.
 *
 * The three QUANTITY columns are choosable - KG, Meter and the roll count - because a sub-customer
 * who receives goods by the metre only does not want an empty KG column on their paper. The choice:
 *   * affects the PRINT-OUT ONLY: it never edits or deletes a recorded quantity, and the document
 *     keeps storing every figure exactly as it was entered;
 *   * never touches the identifying data - company identity, recipient, job order, item and colour
 *     always print;
 *   * is per document format: a sub-customer permit's choice is stored separately from an ordinary
 *     permit's, so setting up one never changes the other;
 *   * is remembered like the language and theme switches (localStorage), defaulting to all columns.
 */
type QuantityColumns = { kg: boolean; meter: boolean; rolls: boolean };

/** Everything prints by default - hiding a column is an explicit act. */
const DEFAULT_COLUMNS: QuantityColumns = { kg: true, meter: true, rolls: true };

/** A sub-customer permit keeps its own print format, independent of every other document. */
const storageKeyFor = (isSubCustomerPermit: boolean) =>
  isSubCustomerPermit ? "dyehouse_delivery_print_columns.sub" : "dyehouse_delivery_print_columns";

const readColumns = (storageKey: string): QuantityColumns => {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return DEFAULT_COLUMNS;
    const parsed = JSON.parse(raw) as Partial<QuantityColumns>;
    return {
      kg: typeof parsed.kg === "boolean" ? parsed.kg : DEFAULT_COLUMNS.kg,
      meter: typeof parsed.meter === "boolean" ? parsed.meter : DEFAULT_COLUMNS.meter,
      rolls: typeof parsed.rolls === "boolean" ? parsed.rolls : DEFAULT_COLUMNS.rolls
    };
  } catch {
    // Private mode or a corrupt value - the defaults still print a complete document.
    return DEFAULT_COLUMNS;
  }
};

export default function DeliveryPrintPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();
  const { data: delivery, isLoading, isError } = useQuery({ queryKey: ["delivery-print", id], queryFn: () => DeliveriesApi.get(id!), enabled: !!id });

  const [quantityColumns, setQuantityColumns] = useState<QuantityColumns>(DEFAULT_COLUMNS);
  const [loadedKey, setLoadedKey] = useState<string | null>(null);

  // The permit's own kind decides WHICH stored choice applies, and that is only known once it has
  // loaded - so the remembered choice is read as soon as the key is known.
  const storageKey = delivery ? storageKeyFor(delivery.isSubCustomerPermit) : null;

  useEffect(() => {
    if (!storageKey || loadedKey === storageKey) return;
    setQuantityColumns(readColumns(storageKey));
    setLoadedKey(storageKey);
  }, [storageKey, loadedKey]);

  const toggleColumn = (key: keyof QuantityColumns) =>
    setQuantityColumns((prev) => {
      const next = { ...prev, [key]: !prev[key] };
      if (storageKey) {
        try {
          localStorage.setItem(storageKey, JSON.stringify(next));
        } catch {
          // Private mode - the choice still applies for this session.
        }
      }
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
      <div className="no-print mx-auto mt-4 max-w-[210mm] rounded-lg border border-gray-200 bg-white p-4" dir="rtl">
        <div className="mb-2 text-sm font-semibold text-gray-700">{t("del.printColumns")}</div>
        <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm text-gray-600">
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
        <p className="mt-2 text-2xs text-gray-500">{t("del.printColumnsHint")}</p>
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
