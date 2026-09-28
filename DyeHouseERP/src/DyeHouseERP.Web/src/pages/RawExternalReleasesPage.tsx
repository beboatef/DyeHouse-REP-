import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomersApi, ItemsApi, RawExternalReleasesApi, RawMessagesApi, RawReleaseReason,
  type ExternalProcessingStatus, type RawExternalRelease
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ExternalReleasesExports } from "@/api/exports";

const reasonLabel: Record<RawReleaseReason, string> = {
  ReturnToCustomer: "إرجاع للعميل",
  ExternalProcessing: "تشغيل خارجي",
  Sale: "بيع خام"
};
const reasonTone: Record<RawReleaseReason, "blue" | "yellow" | "green"> = {
  ReturnToCustomer: "blue", ExternalProcessing: "yellow", Sale: "green"
};

const statusLabel: Record<ExternalProcessingStatus, string> = {
  NotApplicable: "—",
  AwaitingReturn: "بانتظار العودة",
  PartiallyReturned: "عودة جزئية",
  Returned: "تمت العودة",
  Cancelled: "ملغاة"
};
const statusTone: Record<ExternalProcessingStatus, "gray" | "green" | "yellow" | "red" | "blue"> = {
  NotApplicable: "gray", AwaitingReturn: "yellow", PartiallyReturned: "blue",
  Returned: "green", Cancelled: "red"
};

const today = () => new Date().toISOString().slice(0, 10);

/** External-processing return / details / cancellation (spec section 21). */
function ExternalProcessingActions({ release }: { release: RawExternalRelease }) {
  const qc = useQueryClient();
  const [panel, setPanel] = useState<"return" | "details" | "cancel" | null>(null);

  const [retKg, setRetKg] = useState("");
  const [retMeter, setRetMeter] = useState("");
  const [retDate, setRetDate] = useState(today());
  const [retNotes, setRetNotes] = useState("");

  const [party, setParty] = useState(release.externalParty ?? "");
  const [stage, setStage] = useState(release.externalProcessingStage ?? "");
  const [cost, setCost] = useState(release.externalProcessingCost?.toString() ?? "");
  const [expected, setExpected] = useState(release.expectedReturnDate?.slice(0, 10) ?? "");

  const [cancelReason, setCancelReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const reset = () => { setPanel(null); setError(null); };
  const invalidate = () => qc.invalidateQueries({ queryKey: ["raw-external-releases"] });

  const returnMutation = useMutation({
    mutationFn: () => RawExternalReleasesApi.recordReturn(release.id, {
      returnedQuantityKg: retKg ? Number(retKg) : undefined,
      returnedQuantityMeter: retMeter ? Number(retMeter) : undefined,
      actualReturnDate: retDate,
      notes: retNotes || undefined
    }),
    onSuccess: () => { invalidate(); reset(); setRetKg(""); setRetMeter(""); setRetNotes(""); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذر تسجيل العودة")
  });

  const detailsMutation = useMutation({
    mutationFn: () => RawExternalReleasesApi.setExternalProcessing(release.id, {
      externalParty: party || undefined,
      stage: stage || undefined,
      cost: cost ? Number(cost) : undefined,
      expectedReturnDate: expected ? new Date(expected).toISOString() : undefined
    }),
    onSuccess: () => { invalidate(); reset(); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذر تحديث بيانات التشغيل الخارجي")
  });

  const cancelMutation = useMutation({
    mutationFn: () => RawExternalReleasesApi.cancel(release.id, cancelReason),
    onSuccess: () => { invalidate(); reset(); setCancelReason(""); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذر الإلغاء")
  });

  if (release.reason !== "ExternalProcessing") return <span className="text-gray-300">—</span>;

  const closed = release.status === "Returned" || release.status === "Cancelled";

  return (
    <div className="flex flex-wrap gap-2">
      {!closed && (
        <>
          <Button variant="secondary" onClick={() => setPanel(panel === "return" ? null : "return")}>تسجيل العودة</Button>
          <Button variant="secondary" onClick={() => setPanel(panel === "details" ? null : "details")}>بيانات التشغيل</Button>
          <Button variant="secondary" onClick={() => setPanel(panel === "cancel" ? null : "cancel")}>إلغاء</Button>
        </>
      )}

      {panel === "return" && (
        <Card className="p-3 mt-2 w-full border-gray-200">
          <p className="text-xs text-gray-500 mb-2">
            العودة تُسجَّل كحركة إدخال في سجل المخزون مقابل نفس الرسالة والعميل، فلا يتضاعف الرصيد.
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
            <Input type="number" step="0.001" min="0" placeholder="الكمية كجم" value={retKg} onChange={(e) => setRetKg(e.target.value)} />
            <Input type="number" step="0.001" min="0" placeholder="الكمية متر" value={retMeter} onChange={(e) => setRetMeter(e.target.value)} />
            <Input type="date" value={retDate} onChange={(e) => setRetDate(e.target.value)} />
          </div>
          <Input className="mt-2" placeholder="ملاحظات" value={retNotes} onChange={(e) => setRetNotes(e.target.value)} />
          <Button className="mt-2" disabled={(!retKg && !retMeter) || returnMutation.isPending} onClick={() => returnMutation.mutate()}>
            {returnMutation.isPending ? "جارٍ الحفظ..." : "تأكيد العودة"}
          </Button>
        </Card>
      )}

      {panel === "details" && (
        <Card className="p-3 mt-2 w-full border-gray-200">
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-2">
            <Input placeholder="الطرف الخارجي" value={party} onChange={(e) => setParty(e.target.value)} />
            <Input placeholder="المرحلة" value={stage} onChange={(e) => setStage(e.target.value)} />
            <Input type="number" step="0.01" min="0" placeholder="تكلفة التشغيل" value={cost} onChange={(e) => setCost(e.target.value)} />
            <Input type="date" value={expected} onChange={(e) => setExpected(e.target.value)} />
          </div>
          <Button className="mt-2" disabled={detailsMutation.isPending} onClick={() => detailsMutation.mutate()}>
            {detailsMutation.isPending ? "جارٍ الحفظ..." : "حفظ البيانات"}
          </Button>
        </Card>
      )}

      {panel === "cancel" && (
        <Card className="p-3 mt-2 w-full border-gray-200">
          <p className="text-xs text-gray-500 mb-2">الإلغاء لا يحذف الحركة، بل يوثّق سببها في السجل.</p>
          <div className="flex gap-2">
            <Input placeholder="سبب الإلغاء" value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
            <Button variant="secondary" disabled={!cancelReason || cancelMutation.isPending} onClick={() => cancelMutation.mutate()}>تأكيد الإلغاء</Button>
          </div>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 w-full">{error}</p>}
    </div>
  );
}

export default function RawExternalReleasesPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [rawMessageId, setRawMessageId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [reason, setReason] = useState<RawReleaseReason>("ReturnToCustomer");
  const [externalParty, setExternalParty] = useState("");
  const [stage, setStage] = useState("");
  const [cost, setCost] = useState("");
  const [expectedReturnDate, setExpectedReturnDate] = useState("");
  const [notes, setNotes] = useState("");
  const [statusFilter, setStatusFilter] = useState<ExternalProcessingStatus | "">("");
  const [error, setError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-release", customerId],
    queryFn: () => RawMessagesApi.list({ customerId, onlyWithBalance: true }),
    enabled: !!customerId
  });
  const { data: releases, isLoading } = useQuery({
    queryKey: ["raw-external-releases", statusFilter],
    queryFn: () => RawExternalReleasesApi.list(statusFilter ? { status: statusFilter } : undefined)
  });

  const createMutation = useMutation({
    mutationFn: (overrideNegativeStock: boolean) =>
      RawExternalReleasesApi.create({
        customerId, itemId, rawMessageId, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        externalParty: externalParty || undefined,
        externalProcessingStage: stage || undefined,
        externalProcessingCost: cost ? Number(cost) : undefined,
        expectedReturnDate: expectedReturnDate ? new Date(expectedReturnDate).toISOString() : undefined,
        notes: notes || undefined,
        overrideNegativeStock,
        overrideReason: negativeStockDetail?.reason
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["raw-external-releases"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setExternalParty(""); setStage(""); setCost("");
      setExpectedReturnDate(""); setNotes(""); setError(null); setNegativeStockDetail(null);
    },
    onError: (err: any) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") {
        setNegativeStockDetail({ shortage: err.response.data.shortage, reason: "" });
      } else {
        setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ");
      }
    }
  });

  const isExternal = reason === "ExternalProcessing";

  return (
    <>
      <PageHeader
        title="إفراج الخام الخارجي"
        subtitle="إرجاع للعميل، تشغيل خارجي، أو بيع خام - كل حركة تُسحب من رسالة محددة يختارها المستخدم"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: ExternalReleasesExports.excel }}
              pdf={{ action: ExternalReleasesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ حركة جديدة"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate(false);
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر العميل...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر الصنف...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">السبب</label>
                <Select value={reason} onChange={(e) => setReason(e.target.value as RawReleaseReason)}>
                  <option value="ReturnToCustomer">إرجاع للعميل</option>
                  <option value="ExternalProcessing">تشغيل خارجي</option>
                  <option value="Sale">بيع خام</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="block text-xs font-medium text-gray-600 mb-1">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!customerId}>
                  <option value="">اختر الرسالة...</option>
                  {messages?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الطرف الخارجي (اختياري)</label>
                <Input value={externalParty} onChange={(e) => setExternalParty(e.target.value)} placeholder="مصنع التشغيل الخارجي / المشتري" />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">الكمية (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
              </div>
              {isExternal && (
                <>
                  <div>
                    <label className="block text-xs font-medium text-gray-600 mb-1">مرحلة التشغيل الخارجي</label>
                    <Input value={stage} onChange={(e) => setStage(e.target.value)} placeholder="مثال: صباغة خارجية" />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-600 mb-1">تكلفة التشغيل المتوقعة</label>
                    <Input type="number" step="0.01" min="0" value={cost} onChange={(e) => setCost(e.target.value)} />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-600 mb-1">تاريخ العودة المتوقع</label>
                    <Input type="date" value={expectedReturnDate} onChange={(e) => setExpectedReturnDate(e.target.value)} />
                  </div>
                </>
              )}
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
            {error && <p className="text-sm text-red-600">{error}</p>}

            {negativeStockDetail && (
              <Card className="p-4 border-yellow-300 bg-yellow-50">
                <p className="text-sm text-yellow-800">
                  الكمية تتجاوز الرصيد المتاح بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
                  يتطلب صلاحية تجاوز الرصيد السالب وسببًا موثقًا.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب التجاوز..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary" disabled={!negativeStockDetail.reason} onClick={() => createMutation.mutate(true)}>
                    تجاوز واعتماد
                  </Button>
                </div>
              </Card>
            )}
          </form>
        </Card>
      )}

      <Card>
        <div className="flex items-center gap-2 p-3 border-b border-gray-100">
          <span className="text-xs text-gray-500">حالة التشغيل الخارجي:</span>
          <Select className="w-48" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as ExternalProcessingStatus | "")}>
            <option value="">الكل</option>
            <option value="AwaitingReturn">بانتظار العودة</option>
            <option value="PartiallyReturned">عودة جزئية</option>
            <option value="Returned">تمت العودة</option>
            <option value="Cancelled">ملغاة</option>
          </Select>
        </div>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">رقم الحركة</th>
              <th className="text-start px-4 py-3 font-medium">العميل</th>
              <th className="text-start px-4 py-3 font-medium">الصنف</th>
              <th className="text-start px-4 py-3 font-medium">الرسالة المصدر</th>
              <th className="text-start px-4 py-3 font-medium">الكمية</th>
              <th className="text-start px-4 py-3 font-medium">السبب</th>
              <th className="text-start px-4 py-3 font-medium">الحالة</th>
              <th className="text-start px-4 py-3 font-medium">إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && releases?.length === 0 && <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-400">لا توجد حركات بعد</td></tr>}
            {releases?.map((r) => (
              <tr key={r.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50 align-top">
                <td className="px-4 py-3 font-medium ltr-nums">{r.releaseNumber}</td>
                <td className="px-4 py-3">{r.customerCode} - {r.customerName}</td>
                <td className="px-4 py-3">{r.itemCode} - {r.itemName}</td>
                <td className="px-4 py-3 ltr-nums">{r.messageNumber}</td>
                <td className="px-4 py-3 ltr-nums">
                  {r.quantityKg != null && `${r.quantityKg} كجم `}
                  {r.quantityMeter != null && `${r.quantityMeter} م`}
                  {r.returnedQuantityKg != null && (
                    <div className="text-xs text-green-700">عائد: {r.returnedQuantityKg} كجم</div>
                  )}
                  {r.returnedQuantityMeter != null && (
                    <div className="text-xs text-green-700">عائد: {r.returnedQuantityMeter} م</div>
                  )}
                </td>
                <td className="px-4 py-3"><Badge tone={reasonTone[r.reason]}>{reasonLabel[r.reason]}</Badge></td>
                <td className="px-4 py-3"><Badge tone={statusTone[r.status]}>{statusLabel[r.status]}</Badge></td>
                <td className="px-4 py-3">
                  <ExternalProcessingActions release={r} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
