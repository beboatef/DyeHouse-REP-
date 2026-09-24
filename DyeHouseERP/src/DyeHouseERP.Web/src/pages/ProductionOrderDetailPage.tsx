import { useState } from "react";
import { useParams, Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, RawMessagesApi, StageExecution, CostAccountingApi, CostCategory, DocumentPdfApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

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
  const [allocKg, setAllocKg] = useState("");
  const [allocMeter, setAllocMeter] = useState("");
  const [allocError, setAllocError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);

  const invalidate = () => qc.invalidateQueries({ queryKey: ["production-orders", id] });

  const allocateMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      ProductionOrdersApi.allocateRaw(id!, {
        rawMessageId: selectedMessageId,
        quantityKg: allocKg ? Number(allocKg) : undefined,
        quantityMeter: allocMeter ? Number(allocMeter) : undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      invalidate();
      setSelectedMessageId(""); setAllocKg(""); setAllocMeter(""); setAllocError(null); setNegativeStockDetail(null);
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

  return (
    <>
      <PageHeader
        title={order.orderNumber}
        subtitle={`${order.customerCode} - ${order.customerName} · ${order.itemCode} - ${order.itemName}${order.color ? " · " + order.color : ""}`}
        action={
          <div className="flex items-center gap-2">
            <Link to={`/print/production-order/${order.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
            <Button variant="ghost" onClick={() => DocumentPdfApi.productionOrder(order.id, order.orderNumber)}>تنزيل PDF</Button>
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
            <Select value={selectedMessageId} onChange={(e) => setSelectedMessageId(e.target.value)}>
              <option value="">اختر رسالة استلام...</option>
              {candidateMessages?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
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

        {selectedMessage && (
          <p className="text-xs text-gray-500 mt-2">
            الرصيد المتاح في هذه الرسالة:{" "}
            {selectedMessage.lines.map((l) => (
              <span key={l.id} className="ltr-nums font-medium">
                {l.remainingKg != null && `${l.remainingKg} كجم `}
                {l.remainingMeter != null && `${l.remainingMeter} م`}
              </span>
            ))}
          </p>
        )}

        <div className="mt-3">
          <Button
            disabled={!selectedMessageId || (!allocKg && !allocMeter) || allocateMutation.isPending}
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

      <CostPanel productionOrderId={order.id} />
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

function CostPanel({ productionOrderId }: { productionOrderId: string }) {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [category, setCategory] = useState<CostCategory>("Labor");
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");

  const { data: cost, isLoading } = useQuery({
    queryKey: ["production-order-cost", productionOrderId],
    queryFn: () => CostAccountingApi.get(productionOrderId)
  });

  const addMutation = useMutation({
    mutationFn: () => CostAccountingApi.addEntry(productionOrderId, {
      category, amount: Number(amount), entryDate: new Date().toISOString().slice(0, 10), description: description || undefined
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["production-order-cost", productionOrderId] });
      setShowForm(false); setAmount(""); setDescription("");
    }
  });

  if (isLoading || !cost) return null;

  return (
    <Card className="p-5 mt-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="font-semibold text-gray-800">التكلفة</h2>
        <Button variant="secondary" onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تكلفة تشغيلية"}</Button>
      </div>

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
