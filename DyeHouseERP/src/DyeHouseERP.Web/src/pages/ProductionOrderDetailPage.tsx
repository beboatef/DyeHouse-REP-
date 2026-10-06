import { useState } from "react";
import { useParams, Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, CostAccountingApi, CostCategory, ProductionStagesApi, type ProductionOrder, type UnitOfMeasure } from "@/api/client";

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

  const { data: candidateMessages } = useQuery({
    queryKey: ["raw-messages", "for-allocation", order?.customerId],
    queryFn: () => RawMessagesApi.list({ customerId: order!.customerId, onlyWithBalance: true }),
    enabled: !!order?.customerId
  });

  const [selectedMessageId, setSelectedMessageId] = useState("");
  const [selectedLineItemId, setSelectedLineItemId] = useState("");
  const [allocKg, setAllocKg] = useState("");
  const [allocMeter, setAllocMeter] = useState("");
  const [allocError, setAllocError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);

  const invalidate = () => qc.invalidateQueries({ queryKey: ["production-orders", id] });

  const allocateMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      ProductionOrdersApi.allocateRaw(id!, {
        rawMessageId: selectedMessageId,
        itemId: selectedLineItemId,
        quantityKg: allocKg ? Number(allocKg) : undefined,
        quantityMeter: allocMeter ? Number(allocMeter) : undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      invalidate();
      setSelectedMessageId(""); setSelectedLineItemId(""); setAllocKg(""); setAllocMeter(""); setAllocError(null); setNegativeStockDetail(null);
    },
    onError: (err: any) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") {
        setNegativeStockDetail({ shortage: err.response.data.shortage, reason: "" });
      } else {
        setAllocError(err?.response?.data?.title ?? "حدث خطأ أثناء التخصيص");
      }
    }
  });

  if (isLoading || !order) return <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>;

  const selectedMessage = candidateMessages?.find((m) => m.id === selectedMessageId);
  const selectedLine = selectedMessage?.lines.find((l) => l.itemId === selectedLineItemId);

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

      {/* Origin of this order in the formation plan (spec sections 10-11, 31): request -> group -> basin. */}
      {order.formationRequestNumber && (
        <Card className="p-4 mb-6 flex flex-wrap items-center gap-2 text-sm">
          <span className="text-ink-subtle text-xs">{t("fr.title")}</span>
          <Link
            to={`/formation-requests/${order.formationRequestId}`}
            className="ltr-nums font-medium text-brand-600 hover:underline"
          >
            {order.formationRequestNumber}
          </Link>
          {order.formationGroupNumber != null && (
            <span className="text-ink-subtle">
              · {t("fr.group")} #{order.formationGroupNumber}
            </span>
          )}
          {order.formationBasinNumber != null && (
            <span className="text-ink-subtle">
              · {t("fr.basin")} #{order.formationBasinNumber}
            </span>
          )}
        </Card>
      )}

      {/* Raw allocation - manual message picker, NO FIFO */}
      <Card className="p-5 mb-6">
        <h2 className="font-semibold text-gray-800 mb-1">تخصيص الخام</h2>
        <p className="text-xs text-gray-500 mb-4">
          يتم اختيار الرسالة (المرسال) يدويًا من قبل المستخدم - لا يوجد اختيار تلقائي (لا FIFO). يمكن التخصيص من أكثر من رسالة.
        </p>

        {order.rawAllocations.length > 0 && (
          <table className="table mb-4">
            <thead>
              <tr>
                <th>رقم الرسالة</th>
                <th>الكمية المخصصة</th>
                <th>بواسطة</th>
              </tr>
            </thead>
            <tbody>
              {order.rawAllocations.map((a) => (
                <tr key={a.id} className="border-b border-gray-50 last:border-0">
                  <td className="ltr-nums font-medium">{a.messageNumber}</td>
                  <td className="ltr-nums">
                    {a.quantityKg != null && `${a.quantityKg} كجم`}
                    {a.quantityKg != null && a.quantityMeter != null && " / "}
                    {a.quantityMeter != null && `${a.quantityMeter} م`}
                  </td>
                  <td className="text-gray-500">{a.allocatedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end bg-gray-50 rounded-lg p-3">
          <div className="sm:col-span-2">
            <label className="field-label">اختر الرسالة</label>
            <Select
              value={selectedMessageId}
              onChange={(e) => {
                setSelectedMessageId(e.target.value);
                setSelectedLineItemId("");
              }}
            >
              <option value="">اختر رسالة استلام...</option>
              {candidateMessages?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
                </option>
              ))}
            </Select>
          </div>
          <div className="sm:col-span-2">
            <label className="field-label">اختر الصنف (سطر الرسالة)</label>
            <Select value={selectedLineItemId} onChange={(e) => setSelectedLineItemId(e.target.value)} disabled={!selectedMessage}>
              <option value="">اختر الصنف...</option>
              {selectedMessage?.lines.map((l) => (
                <option key={l.id} value={l.itemId}>
                  {l.itemCode} - {l.itemName} ({l.remainingKg != null ? `${l.remainingKg} كجم` : `${l.remainingMeter} م`})
                </option>
              ))}
            </Select>
          </div>
          <div>
            <label className="field-label">كمية (كجم)</label>
            <Input type="number" step="0.001" min="0" value={allocKg} onChange={(e) => setAllocKg(e.target.value)} />
          </div>
          <div>
            <label className="field-label">كمية (متر)</label>
            <Input type="number" step="0.001" min="0" value={allocMeter} onChange={(e) => setAllocMeter(e.target.value)} />
          </div>
        </div>

        {selectedLine && (
          <p className="text-xs text-gray-500 mt-2">
            الرصيد المتاح للصنف المحدد في هذه الرسالة:{" "}
            <span className="ltr-nums font-medium">
              {selectedLine.remainingKg != null && `${selectedLine.remainingKg} كجم `}
              {selectedLine.remainingMeter != null && `${selectedLine.remainingMeter} م`}
            </span>
          </p>
        )}

        <div className="mt-3">
          <Button
            disabled={!selectedMessageId || !selectedLineItemId || (!allocKg && !allocMeter) || allocateMutation.isPending}
            onClick={() => allocateMutation.mutate(false)}
          >
            تخصيص
          </Button>
        </div>

        {allocError && <p className="form-error mt-2">{allocError}</p>}

        {negativeStockDetail && (
          <Card className="p-4 mt-3 border-yellow-300 bg-yellow-50">
            <p className="text-sm text-yellow-800">
              الكمية المطلوبة تتجاوز الرصيد المتاح بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
              هذا يتطلب صلاحية تجاوز الرصيد السالب (inventory.allow_negative_stock) وسببًا موثقًا.
            </p>
            <div className="flex gap-2 mt-2">
              <Input
                placeholder="سبب التجاوز..."
                value={negativeStockDetail.reason}
                onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })}
              />
              <Button
                variant="secondary"
                disabled={!negativeStockDetail.reason}
                onClick={() => allocateMutation.mutate(true)}
              >
                تجاوز واعتماد
              </Button>
            </div>
          </Card>
        )}
      </Card>

      {/* Stage timeline */}
      <Card className="p-5">
        <h2 className="font-semibold text-gray-800 mb-1">مراحل التشغيل</h2>
        <p className="mb-4 text-2xs text-ink-subtle">{t("stage.transferHint")}</p>
        <div className="space-y-3">
          {order.stageExecutions.map((s) => (
            <StageRow key={s.id} stage={s} />
          ))}
        </div>
      </Card>

      {/* Stage transfer (spec sections 13-17) and Pause/Resume (spec section 19). */}
      <StageFlowPanel order={order} />

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

  const [nextStageId, setNextStageId] = useState("");
  const [outputKg, setOutputKg] = useState("");
  const [outputMeter, setOutputMeter] = useState("");
  const [notes, setNotes] = useState("");
  const [flowError, setFlowError] = useState<string | null>(null);

  const [pauseOpen, setPauseOpen] = useState(false);
  const [releaseKg, setReleaseKg] = useState("");
  const [releaseMeter, setReleaseMeter] = useState("");
  const [pauseReason, setPauseReason] = useState("");
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

  const invalidate = () => qc.invalidateQueries({ queryKey: ["production-orders", order.id] });

  const current = order.stageExecutions.find((s) => s.status === "InProgress") ?? null;

  const transferMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.transferStage(current!.id, {
        nextStageDefinitionId: nextStageId,
        outputKg: outputKg ? Number(outputKg) : undefined,
        outputMeter: outputMeter ? Number(outputMeter) : undefined,
        notes: notes.trim() || undefined
      }),
    onSuccess: () => {
      invalidate();
      setNextStageId("");
      setOutputKg("");
      setOutputMeter("");
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
        reason: pauseReason.trim()
      }),
    onSuccess: () => {
      invalidate();
      setPauseOpen(false);
      setReleaseKg("");
      setReleaseMeter("");
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
  const nextIsReadyGoods = stages?.find((s) => s.id === nextStageId)?.isReadyGoodsStage ?? false;
  const candidateStages = (stages ?? []).filter(
    (s) => s.isActive && s.id !== current?.stageDefinitionId && !s.isFormationStage
  );
  const resumeLines = messages?.find((m) => m.id === resumeMessageId)?.lines ?? [];

  return (
    <Card className="p-5 mt-6">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
        <h2 className="font-semibold text-gray-800">{t("stage.transfer")}</h2>
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
          <Field label={t("pause.reason")}>
            <Input value={pauseReason} onChange={(e) => setPauseReason(e.target.value)} />
          </Field>
          <div className="mt-3 flex gap-2">
            <Button
              disabled={(!releaseKg && !releaseMeter) || pauseReason.trim() === "" || pauseMutation.isPending}
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

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
            <Field label={t("stage.nextStage")}>
              <Select value={nextStageId} onChange={(e) => setNextStageId(e.target.value)}>
                <option value="">{t("stage.selectNext")}</option>
                {candidateStages.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}{s.isReadyGoodsStage ? " (الجاهز)" : ""}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label={t("stage.outputKg")}>
              <Input type="number" min={0} step="0.01" value={outputKg} onChange={(e) => setOutputKg(e.target.value)} />
            </Field>
            <Field label={t("stage.outputMeter")}>
              <Input type="number" min={0} step="0.01" value={outputMeter} onChange={(e) => setOutputMeter(e.target.value)} />
            </Field>
            <Field label={t("common.notes", "ملاحظات")}>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </Field>
          </div>

          {nextIsReadyGoods && (
            <p className="mt-2 text-2xs text-success-ink">{t("stage.readyGoodsHint")}</p>
          )}

          <div className="mt-3">
            <Button
              disabled={!nextStageId || (!outputKg && !outputMeter) || transferMutation.isPending}
              onClick={() => transferMutation.mutate()}
            >
              {t("stage.transfer")}
            </Button>
          </div>
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
      category, amount: Number(amount), entryDate: new Date().toISOString().slice(0, 10), description: description || undefined
    }),
    onSuccess: () => {
      refresh();
      setShowForm(false); setAmount(""); setDescription("");
    }
  });

  // The estimate is advisory and revisable; the approved figure is an explicit
  // sign-off that is never silently replaced by the live actual.
  const saveEstimate = useMutation({
    mutationFn: () => CostingApi.setEstimate(productionOrderId, {
      estimatedCost: estimate === "" ? null : Number(estimate)
    }),
    onSuccess: () => { refresh(); setCostError(null); },
    onError: (err: any) => setCostError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const approveCost = useMutation({
    mutationFn: () => CostingApi.approve(productionOrderId, { approvedCost: Number(approvedCost) }),
    onSuccess: () => { refresh(); setApprovedCost(""); setCostError(null); },
    onError: (err: any) => setCostError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  if (isLoading || !cost) return null;

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  return (
    <Card className="p-5 mt-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="font-semibold text-gray-800">{t("cost.tab")}</h2>
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
        <div className="bg-gray-50 rounded-lg p-3">
          <div className="text-xs text-gray-500">{t("cost.estimated")}</div>
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

        <div className="bg-gray-50 rounded-lg p-3">
          <div className="text-xs text-gray-500">{t("cost.actual")}</div>
          <div className="ltr-nums text-lg font-semibold">{money(cost.totalCost)}</div>
          <p className="text-2xs text-gray-400 mt-2">{t("cost.actualNote")}</p>
        </div>

        <div className="bg-gray-50 rounded-lg p-3">
          <div className="text-xs text-gray-500">{t("cost.approved")}</div>
          <div className="ltr-nums text-lg font-semibold">
            {cost.approvedCost != null ? money(cost.approvedCost) : "—"}
          </div>
          {cost.approvedCost != null ? (
            <p className="text-2xs text-gray-400 mt-1">
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
            <p className="text-2xs text-gray-500 mt-1">
              {t("cost.variance")}: <span className="ltr-nums">{money(cost.approvedVariance)}</span>
            </p>
          )}
        </div>
      </div>

      <p className="text-2xs text-gray-400 mb-3">{t("cost.customerOwnedNote")}</p>

      {costError && <p className="form-error mb-3">{costError}</p>}

      {showForm && (
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end mb-4 bg-gray-50 rounded-lg p-3">
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
          <Button onClick={() => addMutation.mutate()} disabled={!amount || addMutation.isPending}>حفظ</Button>
        </div>
      )}

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
        <div><div className="text-gray-500 text-xs">تكلفة المواد</div><div className="ltr-nums font-medium">{cost.materialCost}</div></div>
        <div><div className="text-gray-500 text-xs">تكلفة التحضير</div><div className="ltr-nums font-medium">{cost.preparationCost}</div></div>
        <div><div className="text-gray-500 text-xs">{t("cost.externalProcessing")}</div><div className="ltr-nums font-medium">{cost.externalProcessingCost}</div></div>
        <div><div className="text-gray-500 text-xs">عمالة</div><div className="ltr-nums font-medium">{cost.laborCost}</div></div>
        <div><div className="text-gray-500 text-xs">كهرباء</div><div className="ltr-nums font-medium">{cost.electricityCost}</div></div>
        <div><div className="text-gray-500 text-xs">وقود</div><div className="ltr-nums font-medium">{cost.fuelCost}</div></div>
        <div><div className="text-gray-500 text-xs">{t("cost.cat.Maintenance")}</div><div className="ltr-nums font-medium">{cost.maintenanceCost}</div></div>
        <div><div className="text-gray-500 text-xs">{t("cost.cat.Transport")}</div><div className="ltr-nums font-medium">{cost.transportCost}</div></div>
        <div><div className="text-gray-500 text-xs">{t("cost.cat.Packaging")}</div><div className="ltr-nums font-medium">{cost.packagingCost}</div></div>
        <div><div className="text-gray-500 text-xs">{t("cost.cat.Repair")}</div><div className="ltr-nums font-medium">{cost.repairCost}</div></div>
        <div><div className="text-gray-500 text-xs">أخرى</div><div className="ltr-nums font-medium">{cost.otherCost}</div></div>
        <div><div className="text-gray-500 text-xs">إجمالي التكلفة</div><div className="ltr-nums font-bold">{cost.totalCost}</div></div>
        {cost.costPerKg != null && <div><div className="text-gray-500 text-xs">التكلفة/كجم</div><div className="ltr-nums font-medium">{cost.costPerKg.toFixed(2)}</div></div>}
        {cost.costPerMeter != null && <div><div className="text-gray-500 text-xs">التكلفة/متر</div><div className="ltr-nums font-medium">{cost.costPerMeter.toFixed(2)}</div></div>}
        <div><div className="text-gray-500 text-xs">قيمة التشغيل</div><div className="ltr-nums font-medium">{cost.processingValue}</div></div>
        <div><div className="text-gray-500 text-xs">الربح</div><div className="ltr-nums font-bold">{cost.profit}</div></div>
        {cost.marginPercent != null && <div><div className="text-gray-500 text-xs">هامش الربح</div><div className="ltr-nums font-medium">{cost.marginPercent}%</div></div>}
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

  const stageOptions = (stages ?? []).filter((s) => s.isActive);

  return (
    <Card className="p-5 mt-6">
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
