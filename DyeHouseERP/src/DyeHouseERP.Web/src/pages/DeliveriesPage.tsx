import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, DeliveriesApi, ReadyGoodsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";
import { ExportButtons } from "@/components/ExportButtons";
import { DeliveriesExports } from "@/api/exports";
import { deliveryColumnsStorageKey, readDeliveryQuantityColumns } from "@/deliveryPrintColumns";

const statusLabel: Record<string, string> = { Draft: "مسودة", Prepared: "مجهزة", Delivered: "تم التسليم", Cancelled: "ملغاة" };
const statusTone: Record<string, "gray" | "blue" | "green" | "red"> = { Draft: "gray", Prepared: "blue", Delivered: "green", Cancelled: "red" };

// Each line carries its OWN actual quantities, entered by hand for this delivery - including the
// original raw quantity it settles (spec section 31). Nothing here is derived from a previous
// delivery: a partial delivery is its own record, with its own figures.
//
// `sourceKey` identifies the lot the line slices ("<jobOrderId>|<itemId>"). The ITEM is never chosen
// on its own: it arrives with the source, and on a sub-customer permit the source and the colour both
// come from the primary permit being sliced (spec section 31).
type LineDraft = {
  sourceKey: string; productionOrderId: string; itemId: string; color: string;
  category: "FirstGrade" | "SecondGrade" | "Separates";
  quantityKg: string; quantityMeter: string; pieceCount: string;
  originalRawQuantityKg: string; originalRawQuantityMeter: string;
};
const emptyLine = (): LineDraft => ({
  sourceKey: "", productionOrderId: "", itemId: "", color: "", category: "FirstGrade",
  quantityKg: "", quantityMeter: "", pieceCount: "",
  originalRawQuantityKg: "", originalRawQuantityMeter: ""
});

export default function DeliveriesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [deliveryDate, setDeliveryDate] = useState(new Date().toISOString().slice(0, 10));
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);
  const [cancelReasonById, setCancelReasonById] = useState<Record<string, string>>({});
  // Post-approval correction (spec section 30): which delivery's editor is open,
  // and the new quantity typed for each of its existing lines.
  const [correctingId, setCorrectingId] = useState<string | null>(null);
  const [correctQty, setCorrectQty] = useState<Record<string, string>>({});
  // The original raw figures are hand-entered data too, so the correction editor can change them -
  // they are re-checked against the order's fixed original quantity by the API.
  const [correctOriginalRaw, setCorrectOriginalRaw] = useState<Record<string, string>>({});
  const [correctReason, setCorrectReason] = useState("");
  const [correctError, setCorrectError] = useState<string | null>(null);

  // Sub-customer permit (spec section 31): when set, this permit is issued AGAINST another
  // (primary) permit. It is a document link only - creating it moves no stock.
  const [primaryDeliveryId, setPrimaryDeliveryId] = useState("");

  // The idempotency token of the CURRENT delivery confirmation, minted per document. Submitting the
  // same confirmation twice (a double click, a retried request) posts the deduction exactly once.
  const [deliverTokens, setDeliverTokens] = useState<Record<string, string>>({});
  const tokenFor = (id: string) =>
    deliverTokens[id] ?? (typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : String(Date.now()));

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  // A sub-customer permit is issued AGAINST a primary permit and may name that primary customer's Job
  // Order, so while one is being issued the picker lists every ready lot (shown with its customer) -
  // the server still refuses any line that is not the sub-customer's own or the linked primary
  // customer's. An ordinary permit keeps the strict per-customer list.
  const { data: readyBalance } = useQuery({
    queryKey: ["ready-goods-balance", primaryDeliveryId || customerId],
    queryFn: () => ReadyGoodsApi.balance(primaryDeliveryId ? undefined : { customerId }),
    enabled: !!customerId
  });
  const { data: deliveries, isLoading } = useQuery({ queryKey: ["deliveries"], queryFn: () => DeliveriesApi.list() });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["deliveries"] });

  /**
   * The ready-goods lots the "new delivery" form can draw from are a LIVE balance, so every action that
   * changes it has to refresh them - otherwise the screen silently keeps an out-of-date list.
   *
   * This is the bug behind "a cancelled permit stops me issuing a new one": delivering emptied the lot
   * (the list refreshed and the lot vanished), and CANCELLING put the stock back but never refreshed the
   * list, so the lot the user needed to deliver again was simply not offered. The same applies to an
   * after-approval correction, which also moves ready stock.
   */
  const refreshReadyBalance = () => {
    qc.invalidateQueries({ queryKey: ["ready-goods-balance"] });
    qc.invalidateQueries({ queryKey: ["stock-traceability"] });
    qc.invalidateQueries({ queryKey: ["production-floor"] });
  };

  // The primary permit being sliced, when one was chosen. On a sub-customer permit the item and the
  // colour are taken from THIS permit's own lines - they are never independent master data, so the
  // screen never lets a line name goods the primary permit does not carry.
  const primaryPermit = deliveries?.find((d) => d.id === primaryDeliveryId) ?? null;

  // The recipient is chosen by hand on every permit. When one is issued as a sub-customer permit the
  // list narrows to the sub-customers of the primary permit's customer (the stored parent link); the
  // server re-checks that relationship and refuses anything else.
  const recipientOptions = useMemo(() => {
    const all = customers ?? [];
    return primaryPermit ? all.filter((c) => c.parentCustomerId === primaryPermit.customerId) : all;
  }, [customers, primaryPermit]);

  // Switching to (or between) primary permits can leave a recipient that is no longer allowed.
  useEffect(() => {
    if (primaryPermit && customerId && !recipientOptions.some((c) => c.id === customerId)) setCustomerId("");
  }, [primaryPermit, recipientOptions, customerId]);

  /**
   * Where a sub-permit line may be built from, keyed by "<jobOrderId>|<itemId>".
   *
   * Sub-customer permit: the primary permit's own lines first - that is what "the item comes from the
   * primary permit" means - followed by the sub-customer's OWN lots, whose item is their job order's
   * own item. An ordinary permit keeps the ready-goods lots of the chosen customer.
   */
  const sourceOptions = useMemo(() => {
    const asOption = (productionOrderId: string, itemId: string, category: "FirstGrade" | "SecondGrade" | "Separates" | "Waste", number: string, code: string, color: string | null | undefined, fromPrimary: boolean) => ({
      key: `${productionOrderId}|${itemId}|${category}`,
      productionOrderId, itemId, category,
      label: `${number} - ${code}${color ? ` (${color})` : ""}`,
      color: color ?? "", fromPrimary
    });

    if (!primaryPermit) {
      return (readyBalance ?? []).filter((b) => b.category !== "Waste").map((b) =>
        asOption(b.productionOrderId, b.itemId, b.category, b.productionOrderNumber, b.itemCode, b.color, false));
    }

    const fromPrimary = primaryPermit.lines.filter((l) => l.category !== "Waste").map((l) =>
      asOption(l.productionOrderId, l.itemId, l.category, l.productionOrderNumber, l.itemCode, l.color, true));

    const own = (readyBalance ?? [])
      .filter((b) => b.customerId === customerId)
      .map((b) => asOption(b.productionOrderId, b.itemId, b.category, b.productionOrderNumber, b.itemCode, b.color, false));

    const seen = new Set(fromPrimary.map((o) => o.key));
    return [...fromPrimary, ...own.filter((o) => !seen.has(o.key))];
  }, [primaryPermit, readyBalance, customerId]);

  /** The colours the primary permit recorded for one source line - the only ones it may be issued in. */
  const primaryColorsFor = useMemo(() => {
    const bySource = new Map<string, string[]>();
    for (const line of primaryPermit?.lines ?? []) {
      if (!line.color) continue;
      const key = `${line.productionOrderId}|${line.itemId}|${line.category}`;
      const colors = bySource.get(key) ?? [];
      if (!colors.includes(line.color)) colors.push(line.color);
      bySource.set(key, colors);
    }
    return bySource;
  }, [primaryPermit]);

  /**
   * Per source line: what the primary permit delivered, and what sub-customer permits already claim of
   * it. KG and Meter are tracked and shown SEPARATELY - never added together and never converted.
   */
  const primaryUsage = useMemo(() => {
    const usage = new Map<string, { allowedKg: number; allowedMeter: number; claimedKg: number; claimedMeter: number; hasKg: boolean; hasMeter: boolean }>();
    if (!primaryPermit) return usage;

    for (const line of primaryPermit.lines) {
      const key = `${line.productionOrderId}|${line.itemId}|${line.category}`;
      const row = usage.get(key) ?? { allowedKg: 0, allowedMeter: 0, claimedKg: 0, claimedMeter: 0, hasKg: false, hasMeter: false };
      row.allowedKg += line.quantityKg ?? 0;
      row.allowedMeter += line.quantityMeter ?? 0;
      row.hasKg = row.hasKg || line.quantityKg != null;
      row.hasMeter = row.hasMeter || line.quantityMeter != null;
      usage.set(key, row);
    }

    for (const delivery of deliveries ?? []) {
      if (delivery.primaryDeliveryId !== primaryPermit.id || delivery.status === "Cancelled") continue;
      for (const line of delivery.lines) {
        const row = usage.get(`${line.productionOrderId}|${line.itemId}|${line.category}`);
        if (!row) continue;
        row.claimedKg += line.quantityKg ?? 0;
        row.claimedMeter += line.quantityMeter ?? 0;
      }
    }

    return usage;
  }, [primaryPermit, deliveries]);

  /**
   * Picking a source sets the item (it travels with the source, never typed) and defaults the colour to
   * the first one the primary permit recorded for it. KG, Meter and the roll count stay manual and
   * independent - the example "400 + 350 + 250 = 1000 m" is entered by hand, never derived.
   */
  const selectSource = (idx: number, key: string) => {
    const source = sourceOptions.find((o) => o.key === key);
    if (!source) {
      updateLine(idx, { sourceKey: "", productionOrderId: "", itemId: "", color: "" });
      return;
    }
    updateLine(idx, {
      sourceKey: key, productionOrderId: source.productionOrderId, itemId: source.itemId,
      color: primaryPermit ? (primaryColorsFor.get(`${source.productionOrderId}|${source.itemId}`)?.[0] ?? "") : source.color,
      category: source.category
    });
  };

  const createMutation = useMutation({
    mutationFn: () => DeliveriesApi.create({
      customerId, deliveryDate,
      primaryDeliveryId: primaryDeliveryId || null,
      // A line needs its lot and at least one measured quantity (KG and/or Meter) - the roll count is
      // an auxiliary figure the user may enter on its own, without forcing the other two.
      lines: lines.filter((l) => l.productionOrderId && l.itemId && (l.quantityKg || l.quantityMeter)).map((l) => ({
        productionOrderId: l.productionOrderId, itemId: l.itemId, category: l.category, color: l.color || undefined,
        quantityKg: l.quantityKg ? Number(l.quantityKg) : undefined,
        quantityMeter: l.quantityMeter ? Number(l.quantityMeter) : undefined,
        // Manually entered per delivery: the roll count, and the original raw this delivery settles.
        pieceCount: l.pieceCount ? Number(l.pieceCount) : undefined,
        originalRawQuantityKg: l.originalRawQuantityKg ? Number(l.originalRawQuantityKg) : undefined,
        originalRawQuantityMeter: l.originalRawQuantityMeter ? Number(l.originalRawQuantityMeter) : undefined
      }))
    }),
    onSuccess: () => {
      invalidate();
      refreshReadyBalance();
      setShowForm(false); setLines([emptyLine()]); setError(null); setPrimaryDeliveryId(""); setCustomerId("");
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? err?.response?.data?.detail ?? "حدث خطأ أثناء الحفظ")
  });

  const prepareMutation = useMutation({ mutationFn: DeliveriesApi.prepare, onSuccess: invalidate });


  /**
   * Posts the delivery. The token is minted once per permit and reused on a retry, so a double click
   * or a resent request can never deduct the same ready stock twice; the screen only rotates the token
   * after a response comes back.
   */
  const deliverMutation = useMutation({
    mutationFn: (id: string) => DeliveriesApi.deliver(id, tokenFor(id)),
    onSuccess: (_data, id) => {
      invalidate();
      refreshReadyBalance();
      setDeliverTokens((prev) => ({ ...prev, [id]: crypto.randomUUID() }));
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? err?.response?.data?.detail ?? "تعذر التسليم")
  });
  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => DeliveriesApi.cancel(vars.id, vars.reason),
    // Cancelling reverses the delivery's OUT rows, so the lot is back on the balance the moment this
    // succeeds - and the screen must offer it again (see refreshReadyBalance).
    onSuccess: () => { invalidate(); refreshReadyBalance(); },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? err?.response?.data?.detail ?? "تعذر إلغاء الإذن")
  });

  /**
   * Corrects an approved delivery (spec section 30). Only quantities are sent -
   * the API decides the compensating movement, so the screen cannot accidentally
   * imply that editing a line "just changes a number" without touching stock.
   */
  const correctMutation = useMutation({
    mutationFn: (vars: { id: string; lines: { lineId: string; quantityKg?: number; quantityMeter?: number }[]; reason: string }) =>
      DeliveriesApi.correctApprovedLines(vars.id, vars),
    onSuccess: () => {
      invalidate();
      // A correction posts compensating movements too, so the ready-goods balance has moved as well.
      refreshReadyBalance();
      setCorrectingId(null);
      setCorrectQty({});
      setCorrectReason("");
      setCorrectError(null);
    },
    onError: (err: ApiErrorLike) => setCorrectError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "تعذر حفظ التصحيح")
  });

  const openCorrection = (id: string) => {
    setCorrectingId(id);
    setCorrectQty({});
    setCorrectOriginalRaw({});
    setCorrectReason("");
    setCorrectError(null);
  };

  const updateLine = (idx: number, patch: Partial<LineDraft>) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  return (
    <>
      <PageHeader
        title="التسليمات"
        subtitle="مسودة ← مجهزة ← تم التسليم ← ملغاة - التسليم يخصم من المخزون الجاهز، والإلغاء ينشئ حركة عكسية"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: DeliveriesExports.excel }}
              pdf={{ action: DeliveriesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تسليم جديد"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="card-pad mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="field-label">{t("del.recipient")}</label>
                {/* The recipient is picked by hand here - never inherited from the primary permit. */}
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                  <option value="">اختر...</option>
                  {recipientOptions.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
                <p className="kpi-footnote mt-1">{primaryPermit ? t("del.subPermitNeedsSubCustomer") : t("del.recipientHint")}</p>
              </div>
              <div>
                <label className="field-label">تاريخ التسليم</label>
                <Input type="date" value={deliveryDate} onChange={(e) => setDeliveryDate(e.target.value)} required />
              </div>
              {/* Sub-customer permit: linked to a primary permit, and the sub-customer must be a
                  sub of that permit's customer. Issuing it moves no stock - the delivery itself is
                  still the separate transaction below. */}
              <div className="sm:col-span-2">
                <label className="field-label">إذن أساسي (لإصدار إذن عميل فرعي)</label>
                <Select value={primaryDeliveryId} onChange={(e) => setPrimaryDeliveryId(e.target.value)}>
                  <option value="">— إذن عادي بدون إذن أساسي —</option>
                  {deliveries?.filter((d) => d.status !== "Cancelled").map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.deliveryNumber} — {d.customerCode} {d.customerName}
                    </option>
                  ))}
                </Select>
                <p className="kpi-footnote mt-1">
                  الإذن الفرعي يرتبط بالإذن الأساسي وعميله، ولا يُنشئ أي حركة مخزنية.
                </p>
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="field-label">بنود التسليم (من الرصيد الجاهز)</label>
                <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
              </div>
              {primaryPermit && <p className="mb-2 text-2xs text-ink-subtle">{t("del.itemFromPrimary")}</p>}
              <div className="space-y-3">
                {lines.map((line, idx) => {
                  const source = sourceOptions.find((o) => o.key === line.sourceKey);
                  // On a sub-customer permit the colour may only be one the primary permit recorded for
                  // this same item - hence a chooser over exactly those, and nothing else.
                  const colorOptions = primaryColorsFor.get(line.sourceKey) ?? [];
                  const primarySources = sourceOptions.filter((o) => o.fromPrimary);
                  const ownSources = sourceOptions.filter((o) => !o.fromPrimary);
                  // What the primary permit delivered for this line and what other sub-permits claim of it.
                  const usage = source?.fromPrimary ? primaryUsage.get(line.sourceKey) : undefined;
                  return (
                  <div key={idx} className="rounded-lg bg-surface-sunken p-3">
                    <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end">
                      <div className="sm:col-span-2">
                        <label className="field-label">{primaryPermit ? t("del.sourceLine") : "أمر التشغيل (الرصيد الجاهز)"}</label>
                        <Select value={line.sourceKey} onChange={(e) => selectSource(idx, e.target.value)} required disabled={!customerId}>
                          <option value="">اختر...</option>
                          {primarySources.length > 0 && (
                            <optgroup label={t("del.primaryPermitSource")}>
                              {primarySources.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
                            </optgroup>
                          )}
                          {ownSources.length > 0 && (
                            <optgroup label={primaryPermit ? t("del.ownStockSource") : "الرصيد الجاهز"}>
                              {ownSources.map((o) => <option key={o.key} value={o.key}>{o.label}</option>)}
                            </optgroup>
                          )}
                        </Select>
                      </div>
                      {colorOptions.length > 1 && (
                        <div>
                          <label className="field-label">{t("del.color")}</label>
                          <Select value={line.color} onChange={(e) => updateLine(idx, { color: e.target.value })}>
                            {colorOptions.map((c) => <option key={c} value={c}>{c}</option>)}
                          </Select>
                        </div>
                      )}
                      {/* Roll count and both measurements are entered by hand and are independent: the
                          user fills in what the document actually needs, not all three. */}
                      <div>
                        <label className="field-label">{t("del.pieceCount")}</label>
                        <Input type="number" min="0" step="1" value={line.pieceCount}
                          onChange={(e) => updateLine(idx, { pieceCount: e.target.value })} />
                      </div>
                      <div><label className="field-label">كمية (كجم)</label><Input type="number" step="0.001" min="0" value={line.quantityKg} onChange={(e) => updateLine(idx, { quantityKg: e.target.value })} /></div>
                      <div><label className="field-label">كمية (متر)</label><Input type="number" step="0.001" min="0" value={line.quantityMeter} onChange={(e) => updateLine(idx, { quantityMeter: e.target.value })} /></div>
                      {/* The original raw this delivery settles - entered by hand, per delivery, and
                          checked against what the Job Order started from (its original quantity is
                          never overwritten). Compatible units only: kg against kg, meter against meter. */}
                      <div>
                        <label className="field-label">خام أصلي مُسوَّى (كجم)</label>
                        <Input type="number" step="0.001" min="0" value={line.originalRawQuantityKg}
                          onChange={(e) => updateLine(idx, { originalRawQuantityKg: e.target.value })} />
                      </div>
                      <div>
                        <label className="field-label">خام أصلي مُسوَّى (متر)</label>
                        <Input type="number" step="0.001" min="0" value={line.originalRawQuantityMeter}
                          onChange={(e) => updateLine(idx, { originalRawQuantityMeter: e.target.value })} />
                      </div>
                    </div>
                    {/* Distributed and remaining per UNIT, never as one combined figure: KG is shown
                        against KG and Meter against Meter, and no conversion is implied. */}
                    {usage && (usage.hasKg || usage.hasMeter) && (
                      <p className="kpi-footnote mt-2 ltr-nums">
                        {usage.hasKg && `${t("del.distributedOfPrimary")}: ${usage.claimedKg} / ${usage.allowedKg} كجم — ${t("del.remainingOfPrimary")}: ${usage.allowedKg - usage.claimedKg} كجم `}
                        {usage.hasMeter && `${t("del.distributedOfPrimary")}: ${usage.claimedMeter} / ${usage.allowedMeter} م — ${t("del.remainingOfPrimary")}: ${usage.allowedMeter - usage.claimedMeter} م`}
                      </p>
                    )}
                  </div>
                  );
                })}
              </div>
            </div>

            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ كمسودة"}</Button>
            {error && <p className="form-error">{error}</p>}
          </form>
        </Card>
      )}

      <div className="space-y-3">
        {isLoading && <Card className="card-pad text-center text-ink-subtle">جارٍ التحميل...</Card>}
        {deliveries?.map((d) => (
          <Card key={d.id} className="card-pad">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="font-bold ltr-nums">{d.deliveryNumber}</span>
                <span className="text-ink-subtle mx-2">·</span>
                <span className="text-sm text-ink-muted">{d.customerCode} - {d.customerName}</span>
                {/* The one cross-reference a sub-customer permit carries: the primary permit and
                    its customer's code/name. Never the primary customer's full record. */}
                {d.isSubCustomerPermit && d.primaryDeliveryNumber && (
                  <span className="ms-2 text-2xs text-ink-subtle">
                    إذن فرعي عن <span className="ltr-nums">{d.primaryDeliveryNumber}</span>
                    {d.primaryCustomerCode ? ` — عميل أساسي ${d.primaryCustomerCode}` : ""}
                  </span>
                )}
              </div>
              <Badge tone={statusTone[d.status]}>{statusLabel[d.status]}</Badge>
            </div>

            <div className="table-wrap mt-3">
              <table className="table table-dense">
                <thead>
                  <tr>
                    <th>{t("prod.jobOrder", "أمر التشغيل")}</th>
                    <th>{t("statement.orderDate", "تاريخ الأمر الأصلي")}</th>
                    <th>{t("common.item", "الصنف")}</th>
                    <th>{t("common.quantity", "الكمية")}</th>
                    <th>{t("statement.originalRaw", "الخام الأصلي المُسوَّى")}</th>
                    <th>{t("statement.remaining", "المتبقي من الخام الأصلي")}</th>
                  </tr>
                </thead>
                <tbody>
                  {d.lines.map((l) => (
                    <tr key={l.id}>
                      <td className="ltr-nums">{l.productionOrderNumber}</td>
                      {/* The ORIGINAL Job Order date, kept on later partial-delivery documents. */}
                      <td className="ltr-nums text-ink-muted">
                        {l.productionOrderDate ? new Date(l.productionOrderDate).toLocaleDateString("en-GB") : "—"}
                      </td>
                      <td>{l.itemCode}{l.color ? ` (${l.color})` : ""}</td>
                      <td className="ltr-nums">{l.quantityKg != null && `${l.quantityKg} كجم `}{l.quantityMeter != null && `${l.quantityMeter} م`}</td>
                      <td className="ltr-nums">
                        {l.originalRawQuantityKg != null && `${l.originalRawQuantityKg} كجم `}
                        {l.originalRawQuantityMeter != null && `${l.originalRawQuantityMeter} م`}
                        {l.originalRawQuantityKg == null && l.originalRawQuantityMeter == null && "—"}
                      </td>
                      <td className="ltr-nums">
                        {/* Remaining original raw = the order's FIXED original quantity minus what
                            its deliveries have settled; kg and meter are independent figures. */}
                        {l.originalOrderRawQuantityKg != null &&
                          `${l.remainingOriginalRawKg} / ${l.originalOrderRawQuantityKg} كجم `}
                        {l.originalOrderRawQuantityMeter != null &&
                          `${l.remainingOriginalRawMeter} / ${l.originalOrderRawQuantityMeter} م`}
                        {l.originalOrderRawQuantityKg == null && l.originalOrderRawQuantityMeter == null && "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-3 flex flex-wrap items-center gap-2">
              {d.status === "Draft" && <Button variant="secondary" onClick={() => prepareMutation.mutate(d.id)}>تجهيز</Button>}
              <Link to={`/print/delivery/${d.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
              <Link to={`/print/delivery-statement/${d.id}`} target="_blank"><Button type="button" variant="ghost">بيان التسليم</Button></Link>
              {/* The PDF carries the SAME quantity columns the user chose for this document format,
                  so what is downloaded matches the on-screen print preview. */}
              <Button variant="ghost"
                onClick={DeliveriesExports.documentPdf(
                  d.id, readDeliveryQuantityColumns(deliveryColumnsStorageKey(d.isSubCustomerPermit)))}>
                تنزيل PDF
              </Button>
              <Button variant="ghost" onClick={DeliveriesExports.statementPdf(d.id)}>تنزيل بيان التسليم PDF</Button>
              {d.status === "Prepared" && <Button variant="secondary" onClick={() => deliverMutation.mutate(d.id)}>تسليم</Button>}
              {d.status === "Delivered" && correctingId !== d.id && (
                <Button variant="secondary" onClick={() => openCorrection(d.id)}>
                  {t("del.correctApproved", "تصحيح بعد الاعتماد")}
                </Button>
              )}
              {(d.status === "Draft" || d.status === "Prepared" || d.status === "Delivered") && (
                <>
                  <Input
                    placeholder="سبب الإلغاء..."
                    value={cancelReasonById[d.id] ?? ""}
                    onChange={(e) => setCancelReasonById((p) => ({ ...p, [d.id]: e.target.value }))}
                    className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                  />
                  <Button variant="ghost" disabled={!cancelReasonById[d.id]} onClick={() => cancelMutation.mutate({ id: d.id, reason: cancelReasonById[d.id] })}>إلغاء</Button>
                </>
              )}
            </div>

            {/* Post-approval correction editor. Each line keeps its delivered
                quantity until the user changes it, and the difference is shown
                per line so the effect on Ready Goods is never a surprise. */}
            {d.status === "Delivered" && correctingId === d.id && (
              <div className="mt-3 rounded-lg border border-line bg-surface-sunken p-3">
                <p className="mb-2 text-2xs text-ink-subtle">{t("del.correctHint")}</p>
                <div className="table-wrap">
                  <table className="table table-dense">
                    <thead>
                      <tr>
                        <th>{t("prod.jobOrder", "أمر التشغيل")}</th>
                        <th>{t("common.item", "الصنف")}</th>
                        <th>{t("common.quantity", "الكمية المعتمدة")}</th>
                        <th>{t("del.newQuantity", "الكمية الصحيحة")}</th>
                        <th>{t("statement.originalRaw", "الخام الأصلي المُسوَّى")}</th>
                        <th>{t("del.difference", "الفرق")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {d.lines.map((l) => {
                        const isKg = l.quantityKg != null;
                        const original = Number(isKg ? l.quantityKg : l.quantityMeter);
                        const typed = correctQty[l.id];
                        const next = typed === undefined ? original : Number(typed);
                        const diff = Number.isFinite(next) ? next - original : 0;
                        return (
                          <tr key={l.id}>
                            <td className="ltr-nums">{l.productionOrderNumber}</td>
                            <td>{l.itemCode}{l.color ? ` (${l.color})` : ""}</td>
                            <td className="ltr-nums">
                              {isKg ? `${l.quantityKg} كجم` : `${l.quantityMeter} م`}
                            </td>
                            <td>
                              <Input
                                type="number"
                                min={0}
                                step="0.01"
                                className="max-w-[9rem]"
                                value={typed ?? String(original)}
                                onChange={(e) => setCorrectQty((p) => ({ ...p, [l.id]: e.target.value }))}
                              />
                            </td>
                            <td>
                              {/* Editable per workflow: blank keeps what the line already records. */}
                              <Input
                                type="number"
                                min={0}
                                step="0.001"
                                className="max-w-[9rem]"
                                placeholder={isKg
                                  ? String(l.originalRawQuantityKg ?? "")
                                  : String(l.originalRawQuantityMeter ?? "")}
                                value={correctOriginalRaw[l.id] ?? ""}
                                onChange={(e) => setCorrectOriginalRaw((p) => ({ ...p, [l.id]: e.target.value }))}
                              />
                            </td>
                            <td className="ltr-nums">
                              {diff === 0 ? (
                                <span className="text-ink-subtle">—</span>
                              ) : (
                                <span className={diff < 0 ? "text-success-ink" : "text-warning-ink"}>
                                  {diff > 0 ? "+" : ""}
                                  {diff.toLocaleString("en-US")}
                                </span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>

                {correctError && <p className="mt-2 text-sm text-danger-ink">{correctError}</p>}

                <div className="mt-3 flex flex-wrap items-end gap-2">
                  <div className="min-w-[14rem] flex-1">
                    <label className="field-label">
                      {t("del.correctReason", "سبب التصحيح")}
                    </label>
                    <Input value={correctReason} onChange={(e) => setCorrectReason(e.target.value)} />
                  </div>
                  <Button
                    disabled={correctReason.trim() === "" || correctMutation.isPending}
                    onClick={() =>
                      correctMutation.mutate({
                        id: d.id,
                        reason: correctReason.trim(),
                        lines: d.lines.map((l) => {
                          const isKg = l.quantityKg != null;
                          const original = Number(isKg ? l.quantityKg : l.quantityMeter);
                          const typed = correctQty[l.id];
                          const rawTyped = correctOriginalRaw[l.id];
                          return {
                            lineId: l.id,
                            quantityKg: isKg ? (typed === undefined ? original : Number(typed)) : undefined,
                            quantityMeter: isKg ? undefined : (typed === undefined ? original : Number(typed)),
                            // Only sent when the user actually typed one - omitted means "leave as is".
                            originalRawQuantityKg: isKg && rawTyped ? Number(rawTyped) : undefined,
                            originalRawQuantityMeter: !isKg && rawTyped ? Number(rawTyped) : undefined
                          };
                        })
                      })
                    }
                  >
                    {t("common.save", "حفظ")}
                  </Button>
                  <Button variant="secondary" onClick={() => setCorrectingId(null)}>
                    {t("common.close", "إغلاق")}
                  </Button>
                </div>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
