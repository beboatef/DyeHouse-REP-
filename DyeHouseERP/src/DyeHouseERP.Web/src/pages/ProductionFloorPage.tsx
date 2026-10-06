import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { CustomersApi, ItemsApi, ProductionFloorApi, ProductionFloorRow, ProductionStagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge, KpiCard, QuickTile, TableStateRow, TableWrap, fmt } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Factory floor - شاشة أرضية المصنع (spec section 37).
 *
 * This is an OPERATIONAL screen, not an administration screen: it answers the
 * only four questions a shift supervisor asks while standing on the floor -
 *
 *   1. what is running right now, and in which stage?
 *   2. what has been sitting there too long?
 *   3. what is finished/awaiting a decision?
 *   4. what has not started yet at all?
 *
 * Everything it shows comes from the real Job Order / stage execution records; no
 * figure is derived, guessed or stored twice. The screen is deliberately READ-ONLY:
 * closing a stage, entering an output or transferring to the next stage is real
 * production work and stays on the Job Order, where it is validated and audited.
 */

const priorityTone: Record<string, "gray" | "blue" | "yellow" | "red"> = {
  Low: "gray", Normal: "blue", High: "yellow", Urgent: "red"
};

const orderStatusTone: Record<string, "gray" | "blue" | "yellow" | "red"> = {
  Draft: "gray", RawAllocated: "yellow", InProduction: "blue",
  Paused: "yellow", Completed: "gray", Cancelled: "gray"
};

/** A job that has been on the same stage for this long needs chasing. */
const STALE_MINUTES = 240;

export default function ProductionFloorPage() {
  const { t } = useI18n();
  const [search, setSearch] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [stageId, setStageId] = useState("");
  const [priority, setPriority] = useState("");
  const [status, setStatus] = useState("");

  const { data: customers } = useQuery({
    queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true })
  });
  const { data: items } = useQuery({
    queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true })
  });
  const { data: stages } = useQuery({
    queryKey: ["production-stages", "active"], queryFn: () => ProductionStagesApi.list({ activeOnly: true })
  });

  const { data: rows, isLoading, isFetching, refetch, dataUpdatedAt } = useQuery({
    queryKey: ["production-floor", { customerId, itemId, stageId, priority, status, search }],
    queryFn: () =>
      ProductionFloorApi.get({
        customerId: customerId || undefined,
        itemId: itemId || undefined,
        stageDefinitionId: stageId || undefined,
        priority: priority || undefined,
        status: status || undefined,
        search: search.trim() || undefined
      }),
    refetchInterval: 30000
  });

  const list = rows ?? [];

  const stats = useMemo(() => {
    const running = list.filter((r) => r.currentStageStatus === "InProgress");
    const awaiting = list.filter((r) => r.awaitingStart);
    const paused = list.filter((r) => r.orderStatus === "Paused");
    const stale = running.filter((r) => (r.minutesInStage ?? 0) > STALE_MINUTES);

    // Total quantity physically committed to work happening right now, in the unit the
    // order was planned in. KG and Meter are never added together.
    const wipKg = running.reduce((sum, r) => sum + (r.baselineKg ?? r.requestedQuantityKg ?? 0), 0);
    const wipMeter = running.reduce((sum, r) => sum + (r.baselineMeter ?? r.requestedQuantityMeter ?? 0), 0);

    return { total: list.length, running: running.length, awaiting: awaiting.length, paused: paused.length, stale: stale.length, wipKg, wipMeter };
  }, [list]);

  const stageLoad = useMemo(() => {
    const byStage = new Map<string, number>();
    for (const r of list) {
      if (!r.currentStageName) continue;
      byStage.set(r.currentStageName, (byStage.get(r.currentStageName) ?? 0) + 1);
    }
    return [...byStage.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6);
  }, [list]);

  return (
    <>
      <PageHeader
        title={t("floor.title")}
        subtitle={t("floor.subtitle")}
        action={
          <div className="flex items-center gap-2">
            {isFetching && !isLoading && (
              <span className="text-2xs text-slate-400">{t("floor.refreshing")}</span>
            )}
            <span className="text-2xs text-slate-400">{t("floor.updatedAt")}: {fmt(new Date(dataUpdatedAt).toISOString())}</span>
            <Button variant="secondary" onClick={() => refetch()}>{t("floor.refresh")}</Button>
          </div>
        }
      />

      {/* Shift summary - the four numbers the supervisor checks first. */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-5">
        <KpiCard label={t("floor.running")} value={stats.running} tone={stats.running > 0 ? "info" : "neutral"} />
        <KpiCard label={t("floor.awaitingStart")} value={stats.awaiting} tone={stats.awaiting > 0 ? "warning" : "neutral"} />
        <KpiCard label={t("floor.stale")} value={stats.stale} tone={stats.stale > 0 ? "danger" : "neutral"} />
        <KpiCard
          label={t("floor.wip")}
          value={stats.wipKg > 0 ? `${fmt(stats.wipKg)} ${t("unit.kg")}` : `${fmt(stats.wipMeter)} ${t("unit.meter")}`}
          tone="brand"
        />
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-4 gap-5 mb-5">
        <Card className="xl:col-span-3 p-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <div>
            <label className="field-label">{t("floor.search")}</label>
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("floor.searchPlaceholder")}
            />
          </div>
          <div>
            <label className="field-label">{t("floor.stage")}</label>
            <Select value={stageId} onChange={(e) => setStageId(e.target.value)}>
              <option value="">{t("common.all")}</option>
              {stages?.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </Select>
          </div>
          <div>
            <label className="field-label">{t("floor.orderStatus")}</label>
            <Select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("common.all")}</option>
              <option value="RawAllocated">{t("floor.awaitingStart")}</option>
              <option value="InProduction">{t("floor.running")}</option>
              <option value="Paused">{t("prod.status.Paused")}</option>
            </Select>
          </div>
          <div>
            <label className="field-label">{t("floor.priority")}</label>
            <Select value={priority} onChange={(e) => setPriority(e.target.value)}>
              <option value="">{t("common.all")}</option>
              <option value="Urgent">Urgent</option>
              <option value="High">High</option>
              <option value="Normal">Normal</option>
              <option value="Low">Low</option>
            </Select>
          </div>
          <div>
            <label className="field-label">{t("common.customer")}</label>
            <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
              <option value="">{t("common.all")}</option>
              {customers?.map((c) => (
                <option key={c.id} value={c.id}>{c.code} - {c.name}</option>
              ))}
            </Select>
          </div>
          <div>
            <label className="field-label">{t("common.item")}</label>
            <Select value={itemId} onChange={(e) => setItemId(e.target.value)}>
              <option value="">{t("common.all")}</option>
              {items?.map((i) => (
                <option key={i.id} value={i.id}>{i.code} - {i.name}</option>
              ))}
            </Select>
          </div>
          <div className="sm:col-span-2 flex items-end">
            <Button variant="ghost" onClick={() => {
              setSearch(""); setCustomerId(""); setItemId(""); setStageId(""); setPriority(""); setStatus("");
            }}>
              {t("common.clearFilters")}
            </Button>
          </div>
        </Card>

        {/* Where the work is right now - the busiest stages, from the same live rows. */}
        <Card className="p-4">
          <h3 className="text-sm font-bold text-slate-800 mb-3">{t("floor.busiestStages")}</h3>
          {stageLoad.length === 0 ? (
            <p className="text-xs text-slate-400">{t("common.empty")}</p>
          ) : (
            <div className="grid grid-cols-2 gap-2">
              {stageLoad.map(([name, count], i) => (
                <QuickTile
                  key={name}
                  index={((i % 4) + 1) as 1 | 2 | 3 | 4}
                  icon={<span className="text-[10px] ltr-nums">#{i + 1}</span>}
                  label={name}
                  value={String(count)}
                />
              ))}
            </div>
          )}
        </Card>
      </div>

      {pausedBanner(list)}

      <Card>
        <TableWrap>
          <table className="table">
            <thead>
              <tr>
                <th>{t("floor.order")}</th>
                <th>{t("floor.customer")}</th>
                <th>{t("floor.item")}</th>
                <th>{t("floor.stage")}</th>
                <th>{t("floor.inStageFor")}</th>
                <th>{t("floor.in")}</th>
                <th>{t("floor.out")}</th>
                <th>{t("floor.loss")}</th>
                <th>{t("common.status")}</th>
              </tr>
            </thead>
            <tbody>
              <TableStateRow
                colSpan={9}
                loading={isLoading}
                isEmpty={list.length === 0}
                loadingText={t("common.loading")}
                emptyText={t("floor.empty")}
              />
              {list.map((r) => <FloorRow key={r.productionOrderId} row={r} staleMinutes={STALE_MINUTES} />)}
            </tbody>
          </table>
        </TableWrap>
      </Card>

      <p className="text-2xs text-slate-400 mt-3">{t("floor.readOnlyHint")}</p>
    </>
  );
}

/** Paused jobs are still holding allocated material, so they are called out rather than hidden. */
function pausedBanner(list: ProductionFloorRow[]) {
  const paused = list.filter((r) => r.orderStatus === "Paused");
  if (paused.length === 0) return null;
  return (
    <div className="mb-4 rounded-xl border border-warning/40 bg-warning/5 px-4 py-3 text-sm text-ink">
      <span className="font-semibold">{paused.length}</span>{" "}
      <span>{paused.map((r) => r.orderNumber).join(" · ")}</span>
    </div>
  );
}

function FloorRow({ row, staleMinutes }: { row: ProductionFloorRow; staleMinutes: number }) {
  const { t } = useI18n();
  const minutes = row.minutesInStage ?? 0;
  const isStale = row.currentStageStatus === "InProgress" && minutes > staleMinutes;

  // KG and Meter are shown in their own column and never converted into one another.
  const inText = qtyText(row.inputKg, row.inputMeter) ?? qtyText(row.baselineKg, row.baselineMeter) ?? "—";
  const outText = qtyText(row.outputKg, row.outputMeter) ?? "—";
  const lossPct = row.lossPercentKg ?? row.lossPercentMeter;
  const lossText = lossPct != null ? `${fmt(lossPct)}%` : qtyText(row.lossKg, row.lossMeter) ?? "—";

  return (
    <tr className={`border-b border-gray-100 last:border-0 hover:bg-gray-50 ${row.awaitingStart ? "bg-amber-50/40" : ""}`}>
      <td>
        <Link to={`/production-orders/${row.productionOrderId}`} className="ltr-nums font-medium text-brand-600 hover:underline">
          {row.orderNumber}
        </Link>
        <div className="mt-1">
          <Badge tone={priorityTone[row.priority] ?? "gray"}>{row.priority}</Badge>
        </div>
      </td>
      <td>
        <span className="ltr-nums text-gray-600">{row.customerCode}</span>
        <span className="block text-2xs text-gray-400">{row.customerName}</span>
      </td>
      <td>
        <span className="ltr-nums">{row.itemCode}</span>
        <span className="block text-2xs text-gray-400">
          {row.itemName}{row.color ? ` · ${row.color}` : ""}
        </span>
      </td>
      <td>
        {row.awaitingStart ? (
          <Badge tone="yellow">{row.orderNumber ? t("floor.awaitingStart") : ""}</Badge>
        ) : (
          <>
            <span className="font-medium">{row.currentStageName}</span>
            <span className="block text-2xs text-gray-400">
              {row.stageSequence != null ? `#${row.stageSequence}` : ""}{row.operator ? ` · ${row.operator}` : ""}
            </span>
          </>
        )}
      </td>
      <td className={`px-4 py-3 ltr-nums ${isStale ? "font-bold text-red-600" : ""}`}>
        {row.awaitingStart || minutes === 0 ? "—" : `${Math.round(minutes)}′`}
        {isStale && <span className="block text-[10px] font-normal">{t("floor.tooLong")}</span>}
      </td>
      <td className="ltr-nums">{inText}</td>
      <td className="ltr-nums">{outText}</td>
      <td className="ltr-nums text-gray-600">{lossText}</td>
      <td>
        <Badge tone={orderStatusTone[row.orderStatus] ?? "gray"}>{t(`prod.status.${row.orderStatus}`)}</Badge>
      </td>
    </tr>
  );
}

/** Prints whichever unit this stage is running in - never both, never a conversion. */
function qtyText(kg: number | null | undefined, meter: number | null | undefined) {
  if (kg != null) return `${fmt(kg)} KG`;
  if (meter != null) return `${fmt(meter)} M`;
  return null;
}