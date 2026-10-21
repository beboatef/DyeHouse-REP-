import { useState } from "react";
import { useParams, Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, CostAccountingApi, CostCategory, ProductionStagesApi, type ProductionOrder, type ProductionPriority, type JobOrderType, type UnitOfMeasure } from "@/api/client";

/**
 * The cost-line categories a user can add (spec section 35). Order matters - it
 * is the order shown in the picker. External processing is intentionally absent:
 * it is derived from the external-release rows, so it is not hand-entered.
 */
const COST_CATEGORIES: CostCategory[] = [
  "Labor", "Electricity", "Fuel", "Maintenance", "Transport", "Packaging", "Repair", "Other"
];
import { CostingApi } from "@/api/documents";
import { JobOrderCostingExports, ProductionOrdersExports } from "@/api/exports";
import { PageHeader, Card, Button, Input, Select, Field, Badge, KpiCard, TableStateRow, errorMessage } from "@/components/ui";
import StageRow from "@/pages/StageRow";
import { Coins, Percent, Tag, TrendingUp, Receipt } from "lucide-react";
import AttachmentsPanel from "@/components/AttachmentsPanel";
import { useI18n } from "@/i18n";

const statusLabel: Record<string, string> = {
  // Paused = موقوف مؤقتًا: the order is resumable, which is what separates it
  // from a cancelled one (spec section 19).
  Draft: "مسودة", RawAllocated: "تم تخصيص الخام", InProduction: "قيد التشغيل", Paused: "موقوف مؤقتًا", Completed: "مكتمل", Cancelled: "ملغي"
};

export default function ProductionOrderDetailPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();
  const qc = useQueryClient();

  const { data: order, isLoading } = useQuery({
    queryKey: ["production-orders", id],
    queryFn: () => ProductionOrdersApi.get(id!),
    enabled: !!id
  });

  // The raw-allocation form (and the approval request it raised) is GONE: raw material is no longer
  // allocated to an existing Job Order. The deduction is made once, by the raw issue that CREATES the
  // order, which also records the order's single allocation row - see the read-only "الخام المصروف" card.
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [cancelError, setCancelError] = useState<string | null>(null);
  const [editOpen, setEditOpen] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const [editValues, setEditValues] = useState({
    orderDate: "", color: "", rawOrigin: "", customerReference: "", notes: "",
    priority: "Normal" as ProductionPriority, jobOrderType: "ClosedLine" as JobOrderType
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["production-orders", id] });
    qc.invalidateQueries({ queryKey: ["stock-traceability"] });
    qc.invalidateQueries({ queryKey: ["production-floor"] });
    qc.invalidateQueries({ queryKey: ["separates"] });
  };

  const editMutation = useMutation({
    mutationFn: (order: ProductionOrder) =>
      ProductionOrdersApi.update(order.id, {
        orderDate: editValues.orderDate,
        color: editValues.color || undefined,
        rawOrigin: editValues.rawOrigin || undefined,
        customerReference: editValues.customerReference || undefined,
        notes: editValues.notes || undefined,
        priority: editValues.priority,
        jobOrderType: editValues.jobOrderType
      }),
    onSuccess: () => { invalidate(); setEditOpen(false); setEditError(null); },
    onError: (err: ApiErrorLike) => setEditError(errorMessage(err, t("common.error")))
  });

  const cancelMutation = useMutation({
    mutationFn: () => ProductionOrdersApi.cancel(id!, cancelReason.trim()),
    onSuccess: () => { invalidate(); setCancelOpen(false); setCancelReason(""); setCancelError(null); },
    onError: (err: ApiErrorLike) => setCancelError(errorMessage(err, t("common.error")))
  });

  if (isLoading || !order) return <Card className="card-pad text-center text-ink-subtle">جارٍ التحميل...</Card>;

  // The ordinary cancellation ("unused raw material return") only covers cases A and B:
  // a draft with no issue, or an issued order that no stage has consumed yet. Once any stage
  // movement is recorded, the BACKEND refuses it (spec section 9, case C) - so the UI must not
  // offer the action that would fail; it explains the situation instead.
  const productionStarted = order.hasProductionStarted;
  const canCancel = !productionStarted && order.status !== "Completed" && order.status !== "Cancelled";
  const cancelBlockedByProduction = productionStarted && order.status !== "Cancelled";
  // Descriptive/scheduling edits are allowed while the order is open; the identity (customer/item),
  // the requested quantity, posted ledger rows, recorded stage output and approved costs never change here.
  const canEdit = order.status !== "Completed" && order.status !== "Cancelled";
  const openEdit = () => {
    setEditValues({
      orderDate: order.orderDate.slice(0, 10),
      color: order.color ?? "",
      rawOrigin: order.rawOrigin ?? "",
      customerReference: order.customerReference ?? "",
      notes: order.notes ?? "",
      priority: order.priority,
      jobOrderType: order.jobOrderType
    });
    setEditError(null);
    setEditOpen(true);
  };
  return (
    <>
      <PageHeader
        title={order.orderNumber}
        subtitle={`${order.customerCode} - ${order.customerName} · ${order.itemCode} - ${order.itemName}${order.color ? " · " + order.color : ""}`}
        action={
          <div className="flex items-center gap-2">
            <Link to={`/print/production-order/${order.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
            <Button variant="ghost" onClick={ProductionOrdersExports.documentPdf(order.id)}>تنزيل PDF</Button>
            <Badge tone="blue">{statusLabel[order.status]}</Badge>
          </div>
        }
      />

      {canCancel && (
        <Card className="card-pad mb-6">
          {!cancelOpen ? (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-2xs text-ink-subtle">{t("po.cancelHint")}</p>
              <Button variant="ghost" size="sm" onClick={() => setCancelOpen(true)}>{t("po.cancelOrder")}</Button>
            </div>
          ) : (
            <div className="space-y-2">
              <p className="text-2xs text-ink-subtle">{t("po.cancelHint")}</p>
              <Field label={t("po.cancelReason")}>
                <Input value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
              </Field>
              {cancelError && <p className="form-error">{cancelError}</p>}
              <div className="flex gap-2">
                <Button
                  disabled={cancelReason.trim() === "" || cancelMutation.isPending}
                  onClick={() => cancelMutation.mutate()}
                >
                  {t("po.confirmCancel")}
                </Button>
                <Button variant="secondary" onClick={() => { setCancelOpen(false); setCancelReason(""); setCancelError(null); }}>
                  {t("common.close")}
                </Button>
              </div>
            </div>
          )}
        </Card>
      )}

      {cancelBlockedByProduction && (
        <Card className="card-pad mb-6">
          <p className="text-2xs text-ink-subtle">{t("po.cancelBlockedStarted")}</p>
        </Card>
      )}

      {canEdit && (
        <Card className="card-pad mb-6">
          {!editOpen ? (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-2xs text-ink-subtle">{t("po.editHint")}</p>
              <Button variant="ghost" size="sm" onClick={openEdit}>{t("po.editDetails")}</Button>
            </div>
          ) : (
            <div className="space-y-3">
              <p className="text-2xs text-ink-subtle">{t("po.editHint")}</p>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                <Field label={t("po.orderDate")}>
                  <Input type="date" value={editValues.orderDate} onChange={(e) => setEditValues({ ...editValues, orderDate: e.target.value })} />
                </Field>
                <Field label={t("po.color")}>
                  <Input value={editValues.color} onChange={(e) => setEditValues({ ...editValues, color: e.target.value })} />
                </Field>
                <Field label={t("po.customerReference")}>
                  <Input value={editValues.customerReference} onChange={(e) => setEditValues({ ...editValues, customerReference: e.target.value })} />
                </Field>
                <Field label={t("po.rawOrigin")}>
                  <Input value={editValues.rawOrigin} onChange={(e) => setEditValues({ ...editValues, rawOrigin: e.target.value })} />
                </Field>
                <Field label={t("po.priority")}>
                  <Select value={editValues.priority} onChange={(e) => setEditValues({ ...editValues, priority: e.target.value as ProductionPriority })}>
                    <option value="Low">Low</option>
                    <option value="Normal">Normal</option>
                    <option value="High">High</option>
                    <option value="Urgent">Urgent</option>
                  </Select>
                </Field>
                <Field label={t("po.lineType")}>
                  <Select value={editValues.jobOrderType} onChange={(e) => setEditValues({ ...editValues, jobOrderType: e.target.value as JobOrderType })}>
                    <option value="ClosedLine">{t("jo.type.ClosedLine")}</option>
                    <option value="OpenLine">{t("jo.type.OpenLine")}</option>
                  </Select>
                </Field>
                <Field label={t("common.notes")}>
                  <Input value={editValues.notes} onChange={(e) => setEditValues({ ...editValues, notes: e.target.value })} />
                </Field>
              </div>
              <p className="text-2xs text-warning-ink">{t("po.editRestricted")}</p>
              {editError && <p className="form-error">{editError}</p>}
              <div className="flex gap-2">
                <Button disabled={editMutation.isPending || editValues.orderDate === ""} onClick={() => order && editMutation.mutate(order)}>
                  {t("common.save")}
                </Button>
                <Button variant="secondary" onClick={() => { setEditOpen(false); setEditError(null); }}>{t("common.close")}</Button>
              </div>
            </div>
          )}
        </Card>
      )}

      {/* Raw material ledger for THIS order - READ ONLY. Material is issued ONCE, by the raw issue that
          created the order (POST /production-orders/issue); that issue posts the OUT movement and records the
          order's single allocation row in the same transaction. There is deliberately no allocation form
          here any more: a reserved quantity that deducted nothing was a second, conflicting way to claim
          raw material, and it is what allowed the same lot to be deducted twice. Pausing releases material
          and resuming issues it again, each through its own real ledger movement. */}
      <Card className="card-pad mb-6">
        <h2 className="card-title mb-1">{t("po.rawIssue", "الخام المصروف")}</h2>
        <p className="text-xs text-ink-muted mb-4">{t("po.rawIssueHint")}</p>

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-4">
          <div className="rounded-lg bg-surface-sunken px-3 py-2">
            <div className="text-2xs text-ink-muted">{t("po.alloc.required")}</div>
            <div className="ltr-nums font-medium">
              {order.requestedQuantityKg != null && `${order.requestedQuantityKg} ${t("common.kg")} `}
              {order.requestedQuantityMeter != null && `${order.requestedQuantityMeter} ${t("common.meter")}`}
            </div>
          </div>
          <div className="rounded-lg bg-surface-sunken px-3 py-2">
            <div className="text-2xs text-ink-muted">{t("po.alloc.allocated")}</div>
            <div className="ltr-nums font-medium">
              {(order.requestedQuantityKg != null || order.allocatedQuantityKg > 0) &&
                `${order.allocatedQuantityKg} ${t("common.kg")} `}
              {(order.requestedQuantityMeter != null || order.allocatedQuantityMeter > 0) &&
                `${order.allocatedQuantityMeter} ${t("common.meter")}`}
            </div>
          </div>
          <div className="rounded-lg bg-surface-sunken px-3 py-2">
            <div className="text-2xs text-ink-muted">{t("po.alloc.remaining")}</div>
            <div className="ltr-nums font-medium">
              {(order.requestedQuantityKg != null || order.allocatedQuantityKg > 0) &&
                `${order.remainingToAllocateKg} ${t("common.kg")} `}
              {(order.requestedQuantityMeter != null || order.allocatedQuantityMeter > 0) &&
                `${order.remainingToAllocateMeter} ${t("common.meter")}`}
            </div>
          </div>
          <div className={`rounded-xl px-3 py-2 ${order.isRawAllocationComplete ? "bg-success-soft" : "bg-warning-soft"}`}>
            <div className="text-2xs text-ink-muted">{t("po.alloc.status")}</div>
            <div className={order.isRawAllocationComplete ? "text-sm font-medium text-success-ink" : "text-sm font-medium text-warning-ink"}>
              {order.isRawAllocationComplete ? t("po.alloc.statusComplete") : t("po.alloc.statusIncomplete")}
            </div>
          </div>
        </div>

        {order.rawAllocations.length > 0 ? (
          <table className="table">
            <thead>
              <tr>
                <th>{t("po.rawIssueLot")}</th>
                <th>{t("po.rawIssueQuantity")}</th>
                <th>{t("po.rawIssueDate", "التاريخ")}</th>
                <th>{t("po.rawIssueBy")}</th>
              </tr>
            </thead>
            <tbody>
              {order.rawAllocations.map((a) => (
                <tr key={a.id} className="border-b border-line last:border-0">
                  <td className="ltr-nums font-medium">{a.messageNumber}</td>
                  <td className="ltr-nums">
                    {a.quantityKg != null && `${a.quantityKg} كجم`}
                    {a.quantityKg != null && a.quantityMeter != null && " / "}
                    {a.quantityMeter != null && `${a.quantityMeter} م`}
                  </td>
                  <td className="ltr-nums text-ink-muted">
                    {a.allocatedAtUtc ? new Date(a.allocatedAtUtc).toLocaleDateString("en-GB") : "—"}
                  </td>
                  <td className="text-ink-muted">{a.allocatedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p className="text-sm text-ink-subtle">{t("po.rawIssueNone")}</p>
        )}

        <p className="mt-3 text-2xs text-ink-subtle">{t("po.rawIssueFootnote")}</p>
      </Card>

      {/* Stage timeline */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">مراحل التشغيل</h2>
        <p className="mb-4 text-2xs text-ink-subtle">{t("stage.transferHint")}</p>
        <div className="space-y-3">
          {order.stageExecutions.map((s) => (
            <StageRow key={s.id} stage={s} />
          ))}
        </div>
      </Card>

      {/* Stage transfer (spec sections 13-17) and Pause/Resume (spec section 19). */}
      <StageFlowPanel order={order} />
      {order.stageExecutions.filter((s) => s.drawingNumber).map((s) => (
        <p key={`drawing-${s.id}`} className="mt-2 text-sm">{s.stageName} · {t("printing.drawingNumber")}: <b dir="auto">{s.drawingNumber}</b></p>
      ))}

      {/* Job order costing tab (spec section 34): estimate, live actual, approved. */}
      <CostPanel
        productionOrderId={order.id}
        productionOrderNumber={order.orderNumber}
        isCompleted={order.status === "Completed"}
      />

      {/* Profitability (spec section 37): snapshotted price vs the live actual cost. */}
      <ProfitabilityPanel productionOrderId={order.id} />

      {/* Private attachments for this job order (spec section 47). */}
      <div className="mt-6">
        <AttachmentsPanel entityType="ProductionOrder" entityId={order.id} />
      </div>
    </>
  );
}

/**
 * The dynamic stage flow and the pause/resume controls (spec sections 13-17 and 19).
 *
 * There is no "End Stage" button here: the single transfer form IS the end of a
 * stage. It closes the current stage with the output the user types, which locks
 * it, and activates exactly the stage they picked next, baselined on that output.
 * Picking the stage marked الجاهز moves the output into Ready Goods and completes
 * the order, which is why there is no separate "End Job Order" button either.
 */
function StageFlowPanel({ order }: { order: ProductionOrder }) {
  const { t } = useI18n();
  const qc = useQueryClient();

  // Off by default: ticking an operation MOVES the job there and records nothing. Switching this on says
  // "and record what this operation consumed and produced" as well.
  const [recordFigures, setRecordFigures] = useState(false);
  const [drawingNumber, setDrawingNumber] = useState("");
  const [drawingReason, setDrawingReason] = useState("");
  const [inputKg, setInputKg] = useState("");
  const [inputMeter, setInputMeter] = useState("");
  const [outputKg, setOutputKg] = useState("");
  const [outputMeter, setOutputMeter] = useState("");
  const [separatesKg, setSeparatesKg] = useState("");
  const [separatesMeter, setSeparatesMeter] = useState("");
  const [secondGradeKg, setSecondGradeKg] = useState("");
  const [secondGradeMeter, setSecondGradeMeter] = useState("");
  const [secondGradePieceCount, setSecondGradePieceCount] = useState("");
  const [notes, setNotes] = useState("");
  const [flowError, setFlowError] = useState<string | null>(null);

  const [pauseOpen, setPauseOpen] = useState(false);
  const [releaseKg, setReleaseKg] = useState("");
  const [releaseMeter, setReleaseMeter] = useState("");
  const [pauseReason, setPauseReason] = useState("");
  const [releaseLotId, setReleaseLotId] = useState("");
  const [resumeQtyKg, setResumeQtyKg] = useState("");
  const [resumeQtyMeter, setResumeQtyMeter] = useState("");
  const [resumeMessageId, setResumeMessageId] = useState("");
  const [resumeItemId, setResumeItemId] = useState("");

  const { data: stages } = useQuery({
    queryKey: ["production-stages", "all"],
    queryFn: () => ProductionStagesApi.list()
  });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-allocation", order.customerId],
    queryFn: () => RawMessagesApi.list({ customerId: order.customerId, onlyWithBalance: true }),
    enabled: resumeMessageId !== "" || order.status === "Paused"
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["production-orders", order.id] });
    qc.invalidateQueries({ queryKey: ["stock-traceability"] });
    qc.invalidateQueries({ queryKey: ["production-floor"] });
    qc.invalidateQueries({ queryKey: ["separates"] });
  };

  const current = order.stageExecutions.find((s) => s.status === "InProgress") ?? null;

  /**
   * Moves the job to the chosen operation - ticking its box IS the move.
   *
   * `recordFigures` is false for the ordinary case, which makes the transfer a ROUTE step: the operation is
   * closed with no output, no loss and no stock movement at all (the server refuses to invent production
   * from a selection). It is true only when the operator explicitly asked to record this operation's figures,
   * or when the chosen operation is the Ready Goods stop - whose final weights are exactly what posts the
   * ready-goods stock and are therefore mandatory. Every figure sent is validated server-side against the
   * item's own units and is never converted between KG and Meter.
   */
  const transferMutation = useMutation({
    mutationFn: (input: { nextStageDefinitionId: string; recordFigures: boolean }) =>
      ProductionOrdersApi.transferStage(current!.id, {
        nextStageDefinitionId: input.nextStageDefinitionId,
        drawingNumber: drawingNumber.trim() || undefined,
        inputKg: input.recordFigures && inputKg ? Number(inputKg) : undefined,
        inputMeter: input.recordFigures && inputMeter ? Number(inputMeter) : undefined,
        outputKg: input.recordFigures && outputKg ? Number(outputKg) : undefined,
        outputMeter: input.recordFigures && outputMeter ? Number(outputMeter) : undefined,
        separatesKg: input.recordFigures && separatesKg ? Number(separatesKg) : undefined,
        separatesMeter: input.recordFigures && separatesMeter ? Number(separatesMeter) : undefined,
        secondGradeKg: input.recordFigures && secondGradeKg ? Number(secondGradeKg) : undefined,
        secondGradeMeter: input.recordFigures && secondGradeMeter ? Number(secondGradeMeter) : undefined,
        secondGradePieceCount: input.recordFigures && secondGradePieceCount ? Number(secondGradePieceCount) : undefined,
        notes: notes.trim() || undefined
      }),
    onSuccess: () => {
      invalidate();
      setRecordFigures(false);
      setDrawingNumber("");
      setInputKg("");
      setInputMeter("");
      setOutputKg("");
      setOutputMeter("");
      setSeparatesKg("");
      setSeparatesMeter("");
      setSecondGradeKg("");
      setSecondGradeMeter("");
      setSecondGradePieceCount("");
      setNotes("");
      setFlowError(null);
    },
    onError: (err) => setFlowError(errorMessage(err, t("common.error")))
  });

  const pauseMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.pause(order.id, {
        releaseKg: releaseKg ? Number(releaseKg) : undefined,
        releaseMeter: releaseMeter ? Number(releaseMeter) : undefined,
        reason: pauseReason.trim(),
        // The lot is named only when material actually goes back: the release is read
        // from the ledger per lot, and with several source lots the backend refuses to
        // pick one on its own (no FIFO).
        rawMessageId: releaseKg || releaseMeter ? releaseLotId || null : null
      }),
    onSuccess: () => {
      invalidate();
      setPauseOpen(false);
      setReleaseKg("");
      setReleaseMeter("");
      setReleaseLotId("");
      setPauseReason("");
      setFlowError(null);
    },
    onError: (err) => setFlowError(errorMessage(err, t("common.error")))
  });

  const resumeMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.resume(order.id, {
        rawMessageId: resumeMessageId,
        itemId: resumeItemId,
        quantityKg: resumeQtyKg ? Number(resumeQtyKg) : undefined,
        quantityMeter: resumeQtyMeter ? Number(resumeQtyMeter) : undefined
      }),
    onSuccess: () => {
      invalidate();
      setResumeMessageId("");
      setResumeItemId("");
      setResumeQtyKg("");
      setResumeQtyMeter("");
      setFlowError(null);
    },
    onError: (err) => setFlowError(errorMessage(err, t("common.error")))
  });

  const isPaused = order.status === "Paused";
  // Pause is only offered while the job is genuinely running and has material
  // issued; a draft or finished order has nothing to release.
  const canPause = !isPaused && (order.status === "InProduction" || order.status === "RawAllocated");
  const drawingMutation = useMutation({
    mutationFn: () => ProductionOrdersApi.updateDrawing(current!.id, drawingNumber.trim(), drawingReason.trim()),
    onSuccess: () => { invalidate(); setDrawingNumber(""); setDrawingReason(""); setFlowError(null); },
    onError: (err) => setFlowError(errorMessage(err, t("common.error")))
  });
  // Correcting the drawing number of a run that has ALREADY CLOSED. The figure is documentary (the
  // delivery statement reads it live from the executions), so a typo stays correctable - but only with
  // a reason, which the backend requires and writes to the audit trail with the old value.
  const [drawingFixFor, setDrawingFixFor] = useState<string | null>(null);
  const [drawingFixValue, setDrawingFixValue] = useState("");
  const [drawingFixReason, setDrawingFixReason] = useState("");
  const drawingFixMutation = useMutation({
    mutationFn: (input: { id: string; drawingNumber: string; reason: string }) =>
      ProductionOrdersApi.updateDrawing(input.id, input.drawingNumber, input.reason),
    onSuccess: () => { invalidate(); setDrawingFixFor(null); setDrawingFixValue(""); setDrawingFixReason(""); setFlowError(null); },
    onError: (err) => setFlowError(errorMessage(err, t("common.error")))
  });
  // A printing operation may run MORE THAN ONCE on the same Job Order - one execution per drawing, each
  // with its own drawing number (spec sections 19-22). The operation currently in progress is therefore
  // offered again as "another print run" when it requires a drawing number; every other operation keeps the
  // original rule, so no repeat route is invented anywhere else. The list itself stays DYNAMIC: it is the
  // configured, active operations, minus the one already running (unless it may run again), minus formation.
  const currentStageOption = (stages ?? []).filter(
    (s) => s.isActive && current?.requiresDrawingNumber && s.id === current?.stageDefinitionId
  );
  const candidateStages = [
    ...(stages ?? []).filter((s) => s.isActive && s.id !== current?.stageDefinitionId && !s.isFormationStage),
    ...currentStageOption
  ];

  const stageById = (id: string) => candidateStages.find((s) => s.id === id);
  const nextRequiresDrawingFor = (id: string) => stageById(id)?.requiresDrawingNumber ?? false;
  const existingDrawingFor = (id: string) =>
    order.stageExecutions.find((s) => s.stageDefinitionId === id && s.drawingNumber)?.drawingNumber;

  // The Ready Goods stop is the one operation whose weights are mandatory: it is the LAST operation before
  // the warehouse, and the figures entered for it are what the ready-goods stock is posted from.
  const readyGoodsTarget = candidateStages.some((s) => s.isReadyGoodsStage);
  const num = (v: string) => (v.trim() === "" ? null : Number(v));
  const finalRawKg = num(inputKg);
  const finalRawMeter = num(inputMeter);
  const finalReadyKg = num(outputKg);
  const finalReadyMeter = num(outputMeter);
  const finalSeparatesKg = num(separatesKg);
  // الهالك وزن، فيُحسب بالكيلو فقط: أصل الخام بالكيلو − وزن الجاهز بالكيلو − المنفصلات بالكيلو. The metre
  // figures entered on the same operation stay a LENGTH and are never turned into a "metre waste" -
  // nothing is shown until the two KG figures it needs exist, and a negative result is reported as an
  // error rather than printed as a "waste".
  const wasteKg = finalRawKg != null && finalReadyKg != null ? finalRawKg - finalReadyKg - (finalSeparatesKg ?? 0) : null;
  const finalWasteKg = wasteKg != null && wasteKg >= 0 ? wasteKg : null;
  const finalWasteNegativeKg = wasteKg != null && wasteKg < 0;
  // The final capture is complete when the ready weight AND the original raw weight it is measured against
  // are both recorded - in the unit the operation is actually running in. Separates are deliberately absent.
  const finalCaptureComplete = (finalReadyKg != null || finalReadyMeter != null) && (finalRawKg != null || finalRawMeter != null);
  const isAnotherPrintRunHintShown = candidateStages.some((s) => s.id === current?.stageDefinitionId);

  /** Ticking an operation moves the job there at once - and records production only when asked to. */
  const tickStage = (stageDefinitionId: string) => {
    setFlowError(null);
    const isReady = stageById(stageDefinitionId)?.isReadyGoodsStage ?? false;

    if (isReady && !finalCaptureComplete) {
      setFlowError(t("stage.finalCaptureRequired"));
      return;
    }
    if (isReady && finalWasteNegativeKg) {
      setFlowError(t("stage.wasteNegative"));
      return;
    }
    if (!isReady && recordFigures && !outputKg && !outputMeter) {
      setFlowError(t("stage.outputRequiredForRecording"));
      return;
    }

    transferMutation.mutate({ nextStageDefinitionId: stageDefinitionId, recordFigures: isReady || recordFigures });
  };

  const resumeLines = messages?.find((m) => m.id === resumeMessageId)?.lines ?? [];

  return (
    <Card className="card-pad mt-6">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
        <h2 className="card-title">{t("stage.transfer")}</h2>
        {canPause && !pauseOpen && (
          <Button variant="secondary" size="sm" onClick={() => setPauseOpen(true)}>
            {t("pause.title")}
          </Button>
        )}
      </div>
      <p className="mb-3 text-2xs text-ink-subtle">{t("stage.transferHint")}</p>

      {flowError && <p className="mb-3 text-sm text-danger-ink">{flowError}</p>}

      {isPaused ? (
        <div className="rounded-lg bg-warning-soft p-3">
          <p className="mb-2 text-sm font-medium">{t("resume.title")}</p>
          <p className="mb-3 text-2xs">{t("resume.hint")}</p>

          {order.previousCycleLossPercent != null && (
            <p className="mb-3 text-2xs">
              {t("resume.previousLoss")}: <span className="ltr-nums">{order.previousCycleLossPercent}%</span>
              {" · "}
              {t("resume.expected")}:{" "}
              <span className="ltr-nums">
                {order.expectedOutputKg != null ? `${order.expectedOutputKg} KG` : `${order.expectedOutputMeter ?? 0} Meter`}
              </span>
              {" — "}
              {t("resume.expectedHint")}
            </p>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
            <Field label={t("common.customer", "العميل")}>
              <Select value={resumeMessageId} onChange={(e) => setResumeMessageId(e.target.value)}>
                <option value="">{t("prod.selectMessage", "اختر الرسالة")}</option>
                {(messages ?? []).map((m) => (
                  <option key={m.id} value={m.id}>{m.messageNumber}</option>
                ))}
              </Select>
            </Field>
            <Field label={t("common.item", "الصنف")}>
              <Select value={resumeItemId} onChange={(e) => setResumeItemId(e.target.value)} disabled={!resumeMessageId}>
                <option value="">{t("common.select", "اختر")}</option>
                {resumeLines.map((l) => (
                  <option key={l.id} value={l.itemId}>{l.itemCode}</option>
                ))}
              </Select>
            </Field>
            <Field label={t("resume.newQtyKg")}>
              <Input type="number" min={0} step="0.01" value={resumeQtyKg} onChange={(e) => setResumeQtyKg(e.target.value)} />
            </Field>
            <Field label={t("resume.newQtyMeter")}>
              <Input type="number" min={0} step="0.01" value={resumeQtyMeter} onChange={(e) => setResumeQtyMeter(e.target.value)} />
            </Field>
          </div>
          <div className="mt-3">
            <Button
              disabled={!resumeMessageId || !resumeItemId || (!resumeQtyKg && !resumeQtyMeter) || resumeMutation.isPending}
              onClick={() => resumeMutation.mutate()}
            >
              {t("resume.title")}
            </Button>
          </div>
        </div>
      ) : pauseOpen ? (
        <div className="rounded-lg bg-surface-sunken p-3">
          <p className="mb-3 text-2xs text-ink-subtle">{t("pause.hint")}</p>
          <p className="mb-3 text-2xs">{t("pause.bound")}</p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 items-end">
            <Field label={t("pause.releaseKg")}>
              <Input type="number" min={0} step="0.01" value={releaseKg} onChange={(e) => setReleaseKg(e.target.value)} />
            </Field>
            <Field label={t("pause.releaseMeter")}>
              <Input type="number" min={0} step="0.01" value={releaseMeter} onChange={(e) => setReleaseMeter(e.target.value)} />
            </Field>
          </div>
          {(releaseKg !== "" || releaseMeter !== "") && order.rawAllocations.length > 0 && (
            <Field label={t("pause.releaseLot")}>
              <Select value={releaseLotId} onChange={(e) => setReleaseLotId(e.target.value)}>
                <option value="">{t("common.select")}</option>
                {order.rawAllocations.map((a) => (
                  <option key={a.id} value={a.rawMessageId}>
                    {a.messageNumber}{a.quantityKg != null ? ` - ${a.quantityKg} كجم` : ""}{a.quantityMeter != null ? ` ${a.quantityMeter} م` : ""}
                  </option>
                ))}
              </Select>
            </Field>
          )}
          <Field label={t("pause.reason")}>
            <Input value={pauseReason} onChange={(e) => setPauseReason(e.target.value)} />
          </Field>
          <div className="mt-3 flex gap-2">
            <Button
              disabled={
                (!releaseKg && !releaseMeter) || pauseReason.trim() === "" || pauseMutation.isPending ||
                ((releaseKg !== "" || releaseMeter !== "") && order.rawAllocations.length > 1 && !releaseLotId)
              }
              onClick={() => pauseMutation.mutate()}
            >
              {t("pause.title")}
            </Button>
            <Button variant="secondary" onClick={() => setPauseOpen(false)}>{t("common.close")}</Button>
          </div>
        </div>
      ) : current ? (
        <div>
          <div className="mb-3 grid grid-cols-2 sm:grid-cols-4 gap-3 text-sm">
            <div>
              <div className="text-2xs text-ink-subtle">{t("stage.baseline")}</div>
              <div className="ltr-nums font-medium">
                {current.baselineKg != null ? `${current.baselineKg} KG` : `${current.baselineMeter ?? 0} Meter`}
              </div>
            </div>
            <div>
              <div className="text-2xs text-ink-subtle">{t("stage.lossPercent")}</div>
              <div className="ltr-nums font-medium">
                {current.lossPercentKg != null ? `${current.lossPercentKg}%` : "—"}
              </div>
            </div>
            <div>
              <div className="text-2xs text-ink-subtle">{t("stage.nextStage")}</div>
              <div className="font-medium">{current.stageName}</div>
            </div>
            <div>
              <div className="text-2xs text-ink-subtle">{t("common.status", "الحالة")}</div>
              <Badge tone="yellow">{t("prod.stage.inProgress", "جارية")}</Badge>
            </div>
          </div>

          {order.stageExecutions.filter((s) => s.drawingNumber).map((s) => (
            <div key={s.id} className="mb-2">
              <p className="text-sm">
                {s.stageName} · {t("printing.drawingNumber")}: <b dir="auto">{s.drawingNumber}</b>
                {s.requiresDrawingNumber && s.status === "Completed" && (
                  <button
                    type="button"
                    className="btn-link ms-2"
                    onClick={() => {
                      if (drawingFixFor === s.id) { setDrawingFixFor(null); return; }
                      setDrawingFixFor(s.id);
                      setDrawingFixValue(s.drawingNumber ?? "");
                      setDrawingFixReason("");
                    }}
                  >
                    {t("printing.correct")}
                  </button>
                )}
              </p>
              {drawingFixFor === s.id && (
                <div className="mt-2 grid grid-cols-1 gap-3 rounded-lg border border-warning/30 bg-warning-soft/60 p-3 sm:grid-cols-3">
                  <Field label={t("printing.drawingNumber")}>
                    <Input maxLength={100} value={drawingFixValue} onChange={(e) => setDrawingFixValue(e.target.value)} />
                  </Field>
                  <Field label={t("pause.reason")}>
                    <Input value={drawingFixReason} onChange={(e) => setDrawingFixReason(e.target.value)} />
                  </Field>
                  <div className="flex items-end gap-2">
                    <Button
                      size="sm"
                      disabled={!drawingFixValue.trim() || !drawingFixReason.trim() || drawingFixMutation.isPending}
                      onClick={() => drawingFixMutation.mutate({ id: s.id, drawingNumber: drawingFixValue.trim(), reason: drawingFixReason.trim() })}
                    >
                      {t("printing.correct")}
                    </Button>
                    <p className="kpi-footnote">{t("printing.closedRunHint")}</p>
                  </div>
                </div>
              )}
            </div>
          ))}
          {/* The drawing number a printing operation must name before it can be left - either because the
              operation being CLOSED requires one, or because the operation being moved to does. */}
          {(current.requiresDrawingNumber || candidateStages.some((s) => s.requiresDrawingNumber)) && (
            <div className="mb-3 grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label={t("printing.drawingNumber")}>
                <Input maxLength={100} value={drawingNumber} onChange={(e) => setDrawingNumber(e.target.value)} placeholder={t("printing.required")} />
              </Field>
              {current.requiresDrawingNumber && <>
                <Field label={t("pause.reason")}><Input value={drawingReason} onChange={(e) => setDrawingReason(e.target.value)} /></Field>
                <Button disabled={!drawingNumber.trim() || !drawingReason.trim() || drawingMutation.isPending} onClick={() => drawingMutation.mutate()}>{t("printing.correct")}</Button>
              </>}
            </div>
          )}
          {/* The LAST operation before the Ready Goods warehouse records the final weights: the original
              raw it started from, the ready weight in KG and the final metres, with the separates optional.
              The waste is DERIVED from them - original raw - ready weight - separates - per unit, never
              converted between KG and Meter, and shown here before the move is made. */}
          {readyGoodsTarget && (
            <div className="mb-4 rounded-lg border border-success/30 bg-success-soft/40 p-3">
              <p className="mb-1 text-sm font-medium text-success-ink">{t("stage.finalCapture")}</p>
              <p className="mb-3 text-2xs text-ink-subtle">{t("stage.finalCaptureHint")}</p>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                <Field label={t("stage.originalRawKg")}>
                  <Input type="number" min={0} step="0.01" value={inputKg} onChange={(e) => setInputKg(e.target.value)} />
                </Field>
                <Field label={t("stage.readyWeightKg")}>
                  <Input type="number" min={0} step="0.01" value={outputKg} onChange={(e) => setOutputKg(e.target.value)} />
                </Field>
                <Field label={t("stage.finalMeter")}>
                  <Input type="number" min={0} step="0.01" value={outputMeter} onChange={(e) => setOutputMeter(e.target.value)} />
                </Field>
                {/* Separates are OPTIONAL on purpose: with none, the operator completes the operation
                    without entering them and the waste is simply original raw - ready weight. */}
                <Field label={t("stage.separatesKgOptional")}>
                  <Input type="number" min={0} step="0.01" value={separatesKg} onChange={(e) => setSeparatesKg(e.target.value)} />
                </Field>
                <Field label={t("stage.separatesMeterOptional")}>
                  <Input type="number" min={0} step="0.01" value={separatesMeter} onChange={(e) => setSeparatesMeter(e.target.value)} />
                </Field>
                <div className="rounded-lg bg-surface px-3 py-2">
                  <div className="text-2xs text-ink-muted">{t("stage.waste")}</div>
                  <div className="ltr-nums font-semibold">
                    {/* الوزن فقط: لا يوجد هالك بالمتر */}
                    {finalWasteKg != null ? `${finalWasteKg} ${t("common.kg")}` : "—"}
                  </div>
                </div>
              </div>
              {finalWasteNegativeKg && (
                <p className="mt-2 text-sm text-danger-ink">{t("stage.wasteNegative")}</p>
              )}
            </div>
          )}

          {/* The operations the job can move to - ONE checkbox each, the dynamic stage list unchanged.
              Ticking one MOVES the order there immediately and saves the move: that IS the transfer, and
              there is no separate "end stage" step. By default the move records NO production and posts NO
              stock - a selection is a routing decision, not a weighing - and the server refuses to invent an
              output for it. The single exception is the Ready Goods stop, which demands the final weights
              above because that is what posts the ready-goods stock. */}
          <p className="mb-1 text-2xs text-ink-subtle">{t("stage.chooseNext")}</p>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {candidateStages.map((s) => {
              const isReady = s.isReadyGoodsStage;
              const isCurrentAgain = s.id === current?.stageDefinitionId;
              const needsFinalCapture = isReady && !finalCaptureComplete;
              const blocked =
                transferMutation.isPending ||
                needsFinalCapture ||
                (nextRequiresDrawingFor(s.id) && isCurrentAgain && !drawingNumber.trim()) ||
                (nextRequiresDrawingFor(s.id) && !isCurrentAgain && !drawingNumber.trim() && !existingDrawingFor(s.id));
              return (
                <label
                  key={s.id}
                  className={`flex items-start gap-2 rounded-lg border p-3 text-sm ${
                    needsFinalCapture ? "border-warning/40 bg-warning-soft/40" : "border-line bg-surface"
                  } ${blocked ? "cursor-not-allowed opacity-60" : "cursor-pointer hover:bg-surface-sunken"}`}
                >
                  <input
                    type="checkbox"
                    className="mt-0.5"
                    checked={false}
                    disabled={blocked}
                    onChange={() => tickStage(s.id)}
                  />
                  <span>
                    <span className="font-medium">{s.name}</span>
                    {isReady && <span className="text-ink-subtle"> ({t("stage.readyGoods")})</span>}
                    {isCurrentAgain && <span className="text-ink-subtle"> — {t("stage.anotherPrintRun")}</span>}
                    {isReady && (
                      <span className="block text-2xs text-ink-subtle">
                        {finalCaptureComplete ? t("stage.readyGoodsReady") : t("stage.finalCaptureRequired")}
                      </span>
                    )}
                  </span>
                </label>
              );
            })}
          </div>

          {/* Recording the operation's own figures is an EXPLICIT, off-by-default choice: ticking a stage
              moves the job (and records nothing), while this switch says "and record what this operation
              consumed and produced". The units are validated server-side against the item's own
              configuration and are never converted. */}
          {!readyGoodsTarget && (
            <label className="mt-3 flex items-center gap-2 text-sm text-ink-muted">
              <input type="checkbox" checked={recordFigures} onChange={(e) => setRecordFigures(e.target.checked)} />
              {t("stage.recordFigures")}
            </label>
          )}

          {recordFigures && !readyGoodsTarget && (
            <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <Field label={t("stage.outputKg")}>
                <Input type="number" min={0} step="0.01" value={outputKg} onChange={(e) => setOutputKg(e.target.value)} />
              </Field>
              <Field label={t("stage.outputMeter")}>
                <Input type="number" min={0} step="0.01" value={outputMeter} onChange={(e) => setOutputMeter(e.target.value)} />
              </Field>
              <Field label={t("stage.inputKg")}>
                <Input type="number" min={0} step="0.01" value={inputKg} onChange={(e) => setInputKg(e.target.value)} />
              </Field>
              <Field label={t("stage.inputMeter")}>
                <Input type="number" min={0} step="0.01" value={inputMeter} onChange={(e) => setInputMeter(e.target.value)} />
              </Field>
              <Field label={t("stage.separatesKg")}>
                <Input type="number" min={0} step="0.01" value={separatesKg} onChange={(e) => setSeparatesKg(e.target.value)} />
              </Field>
              <Field label={t("stage.separatesMeter")}>
                <Input type="number" min={0} step="0.01" value={separatesMeter} onChange={(e) => setSeparatesMeter(e.target.value)} />
              </Field>
              <Field label={t("stage.secondGradeKg")}>
                <Input type="number" min={0} step="0.01" value={secondGradeKg} onChange={(e) => setSecondGradeKg(e.target.value)} />
              </Field>
              <Field label={t("stage.secondGradeMeter")}>
                <Input type="number" min={0} step="0.01" value={secondGradeMeter} onChange={(e) => setSecondGradeMeter(e.target.value)} />
              </Field>
              <Field label={t("stage.secondGradePieceCount")}>
                <Input type="number" min={0} step="1" value={secondGradePieceCount} onChange={(e) => setSecondGradePieceCount(e.target.value)} />
              </Field>
            </div>
          )}

          <div className="mt-3">
            <Field label={t("common.notes", "ملاحظات")}>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </Field>
          </div>

          {isAnotherPrintRunHintShown && (
            <p className="mt-2 text-2xs text-brand-700">{t("stage.anotherPrintRunHint")}</p>
          )}
        </div>
      ) : (
        <p className="text-sm text-ink-subtle">{t("stage.locked")}</p>
      )}
    </Card>
  );
}

function CostPanel({
  productionOrderId, productionOrderNumber, isCompleted
}: {
  productionOrderId: string;
  productionOrderNumber: string;
  isCompleted: boolean;
}) {
  const qc = useQueryClient();
  const { t } = useI18n();
  const [showForm, setShowForm] = useState(false);
  const [category, setCategory] = useState<CostCategory>("Labor");
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");
  // Explicit, off-by-default: a cost stays the factory's own unless somebody marks it
  // chargeable to the customer (spec sections 20 + 32). Only then can an invoice bill it.
  const [chargeable, setChargeable] = useState(false);
  const [estimate, setEstimate] = useState("");
  const [approvedCost, setApprovedCost] = useState("");
  const [costError, setCostError] = useState<string | null>(null);

  const { data: cost, isLoading } = useQuery({
    queryKey: ["production-order-cost", productionOrderId],
    queryFn: () => CostAccountingApi.get(productionOrderId)
  });

  const refresh = () => qc.invalidateQueries({ queryKey: ["production-order-cost", productionOrderId] });

  const addMutation = useMutation({
    mutationFn: () => CostAccountingApi.addEntry(productionOrderId, {
      category, amount: Number(amount), entryDate: new Date().toISOString().slice(0, 10),
      description: description || undefined, isChargeableToCustomer: chargeable
    }),
    onSuccess: () => {
      refresh();
      setShowForm(false); setAmount(""); setDescription(""); setChargeable(false);
    }
  });

  // The estimate is advisory and revisable; the approved figure is an explicit
  // sign-off that is never silently replaced by the live actual.
  const saveEstimate = useMutation({
    mutationFn: () => CostingApi.setEstimate(productionOrderId, {
      estimatedCost: estimate === "" ? null : Number(estimate)
    }),
    onSuccess: () => { refresh(); setCostError(null); },
    onError: (err: ApiErrorLike) => setCostError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const approveCost = useMutation({
    mutationFn: () => CostingApi.approve(productionOrderId, { approvedCost: Number(approvedCost) }),
    onSuccess: () => { refresh(); setApprovedCost(""); setCostError(null); },
    onError: (err: ApiErrorLike) => setCostError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  if (isLoading || !cost) return null;

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  return (
    <Card className="card-pad mt-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="card-title">{t("cost.tab")}</h2>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" onClick={() => JobOrderCostingExports.file("excel")(productionOrderId, productionOrderNumber)}>
            {t("export.costing")} {t("common.excel")}
          </Button>
          <Button variant="ghost" onClick={() => JobOrderCostingExports.file("pdf")(productionOrderId, productionOrderNumber)}>
            {t("export.costing")} {t("common.pdf")}
          </Button>
          <Button variant="secondary" onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : "+ " + t("cost.actual")}</Button>
        </div>
      </div>

      {/* Three figures, never conflated: estimate, live actual, approved sign-off. */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-4">
        <div className="bg-surface-sunken rounded-lg p-3">
          <div className="text-xs text-ink-muted">{t("cost.estimated")}</div>
          <div className="ltr-nums text-lg font-semibold">
            {cost.estimatedCost != null ? money(cost.estimatedCost) : "—"}
          </div>
          <div className="flex gap-2 mt-2">
            <Input
              type="number"
              step="0.01"
              min="0"
              placeholder={cost.estimatedCost != null ? String(cost.estimatedCost) : "0.00"}
              value={estimate}
              onChange={(e) => setEstimate(e.target.value)}
            />
            <Button variant="secondary" onClick={() => saveEstimate.mutate()} disabled={saveEstimate.isPending}>
              {t("cost.saveEstimate")}
            </Button>
          </div>
        </div>

        <div className="bg-surface-sunken rounded-lg p-3">
          <div className="text-xs text-ink-muted">{t("cost.actual")}</div>
          <div className="ltr-nums text-lg font-semibold">{money(cost.totalCost)}</div>
          <p className="text-2xs text-ink-subtle mt-2">{t("cost.actualNote")}</p>
        </div>

        <div className="bg-surface-sunken rounded-lg p-3">
          <div className="text-xs text-ink-muted">{t("cost.approved")}</div>
          <div className="ltr-nums text-lg font-semibold">
            {cost.approvedCost != null ? money(cost.approvedCost) : "—"}
          </div>
          {cost.approvedCost != null ? (
            <p className="text-2xs text-ink-subtle mt-1">
              {t("cost.approvedBy")} {cost.costApprovedBy}
              {cost.costApprovedAtUtc && <> · {new Date(cost.costApprovedAtUtc).toLocaleDateString("en-GB")}</>}
            </p>
          ) : (
            <div className="flex gap-2 mt-2">
              <Input
                type="number"
                step="0.01"
                min="0"
                placeholder={money(cost.totalCost)}
                value={approvedCost}
                onChange={(e) => setApprovedCost(e.target.value)}
                disabled={!isCompleted}
              />
              <Button
                variant="secondary"
                disabled={!isCompleted || approvedCost === "" || approveCost.isPending}
                onClick={() => approveCost.mutate()}
                title={isCompleted ? undefined : t("cost.approveOnlyCompleted")}
              >
                {t("cost.approve")}
              </Button>
            </div>
          )}
          {cost.approvedVariance != null && (
            <p className="text-2xs text-ink-muted mt-1">
              {t("cost.variance")}: <span className="ltr-nums">{money(cost.approvedVariance)}</span>
            </p>
          )}
        </div>
      </div>

      <p className="text-2xs text-ink-subtle mb-3">{t("cost.customerOwnedNote")}</p>

      {costError && <p className="form-error mb-3">{costError}</p>}

      {showForm && (
        <div className="grid grid-cols-1 sm:grid-cols-5 gap-3 items-end mb-4 bg-surface-sunken rounded-lg p-3">
          <div>
            <label className="field-label">البند</label>
            <Select value={category} onChange={(e) => setCategory(e.target.value as CostCategory)}>
              {COST_CATEGORIES.map((c) => (
                <option key={c} value={c}>{t(`cost.cat.${c}`)}</option>
              ))}
            </Select>
          </div>
          <div><label className="field-label">المبلغ</label><Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} /></div>
          <div><label className="field-label">بيان</label><Input value={description} onChange={(e) => setDescription(e.target.value)} /></div>
          <label className="flex items-center gap-2 text-sm text-ink-muted" title={t("cost.chargeableNote")}>
            <input type="checkbox" checked={chargeable} onChange={(e) => setChargeable(e.target.checked)} />
            {t("cost.chargeableToCustomer")}
          </label>
          <Button onClick={() => addMutation.mutate()} disabled={!amount || addMutation.isPending}>حفظ</Button>
        </div>
      )}

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
        <div><div className="text-ink-muted text-xs">تكلفة المواد</div><div className="ltr-nums font-medium">{cost.materialCost}</div></div>
        <div><div className="text-ink-muted text-xs">تكلفة التحضير</div><div className="ltr-nums font-medium">{cost.preparationCost}</div></div>
        <div><div className="text-ink-muted text-xs">{t("cost.externalProcessing")}</div><div className="ltr-nums font-medium">{cost.externalProcessingCost}</div></div>
        <div><div className="text-ink-muted text-xs">عمالة</div><div className="ltr-nums font-medium">{cost.laborCost}</div></div>
        <div><div className="text-ink-muted text-xs">كهرباء</div><div className="ltr-nums font-medium">{cost.electricityCost}</div></div>
        <div><div className="text-ink-muted text-xs">وقود</div><div className="ltr-nums font-medium">{cost.fuelCost}</div></div>
        <div><div className="text-ink-muted text-xs">{t("cost.cat.Maintenance")}</div><div className="ltr-nums font-medium">{cost.maintenanceCost}</div></div>
        <div><div className="text-ink-muted text-xs">{t("cost.cat.Transport")}</div><div className="ltr-nums font-medium">{cost.transportCost}</div></div>
        <div><div className="text-ink-muted text-xs">{t("cost.cat.Packaging")}</div><div className="ltr-nums font-medium">{cost.packagingCost}</div></div>
        <div><div className="text-ink-muted text-xs">{t("cost.cat.Repair")}</div><div className="ltr-nums font-medium">{cost.repairCost}</div></div>
        <div><div className="text-ink-muted text-xs">أخرى</div><div className="ltr-nums font-medium">{cost.otherCost}</div></div>
        <div><div className="text-ink-muted text-xs">إجمالي التكلفة</div><div className="ltr-nums font-bold">{cost.totalCost}</div></div>
        {cost.costPerKg != null && <div><div className="text-ink-muted text-xs">التكلفة/كجم</div><div className="ltr-nums font-medium">{cost.costPerKg.toFixed(2)}</div></div>}
        {cost.costPerMeter != null && <div><div className="text-ink-muted text-xs">التكلفة/متر</div><div className="ltr-nums font-medium">{cost.costPerMeter.toFixed(2)}</div></div>}
        <div><div className="text-ink-muted text-xs">قيمة التشغيل</div><div className="ltr-nums font-medium">{cost.processingValue}</div></div>
        <div><div className="text-ink-muted text-xs">الربح</div><div className="ltr-nums font-bold">{cost.profit}</div></div>
        {cost.marginPercent != null && <div><div className="text-ink-muted text-xs">هامش الربح</div><div className="ltr-nums font-medium">{cost.marginPercent}%</div></div>}
      </div>
    </Card>
  );
}

/**
 * Job Order profitability (spec section 37).
 *
 * Revenue is computed ONCE for the order - final actual quantity x the price that
 * was SNAPSHOTTED onto the order - never summed per stage, and never re-read from
 * the price list. That is the point of the snapshot: an order's revenue must not
 * move because somebody changed a price list last week. Cost comes from the same
 * live rollup the costing panel shows, so the two figures are comparable.
 */
function ProfitabilityPanel({ productionOrderId }: { productionOrderId: string }) {
  const { t } = useI18n();
  const qc = useQueryClient();

  const { data: p, isLoading, error } = useQuery({
    queryKey: ["production-order-profitability", productionOrderId],
    queryFn: () => CostAccountingApi.profitability(productionOrderId)
  });

  const { data: stages } = useQuery({
    queryKey: ["production-stages", "all"],
    queryFn: () => ProductionStagesApi.list()
  });

  const [stageId, setStageId] = useState("");
  const [unit, setUnit] = useState<UnitOfMeasure>("KG");
  const [override, setOverride] = useState("");
  const [reason, setReason] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const applyMutation = useMutation({
    mutationFn: () =>
      CostAccountingApi.applyServicePrice(productionOrderId, {
        stageDefinitionId: stageId,
        unit,
        // Omitting the amount is the "resolve from the price list" path. Supplying
        // it is a deliberate override, which the API requires a reason for.
        ...(override.trim() !== ""
          ? { pricePerUnit: Number(override), overrideReason: reason.trim() }
          : {})
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["production-order-profitability", productionOrderId] });
      setOverride("");
      setReason("");
      setFormError(null);
    },
    onError: (err) => setFormError(errorMessage(err, t("common.error")))
  });

  const hasOverride = override.trim() !== "";
  const overrideInvalid =
    hasOverride && (Number.isNaN(Number(override)) || Number(override) < 0 || reason.trim() === "");
  const canApply = stageId !== "" && !overrideInvalid && !applyMutation.isPending;

  const unitLabel = (u: UnitOfMeasure) => (u === "KG" ? t("common.kg", "Kilogram") : t("common.meter", "Meter"));

  const money = (v: number) => v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  const finalQtyLabel =
    p?.finalQuantitySource === "ReadyGoodsTransfer"
      ? t("profit.source.ReadyGoodsTransfer")
      : p?.finalQuantitySource === "LastStageOutput"
        ? t("profit.source.LastStageOutput")
        : t("profit.source.None");

  const stageOptions = (stages ?? []).filter((s) => s.isActive && !s.isReadyGoodsStage);

  return (
    <Card className="card-pad mt-6">
      <div className="flex items-center gap-2 mb-1">
        <TrendingUp size={18} className="text-ink-muted" />
        <h2 className="text-base font-semibold">{t("profit.title", "Profitability")}</h2>
      </div>
      <p className="text-2xs text-ink-subtle mb-4">{t("profit.snapshotNote")}</p>

      {isLoading && <p className="text-sm text-ink-subtle py-4">{t("common.loading")}</p>}

      {!isLoading && error && (
        <p className="text-sm text-danger-ink py-4">{errorMessage(error, t("common.error"))}</p>
      )}

      {p && (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mb-4">
            <KpiCard
              label={t("profit.revenue", "Revenue")}
              value={money(p.revenue)}
              tone="info"
              icon={<Coins size={16} />}
              footnote={
                p.hasServicePrice
                  ? `${t("profit.finalQty", "Final actual quantity")}: ${
                      p.finalQuantityKg !== 0 ? `${p.finalQuantityKg} KG` : `${p.finalQuantityMeter} Meter`
                    } · ${finalQtyLabel}`
                  : undefined
              }
            />
            <KpiCard
              label={t("profit.actualCost", "Actual cost")}
              value={money(p.actualCost)}
              tone="neutral"
              icon={<Receipt size={16} />}
            />
            <KpiCard
              label={t("profit.profit", "Profit")}
              value={money(p.profit)}
              tone={p.profit >= 0 ? "success" : "danger"}
              icon={<TrendingUp size={16} />}
            />
            <KpiCard
              label={t("profit.margin", "Profit margin")}
              value={p.marginPercent != null ? `${p.marginPercent}%` : "—"}
              tone={p.marginPercent != null && p.marginPercent >= 0 ? "success" : "neutral"}
              icon={<Percent size={16} />}
            />
          </div>

          {!p.hasServicePrice && (
            <p className="mb-4 rounded-lg bg-warning-soft px-3 py-2 text-sm text-warning-ink">
              {t("profit.noPrice", "This Job Order has not been priced yet")}
            </p>
          )}

          <div className="table-wrap mb-4">
            <table className="table">
              <thead>
                <tr>
                  <th>{t("pl.stage")}</th>
                  <th>{t("common.unit")}</th>
                  <th>{t("pl.pricePerUnit")}</th>
                  <th>{t("profit.priceFrom", "Price source")}</th>
                  <th>{t("profit.pricedBy", "Priced by")}</th>
                </tr>
              </thead>
              <tbody>
                <TableStateRow
                  colSpan={5}
                  isEmpty={p.servicePrices.length === 0}
                  emptyText={t("profit.noPrice")}
                  loadingText={t("common.loading")}
                />
                {p.servicePrices.map((s) => (
                  <tr key={s.id}>
                    <td>
                      <div className="font-medium">{s.stageName}</div>
                      <div className="text-xs text-ink-subtle">{s.stageCode}</div>
                    </td>
                    <td>{unitLabel(s.unit)}</td>
                    <td className="ltr-nums font-medium">
                      {s.pricePerUnit}
                      {s.previousPricePerUnit != null && (
                        <span className="ms-2 text-xs text-ink-subtle">
                          {t("profit.repriced", "Repriced (was")} {s.previousPricePerUnit})
                        </span>
                      )}
                    </td>
                    <td>
                      {s.isOverride ? (
                        <Badge tone="warning">
                          {t("profit.override", "Manual override")}
                          {s.overrideReason ? `: ${s.overrideReason}` : ""}
                        </Badge>
                      ) : (
                        <Badge tone="info">{s.sourceCustomerName ?? t("pl.generalDefault")}</Badge>
                      )}
                    </td>
                    <td className="text-xs text-ink-muted">
                      {s.pricedBy}
                      <div>{new Date(s.pricedAtUtc).toLocaleDateString()}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {p.legacyProcessingValue !== 0 && (
            <p className="mb-4 text-2xs text-ink-subtle">
              {t("profit.legacyValue", "Legacy processing value")}:{" "}
              <span className="ltr-nums">{money(p.legacyProcessingValue)}</span>
            </p>
          )}

          {/* Price this Job Order: resolved from the list, or a deliberate override. */}
          <div className="rounded-lg bg-surface-sunken p-3">
            <div className="mb-2 text-xs text-ink-muted">{t("profit.applyPrice", "Price this Job Order")}</div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
              <div>
                <label className="field-label">{t("pl.stage")}</label>
                <Select value={stageId} onChange={(e) => setStageId(e.target.value)}>
                  <option value="">{t("profit.stagePrompt", "Pick the stage")}</option>
                  {stageOptions.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="field-label">{t("common.unit")}</label>
                <Select value={unit} onChange={(e) => setUnit(e.target.value as UnitOfMeasure)}>
                  <option value="KG">{unitLabel("KG")}</option>
                  <option value="Meter">{unitLabel("Meter")}</option>
                </Select>
              </div>
              <div>
                <label className="field-label">
                  {t("profit.overridePrice", "Manual price per unit")}
                </label>
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  value={override}
                  onChange={(e) => setOverride(e.target.value)}
                />
              </div>
              <div>
                <label className="field-label">
                  {t("profit.overrideReason", "Override reason")}
                </label>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} disabled={!hasOverride} />
              </div>
            </div>
            <p className="mt-2 text-2xs text-ink-subtle">{t("profit.overrideHint")}</p>
            {hasOverride && reason.trim() === "" && (
              <p className="mt-1 text-2xs text-danger-ink">{t("profit.overrideReason")}</p>
            )}
            {formError && <p className="mt-1 text-sm text-danger-ink">{formError}</p>}
            <div className="mt-3">
              <Button disabled={!canApply} onClick={() => applyMutation.mutate()}>
                <Tag size={16} /> {t("profit.applyPrice")}
              </Button>
            </div>
          </div>
        </>
      )}
    </Card>
  );
}
