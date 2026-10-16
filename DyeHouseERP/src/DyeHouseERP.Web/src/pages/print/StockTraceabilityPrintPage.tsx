import { useMemo } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { FileDown } from "lucide-react";
import {
  InventoryTraceabilityApi,
  type StockScope,
  type StockTraceabilityFilters,
  type StockTraceMovement
} from "@/api/client";
import { InventoryTraceabilityExports } from "@/api/exports";
import PrintPreviewLayout, { PrintToolbar, type PrintColumn } from "@/components/PrintPreviewLayout";
import { useI18n } from "@/i18n";

/**
 * LOT TRACEABILITY CARD (بطاقة تتبع اللوط) - the official, print-ready document for ONE lot, and the
 * batch renderer that prints SEVERAL lots as independent cards, one per sheet.
 *
 * It IS the same traceability read the screen uses (spec sections 10, 14, 18, 30): the endpoint is
 * called with the lot's IDENTITY filter only, so a card shows exactly that lot's ledger movements and
 * nothing is re-summed, re-stored or re-derived here. Each row carries the CUMULATIVE balance the lot
 * reached after it - which is what makes the card prove, on paper, that the closing balance is the sum
 * of exactly these movements and no others.
 *
 * Rules the cards keep, because getting them wrong is how a printed document lies:
 *   * KG and Meter are INDEPENDENT - each is printed in its own figure, and a unit the lot never
 *     recorded prints "—" rather than a zero that would read as a real measurement;
 *   * the lot's identity is per warehouse (a receipt message in the raw warehouse, a job order in the
 *     ready warehouse, a (material, warehouse) lot in the materials warehouse);
 *   * a batch never lets two cards share a sheet, and a card that failed to load says so in its own
 *     place instead of silently dropping out of the document;
 *   * READ-ONLY: printing or downloading posts nothing and moves no stock.
 */

/** How the URL names one or many lots: /print/stock-traceability/:scope?lots=id,id,id */
const isScope = (value: string | undefined): value is StockScope =>
  value === "raw" || value === "ready" || value === "materials";

export default function StockTraceabilityPrintPage() {
  const { t } = useI18n();
  const { scope: scopeParam } = useParams<{ scope: string }>();
  const [params] = useSearchParams();
  const scope: StockScope = isScope(scopeParam) ? scopeParam : "raw";

  // De-duplicated: the same lot selected twice must print one card, not two.
  const lotIds = useMemo(
    () => Array.from(new Set((params.get("lots") ?? "").split(",").map((s) => s.trim()).filter(Boolean))),
    [params]);

  if (lotIds.length === 0) {
    return (
      <div className="min-h-screen bg-surface-sunken">
        <PrintToolbar />
        <div className="mx-auto my-10 max-w-[210mm] rounded-2xl border border-line bg-surface p-10 text-center" dir="rtl">
          <div className="text-lg font-semibold">{t("stock.print.noLots", "لم يتم تحديد أي لوط للطباعة")}</div>
          <p className="mt-2 text-sm text-ink-muted">
            {t("stock.print.noLotsHint", "ارجع إلى قائمة الأرصدة، حدّد لوطًا أو أكثر، ثم اطبع بطاقاته.")}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-surface-sunken">
      {/* One toolbar for the whole document: the pages themselves must not each carry a sticky bar. */}
      <PrintToolbar
        note={lotIds.length > 1 ? `${t("stock.print.cards", "بطاقات")} · ${lotIds.length}` : undefined}
      />
      {lotIds.map((lotId, index) => (
        <LotTraceabilityCard
          key={lotId}
          scope={scope}
          lotId={lotId}
          pageBreakAfter={index < lotIds.length - 1}
        />
      ))}
    </div>
  );
}

/** One sheet: a single lot's identity, movement log and cumulative balance. */
function LotTraceabilityCard({ scope, lotId, pageBreakAfter }: {
  scope: StockScope; lotId: string; pageBreakAfter: boolean;
}) {
  const { t } = useI18n();
  const isMaterials = scope === "materials";

  const { data, isLoading, isError } = useQuery({
    queryKey: ["stock-traceability-print", scope, lotId],
    queryFn: () => {
      // The lot's IDENTITY filter only: a card must never be narrowed by a date or a movement kind,
      // because those filters shorten the MOVEMENT LIST without changing the balance, and a card whose
      // rows do not add up to its own closing balance would be a false document.
      const filters: StockTraceabilityFilters = { scope, includeDelivered: true };
      if (scope === "raw") filters.rawMessageId = lotId;
      else if (scope === "ready") filters.productionOrderId = lotId;
      else filters.materialId = lotId;
      return InventoryTraceabilityApi.get(filters);
    }
  });

  const rows = data?.rows ?? [];
  // Memoised so the sort below re-runs when the ledger read changes, not on every render.
  const movements = useMemo(() => data?.movements ?? [], [data?.movements]);
  const lot = rows[0];

  // Chronological (oldest first), the order a running-balance ledger is read in, so the cumulative
  // column accumulates down the sheet. The figures are the query's own - nothing is recomputed.
  const ordered = useMemo(
    () => [...movements].sort((a, b) =>
      a.date === b.date ? a.createdAtUtc.localeCompare(b.createdAtUtc) : a.date.localeCompare(b.date)),
    [movements]);

  // A unit no visible row ever recorded has no figure at all: an empty KG or Meter column would imply
  // the unit exists for this lot.
  const hasMeter = ordered.some((m) => m.quantityMeter != null || m.balanceAfterMeter != null)
    || rows.some((r) => r.remainingMeter != null || r.receivedMeter != null
      || r.producedMeter != null || r.deliveredMeter != null);

  const number = (v: number | null | undefined) =>
    (v === null || v === undefined ? "—" : v.toLocaleString("en-US", { maximumFractionDigits: 3 }));

  const lotNumber = lot?.messageNumber ?? lot?.orderNumber ?? lot?.materialCode ?? lotId;
  const lotStatus = lot?.orderStatus ?? lot?.messageStatus ?? lot?.materialKind ?? "—";
  const lotDate = lot?.messageDate ?? lot?.orderDate;

  // A lot can be more than one line (a raw receipt message may carry several item lines), so the
  // header names EVERY distinct item on the card instead of the first row's - naming one line's item
  // on a multi-line lot would be a false identity on an official document.
  const distinct = (values: (string | null | undefined)[]) =>
    Array.from(new Set(values.filter((v): v is string => !!v && v.length > 0)));
  const itemLabel = isMaterials
    ? `${lot?.materialCode ?? "—"} · ${lot?.materialName ?? ""}`.trim()
    : distinct(rows.map((r) => `${r.itemCode} · ${r.itemName}`.trim())).join("  /  ");
  const customerLabel = distinct(rows.map((r) => `${r.customerCode ?? ""} - ${r.customerName ?? ""}`.trim())).join("  /  ");
  // The colour is shown only when the whole lot carries one - it is part of the lot's identity, not of
  // a single line, and a mixed lot must not print as if it were one colour.
  const lotColor = distinct(rows.map((r) => r.color)).length === 1 ? rows[0]?.color : null;

  const qrValue = scope === "raw" && lot?.rawMessageId
    ? `${window.location.origin}/scan/raw-message/${lot.rawMessageId}`
    : scope === "ready" && lot?.productionOrderId
      ? `${window.location.origin}/scan/production-order/${lot.productionOrderId}`
      : undefined;

  const columns: PrintColumn<StockTraceMovement>[] = [
    { header: t("common.date", "التاريخ"), render: (m) => m.date.slice(0, 10) },
    {
      header: t("stock.movementKind", "نوع الحركة"),
      render: (m) => (
        <>
          {t(`stock.kind.${m.kind}`, m.kind)}
          {m.isReversal && <span className="text-2xs text-ink-subtle"> · {t("stock.reversal", "عكس حركة")}</span>}
        </>
      )
    },
    {
      header: t("stock.document", "المستند"),
      render: (m) => (
        <>
          <div className="font-medium ltr-nums">{m.documentNumber}</div>
          <div className="text-2xs text-ink-subtle">
            {m.documentType}
            {m.warehouseName ? ` · ${m.warehouseName}` : ""}
          </div>
        </>
      )
    },
    {
      header: t("stock.direction", "الاتجاه"),
      render: (m) => (m.direction === "Out" ? t("stock.out", "صادر") : t("stock.in", "وارد"))
    },
    {
      header: isMaterials ? t("common.quantity", "الكمية") : `KG / ${t("common.meter", "م")}`,
      align: "end",
      render: (m) => (isMaterials
        ? `${number(m.quantity)} ${t(`material.unit.${m.unit}`, m.unit ?? "")}`.trim()
        : (
          <>
            {m.quantityKg != null && <div>{number(m.quantityKg)}</div>}
            {hasMeter && m.quantityMeter != null && <div className="text-2xs text-ink-muted">{number(m.quantityMeter)}</div>}
            {m.quantityKg == null && m.quantityMeter == null && "—"}
          </>
        ))
    },
    {
      header: t("stock.print.runningBalance", "الرصيد التراكمي"),
      align: "end",
      render: (m) => (isMaterials
        ? number(m.balanceAfter)
        : (
          <>
            <div>{number(m.balanceAfterKg)}</div>
            {hasMeter && <div className="text-2xs text-ink-muted">{number(m.balanceAfterMeter)}</div>}
          </>
        ))
    },
    { header: t("common.user", "المستخدم"), render: (m) => m.createdBy }
  ];

  const totals = isMaterials
    ? [
        { label: t("stock.received", "المستلم"), value: number(data?.totalReceivedQuantity) },
        { label: t("stock.issued", "المصروف"), value: number(data?.totalIssuedQuantity) },
        { label: t("stock.returned", "المرتجع"), value: number(data?.totalReturnedQuantity) },
        { label: t("stock.print.closingBalance", "الرصيد الختامي"), value: number(data?.totalRemainingQuantity) }
      ]
    : [
        { label: t("stock.received", "المستلم"), value: number(data?.totalReceivedKg) },
        ...(scope === "ready" ? [{ label: t("stock.produced", "المنتج"), value: number(data?.totalProducedKg) }] : []),
        { label: t("stock.delivered", "المسلّم للعميل"), value: number(data?.totalDeliveredKg) },
        { label: t("stock.print.closingBalanceKg", "الرصيد الختامي كجم"), value: number(data?.totalRemainingKg) },
        ...(hasMeter ? [{ label: t("stock.print.closingBalanceM", "الرصيد الختامي متر"), value: number(data?.totalRemainingMeter) }] : [])
      ];

  // Each card reports its OWN failure: one unreadable/unknown lot must not blank the whole batch.
  if (isError) {
    return (
      <CardNotice tone="danger" pageBreakAfter={pageBreakAfter}>
        {t("stock.print.loadFailed", "تعذّر تحميل بيانات هذا اللوط. تحقق من الاتصال أو من صلاحيتك على بيانات المخزون.")}
      </CardNotice>
    );
  }
  if (isLoading) {
    return <CardNotice pageBreakAfter={pageBreakAfter}>{t("common.loading", "جارٍ التحميل...")}</CardNotice>;
  }
  if (!lot) {
    return (
      <CardNotice tone="danger" pageBreakAfter={pageBreakAfter}>
        {t("stock.print.lotMissing", "لا يوجد لوط بهذا الرقم، أو لا صلاحية لديك لعرضه.")}
      </CardNotice>
    );
  }

  return (
    <>
      {/* Per-lot download, never printed: the server builds the very same card as a PDF. */}
      <div className="no-print mx-auto mt-6 flex max-w-[210mm] items-center justify-end gap-2 px-1" dir="rtl">
        <span className="me-auto text-xs text-ink-subtle ltr-nums">{lotNumber}</span>
        <button
          type="button"
          className="btn btn-ghost btn-sm"
          onClick={InventoryTraceabilityExports.lotCardPdf(scope, lotId, lotNumber)}
        >
          <FileDown size={14} />
          {t("stock.print.downloadPdf", "تنزيل PDF")}
        </button>
      </div>

      <PrintPreviewLayout
        bare
        pageBreakAfter={pageBreakAfter}
        title={t("stock.print.title", "بطاقة تتبع اللوط")}
        documentNumber={lotNumber}
        subtitleLines={[
          { label: t("stock.print.scope", "المخزن"), value: t(scopeLabelKey[scope], scope) },
          ...(isMaterials
            ? [{ label: t("stock.material", "المادة / الكيماوي"), value: `${lot.materialCode ?? "—"} · ${lot.materialName ?? ""}`.trim() }]
            : [
                { label: t("common.item", "الصنف"), value: itemLabel },
                ...(lotColor ? [{ label: t("stock.print.color", "اللون"), value: lotColor }] : []),
                { label: t("common.customer", "العميل"), value: customerLabel }
              ]),
          { label: t("stock.print.lot", "رقم اللوط"), value: lotNumber },
          { label: t("common.status", "الحالة"), value: lotStatus },
          ...(lot.warehouseName ? [{ label: t("wh.master", "المخزن"), value: lot.warehouseName }] : []),
          ...(lotDate ? [{ label: t("common.date", "التاريخ"), value: lotDate.slice(0, 10) }] : []),
          {
            label: t("stock.print.generatedAt", "تاريخ الطباعة"),
            value: new Date(data?.generatedAtUtc ?? new Date().toISOString()).toLocaleString("en-GB")
          }
        ]}
        columns={columns}
        rows={ordered}
        totals={totals}
        qrValue={qrValue}
        notes={t("stock.balanceIsLive", "الرصيد محسوب لحظيًا من دفتر الحركات: التصفية بالتاريخ أو نوع الحركة تقصر قائمة الحركات فقط ولا تغيّر الرصيد.")}
      />
    </>
  );
}

const scopeLabelKey: Record<StockScope, string> = {
  raw: "stock.print.scope.raw",
  ready: "stock.print.scope.ready",
  materials: "stock.print.scope.materials"
};

/** A card that could not render says why, in its own place, and still respects the page break. */
function CardNotice({ children, tone, pageBreakAfter }: {
  children: React.ReactNode; tone?: "danger"; pageBreakAfter: boolean;
}) {
  return (
    <div
      dir="rtl"
      className={`mx-auto my-6 max-w-[210mm] rounded-2xl border bg-surface p-10 text-center ${
        pageBreakAfter ? "sheet-break" : ""} ${
        tone === "danger" ? "border-danger-ink/40 text-danger-ink" : "border-line text-ink-subtle"}`}
    >
      {children}
    </div>
  );
}
