import { useState } from "react";
import { useParams, Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, StageExecution, CostAccountingApi, CostCategory } from "@/api/client";
import { CostingApi } from "@/api/documents";
import { JobOrderCostingExports, ProductionOrdersExports } from "@/api/exports";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import AttachmentsPanel from "@/components/AttachmentsPanel";
import { useI18n } from "@/i18n";

const statusLabel: Record<string, string> = {
  Draft: "مسودة", RawAllocated: "تم تخصيص الخام", InProduction: "قيد التشغيل", Completed: "مكتمل", Cancelled: "ملغي"
};
const stageStatusLabel: Record<string, string> = {
  Pending: "بالانتظار", InProgress: "جارية", Completed: "مكتملة", Skipped: "تم تخطيها"
};
const stageStatusTone: Record<string, "gray" | "yellow" | "green" | "blue"> = {
  Pending: "gray", InProgress: "yellow", Completed: "green", Skipped: "blue"
};

export default function ProductionOrderDetailPage() {
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

  const startStage = useMutation({ mutationFn: ProductionOrdersApi.startStage, onSuccess: invalidate });
  const skipStage = useMutation({
    mutationFn: (vars: { stageExecutionId: string; reason: string }) => ProductionOrdersApi.skipStage(vars.stageExecutionId, vars.reason),
    onSuccess: invalidate
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

      {/* Raw allocation - manual message picker, NO FIFO */}
      <Card className="p-5 mb-6">
        <h2 className="font-semibold text-gray-800 mb-1">تخصيص الخام</h2>
        <p className="text-xs text-gray-500 mb-4">
          يتم اختيار الرسالة (المرسال) يدويًا من قبل المستخدم - لا يوجد اختيار تلقائي (لا FIFO). يمكن التخصيص من أكثر من رسالة.
        </p>

        {order.rawAllocations.length > 0 && (
          <table className="w-full text-sm mb-4">
            <thead>
              <tr className="text-gray-400 text-xs border-b border-gray-100">
                <th className="text-start py-2 font-medium">رقم الرسالة</th>
                <th className="text-start py-2 font-medium">الكمية المخصصة</th>
                <th className="text-start py-2 font-medium">بواسطة</th>
              </tr>
            </thead>
            <tbody>
              {order.rawAllocations.map((a) => (
                <tr key={a.id} className="border-b border-gray-50 last:border-0">
                  <td className="py-2 ltr-nums font-medium">{a.messageNumber}</td>
                  <td className="py-2 ltr-nums">
                    {a.quantityKg != null && `${a.quantityKg} كجم`}
                    {a.quantityKg != null && a.quantityMeter != null && " / "}
                    {a.quantityMeter != null && `${a.quantityMeter} م`}
                  </td>
                  <td className="py-2 text-gray-500">{a.allocatedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end bg-gray-50 rounded-lg p-3">
          <div className="sm:col-span-2">
            <label className="block text-[11px] text-gray-500 mb-1">اختر الرسالة</label>
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
            <label className="block text-[11px] text-gray-500 mb-1">اختر الصنف (سطر الرسالة)</label>
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
            <label className="block text-[11px] text-gray-500 mb-1">كمية (كجم)</label>
            <Input type="number" step="0.001" min="0" value={allocKg} onChange={(e) => setAllocKg(e.target.value)} />
          </div>
          <div>
            <label className="block text-[11px] text-gray-500 mb-1">كمية (متر)</label>
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

        {allocError && <p className="text-sm text-red-600 mt-2">{allocError}</p>}

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
        <h2 className="font-semibold text-gray-800 mb-4">مراحل التشغيل</h2>
        <div className="space-y-3">
          {order.stageExecutions.map((s) => (
            <StageRow
              key={s.id}
              stage={s}
              onStart={() => startStage.mutate(s.id)}
              onSkip={(reason) => skipStage.mutate({ stageExecutionId: s.id, reason })}
              onCompleted={invalidate}
            />
          ))}
        </div>
      </Card>

      {/* Job order costing tab (spec section 34): estimate, live actual, approved. */}
      <CostPanel
        productionOrderId={order.id}
        productionOrderNumber={order.orderNumber}
        isCompleted={order.status === "Completed"}
      />

      {/* Private attachments for this job order (spec section 47). */}
      <div className="mt-6">
        <AttachmentsPanel entityType="ProductionOrder" entityId={order.id} />
      </div>
    </>
  );
}

function StageRow({
  stage, onStart, onSkip, onCompleted
}: {
  stage: StageExecution;
  onStart: () => void;
  onSkip: (reason: string) => void;
  onCompleted: () => void;
}) {
  const [showComplete, setShowComplete] = useState(false);
  const [inputKg, setInputKg] = useState("");
  const [outputKg, setOutputKg] = useState("");
  const [lossKg, setLossKg] = useState("");
  const [separatesKg, setSeparatesKg] = useState("");
  const [approvedBy, setApprovedBy] = useState("");

  const completeMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.completeStage(stage.id, {
        inputKg: inputKg ? Number(inputKg) : undefined,
        outputKg: outputKg ? Number(outputKg) : undefined,
        lossKg: lossKg ? Number(lossKg) : undefined,
        separatesKg: separatesKg ? Number(separatesKg) : undefined,
        approvedBy: approvedBy || undefined
      }),
    onSuccess: () => {
      onCompleted();
      setShowComplete(false);
    }
  });

  return (
    <div className="border border-gray-100 rounded-lg p-3">
      <div className="flex items-center justify-between">
        <div>
          <span className="text-xs text-gray-400 ltr-nums me-2">#{stage.sequence}</span>
          <span className="font-medium">{stage.stageName}</span>
        </div>
        <Badge tone={stageStatusTone[stage.status]}>{stageStatusLabel[stage.status]}</Badge>
      </div>

      {stage.status === "Completed" && (
        <div className="text-xs text-gray-500 mt-2 ltr-nums">
          {stage.inputKg != null && `دخول: ${stage.inputKg} كجم `}
          {stage.outputKg != null && `· خروج: ${stage.outputKg} كجم `}
          {stage.lossKg != null && `· فاقد: ${stage.lossKg} كجم `}
          {stage.separatesKg != null && `· منفصلات: ${stage.separatesKg} كجم`}
        </div>
      )}

      {stage.status === "Pending" && (
        <div className="flex gap-2 mt-2">
          <Button variant="secondary" onClick={onStart}>بدء المرحلة</Button>
          {stage.allowSkip && (
            <Button variant="ghost" onClick={() => onSkip("تخطي بواسطة المستخدم")}>تخطي</Button>
          )}
        </div>
      )}

      {stage.status === "InProgress" && !showComplete && (
        <div className="mt-2">
          <Button variant="secondary" onClick={() => setShowComplete(true)}>إكمال المرحلة</Button>
        </div>
      )}

      {stage.status === "InProgress" && showComplete && (
        <div className="grid grid-cols-2 sm:grid-cols-5 gap-2 mt-3 items-end">
          <div><label className="block text-[11px] text-gray-500 mb-1">دخول (كجم)</label><Input type="number" value={inputKg} onChange={(e) => setInputKg(e.target.value)} /></div>
          <div><label className="block text-[11px] text-gray-500 mb-1">خروج (كجم)</label><Input type="number" value={outputKg} onChange={(e) => setOutputKg(e.target.value)} /></div>
          <div><label className="block text-[11px] text-gray-500 mb-1">فاقد (كجم)</label><Input type="number" value={lossKg} onChange={(e) => setLossKg(e.target.value)} /></div>
          <div><label className="block text-[11px] text-gray-500 mb-1">منفصلات (كجم)</label><Input type="number" value={separatesKg} onChange={(e) => setSeparatesKg(e.target.value)} /></div>
          {stage.requiresApproval && (
            <div><label className="block text-[11px] text-gray-500 mb-1">معتمد بواسطة</label><Input value={approvedBy} onChange={(e) => setApprovedBy(e.target.value)} /></div>
          )}
          <Button onClick={() => completeMutation.mutate()} disabled={completeMutation.isPending}>حفظ</Button>
        </div>
      )}
    </div>
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
          <p className="text-[11px] text-gray-400 mt-2">{t("cost.actualNote")}</p>
        </div>

        <div className="bg-gray-50 rounded-lg p-3">
          <div className="text-xs text-gray-500">{t("cost.approved")}</div>
          <div className="ltr-nums text-lg font-semibold">
            {cost.approvedCost != null ? money(cost.approvedCost) : "—"}
          </div>
          {cost.approvedCost != null ? (
            <p className="text-[11px] text-gray-400 mt-1">
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
            <p className="text-[11px] text-gray-500 mt-1">
              {t("cost.variance")}: <span className="ltr-nums">{money(cost.approvedVariance)}</span>
            </p>
          )}
        </div>
      </div>

      <p className="text-[11px] text-gray-400 mb-3">{t("cost.customerOwnedNote")}</p>

      {costError && <p className="text-sm text-red-600 mb-3">{costError}</p>}

      {showForm && (
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end mb-4 bg-gray-50 rounded-lg p-3">
          <div>
            <label className="block text-[11px] text-gray-500 mb-1">البند</label>
            <Select value={category} onChange={(e) => setCategory(e.target.value as CostCategory)}>
              <option value="Labor">عمالة</option><option value="Electricity">كهرباء</option>
              <option value="Fuel">وقود</option><option value="Maintenance">صيانة</option><option value="Other">أخرى</option>
            </Select>
          </div>
          <div><label className="block text-[11px] text-gray-500 mb-1">المبلغ</label><Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} /></div>
          <div><label className="block text-[11px] text-gray-500 mb-1">بيان</label><Input value={description} onChange={(e) => setDescription(e.target.value)} /></div>
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
        <div><div className="text-gray-500 text-xs">صيانة</div><div className="ltr-nums font-medium">{cost.maintenanceCost}</div></div>
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
