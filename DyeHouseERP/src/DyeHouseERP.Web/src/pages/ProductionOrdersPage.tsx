import { useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ApprovalsApi, CustomersApi, FormationRequestsApi, ItemsApi, JobOrderType,
  ProductionOrdersApi, ProductionPriority, RawMessagesApi
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge, SearchInput } from "@/components/ui";
import { ExportButtons, DocumentActions } from "@/components/ExportButtons";
import { ProductionOrdersExports } from "@/api/exports";
import { useI18n } from "@/i18n";

const statusTone: Record<string, "gray" | "blue" | "yellow" | "green" | "red"> = {
  // "Paused" (موقوف مؤقتًا) is deliberately amber rather than red: the order is
  // still alive and resumable, which is exactly how it differs from a cancelled one.
  Draft: "gray", RawAllocated: "blue", InProduction: "yellow", Paused: "yellow", Completed: "green", Cancelled: "red"
};
const priorityLabel: Record<string, string> = { Low: "منخفضة", Normal: "عادية", High: "عالية", Urgent: "عاجلة" };

/**
 * Job orders (أوامر التشغيل).
 *
 * Creating an order IS issuing raw material (spec section 5): the screen collects the
 * ACTUAL issued quantities and calls the one atomic endpoint that posts the inventory OUT,
 * creates the order and links them. There is no separate "create order then allocate" step,
 * which is what used to make a second stock OUT possible.
 *
 * The Formation Request is OPTIONAL: pick one to record which plan the issue came from
 * (and optionally a basin), or leave it empty for a direct issue. Planned quantities are
 * shown for reference only - the actual issued figures always win and are never enforced
 * against the plan.
 */
export default function ProductionOrdersPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  // A Formation Request can hand its customer/item/request references over to this screen
  // (Workflow 1). Nothing is issued by that navigation - the ACTUAL quantities are still
  // entered and confirmed here, and the warehouse may instead issue directly.
  const [searchParams] = useSearchParams();
  const prefillCustomerId = searchParams.get("customerId") ?? "";
  const prefillItemId = searchParams.get("itemId") ?? "";
  const prefillFormationRequestId = searchParams.get("formationRequestId") ?? "";
  const [showForm, setShowForm] = useState(Boolean(prefillCustomerId || prefillFormationRequestId));
  const [jobOrderType, setJobOrderType] = useState<JobOrderType>("ClosedLine");
  const [typeFilter, setTypeFilter] = useState<JobOrderType | "">("");
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [search, setSearch] = useState("");
  const [customerId, setCustomerId] = useState(prefillCustomerId);
  const [itemId, setItemId] = useState(prefillItemId);
  const [rawMessageId, setRawMessageId] = useState("");
  const [formationRequestId, setFormationRequestId] = useState(prefillFormationRequestId);
  const [formationBasinId, setFormationBasinId] = useState("");
  const [color, setColor] = useState("");
  const [orderDate, setOrderDate] = useState(new Date().toISOString().slice(0, 10));
  const [actualKg, setActualKg] = useState("");
  const [actualMeter, setActualMeter] = useState("");
  const [priority, setPriority] = useState<ProductionPriority>("Normal");
  const [customerReference, setCustomerReference] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<{ id: string; orderNumber: string } | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);
  const [approvalRequested, setApprovalRequested] = useState<string | null>(null);

  // One idempotency token per confirmation. Re-sending the same confirmation (a double
  // click, a retry) returns the SAME order instead of issuing the material twice.
  const requestId = useRef<string>(crypto.randomUUID());

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: orders, isLoading } = useQuery({
    queryKey: ["production-orders", typeFilter],
    queryFn: () => ProductionOrdersApi.list({ jobOrderType: typeFilter || undefined })
  });

  const visibleOrders = useMemo(() => {
    const term = search.trim().toLocaleLowerCase();
    return (orders ?? []).filter((order) => {
      const matchesStatus = !statusFilter || order.status === statusFilter;
      const matchesSearch = !term || [
        order.orderNumber, order.customerCode, order.customerName, order.itemCode,
        order.itemName, order.color, order.customerReference
      ].filter(Boolean).join(" ").toLocaleLowerCase().includes(term);
      return matchesStatus && matchesSearch;
    });
  }, [orders, search, statusFilter]);

  // Lots of the chosen customer/item that still hold a balance - the material actually
  // available to issue from.
  const { data: lots } = useQuery({
    queryKey: ["raw-messages", "issuable", customerId, itemId],
    queryFn: () => RawMessagesApi.list({ customerId, itemId: itemId || undefined, onlyWithBalance: true }),
    enabled: !!customerId
  });

  // Optional planning documents for the same customer/item.
  const { data: formationRequests } = useQuery({
    queryKey: ["formation-requests", "issuable", customerId, itemId],
    queryFn: () => FormationRequestsApi.list({ customerId, itemId: itemId || undefined }),
    enabled: !!customerId && !!itemId
  });

  const selectedRequest = useMemo(
    () => (formationRequests ?? []).find((r) => r.id === formationRequestId),
    [formationRequests, formationRequestId]
  );

  const selectedItem = useMemo(() => (items ?? []).find((i) => i.id === itemId), [items, itemId]);
  const unitLabel = (u: string | null | undefined) => (u === "Meter" ? t("common.meter") : t("common.kg"));

  const issueMutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.issue({
        customerId,
        itemId,
        rawMessageId,
        orderDate,
        actualQuantityKg: actualKg ? Number(actualKg) : undefined,
        actualQuantityMeter: actualMeter ? Number(actualMeter) : undefined,
        color: color || undefined,
        priority,
        jobOrderType,
        customerReference: customerReference || undefined,
        notes: notes || undefined,
        formationRequestId: formationRequestId || null,
        formationBasinId: formationBasinId || null,
        requestId: requestId.current
      }),
    onSuccess: (order) => {
      qc.invalidateQueries({ queryKey: ["production-orders"] });
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      qc.invalidateQueries({ queryKey: ["production-floor"] });
      // Show the created order (number + link) - the issue produced it.
      setCreated({ id: order.id, orderNumber: order.orderNumber });
      setColor(""); setActualKg(""); setActualMeter(""); setCustomerReference(""); setNotes("");
      setFormationRequestId(""); setFormationBasinId("");
      requestId.current = crypto.randomUUID();
      setError(null);
      setNegativeStockDetail(null);
      setApprovalRequested(null);
    },
    onError: (err: ApiErrorLike) => {
      // A shortage is NOT overridden by the person doing the issue (requester ≠ approver):
      // the screen offers to send the exact same issue to the Approval Center instead, and a
      // DIFFERENT user with inventory.approve_negative_stock decides it there.
      if (err?.response?.data?.code === "NEGATIVE_STOCK") {
        setNegativeStockDetail({ shortage: err.response?.data?.shortage ?? 0, reason: "" });
        setError(null);
      } else {
        setNegativeStockDetail(null);
        setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "تعذر تنفيذ الإذن");
      }
    }
  });

  /**
   * Raises the separation-of-duties request for the SAME issue: the backend stores this exact
   * command, and approving it posts the very same inventory OUT + Job Order through the one
   * canonical handler - bound to this lot, item, customer and quantity.
   */
  const requestApprovalMutation = useMutation({
    mutationFn: (approvalReason: string) =>
      ApprovalsApi.submit({
        type: "RawIssue",
        reason: approvalReason,
        payloadJson: JSON.stringify({
          customerId, itemId, rawMessageId, orderDate,
          actualQuantityKg: actualKg ? Number(actualKg) : undefined,
          actualQuantityMeter: actualMeter ? Number(actualMeter) : undefined,
          color: color || undefined,
          customerReference: customerReference || undefined,
          notes: notes || undefined,
          priority,
          jobOrderType,
          formationRequestId: formationRequestId || null,
          formationBasinId: formationBasinId || null,
          requestId: requestId.current
        })
      }),
    onSuccess: (request) => {
      setNegativeStockDetail(null);
      setApprovalRequested(request.documentNumber);
      qc.invalidateQueries({ queryKey: ["approval-requests"] });
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "تعذر إرسال طلب الاعتماد")
  });

  const canSubmit =
    !!customerId && !!itemId && !!rawMessageId && (!!actualKg || !!actualMeter) && !issueMutation.isPending;

  return (
    <>
      <PageHeader
        title={t("nav.jobOrders")}
        subtitle={t("jo.subtitle")}
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ label: t("common.export"), action: ProductionOrdersExports.excel }}
              pdf={{ label: t("common.exportPdf"), action: ProductionOrdersExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>
              {showForm ? t("common.cancel") : t("jo.issueRaw")}
            </Button>
          </div>
        }
      />

      {created && (
        <Card className="card-pad mb-4 border-success/30 bg-success-soft/50">
          <span className="text-sm text-success-ink">{t("jo.issueCreated")}: </span>
          <Link to={`/production-orders/${created.id}`} className="font-bold ltr-nums text-brand-600 hover:underline">
            {created.orderNumber}
          </Link>
        </Card>
      )}

      <Card className="card-pad mb-4">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <div>
            <label className="field-label">{t("jo.list.quickSearch")}</label>
            <SearchInput value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("jo.list.searchPlaceholder")} />
          </div>
          <div>
            <label className="field-label">{t("jo.type")}</label>
            <Select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as JobOrderType | "")}>
              <option value="">{t("common.all")} - {t("jo.type")}</option>
              <option value="ClosedLine">{t("jo.type.ClosedLine")}</option>
              <option value="OpenLine">{t("jo.type.OpenLine")}</option>
            </Select>
          </div>
          <div>
            <label className="field-label">{t("jo.list.statusFilter")}</label>
            <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
              <option value="">{t("jo.list.allStatuses")}</option>
              <option value="Draft">مسودة</option>
              <option value="RawAllocated">تم صرف الخام</option>
              <option value="InProduction">تحت التشغيل</option>
              <option value="Paused">متوقف مؤقتًا</option>
              <option value="Completed">مكتمل</option>
              <option value="Cancelled">ملغي</option>
            </Select>
          </div>
        </div>
      </Card>

      {showForm && (
        <Card className="card-pad mb-6">
          <h3 className="card-title">{t("jo.issueRaw")}</h3>
          <p className="text-2xs text-ink-muted mt-1">{t("jo.issueHint")}</p>
          <form
            className="space-y-4 mt-3"
            onSubmit={(e) => {
              e.preventDefault();
              setApprovalRequested(null);
              issueMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="field-label">العميل</label>
                <Select
                  value={customerId}
                  onChange={(e) => {
                    setCustomerId(e.target.value);
                    setRawMessageId(""); setFormationRequestId(""); setFormationBasinId("");
                  }}
                  required
                >
                  <option value="">اختر العميل...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">الصنف</label>
                <Select
                  value={itemId}
                  onChange={(e) => {
                    setItemId(e.target.value);
                    setRawMessageId(""); setFormationRequestId(""); setFormationBasinId("");
                  }}
                  required
                >
                  <option value="">اختر الصنف...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name} ({i.baseUnit})</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">الرسالة / المصدر (الرصيد المتاح)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!customerId}>
                  <option value="">اختر رصيد الرسالة...</option>
                  {lots?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.messageNumber} · {m.warehouseName}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="field-label">
                  الكمية الفعلية المنصرفة ({unitLabel(selectedItem?.baseUnit)})
                </label>
                <Input type="number" step="0.001" min="0" value={actualKg} onChange={(e) => setActualKg(e.target.value)} />
              </div>
              <div>
                <label className="field-label">
                  الكمية الفعلية المنصرفة (وحدة ثانية)
                </label>
                <Input
                  type="number" step="0.001" min="0" value={actualMeter}
                  onChange={(e) => setActualMeter(e.target.value)}
                  disabled={!selectedItem?.secondaryUnit}
                  placeholder={selectedItem?.secondaryUnit ? "" : "لا توجد وحدة ثانية"}
                />
              </div>
              <div>
                <label className="field-label">اللون</label>
                <Input value={color} onChange={(e) => setColor(e.target.value)} />
              </div>
              <div>
                <label className="field-label">طلب التشكيل (اختياري)</label>
                <Select
                  value={formationRequestId}
                  onChange={(e) => { setFormationRequestId(e.target.value); setFormationBasinId(""); }}
                  disabled={!itemId}
                >
                  <option value="">إذن مباشر بدون طلب تشكيل</option>
                  {formationRequests?.map((r) => (
                    <option key={r.id} value={r.id}>{r.requestNumber} ({r.status})</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="field-label">الحوض (اختياري)</label>
                <Select
                  value={formationBasinId}
                  onChange={(e) => setFormationBasinId(e.target.value)}
                  disabled={!selectedRequest}
                >
                  <option value="">بدون حوض</option>
                  {selectedRequest?.groups.flatMap((g) =>
                    g.basins.map((b) => (
                      <option key={b.id} value={b.id}>
                        {g.name ?? `مجموعة ${g.groupNumber}`} · {b.name ?? `حوض ${b.basinNumber}`} · مخطط {b.plannedQuantity} {b.unit === "Meter" ? "م" : "كجم"}
                      </option>
                    ))
                  )}
                </Select>
              </div>
              <div>
                <label className="field-label">{t("jo.type")}</label>
                <Select value={jobOrderType} onChange={(e) => setJobOrderType(e.target.value as JobOrderType)}>
                  <option value="ClosedLine">{t("jo.type.ClosedLine")}</option>
                  <option value="OpenLine">{t("jo.type.OpenLine")}</option>
                </Select>
              </div>
              <div>
                <label className="field-label">الأولوية</label>
                <Select value={priority} onChange={(e) => setPriority(e.target.value as ProductionPriority)}>
                  <option value="Low">منخفضة</option>
                  <option value="Normal">عادية</option>
                  <option value="High">عالية</option>
                  <option value="Urgent">عاجلة</option>
                </Select>
              </div>
              <div>
                <label className="field-label">تاريخ الأمر</label>
                <Input type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} required />
              </div>
              <div>
                <label className="field-label">مرجع العميل</label>
                <Input value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} />
              </div>
            </div>
            <div>
              <label className="field-label">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <div className="flex items-center gap-3">
              <Button type="submit" disabled={!canSubmit}>
                {issueMutation.isPending ? "جارٍ التنفيذ..." : t("jo.issueConfirm")}
              </Button>
              <span className="text-2xs text-ink-subtle">{t("jo.issueActualHint")}</span>
            </div>
            {error && <p className="form-error">{error}</p>}

            {negativeStockDetail && (
              <Card className="card-pad border-warning/30 bg-warning-soft">
                <p className="text-sm text-warning-ink">
                  الرصيد المتاح من هذه الرسالة لا يكفي، والنقص بمقدار{" "}
                  <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
                  لا يتم تجاوز الرصيد السالب من نفس شاشة الصرف: يُرسل الطلب إلى مركز الاعتمادات، ولا ينفّذه
                  إلا مستخدم آخر يملك صلاحية اعتماد تجاوز الرصيد السالب.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب طلب الاعتماد..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary"
                    disabled={!negativeStockDetail.reason || requestApprovalMutation.isPending}
                    onClick={() => { setError(null); requestApprovalMutation.mutate(negativeStockDetail.reason); }}>
                    {requestApprovalMutation.isPending ? "جارٍ الإرسال..." : "طلب اعتماد"}
                  </Button>
                </div>
                <p className="mt-2 text-2xs text-warning-ink">
                  الاعتماد مرتبط بنفس الرسالة والصنف والعميل والكمية والوحدة وطلب التشكيل - أي تغيير يتطلب طلبًا جديدًا.
                </p>
              </Card>
            )}

            {approvalRequested && (
              <p className="text-sm text-success-ink">
                تم إرسال طلب اعتماد على الرسالة <span className="ltr-nums font-bold">{approvalRequested}</span>.
                لن يُخصم المخزون ولن يُنشأ أمر التشغيل إلا بعد موافقة مستخدم آخر من مركز الاعتمادات.
              </p>
            )}
          </form>
        </Card>
      )}

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-3">
          <h2 className="card-title">{t("jo.list.registerTitle")}</h2>
          <span className="text-xs text-ink-muted">{t("jo.list.results")}: <b className="ltr-nums">{visibleOrders.length}</b></span>
        </div>
        <div className="table-wrap">
          <table className="table table-dense">
            <thead><tr>
              <th>{t("jo.list.orderDate")}</th><th>{t("jo.list.customer")}</th><th>{t("jo.list.itemColor")}</th>
              <th>{t("jo.list.requestedQuantity")}</th><th>{t("jo.list.recordedOutput")}</th><th>{t("jo.list.typePriority")}</th>
              <th>{t("jo.list.currentStage")}</th><th>{t("jo.list.stageProgress")}</th><th>{t("jo.list.status")}</th><th>{t("jo.list.actions")}</th>
            </tr></thead>
            <tbody>
              {isLoading && <tr><td colSpan={10} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
              {!isLoading && visibleOrders.length === 0 && <tr><td colSpan={10} className="px-4 py-6 text-center text-ink-subtle">{t("jo.list.noMatchingOrders")}</td></tr>}
              {visibleOrders.map((o) => {
                const completedStages = o.stageExecutions.filter((stage) => stage.status === "Completed").length;
                const currentStage = o.stageExecutions.find((stage) => stage.status !== "Completed");
                const lastCompletedStage = [...o.stageExecutions]
                  .filter((stage) => stage.status === "Completed")
                  .sort((a, b) => b.sequence - a.sequence)[0];
                const formatQty = (value: number | null | undefined, unit: string) =>
                  value == null ? "—" : `${value.toLocaleString("en-US", { maximumFractionDigits: 3 })} ${unit}`;
                return (
                  <tr key={o.id}>
                    <td>
                      <Link to={`/production-orders/${o.id}`} className="font-semibold ltr-nums text-brand-700 hover:underline">{o.orderNumber}</Link>
                      <div className="text-xs text-ink-subtle ltr-nums">{new Date(o.orderDate).toLocaleDateString()}</div>
                    </td>
                    <td><div className="font-medium">{o.customerName}</div><div className="text-xs text-ink-subtle ltr-nums">{o.customerCode}</div></td>
                    <td><div className="font-medium">{o.itemName}</div><div className="text-xs text-ink-subtle">{o.itemCode}{o.color ? ` · ${o.color}` : ""}</div></td>
                    <td><div className="ltr-nums">{formatQty(o.requestedQuantityKg, t("common.kg"))}</div><div className="ltr-nums text-xs text-ink-muted">{formatQty(o.requestedQuantityMeter, t("common.meter"))}</div></td>
                    <td><div className="ltr-nums">{formatQty(lastCompletedStage?.outputKg, t("common.kg"))}</div><div className="ltr-nums text-xs text-ink-muted">{formatQty(lastCompletedStage?.outputMeter, t("common.meter"))}</div></td>
                    <td><div><Badge tone={o.jobOrderType === "OpenLine" ? "yellow" : "gray"}>{t(`jo.type.${o.jobOrderType}`)}</Badge></div><div className="mt-1 text-xs text-ink-muted">{priorityLabel[o.priority]}</div></td>
                    <td>{currentStage?.stageName ?? (o.status === "Completed" ? t("prod.status.Completed") : "—")}</td>
                    <td className="ltr-nums">{completedStages} / {o.stageExecutions.length}</td>
                    <td><Badge tone={statusTone[o.status]}>{t(`prod.status.${o.status}`)}</Badge></td>
                    <td><DocumentActions pdf={ProductionOrdersExports.documentPdf(o.id)} printTo={`/print/production-order/${o.id}`} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </Card>
    </>
  );
}
