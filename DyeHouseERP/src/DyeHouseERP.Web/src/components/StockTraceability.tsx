import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  CustomersApi, InventoryTraceabilityApi, ItemsApi, MaterialsApi, WarehousesApi,
  type StockScope, type StockTraceMovement, type StockTraceRow
} from "@/api/client";
import { Card, Button, Input, Select, Badge, SearchInput } from "@/components/ui";
import { downloadFile, InventoryTraceabilityExports, lotToken, type LotCardIdentity } from "@/api/exports";
import { useI18n } from "@/i18n";

/**
 * BALANCES WITH THE MOVEMENTS BEHIND THEM.
 *
 * One component, four stock classes (raw material / ready goods / materials & chemicals / operating supplies), because the
 * question is the same in all of them: "this is the balance - WHICH movements is it made of, and where
 * did each one come from?"
 *
 * Rules it makes visible, because getting them wrong is how a stock screen lies:
 *   * the balance is the sum of the appended ledger rows, computed by the server on every request -
 *     nothing is cached, nothing is stored, so it cannot be stale and a second user's posting shows up
 *     on its own (the query also re-runs on a timer and on window focus);
 *   * KG and Meter are INDEPENDENT: each has its own column, and a unit the lot never recorded prints
 *     "—" rather than a zero that would read as a real measurement;
 *   * a row is a LOT, not an item - a receipt message in the raw warehouse, a job order in the ready
 *     warehouse, a material in a warehouse - so two lots of the same item are never added together;
 *   * drilling into a lot shows exactly that lot's movements WITH the balance as it stood after each one,
 *     which is what proves the total is made of those rows and nothing else.
 */
export default function StockTraceability({ scope, compact = false }: { scope: StockScope; compact?: boolean }) {
  const { t } = useI18n();
  const navigate = useNavigate();

  const [filters, setFilters] = useState({
    customerId: "", itemId: "", materialId: "", warehouseId: "",
    documentNumber: "", kind: "", from: "", to: "",
    onlyWithBalance: false, includeDelivered: true
  });

  /**
   * The lot the user opened: the request is re-scoped so the screen shows that lot and its movements.
   * It is the FULL identity - the document (receipt message / job order / material) PLUS the item, or
   * the warehouse for a material - because one receipt message can carry several items and one material
   * can sit in several warehouses, each an independent balance. Opening "the message" would otherwise
   * show, and print, several lots as if they were one.
   */
  const [lot, setLot] = useState<LotCardIdentity | null>(null);

  /**
   * The lots ticked for BATCH printing. Keyed by "scope:id" on purpose: the same component is reused
   * for the three warehouses, so a lot ticked in the raw list can never print as a materials lot when
   * the user switches tab.
   */
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "active"], queryFn: () => WarehousesApi.list({ activeOnly: true }) });
  const { data: materials } = useQuery({
    queryKey: ["materials", "active"],
    queryFn: () => MaterialsApi.list({ activeOnly: true }),
    enabled: scope === "materials" || scope === "supplies"
  });

  const isMaterials = scope === "materials" || scope === "supplies";
  const isRaw = scope === "raw";

  const { data, isLoading, isFetching, refetch, dataUpdatedAt } = useQuery({
    // Nothing is memoised on the server: every key change is a fresh read of the ledger.
    queryKey: ["stock-traceability", scope, filters, lot],
    queryFn: () => InventoryTraceabilityApi.get({
      scope,
      customerId: filters.customerId || undefined,
      // Drilling into a lot is an IDENTITY filter: the balances become that lot's own, item included, so
      // a receipt message carrying two items shows ONE of them rather than both added together.
      itemId: lot?.itemId ?? (filters.itemId || undefined),
      materialId: lot?.materialId ?? (filters.materialId || undefined),
      warehouseId: lot?.warehouseId ?? (filters.warehouseId || undefined),
      documentNumber: filters.documentNumber.trim() || undefined,
      kind: filters.kind || undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
      onlyWithBalance: filters.onlyWithBalance || undefined,
      includeDelivered: filters.includeDelivered,
      rawMessageId: lot?.rawMessageId,
      productionOrderId: lot?.productionOrderId
    }),
    // "Live" balances: re-read the ledger when the tab regains focus and on a short timer, so a receipt,
    // an issue, a return or a delivery posted by someone else appears without any extra bookkeeping.
    refetchInterval: 15000,
    refetchOnWindowFocus: true,
    staleTime: 0
  });

  const rows = useMemo(() => data?.rows ?? [], [data?.rows]);
  const movements = data?.movements ?? [];
  // A unit that no visible lot ever recorded has no column at all: an empty KG or Meter column would
  // imply the unit exists for those items.
  const hasMeter = useMemo(() => rows.some((r) => r.remainingMeter != null || r.receivedMeter != null
    || r.producedMeter != null || r.deliveredMeter != null || r.issuedToProductionMeter != null), [rows]);

  const number = (v: number | null | undefined) => (v === null || v === undefined ? "—" : v.toLocaleString("en-US", { maximumFractionDigits: 3 }));

  /** Renders a fabric quantity pair (KG and/or Meter) strictly in the units actually recorded for the lot. */
  const fabricCell = (kg: number | null | undefined, meter: number | null | undefined, pieces?: number | null) => {
    const hasKgVal = kg !== null && kg !== undefined;
    const hasMeterVal = hasMeter && meter !== null && meter !== undefined;
    if (!hasKgVal && !hasMeterVal && (pieces === null || pieces === undefined)) return "—";
    return (
      <>
        {hasKgVal && <div>{number(kg)} كجم</div>}
        {hasMeterVal && <div className="text-2xs font-normal text-ink-muted">{number(meter)} م</div>}
        {pieces != null && <div className="text-2xs font-normal text-ink-subtle">{pieces} {t("stock.pieces", "عدد")}</div>}
      </>
    );
  };

  const kindOptions = isMaterials
    ? ["Received", "Issued", "Returned", "TransferredIn", "TransferredOut"]
    : isRaw
      ? ["Received", "Issued", "IssuedExternal", "Returned", "TransferredIn", "TransferredOut", "AdjustedIn", "AdjustedOut"]
      : ["Produced", "Delivered", "Returned", "TransferredOut", "AdjustedIn", "AdjustedOut", "Reversal"];

  /**
   * The FULL identity of one balance row - the very identity the card endpoint is given, so the screen,
   * the batch-print URL and the downloaded PDF all name the same lot and no other.
   */
  const lotOf = (row: StockTraceRow): LotCardIdentity => (isMaterials
    ? { materialId: row.materialId ?? undefined, warehouseId: row.warehouseId ?? undefined }
    : isRaw
      ? { rawMessageId: row.rawMessageId ?? undefined, itemId: row.itemId || undefined }
      : { productionOrderId: row.productionOrderId ?? undefined, itemId: row.itemId || undefined });

  const openLot = (row: StockTraceRow) => setLot(lotOf(row));

  /** The opened lot as it travels in a print URL - the full identity, disambiguator included. */
  const openLotToken = lot ? lotToken(scope, lot) : null;

  const isOpen = (row: StockTraceRow) => {
    const token = lotToken(scope, lotOf(row));
    return token !== null && token === openLotToken;
  };

  const printLot = () => { if (openLotToken) navigate(`/print/stock-traceability/${scope}?lots=${openLotToken}`); };

  const openLotLabel = () => {
    if (!lot) return null;
    const row = rows[0];
    if (!row) return null;
    // The item is part of the label: two items of the same receipt message are two different lots.
    return isMaterials
      ? `${row.materialCode} - ${row.materialName}`
      : `${isRaw ? row.messageNumber : row.orderNumber} · ${row.itemCode}`;
  };

  // ---- BATCH PRINTING: tick one or several lots and print their cards as one document ----------
  /**
   * A row's lot as the drill-down and the card name it, DISAMBIGUATOR INCLUDED: two items of one receipt
   * message are two lots, so they are two tokens. Keying only by the message id would make ticking one
   * item tick the other, and would collapse their two cards into one document.
   */
  const tokenOf = (row: StockTraceRow) => lotToken(scope, lotOf(row));
  const lotNumberOf = (row: StockTraceRow) =>
    (isMaterials ? row.materialCode : isRaw ? row.messageNumber : row.orderNumber) ?? undefined;
  /**
   * The row's OWN disambiguator: the item inside the message or job order, or the warehouse a material
   * sits in. It is the half of the identity that makes the row ONE lot, so it is also what keeps two
   * lots of one document (two items of a receipt, one material in two warehouses) from downloading
   * under a single name.
   */
  const lotDisambiguatorOf = (row: StockTraceRow) =>
    isMaterials
      ? (row.warehouseCode ?? row.warehouseName ?? undefined)
      : (row.itemCode || undefined);
  const selectionKeyOf = (row: StockTraceRow) => { const token = tokenOf(row); return token ? `${scope}:${token}` : null; };

  // Only rows that actually carry a lot identity can be printed - history rows with a NULL link have
  // nothing to name on a card, so their checkbox is disabled rather than printing an empty document.
  const selectableRows = rows.filter((row) => selectionKeyOf(row) !== null);
  const isSelected = (row: StockTraceRow) => { const key = selectionKeyOf(row); return key !== null && selected.has(key); };
  const selectedLotTokens = selectableRows.filter(isSelected).map((row) => tokenOf(row)!);
  const allSelected = selectableRows.length > 0 && selectedLotTokens.length === selectableRows.length;

  const toggleRow = (row: StockTraceRow) => {
    const key = selectionKeyOf(row);
    if (!key) return;
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  };

  const toggleAll = () => setSelected((prev) => {
    const next = new Set(prev);
    for (const row of selectableRows) {
      const key = selectionKeyOf(row)!;
      if (allSelected) next.delete(key); else next.add(key);
    }
    return next;
  });

  const printSelected = () => {
    if (selectedLotTokens.length === 0) return;
    navigate(`/print/stock-traceability/${scope}?lots=${selectedLotTokens.join(",")}`);
  };

  const exportStock = (format: "excel" | "pdf") => downloadFile("/inventory/traceability/export", `stock-${scope}.${format === "pdf" ? "pdf" : "xlsx"}`, {
    scope,
    customerId: filters.customerId || undefined,
    // The exported list is the SAME set the screen is showing, identity included: exporting while a lot
    // is open exports that one lot, not every lot of its message.
    itemId: lot?.itemId ?? (filters.itemId || undefined),
    materialId: lot?.materialId ?? (filters.materialId || undefined),
    warehouseId: lot?.warehouseId ?? (filters.warehouseId || undefined),
    documentNumber: filters.documentNumber.trim() || undefined,
    kind: filters.kind || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
    onlyWithBalance: filters.onlyWithBalance ? "true" : undefined,
    includeDelivered: filters.includeDelivered ? "true" : "false",
    rawMessageId: lot?.rawMessageId,
    productionOrderId: lot?.productionOrderId,
    format
  });

  const balanceColumns = () => (isMaterials
    ? [
        { key: "material", label: t("stock.material", "المادة / الكيماوي") },
        { key: "unit", label: t("stock.unit", "الوحدة") },
        { key: "warehouse", label: t("wh.master", "المخزن") },
        { key: "supplier", label: t("pur.supplier", "المورد") },
        { key: "received", label: t("stock.received", "المستلم") },
        { key: "issued", label: t("stock.issued", "المصروف") },
        { key: "returned", label: t("stock.returned", "المرتجع") },
        { key: "balance", label: t("stock.balance", "الرصيد") }
      ]
    : [
        { key: "item", label: t("common.item", "الصنف") },
        { key: isRaw ? "lot" : "order", label: isRaw ? t("fr.rawMessage", "رسالة الخام") : t("fr.jobOrder", "أمر التشغيل") },
        { key: "date", label: t("common.date", "التاريخ") },
        { key: "customer", label: t("common.customer", "العميل") },
        { key: isRaw ? "received" : "produced", label: isRaw ? t("stock.received", "المستلم") : t("stock.produced", "المنتج") },
        ...(isRaw ? [{ key: "issued", label: t("stock.issued", "المصروف") },
                     { key: "under", label: t("stock.underProduction", "تحت التشغيل") }] : []),
        { key: "returned", label: t("stock.returned", "المرتجع") },
        { key: "delivered", label: t("stock.delivered", "المسلّم للعميل") },
        { key: "balance", label: t("stock.balance", "الرصيد") }
      ]);

  return (
    <>
      {/* ---- filters: which lots, and which movements ---- */}
      <Card className={`card-pad mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4 items-end ${compact ? "text-sm" : ""}`}>
        {!isMaterials && (
          <>
            <div>
              <label className="field-label">{t("common.customer", "العميل")}</label>
              <Select value={filters.customerId} onChange={(e) => setFilters({ ...filters, customerId: e.target.value })}>
                <option value="">{t("common.all", "الكل")}</option>
                {(customers ?? []).map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">{t("common.item", "الصنف")}</label>
              <Select value={filters.itemId} onChange={(e) => setFilters({ ...filters, itemId: e.target.value })}>
                <option value="">{t("common.all", "الكل")}</option>
                {(items ?? []).map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
              </Select>
            </div>
          </>
        )}

        {isMaterials && (
          <div>
            <label className="field-label">{t("stock.material", "المادة / الكيماوي")}</label>
            <Select value={filters.materialId} onChange={(e) => setFilters({ ...filters, materialId: e.target.value })}>
              <option value="">{t("common.all", "الكل")}</option>
              {(materials ?? []).map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}
            </Select>
          </div>
        )}

        <div>
          <label className="field-label">{t("wh.master", "المخزن")}</label>
          <Select value={filters.warehouseId} onChange={(e) => setFilters({ ...filters, warehouseId: e.target.value })}>
            <option value="">{t("common.all", "الكل")}</option>
            {(warehouses ?? []).map((w) => <option key={w.id} value={w.id}>{w.code} - {w.name}</option>)}
          </Select>
        </div>

        <div>
          <label className="field-label">{t("stock.movementKind", "نوع الحركة")}</label>
          <Select value={filters.kind} onChange={(e) => setFilters({ ...filters, kind: e.target.value })}>
            <option value="">{t("common.all", "الكل")}</option>
            {kindOptions.map((k) => <option key={k} value={k}>{t(`stock.kind.${k}`, k)}</option>)}
          </Select>
        </div>

        <div>
          <label className="field-label">{t("stock.documentNumber", "رقم المستند")}</label>
          <SearchInput
            placeholder={t("stock.documentNumberHint", "رسالة / أمر تشغيل / إذن تسليم / إذن صرف")}
            value={filters.documentNumber}
            onChange={(e) => setFilters({ ...filters, documentNumber: e.target.value })}
          />
        </div>

        <div>
          <label className="field-label">{t("common.from", "من")}</label>
          <Input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} />
        </div>
        <div>
          <label className="field-label">{t("common.to", "إلى")}</label>
          <Input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} />
        </div>

        <div className="flex flex-col gap-1">
          <label className="flex items-center gap-2 text-xs text-ink-muted">
            <input type="checkbox" checked={filters.onlyWithBalance}
              onChange={(e) => setFilters({ ...filters, onlyWithBalance: e.target.checked })} />
            {t("stock.onlyWithBalance", "الأرصدة الموجودة فقط")}
          </label>
          {!isMaterials && (
            <label className="flex items-center gap-2 text-xs text-ink-muted">
              <input type="checkbox" checked={filters.includeDelivered}
                onChange={(e) => setFilters({ ...filters, includeDelivered: e.target.checked })} />
              {t("stock.includeDelivered", "إظهار اللوطات المسلّمة كاملة")}
            </label>
          )}
          <p className="text-2xs text-ink-subtle">
            {t("stock.balanceIsLive", "الرصيد محسوب لحظيًا من دفتر الحركات: التصفية بالتاريخ أو نوع الحركة تقصر قائمة الحركات فقط ولا تغيّر الرصيد.")}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => refetch()}>{t("floor.refresh", "تحديث")}</Button>
          <Button variant="ghost" onClick={() => exportStock("excel")}>{t("common.export", "تصدير")}</Button>
          <Button variant="ghost" onClick={() => exportStock("pdf")}>PDF</Button>
          {isFetching && !isLoading && <span className="text-2xs text-ink-subtle">{t("floor.refreshing", "جارٍ التحديث...")}</span>}
          <span className="text-2xs text-ink-subtle">
            {t("floor.updatedAt", "آخر تحديث")}: {new Date(data?.generatedAtUtc ?? dataUpdatedAt).toLocaleTimeString("en-GB")}
          </span>
        </div>
      </Card>

      {/* ---- totals: per unit, never merged into one number ---- */}
      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <TotalCard
          label={isMaterials ? t("stock.balance", "الرصيد") : t("stock.balanceKg", "الرصيد كجم")}
          value={number(isMaterials ? data?.totalRemainingQuantity : data?.totalRemainingKg)}
        />
        {!isMaterials && hasMeter && (
          <TotalCard label={t("stock.balanceM", "الرصيد متر")} value={number(data?.totalRemainingMeter)} />
        )}
        {isRaw && (
          <TotalCard label={t("stock.underProduction", "تحت التشغيل (أوامر مفتوحة)")} value={number(data?.totalUnderProductionKg)} tone="info" />
        )}
        {!isMaterials && (
          <>
            <TotalCard label={t("stock.received", "المستلم")} value={number(data?.totalReceivedKg)} />
            {!isRaw && <TotalCard label={t("stock.produced", "المنتج")} value={number(data?.totalProducedKg)} />}
            <TotalCard label={t("stock.delivered", "المسلّم للعميل")} value={number(data?.totalDeliveredKg)} />
          </>
        )}
        {isMaterials && (
          <>
            <TotalCard label={t("stock.received", "المستلم")} value={number(data?.totalReceivedQuantity)} />
            <TotalCard label={t("stock.issued", "المصروف")} value={number(data?.totalIssuedQuantity)} />
            <TotalCard label={t("stock.returned", "المرتجع")} value={number(data?.totalReturnedQuantity)} />
          </>
        )}
      </div>

      {lot && (
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Badge tone="blue">{t("stock.drilledInto", "تفاصيل اللوط")}: <span className="ltr-nums ms-1">{openLotLabel() ?? "—"}</span></Badge>
          {/* طباعة بطاقة اللوط: the same ledger read as an official document - movements with the
              cumulative balance after each one. Read-only, exactly like the screen. */}
          <Button variant="secondary" onClick={printLot}>{t("stock.print.action", "طباعة بطاقة اللوط")}</Button>
          <Button variant="ghost" onClick={() => setLot(null)}>{t("stock.backToAll", "رجوع لكل الأرصدة")}</Button>
        </div>
      )}

      {/* Batch selection lives with the balances it selects from - no separate screen. */}
      <Card className="card-pad mb-3 flex flex-wrap items-center gap-x-4 gap-y-2">
        <label className="flex items-center gap-2 text-xs text-ink-muted">
          <input
            type="checkbox"
            checked={allSelected}
            disabled={selectableRows.length === 0}
            onChange={toggleAll}
            aria-label={t("stock.selectAll", "تحديد الكل")}
          />
          {t("stock.selectAll", "تحديد الكل")}
        </label>
        <span className="text-2xs text-ink-subtle">
          {t("stock.selectHint", "حدّد لوطًا أو أكثر ثم اطبع بطاقاتها كمستند واحد - بطاقة لكل لوط.")}
        </span>
        <div className="ms-auto flex items-center gap-2">
          <Button variant="secondary" onClick={printSelected} disabled={selectedLotTokens.length === 0}>
            {t("stock.print.selected", "طباعة البطاقات المحددة")} ({selectedLotTokens.length})
          </Button>
          {selectedLotTokens.length === 0
            ? <span className="text-2xs text-ink-subtle">{t("stock.print.noneSelected", "حدد لوطًا واحدًا على الأقل للطباعة.")}</span>
            : <Button variant="ghost" onClick={() => setSelected(new Set())}>{t("stock.clearSelection", "إلغاء التحديد")}</Button>}
        </div>
      </Card>

      {/* ---- the balances: one row per lot ---- */}
      <Card className="mb-4 overflow-x-auto">
        <table className="table">
          <thead>
            <tr>
              <th className="w-8">{t("stock.select", "تحديد")}</th>
              {balanceColumns().map((c) => <th key={c.key}>{c.label}</th>)}
              <th>{t("stock.movements", "الحركات")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={13} className="px-4 py-6 text-center text-ink-subtle">{t("common.loading", "جارٍ التحميل...")}</td></tr>}
            {!isLoading && rows.length === 0 && (
              <tr><td colSpan={13} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty", "لا توجد بيانات")}</td></tr>
            )}
            {rows.map((row, index) => (
              <tr
                key={row.lotKey || `${row.itemId}-${row.warehouseId}-${index}`}
                className={`border-b border-line last:border-0 hover:bg-surface-sunken ${isOpen(row) ? "bg-brand-50/40" : ""}`}
              >
                <td>
                  <input
                    type="checkbox"
                    checked={isSelected(row)}
                    disabled={selectionKeyOf(row) === null}
                    onChange={() => toggleRow(row)}
                    aria-label={t("stock.select", "تحديد")}
                  />
                </td>
                {isMaterials ? (
                  <>
                    <td>
                      <span className="ltr-nums font-medium">{row.materialCode}</span>
                      <span className="text-ink-muted"> · {row.materialName}</span>
                    </td>
                    <td>{t(`material.unit.${row.materialUnit}`, row.materialUnit ?? "—")}</td>
                    <td>{row.warehouseName ?? "—"}</td>
                    <td>
                      {row.supplierName ?? "—"}
                      {row.purchaseOrderNumber ? <span className="text-ink-subtle"> · {row.purchaseOrderNumber}</span> : null}
                      {row.orderNumbers && <div className="text-2xs text-ink-subtle ltr-nums">{row.orderNumbers}</div>}
                    </td>
                    <td className="ltr-nums">{number(row.receivedQuantity)}</td>
                    <td className="ltr-nums">{number(row.issuedToProductionQuantity)}</td>
                    <td className="ltr-nums">{number(row.returnedQuantity)}</td>
                    <td className="ltr-nums font-semibold">{number(row.remainingQuantity)}</td>
                  </>
                ) : (
                  <>
                    <td>
                      <Link className="font-medium" to={`/items?focus=${row.itemId}`}>{row.itemCode}</Link>
                      <span className="text-ink-muted"> · {row.itemName}</span>
                      <div className="text-2xs text-ink-subtle">
                        {row.secondaryUnit ? `${row.baseUnit} / ${row.secondaryUnit}` : row.baseUnit}
                      </div>
                    </td>
                    <td>
                      {isRaw ? (
                        row.rawMessageId
                          ? <Link className="ltr-nums font-medium" to={`/print/raw-message/${row.rawMessageId}`}>{row.messageNumber}</Link>
                          : "—"
                      ) : (
                        row.productionOrderId
                          ? <Link className="ltr-nums font-medium" to={`/production-orders/${row.productionOrderId}`}>{row.orderNumber}</Link>
                          : "—"
                      )}
                      <div className="text-2xs text-ink-subtle">{row.color ?? row.orderStatus ?? row.messageStatus ?? ""}</div>
                      {isRaw && row.orderNumbers && (
                        <div className="text-2xs text-ink-muted ltr-nums">{row.orderNumbers}</div>
                      )}
                    </td>
                    <td className="ltr-nums text-ink-muted">
                      {(row.messageDate ?? row.orderDate)?.slice(0, 10) ?? "—"}
                    </td>
                    <td className="ltr-nums">
                      {row.customerCode ?? "—"}
                      <div className="text-2xs text-ink-subtle">{row.customerName ?? ""}</div>
                    </td>
                    <td className="ltr-nums">
                      {isRaw
                        ? fabricCell(row.receivedKg, row.receivedMeter)
                        : fabricCell(row.producedKg, row.producedMeter, row.producedPieceCount)}
                    </td>
                    {isRaw && <td className="ltr-nums">{fabricCell(row.issuedToProductionKg, row.issuedToProductionMeter)}</td>}
                    {isRaw && <td className="ltr-nums text-info-ink">{fabricCell(row.underProductionKg, row.underProductionMeter)}</td>}
                    <td className="ltr-nums">{fabricCell(row.returnedKg, row.returnedMeter)}</td>
                    <td className="ltr-nums">
                      {fabricCell(row.deliveredKg, row.deliveredMeter, row.deliveredPieceCount)}
                      {row.deliveryNumbers && (
                        <div className="text-2xs">
                          <Link to="/deliveries">{row.deliveryNumbers}</Link>
                          {row.lastDeliveryDate && <span className="text-ink-subtle"> · {row.lastDeliveryDate.slice(0, 10)}</span>}
                        </div>
                      )}
                    </td>
                    <td className={`ltr-nums font-semibold ${(row.remainingKg ?? 0) < 0 || (row.remainingMeter ?? 0) < 0 ? "text-danger-ink" : ""}`}>
                      {fabricCell(row.remainingKg, row.remainingMeter)}
                    </td>
                  </>
                )}
                <td>
                  <div className="flex items-center gap-2">
                    <button type="button" className="btn-link" onClick={() => (isOpen(row) ? setLot(null) : openLot(row))}>
                      {row.movementCount} {t("stock.movementsShort", "حركة")}
                    </button>
                    {/* بطاقة اللوط as a server-rendered PDF - the same read the screen shows, for the
                        same identity: a row's own item travels with the request, so a message carrying
                        two items yields two cards rather than one card for whichever came first. */}
                    {tokenOf(row) && (
                      <button
                        type="button"
                        className="btn-link"
                        title={t("stock.print.downloadPdf", "تنزيل PDF")}
                        onClick={InventoryTraceabilityExports.lotCardPdf(scope, lotOf(row), lotNumberOf(row), lotDisambiguatorOf(row))}
                      >
                        PDF
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* ---- the ledger rows behind those balances, each with the balance it left ---- */}
      <Card className="overflow-x-auto">
        <div className="card-pad flex flex-wrap items-center justify-between gap-2">
          <h3 className="card-title">
            {t("stock.movementsTitle", "الحركات المكوّنة للرصيد")} ({movements.length})
          </h3>
          <span className="text-2xs text-ink-subtle">
            {t("stock.movementsHint", "كل سطر حركة واحدة من دفتر المخزون، مع الرصيد الذي صار إليه اللوط بعدها")}
          </span>
        </div>
        <table className="table">
          <thead>
            <tr>
              <th>{t("common.date", "التاريخ")}</th>
              <th>{t("stock.movementKind", "نوع الحركة")}</th>
              <th>{t("stock.document", "المستند")}</th>
              <th>{isMaterials ? t("stock.material", "المادة") : isRaw ? t("fr.rawMessage", "رسالة الخام") : t("fr.jobOrder", "أمر التشغيل")}</th>
              <th>{t("stock.direction", "الاتجاه")}</th>
              <th>{isMaterials ? t("common.quantity", "الكمية") : `KG / ${t("common.meter", "م")}`}</th>
              <th>{t("stock.balanceAfter", "الرصيد بعد الحركة")}</th>
              <th>{t("common.user", "المستخدم")}</th>
            </tr>
          </thead>
          <tbody>
            {movements.length === 0 && (
              <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty", "لا توجد حركات")}</td></tr>
            )}
            {movements.map((m) => <MovementRow key={m.id} movement={m} scope={scope} formatter={number} hasMeter={hasMeter} />)}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function TotalCard({ label, value, tone }: { label: string; value: string; tone?: "info" }) {
  return (
    <Card className="card-pad">
      <div className="text-2xs text-ink-subtle">{label}</div>
      <div className={`ltr-nums text-lg font-semibold ${tone === "info" ? "text-info-ink" : ""}`}>{value}</div>
    </Card>
  );
}

/** One ledger row. Which document it belongs to decides where the number links to. */
function MovementRow({ movement, scope, formatter, hasMeter }: {
  movement: StockTraceMovement; scope: StockScope; formatter: (v: number | null | undefined) => string; hasMeter: boolean;
}) {
  const { t } = useI18n();

  const documentLink = () => {
    if (movement.documentType === "Delivery" && movement.deliveryId) return `/print/delivery-statement/${movement.deliveryId}`;
    if (movement.productionOrderId && (movement.documentType === "ProductionOrder" || movement.documentType === "ReadyGoodsTransfer")) {
      return `/production-orders/${movement.productionOrderId}`;
    }
    if (movement.documentType === "RawReceiptMessage" && movement.rawMessageId) return `/print/raw-message/${movement.rawMessageId}`;
    return null;
  };

  const link = documentLink();
  const isOut = movement.direction === "Out";

  return (
    <tr className="border-b border-line last:border-0 hover:bg-surface-sunken">
      <td className="ltr-nums text-ink-muted">{movement.date.slice(0, 10)}</td>
      <td>
        <Badge tone={isOut ? "red" : "green"}>{t(`stock.kind.${movement.kind}`, movement.kind)}</Badge>
        {movement.isReversal && <span className="ms-1 text-2xs text-ink-subtle">{t("stock.reversal", "عكس حركة")}</span>}
      </td>
      <td>
        {link
          ? <Link className="ltr-nums font-medium" to={link}>{movement.documentNumber}</Link>
          : <span className="ltr-nums font-medium">{movement.documentNumber}</span>}
        <div className="text-2xs text-ink-subtle">{movement.documentType}{movement.warehouseName ? ` · ${movement.warehouseName}` : ""}</div>
        {movement.supplierName && <div className="text-2xs text-ink-subtle">{movement.supplierName}{movement.purchaseOrderNumber ? ` · ${movement.purchaseOrderNumber}` : ""}</div>}
      </td>
      <td>
        {isMaterials ? (movement.materialCode ?? "—") : (
          <>
            {scope === "raw"
              ? (movement.messageNumber ?? "—")
              : (movement.productionOrderId
                ? <Link className="ltr-nums" to={`/production-orders/${movement.productionOrderId}`}>{movement.orderNumber}</Link>
                : "—")}
            {movement.orderNumber && scope === "raw" && (
              <div className="text-2xs">
                <Link to={`/production-orders/${movement.productionOrderId}`}>{movement.orderNumber}</Link>
              </div>
            )}
            {movement.itemCode && <div className="text-2xs text-ink-subtle">{movement.itemCode}</div>}
          </>
        )}
      </td>
      <td>
        <span className={isOut ? "text-danger-ink" : "text-success-ink"}>
          {isOut ? t("stock.out", "صادر") : t("stock.in", "وارد")}
        </span>
      </td>
      <td className="ltr-nums">
        {isMaterials
          ? `${formatter(movement.quantity)} ${t(`material.unit.${movement.unit}`, movement.unit ?? "")}`.trim()
          : <>
              {movement.quantityKg != null && <span className="font-medium">{formatter(movement.quantityKg)} كجم</span>}
              {hasMeter && movement.quantityMeter != null && <span className="text-ink-muted"> · {formatter(movement.quantityMeter)} م</span>}
              {movement.quantityKg == null && movement.quantityMeter == null && "—"}
            </>}
      </td>
      <td className="ltr-nums">
        {isMaterials
          ? formatter(movement.balanceAfter)
          : <>
              {formatter(movement.balanceAfterKg)}{hasMeter ? ` · ${formatter(movement.balanceAfterMeter)}` : ""}
            </>}
      </td>
      <td className="text-ink-muted">{movement.createdBy}</td>
    </tr>
  );
}
