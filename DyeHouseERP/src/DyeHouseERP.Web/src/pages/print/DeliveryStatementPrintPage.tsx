import { useMemo, useState } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { DeliveriesApi, type DeliveryStatementRow } from "@/api/client";
import { useI18n } from "@/i18n";
import PrintPreviewLayout, { type PrintColumn } from "@/components/PrintPreviewLayout";

/**
 * DELIVERY STATEMENT (بيان تسليم) - spec section 31.
 *
 * The document reports, per delivered line, the production accounting behind it: the ORIGINAL raw
 * quantity the job started from, what this delivery settles of it, what is still outstanding, the
 * ready-goods quantities handed over, the bale count, the scraps/separates and second-grade the
 * operations recorded, the calculated waste and the drawing number when the order passed Printing.
 *
 * Two rules the screen has to make obvious, because getting them wrong is how a statement lies:
 *   * KG and Meter are INDEPENDENT - a column that has no figure shows "—", never a zero that reads
 *     as a real measurement, and the waste is never computed across the two units;
 *   * the ORIGINAL quantity and the ORIGINAL Job Order date are the order's own, so a later partial
 *     delivery shows when the job was taken in and what it started from - not today's figures.
 *
 * The columns are CONFIGURABLE: a factory prints a different statement per document format (a
 * customer's copy does not want the waste breakdown; the floor copy does). Everything is read-only -
 * this page posts nothing, so printing it never moves stock.
 */
export default function DeliveryStatementPrintPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();

  const { data: statement, isLoading, isError } = useQuery({
    queryKey: ["delivery-statement", id],
    queryFn: () => DeliveriesApi.statement(id!),
    enabled: !!id
  });

  // Which optional columns this print-out shows. The identifying columns (job order, item, quantity
  // handed over) are always printed - the rest can be switched off for a given document format.
  const [optional, setOptional] = useState({
    originalRaw: true,
    settled: true,
    remaining: true,
    bales: true,
    scraps: false,
    secondGrade: false,
    waste: true,
    drawing: true,
    dates: true,
    notes: false
  });

  const toggle = (key: keyof typeof optional) => setOptional((prev) => ({ ...prev, [key]: !prev[key] }));

  const columns = useMemo<PrintColumn<DeliveryStatementRow>[]>(() => {
    const cells: PrintColumn<DeliveryStatementRow>[] = [
      { header: t("statement.jobOrder", "أمر التشغيل"), render: (r: DeliveryStatementRow) => r.productionOrderNumber }
    ];

    if (optional.dates) {
      cells.push({
        header: t("statement.orderDate", "تاريخ الأمر الأصلي"),
        render: (r: DeliveryStatementRow) => (r.productionOrderDate ? new Date(r.productionOrderDate).toLocaleDateString("en-GB") : "—")
      });
    }

    cells.push({
      header: t("statement.item", "الصنف"),
      render: (r: DeliveryStatementRow) => `${r.itemCode}${r.color ? ` (${r.color})` : ""}`
    });

    if (optional.originalRaw) {
      cells.push(
        { header: t("statement.originalKg", "خام أصلي كجم"), render: (r: DeliveryStatementRow) => r.originalRawQuantityKg ?? "—", align: "end" },
        { header: t("statement.originalM", "خام أصلي متر"), render: (r: DeliveryStatementRow) => r.originalRawQuantityMeter ?? "—", align: "end" }
      );
    }

    if (optional.settled) {
      cells.push(
        { header: t("statement.settledKg", "المُسوَّى كجم"), render: (r: DeliveryStatementRow) => r.deliveredOriginalRawQuantityKg ?? "—", align: "end" },
        { header: t("statement.settledM", "المُسوَّى متر"), render: (r: DeliveryStatementRow) => r.deliveredOriginalRawQuantityMeter ?? "—", align: "end" }
      );
    }

    if (optional.remaining) {
      cells.push(
        { header: t("statement.remainingKg", "المتبقي كجم"), render: (r: DeliveryStatementRow) => r.remainingOriginalRawKg, align: "end" },
        { header: t("statement.remainingM", "المتبقي متر"), render: (r: DeliveryStatementRow) => r.remainingOriginalRawMeter, align: "end" }
      );
    }

    cells.push(
      { header: t("statement.readyKg", "جاهز كجم"), render: (r: DeliveryStatementRow) => r.readyGoodsQuantityKg ?? "—", align: "end" },
      { header: t("statement.readyM", "جاهز متر"), render: (r: DeliveryStatementRow) => r.readyGoodsQuantityMeter ?? "—", align: "end" }
    );

    if (optional.bales) {
      cells.push({ header: t("statement.bales", "عدد البالات"), render: (r: DeliveryStatementRow) => r.baleCount ?? "—", align: "end" });
    }

    if (optional.scraps) {
      cells.push(
        { header: t("statement.scrapsKg", "قصاصات كجم"), render: (r: DeliveryStatementRow) => r.scrapsKg ?? "—", align: "end" },
        { header: t("statement.scrapsM", "قصاصات متر"), render: (r: DeliveryStatementRow) => r.scrapsMeter ?? "—", align: "end" }
      );
    }

    if (optional.secondGrade) {
      cells.push(
        { header: t("statement.secondKg", "درجة ثانية كجم"), render: (r: DeliveryStatementRow) => r.secondGradeKg ?? "—", align: "end" },
        { header: t("statement.secondM", "درجة ثانية متر"), render: (r: DeliveryStatementRow) => r.secondGradeMeter ?? "—", align: "end" }
      );
    }

    if (optional.waste) {
      cells.push(
        { header: t("statement.wasteKg", "الهالك كجم"), render: (r: DeliveryStatementRow) => r.calculatedWasteKg ?? "—", align: "end" },
        { header: t("statement.wasteM", "الهالك متر"), render: (r: DeliveryStatementRow) => r.calculatedWasteMeter ?? "—", align: "end" }
      );
    }

    if (optional.drawing) {
      cells.push({
        header: t("printing.drawingNumber", "رقم الرسمة"),
        render: (r: DeliveryStatementRow) => (r.drawingNumbers.length > 0 ? r.drawingNumbers.join(", ") : "—")
      });
    }

    if (optional.notes) {
      cells.push({ header: t("common.notes", "ملاحظات"), render: (r: DeliveryStatementRow) => r.lineNotes ?? "—" });
    }

    return cells;
  }, [optional, t]);

  if (isError) return <div role="alert" className="p-10 text-center">{t("common.error")}</div>;
  if (isLoading) return <div className="p-10 text-center">{t("common.loading")}</div>;
  if (!statement) return <div className="p-10 text-center">{t("common.noData")}</div>;

  return (
    <>
      {/* Column chooser. Deliberately part of the page rather than the printed document - it is
          hidden by the print stylesheet, so a printed statement never carries the controls. */}
      <div className="no-print mx-auto mt-4 max-w-[210mm] rounded-lg border border-gray-200 bg-white p-4" dir="rtl">
        <div className="mb-2 text-sm font-semibold text-gray-700">
          {t("statement.columns", "الأعمدة الظاهرة في البيان")}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm text-gray-600">
          {(
            [
              ["dates", t("statement.orderDate", "تاريخ الأمر الأصلي")],
              ["originalRaw", t("statement.originalRaw", "الخام الأصلي")],
              ["settled", t("statement.settled", "المُسوَّى في هذا التسليم")],
              ["remaining", t("statement.remaining", "المتبقي")],
              ["bales", t("statement.bales", "عدد البالات")],
              ["scraps", t("statement.scraps", "القصاصات")],
              ["secondGrade", t("statement.secondGrade", "الدرجة الثانية")],
              ["waste", t("statement.waste", "الهالك المحسوب")],
              ["drawing", t("printing.drawingNumber", "رقم الرسمة")],
              ["notes", t("common.notes", "ملاحظات")]
            ] as const
          ).map(([key, label]) => (
            <label key={key} className="flex items-center gap-1.5">
              <input type="checkbox" checked={optional[key]} onChange={() => toggle(key)} />
              {label}
            </label>
          ))}
        </div>
        <p className="mt-2 text-2xs text-gray-500">
          {t("statement.unitHint", "الوحدات مستقلة: «—» تعني أن الوحدة غير مسجَّلة، والهالك لا يُحسب بين كجم والمتر.")}
        </p>
      </div>

      <PrintPreviewLayout
        title={t("statement.title", "بيان تسليم")}
        documentNumber={statement.deliveryNumber}
        subtitleLines={[
          { label: t("common.customer", "العميل"), value: `${statement.customerCode} - ${statement.customerName}` },
          { label: t("common.date", "التاريخ"), value: new Date(statement.deliveryDate).toLocaleDateString("en-GB") },
          { label: t("common.status", "الحالة"), value: statement.status },
          // A sub-customer permit identifies the primary permit and its customer by code and name
          // ONLY - nothing else of the primary customer's record travels onto this document.
          ...(statement.primaryDeliveryNumber
            ? [
                { label: t("statement.primaryPermit", "الإذن الأساسي"), value: statement.primaryDeliveryNumber },
                {
                  label: t("statement.primaryCustomer", "العميل الأساسي"),
                  value: `${statement.primaryCustomerCode ?? ""} ${statement.primaryCustomerName ?? ""}`.trim()
                }
              ]
            : [])
        ]}
        columns={columns}
        rows={statement.rows}
        qrValue={`${window.location.origin}/scan/delivery/${statement.deliveryId}`}
        notes={statement.notes}
      />
    </>
  );
}
