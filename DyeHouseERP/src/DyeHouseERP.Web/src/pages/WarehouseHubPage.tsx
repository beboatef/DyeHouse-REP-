import { useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  CustomersApi, InventoryTraceabilityApi, ItemsApi, MaterialsApi, WarehousesApi,
  type StockScope, type Warehouse
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { InventoryTraceabilityExports, ReportsExports } from "@/api/exports";
import StockTraceability from "@/components/StockTraceability";
import { useI18n } from "@/i18n";

/**
 * THE WAREHOUSE SCREEN - one warehouse at a time, chosen from a DROPDOWN.
 *
 * The four stock warehouses (WH-RAW, WH-RDY, WH-MAT, WH-SUP) are NOT tabs: the user picks one, and every
 * figure on the page - balances, ledger movements, the item card, receipts/issues/adjustments and the
 * reports - is read for THAT warehouse. The warehouse is sent to the server with every request, so this
 * is a real scope and not a filter drawn over a combined view: two warehouses holding the same item can
 * never add their stock together here.
 *
 * Nothing on this page stores or caches a balance: each section reads the append-only ledger, which is
 * what makes the figures current and traceable to the documents behind them.
 */

const STOCK_KINDS: Warehouse["kind"][] = ["RawMaterial", "ReadyGoods", "Materials", "OperatingSupplies"];

/** Which ledger a warehouse's stock lives in - the same four scopes the balance screens use. */
const scopeOf = (kind: Warehouse["kind"]): StockScope =>
  kind === "ReadyGoods" ? "ready"
    : kind === "Materials" ? "materials"
      : kind === "OperatingSupplies" ? "supplies"
        : "raw";

const scopeLabelKey = (kind: Warehouse["kind"]) =>
  kind === "ReadyGoods" ? "wh.kind.ReadyGoods"
    : kind === "Materials" ? "wh.kind.Materials"
      : kind === "OperatingSupplies" ? "wh.kind.OperatingSupplies"
        : "wh.kind.RawMaterial";

/** The order the warehouses are offered in: the four the factory runs on, in workflow order. */
const KIND_ORDER: Record<string, number> = { RawMaterial: 0, ReadyGoods: 1, Materials: 2, OperatingSupplies: 3 };

type Section = "overview" | "balances" | "movements" | "itemCard" | "documents" | "reports";

export default function WarehouseHubPage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { warehouseId } = useParams<{ warehouseId?: string }>();
  const [search] = useSearchParams();
  const [section, setSection] = useState<Section>("overview");

  const { data: warehouses, isLoading } = useQuery({
    queryKey: ["warehouses", "active"],
    queryFn: () => WarehousesApi.list({ activeOnly: true })
  });

  const stockWarehouses = useMemo(
    () => (warehouses ?? [])
      .filter((w) => STOCK_KINDS.includes(w.kind))
      .sort((a, b) => (KIND_ORDER[a.kind] ?? 9) - (KIND_ORDER[b.kind] ?? 9) || a.code.localeCompare(b.code)),
    [warehouses]);

  // The chosen warehouse comes from the URL, so a link to "the materials warehouse" is shareable and
  // survives a refresh. Old ?tab= links from before the dropdown existed are still resolved onto the
  // warehouse they were pointing at instead of dropping the user on an unrelated one.
  const legacyScope: StockScope | null = (() => {
    const tab = search.get("tab");
    if (tab === "ready") return "ready";
    if (tab === "materials") return "materials";
    if (tab === "supplies") return "supplies";
    if (tab === "balances" || tab === "receipts" || tab === "issues" || tab === "movements") return "raw";
    return null;
  })();
  const legacyWarehouse = legacyScope ? stockWarehouses.find((w) => scopeOf(w.kind) === legacyScope) : undefined;
  const current = stockWarehouses.find((w) => w.id === warehouseId) ?? legacyWarehouse ?? stockWarehouses[0] ?? null;

  const choose = (id: string) => {
    setSection("overview");
    navigate(`/warehouse/${id}`);
  };

  if (isLoading) {
    return (
      <>
        <PageHeader title={t("nav.warehouseHub")} subtitle={t("wh.subtitle")} />
        <Card className="card-pad text-sm text-ink-subtle">{t("common.loading", "جارٍ التحميل...")}</Card>
      </>
    );
  }

  if (!current) {
    return (
      <>
        <PageHeader title={t("nav.warehouseHub")} subtitle={t("wh.subtitle")} />
        <Card className="card-pad">
          <p className="text-sm text-ink-muted">
            {t("wh.noStockWarehouse", "لا يوجد مخزن مخزوني معرّف: عرّف مخزن الخام أو الجاهز أو المواد أو المستلزمات أولًا.")}
          </p>
          <Link className="btn btn-secondary btn-sm mt-3 inline-flex" to="/warehouses">
            {t("wh.tab.master", "إدارة المخازن")}
          </Link>
        </Card>
      </>
    );
  }

  const scope = scopeOf(current.kind);
  const isMaterials = current.kind === "Materials" || current.kind === "OperatingSupplies";

  const sections: { key: Section; label: string }[] = [
    { key: "overview", label: t("wh.section.overview", "نظرة عامة") },
    { key: "balances", label: t("wh.tab.balances", "الأرصدة") },
    { key: "movements", label: t("wh.tab.movements", "الحركات") },
    { key: "itemCard", label: t("wh.section.itemCard", "كارت الصنف") },
    { key: "documents", label: t("wh.section.documents", "المستندات: استلام / صرف / جرد") },
    { key: "reports", label: t("wh.tab.reports", "تقارير المخزون") }
  ];

  return (
    <>
      <PageHeader title={t("nav.warehouseHub")} subtitle={t("wh.subtitle")} />

      {/* ---- the ONE entry point: which warehouse am I looking at? ---- */}
      <Card className="card-pad mb-4 grid grid-cols-1 items-end gap-3 lg:grid-cols-[minmax(0,340px)_1fr]">
        <div>
          <label className="field-label" htmlFor="warehouse-select">{t("wh.choose", "اختيار المخزن")}</label>
          <Select id="warehouse-select" value={current.id} onChange={(e) => choose(e.target.value)}>
            {stockWarehouses.map((w) => (
              <option key={w.id} value={w.id}>{w.code} — {w.name}</option>
            ))}
          </Select>
          <p className="mt-1 text-2xs text-ink-subtle">
            {t("wh.chooseHint", "كل الأرصدة والحركات وكارت الصنف والتقارير أدناه لهذا المخزن وحده، ومقروءة من دفتره.")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone="gray">{current.code}</Badge>
          <Badge tone="blue">{t(scopeLabelKey(current.kind), current.kind)}</Badge>
          <span className="text-2xs text-ink-muted">
            {t("wh.scopeLabel", "الدفتر")}: {isMaterials
              ? t("wh.ledgerMaterials", "دفتر المواد والمستلزمات")
              : t("wh.ledgerFabric", "دفتر القماش (خام / جاهز)")}
          </span>
          <Link className="btn btn-ghost btn-sm ms-auto" to="/warehouses">{t("wh.tab.master", "إدارة المخازن")}</Link>
        </div>
      </Card>

      {/* Sections inside ONE warehouse - the warehouse itself is never chosen from these. */}
      <div className="tabs mb-5 flex-wrap">
        {sections.map((s) => (
          <button
            key={s.key}
            onClick={() => setSection(s.key)}
            className={`tab ${section === s.key ? "tab-active" : ""}`}
          >
            {s.label}
          </button>
        ))}
      </div>

      {section === "overview" && <Overview warehouse={current} scope={scope} />}
      {section === "balances" && (
        <>
          <p className="mb-3 text-xs text-ink-muted">{t("wh.balanceNote")}</p>
          {/* Keyed by warehouse: switching warehouses remounts the panel instead of reusing the previous
              warehouse's filter state. */}
          <StockTraceability key={current.id} scope={scope} warehouseId={current.id} lockWarehouse />
        </>
      )}
      {section === "movements" && <WarehouseMovements warehouse={current} scope={scope} />}
      {section === "itemCard" && <ItemCardPanel warehouse={current} scope={scope} isMaterials={isMaterials} />}
      {section === "documents" && <WarehouseDocuments warehouse={current} scope={scope} isMaterials={isMaterials} />}
      {section === "reports" && <WarehouseReports warehouse={current} scope={scope} />}
    </>
  );
}

/* ------------------------------------------------------------------------ overview */

function Overview({ warehouse, scope }: { warehouse: Warehouse; scope: StockScope }) {
  const { t } = useI18n();
  const { data, isLoading } = useQuery({
    queryKey: ["warehouse-overview", warehouse.id, scope],
    queryFn: () => InventoryTraceabilityApi.get({ scope, warehouseId: warehouse.id }),
    refetchInterval: 15000
  });

  const isMaterials = scope === "materials" || scope === "supplies";
  // Three decimals: a stock figure is a measurement, never rounded to a whole number for display.
  const number = (v: number | null | undefined) =>
    (v === null || v === undefined ? "—" : v.toLocaleString("en-US", { maximumFractionDigits: 3 }));

  return (
    <>
      <div className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Card className="card-pad">
          <div className="text-2xs text-ink-subtle">{t("wh.kpi.rows", "أرصدة معروضة")}</div>
          <div className="ltr-nums text-lg font-semibold">{isLoading ? "…" : number(data?.rows.length)}</div>
        </Card>
        <Card className="card-pad">
          <div className="text-2xs text-ink-subtle">{t("wh.kpi.movements", "حركات في هذا المخزن")}</div>
          <div className="ltr-nums text-lg font-semibold">{isLoading ? "…" : number(data?.movementCount)}</div>
        </Card>
        <Card className="card-pad">
          <div className="text-2xs text-ink-subtle">
            {isMaterials ? t("wh.kpi.balance", "إجمالي الرصيد") : t("wh.kpi.balanceKg", "إجمالي الرصيد (كجم)")}
          </div>
          <div className="ltr-nums text-lg font-semibold">
            {isLoading ? "…" : number(isMaterials ? data?.totalRemainingQuantity : data?.totalRemainingKg)}
          </div>
        </Card>
        <Card className="card-pad">
          <div className="text-2xs text-ink-subtle">
            {isMaterials ? t("wh.kpi.unit", "الوحدة") : t("wh.kpi.balanceMeter", "إجمالي الرصيد (متر)")}
          </div>
          <div className="ltr-nums text-lg font-semibold">
            {isLoading ? "…" : number(isMaterials ? null : data?.totalRemainingMeter)}
          </div>
        </Card>
      </div>

      <Card className="card-pad text-xs text-ink-muted">
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
          <div><span className="text-ink-subtle">{t("wh.code", "كود المخزن")}: </span><span className="ltr-nums font-medium">{warehouse.code}</span></div>
          <div><span className="text-ink-subtle">{t("wh.name", "اسم المخزن")}: </span><span className="font-medium">{warehouse.name}</span></div>
          <div><span className="text-ink-subtle">{t("wh.kind", "نوع المخزن")}: </span><span className="font-medium">{t(scopeLabelKey(warehouse.kind), warehouse.kind)}</span></div>
        </div>
        <p className="mt-3 text-2xs text-ink-subtle">
          {t("wh.overviewHint", "الرصيد هنا مجموع حركات دفتر المخزون لهذا المخزن - لا يوجد رصيد مخزَّن، ولذلك لا يمكن أن يكون قديمًا.")}
        </p>
      </Card>
    </>
  );
}

/* ----------------------------------------------------------------------- movements */

function WarehouseMovements({ warehouse, scope, initialKind = "" }: {
  warehouse: Warehouse; scope: StockScope; initialKind?: string;
}) {
  const { t } = useI18n();
  const [filters, setFilters] = useState({ from: "", to: "", kind: initialKind, documentNumber: "" });

  const { data, isLoading } = useQuery({
    queryKey: ["warehouse-movements", warehouse.id, scope, filters],
    queryFn: () => InventoryTraceabilityApi.get({
      scope,
      warehouseId: warehouse.id,
      from: filters.from || undefined,
      to: filters.to || undefined,
      kind: filters.kind || undefined,
      documentNumber: filters.documentNumber.trim() || undefined
    }),
    refetchInterval: 15000
  });

  const isMaterials = scope === "materials" || scope === "supplies";
  const movements = data?.movements ?? [];
  const number = (v: number | null | undefined) =>
    (v === null || v === undefined ? "—" : v.toLocaleString("en-US", { maximumFractionDigits: 3 }));

  return (
    <>
      <Card className="card-pad mb-4 grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <div>
          <label className="field-label">{t("common.from")}</label>
          <Input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} />
        </div>
        <div>
          <label className="field-label">{t("common.to")}</label>
          <Input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} />
        </div>
        <div>
          <label className="field-label">{t("stock.movementKind", "نوع الحركة")}</label>
          <Select value={filters.kind} onChange={(e) => setFilters({ ...filters, kind: e.target.value })}>
            <option value="">{t("common.all")}</option>
            {(isMaterials
              ? ["Received", "Issued", "Returned", "TransferredIn", "TransferredOut"]
              : scope === "raw"
                ? ["Received", "Issued", "IssuedExternal", "Returned", "TransferredIn", "TransferredOut", "AdjustedIn", "AdjustedOut"]
                : ["Produced", "Delivered", "Returned", "TransferredOut", "AdjustedIn", "AdjustedOut", "Reversal"]
            ).map((k) => <option key={k} value={k}>{t(`stock.kind.${k}`, k)}</option>)}
          </Select>
        </div>
        <div>
          <label className="field-label">{t("stock.documentNumber", "رقم المستند")}</label>
          <Input
            value={filters.documentNumber}
            onChange={(e) => setFilters({ ...filters, documentNumber: e.target.value })}
            placeholder={t("wh.documentHint", "رسالة / أمر تشغيل / إذن صرف / تسوية")}
          />
        </div>
        <div className="text-xs text-ink-muted">
          {t("wh.tab.movements")}: <span className="ltr-nums font-semibold">{movements.length}</span>
        </div>
      </Card>

      <Card className="overflow-x-auto">
        <table className="table">
          <thead>
            <tr>
              <th>{t("common.date")}</th>
              <th>{t("stock.movementKind", "نوع الحركة")}</th>
              <th>{t("stock.document", "المستند")}</th>
              <th>{isMaterials ? t("stock.material", "المادة") : t("common.item")}</th>
              <th>{t("stock.direction", "الاتجاه")}</th>
              <th>{isMaterials ? t("common.quantity") : "KG"}</th>
              {!isMaterials && <th>{t("common.meter", "م")}</th>}
              <th>{t("stock.balanceAfter", "الرصيد بعد الحركة")}</th>
              <th>{t("common.user")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={9} className="px-4 py-6 text-center text-ink-subtle">{t("common.loading")}</td></tr>
            )}
            {!isLoading && movements.length === 0 && (
              <tr><td colSpan={9} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty")}</td></tr>
            )}
            {movements.map((m) => (
              <tr key={m.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="ltr-nums text-ink-muted">{m.date.slice(0, 10)}</td>
                <td><Badge tone={m.direction === "Out" ? "red" : "green"}>{t(`stock.kind.${m.kind}`, m.kind)}</Badge></td>
                <td>
                  <div className="ltr-nums font-medium">{m.documentNumber}</div>
                  <div className="text-2xs text-ink-subtle">{m.documentType}</div>
                </td>
                <td>
                  <div className="ltr-nums font-medium">{isMaterials ? m.materialCode : m.itemCode}</div>
                  <div className="text-2xs text-ink-subtle">
                    {isMaterials ? "" : (m.messageNumber ?? m.orderNumber ?? "")}
                  </div>
                </td>
                <td>{m.direction === "Out" ? t("stock.out", "صادر") : t("stock.in", "وارد")}</td>
                {isMaterials ? (
                  <td className="ltr-nums">{number(m.quantity)} {m.unit ?? ""}</td>
                ) : (
                  <>
                    <td className="ltr-nums">{number(m.quantityKg)}</td>
                    <td className="ltr-nums">{number(m.quantityMeter)}</td>
                  </>
                )}
                <td className="ltr-nums font-semibold">
                  {isMaterials
                    ? number(m.balanceAfter)
                    : `${number(m.balanceAfterKg)}${m.balanceAfterMeter === null ? "" : ` / ${number(m.balanceAfterMeter)}`}`}
                </td>
                <td className="text-ink-muted">{m.createdBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

/* ------------------------------------------------------------------------ item card */

function ItemCardPanel({ warehouse, scope, isMaterials }: {
  warehouse: Warehouse; scope: StockScope; isMaterials: boolean;
}) {
  const { t } = useI18n();
  const [itemId, setItemId] = useState("");
  const [materialId, setMaterialId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data: items } = useQuery({
    queryKey: ["items", "active"],
    queryFn: () => ItemsApi.list({ activeOnly: true }),
    enabled: !isMaterials
  });
  const { data: materials } = useQuery({
    queryKey: ["materials", "active"],
    queryFn: () => MaterialsApi.list({ activeOnly: true }),
    enabled: isMaterials
  });
  // Hook order is fixed: the customers query is always declared and only disabled for a materials
  // warehouse, never called conditionally inside the JSX below.
  const { data: customers } = useQuery({
    queryKey: ["customers", "active"],
    queryFn: () => CustomersApi.list({ activeOnly: true }),
    enabled: !isMaterials
  });

  const identityChosen = isMaterials ? !!materialId : !!itemId;

  const { data: card, isLoading, error } = useQuery({
    queryKey: ["item-card", warehouse.id, scope, itemId, materialId, customerId, from, to],
    queryFn: () => InventoryTraceabilityApi.itemCard({
      warehouseId: warehouse.id,
      itemId: itemId || undefined,
      materialId: materialId || undefined,
      customerId: isMaterials ? undefined : (customerId || undefined),
      from: from || undefined,
      to: to || undefined
    }),
    enabled: identityChosen
  });

  const number = (v: number | null | undefined) =>
    (v === null || v === undefined ? "—" : v.toLocaleString("en-US", { maximumFractionDigits: 3 }));
  const hasMeter = !isMaterials && (card?.baseUnit === "Meter" || card?.secondaryUnit === "Meter");
  const hasKg = !isMaterials && (card?.baseUnit === "KG" || card?.secondaryUnit === "KG");

  return (
    <>
      <p className="mb-3 text-xs text-ink-muted">
        {t("wh.itemCardHint", "الرصيد الافتتاحي وحركات الفترة والرصيد الختامي، من دفتر هذا المخزن وحده - الكيلو والمتر كلٌّ في عموده.")}
      </p>

      <Card className="card-pad mb-4 grid grid-cols-1 items-end gap-3 sm:grid-cols-2 lg:grid-cols-5">
        {isMaterials ? (
          <div>
            <label className="field-label">{t("stock.material", "المادة / الكيماوي")}</label>
            <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)}>
              <option value="">{t("wh.chooseItem", "اختر صنفًا")}</option>
              {(materials ?? []).map((m) => <option key={m.id} value={m.id}>{m.code} — {m.name}</option>)}
            </Select>
          </div>
        ) : (
          <>
            <div>
              <label className="field-label">{t("common.item")}</label>
              <Select value={itemId} onChange={(e) => setItemId(e.target.value)}>
                <option value="">{t("wh.chooseItem", "اختر صنفًا")}</option>
                {(items ?? []).map((i) => <option key={i.id} value={i.id}>{i.code} — {i.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">{t("common.customer")}</label>
              <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                <option value="">{t("common.all")}</option>
                {(customers ?? []).map((c) => <option key={c.id} value={c.id}>{c.code} — {c.name}</option>)}
              </Select>
            </div>
          </>
        )}
        <div>
          <label className="field-label">{t("common.from")}</label>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div>
          <label className="field-label">{t("common.to")}</label>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
        <div className="text-2xs text-ink-subtle">
          {identityChosen
            ? t("wh.itemCardScoped", "الكارت لمخزن {{w}} فقط").replace("{{w}}", warehouse.code)
            : t("wh.itemCardPick", "اختر الصنف لعرض كارت المخزن")}
        </div>
      </Card>

      {error != null && (
        <Card className="card-pad mb-4 text-sm text-danger-ink">
          {t("wh.itemCardError", "تعذّر إنشاء الكارت - راجع المخزن والصنف المختارين.")}
        </Card>
      )}

      {identityChosen && card && (
        <>
          <div className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Card className="card-pad">
              <div className="text-2xs text-ink-subtle">{t("wh.itemCard.opening", "الرصيد الافتتاحي")}</div>
              <div className="ltr-nums text-lg font-semibold">
                {isMaterials ? `${number(card.openingQuantity)} ${card.materialUnit ?? ""}` : number(hasKg ? card.openingKg : null)}
                {!isMaterials && hasMeter && <span className="text-sm text-ink-muted"> / {number(card.openingMeter)} م</span>}
              </div>
            </Card>
            <Card className="card-pad">
              <div className="text-2xs text-ink-subtle">{t("wh.itemCard.in", "وارد الفترة")}</div>
              <div className="ltr-nums text-lg font-semibold text-success-ink">
                {isMaterials ? number(card.inQuantity) : number(hasKg ? card.inKg : null)}
                {!isMaterials && hasMeter && <span className="text-sm text-ink-muted"> / {number(card.inMeter)}</span>}
              </div>
            </Card>
            <Card className="card-pad">
              <div className="text-2xs text-ink-subtle">{t("wh.itemCard.out", "صادر الفترة")}</div>
              <div className="ltr-nums text-lg font-semibold text-danger-ink">
                {isMaterials ? number(card.outQuantity) : number(hasKg ? card.outKg : null)}
                {!isMaterials && hasMeter && <span className="text-sm text-ink-muted"> / {number(card.outMeter)}</span>}
              </div>
            </Card>
            <Card className="card-pad">
              <div className="text-2xs text-ink-subtle">{t("wh.itemCard.closing", "الرصيد الختامي")}</div>
              <div className="ltr-nums text-lg font-semibold">
                {isMaterials ? `${number(card.closingQuantity)} ${card.materialUnit ?? ""}` : number(hasKg ? card.closingKg : null)}
                {!isMaterials && hasMeter && <span className="text-sm text-ink-muted"> / {number(card.closingMeter)} م</span>}
              </div>
            </Card>
          </div>

          <Card className="mb-3 card-pad text-xs text-ink-muted">
            <span className="font-medium text-ink">{isMaterials ? card.materialCode : card.itemCode}</span>
            {" · "}
            {isMaterials ? card.materialName : card.itemName}
            {" · "}
            {card.warehouseCode} — {card.warehouseName}
            {!isMaterials && card.customerCode ? ` · ${card.customerCode} — ${card.customerName}` : ""}
          </Card>

          <Card className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>{t("common.date")}</th>
                  <th>{t("stock.movementKind", "نوع الحركة")}</th>
                  <th>{t("stock.document", "المستند")}</th>
                  <th>{t("stock.direction", "الاتجاه")}</th>
                  <th>{isMaterials ? t("common.quantity") : "KG"}</th>
                  {!isMaterials && <th>{t("common.meter", "م")}</th>}
                  <th>{t("stock.balanceAfter", "الرصيد بعد الحركة")}</th>
                  <th>{t("common.user")}</th>
                </tr>
              </thead>
              <tbody>
                {isLoading && (
                  <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">{t("common.loading")}</td></tr>
                )}
                {!isLoading && card.movements.length === 0 && (
                  <tr><td colSpan={8} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty")}</td></tr>
                )}
                {card.movements.map((m) => (
                  <tr key={m.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                    <td className="ltr-nums text-ink-muted">{m.date.slice(0, 10)}</td>
                    <td><Badge tone={m.direction === "Out" ? "red" : "green"}>{t(`stock.kind.${m.kind}`, m.kind)}</Badge></td>
                    <td>
                      <div className="ltr-nums font-medium">{m.documentNumber}</div>
                      <div className="text-2xs text-ink-subtle">{m.documentType}</div>
                    </td>
                    <td>{m.direction === "Out" ? t("stock.out", "صادر") : t("stock.in", "وارد")}</td>
                    {isMaterials ? (
                      <td className="ltr-nums">{number(m.quantity)} {m.unit ?? ""}</td>
                    ) : (
                      <>
                        <td className="ltr-nums">{number(m.quantityKg)}</td>
                        <td className="ltr-nums">{number(m.quantityMeter)}</td>
                      </>
                    )}
                    <td className="ltr-nums font-semibold">
                      {isMaterials
                        ? number(m.balanceAfter)
                        : `${number(m.balanceAfterKg)}${m.balanceAfterMeter === null ? "" : ` / ${number(m.balanceAfterMeter)}`}`}
                    </td>
                    <td className="text-ink-muted">{m.createdBy}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </>
      )}
    </>
  );
}

/* ------------------------------------------------------------------------ documents */

function WarehouseDocuments({ warehouse, scope, isMaterials }: {
  warehouse: Warehouse; scope: StockScope; isMaterials: boolean;
}) {
  const { t } = useI18n();
  const [kind, setKind] = useState("");

  // The documents of THIS warehouse are its ledger rows: a receipt posts an IN row, an issue an OUT row,
  // an adjustment its own row. Their own screens stay the place where a document is created or printed.
  const chips: { key: string; label: string }[] = isMaterials
    ? [
        { key: "Received", label: t("wh.doc.receipts", "استلام (شراء)") },
        { key: "Issued", label: t("wh.doc.issues", "صرف") },
        { key: "", label: t("common.all") }
      ]
    : scope === "raw"
      ? [
          { key: "Received", label: t("wh.doc.receipts", "استلام الخام") },
          { key: "Issued", label: t("wh.doc.issues", "صرف للتشغيل") },
          { key: "IssuedExternal", label: t("wh.doc.releases", "إفراج / تشغيل خارجي") },
          { key: "AdjustedIn", label: t("wh.doc.adjustments", "جرد وتسويات") },
          { key: "", label: t("common.all") }
        ]
      : [
          { key: "Produced", label: t("wh.doc.produced", "ترحيل الجاهز") },
          { key: "Delivered", label: t("wh.doc.delivered", "تسليم") },
          { key: "AdjustedIn", label: t("wh.doc.adjustments", "جرد وتسويات") },
          { key: "", label: t("common.all") }
        ];

  const links: { to: string; label: string }[] = isMaterials
    ? [
        { to: "/purchases", label: t("nav.purchases") },
        { to: "/materials", label: t("nav.materials") },
        { to: "/material-sales", label: t("nav.materialSales") },
        { to: "/stock-adjustments", label: t("wh.tab.adjustments") }
      ]
    : scope === "raw"
      ? [
          { to: "/raw-messages", label: t("wh.tab.receipts") },
          { to: "/raw-external-releases", label: t("wh.tab.issues") },
          { to: "/customer-returns", label: t("nav.customerReturns") },
          { to: "/stock-adjustments", label: t("wh.tab.adjustments") }
        ]
      : [
          { to: "/ready-goods", label: t("nav.readyGoods") },
          { to: "/deliveries", label: t("nav.delivery") },
          { to: "/stock-adjustments", label: t("wh.tab.adjustments") }
        ];

  return (
    <>
      <Card className="card-pad mb-4 flex flex-wrap items-center gap-2">
        {chips.map((c) => (
          <Button
            key={c.key || "all"}
            variant={kind === c.key ? "primary" : "secondary"}
            onClick={() => setKind(c.key)}
          >
            {c.label}
          </Button>
        ))}
        <span className="ms-auto flex flex-wrap gap-2">
          {links.map((l) => (
            <Link key={l.to} className="btn btn-ghost btn-sm" to={l.to}>{l.label}</Link>
          ))}
        </span>
      </Card>

      {/* Keyed on the chosen kind so the table below always reflects the button that was pressed. */}
      <WarehouseMovements key={`${warehouse.id}-${kind}`} warehouse={warehouse} scope={scope} initialKind={kind} />
    </>
  );
}

/* ------------------------------------------------------------------------- reports */

function WarehouseReports({ warehouse, scope }: { warehouse: Warehouse; scope: StockScope }) {
  const { t } = useI18n();
  const params = { scope, warehouseId: warehouse.id };

  return (
    <Card className="card-pad">
      <p className="mb-3 text-xs text-ink-muted">
        {t("wh.reportsHint", "كل تقرير يخرج بنفس أرقام هذه الشاشة لنفس المخزن - لا يُعاد حساب شيء ولا يُدمج مخزنان في رقم واحد.")}
      </p>
      <div className="flex flex-wrap gap-2">
        {/* Each handler starts the download itself: a click handler returns void, never a promise. */}
        <Button variant="secondary" onClick={() => void InventoryTraceabilityExports.stock("excel")(params)}>
          {t("wh.report.balancesExcel", "أرصدة هذا المخزن (Excel)")}
        </Button>
        <Button variant="secondary" onClick={() => void InventoryTraceabilityExports.stock("pdf")(params)}>
          {t("wh.report.balancesPdf", "أرصدة هذا المخزن (PDF)")}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void ReportsExports.warehouseBalances("excel")({ warehouseId: warehouse.id })}
        >
          {t("wh.report.warehouseBalancesExcel", "تقرير أرصدة المخازن (Excel)")}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void ReportsExports.warehouseBalances("pdf")({ warehouseId: warehouse.id })}
        >
          {t("wh.report.warehouseBalancesPdf", "تقرير أرصدة المخازن (PDF)")}
        </Button>
      </div>
      <div className="mt-4 text-2xs text-ink-subtle">
        {t("wh.report.scopeNote", "تقرير أرصدة المخازن يعرض كل مخزن في صفوفه مع إجمالي لكل مخزن، ثم إجماليًا عامًا منفصلًا لكل دفتر ووحدة.")}
      </div>
    </Card>
  );
}
